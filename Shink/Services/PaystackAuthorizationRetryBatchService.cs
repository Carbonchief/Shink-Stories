using Microsoft.Extensions.Options;
using Shink.Components.Content;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shink.Services;

public sealed class PaystackAuthorizationRetryBatchService(
    HttpClient httpClient,
    PaystackCheckoutService paystackCheckoutService,
    IOptions<SupabaseOptions> supabaseOptions,
    ILogger<PaystackAuthorizationRetryBatchService> logger)
{
    private static readonly TimeSpan MinimumPaystackInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan VerificationDelay = TimeSpan.FromSeconds(6);

    private readonly HttpClient _httpClient = httpClient;
    private readonly PaystackCheckoutService _paystackCheckoutService = paystackCheckoutService;
    private readonly SupabaseOptions _supabaseOptions = supabaseOptions.Value;
    private readonly ILogger<PaystackAuthorizationRetryBatchService> _logger = logger;

    private DateTimeOffset _lastPaystackCallAtUtc = DateTimeOffset.MinValue;

    public async Task<PaystackAuthorizationRetryBatchResult> RetryEligibleSubscriptionsAsync(
        int? maxAttempts = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri) || string.IsNullOrWhiteSpace(_supabaseOptions.SecretKey))
        {
            return new PaystackAuthorizationRetryBatchResult(
                TotalProblematicChargeKeys: 0,
                RetryReadyCandidates: 0,
                AttemptedCount: 0,
                SucceededCount: 0,
                FailedCount: 0,
                SkippedRecentRetryCount: 0,
                SkippedMissingTokenCount: 0,
                SkippedNotActionableLiveCount: 0,
                SkippedMissingEmailOrPlanCount: 0,
                Errors: ["Supabase SecretKey or URL is not configured."],
                Attempts: []);
        }

        var errors = new List<string>();
        var nowUtc = DateTimeOffset.UtcNow;
        var cutoffUtc = nowUtc.AddHours(-24);

        var subscriptions = await FetchNonCancelledPaystackSubscriptionsAsync(baseUri, cancellationToken);
        var problematicChargeKeys = subscriptions
            .Where(IsLocallyProblematic)
            .Select(BuildChargeKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recentRetryByChargeKey = await FetchRecentRetryByChargeKeyAsync(baseUri, cutoffUtc, cancellationToken);
        var subscribersById = await FetchSubscribersByIdAsync(
            baseUri,
            subscriptions.Select(item => item.SubscriberId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            cancellationToken);

        var preferredCandidates = subscriptions
            .Where(item => problematicChargeKeys.Contains(BuildChargeKey(item)))
            .GroupBy(BuildChargeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => string.Equals(item.SourceSystem, "shink_app", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(item => item.UpdatedAt ?? DateTimeOffset.MinValue)
                .First())
            .ToList();

        var skippedRecentRetryCount = 0;
        var skippedMissingTokenCount = 0;
        var skippedMissingEmailOrPlanCount = 0;
        var retryReadyCandidates = 0;
        var executableCandidates = new List<RetryCandidate>();

        foreach (var candidate in preferredCandidates)
        {
            var chargeKey = BuildChargeKey(candidate);
            if (recentRetryByChargeKey.ContainsKey(chargeKey))
            {
                skippedRecentRetryCount++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.ProviderToken))
            {
                skippedMissingTokenCount++;
                continue;
            }

            if (!subscribersById.TryGetValue(candidate.SubscriberId, out var subscriber) ||
                string.IsNullOrWhiteSpace(subscriber.Email))
            {
                skippedMissingEmailOrPlanCount++;
                continue;
            }

            var plan = PaymentPlanCatalog.FindByTierCode(candidate.TierCode);
            if (plan is null)
            {
                skippedMissingEmailOrPlanCount++;
                continue;
            }

            retryReadyCandidates++;
            executableCandidates.Add(new RetryCandidate(candidate, subscriber, plan, chargeKey));
        }

        if (maxAttempts is > 0)
        {
            executableCandidates = executableCandidates.Take(maxAttempts.Value).ToList();
        }

        var attempts = new List<PaystackAuthorizationRetryAttemptResult>();
        var succeededCount = 0;
        var failedCount = 0;
        var skippedNotActionableLiveCount = 0;

        foreach (var candidate in executableCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subscriptionCode = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
                provider: "paystack",
                sourceSystem: candidate.Subscription.SourceSystem,
                providerPaymentId: candidate.Subscription.ProviderPaymentId,
                providerTransactionId: candidate.Subscription.ProviderTransactionId);
            if (string.IsNullOrWhiteSpace(subscriptionCode))
            {
                attempts.Add(new PaystackAuthorizationRetryAttemptResult(
                    candidate.Subscriber.Email!,
                    candidate.Subscription.SubscriptionId,
                    candidate.ChargeKey,
                    AttemptStatus: "skipped",
                    Message: "No Paystack subscription code was available for retry.",
                    Reference: null,
                    NextPaymentDateUtc: null));
                continue;
            }

            await DelayForPaystackAsync(cancellationToken);
            var liveSubscription = await _paystackCheckoutService.GetSubscriptionAsync(subscriptionCode, cancellationToken);
            var liveMostRecentInvoiceStatus = TryReadMostRecentInvoiceStatus(liveSubscription.RawPayload);
            var isLiveActionable =
                liveSubscription.IsSuccess &&
                (string.Equals(liveSubscription.Status, "attention", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(liveMostRecentInvoiceStatus, "failed", StringComparison.OrdinalIgnoreCase));
            if (!isLiveActionable)
            {
                skippedNotActionableLiveCount++;
                attempts.Add(new PaystackAuthorizationRetryAttemptResult(
                    candidate.Subscriber.Email!,
                    candidate.Subscription.SubscriptionId,
                    candidate.ChargeKey,
                    AttemptStatus: "skipped",
                    Message: $"Live Paystack state was not actionable. status={liveSubscription.Status ?? "unknown"} invoice={liveMostRecentInvoiceStatus ?? "unknown"}",
                    Reference: null,
                    NextPaymentDateUtc: liveSubscription.NextPaymentDate));
                continue;
            }

            var reference = BuildRetryReference(candidate.Subscription.SubscriptionId);
            var retryBillingAmountZar = candidate.Subscription.BillingAmountZar is > 0m
                ? decimal.Round(candidate.Subscription.BillingAmountZar.Value, 2, MidpointRounding.AwayFromZero)
                : decimal.Round(candidate.Plan.Amount, 2, MidpointRounding.AwayFromZero);

            await DelayForPaystackAsync(cancellationToken);
            var chargeResult = await _paystackCheckoutService.ChargeAuthorizationAsync(
                candidate.Plan,
                candidate.Subscriber.Email!,
                candidate.Subscription.ProviderToken!,
                reference,
                candidate.Subscription.SubscriptionId,
                candidate.ChargeKey,
                candidate.Subscription.BillingAmountZar,
                cancellationToken);

            if (chargeResult.Reference is not null || reference is not null)
            {
                await Task.Delay(VerificationDelay, cancellationToken);
            }

            PaystackVerifyResult? verifyResult = null;
            var verifyReference = chargeResult.Reference ?? reference;
            if (!string.IsNullOrWhiteSpace(verifyReference))
            {
                await DelayForPaystackAsync(cancellationToken);
                verifyResult = await _paystackCheckoutService.VerifyTransactionAsync(verifyReference, cancellationToken);
            }

            var isVerifiedSuccess = verifyResult is not null &&
                                    verifyResult.IsSuccess &&
                                    (string.Equals(verifyResult.TransactionStatus, "success", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(verifyResult.TransactionStatus, "successful", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(verifyResult.TransactionStatus, "paid", StringComparison.OrdinalIgnoreCase));
            var isVerifiedFailure = verifyResult is not null &&
                                    verifyResult.IsSuccess &&
                                    string.Equals(verifyResult.TransactionStatus, "failed", StringComparison.OrdinalIgnoreCase);

            if (isVerifiedSuccess)
            {
                await DelayForPaystackAsync(cancellationToken);
                var refreshedSubscription = await _paystackCheckoutService.GetSubscriptionAsync(subscriptionCode, cancellationToken);
                var nextPaymentDateUtc = refreshedSubscription.NextPaymentDate
                                         ?? liveSubscription.NextPaymentDate
                                         ?? (verifyResult!.PaidAt ?? nowUtc).AddMonths(candidate.Plan.BillingPeriodMonths);

                await InsertRetryEventAsync(
                    baseUri,
                    candidate.Subscription.SubscriptionId,
                    candidate.ChargeKey,
                    verifyResult!.ProviderTransactionId ?? verifyResult.Reference ?? reference,
                    "success",
                    BuildEventPayload(
                        reference: verifyResult.Reference ?? reference,
                        customerEmail: candidate.Subscriber.Email!,
                        amountInCents: verifyResult.AmountInCents,
                        currency: verifyResult.Currency ?? "ZAR",
                        gatewayResponse: verifyResult.GatewayResponse,
                        transactionStatus: verifyResult.TransactionStatus,
                        paidAt: verifyResult.PaidAt),
                    cancellationToken);
                await MarkSubscriptionActiveAsync(baseUri, candidate.Subscription.SubscriptionId, nextPaymentDateUtc, cancellationToken);

                succeededCount++;
                attempts.Add(new PaystackAuthorizationRetryAttemptResult(
                    candidate.Subscriber.Email!,
                    candidate.Subscription.SubscriptionId,
                    candidate.ChargeKey,
                    AttemptStatus: "success",
                    Message: verifyResult.GatewayResponse ?? "Approved",
                    Reference: verifyResult.Reference ?? reference,
                    NextPaymentDateUtc: nextPaymentDateUtc));
                continue;
            }

            var failurePayload = BuildEventPayload(
                reference: verifyResult?.Reference ?? chargeResult.Reference ?? reference,
                customerEmail: candidate.Subscriber.Email!,
                amountInCents: verifyResult?.AmountInCents ?? (long)Math.Round(retryBillingAmountZar * 100m, MidpointRounding.AwayFromZero),
                currency: verifyResult?.Currency ?? "ZAR",
                gatewayResponse: verifyResult?.GatewayResponse ?? chargeResult.ErrorMessage,
                transactionStatus: verifyResult?.TransactionStatus ?? chargeResult.TransactionStatus,
                paidAt: verifyResult?.PaidAt);
            await InsertRetryEventAsync(
                baseUri,
                candidate.Subscription.SubscriptionId,
                candidate.ChargeKey,
                verifyResult?.ProviderTransactionId ?? chargeResult.ProviderTransactionId ?? reference,
                "failed",
                failurePayload,
                cancellationToken);

            failedCount++;
            attempts.Add(new PaystackAuthorizationRetryAttemptResult(
                candidate.Subscriber.Email!,
                candidate.Subscription.SubscriptionId,
                candidate.ChargeKey,
                AttemptStatus: "failed",
                Message: verifyResult?.GatewayResponse ?? chargeResult.ErrorMessage ?? "Retry failed.",
                Reference: verifyResult?.Reference ?? chargeResult.Reference ?? reference,
                NextPaymentDateUtc: liveSubscription.NextPaymentDate));
        }

        return new PaystackAuthorizationRetryBatchResult(
            TotalProblematicChargeKeys: problematicChargeKeys.Count,
            RetryReadyCandidates: retryReadyCandidates,
            AttemptedCount: attempts.Count(attempt => !string.Equals(attempt.AttemptStatus, "skipped", StringComparison.OrdinalIgnoreCase)),
            SucceededCount: succeededCount,
            FailedCount: failedCount,
            SkippedRecentRetryCount: skippedRecentRetryCount,
            SkippedMissingTokenCount: skippedMissingTokenCount,
            SkippedNotActionableLiveCount: skippedNotActionableLiveCount,
            SkippedMissingEmailOrPlanCount: skippedMissingEmailOrPlanCount,
            Errors: errors,
            Attempts: attempts);
    }

    private async Task<List<SubscriptionRow>> FetchNonCancelledPaystackSubscriptionsAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        var escapedNow = Uri.EscapeDataString(DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscriptions?provider=eq.paystack&status=not.eq.cancelled&or=(status.eq.failed,next_renewal_at.lt.{escapedNow})&select=subscription_id,subscriber_id,tier_code,provider_payment_id,provider_transaction_id,provider_token,provider_email_token,source_system,status,next_renewal_at,updated_at,billing_amount_zar&order=updated_at.desc&limit=2000");
        using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Paystack retry candidate lookup failed. Status={StatusCode} Body={Body}", (int)response.StatusCode, body);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<SubscriptionRow>>(stream, cancellationToken: cancellationToken) ?? [];
    }

    private async Task<Dictionary<string, DateTimeOffset>> FetchRecentRetryByChargeKeyAsync(
        Uri baseUri,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        var escapedCutoff = Uri.EscapeDataString(cutoffUtc.UtcDateTime.ToString("O"));
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscription_events?provider=eq.paystack&event_type=eq.paystack.authorization_retry&received_at=gt.{escapedCutoff}&select=provider_payment_id,received_at&order=received_at.desc&limit=2000");
        using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<SubscriptionEventRow>>(stream, cancellationToken: cancellationToken) ?? [];
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ProviderPaymentId))
            .GroupBy(row => row.ProviderPaymentId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Max(item => item.ReceivedAt ?? DateTimeOffset.MinValue),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, SubscriberRow>> FetchSubscribersByIdAsync(
        Uri baseUri,
        IReadOnlyList<string> subscriberIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, SubscriberRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in Batch(subscriberIds, 150))
        {
            var filter = string.Join(",", batch.Select(id => $"\"{id}\""));
            var uri = new Uri(
                baseUri,
                $"rest/v1/subscribers?subscriber_id=in.({Uri.EscapeDataString(filter)})&select=subscriber_id,email,first_name,display_name");
            using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var rows = await response.Content.ReadFromJsonAsync<List<SubscriberRow>>(cancellationToken: cancellationToken) ?? [];
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.SubscriberId))
                {
                    result[row.SubscriberId.Trim()] = row;
                }
            }
        }

        return result;
    }

    private async Task InsertRetryEventAsync(
        Uri baseUri,
        string subscriptionId,
        string providerPaymentId,
        string? providerTransactionId,
        string eventStatus,
        Dictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var idempotencyTransactionId = string.IsNullOrWhiteSpace(providerTransactionId)
            ? payload.TryGetValue("reference", out var reference) ? reference?.ToString() : null
            : providerTransactionId.Trim();

        if (!string.IsNullOrWhiteSpace(idempotencyTransactionId))
        {
            var existingUri = new Uri(
                baseUri,
                $"rest/v1/subscription_events?provider=eq.paystack&provider_payment_id=eq.{Uri.EscapeDataString(providerPaymentId)}&provider_transaction_id=eq.{Uri.EscapeDataString(idempotencyTransactionId)}&event_type=eq.paystack.authorization_retry&select=event_id&limit=1");
            using var existingRequest = CreateSupabaseRequest(HttpMethod.Get, existingUri);
            using var existingResponse = await _httpClient.SendAsync(existingRequest, cancellationToken);
            if (existingResponse.IsSuccessStatusCode)
            {
                var existingRows = await existingResponse.Content.ReadFromJsonAsync<List<ExistingEventRow>>(cancellationToken: cancellationToken) ?? [];
                if (existingRows.Count > 0)
                {
                    return;
                }
            }
        }

        var eventPayload = new
        {
            subscription_id = subscriptionId,
            provider = "paystack",
            provider_payment_id = providerPaymentId,
            provider_transaction_id = idempotencyTransactionId,
            event_type = "paystack.authorization_retry",
            event_status = eventStatus,
            received_at = DateTime.UtcNow,
            payload
        };

        using var request = CreateSupabaseJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "rest/v1/subscription_events"),
            eventPayload,
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Paystack retry event insert failed. subscription_id={SubscriptionId} payment_id={ProviderPaymentId} Status={StatusCode} Body={Body}",
                subscriptionId,
                providerPaymentId,
                (int)response.StatusCode,
                body);
        }
    }

    private async Task MarkSubscriptionActiveAsync(
        Uri baseUri,
        string subscriptionId,
        DateTimeOffset nextRenewalAtUtc,
        CancellationToken cancellationToken)
    {
        var escapedSubscriptionId = Uri.EscapeDataString(subscriptionId);
        using var request = CreateSupabaseJsonRequest(
            new HttpMethod("PATCH"),
            new Uri(baseUri, $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}"),
            new
            {
                status = "active",
                next_renewal_at = nextRenewalAtUtc.UtcDateTime,
                cancelled_at = (DateTime?)null,
                updated_at = DateTime.UtcNow
            },
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Paystack retry subscription update failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
                subscriptionId,
                (int)response.StatusCode,
                body);
        }
    }

    private async Task DelayForPaystackAsync(CancellationToken cancellationToken)
    {
        var delay = _lastPaystackCallAtUtc + MinimumPaystackInterval - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        _lastPaystackCallAtUtc = DateTimeOffset.UtcNow;
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        if (Uri.TryCreate((_supabaseOptions.Url ?? string.Empty).TrimEnd('/') + "/", UriKind.Absolute, out baseUri))
        {
            return true;
        }

        baseUri = default!;
        return false;
    }

    private HttpRequestMessage CreateSupabaseRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("apikey", _supabaseOptions.SecretKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _supabaseOptions.SecretKey);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private HttpRequestMessage CreateSupabaseJsonRequest(
        HttpMethod method,
        Uri uri,
        object payload,
        string? prefer = null)
    {
        var request = CreateSupabaseRequest(method, uri);
        request.Content = JsonContent.Create(payload);
        if (!string.IsNullOrWhiteSpace(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }

        return request;
    }

    private static bool IsLocallyProblematic(SubscriptionRow row) =>
        string.Equals(row.Status, "failed", StringComparison.OrdinalIgnoreCase) ||
        (row.NextRenewalAt.HasValue && row.NextRenewalAt.Value < DateTimeOffset.UtcNow);

    private static string BuildChargeKey(SubscriptionRow row) =>
        BuildChargeKey(row.ProviderPaymentId, row.ProviderTransactionId, row.SubscriptionId);

    private static string BuildChargeKey(string? providerPaymentId, string? providerTransactionId, string subscriptionId)
    {
        if (PaystackSubscriptionCodeResolver.IsSubscriptionCode(providerPaymentId))
        {
            return providerPaymentId!.Trim();
        }

        if (PaystackSubscriptionCodeResolver.IsSubscriptionCode(providerTransactionId))
        {
            return providerTransactionId!.Trim();
        }

        return subscriptionId;
    }

    private static string BuildRetryReference(string subscriptionId)
    {
        var compact = new string(subscriptionId.Where(char.IsLetterOrDigit).ToArray());
        var tail = compact.Length > 12 ? compact[^12..] : compact;
        return $"retry-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{tail.ToLowerInvariant()}";
    }

    private static string? TryReadMostRecentInvoiceStatus(string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            if (document.RootElement.TryGetProperty("data", out var dataNode) &&
                dataNode.ValueKind == JsonValueKind.Object &&
                dataNode.TryGetProperty("most_recent_invoice", out var invoiceNode) &&
                invoiceNode.ValueKind == JsonValueKind.Object &&
                invoiceNode.TryGetProperty("status", out var statusNode) &&
                statusNode.ValueKind == JsonValueKind.String)
            {
                return statusNode.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static Dictionary<string, object?> BuildEventPayload(
        string? reference,
        string customerEmail,
        long amountInCents,
        string currency,
        string? gatewayResponse,
        string? transactionStatus,
        DateTimeOffset? paidAt)
    {
        return new Dictionary<string, object?>
        {
            ["reference"] = reference,
            ["customer_email"] = customerEmail,
            ["amount"] = amountInCents,
            ["currency"] = currency,
            ["gateway_response"] = gatewayResponse,
            ["verified_status"] = transactionStatus,
            ["paid_at"] = paidAt?.UtcDateTime.ToString("O"),
            ["source"] = "subscription_authorization_retry_batch"
        };
    }

    private static IEnumerable<List<string>> Batch(IReadOnlyList<string> values, int batchSize)
    {
        for (var index = 0; index < values.Count; index += batchSize)
        {
            yield return values.Skip(index).Take(batchSize).ToList();
        }
    }

    private sealed record RetryCandidate(
        SubscriptionRow Subscription,
        SubscriberRow Subscriber,
        PaymentPlan Plan,
        string ChargeKey);

    private sealed class SubscriptionRow
    {
        [JsonPropertyName("subscription_id")]
        public string SubscriptionId { get; set; } = string.Empty;

        [JsonPropertyName("subscriber_id")]
        public string SubscriberId { get; set; } = string.Empty;

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("provider_token")]
        public string? ProviderToken { get; set; }

        [JsonPropertyName("provider_email_token")]
        public string? ProviderEmailToken { get; set; }

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [JsonPropertyName("billing_amount_zar")]
        public decimal? BillingAmountZar { get; set; }
    }

    private sealed class SubscriberRow
    {
        [JsonPropertyName("subscriber_id")]
        public string SubscriberId { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }

    private sealed class SubscriptionEventRow
    {
        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("received_at")]
        public DateTimeOffset? ReceivedAt { get; set; }
    }

    private sealed class ExistingEventRow
    {
        [JsonPropertyName("event_id")]
        public int EventId { get; set; }
    }
}

public sealed record PaystackAuthorizationRetryBatchResult(
    int TotalProblematicChargeKeys,
    int RetryReadyCandidates,
    int AttemptedCount,
    int SucceededCount,
    int FailedCount,
    int SkippedRecentRetryCount,
    int SkippedMissingTokenCount,
    int SkippedNotActionableLiveCount,
    int SkippedMissingEmailOrPlanCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<PaystackAuthorizationRetryAttemptResult> Attempts);

public sealed record PaystackAuthorizationRetryAttemptResult(
    string Email,
    string SubscriptionId,
    string ChargeKey,
    string AttemptStatus,
    string? Message,
    string? Reference,
    DateTimeOffset? NextPaymentDateUtc);
