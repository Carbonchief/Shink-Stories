using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    [DataRow("active", false)]
    [DataRow("failed", false)]
    [DataRow("failed", true)]
    public async Task CloseAccountAsync_StopsBillingEvenWhenAccessExpiredOrAccountAlreadyClosed(string status, bool alreadyClosed)
    {
        var fixture = new AccountClosureFixture { SubscriptionStatus = status, AccountClosed = alreadyClosed };

        var result = await CreateService(fixture.Handler).CloseAccountAsync("ouer@example.com");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.HasCount(1, fixture.Handler.PaystackDisablePayloads);
        Assert.HasCount(1, fixture.Handler.SubscriptionPatchPayloads);
        using var cancellation = JsonDocument.Parse(fixture.Handler.SubscriptionPatchPayloads.Single());
        Assert.AreEqual("cancelled", cancellation.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(alreadyClosed, fixture.Handler.SubscriberPatchPayload is null);
        Assert.IsTrue(fixture.ProviderStopped);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    [DataRow("subscriptions")]
    [DataRow("provider")]
    [DataRow("disable")]
    [DataRow("persistence")]
    public async Task CloseAccountAsync_CancellationCannotBeConfirmed_DoesNotDisableAccount(string failure)
    {
        var fixture = new AccountClosureFixture { Failure = failure };

        var result = await CreateService(fixture.Handler).CloseAccountAsync("ouer@example.com");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(fixture.Handler.SubscriberPatchPayload);
    }

    [TestMethod]
    public async Task CloseAccountAsync_ProviderStoppedBeforeLocalWriteFailed_RetryFinishesWithoutCancellingAgain()
    {
        var fixture = new AccountClosureFixture { Failure = "persistence" };
        var service = CreateService(fixture.Handler);

        var first = await service.CloseAccountAsync("ouer@example.com");
        fixture.Failure = null;
        var retry = await service.CloseAccountAsync("ouer@example.com");

        Assert.IsFalse(first.IsSuccess);
        Assert.IsTrue(retry.IsSuccess, retry.ErrorMessage);
        Assert.HasCount(1, fixture.Handler.PaystackDisablePayloads);
        Assert.IsNotNull(fixture.Handler.SubscriberPatchPayload);
    }

    [TestMethod]
    public async Task CloseAccountAsync_AuthorizationBilledSubscription_StopsLocalBillingWithoutCallingProvider()
    {
        var fixture = new AccountClosureFixture { AuthorizationBilling = true };

        var result = await CreateService(fixture.Handler).CloseAccountAsync("ouer@example.com");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsEmpty(fixture.Handler.PaystackDisablePayloads);
        Assert.IsEmpty(fixture.Handler.PaystackLookups);
        Assert.HasCount(1, fixture.Handler.SubscriptionPatchPayloads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CardUpdate_AccountClosed_DelayedVerificationOrRefundCannotChargeOrRestoreAccess(bool refund)
    {
        var fixture = new CardUpdateFixture { AccountClosed = true, VerificationReversed = refund };
        var payload = refund
            ? """{"event":"refund.processed","data":{"id":902,"transaction_reference":"update-card-transaction","status":"processed","amount":100,"currency":"ZAR"}}"""
            : fixture.Payload;

        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads);
        Assert.IsTrue(fixture.HasRecovery);
    }

    [TestMethod]
    public async Task CardUpdate_AccountStatusUnavailable_DoesNotChargeAndLeavesEventRetryable()
    {
        var fixture = new CardUpdateFixture { AccountLookupUnavailable = true };

        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ScheduledRecovery_AccountClosedOrUnavailable_DoesNotCollectPayment(bool accountLookupUnavailable)
    {
        var fixture = new CardUpdateFixture
        {
            ExposeScheduledRetry = true,
            RecoveryStatus = "pending",
            AccountClosed = true,
            AccountLookupUnavailable = accountLookupUnavailable
        };

        await CreateService(fixture.Handler).ProcessExpiredPaymentRecoveriesAsync();

        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads);
        Assert.AreEqual(accountLookupUnavailable, fixture.HasRecovery,
            "A closed account ends recovery; an unavailable account lookup leaves it pending without charging.");
    }

    [TestMethod]
    [DataRow("invoice.update")]
    [DataRow("subscription.create")]
    public async Task PaystackMetadata_StaleCardSnapshot_CannotOverwriteReplacementAuthorizationOrCancellation(string eventType)
    {
        const string subscriptionId = "22222222-2222-2222-2222-222222222222";
        string? updateQuery = null;
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscription_events")) return JsonResponse("[]");
            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
                return JsonResponse($$"""[{"subscription_id":"{{subscriptionId}}","subscriber_id":"11111111-1111-1111-1111-111111111111","tier_code":"all_stories_monthly","provider":"paystack","provider_payment_id":"SUB_metadata","provider_token":"AUTH_replacement","status":"active","billing_amount_zar":79,"billing_period_months":1}]""");
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
                return JsonResponse("""[{"subscriber_id":"11111111-1111-1111-1111-111111111111","email":"ouer@example.com"}]""");
            if (request.Method == HttpMethod.Patch && request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                updateQuery = request.RequestUri.Query;
                return JsonResponse($$"""[{"subscription_id":"{{subscriptionId}}"}]""");
            }
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
                return JsonResponse("""[{"event_id":"901","processing_status":"processing"}]""");
            return new(HttpStatusCode.NotFound);
        });

        var result = await CreateService(handler).RecordPaystackEventAsync($$"""
            {"event":"{{eventType}}","data":{"id":810,"reference":"invoice-replacement-card","status":"success","paid":true,"amount":7900,
            "paid_at":"2099-10-18T16:45:17Z","customer":{"email":"ouer@example.com"},"plan":{"plan_code":"PLN_allstories"},
            "subscription":{"subscription_code":"SUB_metadata","next_payment_date":"2099-11-18T16:31:00Z"},
            "authorization":{"authorization_code":"AUTH_expired","reusable":true,"exp_month":"08","exp_year":"2026"} } }
            """);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(subscriptionId, result.SubscriptionId);
        var metadataUpdate = Assert.ContainsSingle(handler.SubscriptionPatchPayloads.Where(payload =>
            payload.Contains("\"provider_payment_id\":\"SUB_metadata\"", StringComparison.Ordinal)));
        using var updated = JsonDocument.Parse(metadataUpdate);
        Assert.IsFalse(updated.RootElement.TryGetProperty("provider_token", out _), "Metadata must not write even a previously read authorization; a concurrent card update must survive.");
        Assert.IsFalse(updated.RootElement.TryGetProperty("authorization_reusable", out _));
        Assert.IsFalse(updated.RootElement.TryGetProperty("cancelled_at", out _), "A provider snapshot cannot undo a local cancellation.");
        Assert.Contains("subscriber_id=eq.11111111-1111-1111-1111-111111111111", updateQuery!);
        Assert.IsEmpty(handler.SubscriptionCreatePayloads);
    }

    private sealed class AccountClosureFixture
    {
        public string SubscriptionStatus { get; set; } = "failed";
        public bool AccountClosed { get; set; }
        public bool AuthorizationBilling { get; set; }
        public bool ProviderStopped { get; private set; }
        public string? Failure { get; set; }
        public RecordingHandler Handler { get; }

        public AccountClosureFixture() => Handler = new RecordingHandler(Respond);

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
                return JsonResponse($$"""[{"subscriber_id":"11111111-1111-1111-1111-111111111111","disabled_at":{{(AccountClosed ? "\"2026-10-30T10:00:00Z\"" : "null")}}}]""");
            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                Assert.Contains("provider=eq.paystack", request.RequestUri.Query);
                Assert.DoesNotContain("status=eq.active", request.RequestUri.Query, "Closure must include failed subscriptions still billing at the provider.");
                if (Failure == "subscriptions") return new(HttpStatusCode.ServiceUnavailable);
                return JsonResponse($$"""[{"subscription_id":"22222222-2222-2222-2222-222222222222","tier_code":"all_stories_monthly","provider":"paystack","provider_payment_id":"{{(AuthorizationBilling ? "authorization-checkout" : "SUB_closure")}}","provider_email_token":"email-token","status":"{{SubscriptionStatus}}","next_renewal_at":"2026-05-17T06:21:00Z","recurring_billing_mode":"{{(AuthorizationBilling ? "paystack_authorization_schedule" : "provider_subscription")}}"}]""");
            }
            if (request.Method == HttpMethod.Get && path == "/subscription/SUB_closure")
                return Failure == "provider" ? new(HttpStatusCode.ServiceUnavailable)
                    : JsonResponse($$"""{"status":true,"data":{"subscription_code":"SUB_closure","status":"{{(ProviderStopped ? "non-renewing" : "attention")}}","email_token":"email-token"} }""");
            if (request.Method == HttpMethod.Post && path == "/subscription/disable")
            {
                Assert.IsNull(Handler.SubscriberPatchPayload, "Stop provider billing before closing the account.");
                if (Failure == "disable") return new(HttpStatusCode.Forbidden);
                ProviderStopped = true;
                return JsonResponse("""{"status":true,"message":"Subscription disabled successfully"}""");
            }
            if (request.Method == HttpMethod.Patch && path == "/rest/v1/subscriptions")
                return new(Failure == "persistence" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Patch && path == "/rest/v1/subscribers")
            {
                Assert.IsTrue(AuthorizationBilling || ProviderStopped);
                Assert.IsNotEmpty(Handler.SubscriptionPatchPayloads);
                return new(HttpStatusCode.NoContent);
            }
            return new(HttpStatusCode.NotFound);
        }
    }
}
