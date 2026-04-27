using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed partial class SupabaseSubscriptionLedgerService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    ISubscriptionPaymentRecoveryEmailService subscriptionPaymentRecoveryEmailService,
    ISubscriptionNotificationEmailService subscriptionNotificationEmailService,
    ILogger<SupabaseSubscriptionLedgerService> logger) : ISubscriptionLedgerService
{
    private const string GratisTierCode = "gratis";
    private const string GratisProvider = "paystack";
    private const string GratisPlanSlug = "gratis";
    private static readonly TimeSpan PaymentRecoveryGracePeriod = TimeSpan.FromDays(4);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly ISubscriptionPaymentRecoveryEmailService _subscriptionPaymentRecoveryEmailService = subscriptionPaymentRecoveryEmailService;
    private readonly ISubscriptionNotificationEmailService _subscriptionNotificationEmailService = subscriptionNotificationEmailService;
    private readonly ILogger<SupabaseSubscriptionLedgerService> _logger = logger;

    public async Task<bool> HasActivePaidSubscriptionAsync(string? email, CancellationToken cancellationToken = default)
    {
        return await HasActiveSubscriptionAsync(
            email,
            tierCode: null,
            excludeGratisTier: true,
            cancellationToken);
    }

    public async Task<bool> HasActiveSubscriptionForTierAsync(string? email, string? tierCode, CancellationToken cancellationToken = default)
    {
        return await HasActiveSubscriptionAsync(
            email,
            tierCode,
            excludeGratisTier: false,
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetActiveTierCodesAsync(string? email, CancellationToken cancellationToken = default)
    {
        var activeSubscriptions = await GetActiveSubscriptionsAsync(email, cancellationToken);
        if (activeSubscriptions.Count == 0)
        {
            return [];
        }

        return activeSubscriptions
            .Select(subscription => subscription.TierCode)
            .Where(tierCode => !string.IsNullOrWhiteSpace(tierCode))
            .Select(tierCode => tierCode!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<SubscriberProfile?> GetSubscriberProfileAsync(string? email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase subscriber profile lookup skipped: URL is not configured.");
            return null;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase subscriber profile lookup skipped: ServiceRoleKey is not configured.");
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscribers?select=email,first_name,last_name,display_name,mobile_number,profile_image_url,profile_image_object_key,profile_image_content_type&email=eq.{escapedEmail}&limit=1");

        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase subscriber profile lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    responseBody);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var profiles = await JsonSerializer.DeserializeAsync<List<SubscriberProfileRow>>(stream, cancellationToken: cancellationToken)
                ?? [];
            var profile = profiles.FirstOrDefault();
            if (profile is null)
            {
                return new SubscriberProfile(normalizedEmail, null, null, null, null, null, null, null);
            }

            return new SubscriberProfile(
                Email: string.IsNullOrWhiteSpace(profile.Email) ? normalizedEmail : profile.Email.Trim().ToLowerInvariant(),
                FirstName: NormalizeOptionalText(profile.FirstName, 80),
                LastName: NormalizeOptionalText(profile.LastName, 80),
                DisplayName: NormalizeOptionalText(profile.DisplayName, 120),
                MobileNumber: NormalizeOptionalText(profile.MobileNumber, 32),
                ProfileImageUrl: NormalizeOptionalText(profile.ProfileImageUrl, 2048),
                ProfileImageObjectKey: NormalizeOptionalText(profile.ProfileImageObjectKey, 512),
                ProfileImageContentType: NormalizeOptionalText(profile.ProfileImageContentType, 128));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase subscriber profile lookup failed unexpectedly.");
            return null;
        }
    }

    public async Task<bool> UpsertSubscriberProfileAsync(
        string? email,
        string? firstName,
        string? lastName,
        string? displayName,
        string? mobileNumber,
        string? profileImageUrl = null,
        string? profileImageObjectKey = null,
        string? profileImageContentType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase subscriber upsert skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase subscriber upsert skipped: ServiceRoleKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await UpsertSubscriberAsync(
                baseUri,
                apiKey,
                email,
                firstName ?? string.Empty,
                lastName ?? string.Empty,
                displayName,
                mobileNumber,
                profileImageUrl,
                profileImageObjectKey,
                profileImageContentType,
                cancellationToken);
            return !string.IsNullOrWhiteSpace(subscriberId);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase subscriber upsert failed unexpectedly.");
            return false;
        }
    }

    public async Task<SubscriberEmailChangeResult> ChangeSubscriberEmailAsync(
        string? currentEmail,
        string? newEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentEmail) || string.IsNullOrWhiteSpace(newEmail))
        {
            return new SubscriberEmailChangeResult(false, "Gebruik asseblief geldige e-posadresse.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase subscriber email change skipped: URL is not configured.");
            return new SubscriberEmailChangeResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase subscriber email change skipped: ServiceRoleKey is not configured.");
            return new SubscriberEmailChangeResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
        }

        var normalizedCurrentEmail = currentEmail.Trim().ToLowerInvariant();
        var normalizedNewEmail = newEmail.Trim().ToLowerInvariant();
        if (string.Equals(normalizedCurrentEmail, normalizedNewEmail, StringComparison.Ordinal))
        {
            return new SubscriberEmailChangeResult(true);
        }

        try
        {
            var currentSubscriberId = await FindSubscriberIdByEmailAsync(
                baseUri,
                apiKey,
                normalizedCurrentEmail,
                cancellationToken);
            var targetSubscriberId = await FindSubscriberIdByEmailAsync(
                baseUri,
                apiKey,
                normalizedNewEmail,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(currentSubscriberId))
            {
                return string.IsNullOrWhiteSpace(targetSubscriberId)
                    ? new SubscriberEmailChangeResult(true)
                    : new SubscriberEmailChangeResult(false, "Daardie e-posadres word reeds deur 'n ander rekening gebruik.");
            }

            if (!string.IsNullOrWhiteSpace(targetSubscriberId) &&
                !string.Equals(currentSubscriberId, targetSubscriberId, StringComparison.OrdinalIgnoreCase))
            {
                return new SubscriberEmailChangeResult(false, "Daardie e-posadres word reeds deur 'n ander rekening gebruik.");
            }

            var escapedCurrentEmail = Uri.EscapeDataString(normalizedCurrentEmail);
            var updateUri = new Uri(
                baseUri,
                $"rest/v1/subscribers?email=eq.{escapedCurrentEmail}&select=subscriber_id,email");

            using var request = CreateJsonRequest(
                new HttpMethod("PATCH"),
                updateUri,
                apiKey,
                new { email = normalizedNewEmail },
                "return=representation");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase subscriber email change failed. current_email={CurrentEmail} new_email={NewEmail} Status={StatusCode} Body={Body}",
                    normalizedCurrentEmail,
                    normalizedNewEmail,
                    (int)response.StatusCode,
                    responseBody);

                return new SubscriberEmailChangeResult(
                    false,
                    ContainsUniqueEmailViolation(responseBody)
                        ? "Daardie e-posadres word reeds deur 'n ander rekening gebruik."
                        : "Kon nie nou jou intekenaar e-pos opdateer nie.");
            }

            var updatedBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var updatedEmail = ReadFirstStringProperty(updatedBody, "email");
            if (!string.IsNullOrWhiteSpace(updatedEmail) &&
                !string.Equals(updatedEmail.Trim(), normalizedNewEmail, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Supabase subscriber email change returned an unexpected email. current_email={CurrentEmail} new_email={NewEmail} updated_email={UpdatedEmail}",
                    normalizedCurrentEmail,
                    normalizedNewEmail,
                    updatedEmail);
                return new SubscriberEmailChangeResult(false, "Kon nie nou jou intekenaar e-pos opdateer nie.");
            }

            return new SubscriberEmailChangeResult(true);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase subscriber email change failed unexpectedly.");
            return new SubscriberEmailChangeResult(false, "Kon nie nou jou intekenaar e-pos opdateer nie.");
        }
    }

    public async Task<bool> EnsureGratisAccessAsync(
        string? email,
        string? firstName,
        string? lastName,
        string? displayName,
        string? mobileNumber,
        string? profileImageUrl = null,
        string? profileImageObjectKey = null,
        string? profileImageContentType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Gratis provisioning skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Gratis provisioning skipped: ServiceRoleKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await UpsertSubscriberAsync(
                baseUri,
                apiKey,
                email,
                firstName ?? string.Empty,
                lastName ?? string.Empty,
                displayName,
                mobileNumber,
                profileImageUrl,
                profileImageObjectKey,
                profileImageContentType,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return false;
            }

            var gratisTierReady = await EnsureGratisTierAsync(baseUri, apiKey, cancellationToken);
            if (!gratisTierReady)
            {
                return false;
            }

            var subscriptionId = await UpsertSubscriptionAsync(
                baseUri,
                apiKey,
                subscriberId,
                GratisTierCode,
                provider: GratisProvider,
                providerPaymentId: BuildGratisProviderPaymentId(subscriberId),
                providerTransactionId: null,
                providerToken: null,
                subscribedAtUtc: DateTimeOffset.UtcNow,
                nextRenewalAtUtc: null,
                cancellationToken);

            return !string.IsNullOrWhiteSpace(subscriptionId);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Gratis provisioning failed unexpectedly.");
            return false;
        }
    }

    public async Task ProcessExpiredPaymentRecoveriesAsync(CancellationToken cancellationToken = default)
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

        IReadOnlyList<PaymentRecoveryRow> dueRecoveries;
        try
        {
            dueRecoveries = await GetDuePaymentRecoveriesAsync(baseUri, apiKey, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Expired subscription payment recovery lookup failed unexpectedly.");
            return;
        }

        foreach (var recovery in dueRecoveries)
        {
            if (string.IsNullOrWhiteSpace(recovery.SubscriptionId))
            {
                continue;
            }

            try
            {
                var subscriptionContext = await TryGetSubscriptionContextByIdAsync(
                    baseUri,
                    apiKey,
                    recovery.SubscriptionId,
                    cancellationToken);
                await MarkSubscriptionFailedByIdAsync(baseUri, apiKey, recovery.SubscriptionId, cancellationToken);
                await ResolvePaymentRecoveryAsync(
                    baseUri,
                    apiKey,
                    recovery.RecoveryId,
                    resolvedAtUtc: DateTimeOffset.UtcNow,
                    resolution: "suspended",
                    cancellationToken);
                await TrySendSubscriptionEndedAsync(
                    subscriptionContext,
                    statusLabel: "verval",
                    accessMessage: "Ons kon nie die betaling binne die hersteltydperk bevestig nie, daarom is betaalde toegang gestop. Jou gratis stories bly beskikbaar.",
                    endedAtUtc: DateTimeOffset.UtcNow,
                    idempotencySuffix: $"payment-recovery-expired/{recovery.RecoveryId}",
                    cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(
                    exception,
                    "Subscription payment recovery suspension failed. recovery_id={RecoveryId} subscription_id={SubscriptionId}",
                    recovery.RecoveryId,
                    recovery.SubscriptionId);
                await TrySendAdminOpsAlertAsync(
                    alertKey: $"payment-recovery-suspension-failed/{recovery.RecoveryId}",
                    severity: "error",
                    title: "Subscription suspension failed",
                    summary: "A due payment recovery could not be suspended.",
                    details: $"Recovery ID: {recovery.RecoveryId}\nSubscription ID: {recovery.SubscriptionId}\nError: {exception.Message}",
                    eventReference: recovery.RecoveryId,
                    occurredAtUtc: DateTimeOffset.UtcNow,
                    cancellationToken);
            }
        }
    }

    private async Task<bool> HasActiveSubscriptionAsync(
        string? email,
        string? tierCode,
        bool excludeGratisTier,
        CancellationToken cancellationToken)
    {
        var normalizedTierCode = string.IsNullOrWhiteSpace(tierCode)
            ? null
            : tierCode.Trim();
        var activeSubscriptions = await GetActiveSubscriptionsAsync(email, cancellationToken);
        return activeSubscriptions.Any(row =>
        {
            if (!string.IsNullOrWhiteSpace(normalizedTierCode) &&
                !string.Equals(row.TierCode, normalizedTierCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (excludeGratisTier &&
                string.Equals(row.TierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        });
    }

    private async Task<IReadOnlyList<SubscriptionStatusRow>> GetActiveSubscriptionsAsync(
        string? email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return [];
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase subscription lookup skipped: URL is not configured.");
            return [];
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase subscription lookup skipped: ServiceRoleKey is not configured.");
            return [];
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var subscriberLookupUri = new Uri(baseUri, $"rest/v1/subscribers?select=subscriber_id&email=eq.{escapedEmail}&limit=1");

        try
        {
            using var subscriberRequest = CreateRequest(HttpMethod.Get, subscriberLookupUri, apiKey);
            using var subscriberResponse = await _httpClient.SendAsync(subscriberRequest, cancellationToken);
            if (!subscriberResponse.IsSuccessStatusCode)
            {
                var responseBody = await subscriberResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase subscriber lookup failed. Status={StatusCode} Body={Body}",
                    (int)subscriberResponse.StatusCode,
                    responseBody);
                return [];
            }

            var subscriberResponseBody = await subscriberResponse.Content.ReadAsStringAsync(cancellationToken);
            var subscriberId = ReadFirstStringProperty(subscriberResponseBody, "subscriber_id");
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return [];
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var subscriptionsUri = new Uri(
                baseUri,
                $"rest/v1/subscriptions?select=status,next_renewal_at,cancelled_at,tier_code&subscriber_id=eq.{escapedSubscriberId}&status=eq.active&order=subscribed_at.desc&limit=25");

            using var subscriptionsRequest = CreateRequest(HttpMethod.Get, subscriptionsUri, apiKey);
            using var subscriptionsResponse = await _httpClient.SendAsync(subscriptionsRequest, cancellationToken);
            if (!subscriptionsResponse.IsSuccessStatusCode)
            {
                var responseBody = await subscriptionsResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase subscription lookup failed. Status={StatusCode} Body={Body}",
                    (int)subscriptionsResponse.StatusCode,
                    responseBody);
                return [];
            }

            await using var subscriptionsStream = await subscriptionsResponse.Content.ReadAsStreamAsync(cancellationToken);
            var subscriptions = await JsonSerializer.DeserializeAsync<List<SubscriptionStatusRow>>(subscriptionsStream, cancellationToken: cancellationToken)
                ?? [];
            var nowUtc = DateTimeOffset.UtcNow;

            return subscriptions
                .Where(row => IsCurrentlyActiveSubscription(row, nowUtc))
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase subscription lookup failed unexpectedly.");
            return [];
        }
    }

    private async Task<string?> FindSubscriberIdByEmailAsync(
        Uri baseUri,
        string apiKey,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var lookupUri = new Uri(
            baseUri,
            $"rest/v1/subscribers?select=subscriber_id&email=eq.{escapedEmail}&limit=1");

        using var request = CreateRequest(HttpMethod.Get, lookupUri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscriber email lookup failed. email={Email} Status={StatusCode} Body={Body}",
                normalizedEmail,
                (int)response.StatusCode,
                responseBody);
            return null;
        }

        var lookupBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReadFirstStringProperty(lookupBody, "subscriber_id");
    }

    private static bool IsCurrentlyActiveSubscription(SubscriptionStatusRow row, DateTimeOffset nowUtc)
    {
        if (!string.Equals(row.Status, "active", StringComparison.OrdinalIgnoreCase))
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

    public async Task<SubscriptionPersistResult> RecordPayFastEventAsync(IFormCollection formCollection, CancellationToken cancellationToken = default)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new SubscriptionPersistResult(false, "Supabase URL is not configured.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new SubscriptionPersistResult(false, "Supabase ServiceRoleKey is not configured.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var paymentStatus = formCollection["payment_status"].ToString().Trim();
        var mPaymentId = formCollection["m_payment_id"].ToString().Trim();
        var pfPaymentId = formCollection["pf_payment_id"].ToString().Trim();
        var rawPayload = formCollection.ToDictionary(field => field.Key, field => field.Value.ToString());
        var duplicateEvent = await TryGetExistingSubscriptionEventAsync(
            baseUri,
            apiKey,
            provider: "payfast",
            providerPaymentId: mPaymentId,
            providerTransactionId: pfPaymentId,
            eventType: "payfast_itn",
            cancellationToken);
        if (duplicateEvent is not null)
        {
            return new SubscriptionPersistResult(true, null, duplicateEvent.SubscriptionId);
        }

        string? subscriptionId = null;
        PaymentRecoverySubscriptionContext? payFastContext = null;
        if (string.Equals(paymentStatus, "COMPLETE", StringComparison.OrdinalIgnoreCase))
        {
            var upsertResult = await UpsertActivePayFastSubscriptionAsync(baseUri, apiKey, formCollection, nowUtc, cancellationToken);
            if (!upsertResult.IsSuccess)
            {
                await TrySendAdminOpsAlertAsync(
                    alertKey: $"payfast-upsert-failed/{mPaymentId}/{pfPaymentId}",
                    severity: "error",
                    title: "PayFast subscription persist failed",
                    summary: upsertResult.ErrorMessage ?? "PayFast subscription could not be persisted.",
                    details: $"Merchant payment ID: {mPaymentId}\nPayFast payment ID: {pfPaymentId}\nStatus: {paymentStatus}",
                    eventReference: string.IsNullOrWhiteSpace(mPaymentId) ? pfPaymentId : mPaymentId,
                    occurredAtUtc: nowUtc,
                    cancellationToken);
                return upsertResult;
            }

            subscriptionId = upsertResult.SubscriptionId;
            if (!string.IsNullOrWhiteSpace(mPaymentId))
            {
                payFastContext = await TryGetSubscriptionContextByProviderPaymentIdAsync(
                    baseUri,
                    apiKey,
                    provider: "payfast",
                    providerPaymentId: mPaymentId,
                    cancellationToken);
            }

            if (payFastContext is not null)
            {
                await TryResolvePaymentRecoveryAfterSuccessfulChargeAsync(baseUri, apiKey, payFastContext, nowUtc, cancellationToken);
            }

            var plan = PaymentPlanCatalog.FindBySlug(ResolvePayFastPlanSlug(formCollection));
            await TrySendSubscriptionConfirmationAsync(
                subscriptionId,
                formCollection["email_address"].ToString(),
                formCollection["name_first"].ToString(),
                displayName: null,
                plan,
                provider: "PayFast",
                paymentReference: string.IsNullOrWhiteSpace(pfPaymentId) ? mPaymentId : pfPaymentId,
                nextRenewalAtUtc: plan is null ? null : nowUtc.AddMonths(plan.BillingPeriodMonths),
                occurredAtUtc: nowUtc,
                cancellationToken);
        }
        else if (string.Equals(paymentStatus, "CANCELLED", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(mPaymentId))
        {
            subscriptionId = await MarkSubscriptionStatusAsync(
                baseUri,
                apiKey,
                provider: "payfast",
                providerPaymentId: mPaymentId,
                status: "cancelled",
                changedAtUtc: nowUtc,
                cancellationToken);

            payFastContext = await TryGetSubscriptionContextByProviderPaymentIdAsync(
                baseUri,
                apiKey,
                provider: "payfast",
                providerPaymentId: mPaymentId,
                cancellationToken);
            if (payFastContext is not null)
            {
                await TryResolvePaymentRecoveryAfterCancellationAsync(baseUri, apiKey, payFastContext, nowUtc, cancellationToken);
                await TrySendSubscriptionEndedAsync(
                    payFastContext,
                    statusLabel: "gekanselleer",
                    accessMessage: "Jou betaalde toegang is gekanselleer. Jou gratis stories bly beskikbaar, en jy kan enige tyd weer 'n plan kies.",
                    endedAtUtc: nowUtc,
                    idempotencySuffix: $"payfast-cancelled/{mPaymentId}",
                    cancellationToken);
            }
        }
        else if (IsPayFastFailureStatus(paymentStatus) &&
                 !string.IsNullOrWhiteSpace(mPaymentId))
        {
            payFastContext = await TryGetSubscriptionContextByProviderPaymentIdAsync(
                baseUri,
                apiKey,
                provider: "payfast",
                providerPaymentId: mPaymentId,
                cancellationToken);
            if (payFastContext is not null)
            {
                subscriptionId = payFastContext.SubscriptionId;
                await TryStartPaymentRecoveryAsync(
                    baseUri,
                    apiKey,
                    payFastContext,
                    providerPaymentId: mPaymentId,
                    provider: "payfast",
                    nowUtc,
                    cancellationToken);
            }
        }

        var eventInserted = await InsertSubscriptionEventAsync(
            baseUri,
            apiKey,
            subscriptionId,
            provider: "payfast",
            providerPaymentId: mPaymentId,
            providerTransactionId: pfPaymentId,
            eventType: "payfast_itn",
            eventStatus: paymentStatus,
            payload: rawPayload,
            cancellationToken);

        if (!eventInserted.IsSuccess)
        {
            await TrySendAdminOpsAlertAsync(
                alertKey: $"payfast-event-insert-failed/{mPaymentId}/{pfPaymentId}/{paymentStatus}",
                severity: "error",
                title: "PayFast event insert failed",
                summary: "A PayFast subscription event could not be persisted.",
                details: $"Merchant payment ID: {mPaymentId}\nPayFast payment ID: {pfPaymentId}\nStatus: {paymentStatus}\nSubscription ID: {subscriptionId ?? "not available"}",
                eventReference: string.IsNullOrWhiteSpace(mPaymentId) ? pfPaymentId : mPaymentId,
                occurredAtUtc: nowUtc,
                cancellationToken);
            return new SubscriptionPersistResult(false, "Could not persist subscription event.", subscriptionId);
        }

        return new SubscriptionPersistResult(true, null, subscriptionId);
    }

    public async Task<SubscriptionPersistResult> RecordPaystackEventAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new SubscriptionPersistResult(false, "Supabase URL is not configured.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new SubscriptionPersistResult(false, "Supabase ServiceRoleKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new SubscriptionPersistResult(false, "Paystack payload is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return new SubscriptionPersistResult(false, "Paystack payload is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new SubscriptionPersistResult(false, "Paystack payload root is invalid.");
            }

            var eventType = TryReadString(root, "event") ?? "unknown";
            var data = root.TryGetProperty("data", out var dataNode) && dataNode.ValueKind == JsonValueKind.Object
                ? dataNode
                : default;

            var providerPaymentId = ResolvePaystackProviderPaymentId(data);
            var providerTransactionId = ResolvePaystackProviderTransactionId(data);
            var eventStatus = ResolvePaystackEventStatus(eventType, data);
            var nowUtc = DateTimeOffset.UtcNow;
            var duplicateEvent = await TryGetExistingSubscriptionEventAsync(
                baseUri,
                apiKey,
                provider: "paystack",
                providerPaymentId: providerPaymentId,
                providerTransactionId: providerTransactionId,
                eventType: eventType,
                cancellationToken);
            if (duplicateEvent is not null)
            {
                return new SubscriptionPersistResult(true, null, duplicateEvent.SubscriptionId);
            }

            string? subscriptionId = null;
            PaymentRecoverySubscriptionContext? paystackContext = null;
            if (ShouldActivatePaystackSubscription(eventType, eventStatus))
            {
                var upsertResult = await UpsertActivePaystackSubscriptionAsync(baseUri, apiKey, data, nowUtc, cancellationToken);
                if (!upsertResult.IsSuccess)
                {
                    await TrySendAdminOpsAlertAsync(
                        alertKey: $"paystack-upsert-failed/{providerPaymentId}/{providerTransactionId}/{eventType}",
                        severity: "error",
                        title: "Paystack subscription persist failed",
                        summary: upsertResult.ErrorMessage ?? "Paystack subscription could not be persisted.",
                        details: $"Event type: {eventType}\nEvent status: {eventStatus ?? "not available"}\nProvider payment ID: {providerPaymentId ?? "not available"}\nProvider transaction ID: {providerTransactionId ?? "not available"}",
                        eventReference: providerPaymentId ?? providerTransactionId ?? eventType,
                        occurredAtUtc: nowUtc,
                        cancellationToken);
                    return new SubscriptionPersistResult(false, upsertResult.ErrorMessage, upsertResult.SubscriptionId);
                }

                subscriptionId = upsertResult.SubscriptionId;
                providerPaymentId ??= upsertResult.ProviderPaymentId;
                providerTransactionId ??= upsertResult.ProviderTransactionId;

                if (!string.IsNullOrWhiteSpace(providerPaymentId))
                {
                    paystackContext = await TryGetSubscriptionContextByProviderPaymentIdAsync(
                        baseUri,
                        apiKey,
                        provider: "paystack",
                        providerPaymentId,
                        cancellationToken);
                }

                if (paystackContext is not null)
                {
                    await TryResolvePaymentRecoveryAfterSuccessfulChargeAsync(baseUri, apiKey, paystackContext, nowUtc, cancellationToken);
                }

                var plan = await ResolvePaystackPlanAsync(baseUri, apiKey, data, cancellationToken);
                var subscribedAtUtc = TryParseDateTimeOffset(
                    TryReadString(data, "paid_at") ??
                    TryReadString(data, "transaction_date")) ?? nowUtc;
                await TrySendSubscriptionConfirmationAsync(
                    subscriptionId,
                    TryReadNestedString(data, "customer", "email"),
                    TryReadNestedString(data, "customer", "first_name"),
                    TryReadNestedString(data, "customer", "display_name"),
                    plan,
                    provider: "Paystack",
                    paymentReference: providerTransactionId ?? providerPaymentId,
                    nextRenewalAtUtc: plan is null ? null : subscribedAtUtc.AddMonths(plan.BillingPeriodMonths),
                    occurredAtUtc: nowUtc,
                    cancellationToken);
            }
            else if (ShouldCancelPaystackSubscription(eventType, eventStatus) &&
                     !string.IsNullOrWhiteSpace(providerPaymentId))
            {
                subscriptionId = await MarkSubscriptionStatusAsync(
                    baseUri,
                    apiKey,
                    provider: "paystack",
                    providerPaymentId: providerPaymentId,
                    status: "cancelled",
                    changedAtUtc: nowUtc,
                    cancellationToken);

                paystackContext = await TryGetSubscriptionContextByProviderPaymentIdAsync(
                    baseUri,
                    apiKey,
                    provider: "paystack",
                    providerPaymentId,
                    cancellationToken);
                if (paystackContext is not null)
                {
                    await TryResolvePaymentRecoveryAfterCancellationAsync(baseUri, apiKey, paystackContext, nowUtc, cancellationToken);
                    await TrySendSubscriptionEndedAsync(
                        paystackContext,
                        statusLabel: "gekanselleer",
                        accessMessage: "Jou betaalde toegang is gekanselleer. Jou gratis stories bly beskikbaar, en jy kan enige tyd weer 'n plan kies.",
                        endedAtUtc: nowUtc,
                        idempotencySuffix: $"paystack-cancelled/{providerPaymentId}",
                        cancellationToken);
                }
            }
            else if (ShouldFailPaystackSubscription(eventType, eventStatus) &&
                     !string.IsNullOrWhiteSpace(providerPaymentId))
            {
                paystackContext = await TryGetSubscriptionContextByProviderPaymentIdAsync(
                    baseUri,
                    apiKey,
                    provider: "paystack",
                    providerPaymentId,
                    cancellationToken);
                if (paystackContext is not null)
                {
                    subscriptionId = paystackContext.SubscriptionId;
                    await TryStartPaymentRecoveryAsync(
                        baseUri,
                        apiKey,
                        paystackContext,
                        providerPaymentId,
                        provider: "paystack",
                        nowUtc,
                        cancellationToken,
                        isRecurringFailure: IsPaystackRecoverableFailureEvent(eventType, eventStatus));
                }
            }

            var normalizedPayload = DeserializePayloadObject(payloadJson);
            var eventInserted = await InsertSubscriptionEventAsync(
                baseUri,
                apiKey,
                subscriptionId,
                provider: "paystack",
                providerPaymentId: providerPaymentId,
                providerTransactionId: providerTransactionId,
                eventType: eventType,
                eventStatus: eventStatus,
                payload: normalizedPayload,
                cancellationToken: cancellationToken);

            if (!eventInserted.IsSuccess)
            {
                await TrySendAdminOpsAlertAsync(
                    alertKey: $"paystack-event-insert-failed/{providerPaymentId}/{providerTransactionId}/{eventType}",
                    severity: "error",
                    title: "Paystack event insert failed",
                    summary: "A Paystack subscription event could not be persisted.",
                    details: $"Event type: {eventType}\nEvent status: {eventStatus ?? "not available"}\nProvider payment ID: {providerPaymentId ?? "not available"}\nProvider transaction ID: {providerTransactionId ?? "not available"}\nSubscription ID: {subscriptionId ?? "not available"}",
                    eventReference: providerPaymentId ?? providerTransactionId ?? eventType,
                    occurredAtUtc: nowUtc,
                    cancellationToken);
                return new SubscriptionPersistResult(false, "Could not persist Paystack event.", subscriptionId);
            }

            return new SubscriptionPersistResult(true, null, subscriptionId);
        }
    }

    private async Task<SubscriptionPersistResult> UpsertActivePayFastSubscriptionAsync(
        Uri baseUri,
        string apiKey,
        IFormCollection formCollection,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var email = formCollection["email_address"].ToString().Trim();
        var mPaymentId = formCollection["m_payment_id"].ToString().Trim();
        var pfPaymentId = formCollection["pf_payment_id"].ToString().Trim();
        var token = formCollection["token"].ToString().Trim();
        var planSlug = ResolvePayFastPlanSlug(formCollection);
        var plan = PaymentPlanCatalog.FindBySlug(planSlug);

        if (string.IsNullOrWhiteSpace(email))
        {
            return new SubscriptionPersistResult(false, "No subscriber email was present in the PayFast ITN payload.");
        }

        if (string.IsNullOrWhiteSpace(mPaymentId))
        {
            return new SubscriptionPersistResult(false, "No merchant payment id was present in the PayFast ITN payload.");
        }

        if (plan is null)
        {
            return new SubscriptionPersistResult(false, "Could not resolve subscription plan from PayFast payload.");
        }

        var subscriberId = await UpsertSubscriberAsync(
            baseUri,
            apiKey,
            email,
            formCollection["name_first"].ToString().Trim(),
            formCollection["name_last"].ToString().Trim(),
            displayName: null,
            mobileNumber: null,
            profileImageUrl: null,
            profileImageObjectKey: null,
            profileImageContentType: null,
            cancellationToken);

        if (subscriberId is null)
        {
            return new SubscriptionPersistResult(false, "Could not upsert subscriber profile.");
        }

        var nextRenewalAtUtc = nowUtc.AddMonths(plan.BillingPeriodMonths);
        var subscriptionId = await UpsertSubscriptionAsync(
            baseUri,
            apiKey,
            subscriberId,
            plan.TierCode,
            provider: "payfast",
            providerPaymentId: mPaymentId,
            providerTransactionId: pfPaymentId,
            providerToken: token,
            subscribedAtUtc: nowUtc,
            nextRenewalAtUtc,
            cancellationToken);

        if (subscriptionId is null)
        {
            return new SubscriptionPersistResult(false, "Could not upsert subscription record.");
        }

        return new SubscriptionPersistResult(true, null, subscriptionId);
    }

    private async Task<PaystackUpsertResult> UpsertActivePaystackSubscriptionAsync(
        Uri baseUri,
        string apiKey,
        JsonElement data,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var email = TryReadNestedString(data, "customer", "email");
        var providerPaymentId = ResolvePaystackProviderPaymentId(data);
        var providerTransactionId = ResolvePaystackProviderTransactionId(data);

        if (string.IsNullOrWhiteSpace(email))
        {
            return new PaystackUpsertResult(false, "No subscriber email was present in the Paystack payload.");
        }

        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return new PaystackUpsertResult(false, "No Paystack payment identifier was present in the payload.");
        }

        var plan = await ResolvePaystackPlanAsync(baseUri, apiKey, data, cancellationToken);
        if (plan is null)
        {
            return new PaystackUpsertResult(false, "Could not resolve subscription plan from Paystack payload.");
        }

        var subscriberId = await UpsertSubscriberAsync(
            baseUri,
            apiKey,
            email,
            TryReadNestedString(data, "customer", "first_name") ?? string.Empty,
            TryReadNestedString(data, "customer", "last_name") ?? string.Empty,
            displayName: TryReadNestedString(data, "customer", "display_name"),
            mobileNumber: TryReadNestedString(data, "customer", "phone"),
            profileImageUrl: null,
            profileImageObjectKey: null,
            profileImageContentType: null,
            cancellationToken);

        if (subscriberId is null)
        {
            return new PaystackUpsertResult(false, "Could not upsert subscriber profile.");
        }

        var providerToken = TryReadNestedString(data, "authorization", "authorization_code")
            ?? TryReadString(data, "authorization_code");

        var subscribedAtUtc = TryParseDateTimeOffset(
            TryReadString(data, "paid_at") ??
            TryReadString(data, "transaction_date")) ?? nowUtc;

        var nextRenewalAtUtc = subscribedAtUtc.AddMonths(plan.BillingPeriodMonths);
        var subscriptionId = await UpsertSubscriptionAsync(
            baseUri,
            apiKey,
            subscriberId,
            plan.TierCode,
            provider: "paystack",
            providerPaymentId: providerPaymentId,
            providerTransactionId: providerTransactionId,
            providerToken: providerToken,
            subscribedAtUtc: subscribedAtUtc,
            nextRenewalAtUtc: nextRenewalAtUtc,
            cancellationToken: cancellationToken);

        if (subscriptionId is null)
        {
            return new PaystackUpsertResult(false, "Could not upsert Paystack subscription record.");
        }

        return new PaystackUpsertResult(true, null, subscriptionId, providerPaymentId, providerTransactionId);
    }

    private async Task<PaymentPlan?> ResolvePaystackPlanAsync(
        Uri baseUri,
        string apiKey,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        var metadataTierCode = TryReadNestedString(data, "metadata", "tier_code");
        var planFromTierCode = PaymentPlanCatalog.FindByTierCode(metadataTierCode);
        if (planFromTierCode is not null)
        {
            return planFromTierCode;
        }

        var metadataPlanSlug = TryReadNestedString(data, "metadata", "plan_slug");
        var planFromSlug = PaymentPlanCatalog.FindBySlug(metadataPlanSlug);
        if (planFromSlug is not null)
        {
            return planFromSlug;
        }

        var paystackPlanCode = TryReadNestedString(data, "plan", "plan_code")
            ?? TryReadString(data, "plan_code");

        if (!string.IsNullOrWhiteSpace(paystackPlanCode))
        {
            var mappedTierCode = await ResolveTierCodeByPaystackPlanCodeAsync(baseUri, apiKey, paystackPlanCode, cancellationToken);
            var planFromPaystackCode = PaymentPlanCatalog.FindByTierCode(mappedTierCode);
            if (planFromPaystackCode is not null)
            {
                return planFromPaystackCode;
            }
        }

        var inferredPlanSlug = ResolvePlanSlugFromPaymentIdentifier(
            TryReadNestedString(data, "metadata", "subscription_key") ??
            TryReadString(data, "reference"));

        return PaymentPlanCatalog.FindBySlug(inferredPlanSlug);
    }

    private async Task<string?> ResolveTierCodeByPaystackPlanCodeAsync(
        Uri baseUri,
        string apiKey,
        string paystackPlanCode,
        CancellationToken cancellationToken)
    {
        var escapedCode = Uri.EscapeDataString(paystackPlanCode);
        var uri = new Uri(baseUri, $"rest/v1/subscription_tiers?paystack_plan_code=eq.{escapedCode}&select=tier_code&limit=1");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase tier lookup failed for paystack_plan_code. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReadFirstStringProperty(responseBody, "tier_code");
    }

    private async Task<bool> EnsureGratisTierAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var payload = new
        {
            tier_code = GratisTierCode,
            display_name = "Gratis",
            description = "Gratis toegang tot Schink Stories se proefstories.",
            billing_period_months = 1,
            price_zar = 0m,
            payfast_plan_slug = GratisPlanSlug,
            paystack_plan_code = (string?)null,
            is_active = true
        };

        var uri = new Uri(baseUri, "rest/v1/subscription_tiers?on_conflict=tier_code");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "resolution=merge-duplicates,return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase gratis tier upsert failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return false;
        }

        return true;
    }

    private async Task<string?> UpsertSubscriberAsync(
        Uri baseUri,
        string apiKey,
        string email,
        string firstName,
        string lastName,
        string? displayName,
        string? mobileNumber,
        string? profileImageUrl,
        string? profileImageObjectKey,
        string? profileImageContentType,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["email"] = email.Trim().ToLowerInvariant()
        };

        if (!string.IsNullOrWhiteSpace(firstName))
        {
            payload["first_name"] = firstName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(lastName))
        {
            payload["last_name"] = lastName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            payload["display_name"] = displayName.Trim();
        }

        var normalizedMobileNumber = NormalizeMobileNumber(mobileNumber);
        if (!string.IsNullOrWhiteSpace(normalizedMobileNumber))
        {
            payload["mobile_number"] = normalizedMobileNumber;
        }

        if (!string.IsNullOrWhiteSpace(profileImageUrl))
        {
            payload["profile_image_url"] = profileImageUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(profileImageObjectKey))
        {
            payload["profile_image_object_key"] = profileImageObjectKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(profileImageContentType))
        {
            payload["profile_image_content_type"] = profileImageContentType.Trim();
        }

        var uri = new Uri(baseUri, "rest/v1/subscribers?on_conflict=email&select=subscriber_id");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscriber upsert failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReadFirstStringProperty(responseBody, "subscriber_id");
    }

    private async Task<string?> UpsertSubscriptionAsync(
        Uri baseUri,
        string apiKey,
        string subscriberId,
        string tierCode,
        string provider,
        string providerPaymentId,
        string? providerTransactionId,
        string? providerToken,
        DateTimeOffset subscribedAtUtc,
        DateTimeOffset? nextRenewalAtUtc,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscriber_id = subscriberId,
            tier_code = tierCode,
            provider,
            provider_payment_id = providerPaymentId,
            provider_transaction_id = string.IsNullOrWhiteSpace(providerTransactionId) ? null : providerTransactionId,
            provider_token = string.IsNullOrWhiteSpace(providerToken) ? null : providerToken,
            status = "active",
            subscribed_at = subscribedAtUtc.UtcDateTime,
            next_renewal_at = nextRenewalAtUtc?.UtcDateTime,
            cancelled_at = (DateTime?)null
        };

        var uri = new Uri(baseUri, "rest/v1/subscriptions?on_conflict=provider,provider_payment_id&select=subscription_id");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "resolution=merge-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscription upsert failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReadFirstStringProperty(responseBody, "subscription_id");
    }

    private async Task<string?> MarkSubscriptionStatusAsync(
        Uri baseUri,
        string apiKey,
        string provider,
        string providerPaymentId,
        string status,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
    {
        var filterProvider = Uri.EscapeDataString(provider);
        var filterPaymentId = Uri.EscapeDataString(providerPaymentId);
        var uri = new Uri(baseUri,
            $"rest/v1/subscriptions?provider=eq.{filterProvider}&provider_payment_id=eq.{filterPaymentId}&select=subscription_id");

        object payload = string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase)
            ? new
            {
                status,
                cancelled_at = changedAtUtc.UtcDateTime
            }
            : new
            {
                status
            };

        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscription status update failed. provider={Provider} status={TargetStatus} Status={StatusCode} Body={Body}",
                provider,
                status,
                (int)response.StatusCode,
                body);
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReadFirstStringProperty(responseBody, "subscription_id");
    }

    private async Task MarkSubscriptionFailedByIdAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriptionId = Uri.EscapeDataString(subscriptionId);
        var uri = new Uri(baseUri, $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}");
        using var request = CreateJsonRequest(
            new HttpMethod("PATCH"),
            uri,
            apiKey,
            new { status = "failed" },
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase subscription final suspend failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
            subscriptionId,
            (int)response.StatusCode,
            body);
    }

    private async Task ExtendSubscriptionGracePeriodAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        DateTimeOffset graceEndsAtUtc,
        CancellationToken cancellationToken)
    {
        var escapedSubscriptionId = Uri.EscapeDataString(subscriptionId);
        var uri = new Uri(baseUri, $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}");
        using var request = CreateJsonRequest(
            new HttpMethod("PATCH"),
            uri,
            apiKey,
            new
            {
                status = "active",
                next_renewal_at = graceEndsAtUtc.UtcDateTime
            },
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase subscription grace-period update failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
            subscriptionId,
            (int)response.StatusCode,
            body);
    }

    private async Task<PaymentRecoverySubscriptionContext?> TryGetSubscriptionContextByProviderPaymentIdAsync(
        Uri baseUri,
        string apiKey,
        string provider,
        string providerPaymentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return null;
        }

        var escapedProvider = Uri.EscapeDataString(provider);
        var escapedPaymentId = Uri.EscapeDataString(providerPaymentId);
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscriptions?provider=eq.{escapedProvider}&provider_payment_id=eq.{escapedPaymentId}&select=subscription_id,subscriber_id,tier_code,status&limit=1");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscription recovery lookup failed. provider={Provider} provider_payment_id={ProviderPaymentId} Status={StatusCode} Body={Body}",
                provider,
                providerPaymentId,
                (int)response.StatusCode,
                body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<PaymentRecoverySubscriptionRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        var row = rows.FirstOrDefault();
        if (row is null ||
            string.IsNullOrWhiteSpace(row.SubscriptionId) ||
            string.IsNullOrWhiteSpace(row.SubscriberId))
        {
            return null;
        }

        var subscriberEmail = await GetSubscriberEmailByIdAsync(baseUri, apiKey, row.SubscriberId, cancellationToken);
        if (string.IsNullOrWhiteSpace(subscriberEmail))
        {
            return null;
        }

        var subscriberProfile = await GetSubscriberNamesByIdAsync(baseUri, apiKey, row.SubscriberId, cancellationToken);
        var planName = PaymentPlanCatalog.FindByTierCode(row.TierCode)?.Name
                       ?? row.TierCode
                       ?? "Schink Stories";

        return new PaymentRecoverySubscriptionContext(
            row.SubscriptionId,
            row.SubscriberId,
            subscriberEmail,
            subscriberProfile?.FirstName,
            subscriberProfile?.DisplayName,
            row.TierCode,
            planName,
            row.Status);
    }

    private async Task<PaymentRecoverySubscriptionContext?> TryGetSubscriptionContextByIdAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return null;
        }

        var escapedSubscriptionId = Uri.EscapeDataString(subscriptionId);
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}&select=subscription_id,subscriber_id,tier_code,status&limit=1");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscription context lookup failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
                subscriptionId,
                (int)response.StatusCode,
                body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<PaymentRecoverySubscriptionRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        var row = rows.FirstOrDefault();
        if (row is null ||
            string.IsNullOrWhiteSpace(row.SubscriptionId) ||
            string.IsNullOrWhiteSpace(row.SubscriberId))
        {
            return null;
        }

        var subscriberEmail = await GetSubscriberEmailByIdAsync(baseUri, apiKey, row.SubscriberId, cancellationToken);
        if (string.IsNullOrWhiteSpace(subscriberEmail))
        {
            return null;
        }

        var subscriberProfile = await GetSubscriberNamesByIdAsync(baseUri, apiKey, row.SubscriberId, cancellationToken);
        var planName = PaymentPlanCatalog.FindByTierCode(row.TierCode)?.Name
                       ?? row.TierCode
                       ?? "Schink Stories";

        return new PaymentRecoverySubscriptionContext(
            row.SubscriptionId,
            row.SubscriberId,
            subscriberEmail,
            subscriberProfile?.FirstName,
            subscriberProfile?.DisplayName,
            row.TierCode,
            planName,
            row.Status);
    }

    private async Task<string?> GetSubscriberEmailByIdAsync(
        Uri baseUri,
        string apiKey,
        string subscriberId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
        var uri = new Uri(baseUri, $"rest/v1/subscribers?subscriber_id=eq.{escapedSubscriberId}&select=email&limit=1");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscriber email lookup failed for recovery. subscriber_id={SubscriberId} Status={StatusCode} Body={Body}",
                subscriberId,
                (int)response.StatusCode,
                body);
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return ReadFirstStringProperty(responseBody, "email");
    }

    private async Task<PaymentRecoverySubscriberRow?> GetSubscriberNamesByIdAsync(
        Uri baseUri,
        string apiKey,
        string subscriberId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
        var uri = new Uri(baseUri, $"rest/v1/subscribers?subscriber_id=eq.{escapedSubscriberId}&select=first_name,display_name&limit=1");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<PaymentRecoverySubscriberRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        return rows.FirstOrDefault();
    }

    private async Task<PaymentRecoveryRow?> GetActivePaymentRecoveryAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriptionId = Uri.EscapeDataString(subscriptionId);
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscription_payment_recoveries?subscription_id=eq.{escapedSubscriptionId}&resolved_at=is.null&select=recovery_id,subscription_id,provider_payment_id,first_failed_at,grace_ends_at,immediate_email_id,warning_email_id,suspension_email_id&limit=1");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase active payment recovery lookup failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
                subscriptionId,
                (int)response.StatusCode,
                body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<PaymentRecoveryRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        return rows.FirstOrDefault();
    }

    private async Task<IReadOnlyList<PaymentRecoveryRow>> GetDuePaymentRecoveriesAsync(
        Uri baseUri,
        string apiKey,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var escapedNow = Uri.EscapeDataString(nowUtc.UtcDateTime.ToString("O"));
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscription_payment_recoveries?resolved_at=is.null&grace_ends_at=lte.{escapedNow}&select=recovery_id,subscription_id,provider_payment_id,first_failed_at,grace_ends_at,immediate_email_id,warning_email_id,suspension_email_id&order=grace_ends_at.asc&limit=100");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase due payment recovery lookup failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<PaymentRecoveryRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
    }

    private async Task<PaymentRecoveryRow?> CreatePaymentRecoveryAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        string provider,
        string providerPaymentId,
        DateTimeOffset firstFailedAtUtc,
        DateTimeOffset graceEndsAtUtc,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscription_id = subscriptionId,
            provider,
            provider_payment_id = providerPaymentId,
            first_failed_at = firstFailedAtUtc.UtcDateTime,
            grace_ends_at = graceEndsAtUtc.UtcDateTime
        };

        var uri = new Uri(baseUri, "rest/v1/subscription_payment_recoveries?select=recovery_id,subscription_id,provider_payment_id,first_failed_at,grace_ends_at,immediate_email_id,warning_email_id,suspension_email_id");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase payment recovery insert failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
                subscriptionId,
                (int)response.StatusCode,
                body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<PaymentRecoveryRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        return rows.FirstOrDefault();
    }

    private async Task StorePaymentRecoveryEmailIdsAsync(
        Uri baseUri,
        string apiKey,
        string recoveryId,
        SubscriptionPaymentRecoveryEmailSequence emailSequence,
        CancellationToken cancellationToken)
    {
        var escapedRecoveryId = Uri.EscapeDataString(recoveryId);
        var uri = new Uri(baseUri, $"rest/v1/subscription_payment_recoveries?recovery_id=eq.{escapedRecoveryId}");
        using var request = CreateJsonRequest(
            new HttpMethod("PATCH"),
            uri,
            apiKey,
            new
            {
                immediate_email_id = emailSequence.ImmediateEmailId,
                warning_email_id = emailSequence.WarningEmailId,
                suspension_email_id = emailSequence.SuspensionEmailId
            },
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase payment recovery email-id update failed. recovery_id={RecoveryId} Status={StatusCode} Body={Body}",
            recoveryId,
            (int)response.StatusCode,
            body);
    }

    private async Task ResolvePaymentRecoveryAsync(
        Uri baseUri,
        string apiKey,
        string recoveryId,
        DateTimeOffset resolvedAtUtc,
        string resolution,
        CancellationToken cancellationToken)
    {
        var escapedRecoveryId = Uri.EscapeDataString(recoveryId);
        var uri = new Uri(baseUri, $"rest/v1/subscription_payment_recoveries?recovery_id=eq.{escapedRecoveryId}");
        using var request = CreateJsonRequest(
            new HttpMethod("PATCH"),
            uri,
            apiKey,
            new
            {
                resolved_at = resolvedAtUtc.UtcDateTime,
                resolution
            },
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase payment recovery resolve failed. recovery_id={RecoveryId} resolution={Resolution} Status={StatusCode} Body={Body}",
            recoveryId,
            resolution,
            (int)response.StatusCode,
            body);
    }

    private async Task<EventInsertResult> InsertSubscriptionEventAsync(
        Uri baseUri,
        string apiKey,
        string? subscriptionId,
        string provider,
        string? providerPaymentId,
        string? providerTransactionId,
        string eventType,
        string? eventStatus,
        object payload,
        CancellationToken cancellationToken)
    {
        var eventPayload = new
        {
            subscription_id = string.IsNullOrWhiteSpace(subscriptionId) ? null : subscriptionId,
            provider,
            provider_payment_id = string.IsNullOrWhiteSpace(providerPaymentId) ? null : providerPaymentId,
            provider_transaction_id = string.IsNullOrWhiteSpace(providerTransactionId) ? null : providerTransactionId,
            event_type = eventType,
            event_status = string.IsNullOrWhiteSpace(eventStatus) ? null : eventStatus,
            payload
        };

        var uri = new Uri(baseUri, "rest/v1/subscription_events");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, eventPayload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscription event insert failed. provider={Provider} event_type={EventType} Status={StatusCode} Body={Body}",
                provider,
                eventType,
                (int)response.StatusCode,
                body);
            return new EventInsertResult(false, false);
        }

        return new EventInsertResult(true, true);
    }

    private async Task<ExistingEventLookupRow?> TryGetExistingSubscriptionEventAsync(
        Uri baseUri,
        string apiKey,
        string provider,
        string? providerPaymentId,
        string? providerTransactionId,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerTransactionId) ||
            string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return null;
        }

        var filters = new List<string>
        {
            $"provider=eq.{Uri.EscapeDataString(provider)}",
            $"provider_payment_id=eq.{Uri.EscapeDataString(providerPaymentId)}",
            $"provider_transaction_id=eq.{Uri.EscapeDataString(providerTransactionId)}",
            $"event_type=eq.{Uri.EscapeDataString(eventType)}",
            "select=subscription_id",
            "limit=1"
        };
        var uri = new Uri(baseUri, $"rest/v1/subscription_events?{string.Join("&", filters)}");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<ExistingEventLookupRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        return rows.FirstOrDefault();
    }

    private async Task TryStartPaymentRecoveryAsync(
        Uri baseUri,
        string apiKey,
        PaymentRecoverySubscriptionContext subscriptionContext,
        string providerPaymentId,
        string provider,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken,
        bool isRecurringFailure = true)
    {
        if (!isRecurringFailure ||
            !string.Equals(subscriptionContext.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var existingRecovery = await GetActivePaymentRecoveryAsync(baseUri, apiKey, subscriptionContext.SubscriptionId, cancellationToken);
        if (existingRecovery is not null)
        {
            return;
        }

        var graceEndsAtUtc = nowUtc.Add(PaymentRecoveryGracePeriod);
        var recovery = await CreatePaymentRecoveryAsync(
            baseUri,
            apiKey,
            subscriptionContext.SubscriptionId,
            provider,
            providerPaymentId,
            nowUtc,
            graceEndsAtUtc,
            cancellationToken);
        if (recovery is null)
        {
            return;
        }

        await ExtendSubscriptionGracePeriodAsync(
            baseUri,
            apiKey,
            subscriptionContext.SubscriptionId,
            graceEndsAtUtc,
            cancellationToken);

        var emailSequence = await _subscriptionPaymentRecoveryEmailService.ScheduleSequenceAsync(
            new SubscriptionPaymentRecoveryEmailRequest(
                recovery.RecoveryId,
                subscriptionContext.SubscriptionId,
                subscriptionContext.Email,
                subscriptionContext.FirstName,
                subscriptionContext.DisplayName,
                subscriptionContext.PlanName,
                provider,
                nowUtc,
                graceEndsAtUtc),
            cancellationToken);

        if (emailSequence is null)
        {
            await TrySendAdminOpsAlertAsync(
                alertKey: $"payment-recovery-email-skipped/{recovery.RecoveryId}",
                severity: "error",
                title: "Subscription recovery email skipped",
                summary: $"Payment recovery started for {subscriptionContext.Email}, but the recovery email sequence was not scheduled.",
                details: $"Provider: {provider}\nSubscription ID: {subscriptionContext.SubscriptionId}\nRecovery ID: {recovery.RecoveryId}\nProvider payment ID: {providerPaymentId}",
                eventReference: recovery.RecoveryId,
                occurredAtUtc: nowUtc,
                cancellationToken);
            return;
        }

        await StorePaymentRecoveryEmailIdsAsync(baseUri, apiKey, recovery.RecoveryId, emailSequence, cancellationToken);
        await TrySendAdminOpsAlertAsync(
            alertKey: $"payment-recovery-started/{recovery.RecoveryId}",
            severity: "warning",
            title: "Subscription payment recovery started",
            summary: $"Payment recovery started for {subscriptionContext.Email}.",
            details: $"Provider: {provider}\nSubscription ID: {subscriptionContext.SubscriptionId}\nRecovery ID: {recovery.RecoveryId}\nProvider payment ID: {providerPaymentId}\nGrace ends: {graceEndsAtUtc:O}",
            eventReference: recovery.RecoveryId,
            occurredAtUtc: nowUtc,
            cancellationToken);
    }

    private async Task TryResolvePaymentRecoveryAfterSuccessfulChargeAsync(
        Uri baseUri,
        string apiKey,
        PaymentRecoverySubscriptionContext subscriptionContext,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var recovery = await GetActivePaymentRecoveryAsync(baseUri, apiKey, subscriptionContext.SubscriptionId, cancellationToken);
        if (recovery is null)
        {
            return;
        }

        await _subscriptionPaymentRecoveryEmailService.CancelSequenceAsync(
            new SubscriptionPaymentRecoveryEmailSequence(
                recovery.ImmediateEmailId,
                recovery.WarningEmailId,
                recovery.SuspensionEmailId),
            cancellationToken);
        await ResolvePaymentRecoveryAsync(baseUri, apiKey, recovery.RecoveryId, nowUtc, "recovered", cancellationToken);
    }

    private async Task TryResolvePaymentRecoveryAfterCancellationAsync(
        Uri baseUri,
        string apiKey,
        PaymentRecoverySubscriptionContext subscriptionContext,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var recovery = await GetActivePaymentRecoveryAsync(baseUri, apiKey, subscriptionContext.SubscriptionId, cancellationToken);
        if (recovery is null)
        {
            return;
        }

        await _subscriptionPaymentRecoveryEmailService.CancelSequenceAsync(
            new SubscriptionPaymentRecoveryEmailSequence(
                recovery.ImmediateEmailId,
                recovery.WarningEmailId,
                recovery.SuspensionEmailId),
            cancellationToken);
        await ResolvePaymentRecoveryAsync(baseUri, apiKey, recovery.RecoveryId, nowUtc, "cancelled", cancellationToken);
    }

    private async Task TrySendSubscriptionConfirmationAsync(
        string? subscriptionId,
        string? email,
        string? firstName,
        string? displayName,
        PaymentPlan? plan,
        string provider,
        string? paymentReference,
        DateTimeOffset? nextRenewalAtUtc,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId) ||
            string.IsNullOrWhiteSpace(email) ||
            plan is null)
        {
            return;
        }

        await _subscriptionNotificationEmailService.SendSubscriptionConfirmationAsync(
            new SubscriptionConfirmationEmailRequest(
                subscriptionId,
                email.Trim(),
                firstName,
                displayName,
                plan.Name,
                plan.Amount,
                plan.BillingPeriodMonths,
                provider,
                paymentReference,
                nextRenewalAtUtc),
            cancellationToken);

        await TrySendAdminOpsAlertAsync(
            alertKey: $"subscription-confirmed/{subscriptionId}",
            severity: "info",
            title: "Paid subscription confirmed",
            summary: $"{plan.Name} subscription confirmed for {email.Trim()}.",
            details: $"Provider: {provider}\nSubscription ID: {subscriptionId}\nPayment reference: {paymentReference ?? "not available"}",
            eventReference: subscriptionId,
            occurredAtUtc,
            cancellationToken);
    }

    private async Task TrySendSubscriptionEndedAsync(
        PaymentRecoverySubscriptionContext? subscriptionContext,
        string statusLabel,
        string accessMessage,
        DateTimeOffset endedAtUtc,
        string idempotencySuffix,
        CancellationToken cancellationToken)
    {
        if (subscriptionContext is null ||
            string.IsNullOrWhiteSpace(subscriptionContext.Email))
        {
            return;
        }

        await _subscriptionNotificationEmailService.SendSubscriptionEndedAsync(
            new SubscriptionEndedEmailRequest(
                subscriptionContext.SubscriptionId,
                subscriptionContext.Email,
                subscriptionContext.FirstName,
                subscriptionContext.DisplayName,
                subscriptionContext.PlanName,
                statusLabel,
                accessMessage,
                endedAtUtc,
                idempotencySuffix),
            cancellationToken);

        await TrySendAdminOpsAlertAsync(
            alertKey: $"subscription-ended/{subscriptionContext.SubscriptionId}/{idempotencySuffix}",
            severity: "warning",
            title: "Subscription access ended",
            summary: $"{subscriptionContext.PlanName} subscription {statusLabel} for {subscriptionContext.Email}.",
            details: $"Status: {statusLabel}\nSubscription ID: {subscriptionContext.SubscriptionId}\nTier: {subscriptionContext.TierCode ?? "not available"}",
            eventReference: subscriptionContext.SubscriptionId,
            occurredAtUtc: endedAtUtc,
            cancellationToken);
    }

    private async Task TrySendAdminOpsAlertAsync(
        string alertKey,
        string severity,
        string title,
        string summary,
        string details,
        string eventReference,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        await _subscriptionNotificationEmailService.SendAdminOpsAlertAsync(
            new AdminOpsAlertEmailRequest(
                alertKey,
                severity,
                title,
                summary,
                details,
                eventReference,
                occurredAtUtc),
            cancellationToken);
    }

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
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        baseUri = parsedUri;
        return true;
    }

    private string ResolveApiKey() => _options.ServiceRoleKey;

    private static string? ResolvePayFastPlanSlug(IFormCollection formCollection)
    {
        var explicitSlug = formCollection["custom_str1"].ToString().Trim();
        if (!string.IsNullOrWhiteSpace(explicitSlug))
        {
            return explicitSlug;
        }

        var paymentId = formCollection["m_payment_id"].ToString().Trim();
        return ResolvePlanSlugFromPaymentIdentifier(paymentId);
    }

    private static string? ResolvePlanSlugFromPaymentIdentifier(string? paymentIdentifier)
    {
        if (string.IsNullOrWhiteSpace(paymentIdentifier))
        {
            return null;
        }

        var suffixMatch = PaymentIdSuffixRegex().Match(paymentIdentifier);
        return suffixMatch.Success && suffixMatch.Index > 0
            ? paymentIdentifier[..suffixMatch.Index]
            : null;
    }

    private static string BuildGratisProviderPaymentId(string subscriberId) =>
        $"gratis-{subscriberId}";

    private static object DeserializePayloadObject(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(payloadJson);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>
            {
                ["raw"] = payloadJson
            };
        }
    }

    private static string? ResolvePaystackProviderPaymentId(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryReadNestedString(data, "subscription", "subscription_code")
            ?? TryReadString(data, "subscription_code")
            ?? TryReadNestedString(data, "metadata", "subscription_key")
            ?? TryReadString(data, "reference")
            ?? TryReadString(data, "id");
    }

    private static string? ResolvePaystackProviderTransactionId(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryReadString(data, "id")
            ?? TryReadString(data, "reference")
            ?? TryReadNestedString(data, "subscription", "subscription_code")
            ?? TryReadString(data, "subscription_code");
    }

    private static string? ResolvePaystackEventStatus(string eventType, JsonElement data)
    {
        var explicitStatus = TryReadString(data, "status");
        if (!string.IsNullOrWhiteSpace(explicitStatus))
        {
            return explicitStatus.Trim();
        }

        if (string.Equals(eventType, "charge.success", StringComparison.OrdinalIgnoreCase))
        {
            return "success";
        }

        if (string.Equals(eventType, "subscription.disable", StringComparison.OrdinalIgnoreCase))
        {
            return "cancelled";
        }

        if (string.Equals(eventType, "charge.failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "invoice.payment_failed", StringComparison.OrdinalIgnoreCase))
        {
            return "failed";
        }

        return null;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength
            ? trimmed[..maxLength]
            : trimmed;
    }

    private static bool ShouldActivatePaystackSubscription(string eventType, string? eventStatus)
    {
        if (string.Equals(eventType, "charge.success", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "subscription.create", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(eventType, "invoice.create", StringComparison.OrdinalIgnoreCase) &&
               IsSuccessfulPaystackStatus(eventStatus);
    }

    private static bool ShouldCancelPaystackSubscription(string eventType, string? eventStatus)
    {
        return string.Equals(eventType, "subscription.disable", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventStatus, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldFailPaystackSubscription(string eventType, string? eventStatus)
    {
        return string.Equals(eventType, "charge.failed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "invoice.payment_failed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventStatus, "abandoned", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPaystackRecoverableFailureEvent(string eventType, string? eventStatus)
    {
        return string.Equals(eventType, "invoice.payment_failed", StringComparison.OrdinalIgnoreCase) ||
               (string.Equals(eventType, "charge.failed", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(eventStatus, "failed", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPayFastFailureStatus(string? paymentStatus)
    {
        if (string.IsNullOrWhiteSpace(paymentStatus))
        {
            return false;
        }

        return string.Equals(paymentStatus, "FAILED", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentStatus, "DENIED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuccessfulPaystackStatus(string? status) =>
        string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "successful", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeMobileNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var sawPlus = false;
        var builder = new StringBuilder(trimmed.Length);

        foreach (var character in trimmed)
        {
            if (!sawPlus && builder.Length == 0 && character == '+')
            {
                sawPlus = true;
                builder.Append(character);
                continue;
            }

            if (char.IsDigit(character))
            {
                builder.Append(character);
            }
        }

        var normalized = builder.ToString();
        return normalized.Length switch
        {
            < 7 => null,
            > 20 => null,
            _ => normalized
        };
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static bool ContainsUniqueEmailViolation(string? responseBody) =>
        !string.IsNullOrWhiteSpace(responseBody) &&
        (responseBody.Contains("duplicate key value", StringComparison.OrdinalIgnoreCase) ||
         responseBody.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
         responseBody.Contains("subscribers_email_key", StringComparison.OrdinalIgnoreCase));

    private static string? ReadFirstStringProperty(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var first = document.RootElement[0];
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty(propertyName, out var node))
            {
                return node.ValueKind switch
                {
                    JsonValueKind.String => node.GetString(),
                    JsonValueKind.Number => node.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? TryReadNestedString(JsonElement element, params string[] path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString(),
            JsonValueKind.Number => node.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    [GeneratedRegex("-\\d{14}-[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex PaymentIdSuffixRegex();

    private sealed record PaystackUpsertResult(
        bool IsSuccess,
        string? ErrorMessage = null,
        string? SubscriptionId = null,
        string? ProviderPaymentId = null,
        string? ProviderTransactionId = null);

    private sealed record EventInsertResult(bool IsSuccess, bool WasInserted);

    private sealed class ExistingEventLookupRow
    {
        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; set; }
    }

    private sealed class PaymentRecoverySubscriptionRow
    {
        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; set; }

        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; set; }

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class PaymentRecoverySubscriberRow
    {
        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }

    private sealed record PaymentRecoverySubscriptionContext(
        string SubscriptionId,
        string SubscriberId,
        string Email,
        string? FirstName,
        string? DisplayName,
        string? TierCode,
        string PlanName,
        string? Status);

    private sealed class PaymentRecoveryRow
    {
        [JsonPropertyName("recovery_id")]
        public string RecoveryId { get; set; } = string.Empty;

        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("first_failed_at")]
        public DateTimeOffset? FirstFailedAt { get; set; }

        [JsonPropertyName("grace_ends_at")]
        public DateTimeOffset? GraceEndsAt { get; set; }

        [JsonPropertyName("immediate_email_id")]
        public string? ImmediateEmailId { get; set; }

        [JsonPropertyName("warning_email_id")]
        public string? WarningEmailId { get; set; }

        [JsonPropertyName("suspension_email_id")]
        public string? SuspensionEmailId { get; set; }
    }

    private sealed class SubscriptionStatusRow
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

    private sealed class SubscriberProfileRow
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("mobile_number")]
        public string? MobileNumber { get; set; }

        [JsonPropertyName("profile_image_url")]
        public string? ProfileImageUrl { get; set; }

        [JsonPropertyName("profile_image_object_key")]
        public string? ProfileImageObjectKey { get; set; }

        [JsonPropertyName("profile_image_content_type")]
        public string? ProfileImageContentType { get; set; }
    }
}
