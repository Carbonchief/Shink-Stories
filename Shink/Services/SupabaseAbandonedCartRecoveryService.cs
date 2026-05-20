using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseAbandonedCartRecoveryService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IOptions<ResendOptions> resendOptions,
    ILogger<SupabaseAbandonedCartRecoveryService> logger) : IAbandonedCartRecoveryService
{
    private const string SubscriptionSourceType = "subscription";
    private const string StoreOrderSourceType = "store_order";
    private const string GratisTierCode = "gratis";
    private const string StoryCornerTierCode = "story_corner_monthly";
    private const string AllStoriesMonthlyTierCode = "all_stories_monthly";
    private const string AllStoriesYearlyTierCode = "all_stories_yearly";
    private const string EmailCancelStatusCancelled = "cancelled";
    private const string EmailCancelStatusFailed = "failed";
    private const string EmailCancelStatusMissing = "missing";
    private const string EmailCancelStatusNotCancellable = "not_cancellable";
    private const int MaxCancelErrorLength = 900;
    private static readonly TimeSpan FirstDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan SecondDelay = TimeSpan.FromHours(24);
    private static readonly TimeSpan FinalDelay = TimeSpan.FromDays(7);
    private static readonly TimeSpan ResolvedCleanupWindow = FinalDelay.Add(TimeSpan.FromDays(1));

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _supabaseOptions = supabaseOptions.Value;
    private readonly ResendOptions _resendOptions = resendOptions.Value;
    private readonly ILogger<SupabaseAbandonedCartRecoveryService> _logger = logger;

    public async Task StartSequenceAsync(
        AbandonedCartRecoveryStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() || !IsResendConfigured() || !IsValidSourceType(request.SourceType))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.CheckoutReference) ||
            string.IsNullOrWhiteSpace(request.CustomerEmail) ||
            string.IsNullOrWhiteSpace(request.CheckoutUrl))
        {
            return;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        var apiKey = ResolveApiKey();
        var nowUtc = DateTimeOffset.UtcNow;
        var activeSubscription = await GetActiveSubscriptionRecoveryBlockerAsync(baseUri, apiKey, request, nowUtc, cancellationToken);
        if (activeSubscription is not null)
        {
            _logger.LogInformation(
                "Abandoned cart recovery email sequence skipped because the customer already has active paid access. source_type={SourceType} source_key={SourceKey} active_tier_code={ActiveTierCode} customer_email={CustomerEmail}",
                request.SourceType,
                request.SourceKey,
                activeSubscription.TierCode,
                request.CustomerEmail.Trim().ToLowerInvariant());
            return;
        }

        var existingRecovery = await GetActiveSimilarRecoveryAsync(baseUri, apiKey, request, cancellationToken);
        if (existingRecovery is not null)
        {
            _logger.LogInformation(
                "Abandoned cart recovery email sequence skipped because an active similar recovery already exists. existing_recovery_id={RecoveryId} source_type={SourceType} source_key={SourceKey} customer_email={CustomerEmail}",
                existingRecovery.RecoveryId,
                request.SourceType,
                request.SourceKey,
                request.CustomerEmail.Trim().ToLowerInvariant());
            return;
        }

        var token = CreateOptOutToken();
        var recovery = await CreateRecoveryAsync(baseUri, apiKey, request, token, nowUtc, cancellationToken);
        if (recovery is null)
        {
            return;
        }

        var optOutUrl = BuildOptOutUrl(request.OptOutBaseUrl, recovery.RecoveryId, token);
        var continueUrl = BuildContinueUrl(request.OptOutBaseUrl, recovery.RecoveryId, token);
        var variables = BuildTemplateVariables(request, continueUrl, optOutUrl);
        var emailIds = await ScheduleEmailsAsync(recovery.RecoveryId, request, variables, nowUtc, cancellationToken);
        if (emailIds is null)
        {
            return;
        }

        var updatedRecovery = await StoreScheduledEmailIdsAsync(baseUri, apiKey, recovery.RecoveryId, emailIds, nowUtc, cancellationToken);
        if (updatedRecovery?.ResolvedAt is not null)
        {
            updatedRecovery.FirstEmailId = emailIds.FirstEmailId;
            updatedRecovery.SecondEmailId = emailIds.SecondEmailId;
            updatedRecovery.FinalEmailId = emailIds.FinalEmailId;
            var cancellationSummary = await CancelScheduledEmailsAsync(
                updatedRecovery,
                cancellationToken);
            await StoreCancellationStatusesAsync(
                baseUri,
                apiKey,
                recovery.RecoveryId,
                cancellationSummary,
                cancellationToken);
        }
    }

    public async Task ResolveByCheckoutReferenceAsync(
        string sourceType,
        string checkoutReference,
        string resolution,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() ||
            !IsResendConfigured() ||
            !IsValidSourceType(sourceType) ||
            string.IsNullOrWhiteSpace(checkoutReference))
        {
            return;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        var apiKey = ResolveApiKey();
        var recoveries = await GetActiveRecoveriesAsync(
            baseUri,
            apiKey,
            $"source_type=eq.{Uri.EscapeDataString(sourceType)}&checkout_reference=eq.{Uri.EscapeDataString(checkoutReference.Trim())}",
            cancellationToken);
        await ResolveRecoveriesAsync(baseUri, apiKey, recoveries, resolution, cancellationToken);
    }

    public async Task ResolveSubscriptionRecoveriesAsync(
        string? customerEmail,
        string? tierCode,
        string resolution,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() ||
            !IsResendConfigured() ||
            string.IsNullOrWhiteSpace(customerEmail) ||
            string.IsNullOrWhiteSpace(tierCode))
        {
            return;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        var apiKey = ResolveApiKey();
        var filter = string.Join(
            "&",
            $"source_type=eq.{SubscriptionSourceType}",
            $"customer_email=eq.{Uri.EscapeDataString(customerEmail.Trim().ToLowerInvariant())}");
        var recoveries = (await GetActiveRecoveriesAsync(baseUri, apiKey, filter, cancellationToken))
            .Where(recovery => IsRecoveryResolvedByPurchasedTier(tierCode.Trim(), recovery.SourceKey))
            .ToArray();
        await ResolveRecoveriesAsync(baseUri, apiKey, recoveries, resolution, cancellationToken);
    }

    public async Task<bool> OptOutAsync(
        string recoveryId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() ||
            !IsResendConfigured() ||
            string.IsNullOrWhiteSpace(recoveryId) ||
            string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return false;
        }

        var apiKey = ResolveApiKey();
        var filter = string.Join(
            "&",
            $"recovery_id=eq.{Uri.EscapeDataString(recoveryId.Trim())}",
            $"opt_out_token=eq.{Uri.EscapeDataString(token.Trim())}");
        var recoveries = await GetActiveRecoveriesAsync(baseUri, apiKey, filter, cancellationToken);
        if (recoveries.Count == 0)
        {
            return false;
        }

        await ResolveRecoveriesAsync(baseUri, apiKey, recoveries, "opted_out", cancellationToken);
        return true;
    }

    public async Task<AbandonedCartRecoveryRecord?> GetActiveRecoveryAsync(
        string recoveryId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() ||
            string.IsNullOrWhiteSpace(recoveryId) ||
            string.IsNullOrWhiteSpace(token) ||
            !TryBuildSupabaseBaseUri(out var baseUri))
        {
            return null;
        }

        var apiKey = ResolveApiKey();
        var filter = string.Join(
            "&",
            $"recovery_id=eq.{Uri.EscapeDataString(recoveryId.Trim())}",
            $"opt_out_token=eq.{Uri.EscapeDataString(token.Trim())}");
        var recovery = (await GetActiveRecoveriesAsync(baseUri, apiKey, filter, cancellationToken)).FirstOrDefault();
        if (recovery is null)
        {
            return null;
        }

        return new AbandonedCartRecoveryRecord(
            recovery.RecoveryId,
            recovery.SourceType,
            recovery.SourceKey,
            recovery.CheckoutReference,
            recovery.Provider,
            recovery.CustomerEmail,
            recovery.CustomerName,
            recovery.ItemName,
            recovery.ItemSummary,
            recovery.CartTotalZar);
    }

    public async Task CleanupResolvedScheduledEmailsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() ||
            !IsResendConfigured() ||
            !TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        var apiKey = ResolveApiKey();
        var cleanupSinceUtc = DateTimeOffset.UtcNow.Subtract(ResolvedCleanupWindow);
        var escapedCleanupSince = Uri.EscapeDataString(cleanupSinceUtc.UtcDateTime.ToString("O"));
        var filter = string.Join(
            "&",
            "resolved_at=not.is.null",
            $"resolved_at=gte.{escapedCleanupSince}",
            "or=(first_email_cancel_status.is.null,second_email_cancel_status.is.null,final_email_cancel_status.is.null)",
            "order=resolved_at.asc",
            "limit=100");

        var recoveries = await GetRecoveriesAsync(baseUri, apiKey, filter, cancellationToken);
        foreach (var recovery in recoveries)
        {
            var cancellationSummary = await CancelScheduledEmailsAsync(recovery, cancellationToken);
            await StoreCancellationStatusesAsync(
                baseUri,
                apiKey,
                recovery.RecoveryId,
                cancellationSummary,
                cancellationToken);
        }
    }

    private async Task<AbandonedRecoveryRow?> CreateRecoveryAsync(
        Uri baseUri,
        string apiKey,
        AbandonedCartRecoveryStartRequest request,
        string token,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var payload = new[]
        {
            new
            {
                source_type = request.SourceType.Trim(),
                source_key = request.SourceKey.Trim(),
                checkout_reference = request.CheckoutReference.Trim(),
                provider = request.Provider.Trim(),
                customer_email = request.CustomerEmail.Trim().ToLowerInvariant(),
                customer_name = NormalizeOptionalText(request.CustomerName, 160),
                item_name = request.ItemName.Trim(),
                item_summary = request.ItemSummary.Trim(),
                cart_total_zar = request.CartTotalZar,
                checkout_url = request.CheckoutUrl.Trim(),
                opt_out_token = token,
                first_scheduled_at = nowUtc.Add(FirstDelay).UtcDateTime,
                second_scheduled_at = nowUtc.Add(SecondDelay).UtcDateTime,
                final_scheduled_at = nowUtc.Add(FinalDelay).UtcDateTime
            }
        };

        var uri = new Uri(baseUri, "rest/v1/abandoned_cart_recoveries?on_conflict=source_type,checkout_reference&select=*");
        using var createRequest = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "resolution=ignore-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(createRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Abandoned cart recovery create failed. source_type={SourceType} reference={Reference} status={StatusCode} body={Body}",
                request.SourceType,
                request.CheckoutReference,
                (int)response.StatusCode,
                body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<AbandonedRecoveryRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        return rows.FirstOrDefault();
    }

    private async Task<ActiveSubscriptionRow?> GetActiveSubscriptionRecoveryBlockerAsync(
        Uri baseUri,
        string apiKey,
        AbandonedCartRecoveryStartRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.SourceType.Trim(), SubscriptionSourceType, StringComparison.Ordinal))
        {
            return null;
        }

        var normalizedRequestedTier = request.SourceKey.Trim();
        if (string.IsNullOrWhiteSpace(normalizedRequestedTier) ||
            string.Equals(normalizedRequestedTier, GratisTierCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalizedEmail = request.CustomerEmail.Trim().ToLowerInvariant();
        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var subscriberLookupUri = new Uri(
            baseUri,
            $"rest/v1/subscribers?select=subscriber_id,disabled_at&email=eq.{escapedEmail}&limit=1");

        try
        {
            using var subscriberRequest = CreateRequest(HttpMethod.Get, subscriberLookupUri, apiKey);
            using var subscriberResponse = await _httpClient.SendAsync(subscriberRequest, cancellationToken);
            if (!subscriberResponse.IsSuccessStatusCode)
            {
                var body = await subscriberResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Abandoned cart recovery active-access subscriber lookup failed. customer_email={CustomerEmail} status={StatusCode} body={Body}",
                    normalizedEmail,
                    (int)subscriberResponse.StatusCode,
                    body);
                return null;
            }

            await using var subscriberStream = await subscriberResponse.Content.ReadAsStreamAsync(cancellationToken);
            var subscribers = await JsonSerializer.DeserializeAsync<List<SubscriberLookupRow>>(subscriberStream, cancellationToken: cancellationToken)
                ?? [];
            var subscriber = subscribers.FirstOrDefault();
            if (subscriber is null ||
                string.IsNullOrWhiteSpace(subscriber.SubscriberId) ||
                subscriber.DisabledAt is not null)
            {
                return null;
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriber.SubscriberId);
            var subscriptionsUri = new Uri(
                baseUri,
                $"rest/v1/subscriptions?select=status,next_renewal_at,cancelled_at,tier_code&subscriber_id=eq.{escapedSubscriberId}&status=eq.active&order=subscribed_at.desc&limit=25");

            using var subscriptionsRequest = CreateRequest(HttpMethod.Get, subscriptionsUri, apiKey);
            using var subscriptionsResponse = await _httpClient.SendAsync(subscriptionsRequest, cancellationToken);
            if (!subscriptionsResponse.IsSuccessStatusCode)
            {
                var body = await subscriptionsResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Abandoned cart recovery active-access subscription lookup failed. subscriber_id={SubscriberId} status={StatusCode} body={Body}",
                    subscriber.SubscriberId,
                    (int)subscriptionsResponse.StatusCode,
                    body);
                return null;
            }

            await using var subscriptionsStream = await subscriptionsResponse.Content.ReadAsStreamAsync(cancellationToken);
            var subscriptions = await JsonSerializer.DeserializeAsync<List<ActiveSubscriptionRow>>(subscriptionsStream, cancellationToken: cancellationToken)
                ?? [];

            return subscriptions.FirstOrDefault(subscription =>
                IsCurrentlyActivePaidSubscription(subscription, nowUtc) &&
                IsRecoveryBlockedByActiveTier(normalizedRequestedTier, subscription.TierCode));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(
                exception,
                "Abandoned cart recovery active-access lookup failed unexpectedly. customer_email={CustomerEmail}",
                normalizedEmail);
            return null;
        }
    }

    private async Task<AbandonedRecoveryRow?> GetActiveSimilarRecoveryAsync(
        Uri baseUri,
        string apiKey,
        AbandonedCartRecoveryStartRequest request,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.SourceType.Trim(), StoreOrderSourceType, StringComparison.Ordinal))
        {
            return null;
        }

        var filter = string.Join(
            "&",
            $"source_type=eq.{Uri.EscapeDataString(request.SourceType.Trim())}",
            $"customer_email=eq.{Uri.EscapeDataString(request.CustomerEmail.Trim().ToLowerInvariant())}");

        return (await GetActiveRecoveriesAsync(baseUri, apiKey, filter, cancellationToken)).FirstOrDefault();
    }

    private async Task<RecoveryEmailIds?> ScheduleEmailsAsync(
        string recoveryId,
        AbandonedCartRecoveryStartRequest request,
        IReadOnlyDictionary<string, object?> variables,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var templates = _resendOptions.Templates.AbandonedCartRecovery;
        var first = await SendEmailAsync(
            request.CustomerEmail,
            templates.Hour1TemplateId,
            variables,
            nowUtc.Add(FirstDelay),
            $"abandoned-cart/{recoveryId}/hour1",
            cancellationToken);
        var second = await SendEmailAsync(
            request.CustomerEmail,
            templates.Hour24TemplateId,
            variables,
            nowUtc.Add(SecondDelay),
            $"abandoned-cart/{recoveryId}/hour24",
            cancellationToken);
        var final = await SendEmailAsync(
            request.CustomerEmail,
            templates.Day7TemplateId,
            variables,
            nowUtc.Add(FinalDelay),
            $"abandoned-cart/{recoveryId}/day7",
            cancellationToken);

        return new RecoveryEmailIds(first?.Id, second?.Id, final?.Id);
    }

    private async Task<ResendEmailResponse?> SendEmailAsync(
        string recipientEmail,
        string templateId,
        IReadOnlyDictionary<string, object?> variables,
        DateTimeOffset scheduledAtUtc,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new ResendEmailRequest(
                From: _resendOptions.FromEmail,
                To: [recipientEmail],
                Subject: null,
                Html: null,
                Text: null,
                ReplyTo: string.IsNullOrWhiteSpace(_resendOptions.ToEmail) ? null : [_resendOptions.ToEmail],
                ScheduledAt: scheduledAtUtc.UtcDateTime.ToString("O"),
                Template: new ResendTemplateRequest(templateId, variables)))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Resend rejected abandoned cart recovery email. template_id={TemplateId} status={StatusCode} body={Body}",
                templateId,
                (int)response.StatusCode,
                body);
            return null;
        }

        return JsonSerializer.Deserialize<ResendEmailResponse>(body);
    }

    private async Task<AbandonedRecoveryRow?> StoreScheduledEmailIdsAsync(
        Uri baseUri,
        string apiKey,
        string recoveryId,
        RecoveryEmailIds emailIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            first_email_id = emailIds.FirstEmailId,
            second_email_id = emailIds.SecondEmailId,
            final_email_id = emailIds.FinalEmailId,
            updated_at = nowUtc.UtcDateTime
        };
        var uri = new Uri(baseUri, $"rest/v1/abandoned_cart_recoveries?recovery_id=eq.{Uri.EscapeDataString(recoveryId)}&select=recovery_id,resolved_at,resolution");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Abandoned cart recovery email id update failed. recovery_id={RecoveryId} status={StatusCode} body={Body}",
                recoveryId,
                (int)response.StatusCode,
                body);
            return null;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<AbandonedRecoveryRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        return rows.FirstOrDefault();
    }

    private async Task<IReadOnlyList<AbandonedRecoveryRow>> GetActiveRecoveriesAsync(
        Uri baseUri,
        string apiKey,
        string filter,
        CancellationToken cancellationToken)
    {
        return await GetRecoveriesAsync(
            baseUri,
            apiKey,
            $"resolved_at=is.null&{filter}",
            cancellationToken);
    }

    private async Task<IReadOnlyList<AbandonedRecoveryRow>> GetRecoveriesAsync(
        Uri baseUri,
        string apiKey,
        string filter,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(baseUri, $"rest/v1/abandoned_cart_recoveries?select=*&{filter}");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Abandoned cart recovery lookup failed. status={StatusCode} body={Body}",
                (int)response.StatusCode,
                body);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<AbandonedRecoveryRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
    }

    private async Task ResolveRecoveriesAsync(
        Uri baseUri,
        string apiKey,
        IReadOnlyList<AbandonedRecoveryRow> recoveries,
        string resolution,
        CancellationToken cancellationToken)
    {
        foreach (var recovery in recoveries)
        {
            var cancellationSummary = await CancelScheduledEmailsAsync(recovery, cancellationToken);
            await MarkResolvedAsync(baseUri, apiKey, recovery.RecoveryId, resolution, cancellationSummary, cancellationToken);
        }
    }

    private async Task<EmailCancellationSummary> CancelScheduledEmailsAsync(
        AbandonedRecoveryRow recovery,
        CancellationToken cancellationToken) =>
        new(
            await CancelScheduledEmailAsync(recovery.FirstEmailId, recovery.FirstEmailCancelStatus, cancellationToken),
            await CancelScheduledEmailAsync(recovery.SecondEmailId, recovery.SecondEmailCancelStatus, cancellationToken),
            await CancelScheduledEmailAsync(recovery.FinalEmailId, recovery.FinalEmailCancelStatus, cancellationToken));

    private async Task<EmailCancellationResult> CancelScheduledEmailAsync(
        string? emailId,
        string? existingCancelStatus,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(existingCancelStatus))
        {
            return new EmailCancellationResult(existingCancelStatus.Trim(), null, null, ShouldPatch: false);
        }

        var attemptedAtUtc = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(emailId))
        {
            return new EmailCancellationResult(EmailCancelStatusMissing, attemptedAtUtc, null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.resend.com/emails/{Uri.EscapeDataString(emailId)}/cancel");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new EmailCancellationResult(EmailCancelStatusCancelled, attemptedAtUtc, null);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new EmailCancellationResult(EmailCancelStatusMissing, attemptedAtUtc, TruncateCancelError(body));
            }

            var error = TruncateCancelError($"{(int)response.StatusCode}: {body}");
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                IsNotCancellableResendResponse(body))
            {
                return new EmailCancellationResult(EmailCancelStatusNotCancellable, attemptedAtUtc, error);
            }

            _logger.LogError(
                "Abandoned cart scheduled email cancel failed. email_id={EmailId} status={StatusCode} body={Body}",
                emailId,
                (int)response.StatusCode,
                body);
            return new EmailCancellationResult(EmailCancelStatusFailed, attemptedAtUtc, error);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(exception, "Abandoned cart scheduled email cancel failed unexpectedly. email_id={EmailId}", emailId);
            return new EmailCancellationResult(
                EmailCancelStatusFailed,
                attemptedAtUtc,
                TruncateCancelError(exception.Message));
        }
    }

    private async Task MarkResolvedAsync(
        Uri baseUri,
        string apiKey,
        string recoveryId,
        string resolution,
        EmailCancellationSummary cancellationSummary,
        CancellationToken cancellationToken)
    {
        var payload = BuildCancellationStatusPayload(cancellationSummary);
        payload["resolved_at"] = DateTimeOffset.UtcNow.UtcDateTime;
        payload["resolution"] = resolution;
        var uri = new Uri(baseUri, $"rest/v1/abandoned_cart_recoveries?recovery_id=eq.{Uri.EscapeDataString(recoveryId)}");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Abandoned cart recovery resolve failed. recovery_id={RecoveryId} status={StatusCode} body={Body}",
                recoveryId,
                (int)response.StatusCode,
                body);
        }
    }

    private async Task StoreCancellationStatusesAsync(
        Uri baseUri,
        string apiKey,
        string recoveryId,
        EmailCancellationSummary cancellationSummary,
        CancellationToken cancellationToken)
    {
        var payload = BuildCancellationStatusPayload(cancellationSummary);
        var uri = new Uri(baseUri, $"rest/v1/abandoned_cart_recoveries?recovery_id=eq.{Uri.EscapeDataString(recoveryId)}");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError(
            "Abandoned cart recovery cancellation-status update failed. recovery_id={RecoveryId} status={StatusCode} body={Body}",
            recoveryId,
            (int)response.StatusCode,
            body);
    }

    private static Dictionary<string, object?> BuildCancellationStatusPayload(EmailCancellationSummary summary)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddCancellationStatus(payload, "first", summary.First);
        AddCancellationStatus(payload, "second", summary.Second);
        AddCancellationStatus(payload, "final", summary.Final);
        return payload;
    }

    private static void AddCancellationStatus(
        Dictionary<string, object?> payload,
        string prefix,
        EmailCancellationResult result)
    {
        if (!result.ShouldPatch)
        {
            return;
        }

        payload[$"{prefix}_email_cancel_status"] = result.Status;
        payload[$"{prefix}_email_cancel_attempted_at"] = result.AttemptedAtUtc?.UtcDateTime;
        payload[$"{prefix}_email_cancel_error"] = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error;
    }

    private bool IsSupabaseConfigured() =>
        !string.IsNullOrWhiteSpace(_supabaseOptions.Url) &&
        !string.IsNullOrWhiteSpace(_supabaseOptions.SecretKey);

    private bool IsResendConfigured()
    {
        var templates = _resendOptions.Templates.AbandonedCartRecovery;
        return !string.IsNullOrWhiteSpace(_resendOptions.ApiKey) &&
               !string.IsNullOrWhiteSpace(_resendOptions.FromEmail) &&
               !string.IsNullOrWhiteSpace(templates.Hour1TemplateId) &&
               !string.IsNullOrWhiteSpace(templates.Hour24TemplateId) &&
               !string.IsNullOrWhiteSpace(templates.Day7TemplateId);
    }

    private static bool IsValidSourceType(string sourceType) =>
        string.Equals(sourceType, SubscriptionSourceType, StringComparison.Ordinal) ||
        string.Equals(sourceType, StoreOrderSourceType, StringComparison.Ordinal);

    private Dictionary<string, object?> BuildTemplateVariables(
        AbandonedCartRecoveryStartRequest request,
        string continueUrl,
        string optOutUrl) =>
        new(StringComparer.Ordinal)
        {
            ["CUSTOMER_NAME"] = string.IsNullOrWhiteSpace(request.CustomerName) ? "daar" : request.CustomerName.Trim(),
            ["ITEM_NAME"] = request.ItemName,
            ["ITEM_SUMMARY"] = request.ItemSummary,
            ["CART_TOTAL"] = request.CartTotalZar is null
                ? string.Empty
                : $"R {request.CartTotalZar.Value.ToString("0.00", CultureInfo.InvariantCulture)}",
            ["CHECKOUT_URL"] = continueUrl,
            ["OPTOUT_URL"] = optOutUrl,
            ["SUPPORT_EMAIL"] = string.IsNullOrWhiteSpace(_resendOptions.ToEmail)
                ? "vanderwaltluan@gmail.com"
                : _resendOptions.ToEmail
        };

    private static string BuildOptOutUrl(string baseUrl, string recoveryId, string token)
    {
        var fallbackBaseUrl = Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl)
            ? parsedBaseUrl
            : new Uri("https://www.schink.co.za");
        return new Uri(
            fallbackBaseUrl,
            $"/betaalherinneringe/stop?id={Uri.EscapeDataString(recoveryId)}&token={Uri.EscapeDataString(token)}").ToString();
    }

    private static string BuildContinueUrl(string baseUrl, string recoveryId, string token)
    {
        var fallbackBaseUrl = Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl)
            ? parsedBaseUrl
            : new Uri("https://www.schink.co.za");
        return new Uri(
            fallbackBaseUrl,
            $"/betaalherinneringe/gaan?id={Uri.EscapeDataString(recoveryId)}&token={Uri.EscapeDataString(token)}").ToString();
    }

    private static string CreateOptOutToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, string apiKey, object payload, string preferHeader)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("Prefer", preferHeader);
        return request;
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = default!;
        return Uri.TryCreate(_supabaseOptions.Url, UriKind.Absolute, out baseUri!);
    }

    private string ResolveApiKey() => _supabaseOptions.SecretKey;

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string? TruncateCancelError(string? value) =>
        NormalizeOptionalText(value, MaxCancelErrorLength);

    private static bool IsNotCancellableResendResponse(string body) =>
        body.Contains("not scheduled", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("cannot cancel", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("can't cancel", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("already sent", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("already been sent", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("already canceled", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("already cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentlyActivePaidSubscription(ActiveSubscriptionRow row, DateTimeOffset nowUtc)
    {
        if (!string.Equals(row.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(row.TierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (row.CancelledAt is not null && row.CancelledAt <= nowUtc)
        {
            return false;
        }

        if (row.NextRenewalAt is not null && row.NextRenewalAt < nowUtc)
        {
            return false;
        }

        return true;
    }

    private static bool IsRecoveryBlockedByActiveTier(string requestedTierCode, string? activeTierCode)
    {
        if (string.IsNullOrWhiteSpace(activeTierCode))
        {
            return false;
        }

        return !string.Equals(activeTierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecoveryResolvedByPurchasedTier(string purchasedTierCode, string recoveryTierCode)
    {
        if (string.Equals(recoveryTierCode, purchasedTierCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(recoveryTierCode, StoryCornerTierCode, StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(purchasedTierCode, AllStoriesMonthlyTierCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(purchasedTierCode, AllStoriesYearlyTierCode, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class SubscriberLookupRow
    {
        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; set; }

        [JsonPropertyName("disabled_at")]
        public DateTimeOffset? DisabledAt { get; set; }
    }

    private sealed class ActiveSubscriptionRow
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }

        [JsonPropertyName("cancelled_at")]
        public DateTimeOffset? CancelledAt { get; set; }

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }
    }

    private sealed class AbandonedRecoveryRow
    {
        [JsonPropertyName("recovery_id")]
        public string RecoveryId { get; set; } = string.Empty;

        [JsonPropertyName("source_type")]
        public string SourceType { get; set; } = string.Empty;

        [JsonPropertyName("source_key")]
        public string SourceKey { get; set; } = string.Empty;

        [JsonPropertyName("checkout_reference")]
        public string CheckoutReference { get; set; } = string.Empty;

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("customer_email")]
        public string CustomerEmail { get; set; } = string.Empty;

        [JsonPropertyName("customer_name")]
        public string? CustomerName { get; set; }

        [JsonPropertyName("item_name")]
        public string ItemName { get; set; } = string.Empty;

        [JsonPropertyName("item_summary")]
        public string ItemSummary { get; set; } = string.Empty;

        [JsonPropertyName("cart_total_zar")]
        public decimal? CartTotalZar { get; set; }

        [JsonPropertyName("first_email_id")]
        public string? FirstEmailId { get; set; }

        [JsonPropertyName("second_email_id")]
        public string? SecondEmailId { get; set; }

        [JsonPropertyName("final_email_id")]
        public string? FinalEmailId { get; set; }

        [JsonPropertyName("first_email_cancel_status")]
        public string? FirstEmailCancelStatus { get; set; }

        [JsonPropertyName("second_email_cancel_status")]
        public string? SecondEmailCancelStatus { get; set; }

        [JsonPropertyName("final_email_cancel_status")]
        public string? FinalEmailCancelStatus { get; set; }

        [JsonPropertyName("resolved_at")]
        public DateTimeOffset? ResolvedAt { get; set; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; set; }
    }

    private sealed record RecoveryEmailIds(
        string? FirstEmailId,
        string? SecondEmailId,
        string? FinalEmailId);

    private sealed record EmailCancellationSummary(
        EmailCancellationResult First,
        EmailCancellationResult Second,
        EmailCancellationResult Final);

    private sealed record EmailCancellationResult(
        string Status,
        DateTimeOffset? AttemptedAtUtc,
        string? Error,
        bool ShouldPatch = true);

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
        [property: JsonPropertyName("html"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Html,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? ReplyTo,
        [property: JsonPropertyName("scheduled_at")] string ScheduledAt,
        [property: JsonPropertyName("template")] ResendTemplateRequest Template);

    private sealed record ResendTemplateRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, object?> Variables);

    private sealed record ResendEmailResponse(
        [property: JsonPropertyName("id")] string Id);
}
