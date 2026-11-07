using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    private const string CheckoutEmail = "ouer@example.com";
    private const string CheckoutTier = "all_stories_monthly";

    [TestMethod]
    [DataRow("attention")]
    [DataRow("active")]
    public async Task AssessSubscriptionCheckoutAsync_ActiveExpiredPaystackAgreementRequiresRecovery(string paystackStatus)
    {
        var calls = 0;
        var handler = CreateCheckoutBillingHandler(
            $"[{CheckoutHistoryRow("active", "SUB_original")}]",
            path =>
            {
                Assert.AreEqual("/subscription/SUB_original", path);
                calls++;
                return PaystackAgreement("SUB_original", paystackStatus);
            });

        var assessment = await CreateService(handler).AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.PaystackRecoveryRequired, assessment.TransitionKind);
        Assert.IsTrue(SubscriptionCheckoutTransitionKinds.BlocksCheckout(assessment.TransitionKind));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow("non-renewing")]
    [DataRow("cancelled")]
    [DataRow("complete")]
    [DataRow("completed")]
    public async Task AssessSubscriptionCheckoutAsync_ConfirmedStoppedAgreementAllowsCheckout(string paystackStatus)
    {
        var calls = 0;
        var handler = CreateCheckoutBillingHandler(
            $"[{CheckoutHistoryRow("failed", "SUB_original")},{CheckoutHistoryRow("cancelled", "SUB_original", imported: true)}]",
            path =>
            {
                Assert.AreEqual("/subscription/SUB_original", path);
                calls++;
                return PaystackAgreement("SUB_original", paystackStatus);
            });

        var assessment = await CreateService(handler).AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.NewCheckout, assessment.TransitionKind);
        Assert.AreEqual(1, calls, "The native and imported records refer to the same agreement.");
    }

    [TestMethod]
    [DataRow("cancelled", false)]
    [DataRow("active", true)]
    public async Task AssessSubscriptionCheckoutAsync_AgreementScheduledToResumeBlocksCheckout(string localStatus, bool current)
    {
        var handler = CreateCheckoutBillingHandler(
            $"[{CheckoutHistoryRow(localStatus, "SUB_original", current: current)}]",
            _ => PaystackAgreement("SUB_original", "non-renewing"),
            hasScheduledResume: true);

        var assessment = await CreateService(handler).AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.BillingVerificationRequired, assessment.TransitionKind);
        Assert.IsTrue(SubscriptionCheckoutTransitionKinds.BlocksCheckout(assessment.TransitionKind));
    }

    [TestMethod]
    [DataRow("provider-unavailable")]
    [DataRow("transport-failure")]
    [DataRow("timeout")]
    [DataRow("unrecognized-status")]
    [DataRow("missing-status")]
    [DataRow("missing-code")]
    [DataRow("wrong-code")]
    [DataRow("malformed-response")]
    public async Task AssessSubscriptionCheckoutAsync_UnverifiedAgreementBlocksCheckout(string failure)
    {
        var handler = CreateCheckoutBillingHandler(
            $"[{CheckoutHistoryRow("failed", "SUB_original")}]",
            _ => failure switch
            {
                "provider-unavailable" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "transport-failure" => throw new HttpRequestException("Paystack is unreachable."),
                "timeout" => throw new TaskCanceledException("Paystack timed out."),
                "unrecognized-status" => PaystackAgreement("SUB_original", "unknown"),
                "missing-status" => JsonResponse("""{"status":true,"data":{"subscription_code":"SUB_original"}}"""),
                "missing-code" => JsonResponse("""{"status":true,"data":{"status":"cancelled"}}"""),
                "wrong-code" => PaystackAgreement("SUB_other", "cancelled"),
                "malformed-response" => JsonResponse("[]"),
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            });

        var assessment = await CreateService(handler).AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.BillingVerificationRequired, assessment.TransitionKind);
        Assert.IsTrue(SubscriptionCheckoutTransitionKinds.BlocksCheckout(assessment.TransitionKind));
    }

    [TestMethod]
    [DataRow("/rest/v1/subscribers")]
    [DataRow("/rest/v1/subscriptions")]
    [DataRow("/rest/v1/subscription_payment_pauses")]
    public async Task AssessSubscriptionCheckoutAsync_UnavailableLedgerDoesNotAllowCheckout(string failedPath)
    {
        var handler = CreateCheckoutBillingHandler(
            $"[{CheckoutHistoryRow("cancelled", "SUB_original")}]",
            _ => PaystackAgreement("SUB_original", "non-renewing"),
            failedPath);

        var assessment = await CreateService(handler).AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.BillingVerificationRequired, assessment.TransitionKind);
    }

    [TestMethod]
    public async Task AssessSubscriptionCheckoutAsync_LegacyAgreementWithoutCodeBlocksCheckout()
    {
        var handler = CreateCheckoutBillingHandler($"[{CheckoutHistoryRow("failed", null, imported: true)}]");

        var assessment = await CreateService(handler).AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.BillingVerificationRequired, assessment.TransitionKind);
    }

    [TestMethod]
    public async Task AssessSubscriptionCheckoutAsync_AdditionalAgreementRequiresSupport()
    {
        var handler = CreateCheckoutBillingHandler(
            $"[{CheckoutHistoryRow("active", "SUB_current", current: true)},{CheckoutHistoryRow("failed", "SUB_original")}]",
            path => path switch
            {
                "/subscription/SUB_current" => PaystackAgreement("SUB_current", "active"),
                "/subscription/SUB_original" => PaystackAgreement("SUB_original", "attention"),
                _ => throw new AssertFailedException($"Unexpected Paystack request {path}.")
            });

        var assessment = await CreateService(handler).AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.BillingVerificationRequired, assessment.TransitionKind);
    }

    [TestMethod]
    public async Task CreatePaystackCheckoutRecoveryLinkAsync_UsesTheVerifiedOriginalAgreement()
    {
        const string link = "https://paystack.com/manage/subscriptions/original?subscription_token=test";
        var handler = CreateCheckoutBillingHandler(
            $"[{CheckoutHistoryRow("active", "SUB_original")},{CheckoutHistoryRow("cancelled", "SUB_old")}]",
            path => path switch
            {
                "/subscription/SUB_original" => PaystackAgreement("SUB_original", "attention"),
                "/subscription/SUB_old" => PaystackAgreement("SUB_old", "cancelled"),
                "/subscription/SUB_original/manage/link" => JsonResponse(JsonSerializer.Serialize(new { status = true, data = new { link } })),
                _ => throw new AssertFailedException($"Unexpected Paystack request {path}.")
            });

        var result = await CreateService(handler).CreatePaystackCheckoutRecoveryLinkAsync(CheckoutEmail);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(link, result.Link);
    }

    [TestMethod]
    public async Task CreatePaystackCheckoutRecoveryLinkAsync_MultipleLiveAgreementsRequireSupport()
    {
        var handler = CreateCheckoutBillingHandler(
            $"[{CheckoutHistoryRow("active", "SUB_original")},{CheckoutHistoryRow("active", "SUB_second")}]",
            path => path switch
            {
                "/subscription/SUB_original" => PaystackAgreement("SUB_original", "attention"),
                "/subscription/SUB_second" => PaystackAgreement("SUB_second", "active"),
                _ => throw new AssertFailedException($"Unexpected Paystack request {path}.")
            });

        var result = await CreateService(handler).CreatePaystackCheckoutRecoveryLinkAsync(CheckoutEmail);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Link);
    }

    private static RecordingHandler CreateCheckoutBillingHandler(
        string rows,
        Func<string, HttpResponseMessage>? paystack = null,
        string? failedPath = null,
        bool hasScheduledResume = false) => new(request =>
    {
        Assert.AreEqual(HttpMethod.Get, request.Method, "Checkout eligibility must not change any agreement or start a payment.");
        var path = request.RequestUri!.AbsolutePath;
        if (path == failedPath)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        return path switch
        {
            "/rest/v1/subscribers" => JsonResponse("""[{"subscriber_id":"11111111-1111-1111-1111-111111111111"}]"""),
            "/rest/v1/subscriptions" => request.RequestUri.Query.Contains("provider.eq.paystack", StringComparison.Ordinal)
                ? JsonResponse(rows)
                : JsonResponse("[]"),
            "/rest/v1/subscription_payment_pauses" => JsonResponse(hasScheduledResume ? """[{"pause_id":"scheduled"}]""" : "[]"),
            _ when request.RequestUri.Host == "api.paystack.co" && paystack is not null => paystack(path),
            _ => throw new AssertFailedException($"Unexpected billing lookup {path}.")
        };
    });

    private static string CheckoutHistoryRow(string status, string? code, bool imported = false, bool current = false) =>
        JsonSerializer.Serialize(new
        {
            subscription_id = Guid.NewGuid(),
            tier_code = current ? "story_corner_monthly" : CheckoutTier,
            provider = "paystack",
            source_system = imported ? "wordpress_pmpro" : "shink_app",
            provider_payment_id = imported ? "wp-pmpro-current-101" : code,
            provider_transaction_id = imported ? code : "previous-payment",
            provider_token = current ? "AUTH_current" : null,
            status,
            next_renewal_at = current ? "2099-01-01T00:00:00Z" : "2020-01-01T00:00:00Z",
            cancelled_at = status == "cancelled" ? "2020-01-01T00:00:00Z" : null
        });

    private static HttpResponseMessage PaystackAgreement(string code, string status) =>
        JsonResponse(JsonSerializer.Serialize(new { status = true, data = new { subscription_code = code, status } }));
}
