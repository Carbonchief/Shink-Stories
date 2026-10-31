using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shink.Services;

public sealed partial class SupabaseSubscriptionLedgerService
{
    private const string ProviderCardUpdatePending = "provider_card_update";
    private const string CardUpdatePaymentPending = "payment_confirmation";
    private static readonly TimeSpan CardUpdateAttentionDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CardUpdateJobLifetime = TimeSpan.FromHours(24);

    private async Task<SubscriptionPersistResult> EnqueuePaystackCardUpdateAsync(
        Uri baseUri, string apiKey, JsonElement payload, string eventType, JsonElement data,
        string? providerPaymentId, string? providerTransactionId, bool isVerification,
        CancellationToken cancellationToken)
    {
        var jobKey = BuildPaystackEventDedupeKey(eventType, data, providerPaymentId, providerTransactionId,
            providerPaymentId, providerTransactionId, payload.GetRawText());
        using var request = CreateJsonRequest(HttpMethod.Post,
            new Uri(baseUri, "rest/v1/paystack_card_update_jobs?on_conflict=job_key"), apiKey,
            new { job_key = jobKey, payload }, "resolution=ignore-duplicates,return=minimal");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode
                ? new SubscriptionPersistResult(true, IsCardVerification: isVerification, IsDeferred: true)
                : new SubscriptionPersistResult(false, "Could not durably queue the Paystack card-update event.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Paystack card-update enqueue failed; the webhook must be retried.");
            return new SubscriptionPersistResult(false, "Could not durably queue the Paystack card-update event.");
        }
    }

    public async Task ProcessPendingPaystackCardUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri) || string.IsNullOrWhiteSpace(ResolveApiKey())) return;
        var apiKey = ResolveApiKey();
        // Claim one at a time: later jobs must not spend their lease waiting for
        // earlier provider requests. Another instance may process different jobs.
        for (var index = 0; index < 5 && !cancellationToken.IsCancellationRequested; index++)
        {
            using var claimRequest = CreateJsonRequest(HttpMethod.Post,
                new Uri(baseUri, "rest/v1/rpc/claim_paystack_card_update_job"), apiKey, new { }, "return=representation");
            using var claimResponse = await _httpClient.SendAsync(claimRequest, cancellationToken);
            claimResponse.EnsureSuccessStatusCode();
            var jobs = await claimResponse.Content.ReadFromJsonAsync<List<PaystackCardUpdateJob>>(cancellationToken);
            var job = jobs?.SingleOrDefault();
            if (job is null) return;

            SubscriptionPersistResult result;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                try
                {
                    result = DateTimeOffset.UtcNow - job.CreatedAt >= CardUpdateJobLifetime
                        ? new SubscriptionPersistResult(false, "The background job exceeded its automatic processing window.",
                            job.SubscriptionId, PendingReason: job.ResultCode)
                        : await ProcessQueuedPaystackCardUpdateAsync(job.Payload.GetRawText(), timeout.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
                {
                    _logger.LogWarning(exception, "Paystack card-update background processing will retry. job_id={JobId}", job.JobId);
                    result = new SubscriptionPersistResult(false, "Background verification could not be completed.");
                }
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var pending = !result.IsSuccess || result.PendingReason is not null;
            var expired = pending && nowUtc - job.CreatedAt >= CardUpdateJobLifetime;
            var code = result.PendingReason ?? (result.IsSuccess
                ? result.IsCardVerification && result.SubscriptionId is not null ? "card_update_applied" : "processed"
                : "processing_error");
            if (pending && (expired || nowUtc - job.CreatedAt >= CardUpdateAttentionDelay))
            {
                var reference = ReadCardUpdateJobReference(job.Payload) ?? job.JobKey;
                // Queue before releasing/terminating the job. A crash can repeat
                // this insert safely; a storage failure leaves the lease retryable.
                var queued = await _subscriptionNotificationEmailService.QueueAdminOpsAlertAsync(
                    new AdminOpsAlertEmailRequest(
                        $"paystack-card-update-pending/{reference}/{code}", "error",
                        code == ProviderCardUpdatePending ? "Paystack subscription card update incomplete" : "Paystack card update needs attention",
                        code == ProviderCardUpdatePending
                            ? "The new card was verified, but Paystack's recurring subscription still uses a different authorization. No recovery charge was started."
                            : "Background processing of a Paystack card update has not completed.",
                        $"Subscription ID: {result.SubscriptionId ?? "not linked"}\nJob ID: {job.JobId}\nState: {code}\nAutomatic checks stopped: {expired}",
                        reference, job.CreatedAt), cancellationToken);
                if (!queued)
                {
                    _logger.LogWarning("Paystack card-update alert is not durable yet; retaining its job lease for retry. job_id={JobId}", job.JobId);
                    continue;
                }
            }
            var completion = new Dictionary<string, object?>
            {
                ["status"] = expired ? "needs_attention" : pending ? "pending" : "completed",
                ["result_code"] = code,
                ["last_error"] = result.ErrorMessage ?? result.PendingReason,
                ["available_at"] = nowUtc.AddMinutes(Math.Min(15, 1 << Math.Min(Math.Max(job.Attempts - 1, 0), 4))),
                ["lease_token"] = null,
                ["locked_until"] = null,
                ["updated_at"] = nowUtc
            };
            if (result.SubscriptionId is not null) completion["subscription_id"] = result.SubscriptionId;
            var completionUri = new Uri(baseUri, "rest/v1/paystack_card_update_jobs" +
                $"?job_id=eq.{job.JobId}&lease_token=eq.{job.LeaseToken}&status=eq.processing&select=job_id");
            using var completionRequest = CreateJsonRequest(HttpMethod.Patch, completionUri, apiKey, completion, "return=representation");
            using var completionResponse = await _httpClient.SendAsync(completionRequest, cancellationToken);
            completionResponse.EnsureSuccessStatusCode();
            var completed = await completionResponse.Content.ReadFromJsonAsync<List<PaystackCardUpdateJobId>>(cancellationToken);
            if (completed?.Count != 1)
            {
                _logger.LogWarning("Paystack card-update job lost its lease before completion. job_id={JobId}", job.JobId);
                continue;
            }
        }
    }

    private static string? ReadCardUpdateJobReference(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data)) return null;
        return IsPaystackRefundEvent(TryReadString(payload, "event") ?? "unknown")
            ? ResolvePaystackRefundTransactionReference(data)
            : TryReadString(data, "reference");
    }

    private async Task CompleteConfirmedCardUpdatePaymentAsync(
        Uri baseUri, string apiKey, PaymentRecoverySubscriptionContext subscription, PaymentRecoveryRow recovery,
        DateTimeOffset nextRenewalAtUtc, string reference, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        // A failed write must leave the durable job retryable. Reprocessing uses
        // the same payment reference, so it reconciles rather than recollects.
        // Protect this confirmed payment from the expiry worker before changing
        // access. A restart must reconcile succeeded-but-unresolved recoveries.
        await MarkPaymentRecoveryAuthorizationRetryAsync(baseUri, apiKey, recovery.RecoveryId,
            nowUtc, "succeeded", reference, null, null, cancellationToken,
            requireSuccess: true, expectedReference: reference);
        await MarkSubscriptionRecoveredByIdAsync(baseUri, apiKey, subscription.SubscriptionId,
            nextRenewalAtUtc, cancellationToken, requireSuccess: true);
        await _subscriptionPaymentRecoveryEmailService.CancelSequenceAsync(
            new SubscriptionPaymentRecoveryEmailSequence(recovery.ImmediateEmailId, recovery.WarningEmailId, recovery.SuspensionEmailId), cancellationToken);
        await ResolvePaymentRecoveryAsync(baseUri, apiKey, recovery.RecoveryId, nowUtc,
            "recovered", cancellationToken, requireSuccess: true);
    }

    private async Task<bool> ReserveCardUpdateAttemptAsync(
        Uri baseUri, string apiKey, PaymentRecoveryRow recovery, string reference, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/reserve_paystack_card_update_attempt"), apiKey,
            new { p_recovery_id = recovery.RecoveryId, p_expected_status = recovery.AuthorizationRetryStatus,
                p_expected_reference = recovery.AuthorizationRetryReference, p_attempt_reference = reference }, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<PaymentRecoveryRow>>(cancellationToken);
        return rows?.Count == 1;
    }

    private async Task<bool> SuspendExpiredPaymentRecoveryAsync(
        Uri baseUri, string apiKey, string recoveryId, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Post,
            new Uri(baseUri, "rest/v1/rpc/suspend_expired_payment_recovery"), apiKey,
            new { p_recovery_id = recoveryId }, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<PaymentRecoveryRow>>(cancellationToken);
        return rows?.Count == 1;
    }

    private sealed record PaystackCardUpdateJob(
        [property: JsonPropertyName("job_id")] Guid JobId,
        [property: JsonPropertyName("job_key")] string JobKey,
        [property: JsonPropertyName("payload")] JsonElement Payload,
        [property: JsonPropertyName("attempts")] int Attempts,
        [property: JsonPropertyName("lease_token")] Guid LeaseToken,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("result_code")] string? ResultCode = null,
        [property: JsonPropertyName("subscription_id")] string? SubscriptionId = null);

    private sealed record PaystackCardUpdateJobId([property: JsonPropertyName("job_id")] Guid JobId);
}
