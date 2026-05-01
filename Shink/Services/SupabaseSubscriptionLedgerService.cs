using System.Net.Http.Headers;
using System.Globalization;
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
    PaystackCheckoutService paystackCheckoutService,
    PayFastCheckoutService payFastCheckoutService,
    ILogger<SupabaseSubscriptionLedgerService> logger) : ISubscriptionLedgerService
{
    private const string GratisTierCode = "gratis";
    private const string GratisProvider = "paystack";
    private const string GratisPlanSlug = "gratis";
    private static readonly TimeSpan AccountRepairAttemptWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AuthorizationRetryDelay = TimeSpan.FromDays(1);
    private static readonly TimeSpan PaymentRecoveryGracePeriod = TimeSpan.FromDays(4);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly ISubscriptionPaymentRecoveryEmailService _subscriptionPaymentRecoveryEmailService = subscriptionPaymentRecoveryEmailService;
    private readonly ISubscriptionNotificationEmailService _subscriptionNotificationEmailService = subscriptionNotificationEmailService;
    private readonly PaystackCheckoutService _paystackCheckoutService = paystackCheckoutService;
    private readonly PayFastCheckoutService _payFastCheckoutService = payFastCheckoutService;
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

    public async Task<CurrentPaidSubscription?> GetCurrentPaidSubscriptionAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        var context = await TryResolveSelfServiceContextAsync(email, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var subscriptions = await FetchSelfServicePaidSubscriptionsAsync(context.BaseUri, context.ApiKey, context.SubscriberId, cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        var current = subscriptions
            .Where(subscription => IsCurrentlyActiveSelfServiceSubscription(subscription, nowUtc))
            .OrderByDescending(subscription => subscription.NextRenewalAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(subscription => subscription.CancelledAt ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

        return current is null
            ? null
            : new CurrentPaidSubscription(
                current.SubscriptionId,
                current.TierCode ?? string.Empty,
                current.Provider,
                current.NextRenewalAt,
                current.CancelledAt,
                current.CancelledAt is not null && current.CancelledAt > nowUtc);
    }

    public async Task<PaidSubscriptionAttention> GetPaidSubscriptionAttentionAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        var candidate = await TryResolvePaidSubscriptionAttentionCandidateAsync(email, cancellationToken);
        if (candidate is null)
        {
            return new PaidSubscriptionAttention(false);
        }

        var subscription = candidate.Subscription;
        return new PaidSubscriptionAttention(
            RequiresAttention: true,
            Reason: candidate.Reason,
            SubscriptionId: subscription?.SubscriptionId,
            TierCode: subscription?.TierCode,
            PlanSlug: candidate.Plan?.Slug,
            Provider: subscription?.Provider,
            CanAttemptAutomaticRetry: subscription is not null &&
                                      candidate.Plan is not null &&
                                      CanAttemptAutomaticRetry(subscription, candidate.Plan));
    }

    public async Task<SubscriptionRepairResult> TryRepairPaidSubscriptionAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        var candidate = await TryResolvePaidSubscriptionAttentionCandidateAsync(email, cancellationToken);
        if (candidate is null)
        {
            return new SubscriptionRepairResult(true);
        }

        var subscription = candidate.Subscription;
        var plan = candidate.Plan;
        if (subscription is null || plan is null)
        {
            return new SubscriptionRepairResult(false, ErrorMessage: "Kon nie die betaalplan vir hierdie intekening bepaal nie.");
        }

        if (!CanAttemptAutomaticRetry(subscription, plan))
        {
            return new SubscriptionRepairResult(false, plan.Slug);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var providerPaymentId = ResolveProviderPaymentIdForEvent(subscription);
        var recentRepairResult = await TryResolveRecentAccountRepairResultAsync(
            candidate.Context.BaseUri,
            candidate.Context.ApiKey,
            subscription.SubscriptionId,
            plan.Slug,
            nowUtc,
            cancellationToken);
        if (recentRepairResult is not null)
        {
            return recentRepairResult;
        }

        var reference = BuildAccountRepairReference(subscription.SubscriptionId);
        var chargeResult = await _paystackCheckoutService.ChargeAuthorizationAsync(
            plan,
            email!.Trim().ToLowerInvariant(),
            subscription.ProviderToken!,
            reference,
            subscription.SubscriptionId,
            providerPaymentId,
            cancellationToken);

        var eventPayload = string.IsNullOrWhiteSpace(chargeResult.RawPayload)
            ? new Dictionary<string, string?>
            {
                ["reference"] = chargeResult.Reference,
                ["error"] = chargeResult.ErrorMessage
            }
            : DeserializePayloadObject(chargeResult.RawPayload);

        await InsertSubscriptionEventAsync(
            candidate.Context.BaseUri,
            candidate.Context.ApiKey,
            subscription.SubscriptionId,
            provider: "paystack",
            providerPaymentId,
            providerTransactionId: chargeResult.ProviderTransactionId ?? chargeResult.Reference ?? reference,
            eventType: "paystack.authorization_retry",
            eventStatus: chargeResult.IsSuccess ? "success" : chargeResult.TransactionStatus ?? "failed",
            payload: eventPayload,
            cancellationToken);

        if (IsPendingPaystackStatus(chargeResult.TransactionStatus) ||
            IsDuplicatePaystackReferenceFailure(chargeResult))
        {
            return new SubscriptionRepairResult(false, plan.Slug, IsPending: true);
        }

        if (!chargeResult.IsSuccess)
        {
            return new SubscriptionRepairResult(false, plan.Slug, chargeResult.ErrorMessage);
        }

        var nextRenewalAtUtc = (chargeResult.PaidAt ?? nowUtc).AddMonths(plan.BillingPeriodMonths);
        var paystackSubscriptionCode = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
            subscription.Provider,
            subscription.SourceSystem,
            subscription.ProviderPaymentId,
            subscription.ProviderTransactionId);
        if (!string.IsNullOrWhiteSpace(paystackSubscriptionCode))
        {
            var subscriptionLookup = await _paystackCheckoutService.GetSubscriptionAsync(
                paystackSubscriptionCode,
                cancellationToken);
            if (subscriptionLookup.IsSuccess && subscriptionLookup.NextPaymentDate is not null)
            {
                nextRenewalAtUtc = subscriptionLookup.NextPaymentDate.Value;
            }
        }

        await MarkSubscriptionRecoveredByIdAsync(
            candidate.Context.BaseUri,
            candidate.Context.ApiKey,
            subscription.SubscriptionId,
            nextRenewalAtUtc,
            cancellationToken);

        return new SubscriptionRepairResult(true, plan.Slug);
    }

    private async Task<SubscriptionRepairResult?> TryResolveRecentAccountRepairResultAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        string planSlug,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var cutoffUtc = nowUtc.Subtract(AccountRepairAttemptWindow);
        var filters = string.Join(
            "&",
            $"subscription_id=eq.{Uri.EscapeDataString(subscriptionId)}",
            "provider=eq.paystack",
            "event_type=eq.paystack.authorization_retry",
            $"received_at=gt.{Uri.EscapeDataString(cutoffUtc.UtcDateTime.ToString("O"))}",
            "select=event_status,provider_transaction_id,received_at,payload",
            "order=received_at.desc",
            "limit=10");
        var uri = new Uri(baseUri, $"rest/v1/subscription_events?{filters}");

        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<AccountRepairEventRow>>(stream, cancellationToken: cancellationToken)
                ?? [];
            foreach (var row in rows.Where(IsAccountRepairEvent))
            {
                var status = ResolveAccountRepairEventStatus(row);
                if (IsPendingPaystackStatus(status))
                {
                    return new SubscriptionRepairResult(false, planSlug, IsPending: true);
                }

                if (IsSuccessfulPaystackStatus(status))
                {
                    return new SubscriptionRepairResult(true, planSlug);
                }
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                         exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Recent account repair lookup failed. subscription_id={SubscriptionId}", subscriptionId);
        }

        return null;
    }

    public async Task<SubscriptionFreeTierTransferResult> TransferPaidSubscriptionToGratisAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        var context = await TryResolveSelfServiceContextAsync(email, cancellationToken);
        if (context is null)
        {
            return new SubscriptionFreeTierTransferResult(false, "Kon nie jou rekening vind nie. Probeer asseblief weer teken in.");
        }

        if (context.DisabledAt is not null)
        {
            return new SubscriptionFreeTierTransferResult(false, "Hierdie rekening is reeds gesluit.");
        }

        var subscriptions = await FetchSelfServicePaidSubscriptionsAsync(context.BaseUri, context.ApiKey, context.SubscriberId, cancellationToken);
        var paidSubscriptions = subscriptions
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.SubscriptionId))
            .ToArray();
        if (paidSubscriptions.Length == 0)
        {
            return new SubscriptionFreeTierTransferResult(false, "Kon nie 'n betaalde intekening vind om na gratis te skuif nie.");
        }

        var cancelledCount = 0;
        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var subscription in paidSubscriptions)
        {
            var requiresProviderCancellation = IsUsablePaidSubscription(subscription, nowUtc);
            var providerEmailToken = subscription.ProviderEmailToken;
            var paystackSubscriptionCode = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
                subscription.Provider,
                subscription.SourceSystem,
                subscription.ProviderPaymentId,
                subscription.ProviderTransactionId);
            if (!string.IsNullOrWhiteSpace(paystackSubscriptionCode))
            {
                var disableResult = await _paystackCheckoutService.DisableSubscriptionAsync(
                    paystackSubscriptionCode,
                    providerEmailToken,
                    cancellationToken);
                if (!disableResult.IsSuccess)
                {
                    if (requiresProviderCancellation)
                    {
                        return new SubscriptionFreeTierTransferResult(false, disableResult.ErrorMessage ?? "Paystack kon nie die intekening kanselleer nie.", cancelledCount);
                    }

                    LogFreeTierProviderCancellationFallback(subscription, disableResult.ErrorMessage);
                }
                else
                {
                    providerEmailToken = disableResult.EmailToken ?? providerEmailToken;
                }
            }
            else if (string.Equals(subscription.Provider, "payfast", StringComparison.OrdinalIgnoreCase))
            {
                var cancelResult = await _payFastCheckoutService.CancelSubscriptionAsync(
                    subscription.ProviderToken,
                    cancellationToken);
                if (!cancelResult.IsSuccess)
                {
                    if (requiresProviderCancellation)
                    {
                        return new SubscriptionFreeTierTransferResult(false, cancelResult.ErrorMessage ?? "PayFast kon nie die intekening kanselleer nie.", cancelledCount);
                    }

                    LogFreeTierProviderCancellationFallback(subscription, cancelResult.ErrorMessage);
                }
            }
            else if (!IsLocallyCancellableSubscription(subscription))
            {
                return new SubscriptionFreeTierTransferResult(false, "Hierdie betaalverskaffer kan nog nie met selfdiens gekanselleer word nie. Kontak ons asseblief om dit te kanselleer.", cancelledCount);
            }

            var updated = await MarkSelfServiceSubscriptionCancelledNowAsync(
                context.BaseUri,
                context.ApiKey,
                subscription.SubscriptionId,
                nowUtc,
                providerEmailToken,
                cancellationToken);
            if (!updated)
            {
                return new SubscriptionFreeTierTransferResult(false, "Kon nie jou gratis skuif nou stoor nie. Probeer asseblief weer.", cancelledCount);
            }

            cancelledCount++;
        }

        var profile = await GetSubscriberProfileAsync(email, cancellationToken);
        var gratisReady = await EnsureGratisAccessAsync(
            email,
            profile?.FirstName,
            profile?.LastName,
            profile?.DisplayName,
            profile?.MobileNumber,
            profile?.ProfileImageUrl,
            profile?.ProfileImageObjectKey,
            profile?.ProfileImageContentType,
            cancellationToken);
        if (!gratisReady)
        {
            return new SubscriptionFreeTierTransferResult(false, "Jou betaalde toegang is gestop, maar ons kon nie gratis toegang nou aktiveer nie.", cancelledCount);
        }

        return new SubscriptionFreeTierTransferResult(true, CancelledPaidSubscriptions: cancelledCount);
    }

    public async Task<SubscriptionCancelResult> CancelPaidSubscriptionAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        var context = await TryResolveSelfServiceContextAsync(email, cancellationToken);
        if (context is null)
        {
            return new SubscriptionCancelResult(false, "Kon nie jou rekening vind nie. Probeer asseblief weer teken in.");
        }

        if (context.DisabledAt is not null)
        {
            return new SubscriptionCancelResult(false, "Hierdie rekening is reeds gesluit.");
        }

        var subscriptions = await FetchSelfServicePaidSubscriptionsAsync(context.BaseUri, context.ApiKey, context.SubscriberId, cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        var activePaidSubscriptions = subscriptions
            .Where(subscription => IsCurrentlyActiveSelfServiceSubscription(subscription, nowUtc))
            .ToArray();

        if (activePaidSubscriptions.Length == 0)
        {
            return new SubscriptionCancelResult(false, "Jy het nie tans 'n aktiewe betaalde intekening om te kanselleer nie.");
        }

        var cancelledCount = 0;
        DateTimeOffset? latestAccessEndsAtUtc = null;
        foreach (var subscription in activePaidSubscriptions)
        {
            if (string.IsNullOrWhiteSpace(subscription.SubscriptionId))
            {
                continue;
            }

            var providerEmailToken = subscription.ProviderEmailToken;
            var paystackSubscriptionCode = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
                subscription.Provider,
                subscription.SourceSystem,
                subscription.ProviderPaymentId,
                subscription.ProviderTransactionId);
            if (!string.IsNullOrWhiteSpace(paystackSubscriptionCode))
            {
                var disableResult = await _paystackCheckoutService.DisableSubscriptionAsync(
                    paystackSubscriptionCode,
                    providerEmailToken,
                    cancellationToken);
                if (!disableResult.IsSuccess)
                {
                    return new SubscriptionCancelResult(false, disableResult.ErrorMessage ?? "Paystack kon nie die intekening kanselleer nie.");
                }

                providerEmailToken = disableResult.EmailToken ?? providerEmailToken;
            }
            else if (string.Equals(subscription.Provider, "payfast", StringComparison.OrdinalIgnoreCase))
            {
                var cancelResult = await _payFastCheckoutService.CancelSubscriptionAsync(
                    subscription.ProviderToken,
                    cancellationToken);
                if (!cancelResult.IsSuccess)
                {
                    return new SubscriptionCancelResult(false, cancelResult.ErrorMessage ?? "PayFast kon nie die intekening kanselleer nie.");
                }
            }
            else if (!string.Equals(subscription.Provider, "free", StringComparison.OrdinalIgnoreCase) &&
                     !IsLocallyCancellableSubscription(subscription))
            {
                return new SubscriptionCancelResult(false, "Hierdie betaalverskaffer kan nog nie met selfdiens gekanselleer word nie. Kontak ons asseblief om dit te kanselleer.");
            }

            var accessEndsAtUtc = ResolveCancellationEffectiveAt(nowUtc, subscription.NextRenewalAt);
            var updated = await ScheduleSubscriptionCancellationAsync(
                context.BaseUri,
                context.ApiKey,
                subscription.SubscriptionId,
                accessEndsAtUtc,
                providerEmailToken,
                cancellationToken);
            if (!updated)
            {
                return new SubscriptionCancelResult(false, "Kon nie jou kansellasie nou stoor nie. Probeer asseblief weer.");
            }

            cancelledCount++;
            latestAccessEndsAtUtc = latestAccessEndsAtUtc is null || accessEndsAtUtc > latestAccessEndsAtUtc
                ? accessEndsAtUtc
                : latestAccessEndsAtUtc;
        }

        return cancelledCount == 0
            ? new SubscriptionCancelResult(false, "Kon nie 'n aktiewe betaalde intekening vind om te kanselleer nie.")
            : new SubscriptionCancelResult(true, AccessEndsAtUtc: latestAccessEndsAtUtc, CancelledSubscriptions: cancelledCount);
    }

    public async Task<AccountClosureResult> CloseAccountAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        var context = await TryResolveSelfServiceContextAsync(email, cancellationToken);
        if (context is null)
        {
            return new AccountClosureResult(false, "Kon nie jou rekening vind nie. Probeer asseblief weer teken in.");
        }

        if (context.DisabledAt is not null)
        {
            return new AccountClosureResult(true);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object?>
        {
            ["disabled_at"] = nowUtc.UtcDateTime,
            ["disabled_by_admin_email"] = "self_service",
            ["disabled_reason"] = "Rekening deur gebruiker gesluit."
        };

        var escapedSubscriberId = Uri.EscapeDataString(context.SubscriberId);
        var uri = new Uri(context.BaseUri, $"rest/v1/subscribers?subscriber_id=eq.{escapedSubscriberId}");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase self-service account close failed. subscriber_id={SubscriberId} Status={StatusCode} Body={Body}",
                context.SubscriberId,
                (int)response.StatusCode,
                body);
            return new AccountClosureResult(false, "Kon nie jou rekening nou sluit nie. Probeer asseblief weer.");
        }

        return new AccountClosureResult(true);
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
                lastLoginAtUtc: null,
                cancellationToken);
            return !string.IsNullOrWhiteSpace(subscriberId);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase subscriber upsert failed unexpectedly.");
            return false;
        }
    }

    public async Task<bool> UpdateSubscriberLastLoginAsync(
        string? email,
        DateTimeOffset lastLoginAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase subscriber last-login update skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase subscriber last-login update skipped: ServiceRoleKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await UpsertSubscriberAsync(
                baseUri,
                apiKey,
                email,
                string.Empty,
                string.Empty,
                displayName: null,
                mobileNumber: null,
                profileImageUrl: null,
                profileImageObjectKey: null,
                profileImageContentType: null,
                lastLoginAtUtc,
                cancellationToken);
            return !string.IsNullOrWhiteSpace(subscriberId);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase subscriber last-login update failed unexpectedly.");
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
                lastLoginAtUtc: null,
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
                providerEmailToken: null,
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

        await ProcessPendingAuthorizationRetriesAsync(baseUri, apiKey, cancellationToken);

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

    private async Task ProcessPendingAuthorizationRetriesAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        IReadOnlyList<PaymentRecoveryRow> pendingRecoveries;
        try
        {
            pendingRecoveries = await GetPendingAuthorizationRetryRecoveriesAsync(baseUri, apiKey, nowUtc, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Pending authorization retry lookup failed unexpectedly.");
            return;
        }

        foreach (var recovery in pendingRecoveries)
        {
            if (string.IsNullOrWhiteSpace(recovery.SubscriptionId))
            {
                continue;
            }

            try
            {
                await ProcessPendingAuthorizationRetryAsync(baseUri, apiKey, recovery, DateTimeOffset.UtcNow, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(
                    exception,
                    "Subscription authorization retry failed unexpectedly. recovery_id={RecoveryId} subscription_id={SubscriptionId}",
                    recovery.RecoveryId,
                    recovery.SubscriptionId);
                await TrySendAdminOpsAlertAsync(
                    alertKey: $"payment-recovery-authorization-retry-failed/{recovery.RecoveryId}",
                    severity: "error",
                    title: "Subscription authorization retry failed",
                    summary: "A due automatic subscription retry could not be completed.",
                    details: $"Recovery ID: {recovery.RecoveryId}\nSubscription ID: {recovery.SubscriptionId}\nError: {exception.Message}",
                    eventReference: recovery.RecoveryId,
                    occurredAtUtc: DateTimeOffset.UtcNow,
                    cancellationToken);
            }
        }
    }

    private async Task ProcessPendingAuthorizationRetryAsync(
        Uri baseUri,
        string apiKey,
        PaymentRecoveryRow recovery,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var subscriptionContext = await TryGetSubscriptionContextByIdAsync(
            baseUri,
            apiKey,
            recovery.SubscriptionId!,
            cancellationToken);
        if (subscriptionContext is null)
        {
            await MarkPaymentRecoveryAuthorizationRetryAsync(
                baseUri,
                apiKey,
                recovery.RecoveryId,
                nowUtc,
                "skipped",
                null,
                "Subscription context could not be loaded.",
                null,
                cancellationToken);
            return;
        }

        if (!string.Equals(subscriptionContext.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            await ResolvePaymentRecoveryAsync(
                baseUri,
                apiKey,
                recovery.RecoveryId,
                nowUtc,
                "cancelled",
                cancellationToken);
            return;
        }

        var provider = recovery.Provider ?? subscriptionContext.Provider ?? string.Empty;
        var providerPaymentId = recovery.ProviderPaymentId ?? subscriptionContext.ProviderPaymentId ?? string.Empty;
        var plan = PaymentPlanCatalog.FindByTierCode(subscriptionContext.TierCode);
        if (!string.Equals(provider, "paystack", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(subscriptionContext.ProviderToken) ||
            plan is null)
        {
            var retryError = plan is null
                ? "No local subscription plan matched this tier."
                : "No Paystack authorization code is available for this subscription.";
            await SchedulePaymentRecoveryEmailsAsync(
                baseUri,
                apiKey,
                recovery,
                subscriptionContext,
                string.IsNullOrWhiteSpace(provider) ? "unknown" : provider,
                providerPaymentId,
                nowUtc,
                "skipped",
                null,
                retryError,
                cancellationToken);
            return;
        }

        var reference = BuildAuthorizationRetryReference(recovery.RecoveryId);
        var chargeResult = await _paystackCheckoutService.ChargeAuthorizationAsync(
            plan,
            subscriptionContext.Email,
            subscriptionContext.ProviderToken,
            reference,
            subscriptionContext.SubscriptionId,
            providerPaymentId,
            cancellationToken);

        var eventPayload = string.IsNullOrWhiteSpace(chargeResult.RawPayload)
            ? new Dictionary<string, string?>
            {
                ["reference"] = chargeResult.Reference,
                ["error"] = chargeResult.ErrorMessage
            }
            : DeserializePayloadObject(chargeResult.RawPayload);
        await InsertSubscriptionEventAsync(
            baseUri,
            apiKey,
            subscriptionContext.SubscriptionId,
            provider: "paystack",
            providerPaymentId,
            providerTransactionId: chargeResult.ProviderTransactionId ?? chargeResult.Reference,
            eventType: "paystack.authorization_retry",
            eventStatus: chargeResult.IsSuccess ? "success" : chargeResult.TransactionStatus ?? "failed",
            payload: eventPayload,
            cancellationToken);

        if (chargeResult.IsSuccess)
        {
            var paidAtUtc = chargeResult.PaidAt ?? nowUtc;
            var nextRenewalAtUtc = paidAtUtc.AddMonths(plan.BillingPeriodMonths);
            var paystackSubscriptionCode = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
                subscriptionContext.Provider,
                subscriptionContext.SourceSystem,
                subscriptionContext.ProviderPaymentId,
                subscriptionContext.ProviderTransactionId);
            if (!string.IsNullOrWhiteSpace(paystackSubscriptionCode))
            {
                var subscriptionLookup = await _paystackCheckoutService.GetSubscriptionAsync(
                    paystackSubscriptionCode,
                    cancellationToken);
                if (subscriptionLookup.IsSuccess && subscriptionLookup.NextPaymentDate is not null)
                {
                    nextRenewalAtUtc = subscriptionLookup.NextPaymentDate.Value;
                }
            }

            await MarkSubscriptionRecoveredByIdAsync(
                baseUri,
                apiKey,
                subscriptionContext.SubscriptionId,
                nextRenewalAtUtc,
                cancellationToken);
            await MarkPaymentRecoveryAuthorizationRetryAsync(
                baseUri,
                apiKey,
                recovery.RecoveryId,
                nowUtc,
                "succeeded",
                chargeResult.Reference,
                null,
                null,
                cancellationToken);
            await ResolvePaymentRecoveryAsync(baseUri, apiKey, recovery.RecoveryId, nowUtc, "recovered", cancellationToken);
            await TrySendAdminOpsAlertAsync(
                alertKey: $"payment-recovery-authorization-retry-succeeded/{recovery.RecoveryId}",
                severity: "info",
                title: "Subscription authorization retry succeeded",
                summary: $"Automatic payment retry recovered {subscriptionContext.Email}.",
                details: $"Provider: Paystack\nSubscription ID: {subscriptionContext.SubscriptionId}\nRecovery ID: {recovery.RecoveryId}\nReference: {chargeResult.Reference ?? "not available"}",
                eventReference: recovery.RecoveryId,
                occurredAtUtc: nowUtc,
                cancellationToken);
            return;
        }

        await SchedulePaymentRecoveryEmailsAsync(
            baseUri,
            apiKey,
            recovery,
            subscriptionContext,
            "paystack",
            providerPaymentId,
            nowUtc,
            "failed",
            chargeResult.Reference,
            chargeResult.ErrorMessage,
            cancellationToken);
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
        var subscriberLookupUri = new Uri(baseUri, $"rest/v1/subscribers?select=subscriber_id,disabled_at&email=eq.{escapedEmail}&limit=1");

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
            if (!string.IsNullOrWhiteSpace(ReadFirstStringProperty(subscriberResponseBody, "disabled_at")))
            {
                return [];
            }

            var subscriberId = ReadFirstStringProperty(subscriberResponseBody, "subscriber_id");
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return [];
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var subscriptionsUri = new Uri(
                baseUri,
                $"rest/v1/subscriptions?select=status,next_renewal_at,cancelled_at,tier_code,source_system&subscriber_id=eq.{escapedSubscriberId}&status=eq.active&order=subscribed_at.desc&limit=25");

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

    private async Task<SelfServiceSubscriptionContext?> TryResolveSelfServiceContextAsync(
        string? email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase self-service account action skipped: URL is not configured.");
            return null;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase self-service account action skipped: ServiceRoleKey is not configured.");
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var lookupUri = new Uri(
            baseUri,
            $"rest/v1/subscribers?select=subscriber_id,disabled_at&email=eq.{escapedEmail}&limit=1");

        using var request = CreateRequest(HttpMethod.Get, lookupUri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase self-service subscriber lookup failed. email={Email} Status={StatusCode} Body={Body}",
                normalizedEmail,
                (int)response.StatusCode,
                responseBody);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var subscribers = await JsonSerializer.DeserializeAsync<List<SelfServiceSubscriberRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        var subscriber = subscribers.FirstOrDefault();
        if (subscriber is null || string.IsNullOrWhiteSpace(subscriber.SubscriberId))
        {
            return null;
        }

        return new SelfServiceSubscriptionContext(
            baseUri,
            apiKey,
            subscriber.SubscriberId,
            subscriber.DisabledAt);
    }

    private async Task<IReadOnlyList<SelfServiceSubscriptionRow>> FetchSelfServicePaidSubscriptionsAsync(
        Uri baseUri,
        string apiKey,
        string subscriberId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
        var subscriptionsUri = new Uri(
            baseUri,
            "rest/v1/subscriptions" +
            "?select=subscription_id,tier_code,provider,source_system,provider_payment_id,provider_transaction_id,provider_token,provider_email_token,next_renewal_at,cancelled_at,status" +
            $"&subscriber_id=eq.{escapedSubscriberId}&status=eq.active&order=subscribed_at.desc&limit=25");

        using var request = CreateRequest(HttpMethod.Get, subscriptionsUri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase self-service paid subscription lookup failed. subscriber_id={SubscriberId} Status={StatusCode} Body={Body}",
                subscriberId,
                (int)response.StatusCode,
                responseBody);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var subscriptions = await JsonSerializer.DeserializeAsync<List<SelfServiceSubscriptionRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        return subscriptions
            .Where(subscription => !string.Equals(subscription.TierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private async Task<PaidSubscriptionAttentionCandidate?> TryResolvePaidSubscriptionAttentionCandidateAsync(
        string? email,
        CancellationToken cancellationToken)
    {
        var context = await TryResolveSelfServiceContextAsync(email, cancellationToken);
        if (context is null)
        {
            return null;
        }

        if (context.DisabledAt is not null)
        {
            return new PaidSubscriptionAttentionCandidate(context, null, null, "account_disabled");
        }

        var subscriptions = await FetchSelfServicePaidSubscriptionsAsync(context.BaseUri, context.ApiKey, context.SubscriberId, cancellationToken);
        if (subscriptions.Count == 0)
        {
            return null;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (subscriptions.Any(subscription => IsUsablePaidSubscription(subscription, nowUtc)))
        {
            return null;
        }

        return subscriptions
            .Select(subscription =>
            {
                var plan = PaymentPlanCatalog.FindByTierCode(subscription.TierCode);
                var reason = ResolvePaidSubscriptionAttentionReason(subscription, plan, nowUtc);
                return string.IsNullOrWhiteSpace(reason)
                    ? null
                    : new PaidSubscriptionAttentionCandidate(context, subscription, plan, reason);
            })
            .Where(candidate => candidate is not null)
            .OrderByDescending(candidate => candidate!.Subscription?.NextRenewalAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(candidate => candidate!.Subscription?.SubscriptionId)
            .FirstOrDefault();
    }

    private static bool IsUsablePaidSubscription(SelfServiceSubscriptionRow row, DateTimeOffset nowUtc)
    {
        if (!string.Equals(row.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (row.CancelledAt is not null)
        {
            return false;
        }

        if (HasOpenEndedImportedPaidAccess(row))
        {
            return true;
        }

        return row.NextRenewalAt is not null && row.NextRenewalAt > nowUtc;
    }

    private static string? ResolvePaidSubscriptionAttentionReason(
        SelfServiceSubscriptionRow row,
        PaymentPlan? plan,
        DateTimeOffset nowUtc)
    {
        if (plan is null)
        {
            return "unknown_plan";
        }

        if (string.Equals(row.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "payment_failed";
        }

        if (!string.Equals(row.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return "subscription_not_active";
        }

        if (row.CancelledAt is not null && row.CancelledAt <= nowUtc)
        {
            return "cancelled";
        }

        if (HasOpenEndedImportedPaidAccess(row))
        {
            return null;
        }

        if (row.NextRenewalAt is null)
        {
            return "missing_next_payment";
        }

        if (row.NextRenewalAt < nowUtc)
        {
            return "next_payment_elapsed";
        }

        return null;
    }

    private static bool CanAttemptAutomaticRetry(SelfServiceSubscriptionRow subscription, PaymentPlan plan) =>
        plan.IsSubscription &&
        string.Equals(subscription.Provider, "paystack", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(subscription.ProviderToken);

    private static string ResolveProviderPaymentIdForEvent(SelfServiceSubscriptionRow subscription)
    {
        if (PaystackSubscriptionCodeResolver.IsSubscriptionCode(subscription.ProviderPaymentId))
        {
            return subscription.ProviderPaymentId!.Trim();
        }

        if (PaystackSubscriptionCodeResolver.IsSubscriptionCode(subscription.ProviderTransactionId))
        {
            return subscription.ProviderTransactionId!.Trim();
        }

        return string.IsNullOrWhiteSpace(subscription.ProviderPaymentId)
            ? subscription.SubscriptionId
            : subscription.ProviderPaymentId.Trim();
    }

    private void LogFreeTierProviderCancellationFallback(SelfServiceSubscriptionRow subscription, string? errorMessage)
    {
        _logger.LogWarning(
            "Provider cancellation failed during paid-to-free transfer for an already-unusable subscription. subscription_id={SubscriptionId} provider={Provider} source_system={SourceSystem} error={Error}",
            subscription.SubscriptionId,
            subscription.Provider,
            subscription.SourceSystem,
            errorMessage);
    }

    private async Task<bool> MarkSelfServiceSubscriptionCancelledNowAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        DateTimeOffset cancelledAtUtc,
        string? providerEmailToken,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["status"] = "cancelled",
            ["cancelled_at"] = cancelledAtUtc.UtcDateTime,
            ["next_renewal_at"] = null,
            ["updated_at"] = DateTime.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(providerEmailToken))
        {
            payload["provider_email_token"] = providerEmailToken.Trim();
        }

        var escapedSubscriptionId = Uri.EscapeDataString(subscriptionId);
        var uri = new Uri(baseUri, $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase paid-to-free subscription cancellation failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
            subscriptionId,
            (int)response.StatusCode,
            body);
        return false;
    }

    private async Task<bool> ScheduleSubscriptionCancellationAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        DateTimeOffset accessEndsAtUtc,
        string? providerEmailToken,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["status"] = "active",
            ["cancelled_at"] = accessEndsAtUtc.UtcDateTime
        };

        if (!string.IsNullOrWhiteSpace(providerEmailToken))
        {
            payload["provider_email_token"] = providerEmailToken.Trim();
        }

        var escapedSubscriptionId = Uri.EscapeDataString(subscriptionId);
        var uri = new Uri(baseUri, $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}&select=subscription_id");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase self-service subscription cancellation failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
            subscriptionId,
            (int)response.StatusCode,
            body);
        return false;
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

        if (HasOpenEndedImportedPaidAccess(row.TierCode, row.SourceSystem, row.Status, row.CancelledAt, row.NextRenewalAt))
        {
            return true;
        }

        if (!string.Equals(row.TierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase) &&
            row.NextRenewalAt is null)
        {
            return false;
        }

        return true;
    }

    private static bool IsCurrentlyActiveSelfServiceSubscription(SelfServiceSubscriptionRow row, DateTimeOffset nowUtc)
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

        if (HasOpenEndedImportedPaidAccess(row))
        {
            return true;
        }

        if (!string.Equals(row.TierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase) &&
            row.NextRenewalAt is null)
        {
            return false;
        }

        return true;
    }

    private static bool HasOpenEndedImportedPaidAccess(SelfServiceSubscriptionRow row) =>
        HasOpenEndedImportedPaidAccess(row.TierCode, row.SourceSystem, row.Status, row.CancelledAt, row.NextRenewalAt);

    private static bool HasOpenEndedImportedPaidAccess(
        string? tierCode,
        string? sourceSystem,
        string? status,
        DateTimeOffset? cancelledAt,
        DateTimeOffset? nextRenewalAt) =>
        !string.Equals(tierCode, GratisTierCode, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(sourceSystem, "wordpress_pmpro", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(sourceSystem, "discount_code", StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) &&
        cancelledAt is null &&
        nextRenewalAt is null;

    private static bool IsLocallyCancellableSubscription(SelfServiceSubscriptionRow row)
    {
        if (string.Equals(row.Provider, "paystack", StringComparison.OrdinalIgnoreCase))
        {
            return PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
                       row.Provider,
                       row.SourceSystem,
                       row.ProviderPaymentId,
                       row.ProviderTransactionId) is null ||
                   string.Equals(row.SourceSystem, "wordpress_pmpro", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(row.Provider, "free", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPaystackRecurringSubscriptionCode(string? providerPaymentId) =>
        PaystackSubscriptionCodeResolver.IsSubscriptionCode(providerPaymentId);

    private static DateTimeOffset ResolveCancellationEffectiveAt(DateTimeOffset nowUtc, DateTimeOffset? nextRenewalAtUtc) =>
        nextRenewalAtUtc is not null && nextRenewalAtUtc > nowUtc
            ? nextRenewalAtUtc.Value
            : nowUtc;

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
            await InsertPaymentWebhookFailureAsync(
                baseUri,
                apiKey,
                provider: "paystack",
                eventType: "unknown",
                eventStatus: null,
                providerPaymentId: null,
                providerTransactionId: null,
                failureStage: "payload-parse",
                errorMessage: "Paystack payload is not valid JSON.",
                payload: DeserializePayloadObject(payloadJson),
                cancellationToken);
            return new SubscriptionPersistResult(false, "Paystack payload is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                await InsertPaymentWebhookFailureAsync(
                    baseUri,
                    apiKey,
                    provider: "paystack",
                    eventType: "unknown",
                    eventStatus: null,
                    providerPaymentId: null,
                    providerTransactionId: null,
                    failureStage: "payload-root",
                    errorMessage: "Paystack payload root is invalid.",
                    payload: DeserializePayloadObject(payloadJson),
                    cancellationToken);
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
                    await InsertPaymentWebhookFailureAsync(
                        baseUri,
                        apiKey,
                        provider: "paystack",
                        eventType,
                        eventStatus,
                        providerPaymentId,
                        providerTransactionId,
                        failureStage: "subscription-upsert",
                        errorMessage: upsertResult.ErrorMessage ?? "Paystack subscription could not be persisted.",
                        payload: DeserializePayloadObject(payloadJson),
                        cancellationToken);
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
                await InsertPaymentWebhookFailureAsync(
                    baseUri,
                    apiKey,
                    provider: "paystack",
                    eventType,
                    eventStatus,
                    providerPaymentId,
                    providerTransactionId,
                    failureStage: "event-insert",
                    errorMessage: "Could not persist Paystack event.",
                    payload: normalizedPayload,
                    cancellationToken);
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
            lastLoginAtUtc: null,
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
            providerEmailToken: null,
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
            lastLoginAtUtc: null,
            cancellationToken);

        if (subscriberId is null)
        {
            return new PaystackUpsertResult(false, "Could not upsert subscriber profile.");
        }

        var providerToken = TryReadNestedString(data, "authorization", "authorization_code")
            ?? TryReadString(data, "authorization_code");
        var providerEmailToken = TryReadNestedString(data, "subscription", "email_token")
            ?? TryReadString(data, "email_token");

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
            providerEmailToken: providerEmailToken,
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
            ?? TryReadString(data, "plan_code")
            ?? TryReadString(data, "plan");

        if (!string.IsNullOrWhiteSpace(paystackPlanCode))
        {
            var mappedTierCode = await ResolveTierCodeByPaystackPlanCodeAsync(baseUri, apiKey, paystackPlanCode, cancellationToken);
            var planFromPaystackCode = PaymentPlanCatalog.FindByTierCode(mappedTierCode);
            if (planFromPaystackCode is not null)
            {
                return planFromPaystackCode;
            }
        }

        var planFromPaystackDetails = ResolvePaystackPlanByAmountAndInterval(data);
        if (planFromPaystackDetails is not null)
        {
            return planFromPaystackDetails;
        }

        var inferredPlanSlug = ResolvePlanSlugFromPaymentIdentifier(
            TryReadNestedString(data, "metadata", "subscription_key") ??
            TryReadString(data, "reference"));

        return PaymentPlanCatalog.FindBySlug(inferredPlanSlug);
    }

    private static PaymentPlan? ResolvePaystackPlanByAmountAndInterval(JsonElement data)
    {
        var amountInCents = TryReadNestedDecimal(data, "plan", "amount")
            ?? TryReadStringAsDecimal(data, "amount");
        var interval = TryReadNestedString(data, "plan", "interval");
        if (amountInCents is null || string.IsNullOrWhiteSpace(interval))
        {
            return null;
        }

        var matches = PaymentPlanCatalog.All
            .Where(plan =>
                plan.IsSubscription &&
                PaystackAmountMatches(plan, amountInCents.Value) &&
                PaystackIntervalMatches(plan, interval))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool PaystackAmountMatches(PaymentPlan plan, decimal amountInCents) =>
        decimal.Round(plan.Amount * 100m, 0, MidpointRounding.AwayFromZero) == decimal.Round(amountInCents, 0, MidpointRounding.AwayFromZero);

    private static bool PaystackIntervalMatches(PaymentPlan plan, string interval)
    {
        var normalizedInterval = interval.Trim().ToLowerInvariant();
        return normalizedInterval switch
        {
            "monthly" => plan.BillingPeriodMonths == 1,
            "annually" or "annual" or "yearly" => plan.BillingPeriodMonths == 12,
            _ => false
        };
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
        DateTimeOffset? lastLoginAtUtc,
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

        if (lastLoginAtUtc is not null)
        {
            payload["last_login_at"] = lastLoginAtUtc.Value.UtcDateTime;
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
        string? providerEmailToken,
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
            provider_email_token = string.IsNullOrWhiteSpace(providerEmailToken) ? null : providerEmailToken,
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

    private async Task MarkSubscriptionRecoveredByIdAsync(
        Uri baseUri,
        string apiKey,
        string subscriptionId,
        DateTimeOffset nextRenewalAtUtc,
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
                next_renewal_at = nextRenewalAtUtc.UtcDateTime,
                cancelled_at = (DateTime?)null
            },
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase subscription retry recovery update failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
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
            $"rest/v1/subscriptions?provider=eq.{escapedProvider}&provider_payment_id=eq.{escapedPaymentId}&select=subscription_id,subscriber_id,tier_code,provider,provider_payment_id,provider_transaction_id,provider_token,source_system,status&limit=1");
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
            row.Provider,
            row.ProviderPaymentId,
            row.ProviderTransactionId,
            row.ProviderToken,
            row.SourceSystem,
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
            $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}&select=subscription_id,subscriber_id,tier_code,provider,provider_payment_id,provider_transaction_id,provider_token,source_system,status&limit=1");
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
            row.Provider,
            row.ProviderPaymentId,
            row.ProviderTransactionId,
            row.ProviderToken,
            row.SourceSystem,
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
            $"rest/v1/subscription_payment_recoveries?subscription_id=eq.{escapedSubscriptionId}&resolved_at=is.null&select=recovery_id,subscription_id,provider,provider_payment_id,first_failed_at,grace_ends_at,authorization_retry_due_at,authorization_retry_attempted_at,authorization_retry_status,authorization_retry_reference,authorization_retry_error,emails_scheduled_at,immediate_email_id,warning_email_id,suspension_email_id&limit=1");
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
            $"rest/v1/subscription_payment_recoveries?resolved_at=is.null&grace_ends_at=lte.{escapedNow}&or=(emails_scheduled_at.not.is.null,authorization_retry_status.is.null,authorization_retry_status.in.(failed,skipped))&select=recovery_id,subscription_id,provider,provider_payment_id,first_failed_at,grace_ends_at,authorization_retry_due_at,authorization_retry_attempted_at,authorization_retry_status,authorization_retry_reference,authorization_retry_error,emails_scheduled_at,immediate_email_id,warning_email_id,suspension_email_id&order=grace_ends_at.asc&limit=100");
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

    private async Task<IReadOnlyList<PaymentRecoveryRow>> GetPendingAuthorizationRetryRecoveriesAsync(
        Uri baseUri,
        string apiKey,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var escapedNow = Uri.EscapeDataString(nowUtc.UtcDateTime.ToString("O"));
        var uri = new Uri(
            baseUri,
            $"rest/v1/subscription_payment_recoveries?resolved_at=is.null&authorization_retry_status=eq.pending&authorization_retry_due_at=lte.{escapedNow}&select=recovery_id,subscription_id,provider,provider_payment_id,first_failed_at,grace_ends_at,authorization_retry_due_at,authorization_retry_attempted_at,authorization_retry_status,authorization_retry_reference,authorization_retry_error,emails_scheduled_at,immediate_email_id,warning_email_id,suspension_email_id&order=authorization_retry_due_at.asc&limit=100");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase pending authorization retry lookup failed. Status={StatusCode} Body={Body}",
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
        DateTimeOffset authorizationRetryDueAtUtc,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscription_id = subscriptionId,
            provider,
            provider_payment_id = providerPaymentId,
            first_failed_at = firstFailedAtUtc.UtcDateTime,
            grace_ends_at = graceEndsAtUtc.UtcDateTime,
            authorization_retry_due_at = authorizationRetryDueAtUtc.UtcDateTime,
            authorization_retry_status = "pending"
        };

        var uri = new Uri(baseUri, "rest/v1/subscription_payment_recoveries?select=recovery_id,subscription_id,provider,provider_payment_id,first_failed_at,grace_ends_at,authorization_retry_due_at,authorization_retry_attempted_at,authorization_retry_status,authorization_retry_reference,authorization_retry_error,emails_scheduled_at,immediate_email_id,warning_email_id,suspension_email_id");
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
        DateTimeOffset emailsScheduledAtUtc,
        DateTimeOffset graceEndsAtUtc,
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
                suspension_email_id = emailSequence.SuspensionEmailId,
                emails_scheduled_at = emailsScheduledAtUtc.UtcDateTime,
                grace_ends_at = graceEndsAtUtc.UtcDateTime
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

    private async Task MarkPaymentRecoveryAuthorizationRetryAsync(
        Uri baseUri,
        string apiKey,
        string recoveryId,
        DateTimeOffset attemptedAtUtc,
        string retryStatus,
        string? retryReference,
        string? retryError,
        DateTimeOffset? graceEndsAtUtc,
        CancellationToken cancellationToken)
    {
        var escapedRecoveryId = Uri.EscapeDataString(recoveryId);
        var uri = new Uri(baseUri, $"rest/v1/subscription_payment_recoveries?recovery_id=eq.{escapedRecoveryId}");
        object payload = graceEndsAtUtc is null
            ? new
            {
                authorization_retry_attempted_at = attemptedAtUtc.UtcDateTime,
                authorization_retry_status = retryStatus,
                authorization_retry_reference = string.IsNullOrWhiteSpace(retryReference) ? null : retryReference,
                authorization_retry_error = string.IsNullOrWhiteSpace(retryError) ? null : retryError
            }
            : new
            {
                authorization_retry_attempted_at = attemptedAtUtc.UtcDateTime,
                authorization_retry_status = retryStatus,
                authorization_retry_reference = string.IsNullOrWhiteSpace(retryReference) ? null : retryReference,
                authorization_retry_error = string.IsNullOrWhiteSpace(retryError) ? null : retryError,
                grace_ends_at = graceEndsAtUtc.Value.UtcDateTime
            };

        using var request = CreateJsonRequest(
            new HttpMethod("PATCH"),
            uri,
            apiKey,
            payload,
            "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase payment recovery authorization retry update failed. recovery_id={RecoveryId} Status={StatusCode} Body={Body}",
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

    private async Task InsertPaymentWebhookFailureAsync(
        Uri baseUri,
        string apiKey,
        string provider,
        string eventType,
        string? eventStatus,
        string? providerPaymentId,
        string? providerTransactionId,
        string failureStage,
        string errorMessage,
        object payload,
        CancellationToken cancellationToken)
    {
        var failurePayload = new
        {
            provider,
            event_type = string.IsNullOrWhiteSpace(eventType) ? "unknown" : eventType,
            event_status = string.IsNullOrWhiteSpace(eventStatus) ? null : eventStatus,
            provider_payment_id = string.IsNullOrWhiteSpace(providerPaymentId) ? null : providerPaymentId,
            provider_transaction_id = string.IsNullOrWhiteSpace(providerTransactionId) ? null : providerTransactionId,
            failure_stage = failureStage,
            error_message = errorMessage,
            payload
        };

        var uri = new Uri(baseUri, "rest/v1/payment_webhook_failures");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, failurePayload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase payment webhook failure insert failed. provider={Provider} event_type={EventType} stage={FailureStage} Status={StatusCode} Body={Body}",
            provider,
            eventType,
            failureStage,
            (int)response.StatusCode,
            body);
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

        var authorizationRetryDueAtUtc = nowUtc.Add(AuthorizationRetryDelay);
        var graceEndsAtUtc = authorizationRetryDueAtUtc.Add(PaymentRecoveryGracePeriod);
        var recovery = await CreatePaymentRecoveryAsync(
            baseUri,
            apiKey,
            subscriptionContext.SubscriptionId,
            provider,
            providerPaymentId,
            nowUtc,
            graceEndsAtUtc,
            authorizationRetryDueAtUtc,
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

        await TrySendAdminOpsAlertAsync(
            alertKey: $"payment-recovery-retry-scheduled/{recovery.RecoveryId}",
            severity: "warning",
            title: "Subscription payment retry scheduled",
            summary: $"Payment retry scheduled for {subscriptionContext.Email}.",
            details: $"Provider: {provider}\nSubscription ID: {subscriptionContext.SubscriptionId}\nRecovery ID: {recovery.RecoveryId}\nProvider payment ID: {providerPaymentId}\nRetry due: {authorizationRetryDueAtUtc:O}\nGrace ends: {graceEndsAtUtc:O}",
            eventReference: recovery.RecoveryId,
            occurredAtUtc: nowUtc,
            cancellationToken);
    }

    private async Task SchedulePaymentRecoveryEmailsAsync(
        Uri baseUri,
        string apiKey,
        PaymentRecoveryRow recovery,
        PaymentRecoverySubscriptionContext subscriptionContext,
        string provider,
        string providerPaymentId,
        DateTimeOffset nowUtc,
        string retryStatus,
        string? retryReference,
        string? retryError,
        CancellationToken cancellationToken)
    {
        var graceEndsAtUtc = nowUtc.Add(PaymentRecoveryGracePeriod);

        await MarkPaymentRecoveryAuthorizationRetryAsync(
            baseUri,
            apiKey,
            recovery.RecoveryId,
            nowUtc,
            retryStatus,
            retryReference,
            retryError,
            graceEndsAtUtc,
            cancellationToken);

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

        await StorePaymentRecoveryEmailIdsAsync(baseUri, apiKey, recovery.RecoveryId, emailSequence, nowUtc, graceEndsAtUtc, cancellationToken);
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

    private static string BuildAuthorizationRetryReference(string recoveryId)
    {
        var safeRecoveryId = new string(
            recoveryId.Where(character => char.IsLetterOrDigit(character) || character == '-').ToArray());
        if (string.IsNullOrWhiteSpace(safeRecoveryId))
        {
            safeRecoveryId = Guid.NewGuid().ToString("N");
        }

        return $"retry-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{safeRecoveryId}";
    }

    private static string BuildAccountRepairReference(string subscriptionId)
    {
        var safeSubscriptionId = new string(
            subscriptionId.Where(character => char.IsLetterOrDigit(character) || character == '-').ToArray());
        if (string.IsNullOrWhiteSpace(safeSubscriptionId))
        {
            safeSubscriptionId = Guid.NewGuid().ToString("N");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var bucketMinute = nowUtc.Minute - nowUtc.Minute % (int)AccountRepairAttemptWindow.TotalMinutes;
        var bucketUtc = new DateTimeOffset(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            nowUtc.Hour,
            bucketMinute,
            0,
            TimeSpan.Zero);

        return $"repair-{bucketUtc:yyyyMMddHHmm}-{safeSubscriptionId}";
    }

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

    private static bool IsPendingPaystackStatus(string? status) =>
        string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "ongoing", StringComparison.OrdinalIgnoreCase);

    private static bool IsDuplicatePaystackReferenceFailure(PaystackAuthorizationChargeResult chargeResult) =>
        ContainsDuplicateReference(chargeResult.ErrorMessage) ||
        ContainsDuplicateReference(chargeResult.RawPayload);

    private static bool ContainsDuplicateReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
        value.Contains("reference", StringComparison.OrdinalIgnoreCase);

    private static bool IsAccountRepairEvent(AccountRepairEventRow row)
    {
        if (row.Payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var metadataSource = TryReadNestedString(row.Payload, "data", "metadata", "source") ??
                             TryReadNestedString(row.Payload, "metadata", "source");
        if (string.Equals(metadataSource, "subscription_authorization_retry", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var reference = TryReadNestedString(row.Payload, "data", "reference") ??
                        TryReadString(row.Payload, "reference") ??
                        row.ProviderTransactionId;
        return !string.IsNullOrWhiteSpace(reference) &&
               reference.StartsWith("repair-", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveAccountRepairEventStatus(AccountRepairEventRow row) =>
        row.EventStatus ??
        TryReadNestedString(row.Payload, "data", "status") ??
        TryReadString(row.Payload, "status");

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

    private static decimal? TryReadNestedDecimal(JsonElement element, params string[] path)
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

        return TryConvertJsonDecimal(current);
    }

    private static decimal? TryReadStringAsDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var node))
        {
            return null;
        }

        return TryConvertJsonDecimal(node);
    }

    private static decimal? TryConvertJsonDecimal(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var number))
        {
            return number;
        }

        if (node.ValueKind == JsonValueKind.String &&
            decimal.TryParse(node.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
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

    private sealed record SelfServiceSubscriptionContext(
        Uri BaseUri,
        string ApiKey,
        string SubscriberId,
        DateTimeOffset? DisabledAt);

    private sealed record PaidSubscriptionAttentionCandidate(
        SelfServiceSubscriptionContext Context,
        SelfServiceSubscriptionRow? Subscription,
        PaymentPlan? Plan,
        string Reason);

    private sealed class SelfServiceSubscriberRow
    {
        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; set; }

        [JsonPropertyName("disabled_at")]
        public DateTimeOffset? DisabledAt { get; set; }
    }

    private sealed class SelfServiceSubscriptionRow
    {
        [JsonPropertyName("subscription_id")]
        public string SubscriptionId { get; set; } = string.Empty;

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("provider_token")]
        public string? ProviderToken { get; set; }

        [JsonPropertyName("provider_email_token")]
        public string? ProviderEmailToken { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }

        [JsonPropertyName("cancelled_at")]
        public DateTimeOffset? CancelledAt { get; set; }
    }

    private sealed class ExistingEventLookupRow
    {
        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; set; }
    }

    private sealed class AccountRepairEventRow
    {
        [JsonPropertyName("event_status")]
        public string? EventStatus { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("received_at")]
        public DateTimeOffset? ReceivedAt { get; set; }

        [JsonPropertyName("payload")]
        public JsonElement Payload { get; set; }
    }

    private sealed class PaymentRecoverySubscriptionRow
    {
        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; set; }

        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; set; }

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("provider_token")]
        public string? ProviderToken { get; set; }

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; set; }

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
        string? Provider,
        string? ProviderPaymentId,
        string? ProviderTransactionId,
        string? ProviderToken,
        string? SourceSystem,
        string? Status);

    private sealed class PaymentRecoveryRow
    {
        [JsonPropertyName("recovery_id")]
        public string RecoveryId { get; set; } = string.Empty;

        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("first_failed_at")]
        public DateTimeOffset? FirstFailedAt { get; set; }

        [JsonPropertyName("grace_ends_at")]
        public DateTimeOffset? GraceEndsAt { get; set; }

        [JsonPropertyName("authorization_retry_due_at")]
        public DateTimeOffset? AuthorizationRetryDueAt { get; set; }

        [JsonPropertyName("authorization_retry_attempted_at")]
        public DateTimeOffset? AuthorizationRetryAttemptedAt { get; set; }

        [JsonPropertyName("authorization_retry_status")]
        public string? AuthorizationRetryStatus { get; set; }

        [JsonPropertyName("authorization_retry_reference")]
        public string? AuthorizationRetryReference { get; set; }

        [JsonPropertyName("authorization_retry_error")]
        public string? AuthorizationRetryError { get; set; }

        [JsonPropertyName("emails_scheduled_at")]
        public DateTimeOffset? EmailsScheduledAt { get; set; }

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

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; set; }
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
