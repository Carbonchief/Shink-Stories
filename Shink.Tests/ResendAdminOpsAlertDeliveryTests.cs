using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class ResendAdminOpsAlertDeliveryTests
{
    [TestMethod]
    public async Task Alert_AlreadyDelivered_IsSuppressedAcrossServiceInstancesAndChangedTimestamps()
    {
        var handler = new DeliveryHandler();
        await CreateService(handler).SendAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow.AddDays(-3)));
        await CreateService(handler).SendAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow));

        Assert.HasCount(1, handler.ProviderPayloads);
        Assert.IsTrue(handler.Completed);
        Assert.HasCount(1, handler.CompletionQueries);
        StringAssert.Contains(handler.CompletionQueries.Single(), "lease_token=eq.");
        StringAssert.Contains(handler.CompletionQueries.Single(), "sent_at=is.null");
    }

    [TestMethod]
    public async Task Alert_AmbiguousProviderFailure_RetainsOriginalPayloadAndIdempotencyKeyOnRetry()
    {
        var handler = new DeliveryHandler { FailFirstSend = true };
        await CreateService(handler).SendAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow.AddHours(-1)));
        handler.ExpireLease();
        await CreateService(handler).SendAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow));

        Assert.HasCount(2, handler.ProviderPayloads);
        Assert.AreEqual(handler.ProviderPayloads[0], handler.ProviderPayloads[1], "The incident timestamp must not change between retries.");
        Assert.AreEqual(handler.ProviderKeys[0], handler.ProviderKeys[1]);
        Assert.IsTrue(handler.Completed);
    }

    [TestMethod]
    public async Task Alert_ClaimUnavailable_DoesNotSendWithoutDuplicateProtection()
    {
        var handler = new DeliveryHandler { StorageUnavailable = true };
        await CreateService(handler).SendAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow));

        Assert.IsEmpty(handler.ProviderPayloads);
    }

    [TestMethod]
    public async Task Alert_LeaseNotGranted_DoesNotSend()
    {
        var handler = new DeliveryHandler { LeaseUnavailable = true };
        await CreateService(handler).SendAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow));

        Assert.IsEmpty(handler.ProviderPayloads);
    }

    [TestMethod]
    public async Task QueuedAlert_SendFails_DispatcherRetriesAfterRestartWithoutAnotherIncident()
    {
        var handler = new DeliveryHandler { FailFirstSend = true };
        Assert.IsTrue(await CreateService(handler).QueueAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow)));
        Assert.IsEmpty(handler.ProviderPayloads, "Queuing must be independent of provider delivery.");
        await CreateService(handler).ProcessPendingAdminOpsAlertsAsync();
        Assert.IsFalse(handler.Completed);
        handler.ExpireLease();

        await CreateService(handler).ProcessPendingAdminOpsAlertsAsync();
        await CreateService(handler).ProcessPendingAdminOpsAlertsAsync();

        Assert.IsTrue(handler.Completed);
        Assert.HasCount(2, handler.ProviderPayloads);
        Assert.AreEqual(handler.ProviderPayloads[0], handler.ProviderPayloads[1]);
        Assert.AreEqual(handler.ProviderKeys[0], handler.ProviderKeys[1]);
        Assert.AreEqual(1, handler.QueuedRequests);
    }

    [TestMethod]
    public async Task QueuedAlert_DuplicateBeforeDispatch_PreservesFirstPayloadAndSendsOnce()
    {
        var handler = new DeliveryHandler();
        Assert.IsTrue(await CreateService(handler).QueueAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow.AddHours(-1))));
        var firstPayload = handler.QueuedPayload;
        Assert.IsTrue(await CreateService(handler).QueueAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow)));
        await CreateService(handler).ProcessPendingAdminOpsAlertsAsync();

        Assert.AreEqual(firstPayload, handler.ProviderPayloads.Single());
        Assert.HasCount(1, handler.ProviderKeys);
        Assert.IsTrue(handler.Completed);
    }

    [TestMethod]
    public async Task QueuedAlert_StorageUnavailable_ReturnsFailureWithoutSending()
    {
        var handler = new DeliveryHandler { StorageUnavailable = true };
        Assert.IsFalse(await CreateService(handler).QueueAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow)));
        Assert.IsEmpty(handler.ProviderPayloads);
    }

    [TestMethod]
    public async Task Alert_AcceptedButCompletionWriteFails_DispatcherRetriesSameProviderKey()
    {
        var handler = new DeliveryHandler { FailFirstCompletion = true };
        await CreateService(handler).SendAdminOpsAlertAsync(Alert(DateTimeOffset.UtcNow));
        Assert.IsFalse(handler.Completed);
        handler.ExpireLease();
        await CreateService(handler).ProcessPendingAdminOpsAlertsAsync();

        Assert.IsTrue(handler.Completed);
        Assert.HasCount(2, handler.ProviderPayloads);
        Assert.AreEqual(handler.ProviderKeys[0], handler.ProviderKeys[1]);
        Assert.AreEqual(handler.ProviderPayloads[0], handler.ProviderPayloads[1]);
    }

    private static AdminOpsAlertEmailRequest Alert(DateTimeOffset occurredAt) =>
        new("paystack-upsert-failed/reference/transaction/charge.success", "error", "Paystack subscription persist failed", "Missing plan", "Details", "reference", occurredAt);

    private static ResendSubscriptionNotificationEmailService CreateService(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new ResendOptions { ApiKey = "resend-test-key", FromEmail = "hello@example.com", ToEmail = "ops@example.com" }),
        NullLogger<ResendSubscriptionNotificationEmailService>.Instance,
        Options.Create(new SupabaseOptions { Url = "https://example.supabase.co", SecretKey = "supabase-test-key" }));

    private sealed class DeliveryHandler : HttpMessageHandler
    {
        public List<string> ProviderPayloads { get; } = [];
        public List<string> ProviderKeys { get; } = [];
        public List<string> CompletionQueries { get; } = [];
        public bool Completed { get; private set; }
        public bool FailFirstSend { get; init; }
        public bool StorageUnavailable { get; init; }
        public bool LeaseUnavailable { get; init; }
        public bool FailFirstCompletion { get; init; }
        public int QueuedRequests { get; private set; }
        public string? QueuedPayload => _firstPayload?.GetRawText();
        private bool _leaseActive;
        private JsonElement? _firstPayload;
        private string? _key;
        public void ExpireLease() => _leaseActive = false;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "api.resend.com")
            {
                Assert.AreEqual("resend-test-key", request.Headers.Authorization?.Parameter);
                Assert.IsFalse(request.Headers.Contains("apikey"));
                ProviderPayloads.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                ProviderKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
                if (FailFirstSend && ProviderPayloads.Count == 1) throw new HttpRequestException("Simulated ambiguous timeout");
                return Json("""{"id":"email-id"}""");
            }

            Assert.AreEqual("supabase-test-key", request.Headers.Authorization?.Parameter);
            if (request.RequestUri.AbsolutePath == "/rest/v1/admin_ops_alert_deliveries" && request.Method == HttpMethod.Post)
            {
                if (StorageUnavailable) return new(HttpStatusCode.ServiceUnavailable);
                QueuedRequests++;
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                _firstPayload ??= doc.RootElement.GetProperty("request_payload").Clone();
                _key ??= doc.RootElement.GetProperty("alert_key").GetString();
                return new(HttpStatusCode.Created);
            }
            if (request.RequestUri.AbsolutePath == "/rest/v1/rpc/claim_pending_admin_ops_alert")
            {
                if (Completed || LeaseUnavailable || _leaseActive || _firstPayload is null) return Json("[]");
                _leaseActive = true;
                return Claim();
            }
            if (request.RequestUri.AbsolutePath == "/rest/v1/rpc/claim_admin_ops_alert")
            {
                if (StorageUnavailable) return new(HttpStatusCode.NotFound);
                if (Completed || LeaseUnavailable || _leaseActive) return Json("[]");
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                _firstPayload ??= doc.RootElement.GetProperty("p_request_payload").Clone();
                _key ??= doc.RootElement.GetProperty("p_alert_key").GetString();
                _leaseActive = true;
                return Claim();
            }
            if (request.RequestUri.AbsolutePath == "/rest/v1/admin_ops_alert_deliveries")
            {
                CompletionQueries.Add(request.RequestUri.Query);
                if (FailFirstCompletion && CompletionQueries.Count == 1) return new(HttpStatusCode.ServiceUnavailable);
                Completed = true;
                return new(HttpStatusCode.NoContent);
            }
            Assert.Fail("Unexpected request: " + request.RequestUri.AbsolutePath);
            return new(HttpStatusCode.NotFound);
        }

        private HttpResponseMessage Claim() => Json(JsonSerializer.Serialize(new[] { new {
            alert_key = _key, request_payload = _firstPayload, lease_token = "11111111-1111-1111-1111-111111111111"
        } }));

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
