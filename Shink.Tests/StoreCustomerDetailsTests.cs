using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Shink.Components.Store;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class StoreCustomerDetailsTests
{
    private const string Owner = "owner@example.invalid";
    private static ClaimsPrincipal User(string? email = Owner) => new(new ClaimsIdentity(
        email is null ? [] : [new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.Name, "Cookie name")],
        email is null ? null : "test"));
    private static readonly StoreCustomerDetails Saved = new("Gift recipient", "contact@example.invalid", "0821234567",
        "PUDO Test", "Unit 2", "Test suburb", "Pretoria", "0002");
    private static SubscriberProfile Profile(string email = Owner) => new(email, "Profile", "Name", "Display", "0831234567", null, null, null);
    private static StoreOrderDraft Order() => new("W-TEST", "tiekie", "Tiekie", 1, 250m,
        [new("tiekie", "Tiekie", 1, 250m)], false, Saved.Name!, Saved.Email!, Saved.Phone!,
        Saved.AddressLine1!, Saved.AddressLine2, Saved.Suburb, Saved.City!, Saved.PostalCode!, "Do not save order notes");
    private static FormCollection Form(bool save = true, string expected = Owner) => new(new Dictionary<string, StringValues>
    {
        ["saveDetails"] = save ? "ja" : "", ["expectedAccountEmail"] = expected
    });

    [TestMethod]
    public void Prefill_Guest_DoesNotExposeProfileOrSavedDetails()
    {
        Assert.AreEqual(new StoreCustomerDetails(), StoreCustomerDetails.Prefill(User(null), Profile(), Saved));
    }

    [TestMethod]
    public void Prefill_Profile_UsesFullNameEmailAndPhone()
    {
        var details = StoreCustomerDetails.Prefill(User(), Profile(), null);
        Assert.AreEqual("Profile Name", details.Name);
        Assert.AreEqual(Owner, details.Email);
        Assert.AreEqual("0831234567", details.Phone);
        Assert.IsNull(details.AddressLine1);
    }

    [TestMethod]
    public void Prefill_SavedCheckout_OverridesProfileWithoutChangingAccountIdentity()
    {
        Assert.AreEqual(Saved, StoreCustomerDetails.Prefill(User(), Profile(), Saved));
        Assert.AreEqual(Owner, StoreCustomerDetails.AccountEmail(User()));
    }

    [TestMethod]
    public void Prefill_ForeignProfile_IsIgnored()
    {
        var details = StoreCustomerDetails.Prefill(User(), Profile("another@example.invalid"), null);
        Assert.AreEqual("Cookie name", details.Name);
        Assert.AreEqual(Owner, details.Email);
        Assert.IsNull(details.Phone);
    }

    [TestMethod]
    public async Task Guest_CannotReadOrWriteSavedDetails()
    {
        using var handler = new RecordingHandler();
        var service = Service(handler);
        Assert.IsNull(await service.GetAsync(User(null)));
        Assert.IsFalse(await service.SaveAsync(User(null), Saved));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task Get_UsesOnlyAuthenticatedEmailAndMapsSavedDetails()
    {
        using var handler = new RecordingHandler(JsonSerializer.Serialize(new[] { Saved }));
        var result = await Service(handler).GetAsync(User(" Account+one@Example.invalid "));
        Assert.AreEqual(Saved, result);
        Assert.HasCount(1, handler.Requests);
        StringAssert.Contains(handler.Requests[0].Uri, "account_email=eq.account%2Bone%40example.invalid");
    }

    [TestMethod]
    public async Task Save_UsesPrincipalAsOwnerAndNeverPersistsNotesOrAuthChanges()
    {
        using var handler = new RecordingHandler();
        Assert.IsTrue(await Service(handler).SaveAsync(User(), StoreCustomerDetails.FromOrder(Order())));
        Assert.HasCount(1, handler.Requests);
        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.AreEqual(Owner, body.RootElement.GetProperty("account_email").GetString());
        Assert.AreEqual(Saved.Email, body.RootElement.GetProperty("customer_email").GetString());
        Assert.AreEqual(Saved.AddressLine1, body.RootElement.GetProperty("delivery_address_line_1").GetString());
        Assert.IsFalse(body.RootElement.TryGetProperty("notes", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("password", out _));
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        StringAssert.Contains(handler.Requests[0].Uri, "on_conflict=account_email");
    }

    [TestMethod]
    public async Task UnavailableStorage_PrefillFallsBackAndSaveReportsFailure()
    {
        using var handler = new RecordingHandler(status: HttpStatusCode.ServiceUnavailable);
        var service = Service(handler);
        var saved = await service.GetAsync(User());
        Assert.IsNull(saved);
        Assert.AreEqual("Profile Name", StoreCustomerDetails.Prefill(User(), Profile(), saved).Name);
        var error = await StoreCheckoutPreferences.SaveIfRequestedAsync(User(), Form(), Order(), service);
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "ontmerk");
    }

    [TestMethod]
    public async Task UncheckedSave_DoesNotWriteOrDeleteExistingPreferences()
    {
        using var handler = new RecordingHandler();
        Assert.IsNull(await StoreCheckoutPreferences.SaveIfRequestedAsync(User(), Form(false), Order(), Service(handler)));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    [DataRow(null, Owner)]
    [DataRow("another@example.invalid", Owner)]
    public async Task Save_StaleOrGuestAccount_IsRejectedBeforeWriting(string? account, string expected)
    {
        using var handler = new RecordingHandler();
        Assert.IsNotNull(await StoreCheckoutPreferences.SaveIfRequestedAsync(User(account), Form(expected: expected), Order(), Service(handler)));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task CheckedSave_PersistsExactlyOnce()
    {
        using var handler = new RecordingHandler();
        Assert.IsNull(await StoreCheckoutPreferences.SaveIfRequestedAsync(User(), Form(), Order(), Service(handler)));
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task CheckoutHtml_SignedIn_PrefillsFieldsAndChecksSaveButNotTerms()
    {
        using var document = new HtmlParser().ParseDocument(await RenderAsync(Owner, Saved));
        var expected = new Dictionary<string, string?> { ["naam"] = Saved.Name, ["epos"] = Saved.Email,
            ["selfoon"] = Saved.Phone, ["adresLyn1"] = Saved.AddressLine1, ["adresLyn2"] = Saved.AddressLine2,
            ["voorstad"] = Saved.Suburb, ["stad"] = Saved.City, ["poskode"] = Saved.PostalCode };
        foreach (var (name, value) in expected) Assert.AreEqual(value, document.QuerySelector($"input[name={name}]")?.GetAttribute("value"));
        Assert.IsTrue(document.QuerySelector("input[name=saveDetails]")!.HasAttribute("checked"));
        Assert.IsFalse(document.QuerySelector("input[name=terme]")!.HasAttribute("checked"));
        Assert.AreEqual(Owner, document.QuerySelector("input[name=expectedAccountEmail]")?.GetAttribute("value"));
        Assert.AreEqual(Owner, document.QuerySelector("#winkel-order-form")?.GetAttribute("data-store-customer-key"));
    }

    [TestMethod]
    public async Task CheckoutHtml_Guest_OffersSignInReturningToCheckout()
    {
        using var document = new HtmlParser().ParseDocument(await RenderAsync(null, new()));
        var signIn = document.QuerySelector(".winkel-checkout-signin a");
        Assert.AreEqual("/teken-in?returnUrl=%2Fwinkel%2Fafreken", signIn?.GetAttribute("href"));
        Assert.IsNull(document.QuerySelector("input[name=saveDetails]"));
    }

    private static SupabaseStoreCustomerDetailsService Service(RecordingHandler handler) => new(new HttpClient(handler),
        Options.Create(new SupabaseOptions { Url = "https://local-test.invalid", SecretKey = "local-test-only" }),
        NullLogger<SupabaseStoreCustomerDetailsService>.Instance);

    private static async Task<string> RenderAsync(string? account, StoreCustomerDetails details)
    {
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<AntiforgeryStateProvider, TestAntiforgeryState>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<StoreCheckout>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(StoreCheckout.Details)] = details, [nameof(StoreCheckout.AccountEmail)] = account
            }))).ToHtmlString());
    }

    private sealed class TestAntiforgeryState : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }

    private sealed class RecordingHandler(string body = "[]", HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
