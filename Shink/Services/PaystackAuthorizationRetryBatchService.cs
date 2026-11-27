using Microsoft.Extensions.Options;
using Shink.Components.Content;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shink.Services;

public sealed class PaystackAuthorizationRetryBatchService(
    HttpClient httpClient,
    ISubscriptionLedgerService ledger,
    IOptions<SupabaseOptions> supabaseOptions,
    ILogger<PaystackAuthorizationRetryBatchService> logger)
{
    private static readonly TimeSpan MinimumPaystackInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AuthorizationRetryDelay = TimeSpan.FromDays(1);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _supabaseOptions = supabaseOptions.Value;
    private readonly ILogger<PaystackAuthorizationRetryBatchService> _logger = logger;

    private DateTimeOffset _lastPaystackCallAtUtc = DateTimeOffset.MinValue;

    public async Task<PaystackAuthorizationRetryBatchResult> RetryEligibleSubscriptionsAsync(
        int? maxAttempts = null,
        IReadOnlySet<string>? excludedSubscriberIds = null,
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
                SkippedMissingBillingAmountCount: 0,
                SkippedDuplicateCurrentSubscriptionCount: 0,
                Errors: ["Supabase SecretKey or URL is not configured."],
                Attempts: []);
        }

        var errors = new List<string>();
        var nowUtc = DateTimeOffset.UtcNow;
        var cutoffUtc = nowUtc.AddHours(-24);

        var retryReadyRecoveryBySubscriptionId = await FetchRetryReadyRecoveryBySubscriptionIdAsync(baseUri, nowUtc, cancellationToken);
        var subscriptions = await FetchNonCancelledPaystackSubscriptionsAsync(baseUri, cancellationToken);
        var subscriptionsWithRetryReadyRecovery = subscriptions
            .Where(item => retryReadyRecoveryBySubscriptionId.ContainsKey(item.SubscriptionId))
            .ToList();
        var problematicChargeKeys = subscriptions
            .Where(item => retryReadyRecoveryBySubscriptionId.ContainsKey(item.SubscriptionId))
            .Select(BuildChargeKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recentRetryByChargeKey = await FetchRecentRetryByChargeKeyAsync(baseUri, cutoffUtc, cancellationToken);
        var subscribersById = await FetchSubscribersByIdAsync(
            baseUri,
            subscriptions.Select(item => item.SubscriberId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            cancellationToken);
        var currentSubscriptionCodesBySubscriber = await FetchCurrentPaystackSubscriptionCodesBySubscriberAsync(
            baseUri,
            subscriptions.Select(item => item.SubscriberId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            nowUtc,
            cancellationToken);

        var preferredCandidates = subscriptionsWithRetryReadyRecovery
            .Where(item => problematicChargeKeys.Contains(BuildChargeKey(item)))
            .GroupBy(BuildChargeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => string.Equals(item.SourceSystem, "shink_app", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(item => item.UpdatedAt ?? DateTimeOffset.MinValue)
                .First())
            .Where(item => excludedSubscriberIds is null || !excludedSubscriberIds.Contains(item.SubscriberId.Trim()))
            .ToList();

        var skippedRecentRetryCount = 0;
        var skippedMissingTokenCount = 0;
        var skippedMissingEmailOrPlanCount = 0;
        var skippedMissingBillingAmountCount = 0;
        var skippedDuplicateCurrentSubscriptionCount = 0;
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

            if (HasCurrentDuplicatePaystackSubscription(candidate, currentSubscriptionCodesBySubscriber))
            {
                skippedDuplicateCurrentSubscriptionCount++;
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

            if (candidate.BillingAmountZar is not > 0m)
            {
                skippedMissingBillingAmountCount++;
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
            await DelayForPaystackAsync(cancellationToken);
            var debt = retryReadyRecoveryBySubscriptionId[candidate.Subscription.SubscriptionId];
            var attempt = await ledger.RetryDueRecoveryAsync(candidate.Subscription.SubscriptionId, debt.RecoveryId!,
                candidate.Subscriber.Email!, candidate.ChargeKey, cancellationToken);
            attempts.Add(attempt);
            if (attempt.AttemptStatus == "success") succeededCount++;
            if (attempt.AttemptStatus == "failed") failedCount++;
            if (attempt.AttemptStatus == "skipped") skippedNotActionableLiveCount++;
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
            SkippedMissingBillingAmountCount: skippedMissingBillingAmountCount,
            SkippedDuplicateCurrentSubscriptionCount: skippedDuplicateCurrentSubscriptionCount,
            Errors: errors,
            Attempts: attempts);
    }

    private async Task<List<SubscriptionRow>> FetchNonCancelledPaystackSubscriptionsAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscriptions?provider=eq.paystack&status=not.eq.cancelled&select=subscription_id,subscriber_id,tier_code,provider_payment_id,provider_transaction_id,provider_token,provider_email_token,source_system,status,next_renewal_at,updated_at,billing_amount_zar&order=updated_at.desc&limit=2000");
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

    private async Task<Dictionary<string, PaymentRecoveryRow>> FetchRetryReadyRecoveryBySubscriptionIdAsync(
        Uri baseUri,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var escapedNow = Uri.EscapeDataString(nowUtc.UtcDateTime.ToString("O"));
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscription_payment_recoveries?provider=eq.paystack&resolved_at=is.null&authorization_retry_status=eq.pending&authorization_retry_due_at=lte.{escapedNow}&select=recovery_id,subscription_id,provider_payment_id,first_failed_at,authorization_retry_due_at,authorization_retry_status&order=authorization_retry_due_at.asc&limit=2000");
        using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Paystack retry recovery lookup failed. Status={StatusCode} Body={Body}", (int)response.StatusCode, body);
            return new Dictionary<string, PaymentRecoveryRow>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<PaymentRecoveryRow>>(stream, cancellationToken: cancellationToken) ?? [];
        return rows
            .Where(row => IsRetryReadyRecovery(row, nowUtc))
            .GroupBy(row => row.SubscriptionId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.AuthorizationRetryDueAt ?? DateTimeOffset.MaxValue).First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, List<CurrentSubscriptionRow>>> FetchCurrentPaystackSubscriptionCodesBySubscriberAsync(
        Uri baseUri,
        IReadOnlyList<string> subscriberIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<CurrentSubscriptionRow>>(StringComparer.OrdinalIgnoreCase);
        var escapedNow = Uri.EscapeDataString(nowUtc.UtcDateTime.ToString("O"));
        foreach (var batch in subscriberIds.Chunk(150))
        {
            var filter = string.Join(",", batch.Select(id => $"\"{id}\""));
            var uri = new Uri(
                baseUri,
                "rest/v1/subscriptions" +
                $"?subscriber_id=in.({Uri.EscapeDataString(filter)})" +
                "&provider=eq.paystack" +
                "&status=eq.active" +
                "&cancelled_at=is.null" +
                $"&or=(next_renewal_at.is.null,next_renewal_at.gte.{escapedNow})" +
                "&select=subscription_id,subscriber_id,provider_payment_id,provider_transaction_id,source_system,next_renewal_at");
            using var request = CreateSupabaseRequest(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var rows = await response.Content.ReadFromJsonAsync<List<CurrentSubscriptionRow>>(cancellationToken: cancellationToken) ?? [];
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.SubscriberId))
                {
                    continue;
                }

                var code = ResolvePaystackSubscriptionCode(row.SourceSystem, row.ProviderPaymentId, row.ProviderTransactionId);
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                row.SubscriptionCode = code;
                if (!result.TryGetValue(row.SubscriberId.Trim(), out var subscriberRows))
                {
                    subscriberRows = [];
                    result[row.SubscriberId.Trim()] = subscriberRows;
                }

                subscriberRows.Add(row);
            }
        }

        return result;
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
        foreach (var batch in subscriberIds.Chunk(150))
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
        if (Uri.TryCreate((_supabaseOptions.Url ?? string.Empty).TrimEnd('/') + "/", UriKind.Absolute, out var parsedBaseUri) &&
            parsedBaseUri is not null)
        {
            baseUri = parsedBaseUri;
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

    private static bool HasCurrentDuplicatePaystackSubscription(
        SubscriptionRow candidate,
        IReadOnlyDictionary<string, List<CurrentSubscriptionRow>> currentSubscriptionCodesBySubscriber)
    {
        if (!currentSubscriptionCodesBySubscriber.TryGetValue(candidate.SubscriberId, out var currentRows))
        {
            return false;
        }

        var candidateCode = ResolvePaystackSubscriptionCode(
            candidate.SourceSystem,
            candidate.ProviderPaymentId,
            candidate.ProviderTransactionId);
        if (string.IsNullOrWhiteSpace(candidateCode))
        {
            return false;
        }

        return currentRows.Any(row =>
            !string.Equals(row.SubscriptionId, candidate.SubscriptionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.SubscriptionCode, candidateCode, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolvePaystackSubscriptionCode(
        string? sourceSystem,
        string? providerPaymentId,
        string? providerTransactionId) =>
        PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
            provider: "paystack",
            sourceSystem: sourceSystem,
            providerPaymentId: providerPaymentId,
            providerTransactionId: providerTransactionId);

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

    private static bool IsRetryReadyRecovery(PaymentRecoveryRow row, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(row.SubscriptionId) ||
            row.FirstFailedAt is null ||
            row.AuthorizationRetryDueAt is null ||
            row.AuthorizationRetryDueAt.Value > nowUtc ||
            !string.Equals(row.AuthorizationRetryStatus, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return row.FirstFailedAt.Value <= nowUtc.Subtract(AuthorizationRetryDelay);
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

    private sealed class CurrentSubscriptionRow
    {
        [JsonPropertyName("subscription_id")]
        public string SubscriptionId { get; set; } = string.Empty;

        [JsonPropertyName("subscriber_id")]
        public string SubscriberId { get; set; } = string.Empty;

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; set; }

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }

        public string? SubscriptionCode { get; set; }
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

    private sealed class PaymentRecoveryRow
    {
        [JsonPropertyName("recovery_id")]
        public string? RecoveryId { get; set; }

        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("first_failed_at")]
        public DateTimeOffset? FirstFailedAt { get; set; }

        [JsonPropertyName("authorization_retry_due_at")]
        public DateTimeOffset? AuthorizationRetryDueAt { get; set; }

        [JsonPropertyName("authorization_retry_status")]
        public string? AuthorizationRetryStatus { get; set; }
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
    int SkippedMissingBillingAmountCount,
    int SkippedDuplicateCurrentSubscriptionCount,
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
