using System.Net;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    public async Task CardUpdateWorker_DifferentVerificationJobsRace_CollectsOneRenewal()
    {
        var fixture = new CardUpdateFixture();
        using var handler = new ConcurrentRecoveryReadsHandler(fixture.Handler);
        var second = JsonNode.Parse(fixture.Payload)!;
        second["data"]!["id"] = 902;
        second["data"]!["reference"] = "update-card-transaction-2";
        await CreateService(handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(handler).RecordPaystackEventAsync(second.ToJsonString());

        await Task.WhenAll(
            Task.Run(() => CreateService(handler).ProcessPendingPaystackCardUpdatesAsync()),
            Task.Run(() => CreateService(handler).ProcessPendingPaystackCardUpdatesAsync()));
        fixture.MakeJobsDue();
        await CreateService(handler).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual(2, handler.SimultaneousRecoveryReads, "Both jobs must read the original failed recovery before competing for it.");
        Assert.AreEqual(1, fixture.CollectedPaymentCount);
        Assert.HasCount(1, fixture.ChargeReferences);
        Assert.IsFalse(fixture.HasRecovery);
        Assert.IsTrue(fixture.Jobs.Values.All(job => job.Status == "completed"));
    }

    [TestMethod]
    public async Task CardUpdateWorker_CrashAfterReservationBeforeProviderAcceptance_ResumesSameAttempt()
    {
        var fixture = new CardUpdateFixture { FailChargeRequestOnce = true };
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();
        Assert.AreEqual("pending", fixture.RecoveryStatus);
        Assert.AreEqual(0, fixture.CollectedPaymentCount);

        fixture.MakeJobsDue();
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("completed", fixture.Jobs.Values.Single().Status);
        Assert.AreEqual(1, fixture.CollectedPaymentCount);
        Assert.HasCount(1, fixture.ChargeReferences);
        Assert.IsFalse(fixture.HasRecovery);
    }

    [TestMethod]
    public async Task CardUpdateWorker_ResolutionFailsAfterSucceededWrite_RestartFinishesAndExpiryPreservesAccess()
    {
        var fixture = new CardUpdateFixture { FailRecoveryResolutionOnce = true };
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();
        Assert.AreEqual("succeeded", fixture.RecoveryStatus);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.AreEqual("pending", fixture.Jobs.Values.Single().Status);

        await CreateService(fixture.Handler).ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsFalse(fixture.AccessSuspended, "Expiry must recheck the successful attempt rather than suspend a paid customer.");
        fixture.MakeJobsDue();
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("completed", fixture.Jobs.Values.Single().Status);
        Assert.IsFalse(fixture.HasRecovery);
        Assert.AreEqual(1, fixture.CollectedPaymentCount);
    }

    [TestMethod]
    public async Task CardVerification_LegacySucceededButUnresolvedRecovery_ReconcilesWithoutCollecting()
    {
        var fixture = new CardUpdateFixture { RecoveryStatus = "succeeded", PreviousPaymentStatus = "success" };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsFalse(fixture.HasRecovery);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(0)]
    public async Task CardVerification_HealthyLegacySubscriptionWithoutBillingAmount_StoresCardWithoutRenewing(int? amount)
    {
        var fixture = new CardUpdateFixture { HasRecovery = false, BillingAmount = amount };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsTrue(result.IsCardVerification);
        Assert.IsTrue(fixture.Handler.SubscriptionPatchPayloads.Any(payload => payload.Contains("AUTH_updated_card", StringComparison.Ordinal)));
        Assert.IsFalse(fixture.Handler.SubscriptionPatchPayloads.Any(payload => payload.Contains("next_renewal_at", StringComparison.Ordinal)));
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task CardVerification_UnpaidRecoveryWithoutBillingAmount_StoresCardButDoesNotGuessChargeAmount()
    {
        var fixture = new CardUpdateFixture { BillingAmount = null };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(fixture.Handler.SubscriptionPatchPayloads.Any(payload => payload.Contains("AUTH_updated_card", StringComparison.Ordinal)));
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsTrue(fixture.HasRecovery);
    }

    [TestMethod]
    public async Task CardUpdateWorker_TerminalAlertStorageFails_RetainsJobUntilAlertIsDurable()
    {
        var fixture = new CardUpdateFixture { JobCreatedAt = DateTimeOffset.UtcNow.AddHours(-25) };
        var alerts = new CardUpdateAlerts { FailQueueOnce = true };
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler, notificationEmailService: alerts).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("processing", fixture.Jobs.Values.Single().Status, "A terminal job must not be released before its alert is stored.");
        Assert.IsEmpty(alerts.Requests);
        fixture.MakeJobsDue();
        await CreateService(fixture.Handler, notificationEmailService: alerts).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("needs_attention", fixture.Jobs.Values.Single().Status);
        Assert.AreEqual(2, alerts.QueueAttempts);
        Assert.HasCount(1, alerts.Requests);
        Assert.AreEqual(0, fixture.ProviderRequestCount);
    }

    [TestMethod]
    public async Task CardUpdateWorker_ExistingIncidentExpires_KeepsSameAlertKeyAndReason()
    {
        var fixture = new CardUpdateFixture { ProviderCardMatches = false, JobCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20) };
        var alerts = new CardUpdateAlerts();
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler, notificationEmailService: alerts).ProcessPendingPaystackCardUpdatesAsync();

        fixture.Jobs.Values.Single().CreatedAt = DateTimeOffset.UtcNow.AddHours(-25);
        fixture.MakeJobsDue();
        await CreateService(fixture.Handler, notificationEmailService: alerts).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("needs_attention", fixture.Jobs.Values.Single().Status);
        Assert.AreEqual("provider_card_update", fixture.Jobs.Values.Single().ResultCode);
        Assert.HasCount(2, alerts.Requests);
        Assert.HasCount(1, alerts.Requests.Select(request => request.AlertKey).Distinct());
        Assert.IsTrue(alerts.Requests.All(request => request.Details.Contains(CardUpdateFixture.SubscriptionId, StringComparison.Ordinal)));
    }

    private sealed class ConcurrentRecoveryReadsHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner = new(inner, disposeHandler: false);
        private readonly object _gate = new();
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;
        public int SimultaneousRecoveryReads => Math.Min(_reads, 2);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            lock (_gate) response = _inner.SendAsync(request, cancellationToken).GetAwaiter().GetResult();
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/rest/v1/subscription_payment_recoveries" &&
                Interlocked.Increment(ref _reads) <= 2)
            {
                if (_reads == 2) _bothRead.TrySetResult();
                await _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
