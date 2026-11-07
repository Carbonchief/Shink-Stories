using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    public async Task ChangePaidSubscriptionPlanAsync_FailedChangeThenDifferentPlanStopsReenabledAgreement()
    {
        var renewalAt = DateTimeOffset.UtcNow.AddDays(8);
        DateTimeOffset? cancelledAt = null;
        var originalRenewing = true;
        var createAttempts = new List<bool>();
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse("""[{"subscriber_id":"11111111-1111-1111-1111-111111111111"}]""");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(JsonSerializer.Serialize(new[] { new
                {
                    subscription_id = "22222222-2222-2222-2222-222222222222",
                    tier_code = "all_stories_yearly",
                    provider = "paystack",
                    source_system = "shink_app",
                    provider_payment_id = "SUB_original",
                    provider_token = "AUTH_original",
                    provider_email_token = "old-email-token",
                    status = "active",
                    next_renewal_at = renewalAt,
                    cancelled_at = cancelledAt
                } }));
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_payment_pauses"))
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Get && path == "/subscription/SUB_original")
            {
                return PaystackAgreement("SUB_original", originalRenewing ? "active" : "non-renewing");
            }

            if (request.Method == HttpMethod.Post && path == "/subscription/disable")
            {
                originalRenewing = false;
                return JsonResponse("""{"status":true}""");
            }

            if (request.Method == HttpMethod.Post && path == "/subscription/enable")
            {
                originalRenewing = true;
                return JsonResponse("""{"status":true}""");
            }

            if (request.Method == HttpMethod.Patch && path == "/rest/v1/subscriptions")
            {
                using var patch = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                cancelledAt = patch.RootElement.GetProperty("cancelled_at").GetDateTimeOffset();
                return JsonResponse("""[{"subscription_id":"22222222-2222-2222-2222-222222222222"}]""");
            }

            if (request.Method == HttpMethod.Post && path == "/subscription")
            {
                createAttempts.Add(originalRenewing);
                return createAttempts.Count == 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : PaystackAgreement("SUB_replacement", "active");
            }

            if (request.Method == HttpMethod.Get && path == "/subscription/SUB_replacement")
            {
                return PaystackAgreement("SUB_replacement", "active");
            }

            if (request.Method == HttpMethod.Post && path == "/rest/v1/subscriptions")
            {
                return JsonResponse("""[{"subscription_id":"33333333-3333-3333-3333-333333333333"}]""");
            }

            if (request.Method == HttpMethod.Post && path == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);

        var failed = await service.ChangePaidSubscriptionPlanAsync(CheckoutEmail, "storie-hoekie-maandeliks");

        Assert.IsFalse(failed.IsSuccess);
        Assert.IsTrue(originalRenewing, "Compensation must restore the original agreement after creation fails.");
        Assert.AreEqual(renewalAt, cancelledAt, "Reproduce the stale cancellation marker left by the failed change.");

        var assessment = await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);
        var nextChange = await service.ChangePaidSubscriptionPlanAsync(CheckoutEmail, "schink-stories-maandeliks");

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.PlanChange, assessment.TransitionKind);
        Assert.IsTrue(nextChange.IsSuccess, nextChange.ErrorMessage);
        CollectionAssert.AreEqual(new[] { false, false }, createAttempts,
            "The original agreement must be stopped before either replacement is created.");
        Assert.IsFalse(originalRenewing);
        Assert.HasCount(2, handler.PaystackDisablePayloads);
        Assert.HasCount(1, handler.PaystackEnablePayloads);
        Assert.IsEmpty(handler.PaystackChargePayloads, "Both changes are downgrades with payment deferred until renewal.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CreatePaystackCheckoutRecoveryLinkAsync_CurrentAndOlderLiveAgreementsRequireSupport(bool olderFirst)
    {
        var current = CheckoutHistoryRow("active", "SUB_current", current: true);
        var older = CheckoutHistoryRow("active", "SUB_older");
        var lookedUp = new List<string>();
        var handler = CreateCheckoutBillingHandler(
            olderFirst ? $"[{older},{current}]" : $"[{current},{older}]",
            path =>
            {
                lookedUp.Add(path);
                return path switch
                {
                    "/subscription/SUB_current" => PaystackAgreement("SUB_current", "active"),
                    "/subscription/SUB_older" => PaystackAgreement("SUB_older", "attention"),
                    "/subscription/SUB_older/manage/link" => JsonResponse("""{"status":true,"data":{"link":"https://paystack.com/manage/subscriptions/older"}}"""),
                    _ => throw new AssertFailedException($"Unexpected Paystack request {path}.")
                };
            });
        var service = CreateService(handler);

        var assessment = await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);
        var link = await service.CreatePaystackCheckoutRecoveryLinkAsync(CheckoutEmail);

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.BillingVerificationRequired, assessment.TransitionKind);
        Assert.IsTrue(SubscriptionCheckoutTransitionKinds.BlocksCheckout(assessment.TransitionKind));
        Assert.IsFalse(link.IsSuccess);
        Assert.IsNull(link.Link);
        Assert.DoesNotContain("/subscription/SUB_older/manage/link", lookedUp);
    }

    [TestMethod]
    [DataRow("failed", "attention")]
    [DataRow("failed", "active")]
    [DataRow("cancelled", "active")]
    [DataRow("pending", "attention")]
    public async Task CreatePaystackCheckoutRecoveryLinkAsync_SuspendedAgreementRequiresReconciliation(
        string localStatus, string paystackStatus)
    {
        var lookedUp = new List<string>();
        var handler = CreateCheckoutBillingHandler(
            $"[{CheckoutHistoryRow(localStatus, "SUB_original")}]",
            path =>
            {
                lookedUp.Add(path);
                return path switch
                {
                    "/subscription/SUB_original" => PaystackAgreement("SUB_original", paystackStatus),
                    "/subscription/SUB_original/manage/link" => JsonResponse("""{"status":true,"data":{"link":"https://paystack.com/manage/subscriptions/original"}}"""),
                    _ => throw new AssertFailedException($"Unexpected Paystack request {path}.")
                };
            });
        var service = CreateService(handler);

        var assessment = await service.AssessSubscriptionCheckoutAsync(CheckoutEmail, CheckoutTier);
        var link = await service.CreatePaystackCheckoutRecoveryLinkAsync(CheckoutEmail);

        Assert.AreEqual(SubscriptionCheckoutTransitionKinds.BillingVerificationRequired, assessment.TransitionKind);
        Assert.IsTrue(SubscriptionCheckoutTransitionKinds.BlocksCheckout(assessment.TransitionKind));
        Assert.IsFalse(link.IsSuccess);
        Assert.IsNull(link.Link);
        Assert.DoesNotContain("/subscription/SUB_original/manage/link", lookedUp);
    }
}
