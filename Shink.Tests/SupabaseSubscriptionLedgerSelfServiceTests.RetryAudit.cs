using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    public async Task LegacyRepairWhileWorkerOwnsTheRenewal_SubmitsOneCharge()
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending" };
        using var gate = new PauseChargeHandler(fixture.Handler);
        var service = CreateService(gate);
        var worker = service.ProcessExpiredPaymentRecoveriesAsync();
        try
        {
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue((await service.TryRepairPaidSubscriptionAsync(CheckoutEmail)).IsPending);
        }
        finally
        {
            gate.Resume.TrySetResult();
            await worker;
        }
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
        Assert.IsFalse(fixture.HasRecovery);
    }

    [TestMethod]
    [DataRow("cancelled", false)]
    [DataRow("attention", true)]
    [DataRow("non-renewing", false)]
    public async Task ScheduledRetry_RequiresAvailableRenewingProviderAgreement(string providerStatus, bool unavailable)
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending",
            ProviderStatus = providerStatus, SubscriptionUnavailable = unavailable };
        await CreateService(fixture.Handler).ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsTrue(fixture.HasRecovery);
    }

    [TestMethod]
    public async Task UpdatedCard_R55InvoiceCannotCollectStoredR79()
    {
        var fixture = new CardUpdateFixture { InvoiceAmount = 5500 };
        await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsNull(fixture.CompletedRenewal);
    }

    [TestMethod]
    [DataRow("queued")]
    [DataRow("processing")]
    public async Task ScheduledRetry_PreviousUnsettledReferencePreventsNewCollection(string state)
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending", PreviousPaymentStatus = state };
        var service = CreateService(fixture.Handler);
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        fixture.PreviousPaymentStatus = "success";
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsFalse(fixture.HasRecovery);
    }

    [TestMethod]
    [DataRow(100, "ZAR", CheckoutEmail)]
    [DataRow(7900, "USD", CheckoutEmail)]
    [DataRow(7900, "ZAR", "another@example.com")]
    public async Task ScheduledRetry_ImmediateSuccessMustMatchFullRenewal(int amount, string currency, string owner)
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending",
            VerifiedAmount = amount, VerifiedCurrency = currency, VerifiedPaymentEmail = owner };
        await CreateService(fixture.Handler).ProcessExpiredPaymentRecoveriesAsync();
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsNull(fixture.CompletedRenewal);
    }

    [TestMethod]
    public async Task PaidInvoiceFromPreviousPeriodCannotSettleCurrentDebt()
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending",
            InvoicePaid = true, PreviousPeriod = true };
        await CreateService(fixture.Handler).ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsNull(fixture.CompletedRenewal);
    }

    [TestMethod]
    public async Task ScheduledRetry_FailedAccessWriteIsReconciledWithoutRecollecting()
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending", FailRecoveryWriteOnce = true };
        var service = CreateService(fixture.Handler);
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsNull(fixture.CompletedRenewal);
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsNotNull(fixture.CompletedRenewal);
        Assert.IsFalse(fixture.HasRecovery);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task DelayedScheduledWebhookCannotUndoCancellation()
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending", PaymentStatus = "processing" };
        var service = CreateService(fixture.Handler);
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.AreEqual("pending", fixture.RecoveryStatus);
        fixture.PaymentStatus = "success";
        fixture.Cancelled = true;
        var result = await service.RecordPaystackEventAsync(RetryWebhook(fixture.ChargeReferences.Single()));
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(fixture.CompletedRenewal);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads);
    }

    [TestMethod]
    [DataRow("success")]
    [DataRow("failed")]
    public async Task BackgroundWorker_ReconcilesManualAttemptAfterBrowserCloses(string terminalStatus)
    {
        var fixture = new CardUpdateFixture { PaymentStatus = "processing", SuspendedRecovery = true };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.IsTrue((await service.RetryCustomerPaymentAsync(CheckoutEmail, offer)).IsPending);
        fixture.PaymentStatus = terminalStatus;
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.AreEqual(terminalStatus == "success" ? "succeeded" : "failed", fixture.RecoveryStatus);
        Assert.AreEqual(terminalStatus == "failed", fixture.HasRecovery);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task CustomerStatusCheckDuringProviderOutageCannotSubmitAnotherCharge()
    {
        var fixture = new CardUpdateFixture { PaymentStatus = "queued" };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        await service.RetryCustomerPaymentAsync(CheckoutEmail, offer);
        fixture.VerificationUnavailable = true;
        var pending = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.IsTrue(pending.AwaitingPayment);
        Assert.IsTrue((await service.RetryCustomerPaymentAsync(CheckoutEmail, pending)).IsPending);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    [DataRow("eligible", 1)]
    [DataRow("outage", 0)]
    [DataRow("different-period", 0)]
    [DataRow("pause", 0)]
    public async Task InterruptedCustomerSubmission_ResumesOnlyItsOriginalEligibleRenewal(string scenario, int expectedCollections)
    {
        var fixture = new CardUpdateFixture { FailChargeRequestOnce = true, SuspendedRecovery = true };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => service.RetryCustomerPaymentAsync(CheckoutEmail, offer));
        var reserved = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.IsTrue(reserved.AwaitingPayment);
        fixture.AttemptedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        fixture.VerificationUnavailable = scenario == "outage";
        fixture.Paused = scenario == "pause";
        if (scenario == "different-period") fixture.ReservedThrough = "2099-12-25T19:43:00Z";

        await service.ProcessExpiredPaymentRecoveriesAsync();

        Assert.AreEqual(expectedCollections, fixture.CollectedPaymentCount);
        Assert.AreEqual(expectedCollections == 0, fixture.HasRecovery);
        if (expectedCollections == 1) Assert.AreEqual(reserved.PreviousReference, fixture.ChargeReferences.Single());
    }

    [TestMethod]
    public async Task LegacySuccessWithRemainingSchedule_IsVerifiedWithoutANewCharge()
    {
        var fixture = new CardUpdateFixture { RecoveryStatus = "succeeded", PreviousPaymentStatus = "success",
            DueAt = DateTimeOffset.UtcNow.AddDays(1).ToString("O") };
        await CreateService(fixture.Handler).ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsFalse(fixture.HasRecovery);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task OperationsBatchAndScheduledWorker_ShareOnePendingReference()
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending", PaymentStatus = "processing" };
        var service = CreateService(fixture.Handler);
        var batch = await service.RetryDueRecoveryAsync(CardUpdateFixture.SubscriptionId,
            "33333333-3333-3333-3333-333333333333", CheckoutEmail, "SUB_cardupdate");
        Assert.AreEqual("pending", batch.AttemptStatus);
        Assert.AreEqual(fixture.ChargeReferences.Single(), batch.Reference);
        fixture.PaymentStatus = "success";
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsFalse(fixture.HasRecovery);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task MissingRetryMetadata_CannotFallThroughToOrdinarySubscriptionActivation()
    {
        var fixture = new CardUpdateFixture { PaymentStatus = "queued" };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        await service.RetryCustomerPaymentAsync(CheckoutEmail, offer);
        var payload = JsonNode.Parse(RetryWebhook(fixture.ChargeReferences.Single()))!;
        payload["data"]!["metadata"] = new JsonObject();
        fixture.PaymentStatus = "success";
        var webhook = await service.RecordPaystackEventAsync(payload.ToJsonString());
        Assert.IsFalse(webhook.IsSuccess);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads);
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsFalse(fixture.HasRecovery);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    [DataRow("paid-invoice", true)]
    [DataRow("unconfirmed-legacy-reference", false)]
    public async Task PreviousDeclineCannotHideAnIndependentLegacyAttempt(string legacyReference, bool alreadyPaid)
    {
        var fixture = new CardUpdateFixture { PreviousPaymentStatus = "failed", HistoricalAttempts =
            JsonSerializer.Serialize(new[] { new { event_status = "queued", payload = new { reference = legacyReference } } }) };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        var result = await service.RetryCustomerPaymentAsync(CheckoutEmail, offer);
        Assert.AreEqual(alreadyPaid, result.IsRecovered);
        Assert.AreEqual(!alreadyPaid, fixture.HasRecovery);
        Assert.IsEmpty(fixture.ChargeReferences);
    }

    [TestMethod]
    [DataRow("failed", true)]
    [DataRow("processing", false)]
    public async Task ProviderPaidInvoiceWithDifferentReference_RequiresDefinitivePriorDecline(string prior, bool recovered)
    {
        var fixture = new CardUpdateFixture { InvoicePaid = true, PreviousPaymentStatus = prior,
            RecoveryStatus = "pending", ExposeScheduledRetry = true };
        await CreateService(fixture.Handler).ProcessExpiredPaymentRecoveriesAsync();
        Assert.AreEqual(!recovered, fixture.HasRecovery);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task LegacyFollowUpRetry_UsesThePersistedAttemptHistory()
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending", ScheduledEmailsAt = null,
            PreviousPaymentStatus = "failed", PaymentStatus = "failed", AttemptedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var emails = new TrackingSubscriptionPaymentRecoveryEmailService();
        await CreateService(fixture.Handler, emails).ProcessExpiredPaymentRecoveriesAsync();
        Assert.AreEqual("failed", fixture.RecoveryStatus);
        Assert.IsNull(fixture.DueAt);
        Assert.HasCount(1, emails.ScheduledRequests);
    }

    private sealed class PauseChargeHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/transaction/charge_authorization")
            {
                Started.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
