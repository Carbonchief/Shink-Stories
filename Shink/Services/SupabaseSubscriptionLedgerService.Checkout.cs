using System.Text.Json;

namespace Shink.Services;

public sealed partial class SupabaseSubscriptionLedgerService
{
    private async Task<CheckoutSubscriptionSnapshot?> ReadSubscriptionCheckoutAsync(
        string? email,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await TryResolveSelfServiceContextAsync(email, cancellationToken, requireSuccessfulLookup: true);
            if (context is null || context.DisabledAt is not null)
            {
                return new CheckoutSubscriptionSnapshot(context, []);
            }

            const int limit = 100;
            var uri = new Uri(context.BaseUri,
                "rest/v1/subscriptions?select=subscription_id,tier_code,provider,source_system,provider_payment_id,provider_transaction_id,provider_token,next_renewal_at,cancelled_at,status,recurring_billing_mode" +
                $"&subscriber_id=eq.{Uri.EscapeDataString(context.SubscriberId)}&tier_code=neq.gratis" +
                $"&or=(status.eq.active,provider.eq.paystack)&order=subscribed_at.desc&limit={limit}");
            using var request = CreateRequest(HttpMethod.Get, uri, context.ApiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<SelfServiceSubscriptionRow>>(stream, cancellationToken: cancellationToken);
            if (rows is null || rows.Count >= limit || rows.Any(row => row is null ||
                    string.IsNullOrWhiteSpace(row.SubscriptionId) || string.IsNullOrWhiteSpace(row.Provider) ||
                    string.IsNullOrWhiteSpace(row.TierCode) || string.IsNullOrWhiteSpace(row.Status)))
            {
                throw new JsonException("Incomplete subscription history for checkout.");
            }

            return new CheckoutSubscriptionSnapshot(
                context, rows.Where(row => !string.Equals(row.TierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        catch (Exception exception) when (IsCheckoutLookupFailure(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Could not read subscription billing history before checkout.");
            return null;
        }
    }

    private async Task<SubscriptionCheckoutAssessment?> GetPaystackCheckoutBlockAsync(
        SelfServiceSubscriptionContext context,
        IReadOnlyList<SelfServiceSubscriptionRow> subscriptions,
        string? planChangeSubscriptionCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var uncertain = new SubscriptionCheckoutAssessment(SubscriptionCheckoutTransitionKinds.BillingVerificationRequired, "paystack");
        var checkedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renewingAgreements = 0;
        SubscriptionCheckoutAssessment? recovery = null;
        var hasStoppedAgreement = false;
        try
        {
            foreach (var row in subscriptions.Where(row => string.Equals(row.Provider, "paystack", StringComparison.OrdinalIgnoreCase)))
            {
                // These payments are scheduled by Schink, so there is no Paystack agreement to look up.
                if (string.Equals(row.RecurringBillingMode, SubscriptionRecurringBillingModes.PaystackAuthorizationSchedule, StringComparison.Ordinal))
                {
                    if (IsCurrentlyBillableSelfServiceSubscription(row, nowUtc))
                    {
                        return uncertain;
                    }

                    continue;
                }

                var code = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
                    row.Provider, row.SourceSystem, row.ProviderPaymentId, row.ProviderTransactionId);
                if (code is null)
                {
                    if (row.SourceSystem is "admin_override" or "discount_code")
                    {
                        continue;
                    }

                    return uncertain;
                }

                if (!checkedCodes.Add(code))
                {
                    continue;
                }

                // Local failure and cancellation can both leave an agreement open at Paystack.
                var live = await _paystackCheckoutService.GetSubscriptionAsync(code, cancellationToken);
                if (!live.IsSuccess || !string.Equals(live.SubscriptionCode, code, StringComparison.OrdinalIgnoreCase))
                {
                    return uncertain;
                }

                if (IsNonRenewingPaystackStatus(live.Status))
                {
                    hasStoppedAgreement = true;
                    continue;
                }

                if (live.Status?.Trim().ToLowerInvariant() is not ("active" or "attention"))
                {
                    return uncertain;
                }

                // Count the current agreement too before offering recovery for another one.
                renewingAgreements++;
                if (renewingAgreements > 1)
                {
                    return uncertain;
                }

                if (string.Equals(code, planChangeSubscriptionCode, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // The card-update webhook requires an active, uncancelled row linked by this code.
                if (!string.Equals(row.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                    row.CancelledAt is not null ||
                    !string.Equals(row.ProviderPaymentId, code, StringComparison.OrdinalIgnoreCase))
                {
                    return uncertain;
                }

                recovery = new SubscriptionCheckoutAssessment(
                    SubscriptionCheckoutTransitionKinds.PaystackRecoveryRequired, "paystack", row.TierCode)
                {
                    PaystackRecoverySubscriptionCode = code
                };
            }

            // A promotional pause looks non-renewing at Paystack until Schink resumes it.
            if ((hasStoppedAgreement || planChangeSubscriptionCode is not null) &&
                await HasPendingPaystackResumeAsync(context, cancellationToken))
            {
                return uncertain;
            }
        }
        catch (Exception exception) when (IsCheckoutLookupFailure(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Could not verify an existing Paystack agreement before checkout.");
            return uncertain;
        }

        return recovery;
    }

    private async Task<bool> HasPendingPaystackResumeAsync(
        SelfServiceSubscriptionContext context,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(context.BaseUri,
            $"rest/v1/subscription_payment_pauses?select=pause_id&subscriber_id=eq.{Uri.EscapeDataString(context.SubscriberId)}" +
            "&provider=eq.paystack&status=in.(active,resume_pending)&limit=1");
        using var request = CreateRequest(HttpMethod.Get, uri, context.ApiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var pauses = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream, cancellationToken: cancellationToken);
        return pauses is null || pauses.Count != 0;
    }

    public async Task<SubscriptionCardUpdateLinkResult> CreatePaystackCheckoutRecoveryLinkAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        const string errorMessage = "Ons kon nie jou bestaande Paystack-intekening oopmaak nie. Probeer weer of kontak ons vir hulp.";
        var assessment = await AssessSubscriptionCheckoutAsync(email, targetTierCode: null, cancellationToken);
        if (assessment.PaystackRecoverySubscriptionCode is not { } code)
        {
            return new SubscriptionCardUpdateLinkResult(false, ErrorMessage: errorMessage);
        }

        try
        {
            var link = await _paystackCheckoutService.GenerateSubscriptionUpdateLinkAsync(code, cancellationToken);
            if (link.IsSuccess && Uri.TryCreate(link.Link, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps && (uri.Host is "paystack.com" or "www.paystack.com") &&
                uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
                uri.AbsolutePath.StartsWith("/manage/subscriptions/", StringComparison.Ordinal))
            {
                return new SubscriptionCardUpdateLinkResult(true, Link: link.Link);
            }
        }
        catch (Exception exception) when (IsCheckoutLookupFailure(exception, cancellationToken))
        {
            _logger.LogWarning(exception, "Could not open the existing Paystack agreement for checkout recovery.");
        }

        return new SubscriptionCardUpdateLinkResult(false, ErrorMessage: errorMessage);
    }

    private static bool IsCheckoutLookupFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException ||
        (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record CheckoutSubscriptionSnapshot(
        SelfServiceSubscriptionContext? Context,
        IReadOnlyList<SelfServiceSubscriptionRow> Subscriptions);
}
