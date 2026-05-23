using System.Text.Json.Serialization;
using Shink.Components.Content;

namespace Shink.Services;

public sealed partial class SupabaseSubscriptionLedgerService
{
    public async Task ProcessPaystackAuthorizationScheduleAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var dueRows = await FetchDuePaystackAuthorizationScheduleRowsAsync(baseUri, apiKey, nowUtc, cancellationToken);
        foreach (var row in dueRows)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ProcessPaystackAuthorizationScheduleRowAsync(baseUri, apiKey, row, nowUtc, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<PaystackAuthorizationScheduleRow>> FetchDuePaystackAuthorizationScheduleRowsAsync(
        Uri baseUri,
        string apiKey,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscriptions" +
            "?select=subscription_id,subscriber_id,tier_code,provider,provider_payment_id,provider_transaction_id,provider_token,source_system,status,next_renewal_at,billing_amount_zar,billing_period_months,billing_amount_source,discount_code_id,discount_percent,discount_duration,discount_payment_count,discount_payments_used,undiscounted_billing_amount_zar,authorization_reusable" +
            "&recurring_billing_mode=eq.paystack_authorization_schedule" +
            "&provider=eq.paystack" +
            "&status=eq.active" +
            "&cancelled_at=is.null" +
            $"&next_renewal_at=lte.{Uri.EscapeDataString(nowUtc.UtcDateTime.ToString("O"))}" +
            "&order=next_renewal_at.asc" +
            "&limit=25");

        return await FetchRowsAsync<PaystackAuthorizationScheduleRow>(uri, apiKey, cancellationToken);
    }

    private async Task ProcessPaystackAuthorizationScheduleRowAsync(
        Uri baseUri,
        string apiKey,
        PaystackAuthorizationScheduleRow row,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.SubscriptionId) ||
            string.IsNullOrWhiteSpace(row.SubscriberId) ||
            string.IsNullOrWhiteSpace(row.TierCode) ||
            string.IsNullOrWhiteSpace(row.ProviderToken) ||
            row.AuthorizationReusable != true)
        {
            return;
        }

        var plan = PaymentPlanCatalog.FindByTierCode(row.TierCode);
        var email = await GetSubscriberEmailByIdAsync(baseUri, apiKey, row.SubscriberId, cancellationToken);
        if (plan is null || string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var amount = ResolveScheduledAuthorizationAmount(row, out var discountApplied);
        if (amount <= 0m)
        {
            return;
        }

        var reference = BuildScheduledAuthorizationReference(row.SubscriptionId, row.NextRenewalAt ?? nowUtc);
        if (!await TryInsertRecurringChargeAttemptAsync(baseUri, apiKey, row.SubscriptionId, reference, row.NextRenewalAt ?? nowUtc, amount, discountApplied, cancellationToken))
        {
            return;
        }

        var chargeResult = await _paystackCheckoutService.ChargeAuthorizationAsync(
            plan,
            email,
            row.ProviderToken,
            reference,
            row.SubscriptionId,
            row.ProviderPaymentId,
            billingAmountZarOverride: amount,
            cancellationToken: cancellationToken,
            metadataOverrides: new Dictionary<string, object?>
            {
                ["source"] = "subscription_discount_recurring",
                ["recurring_billing_mode"] = SubscriptionRecurringBillingModes.PaystackAuthorizationSchedule,
                ["discount_code_id"] = row.DiscountCodeId,
                ["discount_percent"] = row.DiscountPercent,
                ["discount_duration"] = row.DiscountDuration,
                ["discount_payment_count"] = row.DiscountPaymentCount,
                ["discount_payments_used"] = row.DiscountPaymentsUsed,
                ["discount_applied"] = discountApplied
            });

        if (chargeResult.IsSuccess)
        {
            var paidAtUtc = chargeResult.PaidAt ?? nowUtc;
            var nextRenewalAtUtc = paidAtUtc.AddMonths(Math.Max(1, row.BillingPeriodMonths ?? plan.BillingPeriodMonths));
            var nextUsedCount = discountApplied
                ? row.DiscountPaymentsUsed + 1
                : row.DiscountPaymentsUsed;
            var nextAmount = ResolveScheduledAuthorizationAmount(row with { DiscountPaymentsUsed = nextUsedCount }, out _);

            await MarkRecurringChargeAttemptAsync(
                baseUri,
                apiKey,
                reference,
                "success",
                chargeResult.ProviderTransactionId,
                null,
                chargeResult.RawPayload,
                cancellationToken);
            await InsertSubscriptionEventAsync(
                baseUri,
                apiKey,
                row.SubscriptionId,
                "paystack",
                row.ProviderPaymentId,
                chargeResult.ProviderTransactionId ?? chargeResult.Reference,
                "charge.success",
                chargeResult.TransactionStatus ?? "success",
                DeserializePayloadObject(chargeResult.RawPayload ?? "{}"),
                cancellationToken);
            await UpdatePaystackAuthorizationScheduleAfterChargeAsync(
                baseUri,
                apiKey,
                row.SubscriptionId,
                nextRenewalAtUtc,
                nextAmount,
                nextUsedCount,
                chargeResult.ProviderTransactionId,
                cancellationToken);
            return;
        }

        await MarkRecurringChargeAttemptAsync(
            baseUri,
            apiKey,
            reference,
            "failed",
            chargeResult.ProviderTransactionId,
            chargeResult.ErrorMessage,
            chargeResult.RawPayload,
            cancellationToken);
        await InsertSubscriptionEventAsync(
            baseUri,
            apiKey,
            row.SubscriptionId,
            "paystack",
            row.ProviderPaymentId,
            chargeResult.ProviderTransactionId ?? chargeResult.Reference,
            "charge.failed",
            chargeResult.TransactionStatus ?? "failed",
            DeserializePayloadObject(chargeResult.RawPayload ?? "{}"),
            cancellationToken);

        var recoveryContext = await TryGetSubscriptionContextByIdAsync(baseUri, apiKey, row.SubscriptionId, cancellationToken);
        if (recoveryContext is not null)
        {
            await TryStartPaymentRecoveryAsync(
                baseUri,
                apiKey,
                recoveryContext,
                reference,
                "paystack",
                nowUtc,
                cancellationToken);
        }
    }

    private static decimal ResolveScheduledAuthorizationAmount(PaystackAuthorizationScheduleRow row, out bool discountApplied)
    {
        var originalAmount = decimal.Round(row.UndiscountedBillingAmountZar ?? row.BillingAmountZar ?? 0m, 2, MidpointRounding.AwayFromZero);
        var discountPercent = row.DiscountPercent ?? 0m;
        var duration = string.Equals(row.DiscountDuration, SubscriptionDiscountDurations.FirstPayments, StringComparison.OrdinalIgnoreCase)
            ? SubscriptionDiscountDurations.FirstPayments
            : SubscriptionDiscountDurations.Lifetime;
        discountApplied = duration switch
        {
            SubscriptionDiscountDurations.FirstPayments => row.DiscountPaymentCount is int paymentCount && row.DiscountPaymentsUsed < paymentCount,
            _ => discountPercent > 0m
        };

        return discountApplied
            ? CalculateDiscountedAmount(originalAmount, discountPercent)
            : originalAmount;
    }

    private static string BuildScheduledAuthorizationReference(string subscriptionId, DateTimeOffset dueAtUtc)
    {
        var compactSubscriptionId = subscriptionId.Replace("-", string.Empty, StringComparison.Ordinal);
        if (compactSubscriptionId.Length > 16)
        {
            compactSubscriptionId = compactSubscriptionId[..16];
        }

        return $"discount-recurring-{compactSubscriptionId}-{dueAtUtc:yyyyMMddHHmmss}";
    }

    private async Task<bool> TryInsertRecurringChargeAttemptAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        string reference,
        DateTimeOffset dueAtUtc,
        decimal amountZar,
        bool discountApplied,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscription_id = subscriptionId,
            provider = "paystack",
            reference,
            due_at = dueAtUtc.UtcDateTime,
            amount_zar = amountZar,
            discount_applied = discountApplied,
            status = "pending"
        };

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/subscription_recurring_charge_attempts"),
            apiKey,
            payload,
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task MarkRecurringChargeAttemptAsync(
        Uri baseUri,
        string apiKey,
        string reference,
        string status,
        string? providerTransactionId,
        string? errorMessage,
        string? rawPayload,
        CancellationToken cancellationToken)
    {
        var escapedReference = Uri.EscapeDataString(reference);
        var payload = new
        {
            status,
            provider_transaction_id = string.IsNullOrWhiteSpace(providerTransactionId) ? null : providerTransactionId,
            error_message = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage,
            payload = string.IsNullOrWhiteSpace(rawPayload) ? null : DeserializePayloadObject(rawPayload)
        };
        using var request = CreateJsonRequest(
            new HttpMethod("PATCH"),
            new Uri(baseUri, $"rest/v1/subscription_recurring_charge_attempts?reference=eq.{escapedReference}"),
            apiKey,
            payload,
            "return=minimal");
        using var _ = await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task UpdatePaystackAuthorizationScheduleAfterChargeAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        DateTimeOffset nextRenewalAtUtc,
        decimal nextBillingAmountZar,
        int discountPaymentsUsed,
        string? providerTransactionId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriptionId = Uri.EscapeDataString(subscriptionId);
        var payload = new
        {
            next_renewal_at = nextRenewalAtUtc.UtcDateTime,
            billing_amount_zar = nextBillingAmountZar,
            billing_amount_source = SubscriptionRecurringBillingModes.PaystackAuthorizationSchedule,
            discount_payments_used = discountPaymentsUsed,
            provider_transaction_id = string.IsNullOrWhiteSpace(providerTransactionId) ? null : providerTransactionId
        };
        using var request = CreateJsonRequest(
            new HttpMethod("PATCH"),
            new Uri(baseUri, $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}"),
            apiKey,
            payload,
            "return=minimal");
        using var _ = await _httpClient.SendAsync(request, cancellationToken);
    }

    private sealed record PaystackAuthorizationScheduleRow
    {
        [JsonPropertyName("subscription_id")]
        public string SubscriptionId { get; init; } = string.Empty;

        [JsonPropertyName("subscriber_id")]
        public string SubscriberId { get; init; } = string.Empty;

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; init; }

        [JsonPropertyName("provider")]
        public string? Provider { get; init; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; init; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; init; }

        [JsonPropertyName("provider_token")]
        public string? ProviderToken { get; init; }

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; init; }

        [JsonPropertyName("billing_amount_zar")]
        public decimal? BillingAmountZar { get; init; }

        [JsonPropertyName("billing_period_months")]
        public int? BillingPeriodMonths { get; init; }

        [JsonPropertyName("billing_amount_source")]
        public string? BillingAmountSource { get; init; }

        [JsonPropertyName("discount_code_id")]
        public string? DiscountCodeId { get; init; }

        [JsonPropertyName("discount_percent")]
        public decimal? DiscountPercent { get; init; }

        [JsonPropertyName("discount_duration")]
        public string? DiscountDuration { get; init; }

        [JsonPropertyName("discount_payment_count")]
        public int? DiscountPaymentCount { get; init; }

        [JsonPropertyName("discount_payments_used")]
        public int DiscountPaymentsUsed { get; init; }

        [JsonPropertyName("undiscounted_billing_amount_zar")]
        public decimal? UndiscountedBillingAmountZar { get; init; }

        [JsonPropertyName("authorization_reusable")]
        public bool? AuthorizationReusable { get; init; }
    }
}
