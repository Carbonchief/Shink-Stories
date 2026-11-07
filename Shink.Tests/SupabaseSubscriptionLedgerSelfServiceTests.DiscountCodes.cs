using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    [DataRow(1, "Year")]
    [DataRow(1, "years")]
    [DataRow(12, "Month")]
    [DataRow(12, "Months")]
    public async Task PreviewSignupDiscountCodeAsync_YearlySelectionAcceptsOneYearFreeAccess(
        int expirationNumber,
        string expirationPeriod)
    {
        var handler = CreateYearlyVoucherHandler(VoucherMapping("all_stories_monthly", expirationNumber, expirationPeriod));
        var service = CreateService(handler);
        var earliestExpiry = DateTimeOffset.UtcNow.AddYears(1);

        var result = await service.PreviewSignupDiscountCodeAsync(" YEARFREE ", " ALL_STORIES_YEARLY ");

        var latestExpiry = DateTimeOffset.UtcNow.AddYears(1);
        Assert.IsTrue(result.IsValid, result.ErrorMessage);
        Assert.AreEqual("all_stories_monthly", result.ResolvedTierCode);
        Assert.IsTrue(result.BypassesPayment);
        Assert.IsNotNull(result.AccessEndsAtUtc);
        Assert.IsTrue(result.AccessEndsAtUtc >= earliestExpiry && result.AccessEndsAtUtc <= latestExpiry,
            "Selecting yearly must retain the voucher's one-year expiry.");
        Assert.IsEmpty(handler.SubscriptionCreatePayloads);
    }

    [TestMethod]
    [DataRow("free_access", true, "all_stories_monthly", 1, "Month", "all_stories_yearly")]
    [DataRow("free_access", true, "all_stories_monthly", 3, "months", "all_stories_yearly")]
    [DataRow("free_access", true, "all_stories_monthly", 2, "Year", "all_stories_yearly")]
    [DataRow("free_access", true, "all_stories_monthly", 12, " months ", "all_stories_yearly")]
    [DataRow("free_access", true, "all_stories_monthly", null, "Year", "all_stories_yearly")]
    [DataRow("free_access", true, "story_corner_monthly", 1, "Year", "all_stories_yearly")]
    [DataRow("free_access", true, "all_stories_monthly", 1, "Year", "school_small_yearly")]
    [DataRow("percentage", false, "all_stories_monthly", 1, "Year", "all_stories_yearly")]
    [DataRow("percentage", true, "all_stories_monthly", 1, "Year", "all_stories_yearly")]
    [DataRow("free_access", false, "all_stories_monthly", 1, "Year", "all_stories_yearly")]
    public async Task PreviewSignupDiscountCodeAsync_YearlySelectionKeepsVoucherRestrictions(
        string discountKind,
        bool bypassPayment,
        string mappedTier,
        int? expirationNumber,
        string expirationPeriod,
        string selectedTier)
    {
        var handler = CreateYearlyVoucherHandler(
            VoucherMapping(mappedTier, expirationNumber, expirationPeriod),
            discountKind: discountKind,
            bypassPayment: bypassPayment);
        var service = CreateService(handler);

        var result = await service.PreviewSignupDiscountCodeAsync("YEARFREE", selectedTier);

        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.ResolvedTierCode);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsEmpty(handler.SubscriptionCreatePayloads);
    }

    [TestMethod]
    public async Task PreviewSignupDiscountCodeAsync_ExplicitYearlyMappingTakesPrecedence()
    {
        var handler = CreateYearlyVoucherHandler(
            """
            [
              { "tier_code": "all_stories_monthly", "expiration_number": 1, "expiration_period": "Year" },
              { "tier_code": "all_stories_yearly", "expiration_number": 2, "expiration_period": "Year" }
            ]
            """);
        var service = CreateService(handler);
        var earliestExpiry = DateTimeOffset.UtcNow.AddYears(2);

        var result = await service.PreviewSignupDiscountCodeAsync("YEARFREE", "all_stories_yearly");

        Assert.IsTrue(result.IsValid, result.ErrorMessage);
        Assert.AreEqual("all_stories_yearly", result.ResolvedTierCode);
        Assert.IsNotNull(result.AccessEndsAtUtc);
        Assert.IsTrue(result.AccessEndsAtUtc >= earliestExpiry && result.AccessEndsAtUtc <= DateTimeOffset.UtcNow.AddYears(2));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("all_stories_monthly")]
    public async Task PreviewSignupDiscountCodeAsync_MonthlyVoucherSelectionStillWorks(string? selectedTier)
    {
        var service = CreateService(CreateYearlyVoucherHandler(VoucherMapping("all_stories_monthly", 1, "Year")));

        var result = await service.PreviewSignupDiscountCodeAsync("YEARFREE", selectedTier);

        Assert.IsTrue(result.IsValid, result.ErrorMessage);
        Assert.AreEqual("all_stories_monthly", result.ResolvedTierCode);
    }

    [TestMethod]
    [DataRow(1, "Year")]
    [DataRow(12, "Month")]
    public async Task ApplySignupDiscountCodeAsync_YearlySelectionRedeemsCanonicalOneYearMapping(
        int expirationNumber,
        string expirationPeriod)
    {
        string? rpcPayload = null;
        var expiry = DateTimeOffset.UtcNow.AddYears(1);
        var handler = CreateYearlyVoucherHandler(
            VoucherMapping("all_stories_monthly", expirationNumber, expirationPeriod),
            request =>
            {
                if (request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath != "/rest/v1/rpc/redeem_signup_discount_code")
                {
                    return null;
                }

                rpcPayload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return SuccessfulYearlyVoucherRedemption(expiry);
            });
        var service = CreateService(handler);

        var result = await service.ApplySignupDiscountCodeAsync(" Ouer@Example.com ", " YEARFREE ", " ALL_STORIES_YEARLY ");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual("all_stories_monthly", result.TierCode);
        Assert.AreEqual(expiry, result.AccessEndsAtUtc);
        Assert.IsFalse(result.PaymentPauseApplied);
        Assert.IsNotNull(rpcPayload);
        using var payload = JsonDocument.Parse(rpcPayload);
        Assert.AreEqual("ouer@example.com", payload.RootElement.GetProperty("p_email").GetString());
        Assert.AreEqual("yearfree", payload.RootElement.GetProperty("p_code").GetString());
        Assert.AreEqual("all_stories_monthly", payload.RootElement.GetProperty("p_selected_tier_code").GetString());
        Assert.IsEmpty(handler.SubscriptionCreatePayloads, "Access must still be granted by the atomic redemption RPC.");
        Assert.IsEmpty(handler.PaystackDisablePayloads);
        Assert.IsEmpty(handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task ApplySignupDiscountCodeAsync_YearlySelectionRejectsShortVoucherBeforeRedemption()
    {
        var rpcCalled = false;
        var handler = CreateYearlyVoucherHandler(
            VoucherMapping("all_stories_monthly", 3, "Month"),
            request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/rest/v1/rpc/redeem_signup_discount_code")
                {
                    rpcCalled = true;
                    return SuccessfulYearlyVoucherRedemption(DateTimeOffset.UtcNow.AddMonths(3));
                }

                return null;
            });

        var result = await CreateService(handler).ApplySignupDiscountCodeAsync("ouer@example.com", "YEARFREE", "all_stories_yearly");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(rpcCalled);
        Assert.IsEmpty(handler.PaystackDisablePayloads);
        Assert.IsEmpty(handler.SubscriptionCreatePayloads);
    }

    [TestMethod]
    public async Task ApplySignupDiscountCodeAsync_YearlySelectionHonorsAtomicRedemptionRejection()
    {
        var handler = CreateYearlyVoucherHandler(
            VoucherMapping("all_stories_monthly", 1, "Year"),
            request => request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/rest/v1/rpc/redeem_signup_discount_code"
                ? JsonResponse("""{ "success": false, "message": "Hierdie kode het sy maksimum gebruike bereik." }""")
                : null);

        var result = await CreateService(handler).ApplySignupDiscountCodeAsync("ouer@example.com", "YEARFREE", "all_stories_yearly");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("Hierdie kode het sy maksimum gebruike bereik.", result.ErrorMessage);
        Assert.IsNull(result.TierCode);
        Assert.IsEmpty(handler.SubscriptionCreatePayloads);
    }

    [TestMethod]
    public async Task ApplySignupDiscountCodeAsync_YearlySelectionPausesPaidSubscriptionAndRedeemsCanonicalTier()
    {
        string? rpcTier = null;
        var expiry = DateTimeOffset.UtcNow.AddYears(1);
        var handler = CreateYearlyVoucherHandler(
            VoucherMapping("all_stories_monthly", 1, "Year"),
            request =>
            {
                if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
                {
                    return JsonResponse(
                        """
                        [{
                          "subscription_id": "22222222-2222-2222-2222-222222222222",
                          "tier_code": "all_stories_yearly", "provider": "paystack", "source_system": "shink_app",
                          "provider_payment_id": "SUB_yearlyvoucher", "provider_email_token": "email-token",
                          "next_renewal_at": "2099-01-01T00:00:00Z", "status": "active", "billing_amount_zar": 790,
                          "billing_period_months": 12, "recurring_billing_mode": "provider_subscription"
                        }]
                        """);
                }

                if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/subscription/SUB_yearlyvoucher")
                {
                    return JsonResponse("""{ "status": true, "data": { "subscription_code": "SUB_yearlyvoucher", "email_token": "email-token" } }""");
                }

                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/subscription/disable")
                {
                    return JsonResponse("""{ "status": true }""");
                }

                if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/rest/v1/rpc/redeem_signup_discount_code")
                {
                    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    rpcTier = payload.RootElement.GetProperty("p_selected_tier_code").GetString();
                    return SuccessfulYearlyVoucherRedemption(expiry);
                }

                if (IsSupabaseGet(request, "/rest/v1/subscription_discount_code_redemptions") &&
                    request.RequestUri!.Query.Contains("order=redeemed_at.desc", StringComparison.Ordinal))
                {
                    return JsonResponse("""[{ "redemption_id": "44444444-4444-4444-4444-444444444444" }]""");
                }

                if ((request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/rest/v1/subscription_payment_pauses") ||
                    (request.Method == HttpMethod.Patch && request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions"))
                {
                    return JsonResponse("[]");
                }

                return null;
            });

        var result = await CreateService(handler).ApplySignupDiscountCodeAsync("ouer@example.com", "YEARFREE", "all_stories_yearly");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual("all_stories_monthly", rpcTier);
        Assert.AreEqual(expiry, result.AccessEndsAtUtc);
        Assert.IsTrue(result.PaymentPauseApplied);
        Assert.AreEqual(expiry, result.PauseEndsAtUtc);
        Assert.HasCount(1, handler.PaystackDisablePayloads);
        Assert.HasCount(1, handler.SubscriptionPaymentPauseCreatePayloads);
        using var pause = JsonDocument.Parse(handler.SubscriptionPaymentPauseCreatePayloads[0]);
        Assert.AreEqual("22222222-2222-2222-2222-222222222222", pause.RootElement[0].GetProperty("paid_subscription_id").GetString());
        Assert.AreEqual("all_stories_yearly", pause.RootElement[0].GetProperty("tier_code").GetString());
        Assert.IsEmpty(handler.PaystackChargePayloads);
        Assert.IsEmpty(handler.SubscriptionCreatePayloads);
    }

    private static string VoucherMapping(string tierCode, int? expirationNumber, string expirationPeriod) =>
        JsonSerializer.Serialize(new[] { new { tier_code = tierCode, expiration_number = expirationNumber, expiration_period = expirationPeriod } });

    private static HttpResponseMessage SuccessfulYearlyVoucherRedemption(DateTimeOffset expiry) =>
        JsonResponse(JsonSerializer.Serialize(new
        {
            success = true,
            tier_code = "all_stories_monthly",
            access_expires_at = expiry,
            subscription_id = "55555555-5555-5555-5555-555555555555"
        }));

    private static RecordingHandler CreateYearlyVoucherHandler(
        string mappings,
        Func<HttpRequestMessage, HttpResponseMessage?>? handleRequest = null,
        string discountKind = "free_access",
        bool bypassPayment = true) => new(request =>
    {
        var response = handleRequest?.Invoke(request);
        if (response is not null)
        {
            return response;
        }

        if (IsSupabaseGet(request, "/rest/v1/site_settings"))
        {
            return JsonResponse("""[{ "setting_value": true }]""");
        }

        if (IsSupabaseGet(request, "/rest/v1/subscribers"))
        {
            return JsonResponse("""[{ "subscriber_id": "11111111-1111-1111-1111-111111111111", "disabled_at": null }]""");
        }

        if (IsSupabaseGet(request, "/rest/v1/subscriptions") || IsSupabaseGet(request, "/rest/v1/subscription_discount_code_redemptions"))
        {
            return JsonResponse("[]");
        }

        if (IsSupabaseGet(request, "/rest/v1/subscription_discount_codes"))
        {
            return JsonResponse(JsonSerializer.Serialize(new[]
            {
                new
                {
                    discount_code_id = "33333333-3333-3333-3333-333333333333", code = "YEARFREE", normalized_code = "yearfree",
                    max_uses = 1, one_use_per_user = true, bypass_payment = bypassPayment, discount_kind = discountKind,
                    discount_percent = 25, is_active = true
                }
            }));
        }

        if (IsSupabaseGet(request, "/rest/v1/subscription_discount_code_tiers"))
        {
            return JsonResponse(mappings);
        }

        if (IsSupabaseGet(request, "/rest/v1/subscription_tiers"))
        {
            return JsonResponse("""[{ "tier_code": "all_stories_monthly", "display_name": "Alle stories" }, { "tier_code": "all_stories_yearly", "display_name": "Alle stories jaarliks" }]""");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });
}
