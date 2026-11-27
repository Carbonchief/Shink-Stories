using System.Net.Http.Json;
using System.Text.Json;
using Shink.Components.Content;

namespace Shink.Services;

public sealed partial class SupabaseSubscriptionLedgerService
{
    private static bool IsManualAttempt(string? reference) =>
        reference?.StartsWith("customer-retry-", StringComparison.Ordinal) == true;

    private static bool IsCustomerReservedRecovery(PaymentRecoveryRow recovery) =>
        IsManualAttempt(recovery.AuthorizationRetryReference) ||
        recovery.AuthorizationRetryReference?.StartsWith("card-update-retry-", StringComparison.Ordinal) == true;

    private static bool HasSubmittedRetry(PaymentRecoveryRow recovery) =>
        recovery.AuthorizationRetryStatus is "pending" or "succeeded" && recovery.AuthorizationRetryReference is { } reference &&
        (recovery.AuthorizationRetryStatus == "succeeded" || recovery.AuthorizationRetryDueAt is null ||
         reference == BuildAuthorizationRetryReference(recovery.RecoveryId, recovery.AuthorizationRetryDueAt.Value));

    private static bool TryGetRecoveryInvoicePeriod(PaystackSubscriptionLookupResult live, string email, decimal amount,
        out DateTimeOffset start, out DateTimeOffset end, out JsonElement invoice)
    {
        start = end = default;
        invoice = default;
        if (string.IsNullOrWhiteSpace(live.RawPayload)) return false;
        using var json = JsonDocument.Parse(live.RawPayload);
        var data = json.RootElement.GetProperty("data");
        if (!string.Equals(TryReadNestedString(data, "customer", "email"), email.Trim(), StringComparison.OrdinalIgnoreCase) ||
            TryReadNestedString(data, "plan", "currency") != "ZAR" ||
            !data.TryGetProperty("most_recent_invoice", out invoice) || invoice.ValueKind != JsonValueKind.Object ||
            TryReadStringAsDecimal(invoice, "amount") != amount * 100 ||
            TryParseDateTimeOffset(TryReadString(invoice, "period_start")) is not { } periodStart ||
            TryParseDateTimeOffset(TryReadString(invoice, "period_end")) is not { } periodEnd)
        {
            return false;
        }

        invoice = invoice.Clone();
        start = periodStart;
        end = periodEnd;
        return true;
    }

    private static bool TryGetFailedInvoicePeriod(PaystackSubscriptionLookupResult live, string email, decimal amount,
        out DateTimeOffset start, out DateTimeOffset end) =>
        TryGetRecoveryInvoicePeriod(live, email, amount, out start, out end, out var invoice) &&
        TryReadString(invoice, "status") == "failed" && invoice.TryGetProperty("paid", out var paid) && paid.ValueKind == JsonValueKind.False;

    private static bool IsMatchingAgreement(PaystackSubscriptionLookupResult live, PaymentRecoverySubscriptionContext subscription) =>
        live.IsSuccess && live.SubscriptionCode == subscription.ProviderPaymentId &&
        string.Equals(live.CustomerEmail, subscription.Email, StringComparison.OrdinalIgnoreCase);

    private static bool HasMatchingUnpaidInvoice(PaystackSubscriptionLookupResult live, PaymentRecoverySubscriptionContext subscription,
        PaymentRecoveryRow recovery, DateTimeOffset now) =>
        IsMatchingAgreement(live, subscription) && live.Status == "attention" && live.HasReusableCard &&
        live.NextPaymentDate > now && subscription.BillingAmountZar is > 0 &&
        TryGetFailedInvoicePeriod(live, subscription.Email, subscription.BillingAmountZar.Value, out var start, out var end) &&
        recovery.FirstFailedAt >= start && recovery.FirstFailedAt < end;

    private async Task<bool> CanResumeReservedRecoveryAsync(PaymentRecoverySubscriptionContext subscription,
        PaymentRecoveryRow recovery, PaystackSubscriptionLookupResult live, CancellationToken cancellationToken)
    {
        if (recovery.AuthorizationRetryStatus != "pending" || recovery.PaidThrough is null || recovery.PaidThrough != live.NextPaymentDate)
            return false;

        var assessment = await AssessSubscriptionCheckoutAsync(subscription.Email, null, cancellationToken);
        return assessment.PaystackRecoverySubscriptionCode == subscription.ProviderPaymentId &&
            assessment.CustomerRecovery is { AwaitingPayment: true } offer && offer.RecoveryId == recovery.RecoveryId &&
            offer.PreviousReference == recovery.AuthorizationRetryReference;
    }

    private static bool IsTerminalRetryFailure(string? status) => status is "failed" or "abandoned" or "reversed";

    private static bool MatchesRecoveryTransaction(PaystackVerifyResult transaction, string reference,
        PaymentRecoverySubscriptionContext subscription) =>
        transaction.IsSuccess && transaction.Reference == reference && subscription.BillingAmountZar > 0 &&
        transaction.AmountInCents == subscription.BillingAmountZar * 100 && transaction.Currency == "ZAR" &&
        string.Equals(transaction.CustomerEmail, subscription.Email, StringComparison.OrdinalIgnoreCase);

    private async Task<PaystackCardUpdateRefundRetryResult> SettleVerifiedRecoveryAsync(Uri baseUri, string apiKey,
        PaymentRecoverySubscriptionContext subscription, PaymentRecoveryRow recovery, PaystackSubscriptionLookupResult live,
        PaystackVerifyResult verified, CancellationToken cancellationToken)
    {
        if (live.IsSuccess && (!IsMatchingAgreement(live, subscription) || live.Status is not ("active" or "attention")))
            return PaystackCardUpdateRefundRetryResult.Failed("The provider agreement changed before settlement.");
        var next = recovery.PaidThrough;
        if (next is null && IsMatchingAgreement(live, subscription) && live.Status is "active" or "attention" &&
            TryGetRecoveryInvoicePeriod(live, subscription.Email, subscription.BillingAmountZar!.Value,
                out var start, out var end, out _) && recovery.FirstFailedAt >= start && recovery.FirstFailedAt < end)
            next = live.NextPaymentDate;
        if (verified.PaidAt is null || recovery.FirstFailedAt is null || verified.PaidAt < recovery.FirstFailedAt ||
            next is null || verified.PaidAt >= next || next <= DateTimeOffset.UtcNow ||
            !await CompleteCustomerPaymentAsync(baseUri, apiKey, subscription, recovery.RecoveryId, verified.Reference!, next.Value, cancellationToken))
            return PaystackCardUpdateRefundRetryResult.Failed("The verified payment could not be applied to its renewal.", subscription.SubscriptionId);
        return new PaystackCardUpdateRefundRetryResult(subscription.SubscriptionId, subscription.ProviderPaymentId, PaymentRecovered: true);
    }

    private async Task<PaystackCardUpdateRefundRetryResult> ApplyVerifiedAttemptAsync(Uri baseUri, string apiKey,
        PaymentRecoverySubscriptionContext subscription, PaymentRecoveryRow recovery, PaystackSubscriptionLookupResult live,
        string reference, PaystackVerifyResult verified, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (MatchesRecoveryTransaction(verified, reference, subscription))
        {
            if (IsSuccessfulPaystackStatus(verified.TransactionStatus))
                return await SettleVerifiedRecoveryAsync(baseUri, apiKey, subscription, recovery, live, verified, cancellationToken);
            if (IsTerminalRetryFailure(verified.TransactionStatus))
            {
                if (reference.StartsWith("retry-", StringComparison.Ordinal))
                    await SchedulePaymentRecoveryEmailsAsync(baseUri, apiKey, recovery, subscription, "paystack",
                        subscription.ProviderPaymentId!, now, "failed", reference, verified.GatewayResponse, cancellationToken);
                else
                    await MarkPaymentRecoveryAuthorizationRetryAsync(baseUri, apiKey, recovery.RecoveryId, now, "failed",
                        reference, verified.GatewayResponse, null, cancellationToken, requireSuccess: true, expectedReference: reference);
                return new PaystackCardUpdateRefundRetryResult(subscription.SubscriptionId, subscription.ProviderPaymentId);
            }
        }
        if (verified.IsSuccess && IsSuccessfulPaystackStatus(verified.TransactionStatus))
            return PaystackCardUpdateRefundRetryResult.Failed("The verified amount, currency, owner or reference did not match the renewal.");
        return new PaystackCardUpdateRefundRetryResult(subscription.SubscriptionId, subscription.ProviderPaymentId, PendingReason: CardUpdatePaymentPending);
    }

    private async Task<PaystackCardUpdateRefundRetryResult?> ReconcileReservedAttemptAsync(Uri baseUri, string apiKey,
        PaymentRecoverySubscriptionContext subscription, PaymentRecoveryRow recovery, PaystackSubscriptionLookupResult live,
        DateTimeOffset now, CancellationToken cancellationToken, bool allowMissing = false)
    {
        var reference = recovery.AuthorizationRetryReference!;
        var verified = await _paystackCheckoutService.VerifyTransactionAsync(reference, cancellationToken);
        if (allowMissing && verified.ReferenceNotFound) return null;
        return await ApplyVerifiedAttemptAsync(baseUri, apiKey, subscription, recovery, live, reference, verified, now, cancellationToken);
    }

    private async Task<bool> ReconcileRetryWebhookAsync(Uri baseUri, string apiKey, PaymentRecoverySubscriptionContext subscription,
        string reference, JsonElement data, CancellationToken cancellationToken)
    {
        if (subscription.CancelledAt is not null || subscription.Status is not ("failed" or "active") ||
            TryReadNestedString(data, "metadata", "provider_payment_id") != subscription.ProviderPaymentId ||
            !IsSuccessfulPaystackStatus(TryReadString(data, "status")) || TryReadString(data, "currency") != "ZAR" ||
            !string.Equals(TryReadNestedString(data, "customer", "email"), subscription.Email, StringComparison.OrdinalIgnoreCase) ||
            subscription.BillingAmountZar is not > 0 || TryReadStringAsDecimal(data, "amount") != subscription.BillingAmountZar * 100)
            return false;
        var recovery = await GetActivePaymentRecoveryAsync(baseUri, apiKey, subscription.SubscriptionId, cancellationToken,
            requireSuccess: true, includeSuspended: true, includeRecovered: true);
        var eventRecovery = TryReadNestedString(data, "metadata", "recovery_id");
        if (recovery is null || recovery.ProviderPaymentId != subscription.ProviderPaymentId ||
            eventRecovery is not null && eventRecovery != recovery.RecoveryId ||
            recovery.AuthorizationRetryReference != reference) return false;
        if (recovery.Resolution == "recovered") return recovery.AuthorizationRetryStatus == "succeeded";
        var live = await _paystackCheckoutService.GetSubscriptionAsync(subscription.ProviderPaymentId!, cancellationToken);
        var result = await ReconcileReservedAttemptAsync(baseUri, apiKey, subscription, recovery, live, DateTimeOffset.UtcNow, cancellationToken);
        return result?.PaymentRecovered == true;
    }

    private async Task<bool> TryReconcilePaidInvoiceAsync(Uri baseUri, string apiKey, PaymentRecoverySubscriptionContext subscription,
        PaymentRecoveryRow recovery, PaystackSubscriptionLookupResult live, CancellationToken cancellationToken)
    {
        if (!TryGetRecoveryInvoicePeriod(live, subscription.Email, subscription.BillingAmountZar!.Value,
                out var start, out var end, out var invoice) || recovery.FirstFailedAt < start || recovery.FirstFailedAt >= end ||
            !IsSuccessfulPaystackStatus(TryReadString(invoice, "status")) ||
            !invoice.TryGetProperty("paid", out var paid) || paid.ValueKind != JsonValueKind.True ||
            TryReadNestedString(invoice, "transaction", "reference") is not { Length: > 0 } reference) return false;
        var verified = await _paystackCheckoutService.VerifyTransactionAsync(reference, cancellationToken);
        if (MatchesRecoveryTransaction(verified, reference, subscription) && IsSuccessfulPaystackStatus(verified.TransactionStatus) &&
            verified.PaidAt >= recovery.FirstFailedAt && live.NextPaymentDate > DateTimeOffset.UtcNow)
        {
            if (recovery.AuthorizationRetryReference is { } prior && prior != reference)
            {
                var previous = await _paystackCheckoutService.VerifyTransactionAsync(prior, cancellationToken);
                if (!MatchesRecoveryTransaction(previous, prior, subscription) || !IsTerminalRetryFailure(previous.TransactionStatus)) return true;
            }
            await RecordVerifiedRecoveryReceiptAsync(baseUri, apiKey, subscription, recovery, verified,
                "paystack.verified_recovery_invoice", cancellationToken);
            await SettleVerifiedRecoveryAsync(baseUri, apiKey, subscription, recovery, live, verified, cancellationToken);
        }
        // A paid invoice must never generate another charge, even if verification needs a retry.
        return true;
    }

    public async Task<SubscriptionRepairResult> RetryCustomerPaymentAsync(string email, CustomerPaymentRecovery offer,
        CancellationToken cancellationToken = default)
    {
        var pending = new SubscriptionRepairResult(false, IsPending: true);
        var unavailable = new SubscriptionRepairResult(false, ErrorMessage: "Ons kon die betaling nie begin nie. Verfris die blad en probeer weer.");
        var assessment = await AssessSubscriptionCheckoutAsync(email, null, cancellationToken);
        if (assessment.CustomerRecovery != offer || assessment.PaystackRecoverySubscriptionCode is not { } code ||
            !_paystackCheckoutService.CanChargeAuthorization || !TryBuildSupabaseBaseUri(out var baseUri) ||
            ResolveApiKey() is not { Length: > 0 } apiKey)
        {
            return unavailable;
        }

        var subscription = await TryGetSubscriptionContextByIdAsync(baseUri, apiKey, offer.SubscriptionId, cancellationToken);
        if (subscription is null || subscription.CancelledAt is not null || subscription.Status is not ("active" or "failed") ||
            subscription.BillingAmountZar != offer.AmountZar || subscription.ProviderPaymentId != code ||
            !string.Equals(subscription.Email, email, StringComparison.OrdinalIgnoreCase) ||
            PaymentPlanCatalog.FindByTierCode(subscription.TierCode) is not { } plan)
        {
            return unavailable;
        }

        var recovery = await GetActivePaymentRecoveryAsync(baseUri, apiKey, offer.SubscriptionId, cancellationToken,
            requireSuccess: true, includeSuspended: true);
        if (recovery?.RecoveryId != offer.RecoveryId || recovery.AuthorizationRetryReference != offer.PreviousReference)
        {
            return pending;
        }

        var live = await _paystackCheckoutService.GetSubscriptionAsync(code, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (offer.AwaitingPayment && recovery.AuthorizationRetryReference is not null)
        {
            var checkedPayment = await ReconcileReservedAttemptAsync(baseUri, apiKey, subscription, recovery, live, now, cancellationToken);
            if (checkedPayment?.PaymentRecovered == true) return new SubscriptionRepairResult(true);
            return checkedPayment?.PendingReason is not null || checkedPayment?.ShouldFailWebhook == true ? pending :
                new SubscriptionRepairResult(false, ErrorMessage: "Die betaling het nie geslaag nie. Werk jou kaartbesonderhede by of probeer weer.");
        }
        if (!live.IsSuccess || live.SubscriptionCode != code || live.Status != "attention" || live.NextPaymentDate <= now ||
            live.NextPaymentDate is null ||
            (!offer.AwaitingPayment && !live.HasReusableCard) ||
            !TryGetFailedInvoicePeriod(live, email, offer.AmountZar, out var start, out var end) ||
            recovery.FirstFailedAt is null || recovery.FirstFailedAt < start || recovery.FirstFailedAt >= end)
        {
            return unavailable;
        }

        // A repeated submission for the same outstanding attempt must verify the same reference.
        var attemptKey = $"{offer.RecoveryId}:{offer.PreviousReference}";
        var result = await CollectRecoveryPaymentAsync(baseUri, apiKey, subscription, code, live, recovery, plan,
            live.AuthorizationCode ?? string.Empty, "customer_retry", attemptKey, null, now, cancellationToken);
        if (result.PaymentRecovered) return new SubscriptionRepairResult(true);
        if (result.PendingReason is not null || result.ShouldFailWebhook) return pending;
        return new SubscriptionRepairResult(false, ErrorMessage: "Die betaling het nie geslaag nie. Probeer weer wanneer daar fondse beskikbaar is, of werk jou kaartbesonderhede by.");
    }

    private async Task<bool> CompleteCustomerPaymentAsync(Uri baseUri, string apiKey, PaymentRecoverySubscriptionContext subscription,
        string recoveryId, string reference, DateTimeOffset nextPayment, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Post, new Uri(baseUri, "rest/v1/rpc/complete_customer_payment_recovery"), apiKey,
            new { p_recovery_id = recoveryId, p_subscription_id = subscription.SubscriptionId, p_reference = reference,
                p_amount_zar = subscription.BillingAmountZar, p_email = subscription.Email,
                p_provider_payment_id = subscription.ProviderPaymentId, p_next_payment = nextPayment }, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (!await response.Content.ReadFromJsonAsync<bool>(cancellationToken)) return false;

        // A queued charge can complete after the customer has closed the browser.
        // Cancel any outstanding reminders on both the webhook and interactive paths.
        var notices = new Uri(baseUri, "rest/v1/subscription_payment_recoveries?select=immediate_email_id,warning_email_id,suspension_email_id" +
            $"&recovery_id=eq.{Uri.EscapeDataString(recoveryId)}&limit=1");
        using var read = CreateRequest(HttpMethod.Get, notices, apiKey);
        using var stored = await _httpClient.SendAsync(read, cancellationToken);
        stored.EnsureSuccessStatusCode();
        var rows = await stored.Content.ReadFromJsonAsync<List<PaymentRecoveryRow>>(cancellationToken);
        if (rows?.SingleOrDefault() is { } row)
            await _subscriptionPaymentRecoveryEmailService.CancelSequenceAsync(
                new SubscriptionPaymentRecoveryEmailSequence(row.ImmediateEmailId, row.WarningEmailId, row.SuspensionEmailId), cancellationToken);
        return true;
    }

    private Task<EventInsertResult> RecordVerifiedRecoveryReceiptAsync(Uri baseUri, string apiKey,
        PaymentRecoverySubscriptionContext subscription, PaymentRecoveryRow recovery, PaystackVerifyResult verified,
        string eventType, CancellationToken cancellationToken) =>
        InsertSubscriptionEventAsync(baseUri, apiKey, subscription.SubscriptionId, "paystack", subscription.ProviderPaymentId!,
            verified.ProviderTransactionId ?? verified.Reference, eventType, "success",
            new { reference = verified.Reference, recovery_id = recovery.RecoveryId, previous_reference = recovery.AuthorizationRetryReference },
            cancellationToken);

    // Older recoveries could lose their queued reference when an unconfigured worker recorded a later failure.
    private async Task<PaystackCardUpdateRefundRetryResult?> CheckHistoricalRecoveryPaymentsAsync(Uri baseUri, string apiKey,
        PaymentRecoverySubscriptionContext subscription, PaymentRecoveryRow recovery, DateTimeOffset? nextPayment, CancellationToken cancellationToken)
    {
        var pending = new PaystackCardUpdateRefundRetryResult(subscription.SubscriptionId, subscription.ProviderPaymentId, PendingReason: CardUpdatePaymentPending);
        if (recovery.FirstFailedAt is null || recovery.SubscriptionId is null) return pending;
        var uri = new Uri(baseUri, "rest/v1/subscription_events?select=payload,event_status,provider_transaction_id" +
            $"&subscription_id=eq.{Uri.EscapeDataString(recovery.SubscriptionId)}&received_at=gte.{Uri.EscapeDataString(recovery.FirstFailedAt.Value.ToString("O"))}" +
            "&event_type=in.(paystack.authorization_retry,paystack.card_update_retry,paystack.card_update_refund_retry,charge.success)&limit=100");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var attempts = await response.Content.ReadFromJsonAsync<List<AccountRepairEventRow>>(cancellationToken);
        if (attempts is null || attempts.Count >= 100) return pending;
        foreach (var attempt in attempts)
        {
            if (TryReadNestedString(attempt.Payload, "data", "metadata", "transaction_type") == "update_card")
                continue;
            var reference = TryReadNestedString(attempt.Payload, "data", "reference") ??
                TryReadString(attempt.Payload, "retry_reference") ?? TryReadString(attempt.Payload, "reference");
            if (string.IsNullOrEmpty(reference))
            {
                // Configuration failures were recorded without submitting a provider request.
                if (attempt.EventStatus == "failed" && string.IsNullOrEmpty(attempt.ProviderTransactionId)) continue;
                return pending;
            }
            if (reference == recovery.AuthorizationRetryReference) continue; // Already verified as declined.
            var verified = await _paystackCheckoutService.VerifyTransactionAsync(reference, cancellationToken);
            if (!verified.IsSuccess || verified.Reference != reference) return pending;
            if (verified.TransactionStatus is "failed" or "abandoned" or "reversed") continue;
            if (!IsSuccessfulPaystackStatus(verified.TransactionStatus) || verified.PaidAt is null ||
                verified.PaidAt < recovery.FirstFailedAt || nextPayment is null || verified.PaidAt >= nextPayment || nextPayment <= DateTimeOffset.UtcNow ||
                verified.AmountInCents != subscription.BillingAmountZar * 100 || verified.Currency != "ZAR" ||
                !string.Equals(verified.CustomerEmail, subscription.Email, StringComparison.OrdinalIgnoreCase))
                return pending;
            if (recovery.AuthorizationRetryReference is not null)
            {
                await RecordVerifiedRecoveryReceiptAsync(baseUri, apiKey, subscription, recovery, verified,
                    "paystack.verified_recovery_payment", cancellationToken);
            }
            if (await CompleteCustomerPaymentAsync(baseUri, apiKey, subscription, recovery.RecoveryId, reference,
                    nextPayment.Value, cancellationToken))
                return new PaystackCardUpdateRefundRetryResult(subscription.SubscriptionId, subscription.ProviderPaymentId, PaymentRecovered: true);
            return pending;
        }
        return null;
    }
}
