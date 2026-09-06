using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MobileStoreDeliveryTests
{
    [TestMethod]
    [DataRow("SUBSCRIPTION_STATE_ACTIVE", true, false)]
    [DataRow("SUBSCRIPTION_STATE_IN_GRACE_PERIOD", true, false)]
    [DataRow("SUBSCRIPTION_STATE_CANCELED", true, false)]
    [DataRow("SUBSCRIPTION_STATE_EXPIRED", false, true)]
    [DataRow("SUBSCRIPTION_STATE_ON_HOLD", false, true)]
    [DataRow("SUBSCRIPTION_STATE_PENDING", false, false)]
    public async Task GoogleLifecycleStatesSeparateInactiveFromRetryable(string state, bool active, bool inactive)
    {
        using var fixture = new Fixture(state);
        var result = await fixture.Service.CheckAsync("google_play", Product, null, "purchase");
        Assert.AreEqual(active, result.Purchase is not null);
        Assert.AreEqual(inactive, result.IsInactive);
    }

    [TestMethod]
    public async Task ServerAcknowledgmentOccursOnlyAfterDurableAccess()
    {
        using var fixture = new Fixture("SUBSCRIPTION_STATE_ACTIVE");
        var response = await fixture.Service.VerifyAndRecordAsync("owner@example.com", Request);
        Assert.IsTrue(response.IsActive);
        CollectionAssert.AreEqual(new[] { "persist", "acknowledge" }, fixture.Events);
        fixture.Events.Clear();
        fixture.Ledger.Accept = false;
        response = await fixture.Service.VerifyAndRecordAsync("owner@example.com", Request);
        Assert.IsFalse(response.IsActive);
        Assert.IsTrue(response.IsRetryable);
        CollectionAssert.AreEqual(new[] { "persist" }, fixture.Events);
    }

    [TestMethod]
    public async Task NetworkFailureAndWrongAccountDoNotGrantOrRevokeAccess()
    {
        using var fixture = new Fixture("SUBSCRIPTION_STATE_ACTIVE");
        fixture.ProviderStatus = HttpStatusCode.ServiceUnavailable;
        var check = await fixture.Service.CheckAsync("google_play", Product, null, "purchase");
        Assert.IsNull(check.Purchase);
        Assert.IsFalse(check.IsInactive);
        fixture.ProviderStatus = HttpStatusCode.OK;
        fixture.AccountId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("other@example.com"))).ToLowerInvariant();
        var response = await fixture.Service.VerifyAndRecordAsync("owner@example.com", Request);
        Assert.IsFalse(response.IsActive);
        Assert.IsEmpty(fixture.Events);
    }

    [TestMethod]
    public async Task ReconciliationExtendsRenewalsAndAcknowledgesWithoutClient()
    {
        using var fixture = new Fixture("SUBSCRIPTION_STATE_ACTIVE");
        await fixture.ReconcileAsync();
        Assert.IsNotNull(fixture.Update);
        Assert.AreEqual("active", fixture.Update.Value.GetProperty("status").GetString());
        Assert.IsTrue(fixture.Update.Value.GetProperty("next_renewal_at").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        CollectionAssert.AreEqual(new[] { "reconcile-update", "acknowledge" }, fixture.Events);
    }

    [TestMethod]
    public async Task ReconciliationRevokesConfirmedExpiryButPreservesAccessOnOutage()
    {
        using var fixture = new Fixture("SUBSCRIPTION_STATE_EXPIRED");
        await fixture.ReconcileAsync();
        Assert.AreEqual("cancelled", fixture.Update!.Value.GetProperty("status").GetString());
        fixture.Update = null;
        fixture.ProviderStatus = HttpStatusCode.ServiceUnavailable;
        await fixture.ReconcileAsync();
        Assert.IsNull(fixture.Update);
    }

    private const string Product = "schink_stories_maandeliks";
    private static readonly MobileStorePurchaseRequest Request = new("google_play", Product, "purchase", null, "purchase");

    private sealed class Fixture : IDisposable
    {
        private readonly RSA _key = RSA.Create(2048);
        private readonly HttpClient _http;
        public List<string> Events { get; } = [];
        public LedgerProxy Ledger { get; }
        public MobileStoreEntitlementService Service { get; }
        public HttpStatusCode ProviderStatus { get; set; } = HttpStatusCode.OK;
        public string? AccountId { get; set; }
        public JsonElement? Update { get; set; }

        public Fixture(string state)
        {
            var ledger = DispatchProxy.Create<ISubscriptionLedgerService, LedgerProxy>();
            Ledger = (LedgerProxy)(object)ledger;
            Ledger.Events = Events;
            _http = new HttpClient(new Handler(request =>
            {
                if (request.RequestUri!.Host == "oauth2.googleapis.com") return Json(new {access_token="fake"});
                if (request.RequestUri.AbsolutePath.EndsWith(":acknowledge"))
                {
                    Events.Add("acknowledge");
                    return Json(new {});
                }
                if (request.RequestUri.Host == "example.invalid")
                {
                    if (request.Method == HttpMethod.Patch)
                    {
                        Events.Add("reconcile-update");
                        StringAssert.Contains(request.RequestUri.Query, "subscriber_id=eq.owner");
                        StringAssert.Contains(request.RequestUri.Query, "next_renewal_at=eq.");
                        Update = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.Clone();
                        return Json(new[] {new {subscription_id="row"}});
                    }
                    return Json(new[] {new {subscription_id="row",subscriber_id="owner",provider="google_play",provider_payment_id="purchase",provider_token="purchase",provider_transaction_id="old-order",tier_code="all_stories_monthly",next_renewal_at=DateTimeOffset.UtcNow.AddMinutes(-1),status="active"}});
                }
                return Json(new {subscriptionState=state,acknowledgementState="ACKNOWLEDGEMENT_STATE_PENDING",
                    externalAccountIdentifiers=new {obfuscatedExternalAccountId=AccountId},
                    startTime=DateTimeOffset.UtcNow.AddDays(-1),lineItems=new[]{new {productId=Product,expiryTime=DateTimeOffset.UtcNow.AddDays(30),latestSuccessfulOrderId="order"}}}, ProviderStatus);
            }));
            Service = new(_http, Options.Create(new MobileStoreOptions {
                GoogleServiceAccountJson=JsonSerializer.Serialize(new {client_email="fake@example.invalid",private_key=_key.ExportPkcs8PrivateKeyPem()})
            }), ledger, NullLogger<MobileStoreEntitlementService>.Instance);
        }

        public Task ReconcileAsync() => new StoreSubscriptionReconciliationService(_http,
            Options.Create(new SupabaseOptions {Url="https://example.invalid",SecretKey="fake"}), Service,
            NullLogger<StoreSubscriptionReconciliationService>.Instance).ReconcileAsync(CancellationToken.None);
        public void Dispose() { _http.Dispose(); _key.Dispose(); }
    }

    public class LedgerProxy : DispatchProxy
    {
        public bool Accept = true;
        public List<string> Events = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != "RecordVerifiedStoreSubscriptionAsync") throw new InvalidOperationException(method.Name);
            Events.Add("persist");
            return Task.FromResult(new SubscriptionPersistResult(Accept, SubscriptionId: Accept ? "row" : null));
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) {Content=new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8,"application/json")};
}
