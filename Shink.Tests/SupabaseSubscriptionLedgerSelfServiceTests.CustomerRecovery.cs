using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CustomerRetry_RecoversGraceAndSuspendedAccountsOnOriginalAgreement(bool suspended)
    {
        var fixture = new CardUpdateFixture { StoredCardMatches = true, SuspendedRecovery = suspended };
        var service = CreateService(fixture.Handler);
        var assessment = await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null);
        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.CustomerPaymentRetry, assessment.TransitionKind);
        Assert.IsNotNull(assessment.CustomerRecovery);
        Assert.AreEqual(79m, assessment.CustomerRecovery.AmountZar);
        var result = await service.RetryCustomerPaymentAsync(CheckoutEmail, assessment.CustomerRecovery);
        Assert.IsTrue(result.IsRecovered, result.ErrorMessage);
        Assert.IsFalse(fixture.HasRecovery);
        Assert.AreEqual(DateTimeOffset.Parse("2099-11-25T19:43:00Z"), DateTimeOffset.Parse(fixture.CompletedRenewal!));
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);

        await service.RetryCustomerPaymentAsync(CheckoutEmail, assessment.CustomerRecovery);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CustomerRetry_UsesOnlyTheReusableCardOnTheMatchedProviderAgreement(bool reusable)
    {
        var fixture = new CardUpdateFixture { StoredCardMatches = false, ProviderCardReusable = reusable };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.AreEqual(reusable, offer.CanUseSavedCard);
        var result = await service.RetryCustomerPaymentAsync(CheckoutEmail, offer);
        Assert.AreEqual(reusable, result.IsRecovered);
        if (reusable)
        {
            using var charge = JsonDocument.Parse(fixture.Handler.PaystackChargePayloads.Single());
            Assert.AreEqual("AUTH_updated_card", charge.RootElement.GetProperty("authorization_code").GetString());
        }
        else Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task CustomerRetry_QueuedPaymentIsReconciledWithoutAnotherCharge()
    {
        var fixture = new CardUpdateFixture { StoredCardMatches = true, SuspendedRecovery = true, PaymentStatus = "queued" };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.IsTrue((await service.RetryCustomerPaymentAsync(CheckoutEmail, offer)).IsPending);
        Assert.IsTrue(fixture.HasRecovery);
        var pending = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.IsTrue(pending.AwaitingPayment);
        Assert.IsTrue((await service.RetryCustomerPaymentAsync(CheckoutEmail, pending)).IsPending);
        fixture.PaymentStatus = "success";
        Assert.IsTrue((await service.RetryCustomerPaymentAsync(CheckoutEmail, pending)).IsRecovered);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task CustomerRetry_DeclineAllowsOneNewDeliberateRetry()
    {
        var fixture = new CardUpdateFixture { StoredCardMatches = true, SuspendedRecovery = true, PaymentStatus = "failed" };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.IsFalse((await service.RetryCustomerPaymentAsync(CheckoutEmail, offer)).IsRecovered);
        Assert.IsNull(fixture.CompletedRenewal);
        Assert.IsTrue(fixture.HasRecovery);
        await service.RetryCustomerPaymentAsync(CheckoutEmail, offer);
        Assert.HasCount(1, fixture.ChargeReferences);
        // A second click on a newly displayed offer can submit a distinct attempt.
        var next = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.IsNotNull(next.PreviousReference);
        await service.RetryCustomerPaymentAsync(CheckoutEmail, next);
        Assert.HasCount(2, fixture.ChargeReferences);
    }

    [TestMethod]
    public async Task CustomerRetry_WrongVerifiedAmountDoesNotGrantAccess()
    {
        var fixture = new CardUpdateFixture { StoredCardMatches = true, SuspendedRecovery = true, VerifiedAmount = 100 };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        var result = await service.RetryCustomerPaymentAsync(CheckoutEmail, offer);
        Assert.IsFalse(result.IsRecovered);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsNull(fixture.CompletedRenewal);
    }

    [TestMethod]
    public async Task CustomerRetry_DoesNotCollectOverAnUnresolvedHistoricalReference()
    {
        var fixture = new CardUpdateFixture { StoredCardMatches = true, HistoricalAttempts =
            """[{"event_status":"queued","payload":{"data":{"reference":"missing-queued-retry"}}}]""" };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.IsTrue((await service.RetryCustomerPaymentAsync(CheckoutEmail, offer)).IsPending);
        Assert.IsEmpty(fixture.ChargeReferences);
    }

    [TestMethod]
    public async Task SuspendedCustomer_CardUpdateCanCollectAndRestoreAccess()
    {
        var fixture = new CardUpdateFixture { SuspendedRecovery = true };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);
        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsFalse(fixture.HasRecovery);
        Assert.IsNotNull(fixture.CompletedRenewal);
        Assert.HasCount(1, fixture.ChargeReferences);
    }

    [TestMethod]
    [DataRow(100, false)]
    [DataRow(7900, true)]
    public async Task CustomerRetry_QueuedWebhookValidatesFullAmountAndRestoresAfterBrowserCloses(int amount, bool expected)
    {
        var fixture = new CardUpdateFixture { StoredCardMatches = true, SuspendedRecovery = true, PaymentStatus = "queued" };
        var service = CreateService(fixture.Handler);
        var offer = (await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, null)).CustomerRecovery!;
        Assert.IsTrue((await service.RetryCustomerPaymentAsync(CheckoutEmail, offer)).IsPending);
        var reference = fixture.ChargeReferences.Single();
        var payload = JsonSerializer.Serialize(new { @event = "charge.success", data = new {
            id = 999, status = "success", amount, currency = "ZAR", reference,
            customer = new { email = CheckoutEmail }, metadata = new {
                source = "subscription_authorization_retry", retry_trigger = "customer_retry", subscription_id = offer.SubscriptionId,
                recovery_id = offer.RecoveryId, provider_payment_id = "SUB_cardupdate", provider_next_payment_date = "2099-11-25T19:43:00Z"
            }
        } });
        fixture.PaymentStatus = "success";
        var result = await service.RecordPaystackEventAsync(payload);
        Assert.AreEqual(expected, result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(!expected, fixture.HasRecovery);
        Assert.HasCount(1, fixture.ChargeReferences);
    }

    [TestMethod]
    public async Task ScheduledRetry_ReconcilesQueuedChargeOnLaterPollWithoutCollectingTwice()
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending", PaymentStatus = "queued" };
        var service = CreateService(fixture.Handler);
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsTrue(fixture.HasRecovery);
        Assert.AreEqual("pending", fixture.RecoveryStatus);
        using var charge = JsonDocument.Parse(fixture.Handler.PaystackChargePayloads.Single());
        Assert.AreEqual("AUTH_updated_card", charge.RootElement.GetProperty("authorization_code").GetString());
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsTrue(fixture.HasRecovery);
        fixture.ProviderCardReusable = false; // A card change cannot invalidate an already queued transaction.
        fixture.PaymentStatus = "success";
        await service.ProcessExpiredPaymentRecoveriesAsync();
        Assert.IsFalse(fixture.HasRecovery);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task ScheduledRetry_UnconfiguredWorkerLeavesTheDueAttemptUntouched()
    {
        var fixture = new CardUpdateFixture { ExposeScheduledRetry = true, RecoveryStatus = "pending" };
        await CreateService(fixture.Handler, paystackKey: "").ProcessExpiredPaymentRecoveriesAsync();
        Assert.AreEqual("pending", fixture.RecoveryStatus);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsFalse(fixture.AccessSuspended);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }
}
