using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shink.Components.Content;
using Shink.Components.Pages;
using Shink.Utilities;

namespace Shink.Services;

public sealed partial class SupabaseAdminManagementService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    IUserNotificationService userNotificationService,
    IWordPressMigrationService wordPressMigrationService,
    ISupabaseAuthService supabaseAuthService,
    IAuthSessionService authSessionService,
    ISubscriptionPaymentRecoveryEmailService subscriptionPaymentRecoveryEmailService,
    PaystackCheckoutService paystackCheckoutService,
    PayFastCheckoutService payFastCheckoutService,
    ILogger<SupabaseAdminManagementService> logger) : IAdminManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string StoryCatalogSnapshotCacheKey = "stories:catalog:v2";

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly IUserNotificationService _userNotificationService = userNotificationService;
    private readonly IWordPressMigrationService _wordPressMigrationService = wordPressMigrationService;
    private readonly ISupabaseAuthService _supabaseAuthService = supabaseAuthService;
    private readonly IAuthSessionService _authSessionService = authSessionService;
    private readonly ISubscriptionPaymentRecoveryEmailService _subscriptionPaymentRecoveryEmailService = subscriptionPaymentRecoveryEmailService;
    private readonly PaystackCheckoutService _paystackCheckoutService = paystackCheckoutService;
    private readonly PayFastCheckoutService _payFastCheckoutService = payFastCheckoutService;
    private readonly ILogger<SupabaseAdminManagementService> _logger = logger;

    public async Task<bool> IsAdminAsync(string? email, CancellationToken cancellationToken = default)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Admin lookup skipped: Supabase URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Admin lookup skipped: Supabase SecretKey is not configured.");
            return false;
        }

        return await IsAdminCoreAsync(baseUri, apiKey, email, cancellationToken);
    }

    public async Task<bool> ChangeAdminEmailAsync(
        string? currentEmail,
        string? newEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentEmail) || string.IsNullOrWhiteSpace(newEmail))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Admin email change skipped: Supabase URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Admin email change skipped: Supabase SecretKey is not configured.");
            return false;
        }

        var normalizedCurrentEmail = currentEmail.Trim().ToLowerInvariant();
        var normalizedNewEmail = newEmail.Trim().ToLowerInvariant();
        if (string.Equals(normalizedCurrentEmail, normalizedNewEmail, StringComparison.Ordinal))
        {
            return true;
        }

        if (!await IsAdminCoreAsync(baseUri, apiKey, normalizedCurrentEmail, cancellationToken))
        {
            return true;
        }

        if (await IsAdminCoreAsync(baseUri, apiKey, normalizedNewEmail, cancellationToken))
        {
            _logger.LogWarning(
                "Admin email change blocked because target email already exists. current_email={CurrentEmail} new_email={NewEmail}",
                normalizedCurrentEmail,
                normalizedNewEmail);
            return false;
        }

        var escapedCurrentEmail = Uri.EscapeDataString(normalizedCurrentEmail);
        var updateUri = new Uri(baseUri, $"rest/v1/admin_users?email=eq.{escapedCurrentEmail}");
        using var updateRequest = CreateJsonRequest(
            new HttpMethod("PATCH"),
            updateUri,
            apiKey,
            new { email = normalizedNewEmail },
            "return=minimal");
        using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
        if (updateResponse.IsSuccessStatusCode)
        {
            return true;
        }

        var responseBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Admin email change failed. current_email={CurrentEmail} new_email={NewEmail} Status={StatusCode} Body={Body}",
            normalizedCurrentEmail,
            normalizedNewEmail,
            (int)updateResponse.StatusCode,
            responseBody);
        return false;
    }

    public async Task<IReadOnlyList<AdminSubscriberRecord>> GetSubscribersAsync(
        string? adminEmail,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var page = await GetSubscribersPageAsync(
            adminEmail,
            new AdminSubscriberPageRequest(
                PageIndex: 0,
                PageSize: 5000,
                Search: search,
                SortLabel: "subscriber",
                SortDescending: false),
            cancellationToken);

        return page.Items;
    }

    public async Task<AdminSubscriberPageResult> GetSubscribersPageAsync(
        string? adminEmail,
        AdminSubscriberPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminSubscriberPageResult(Array.Empty<AdminSubscriberRecord>(), 0);
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminSubscriberPageResult(Array.Empty<AdminSubscriberRecord>(), 0);
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminSubscriberPageResult(Array.Empty<AdminSubscriberRecord>(), 0);
        }

        var payload = new
        {
            p_page_index = Math.Max(0, request.PageIndex),
            p_page_size = Math.Clamp(request.PageSize, 1, 500),
            p_search = NormalizeSearchTerm(request.Search),
            p_sort_label = NormalizeSubscriberSortLabel(request.SortLabel),
            p_sort_desc = request.SortDescending,
            p_subscriber_text = NormalizeSearchTerm(request.SubscriberText),
            p_mobile_text = NormalizeSearchTerm(request.MobileText),
            p_tier_text = NormalizeSearchTerm(request.TierText),
            p_source = NormalizeSubscriberFilterToken(request.SourceSystem),
            p_provider = NormalizeSubscriberFilterToken(request.PaymentProvider),
            p_status = NormalizeSubscriberFilterToken(request.SubscriptionStatus)
        };

        var response = await InvokeRpcAsync<AdminSubscriberPageRpcResponse>(
            baseUri,
            apiKey,
            "admin_subscribers_page",
            payload,
            cancellationToken);

        if (response is null)
        {
            return new AdminSubscriberPageResult(Array.Empty<AdminSubscriberRecord>(), 0);
        }

        var pageItems = response.Items?
            .Where(item => item.SubscriberId != Guid.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item.Email))
            .ToArray()
            ?? [];
        var disabledStates = await FetchSubscriberDisabledStatesAsync(
            baseUri,
            apiKey,
            pageItems.Select(item => item.SubscriberId).ToArray(),
            cancellationToken);

        var items = pageItems
            .Select(item => new AdminSubscriberRecord(
                SubscriberId: item.SubscriberId,
                Email: item.Email.Trim().ToLowerInvariant(),
                FirstName: NormalizeOptionalText(item.FirstName, 80),
                LastName: NormalizeOptionalText(item.LastName, 80),
                DisplayName: NormalizeOptionalText(item.DisplayName, 120),
                MobileNumber: NormalizeOptionalText(item.MobileNumber, 32),
                ProfileImageUrl: NormalizeOptionalText(item.ProfileImageUrl, 2048),
                CreatedAt: item.CreatedAt,
                UpdatedAt: item.UpdatedAt,
                ActiveTierCodes: disabledStates.GetValueOrDefault(item.SubscriberId)?.DisabledAt is not null
                    ? Array.Empty<string>()
                    : item.ActiveTierCodes?
                    .Where(tier => !string.IsNullOrWhiteSpace(tier))
                    .Select(tier => tier.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tier => tier, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                    ?? Array.Empty<string>(),
                PaymentProvider: NormalizeOptionalText(item.PaymentProvider, 40),
                SubscriptionSourceSystem: NormalizeOptionalText(item.SubscriptionSourceSystem, 40),
                SubscriptionStatus: disabledStates.GetValueOrDefault(item.SubscriberId)?.DisabledAt is not null
                    ? "disabled"
                    : NormalizeOptionalText(item.SubscriptionStatus, 40),
                SubscribedAt: item.SubscribedAt,
                NextPaymentDueAt: item.NextPaymentDueAt,
                CancelledAt: item.CancelledAt,
                DisabledAt: disabledStates.GetValueOrDefault(item.SubscriberId)?.DisabledAt ?? item.DisabledAt,
                DisabledByAdminEmail: NormalizeOptionalText(disabledStates.GetValueOrDefault(item.SubscriberId)?.DisabledByAdminEmail ?? item.DisabledByAdminEmail, 320),
                DisabledReason: NormalizeOptionalText(disabledStates.GetValueOrDefault(item.SubscriberId)?.DisabledReason ?? item.DisabledReason, 400)))
            .ToArray();

        return new AdminSubscriberPageResult(items, Math.Max(0, response.TotalCount));
    }

    public async Task<AdminOperationResult> UpdateSubscriberAsync(
        string? adminEmail,
        AdminSubscriberUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (request.SubscriberId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige intekenaar.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var normalizedFirstName = NormalizeOptionalText(request.FirstName, 80);
        var normalizedLastName = NormalizeOptionalText(request.LastName, 80);
        var normalizedDisplayName = NormalizeOptionalText(request.DisplayName, 120);
        var normalizedMobileNumber = NormalizeMobileNumber(request.MobileNumber);
        if (!string.IsNullOrWhiteSpace(request.MobileNumber) && normalizedMobileNumber is null)
        {
            return new AdminOperationResult(false, "Selfoonnommer moet 7 tot 20 syfers wees.");
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        if (!string.IsNullOrWhiteSpace(request.Email) && normalizedEmail is null)
        {
            return new AdminOperationResult(false, "Gebruik asseblief 'n geldige e-posadres.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["first_name"] = normalizedFirstName,
            ["last_name"] = normalizedLastName,
            ["display_name"] = normalizedDisplayName,
            ["mobile_number"] = normalizedMobileNumber
        };
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            payload["email"] = normalizedEmail;
        }

        var escapedSubscriberId = Uri.EscapeDataString(request.SubscriberId.ToString("D"));
        var uri = new Uri(baseUri, $"rest/v1/subscribers?subscriber_id=eq.{escapedSubscriberId}");

        using var updateRequest = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=minimal");
        using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
        if (updateResponse.IsSuccessStatusCode)
        {
            InvalidateStoryCatalogCache();
            return new AdminOperationResult(true);
        }

        var responseBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Subscriber update failed. subscriber_id={SubscriberId} Status={StatusCode} Body={Body}",
            request.SubscriberId,
            (int)updateResponse.StatusCode,
            responseBody);
        return new AdminOperationResult(false, "Kon nie intekenaar nou opdateer nie.");
    }

    public async Task<AdminOperationResult> CreateSubscriberAsync(
        string? adminEmail,
        AdminSubscriberCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        if (normalizedEmail is null)
        {
            return new AdminOperationResult(false, "Gebruik asseblief 'n geldige e-posadres.");
        }

        var normalizedMobileNumber = NormalizeMobileNumber(request.MobileNumber);
        if (!string.IsNullOrWhiteSpace(request.MobileNumber) && normalizedMobileNumber is null)
        {
            return new AdminOperationResult(false, "Selfoonnommer moet 7 tot 20 syfers wees.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["email"] = normalizedEmail,
            ["first_name"] = NormalizeOptionalText(request.FirstName, 80),
            ["last_name"] = NormalizeOptionalText(request.LastName, 80),
            ["display_name"] = NormalizeOptionalText(request.DisplayName, 120),
            ["mobile_number"] = normalizedMobileNumber,
            ["disabled_at"] = null,
            ["disabled_by_admin_email"] = null,
            ["disabled_reason"] = null
        };

        var uri = new Uri(context.BaseUri, "rest/v1/subscribers?on_conflict=email&select=subscriber_id");
        using var createRequest = CreateJsonRequest(HttpMethod.Post, uri, context.ApiKey, payload, "resolution=merge-duplicates,return=representation");
        using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
        if (!createResponse.IsSuccessStatusCode)
        {
            var responseBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Subscriber create failed. email={Email} Status={StatusCode} Body={Body}",
                normalizedEmail,
                (int)createResponse.StatusCode,
                responseBody);
            return new AdminOperationResult(false, "Kon nie intekenaar nou skep nie.");
        }

        var responseText = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        var subscriberId = TryReadFirstGuidProperty(responseText, "subscriber_id") ?? Guid.Empty;
        if (subscriberId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kon nie nuwe intekenaar bevestig nie.");
        }

        await WriteSubscriberAuditAsync(
            context,
            subscriberId,
            "subscriber.created",
            "Subscriber profile created from admin.",
            new { email = normalizedEmail },
            cancellationToken);

        if (request.SendPasswordReset)
        {
            var resetUrl = NormalizeOptionalText(request.ResetUrl, 2048);
            if (!string.IsNullOrWhiteSpace(resetUrl))
            {
                await _supabaseAuthService.SendPasswordResetEmailAsync(normalizedEmail, resetUrl, cancellationToken);
            }
        }

        return new AdminOperationResult(true, EntityId: subscriberId);
    }

    public async Task<AdminSubscriberDetailSnapshot?> GetSubscriberDetailAsync(
        string? adminEmail,
        Guid subscriberId,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null || subscriberId == Guid.Empty)
        {
            return null;
        }

        var subscriber = await FetchSubscriberRecordByIdAsync(context, subscriberId, cancellationToken);
        if (subscriber is null)
        {
            return null;
        }

        var subscriptions = await FetchSubscriberSubscriptionsAsync(context, subscriberId, cancellationToken);
        var tierOptions = await FetchAdminSubscriptionTierOptionsAsync(context, cancellationToken);
        var tierNames = tierOptions.ToDictionary(tier => tier.TierCode, tier => tier.DisplayName, StringComparer.OrdinalIgnoreCase);

        return new AdminSubscriberDetailSnapshot(
            subscriber,
            subscriptions.Select(row => MapSubscriberSubscription(row, tierNames)).ToArray(),
            await FetchSubscriberBillingEventsAsync(context, subscriberId, subscriptions, cancellationToken),
            await FetchSubscriberStoreOrdersAsync(context, subscriber.Email, cancellationToken),
            await FetchSubscriberActivityAsync(context, subscriberId, subscriber.Email, cancellationToken),
            await FetchSubscriberRecoveriesAsync(context, subscriber, subscriptions, cancellationToken),
            await FetchSubscriberNotificationsAsync(context, subscriberId, cancellationToken),
            tierOptions,
            await FetchSubscriberAuditAsync(context, subscriberId, cancellationToken));
    }

    public async Task<AdminOperationResult> SetSubscriberDisabledAsync(
        string? adminEmail,
        AdminSubscriberDisabledUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var subscriber = await FetchSubscriberRecordByIdAsync(context, request.SubscriberId, cancellationToken);
        if (subscriber is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige intekenaar.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var payload = request.IsDisabled
            ? new Dictionary<string, object?>
            {
                ["disabled_at"] = nowUtc.UtcDateTime,
                ["disabled_by_admin_email"] = context.AdminEmail,
                ["disabled_reason"] = NormalizeOptionalText(request.Reason, 400)
            }
            : new Dictionary<string, object?>
            {
                ["disabled_at"] = null,
                ["disabled_by_admin_email"] = null,
                ["disabled_reason"] = null
            };

        var escapedSubscriberId = Uri.EscapeDataString(request.SubscriberId.ToString("D"));
        var uri = new Uri(context.BaseUri, $"rest/v1/subscribers?subscriber_id=eq.{escapedSubscriberId}");
        using var updateRequest = CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey, payload, "return=minimal");
        using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
        if (!updateResponse.IsSuccessStatusCode)
        {
            var responseBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Subscriber disabled-state update failed. subscriber_id={SubscriberId} Status={StatusCode} Body={Body}",
                request.SubscriberId,
                (int)updateResponse.StatusCode,
                responseBody);
            return new AdminOperationResult(false, "Kon nie intekenaarstatus nou opdateer nie.");
        }

        if (request.IsDisabled)
        {
            await _authSessionService.RevokeAllSessionsAsync(subscriber.Email, cancellationToken);
        }

        await WriteSubscriberAuditAsync(
            context,
            request.SubscriberId,
            request.IsDisabled ? "subscriber.disabled" : "subscriber.enabled",
            NormalizeOptionalText(request.Reason, 400),
            new { disabled = request.IsDisabled },
            cancellationToken);

        return new AdminOperationResult(true, EntityId: request.SubscriberId);
    }

    public async Task<AdminOperationResult> GrantSubscriberAccessAsync(
        string? adminEmail,
        AdminSubscriberAccessGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var normalizedTierCode = NormalizeOptionalText(request.TierCode, 80)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedTierCode) || !TierCodeRegex().IsMatch(normalizedTierCode))
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige toegangsvlak.");
        }

        var expiryValidation = AdminSubscriberManagementLogic.ValidateManualAccessExpiry(request.ExpiresAt, DateTimeOffset.UtcNow);
        if (!expiryValidation.IsValid)
        {
            return new AdminOperationResult(false, expiryValidation.ErrorMessage);
        }

        var subscriber = await FetchSubscriberRecordByIdAsync(context, request.SubscriberId, cancellationToken);
        if (subscriber is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige intekenaar.");
        }

        var providerPaymentId = $"admin-override-{request.SubscriberId:D}-{normalizedTierCode}";
        var payload = new
        {
            subscriber_id = request.SubscriberId,
            tier_code = normalizedTierCode,
            provider = "free",
            provider_payment_id = providerPaymentId,
            provider_transaction_id = (string?)null,
            provider_token = (string?)null,
            status = "active",
            subscribed_at = DateTimeOffset.UtcNow.UtcDateTime,
            next_renewal_at = request.ExpiresAt!.Value.UtcDateTime,
            cancelled_at = (DateTime?)null,
            source_system = "admin_override"
        };

        var uri = new Uri(context.BaseUri, "rest/v1/subscriptions?on_conflict=provider,provider_payment_id&select=subscription_id");
        using var grantRequest = CreateJsonRequest(HttpMethod.Post, uri, context.ApiKey, payload, "resolution=merge-duplicates,return=representation");
        using var grantResponse = await _httpClient.SendAsync(grantRequest, cancellationToken);
        if (!grantResponse.IsSuccessStatusCode)
        {
            var responseBody = await grantResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Admin subscriber access grant failed. subscriber_id={SubscriberId} Status={StatusCode} Body={Body}",
                request.SubscriberId,
                (int)grantResponse.StatusCode,
                responseBody);
            return new AdminOperationResult(false, "Kon nie handmatige toegang nou stoor nie.");
        }

        var responseText = await grantResponse.Content.ReadAsStringAsync(cancellationToken);
        var subscriptionId = TryReadFirstGuidProperty(responseText, "subscription_id") ?? Guid.Empty;

        await WriteSubscriberAuditAsync(
            context,
            request.SubscriberId,
            "access.granted",
            NormalizeOptionalText(request.Reason, 400),
            new { tier_code = normalizedTierCode, expires_at = request.ExpiresAt.Value.UtcDateTime },
            cancellationToken);

        return new AdminOperationResult(true, EntityId: subscriptionId);
    }

    public async Task<AdminOperationResult> CancelSubscriberAccessAsync(
        string? adminEmail,
        AdminSubscriberAccessCancelRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var subscriptions = await FetchSubscriberSubscriptionsAsync(context, request.SubscriberId, cancellationToken);
        var subscription = subscriptions.FirstOrDefault(item => item.SubscriptionId == request.SubscriptionId);
        if (subscription is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige intekening.");
        }

        if (!string.Equals(subscription.SourceSystem, "admin_override", StringComparison.OrdinalIgnoreCase))
        {
            return new AdminOperationResult(false, "Net handmatige admin-toegang kan hier gekanselleer word.");
        }

        var escapedSubscriptionId = Uri.EscapeDataString(request.SubscriptionId.ToString("D"));
        var uri = new Uri(context.BaseUri, $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}&source_system=eq.admin_override");
        var payload = new
        {
            status = "cancelled",
            cancelled_at = DateTimeOffset.UtcNow.UtcDateTime
        };

        using var cancelRequest = CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey, payload, "return=minimal");
        using var cancelResponse = await _httpClient.SendAsync(cancelRequest, cancellationToken);
        if (!cancelResponse.IsSuccessStatusCode)
        {
            var responseBody = await cancelResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Admin subscriber access cancel failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
                request.SubscriptionId,
                (int)cancelResponse.StatusCode,
                responseBody);
            return new AdminOperationResult(false, "Kon nie handmatige toegang nou kanselleer nie.");
        }

        await WriteSubscriberAuditAsync(
            context,
            request.SubscriberId,
            "access.cancelled",
            NormalizeOptionalText(request.Reason, 400),
            new { subscription_id = request.SubscriptionId },
            cancellationToken);

        return new AdminOperationResult(true, EntityId: request.SubscriptionId);
    }

    public async Task<AdminOperationResult> CancelSubscriberPaidSubscriptionAsync(
        string? adminEmail,
        AdminSubscriberPaidSubscriptionCancelRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var subscriber = await FetchSubscriberRecordByIdAsync(context, request.SubscriberId, cancellationToken);
        if (subscriber is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige intekenaar.");
        }

        var subscriptions = await FetchSubscriberSubscriptionsAsync(context, request.SubscriberId, cancellationToken);
        var subscription = subscriptions.FirstOrDefault(item => item.SubscriptionId == request.SubscriptionId);
        if (subscription is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige intekening.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (!IsAdminCancellablePaidSubscription(subscription, nowUtc))
        {
            return new AdminOperationResult(false, "Net aktiewe Paystack- of PayFast-intekeninge kan hier gekanselleer word.");
        }

        var paystackSubscriptionCode = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
            subscription.Provider,
            subscription.SourceSystem,
            subscription.ProviderPaymentId,
            subscription.ProviderTransactionId);
        string? providerEmailToken = null;
        if (!string.IsNullOrWhiteSpace(paystackSubscriptionCode))
        {
            var disableResult = await _paystackCheckoutService.DisableSubscriptionAsync(
                paystackSubscriptionCode,
                subscription.ProviderEmailToken,
                cancellationToken);
            if (!disableResult.IsSuccess)
            {
                return new AdminOperationResult(
                    false,
                    disableResult.ErrorMessage ?? "Paystack kon nie die intekening kanselleer nie.");
            }

            providerEmailToken = disableResult.EmailToken;
        }
        else if (string.Equals(subscription.Provider, "payfast", StringComparison.OrdinalIgnoreCase))
        {
            var cancelResult = await _payFastCheckoutService.CancelSubscriptionAsync(
                subscription.ProviderToken,
                cancellationToken);
            if (!cancelResult.IsSuccess)
            {
                return new AdminOperationResult(
                    false,
                    cancelResult.ErrorMessage ?? "PayFast kon nie die intekening kanselleer nie.");
            }
        }
        else
        {
            return new AdminOperationResult(false, "Intekeningkode of token ontbreek.");
        }

        var accessEndsAtUtc = subscription.NextRenewalAt is not null && subscription.NextRenewalAt.Value > nowUtc
            ? subscription.NextRenewalAt.Value
            : nowUtc;

        var escapedSubscriptionId = Uri.EscapeDataString(request.SubscriptionId.ToString("D"));
        var escapedSubscriberId = Uri.EscapeDataString(request.SubscriberId.ToString("D"));
        var uri = new Uri(
            context.BaseUri,
            $"rest/v1/subscriptions?subscription_id=eq.{escapedSubscriptionId}&subscriber_id=eq.{escapedSubscriberId}");
        var payload = new Dictionary<string, object?>
        {
            ["status"] = "active",
            ["cancelled_at"] = accessEndsAtUtc.UtcDateTime
        };
        if (!string.IsNullOrWhiteSpace(providerEmailToken))
        {
            payload["provider_email_token"] = NormalizeOptionalText(providerEmailToken, 160);
        }

        using var cancelRequest = CreateJsonRequest(new HttpMethod("PATCH"), uri, context.ApiKey, payload, "return=minimal");
        using var cancelResponse = await _httpClient.SendAsync(cancelRequest, cancellationToken);
        if (!cancelResponse.IsSuccessStatusCode)
        {
            var responseBody = await cancelResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Admin paid subscription cancel failed. subscription_id={SubscriptionId} Status={StatusCode} Body={Body}",
                request.SubscriptionId,
                (int)cancelResponse.StatusCode,
                responseBody);
            return new AdminOperationResult(false, "Kon nie betaalde intekening nou kanselleer nie.");
        }

        await WriteSubscriberAuditAsync(
            context,
            request.SubscriberId,
            "subscription.cancelled_by_admin",
            NormalizeOptionalText(request.Reason, 400),
            new
            {
                subscription_id = request.SubscriptionId,
                tier_code = subscription.TierCode,
                provider = subscription.Provider,
                access_ends_at = accessEndsAtUtc.UtcDateTime
            },
            cancellationToken);

        return new AdminOperationResult(true, EntityId: request.SubscriptionId);
    }

    public async Task<AdminOperationResult> SendSubscriberPasswordResetAsync(
        string? adminEmail,
        Guid subscriberId,
        string resetUrl,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var subscriber = await FetchSubscriberRecordByIdAsync(context, subscriberId, cancellationToken);
        if (subscriber is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige intekenaar.");
        }

        var resetResult = await _supabaseAuthService.SendPasswordResetEmailAsync(subscriber.Email, resetUrl, cancellationToken);
        if (!resetResult.IsSuccess)
        {
            return new AdminOperationResult(false, resetResult.ErrorMessage ?? "Kon nie wagwoordherstel nou stuur nie.");
        }

        await WriteSubscriberAuditAsync(
            context,
            subscriberId,
            "auth.password_reset_sent",
            "Password reset email sent from admin.",
            new { email = subscriber.Email },
            cancellationToken);

        return new AdminOperationResult(true, EntityId: subscriberId);
    }

    public async Task<AdminOperationResult> ResendSubscriberRecoveryEmailAsync(
        string? adminEmail,
        Guid subscriberId,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var subscriber = await FetchSubscriberRecordByIdAsync(context, subscriberId, cancellationToken);
        if (subscriber is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige intekenaar.");
        }

        var subscriptions = await FetchSubscriberSubscriptionsAsync(context, subscriberId, cancellationToken);
        var activeRecovery = (await FetchSubscriptionRecoveryRowsForSubscriberAsync(context, subscriptions, cancellationToken))
            .Where(recovery => recovery.ResolvedAt is null)
            .OrderByDescending(recovery => recovery.CreatedAt)
            .FirstOrDefault();
        if (activeRecovery is null)
        {
            return new AdminOperationResult(false, "Daar is nie 'n aktiewe herstel-e-pos vir hierdie intekenaar nie.");
        }

        var subscription = subscriptions.FirstOrDefault(item => item.SubscriptionId == activeRecovery.SubscriptionId);
        if (subscription is null)
        {
            return new AdminOperationResult(false, "Kon nie die herstel-intekening vind nie.");
        }

        var sequence = await _subscriptionPaymentRecoveryEmailService.ScheduleSequenceAsync(
            new SubscriptionPaymentRecoveryEmailRequest(
                activeRecovery.RecoveryId.ToString("D"),
                subscription.SubscriptionId.ToString("D"),
                subscriber.Email,
                subscriber.FirstName,
                subscriber.DisplayName,
                subscription.TierCode ?? "Schink Stories",
                subscription.Provider ?? "unknown",
                activeRecovery.CreatedAt,
                DateTimeOffset.UtcNow.AddDays(4)),
            cancellationToken);

        if (sequence is null)
        {
            return new AdminOperationResult(false, "Kon nie herstel-e-pos nou stuur nie.");
        }

        await WriteSubscriberAuditAsync(
            context,
            subscriberId,
            "recovery.resent",
            "Recovery email sequence resent from admin.",
            new { recovery_id = activeRecovery.RecoveryId },
            cancellationToken);

        return new AdminOperationResult(true, EntityId: subscriberId);
    }

    public async Task<AdminOperationResult> SendSubscriberSubscriptionRecoveryEmailAsync(
        string? adminEmail,
        Guid subscriberId,
        CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var subscriber = await FetchSubscriberRecordByIdAsync(context, subscriberId, cancellationToken);
        if (subscriber is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige intekenaar.");
        }

        var subscriptions = await FetchSubscriberSubscriptionsAsync(context, subscriberId, cancellationToken);
        var failedSubscription = subscriptions
            .Where(IsRecoverableFailedSubscription)
            .OrderByDescending(subscription => subscription.NextRenewalAt ?? subscription.SubscribedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        if (failedSubscription is null)
        {
            return new AdminOperationResult(false, "Hierdie intekenaar het nie tans 'n mislukte intekening nie.");
        }

        var recoveryAction = await ResolveSubscriptionRecoveryActionAsync(
            subscriber.Email,
            failedSubscription,
            cancellationToken);
        if (recoveryAction is null)
        {
            return new AdminOperationResult(false, "Kon nie 'n Paystack herstelkakel vir hierdie intekening skep nie.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var emailId = await _subscriptionPaymentRecoveryEmailService.SendImmediateAsync(
            new SubscriptionPaymentRecoveryEmailRequest(
                $"admin-{subscriberId:D}-{nowUtc:yyyyMMddHHmmss}",
                failedSubscription.SubscriptionId.ToString("D"),
                subscriber.Email,
                subscriber.FirstName,
                subscriber.DisplayName,
                recoveryAction.PlanName,
                failedSubscription.Provider ?? "paystack",
                failedSubscription.NextRenewalAt ?? failedSubscription.SubscribedAt ?? nowUtc,
                nowUtc.AddDays(4),
                recoveryAction.Url,
                recoveryAction.ActionLabel,
                recoveryAction.Context),
            $"admin-subscription-recovery/{subscriberId:D}/{nowUtc:yyyyMMddHHmmss}",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(emailId))
        {
            return new AdminOperationResult(false, "Kon nie intekening-herstel-e-pos nou stuur nie.");
        }

        await WriteSubscriberAuditAsync(
            context,
            subscriberId,
            "recovery.subscription_manual_sent",
            "Subscription recovery email sent from admin.",
            new
            {
                subscription_id = failedSubscription.SubscriptionId,
                recovery_action = recoveryAction.ActionKey,
                email_id = emailId
            },
            cancellationToken);

        return new AdminOperationResult(true, EntityId: subscriberId);
    }

    public async Task<string> ExportSubscribersCsvAsync(
        string? adminEmail,
        AdminSubscriberExportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return AdminSubscriberManagementLogic.BuildSubscriberCsv(Array.Empty<AdminSubscriberRecord>());
        }

        var selectedIds = AdminSubscriberManagementLogic.NormalizeSelectedSubscriberIds(request.SelectedSubscriberIds);
        IReadOnlyList<AdminSubscriberRecord> subscribers;
        if (selectedIds.Count > 0)
        {
            subscribers = await FetchSubscriberRecordsByIdsAsync(selectedIds, cancellationToken);
        }
        else
        {
            var exportedSubscribers = new List<AdminSubscriberRecord>();
            var pageIndex = 0;
            var totalCount = int.MaxValue;
            while (exportedSubscribers.Count < totalCount)
            {
                var page = await GetSubscribersPageAsync(
                    adminEmail,
                    request.PageRequest with { PageIndex = pageIndex, PageSize = 500 },
                    cancellationToken);
                if (page.Items.Count == 0)
                {
                    break;
                }

                exportedSubscribers.AddRange(page.Items);
                totalCount = page.TotalCount;
                pageIndex++;
            }

            subscribers = exportedSubscribers;
        }

        return AdminSubscriberManagementLogic.BuildSubscriberCsv(subscribers);
    }

    private async Task<SubscriptionRecoveryAction?> ResolveSubscriptionRecoveryActionAsync(
        string subscriberEmail,
        SubscriptionRow subscription,
        CancellationToken cancellationToken)
    {
        var plan = PaymentPlanCatalog.FindByTierCode(subscription.TierCode);
        var planName = plan?.Name ?? subscription.TierCode ?? "Schink Stories";

        var paystackSubscriptionCode = PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
            subscription.Provider,
            subscription.SourceSystem,
            subscription.ProviderPaymentId,
            subscription.ProviderTransactionId);
        if (!string.IsNullOrWhiteSpace(paystackSubscriptionCode))
        {
            var updateLink = await _paystackCheckoutService.GenerateSubscriptionUpdateLinkAsync(
                paystackSubscriptionCode,
                cancellationToken);
            if (updateLink.IsSuccess && !string.IsNullOrWhiteSpace(updateLink.Link))
            {
                return new SubscriptionRecoveryAction(
                    updateLink.Link,
                    "Werk kaartbesonderhede by",
                    "Paystack sal jou vra om 'n nuwe kaart of betaalmetode aan hierdie intekening te koppel.",
                    "paystack_card_update",
                    planName);
            }
        }

        if (plan is null)
        {
            return null;
        }

        var checkout = await _paystackCheckoutService.InitializeCheckoutForEmailAsync(
            plan,
            subscriberEmail,
            cancellationToken);
        if (checkout.IsSuccess && !string.IsNullOrWhiteSpace(checkout.AuthorizationUrl))
        {
            return new SubscriptionRecoveryAction(
                checkout.AuthorizationUrl,
                "Teken weer in",
                "Paystack sal 'n nuwe intekening vir jou huidige plan begin.",
                "paystack_resubscribe_checkout",
                planName);
        }

        var checkoutPageUrl = _paystackCheckoutService.BuildCheckoutPageUrl(plan);
        return string.IsNullOrWhiteSpace(checkoutPageUrl)
            ? null
            : new SubscriptionRecoveryAction(
                checkoutPageUrl,
                "Teken weer in",
                "Gebruik die Paystack betaalblad om jou intekening weer te begin.",
                "paystack_resubscribe_page",
                planName);
    }

    private static bool IsRecoverableFailedSubscription(SubscriptionRow subscription) =>
        IsFailedSubscriptionStatus(subscription.Status) &&
        !string.Equals(subscription.SourceSystem, "wordpress_pmpro", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subscription.SourceSystem, "admin_override", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailedSubscriptionStatus(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized is "failed" or "payment_failed" or "past_due";
    }

    public async Task<AdminBulkOperationResult> RunSubscriberBulkActionAsync(
        string? adminEmail,
        AdminSubscriberBulkActionRequest request,
        CancellationToken cancellationToken = default)
    {
        var selectedIds = AdminSubscriberManagementLogic.NormalizeSelectedSubscriberIds(request.SubscriberIds);
        if (selectedIds.Count == 0)
        {
            return new AdminBulkOperationResult(false, 0, 0, ["Kies asseblief ten minste een intekenaar."]);
        }

        var errors = new List<string>();
        var successCount = 0;

        foreach (var subscriberId in selectedIds)
        {
            AdminOperationResult result = request.Action switch
            {
                AdminSubscriberBulkAction.Disable => await SetSubscriberDisabledAsync(
                    adminEmail,
                    new AdminSubscriberDisabledUpdateRequest(subscriberId, true, request.Reason),
                    cancellationToken),
                AdminSubscriberBulkAction.Enable => await SetSubscriberDisabledAsync(
                    adminEmail,
                    new AdminSubscriberDisabledUpdateRequest(subscriberId, false, request.Reason),
                    cancellationToken),
                AdminSubscriberBulkAction.SendPasswordReset => await SendSubscriberPasswordResetAsync(
                    adminEmail,
                    subscriberId,
                    request.ResetUrl ?? string.Empty,
                    cancellationToken),
                _ => new AdminOperationResult(false, "Onbekende grootmaataksie.")
            };

            if (result.IsSuccess)
            {
                successCount++;
            }
            else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                errors.Add(result.ErrorMessage);
            }
        }

        return new AdminBulkOperationResult(
            successCount == selectedIds.Count,
            selectedIds.Count,
            successCount,
            errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<IReadOnlyList<AdminStoryRecord>> GetStoriesAsync(
        string? adminEmail,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return Array.Empty<AdminStoryRecord>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return Array.Empty<AdminStoryRecord>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<AdminStoryRecord>();
        }

        var rows = await FetchStoriesAsync(baseUri, apiKey, cancellationToken);
        var normalizedSearch = NormalizeSearchTerm(search);

        return rows
            .Where(row => row.StoryId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Title))
            .Where(row => MatchesStorySearch(row, normalizedSearch))
            .Select(row => new AdminStoryRecord(
                StoryId: row.StoryId,
                Slug: row.Slug.Trim(),
                Title: row.Title.Trim(),
                Summary: NormalizeOptionalText(row.Summary, 512),
                Description: NormalizeOptionalText(row.Description, 4000),
                YouTubeUrl: NormalizeOptionalText(row.YouTubeUrl, 2048),
                CoverImagePath: NormalizeOptionalText(row.CoverImagePath, 1024),
                ThumbnailImagePath: NormalizeOptionalText(row.ThumbnailImagePath, 1024),
                AudioProvider: string.IsNullOrWhiteSpace(row.AudioProvider) ? "local" : row.AudioProvider.Trim().ToLowerInvariant(),
                AudioBucket: NormalizeOptionalText(row.AudioBucket, 120),
                AudioObjectKey: NormalizeOptionalText(row.AudioObjectKey, 1024),
                AudioContentType: NormalizeOptionalText(row.AudioContentType, 100),
                AccessLevel: string.IsNullOrWhiteSpace(row.AccessLevel) ? "subscriber" : row.AccessLevel.Trim().ToLowerInvariant(),
                Status: string.IsNullOrWhiteSpace(row.Status) ? "draft" : row.Status.Trim().ToLowerInvariant(),
                SortOrder: row.SortOrder,
                PublishedAt: row.PublishedAt,
                DurationSeconds: row.DurationSeconds,
                UpdatedAt: row.UpdatedAt))
            .OrderByDescending(row => row.UpdatedAt ?? row.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AdminOperationResult> UpdateStoryAsync(
        string? adminEmail,
        AdminStoryUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (request.StoryId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige storie.");
        }

        var normalizedSlug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!StorySlugRegex().IsMatch(normalizedSlug))
        {
            return new AdminOperationResult(false, "Die storie slug is ongeldig.");
        }

        var normalizedTitle = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return new AdminOperationResult(false, "Storie titel is verpligtend.");
        }

        var normalizedAudioProvider = request.AudioProvider?.Trim().ToLowerInvariant() switch
        {
            "local" => "local",
            "r2" => "r2",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(normalizedAudioProvider))
        {
            return new AdminOperationResult(false, "Audio provider moet 'local' of 'r2' wees.");
        }

        var normalizedAccessLevel = request.AccessLevel?.Trim().ToLowerInvariant() switch
        {
            "free" => "free",
            "subscriber" => "subscriber",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(normalizedAccessLevel))
        {
            return new AdminOperationResult(false, "Toegangsvlak moet 'free' of 'subscriber' wees.");
        }

        var normalizedStatus = request.Status?.Trim().ToLowerInvariant() switch
        {
            "draft" => "draft",
            "published" => "published",
            "archived" => "archived",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(normalizedStatus))
        {
            return new AdminOperationResult(false, "Status moet 'draft', 'published' of 'archived' wees.");
        }

        var normalizedSummary = NormalizeOptionalText(request.Summary, 512);
        var normalizedDescription = NormalizeOptionalText(request.Description, 4000);
        var normalizedYouTubeUrl = NormalizeYouTubeUrl(request.YouTubeUrl);
        if (!string.IsNullOrWhiteSpace(request.YouTubeUrl) &&
            normalizedYouTubeUrl is null)
        {
            return new AdminOperationResult(false, "Gebruik asseblief 'n geldige YouTube skakel.");
        }

        var normalizedCoverImagePath = NormalizeOptionalText(request.CoverImagePath, 1024);
        var normalizedThumbnailPath = NormalizeOptionalText(request.ThumbnailImagePath, 1024);
        var normalizedAudioBucket = NormalizeOptionalText(request.AudioBucket, 120);
        var normalizedAudioObjectKey = NormalizeOptionalText(request.AudioObjectKey, 1024);
        var normalizedAudioContentType = NormalizeOptionalText(request.AudioContentType, 100);
        var normalizedSortOrder = Math.Clamp(request.SortOrder, -500_000, 500_000);
        var normalizedDurationSeconds = request.DurationSeconds is > 0 ? request.DurationSeconds : null;

        DateTimeOffset? normalizedPublishedAt = request.PublishedAt;
        if (string.Equals(normalizedStatus, "published", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPublishedAt ??= DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(normalizedAudioObjectKey))
            {
                return new AdminOperationResult(false, "Gepubliseerde stories vereis 'n audio object key.");
            }
        }

        if (string.Equals(normalizedAudioProvider, "r2", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(normalizedAudioBucket) ||
                string.IsNullOrWhiteSpace(normalizedAudioObjectKey))
            {
                return new AdminOperationResult(false, "R2 stories vereis beide bucket en object key.");
            }
        }
        else
        {
            normalizedAudioBucket = null;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var existingStory = await FetchStoryByIdAsync(baseUri, apiKey, request.StoryId, cancellationToken);
        var shouldCreatePublishedStoryNotifications =
            string.Equals(normalizedStatus, "published", StringComparison.OrdinalIgnoreCase) &&
            !HasStoryBeenPublished(existingStory);

        var payload = new Dictionary<string, object?>
        {
            ["slug"] = normalizedSlug,
            ["title"] = normalizedTitle,
            ["summary"] = normalizedSummary,
            ["description"] = normalizedDescription,
            ["youtube_url"] = normalizedYouTubeUrl,
            ["cover_image_path"] = normalizedCoverImagePath,
            ["thumbnail_image_path"] = normalizedThumbnailPath,
            ["audio_provider"] = normalizedAudioProvider,
            ["audio_bucket"] = normalizedAudioBucket,
            ["audio_object_key"] = normalizedAudioObjectKey,
            ["audio_content_type"] = normalizedAudioContentType,
            ["access_level"] = normalizedAccessLevel,
            ["status"] = normalizedStatus,
            ["sort_order"] = normalizedSortOrder,
            ["published_at"] = normalizedPublishedAt?.UtcDateTime,
            ["duration_seconds"] = normalizedDurationSeconds
        };

        var escapedStoryId = Uri.EscapeDataString(request.StoryId.ToString("D"));
        var uri = new Uri(baseUri, $"rest/v1/stories?story_id=eq.{escapedStoryId}");

        using var updateRequest = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=minimal");
        using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
        if (updateResponse.IsSuccessStatusCode)
        {
            InvalidateStoryCatalogCache();
            if (shouldCreatePublishedStoryNotifications)
            {
                await _userNotificationService.CreatePublishedStoryNotificationsAsync(
                    new PublishedStoryNotificationRequest(
                        request.StoryId,
                        normalizedSlug,
                        normalizedTitle,
                        normalizedAccessLevel,
                        normalizedSummary,
                        normalizedThumbnailPath,
                        normalizedCoverImagePath),
                    cancellationToken);
            }

            return new AdminOperationResult(true);
        }

        var responseBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Story update failed. story_id={StoryId} Status={StatusCode} Body={Body}",
            request.StoryId,
            (int)updateResponse.StatusCode,
            responseBody);
        return new AdminOperationResult(false, "Kon nie storie nou opdateer nie.");
    }

    public async Task<AdminOperationResult> CreateStoryAsync(
        string? adminEmail,
        AdminStoryCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var normalizedSlug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!StorySlugRegex().IsMatch(normalizedSlug))
        {
            return new AdminOperationResult(false, "Die storie slug is ongeldig.");
        }

        var normalizedTitle = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return new AdminOperationResult(false, "Storie titel is verpligtend.");
        }

        var normalizedAccessLevel = request.AccessLevel?.Trim().ToLowerInvariant() switch
        {
            "free" => "free",
            "subscriber" => "subscriber",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(normalizedAccessLevel))
        {
            return new AdminOperationResult(false, "Toegangsvlak moet 'free' of 'subscriber' wees.");
        }

        var normalizedStatus = request.Status?.Trim().ToLowerInvariant() switch
        {
            "draft" => "draft",
            "published" => "published",
            "archived" => "archived",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(normalizedStatus))
        {
            return new AdminOperationResult(false, "Status moet 'draft', 'published' of 'archived' wees.");
        }

        var normalizedSummary = NormalizeOptionalText(request.Summary, 512);
        var normalizedDescription = NormalizeOptionalText(request.Description, 4000);
        var normalizedYouTubeUrl = NormalizeYouTubeUrl(request.YouTubeUrl);
        if (!string.IsNullOrWhiteSpace(request.YouTubeUrl) &&
            normalizedYouTubeUrl is null)
        {
            return new AdminOperationResult(false, "Gebruik asseblief 'n geldige YouTube skakel.");
        }

        var normalizedCoverImagePath = NormalizeOptionalText(request.CoverImagePath, 1024);
        var normalizedThumbnailPath = NormalizeOptionalText(request.ThumbnailImagePath, 1024);
        var normalizedAudioBucket = NormalizeOptionalText(request.AudioBucket, 120);
        var normalizedAudioObjectKey = NormalizeOptionalText(request.AudioObjectKey, 1024);
        var normalizedAudioContentType = NormalizeOptionalText(request.AudioContentType, 100);
        var normalizedSortOrder = Math.Clamp(request.SortOrder, -500_000, 500_000);
        var normalizedDurationSeconds = request.DurationSeconds is > 0 ? request.DurationSeconds : null;

        DateTimeOffset? normalizedPublishedAt = request.PublishedAt;
        if (string.Equals(normalizedStatus, "published", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPublishedAt ??= DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(normalizedAudioObjectKey))
            {
                return new AdminOperationResult(false, "Gepubliseerde stories vereis 'n audio object key.");
            }
        }

        if (string.IsNullOrWhiteSpace(normalizedAudioBucket) ||
            string.IsNullOrWhiteSpace(normalizedAudioObjectKey))
        {
            return new AdminOperationResult(false, "R2 stories vereis beide bucket en object key.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["slug"] = normalizedSlug,
            ["title"] = normalizedTitle,
            ["summary"] = normalizedSummary,
            ["description"] = normalizedDescription,
            ["youtube_url"] = normalizedYouTubeUrl,
            ["cover_image_path"] = normalizedCoverImagePath,
            ["thumbnail_image_path"] = normalizedThumbnailPath,
            ["audio_provider"] = "r2",
            ["audio_bucket"] = normalizedAudioBucket,
            ["audio_object_key"] = normalizedAudioObjectKey,
            ["audio_content_type"] = normalizedAudioContentType,
            ["access_level"] = normalizedAccessLevel,
            ["status"] = normalizedStatus,
            ["sort_order"] = normalizedSortOrder,
            ["published_at"] = normalizedPublishedAt?.UtcDateTime,
            ["duration_seconds"] = normalizedDurationSeconds
        };

        var createUri = new Uri(baseUri, "rest/v1/stories?select=story_id");
        using var createRequest = CreateJsonRequest(HttpMethod.Post, createUri, apiKey, payload, "return=representation");
        using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
        if (createResponse.IsSuccessStatusCode)
        {
            var responseBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            var createdStoryId = TryReadFirstGuidProperty(responseBody, "story_id");
            InvalidateStoryCatalogCache();

            if (createdStoryId.HasValue &&
                string.Equals(normalizedStatus, "published", StringComparison.OrdinalIgnoreCase))
            {
                await _userNotificationService.CreatePublishedStoryNotificationsAsync(
                    new PublishedStoryNotificationRequest(
                        createdStoryId.Value,
                        normalizedSlug,
                        normalizedTitle,
                        normalizedAccessLevel,
                        normalizedSummary,
                        normalizedThumbnailPath,
                        normalizedCoverImagePath),
                    cancellationToken);
            }

            return new AdminOperationResult(true, EntityId: createdStoryId);
        }

        var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Story create failed. slug={Slug} Status={StatusCode} Body={Body}",
            normalizedSlug,
            (int)createResponse.StatusCode,
            createBody);

        if (ContainsDuplicateSlugViolation(createBody))
        {
            return new AdminOperationResult(false, "Storie slug bestaan reeds.");
        }

        return new AdminOperationResult(false, "Kon nie nuwe storie skep nie.");
    }

    public async Task<IReadOnlyList<AdminPlaylistRecord>> GetPlaylistsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return Array.Empty<AdminPlaylistRecord>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return Array.Empty<AdminPlaylistRecord>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<AdminPlaylistRecord>();
        }

        var playlistsTask = FetchPlaylistsAsync(baseUri, apiKey, cancellationToken);
        var playlistItemsTask = FetchPlaylistItemsAsync(baseUri, apiKey, cancellationToken);
        var storiesTask = FetchStoryLookupAsync(baseUri, apiKey, cancellationToken);
        await Task.WhenAll(playlistsTask, playlistItemsTask, storiesTask);

        var storiesById = storiesTask.Result
            .Where(story => story.StoryId != Guid.Empty)
            .ToDictionary(story => story.StoryId);

        var itemsByPlaylist = playlistItemsTask.Result
            .Where(item => item.PlaylistId != Guid.Empty && item.StoryId != Guid.Empty)
            .GroupBy(item => item.PlaylistId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(item => item.SortOrder)
                    .Select(item =>
                    {
                        if (!storiesById.TryGetValue(item.StoryId, out var story))
                        {
                            return new AdminPlaylistStoryItem(
                                StoryId: item.StoryId,
                                StorySlug: string.Empty,
                                StoryTitle: $"Onbekende storie ({item.StoryId:D})",
                                SortOrder: item.SortOrder,
                                IsShowcase: item.IsShowcase);
                        }

                        return new AdminPlaylistStoryItem(
                            StoryId: story.StoryId,
                            StorySlug: story.Slug?.Trim() ?? string.Empty,
                            StoryTitle: story.Title?.Trim() ?? story.Slug?.Trim() ?? $"Storie {story.StoryId:D}",
                            SortOrder: item.SortOrder,
                            IsShowcase: item.IsShowcase);
                    })
                    .ToArray());

        return playlistsTask.Result
            .Where(row => row.PlaylistId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Title))
            .Select(row =>
            {
                var stories = itemsByPlaylist.TryGetValue(row.PlaylistId, out var values)
                    ? values
                    : Array.Empty<AdminPlaylistStoryItem>();

                return new AdminPlaylistRecord(
                    PlaylistId: row.PlaylistId,
                    Slug: row.Slug.Trim(),
                    Title: row.Title.Trim(),
                    IsSystemPlaylist: IsSystemPlaylistType(row.PlaylistType),
                    SystemKey: NormalizeSystemKey(row.SystemKey),
                    Description: NormalizeOptionalText(row.Description, 4000),
                    LogoImagePath: NormalizePlaylistImagePath(row.LogoImagePath),
                    BackdropImagePath: NormalizePlaylistImagePath(row.BackdropImagePath),
                    ShowcaseImagePath: NormalizePlaylistImagePath(row.ShowcaseImagePath),
                    SortOrder: row.SortOrder,
                    MaxItems: row.MaxItems is > 0 ? row.MaxItems : null,
                    IsEnabled: row.IsEnabled,
                    ShowOnHome: row.ShowOnHome,
                    IncludeInSpeellysteCarousel: row.IncludeInSpeellysteCarousel,
                    ShowShowcaseImageOnLuisterPage: row.ShowShowcaseImageOnLuisterPage,
                    UpdatedAt: row.UpdatedAt,
                    Stories: stories);
            })
            .OrderBy(playlist => playlist.SortOrder)
            .ThenBy(playlist => playlist.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AdminOperationResult> SavePlaylistAsync(
        string? adminEmail,
        AdminPlaylistUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var normalizedSlug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!StorySlugRegex().IsMatch(normalizedSlug))
        {
            return new AdminOperationResult(false, "Playlist slug is ongeldig.");
        }

        var normalizedTitle = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return new AdminOperationResult(false, "Playlist titel is verpligtend.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        if (request.PlaylistId is null || request.PlaylistId == Guid.Empty)
        {
            var createPayload = new Dictionary<string, object?>
            {
                ["slug"] = normalizedSlug,
                ["title"] = normalizedTitle,
                ["description"] = NormalizeOptionalText(request.Description, 4000),
                ["logo_image_path"] = NormalizePlaylistImagePath(request.LogoImagePath),
                ["backdrop_image_path"] = NormalizePlaylistImagePath(request.BackdropImagePath),
                ["showcase_image_path"] = NormalizePlaylistImagePath(request.ShowcaseImagePath),
                ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000),
                ["max_items"] = request.MaxItems is > 0 ? request.MaxItems : null,
                ["is_enabled"] = request.IsEnabled,
                ["show_on_home"] = request.ShowOnHome,
                ["include_in_speellyste_carousel"] = request.IncludeInSpeellysteCarousel,
                ["show_showcase_image_on_luister_page"] = request.ShowShowcaseImageOnLuisterPage,
                ["playlist_type"] = "manual",
                ["system_key"] = null
            };

            var createUri = new Uri(baseUri, "rest/v1/story_playlists?select=playlist_id");
            using var createRequest = CreateJsonRequest(HttpMethod.Post, createUri, apiKey, createPayload, "return=representation");
            using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
            if (!createResponse.IsSuccessStatusCode)
            {
                var responseBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Playlist create failed. slug={Slug} Status={StatusCode} Body={Body}",
                    normalizedSlug,
                    (int)createResponse.StatusCode,
                    responseBody);
                return new AdminOperationResult(false, "Kon nie playlist skep nie.");
            }

            var body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            var createdPlaylistId = TryReadFirstGuidProperty(body, "playlist_id");
            InvalidateStoryCatalogCache();
            return new AdminOperationResult(true, EntityId: createdPlaylistId);
        }

        var existingPlaylist = await FetchPlaylistByIdAsync(baseUri, apiKey, request.PlaylistId.Value, cancellationToken);
        if (existingPlaylist is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige playlist.");
        }

        var isSystemPlaylist = IsSystemPlaylistType(existingPlaylist.PlaylistType);
        var payload = isSystemPlaylist
            ? new Dictionary<string, object?>
            {
                ["description"] = NormalizeOptionalText(request.Description, 4000),
                ["logo_image_path"] = NormalizePlaylistImagePath(request.LogoImagePath),
                ["backdrop_image_path"] = NormalizePlaylistImagePath(request.BackdropImagePath),
                ["showcase_image_path"] = NormalizePlaylistImagePath(request.ShowcaseImagePath),
                ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000),
                ["is_enabled"] = request.IsEnabled,
                ["show_on_home"] = request.ShowOnHome,
                ["include_in_speellyste_carousel"] = request.IncludeInSpeellysteCarousel,
                ["show_showcase_image_on_luister_page"] = request.ShowShowcaseImageOnLuisterPage
            }
            : new Dictionary<string, object?>
            {
                ["slug"] = normalizedSlug,
                ["title"] = normalizedTitle,
                ["description"] = NormalizeOptionalText(request.Description, 4000),
                ["logo_image_path"] = NormalizePlaylistImagePath(request.LogoImagePath),
                ["backdrop_image_path"] = NormalizePlaylistImagePath(request.BackdropImagePath),
                ["showcase_image_path"] = NormalizePlaylistImagePath(request.ShowcaseImagePath),
                ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000),
                ["max_items"] = request.MaxItems is > 0 ? request.MaxItems : null,
                ["is_enabled"] = request.IsEnabled,
                ["show_on_home"] = request.ShowOnHome,
                ["include_in_speellyste_carousel"] = request.IncludeInSpeellysteCarousel,
                ["show_showcase_image_on_luister_page"] = request.ShowShowcaseImageOnLuisterPage
            };

        var escapedPlaylistId = Uri.EscapeDataString(request.PlaylistId.Value.ToString("D"));
        var updateUri = new Uri(baseUri, $"rest/v1/story_playlists?playlist_id=eq.{escapedPlaylistId}");
        using var updateRequest = CreateJsonRequest(new HttpMethod("PATCH"), updateUri, apiKey, payload, "return=minimal");
        using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
        if (updateResponse.IsSuccessStatusCode)
        {
            InvalidateStoryCatalogCache();
            return new AdminOperationResult(true, EntityId: request.PlaylistId);
        }

        var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Playlist update failed. playlist_id={PlaylistId} Status={StatusCode} Body={Body}",
            request.PlaylistId,
            (int)updateResponse.StatusCode,
            updateBody);
        return new AdminOperationResult(false, "Kon nie playlist nou opdateer nie.");
    }

    public async Task<AdminOperationResult> SavePlaylistOrderAsync(
        string? adminEmail,
        IReadOnlyList<Guid> orderedPlaylistIds,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (orderedPlaylistIds.Count == 0)
        {
            return new AdminOperationResult(false, "Geen playlists is gekies vir ordening nie.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var normalizedIds = orderedPlaylistIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (normalizedIds.Length == 0)
        {
            return new AdminOperationResult(false, "Geen geldige playlists is gekies vir ordening nie.");
        }

        for (var index = 0; index < normalizedIds.Length; index++)
        {
            var playlistId = normalizedIds[index];
            var escapedPlaylistId = Uri.EscapeDataString(playlistId.ToString("D"));
            var uri = new Uri(baseUri, $"rest/v1/story_playlists?playlist_id=eq.{escapedPlaylistId}");
            var payload = new Dictionary<string, object?>
            {
                ["sort_order"] = (index + 1) * 10
            };

            using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=minimal");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Playlist order update failed. playlist_id={PlaylistId} Status={StatusCode} Body={Body}",
                playlistId,
                (int)response.StatusCode,
                responseBody);
            return new AdminOperationResult(false, "Kon nie playlist volgorde stoor nie.");
        }

        InvalidateStoryCatalogCache();
        return new AdminOperationResult(true);
    }

    public async Task<AdminOperationResult> SavePlaylistStoriesAsync(
        string? adminEmail,
        Guid playlistId,
        IReadOnlyList<AdminPlaylistStorySaveItem> orderedStories,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (playlistId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige playlist.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var existingPlaylist = await FetchPlaylistByIdAsync(baseUri, apiKey, playlistId, cancellationToken);
        if (existingPlaylist is null)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige playlist.");
        }

        if (IsSystemPlaylistType(existingPlaylist.PlaylistType))
        {
            return new AdminOperationResult(false, "Sisteem playlists kan nie stories handmatig wysig nie.");
        }

        var normalizedStories = orderedStories
            .Where(item => item.StoryId != Guid.Empty)
            .GroupBy(item => item.StoryId)
            .Select(group => group.First())
            .ToArray();
        if (normalizedStories.Length == 0)
        {
            InvalidateStoryCatalogCache();
            return new AdminOperationResult(true);
        }

        var showcaseCount = normalizedStories.Count(item => item.IsShowcase);
        if (showcaseCount > 1)
        {
            return new AdminOperationResult(false, "Playlists kan net een showcase storie he.");
        }

        var escapedPlaylistId = Uri.EscapeDataString(playlistId.ToString("D"));
        var deleteUri = new Uri(baseUri, $"rest/v1/story_playlist_items?playlist_id=eq.{escapedPlaylistId}");
        using (var deleteRequest = CreateRequest(HttpMethod.Delete, deleteUri, apiKey))
        {
            using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken);
            if (!deleteResponse.IsSuccessStatusCode)
            {
                var responseBody = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Playlist item delete failed. playlist_id={PlaylistId} Status={StatusCode} Body={Body}",
                    playlistId,
                    (int)deleteResponse.StatusCode,
                    responseBody);
                return new AdminOperationResult(false, "Kon nie bestaande playlist stories verwyder nie.");
            }
        }

        var payload = normalizedStories
            .Select((story, index) => new Dictionary<string, object?>
            {
                ["playlist_id"] = playlistId,
                ["story_id"] = story.StoryId,
                ["sort_order"] = index + 1,
                ["is_showcase"] = story.IsShowcase
            })
            .ToArray();

        var insertUri = new Uri(baseUri, "rest/v1/story_playlist_items");
        using var insertRequest = CreateJsonRequest(HttpMethod.Post, insertUri, apiKey, payload, "return=minimal");
        using var insertResponse = await _httpClient.SendAsync(insertRequest, cancellationToken);
        if (insertResponse.IsSuccessStatusCode)
        {
            InvalidateStoryCatalogCache();
            return new AdminOperationResult(true);
        }

        var insertBody = await insertResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Playlist item insert failed. playlist_id={PlaylistId} Status={StatusCode} Body={Body}",
            playlistId,
            (int)insertResponse.StatusCode,
            insertBody);
        return new AdminOperationResult(false, "Kon nie nuwe playlist stories stoor nie.");
    }

    public async Task<IReadOnlyList<AdminStoreProductRecord>> GetStoreProductsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return Array.Empty<AdminStoreProductRecord>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return Array.Empty<AdminStoreProductRecord>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<AdminStoreProductRecord>();
        }

        var rows = await FetchStoreProductsAsync(baseUri, apiKey, cancellationToken);

        var records = rows
            .Where(row => row.StoreProductId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Where(row => !string.IsNullOrWhiteSpace(row.ImagePath))
            .Select(row =>
            {
                var normalizedSlug = row.Slug.Trim().ToLowerInvariant();
                var normalizedName = row.Name.Trim();
                var normalizedImagePath = NormalizeStoreProductImagePath(row.ImagePath);
                var normalizedAltText = NormalizeOptionalText(row.AltText, 220) ?? $"{normalizedName} produk";

                return new AdminStoreProductRecord(
                    StoreProductId: row.StoreProductId,
                    Slug: normalizedSlug,
                    Name: normalizedName,
                    Description: NormalizeOptionalText(row.Description, 600),
                    ImagePath: normalizedImagePath,
                    AltText: normalizedAltText,
                    ThemeClass: NormalizeOptionalText(row.ThemeClass, 80),
                    UnitPriceZar: row.UnitPriceZar,
                    SortOrder: Math.Clamp(row.SortOrder, -500_000, 500_000),
                    IsEnabled: row.IsEnabled,
                    UpdatedAt: row.UpdatedAt);
            })
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return records.Length > 0
            ? records
            : BuildFallbackStoreProductRecords();
    }

    public async Task<AdminOperationResult> SaveStoreProductAsync(
        string? adminEmail,
        AdminStoreProductSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var normalizedSlug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!StorySlugRegex().IsMatch(normalizedSlug))
        {
            return new AdminOperationResult(false, "Winkel produk slug is ongeldig.");
        }

        var normalizedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return new AdminOperationResult(false, "Winkel produk naam is verpligtend.");
        }

        var normalizedImagePath = NormalizeStoreProductImagePath(request.ImagePath);
        if (string.IsNullOrWhiteSpace(normalizedImagePath))
        {
            return new AdminOperationResult(false, "Winkel produk image is verpligtend.");
        }

        var normalizedPrice = decimal.Round(request.UnitPriceZar, 2, MidpointRounding.AwayFromZero);
        if (normalizedPrice <= 0m || normalizedPrice > 999_999.99m)
        {
            return new AdminOperationResult(false, "Winkel produk prys moet groter as nul wees.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["slug"] = normalizedSlug,
            ["name"] = normalizedName,
            ["description"] = NormalizeOptionalText(request.Description, 600),
            ["image_path"] = normalizedImagePath,
            ["alt_text"] = NormalizeOptionalText(request.AltText, 220) ?? $"{normalizedName} produk",
            ["theme_class"] = NormalizeOptionalText(request.ThemeClass, 80),
            ["unit_price_zar"] = normalizedPrice,
            ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000),
            ["is_enabled"] = request.IsEnabled
        };

        if (request.StoreProductId is null || request.StoreProductId == Guid.Empty)
        {
            var createUri = new Uri(baseUri, "rest/v1/store_products");
            using var createRequest = CreateJsonRequest(HttpMethod.Post, createUri, apiKey, new[] { payload }, "return=representation");
            using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
            var responseBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!createResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Store product create failed. slug={Slug} Status={StatusCode} Body={Body}",
                    normalizedSlug,
                    (int)createResponse.StatusCode,
                    responseBody);

                if ((int)createResponse.StatusCode == 409 ||
                    responseBody.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                {
                    return new AdminOperationResult(false, "Winkel produk slug bestaan reeds.");
                }

                return new AdminOperationResult(false, "Kon nie winkel produk skep nie.");
            }

            InvalidateStoreProductCatalogCache();
            return new AdminOperationResult(true, EntityId: TryReadFirstGuidProperty(responseBody, "store_product_id"));
        }

        var escapedStoreProductId = Uri.EscapeDataString(request.StoreProductId.Value.ToString("D"));
        var updateUri = new Uri(baseUri, $"rest/v1/store_products?store_product_id=eq.{escapedStoreProductId}");
        using var updateRequest = CreateJsonRequest(new HttpMethod("PATCH"), updateUri, apiKey, payload, "return=representation");
        using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
        var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);

        if (updateResponse.IsSuccessStatusCode)
        {
            var hasUpdatedRow = TryReadFirstGuidProperty(updateBody, "store_product_id").HasValue;
            if (!hasUpdatedRow)
            {
                return new AdminOperationResult(false, "Kies asseblief 'n geldige winkel produk.");
            }

            InvalidateStoreProductCatalogCache();
            return new AdminOperationResult(true, EntityId: request.StoreProductId);
        }

        _logger.LogWarning(
            "Store product update failed. store_product_id={StoreProductId} Status={StatusCode} Body={Body}",
            request.StoreProductId,
            (int)updateResponse.StatusCode,
            updateBody);

        if ((int)updateResponse.StatusCode == 409 ||
            updateBody.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
        {
            return new AdminOperationResult(false, "Winkel produk slug bestaan reeds.");
        }

        return new AdminOperationResult(false, "Kon nie winkel produk nou opdateer nie.");
    }

    public async Task<AdminOperationResult> DeleteStoreProductAsync(
        string? adminEmail,
        Guid storeProductId,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (storeProductId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige winkel produk.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var escapedStoreProductId = Uri.EscapeDataString(storeProductId.ToString("D"));
        var deleteUri = new Uri(baseUri, $"rest/v1/store_products?store_product_id=eq.{escapedStoreProductId}");
        using var deleteRequest = CreateRequest(HttpMethod.Delete, deleteUri, apiKey);
        using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken);
        if (deleteResponse.IsSuccessStatusCode)
        {
            InvalidateStoreProductCatalogCache();
            return new AdminOperationResult(true);
        }

        var responseBody = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Store product delete failed. store_product_id={StoreProductId} Status={StatusCode} Body={Body}",
            storeProductId,
            (int)deleteResponse.StatusCode,
            responseBody);
        return new AdminOperationResult(false, "Kon nie winkel produk nou verwyder nie.");
    }

    public async Task<AdminAnalyticsSnapshot> GetAnalyticsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return AdminAnalyticsSnapshot.Empty;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return AdminAnalyticsSnapshot.Empty;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AdminAnalyticsSnapshot.Empty;
        }

        return await InvokeRpcAsync<AdminAnalyticsSnapshot>(
                   baseUri,
                   apiKey,
                   "get_admin_analytics_snapshot",
                   new { },
                   cancellationToken)
               ?? AdminAnalyticsSnapshot.Empty;
    }

    public async Task<AdminSubscriberReportsSnapshot> GetSubscriberReportsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return AdminSubscriberReportsSnapshot.Empty;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return AdminSubscriberReportsSnapshot.Empty;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AdminSubscriberReportsSnapshot.Empty;
        }

        var wordPressSubscriberReportsTask = FetchWordPressSubscriberReportsAsync(baseUri, apiKey, cancellationToken);
        var subscribersTask = FetchSubscribersAsync(baseUri, apiKey, cancellationToken);
        var subscriptionsTask = FetchSubscriptionsAsync(baseUri, apiKey, cancellationToken);
        var paystackRevenueEventsTask = FetchPaystackRevenueEventsAsync(baseUri, apiKey, cancellationToken);
        var subscriptionTiersTask = FetchSubscriptionTiersAsync(baseUri, apiKey, cancellationToken);
        var recoveriesTask = FetchSubscriptionRecoveriesAsync(baseUri, apiKey, cancellationToken);
        var abandonedCartRecoveriesTask = FetchAbandonedCartRecoveriesAsync(baseUri, apiKey, cancellationToken);
        var authSessionsTask = FetchAuthSessionsAsync(baseUri, apiKey, cancellationToken);
        var storyViewsTask = FetchStoryViewsAsync(baseUri, apiKey, cancellationToken);
        var storyListenSessionsTask = FetchStoryListenSessionsAsync(baseUri, apiKey, cancellationToken);

        await Task.WhenAll(
            wordPressSubscriberReportsTask,
            subscribersTask,
            subscriptionsTask,
            paystackRevenueEventsTask,
            subscriptionTiersTask,
            recoveriesTask,
            abandonedCartRecoveriesTask,
            authSessionsTask,
            storyViewsTask,
            storyListenSessionsTask);

        var wordPressSubscriberReports = wordPressSubscriberReportsTask.Result;
        if (wordPressSubscriberReports is null || !wordPressSubscriberReports.HasWordPressData)
        {
            wordPressSubscriberReports = await TrySyncAndFetchWordPressSubscriberReportsAsync(
                baseUri,
                apiKey,
                cancellationToken) ?? wordPressSubscriberReports;
        }

        return BuildSubscriberReportsSnapshot(
            wordPressSubscriberReports,
            subscribersTask.Result,
            subscriptionsTask.Result,
            paystackRevenueEventsTask.Result,
            BuildPaystackSubscriptionCreateIdentifiers(paystackRevenueEventsTask.Result),
            subscriptionTiersTask.Result,
            recoveriesTask.Result,
            abandonedCartRecoveriesTask.Result,
            authSessionsTask.Result,
            storyViewsTask.Result,
            storyListenSessionsTask.Result);
    }

    private async Task<AdminOperationContext?> TryCreateAdminOperationContextAsync(
        string? adminEmail,
        CancellationToken cancellationToken)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return null;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var normalizedAdminEmail = NormalizeEmail(adminEmail);
        if (normalizedAdminEmail is null ||
            !await IsAdminCoreAsync(baseUri, apiKey, normalizedAdminEmail, cancellationToken))
        {
            return null;
        }

        return new AdminOperationContext(baseUri, apiKey, normalizedAdminEmail);
    }

    private async Task<AdminSubscriberRecord?> FetchSubscriberRecordByIdAsync(
        AdminOperationContext context,
        Guid subscriberId,
        CancellationToken cancellationToken)
    {
        if (subscriberId == Guid.Empty)
        {
            return null;
        }

        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.ToString("D"));
        var uri = new Uri(
            context.BaseUri,
            "rest/v1/subscribers" +
            "?select=subscriber_id,email,first_name,last_name,display_name,mobile_number,profile_image_url,created_at,updated_at,disabled_at,disabled_by_admin_email,disabled_reason" +
            $"&subscriber_id=eq.{escapedSubscriberId}&limit=1");
        var rows = await FetchRowsAsync<SubscriberRow>(uri, context.ApiKey, cancellationToken);
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        var subscriptions = await FetchSubscriberSubscriptionsAsync(context, subscriberId, cancellationToken);
        var summary = BuildSubscriptionSummaryMap(subscriptions).GetValueOrDefault(subscriberId);
        return MapSubscriberRecord(row, summary);
    }

    private async Task<IReadOnlyList<AdminSubscriberRecord>> FetchSubscriberRecordsByIdsAsync(
        IReadOnlyList<Guid> subscriberIds,
        CancellationToken cancellationToken)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return Array.Empty<AdminSubscriberRecord>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<AdminSubscriberRecord>();
        }

        var normalizedIds = AdminSubscriberManagementLogic.NormalizeSelectedSubscriberIds(subscriberIds);
        if (normalizedIds.Count == 0)
        {
            return Array.Empty<AdminSubscriberRecord>();
        }

        var idFilter = string.Join(",", normalizedIds.Select(id => id.ToString("D")));
        var uri = new Uri(
            baseUri,
            "rest/v1/subscribers" +
            "?select=subscriber_id,email,first_name,last_name,display_name,mobile_number,profile_image_url,created_at,updated_at,disabled_at,disabled_by_admin_email,disabled_reason" +
            $"&subscriber_id=in.({idFilter})&limit=500");
        var subscribers = await FetchRowsAsync<SubscriberRow>(uri, apiKey, cancellationToken);
        var subscriptions = await FetchSubscriptionsAsync(baseUri, apiKey, cancellationToken);
        var summaries = BuildSubscriptionSummaryMap(subscriptions);

        return subscribers
            .Select(row => MapSubscriberRecord(row, summaries.GetValueOrDefault(row.SubscriberId)))
            .OrderBy(row => row.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, SubscriberDisabledStateRow>> FetchSubscriberDisabledStatesAsync(
        Uri baseUri,
        string apiKey,
        IReadOnlyList<Guid> subscriberIds,
        CancellationToken cancellationToken)
    {
        var normalizedIds = AdminSubscriberManagementLogic.NormalizeSelectedSubscriberIds(subscriberIds);
        if (normalizedIds.Count == 0)
        {
            return new Dictionary<Guid, SubscriberDisabledStateRow>();
        }

        var idFilter = string.Join(",", normalizedIds.Select(id => id.ToString("D")));
        var uri = new Uri(
            baseUri,
            "rest/v1/subscribers" +
            "?select=subscriber_id,disabled_at,disabled_by_admin_email,disabled_reason" +
            $"&subscriber_id=in.({idFilter})&limit=500");
        var rows = await FetchRowsAsync<SubscriberDisabledStateRow>(uri, apiKey, cancellationToken);
        return rows
            .Where(row => row.SubscriberId != Guid.Empty)
            .ToDictionary(row => row.SubscriberId);
    }

    private async Task<IReadOnlyList<SubscriptionRow>> FetchSubscriberSubscriptionsAsync(
        AdminOperationContext context,
        Guid subscriberId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.ToString("D"));
        var uri = new Uri(
            context.BaseUri,
            "rest/v1/subscriptions" +
            "?select=subscription_id,subscriber_id,tier_code,provider,source_system,status,subscribed_at,next_renewal_at,cancelled_at,provider_payment_id,provider_token,provider_email_token,provider_transaction_id" +
            $"&subscriber_id=eq.{escapedSubscriberId}&order=subscribed_at.desc&limit=100");
        return await FetchRowsAsync<SubscriptionRow>(uri, context.ApiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<AdminSubscriptionTierOption>> FetchAdminSubscriptionTierOptionsAsync(
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            context.BaseUri,
            "rest/v1/subscription_tiers?select=tier_code,display_name,price_zar,is_active&order=display_name.asc&limit=100");
        var rows = await FetchRowsAsync<SubscriptionTierRow>(uri, context.ApiKey, cancellationToken);
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.TierCode))
            .Select(row => new AdminSubscriptionTierOption(
                row.TierCode!.Trim(),
                NormalizeOptionalText(row.DisplayName, 120) ?? row.TierCode!.Trim(),
                row.PriceZar,
                row.IsActive))
            .ToArray();
    }

    private async Task<IReadOnlyList<AdminSubscriberBillingEventRecord>> FetchSubscriberBillingEventsAsync(
        AdminOperationContext context,
        Guid subscriberId,
        IReadOnlyList<SubscriptionRow> subscriptions,
        CancellationToken cancellationToken)
    {
        _ = subscriberId;
        var subscriptionIds = subscriptions
            .Select(row => row.SubscriptionId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (subscriptionIds.Length == 0)
        {
            return Array.Empty<AdminSubscriberBillingEventRecord>();
        }

        var idFilter = string.Join(",", subscriptionIds.Select(id => id.ToString("D")));
        var uri = new Uri(
            context.BaseUri,
            "rest/v1/subscription_events" +
            "?select=received_at,provider,event_type,event_status,provider_payment_id,provider_transaction_id" +
            $"&subscription_id=in.({idFilter})&order=received_at.desc&limit=50");
        var rows = await FetchRowsAsync<SubscriptionEventDetailRow>(uri, context.ApiKey, cancellationToken);
        return rows
            .Select(row => new AdminSubscriberBillingEventRecord(
                row.ReceivedAt,
                NormalizeOptionalText(row.Provider, 40) ?? "-",
                NormalizeOptionalText(row.EventType, 80),
                NormalizeOptionalText(row.EventStatus, 80),
                NormalizeOptionalText(row.ProviderPaymentId, 160),
                NormalizeOptionalText(row.ProviderTransactionId, 160)))
            .ToArray();
    }

    private async Task<IReadOnlyList<AdminSubscriberStoreOrderRecord>> FetchSubscriberStoreOrdersAsync(
        AdminOperationContext context,
        string email,
        CancellationToken cancellationToken)
    {
        var escapedEmail = Uri.EscapeDataString(email);
        var uri = new Uri(
            context.BaseUri,
            "rest/v1/store_orders" +
            "?select=order_id,order_reference,product_name,total_price_zar,payment_status,provider,provider_transaction_id,created_at,paid_at" +
            $"&customer_email=eq.{escapedEmail}&order=created_at.desc&limit=50");
        var rows = await FetchRowsAsync<StoreOrderDetailRow>(uri, context.ApiKey, cancellationToken);
        return rows
            .Where(row => row.OrderId != Guid.Empty)
            .Select(row => new AdminSubscriberStoreOrderRecord(
                row.OrderId,
                row.OrderReference ?? string.Empty,
                row.ProductName ?? string.Empty,
                row.TotalPriceZar,
                row.PaymentStatus ?? string.Empty,
                row.Provider ?? string.Empty,
                NormalizeOptionalText(row.ProviderTransactionId, 160),
                row.CreatedAt,
                row.PaidAt))
            .ToArray();
    }

    private async Task<IReadOnlyList<AdminSubscriberActivityRecord>> FetchSubscriberActivityAsync(
        AdminOperationContext context,
        Guid subscriberId,
        string email,
        CancellationToken cancellationToken)
    {
        var activity = new List<AdminSubscriberActivityRecord>();
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.ToString("D"));
        var escapedEmail = Uri.EscapeDataString(email);

        var sessionRows = await FetchRowsAsync<AuthSessionDetailRow>(
            new Uri(context.BaseUri, $"rest/v1/auth_sessions?select=created_at,expires_at,revoked_at&email=eq.{escapedEmail}&order=created_at.desc&limit=10"),
            context.ApiKey,
            cancellationToken);
        activity.AddRange(sessionRows.Select(row => new AdminSubscriberActivityRecord(
            row.CreatedAt,
            "login",
            "Aanmelding",
            row.RevokedAt is null ? "Active or expired session" : "Session revoked")));

        var viewRows = await FetchRowsAsync<StoryViewDetailRow>(
            new Uri(context.BaseUri, $"rest/v1/story_views?select=story_slug,story_path,viewed_at&subscriber_id=eq.{escapedSubscriberId}&order=viewed_at.desc&limit=10"),
            context.ApiKey,
            cancellationToken);
        activity.AddRange(viewRows.Select(row => new AdminSubscriberActivityRecord(
            row.ViewedAt,
            "story_view",
            $"Storie gekyk: {row.StorySlug}",
            row.StoryPath)));

        var listenRows = await FetchRowsAsync<StoryListenDetailRow>(
            new Uri(context.BaseUri, $"rest/v1/story_listen_events?select=story_slug,event_type,listened_seconds,occurred_at&subscriber_id=eq.{escapedSubscriberId}&order=occurred_at.desc&limit=10"),
            context.ApiKey,
            cancellationToken);
        activity.AddRange(listenRows.Select(row => new AdminSubscriberActivityRecord(
            row.OccurredAt,
            "story_listen",
            $"Luister: {row.StorySlug}",
            $"{row.EventType}; {row.ListenedSeconds:N0}s")));

        var favoriteRows = await FetchRowsAsync<StoryFavoriteDetailRow>(
            new Uri(context.BaseUri, $"rest/v1/story_favorites?select=story_slug,source,updated_at&subscriber_id=eq.{escapedSubscriberId}&order=updated_at.desc&limit=10"),
            context.ApiKey,
            cancellationToken);
        activity.AddRange(favoriteRows.Select(row => new AdminSubscriberActivityRecord(
            row.UpdatedAt,
            "favorite",
            $"Gunsteling: {row.StorySlug}",
            row.Source)));

        return activity
            .OrderByDescending(item => item.OccurredAt)
            .Take(40)
            .ToArray();
    }

    private async Task<IReadOnlyList<AdminSubscriberRecoveryRecord>> FetchSubscriberRecoveriesAsync(
        AdminOperationContext context,
        AdminSubscriberRecord subscriber,
        IReadOnlyList<SubscriptionRow> subscriptions,
        CancellationToken cancellationToken)
    {
        var result = new List<AdminSubscriberRecoveryRecord>();
        var subscriptionRecoveries = await FetchSubscriptionRecoveryRowsForSubscriberAsync(context, subscriptions, cancellationToken);
        result.AddRange(subscriptionRecoveries.Select(row => new AdminSubscriberRecoveryRecord(
            row.RecoveryId.ToString("D"),
            "subscription",
            row.ResolvedAt is null ? "active" : "resolved",
            row.SubscriptionId.ToString("D"),
            row.Provider,
            row.CreatedAt,
            row.ResolvedAt,
            row.Resolution)));

        var escapedEmail = Uri.EscapeDataString(subscriber.Email);
        var abandonedUri = new Uri(
            context.BaseUri,
            "rest/v1/abandoned_cart_recoveries" +
            "?select=recovery_id,source_type,source_key,created_at,resolved_at,resolution" +
            $"&customer_email=eq.{escapedEmail}&order=created_at.desc&limit=30");
        var abandonedRows = await FetchRowsAsync<AbandonedCartRecoveryDetailRow>(abandonedUri, context.ApiKey, cancellationToken);
        result.AddRange(abandonedRows.Select(row => new AdminSubscriberRecoveryRecord(
            row.RecoveryId.ToString("D"),
            row.SourceType ?? "abandoned_cart",
            row.ResolvedAt is null ? "active" : "resolved",
            row.SourceKey,
            null,
            row.CreatedAt,
            row.ResolvedAt,
            row.Resolution)));

        return result.OrderByDescending(row => row.CreatedAt).ToArray();
    }

    private async Task<IReadOnlyList<SubscriptionRecoveryDetailRow>> FetchSubscriptionRecoveryRowsForSubscriberAsync(
        AdminOperationContext context,
        IReadOnlyList<SubscriptionRow> subscriptions,
        CancellationToken cancellationToken)
    {
        var subscriptionIds = subscriptions
            .Select(row => row.SubscriptionId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (subscriptionIds.Length == 0)
        {
            return Array.Empty<SubscriptionRecoveryDetailRow>();
        }

        var idFilter = string.Join(",", subscriptionIds.Select(id => id.ToString("D")));
        var uri = new Uri(
            context.BaseUri,
            "rest/v1/subscription_payment_recoveries" +
            "?select=recovery_id,subscription_id,provider,provider_payment_id,created_at,resolved_at,resolution" +
            $"&subscription_id=in.({idFilter})&order=created_at.desc&limit=30");
        return await FetchRowsAsync<SubscriptionRecoveryDetailRow>(uri, context.ApiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<AdminSubscriberNotificationRecord>> FetchSubscriberNotificationsAsync(
        AdminOperationContext context,
        Guid subscriberId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.ToString("D"));
        var uri = new Uri(
            context.BaseUri,
            "rest/v1/subscriber_notifications" +
            "?select=notification_id,notification_type,title,created_at,read_at,cleared_at" +
            $"&subscriber_id=eq.{escapedSubscriberId}&order=created_at.desc&limit=30");
        var rows = await FetchRowsAsync<SubscriberNotificationDetailRow>(uri, context.ApiKey, cancellationToken);
        return rows
            .Where(row => row.NotificationId != Guid.Empty)
            .Select(row => new AdminSubscriberNotificationRecord(
                row.NotificationId,
                row.NotificationType ?? string.Empty,
                row.Title ?? string.Empty,
                row.CreatedAt,
                row.ReadAt,
                row.ClearedAt))
            .ToArray();
    }

    private async Task<IReadOnlyList<AdminSubscriberAuditRecord>> FetchSubscriberAuditAsync(
        AdminOperationContext context,
        Guid subscriberId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.ToString("D"));
        var uri = new Uri(
            context.BaseUri,
            "rest/v1/subscriber_admin_audit" +
            "?select=created_at,admin_email,action_key,notes" +
            $"&subscriber_id=eq.{escapedSubscriberId}&order=created_at.desc&limit=30");
        var rows = await FetchRowsAsync<SubscriberAdminAuditRow>(uri, context.ApiKey, cancellationToken);
        return rows
            .Select(row => new AdminSubscriberAuditRecord(
                row.CreatedAt,
                row.AdminEmail ?? string.Empty,
                row.ActionKey ?? string.Empty,
                NormalizeOptionalText(row.Notes, 400)))
            .ToArray();
    }

    private async Task WriteSubscriberAuditAsync(
        AdminOperationContext context,
        Guid subscriberId,
        string actionKey,
        string? notes,
        object metadata,
        CancellationToken cancellationToken)
    {
        if (subscriberId == Guid.Empty)
        {
            return;
        }

        var payload = new
        {
            subscriber_id = subscriberId,
            admin_email = context.AdminEmail,
            action_key = actionKey,
            notes = NormalizeOptionalText(notes, 400),
            metadata
        };
        var uri = new Uri(context.BaseUri, "rest/v1/subscriber_admin_audit");
        using var request = CreateJsonRequest(HttpMethod.Post, uri, context.ApiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Subscriber admin audit insert failed. subscriber_id={SubscriberId} action={Action} Status={StatusCode} Body={Body}",
                subscriberId,
                actionKey,
                (int)response.StatusCode,
                responseBody);
        }
    }

    public async Task<IReadOnlyList<AdminResourceTypeRecord>> GetResourceTypesAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return Array.Empty<AdminResourceTypeRecord>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return Array.Empty<AdminResourceTypeRecord>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<AdminResourceTypeRecord>();
        }

        var resourceTypesTask = FetchResourceTypesAsync(baseUri, apiKey, cancellationToken);
        var resourceDocumentsTask = FetchResourceDocumentsAsync(baseUri, apiKey, null, cancellationToken);
        await Task.WhenAll(resourceTypesTask, resourceDocumentsTask);

        var documentCounts = resourceDocumentsTask.Result
            .Where(row => row.ResourceTypeId != Guid.Empty)
            .GroupBy(row => row.ResourceTypeId)
            .ToDictionary(group => group.Key, group => group.Count());

        return resourceTypesTask.Result
            .Where(row => row.ResourceTypeId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new AdminResourceTypeRecord(
                ResourceTypeId: row.ResourceTypeId,
                Slug: row.Slug.Trim(),
                Name: row.Name.Trim(),
                Description: NormalizeOptionalText(row.Description, 4000),
                SortOrder: row.SortOrder,
                IsEnabled: row.IsEnabled,
                DocumentCount: documentCounts.TryGetValue(row.ResourceTypeId, out var count) ? count : 0,
                UpdatedAt: row.UpdatedAt))
            .OrderBy(type => type.SortOrder)
            .ThenBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AdminOperationResult> SaveResourceTypeAsync(
        string? adminEmail,
        AdminResourceTypeUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        var normalizedSlug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!StorySlugRegex().IsMatch(normalizedSlug))
        {
            return new AdminOperationResult(false, "Resource type slug is ongeldig.");
        }

        var normalizedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return new AdminOperationResult(false, "Resource type naam is verpligtend.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["slug"] = normalizedSlug,
            ["name"] = normalizedName,
            ["description"] = NormalizeOptionalText(request.Description, 4000),
            ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000),
            ["is_enabled"] = request.IsEnabled
        };

        if (request.ResourceTypeId is null || request.ResourceTypeId == Guid.Empty)
        {
            var createUri = new Uri(baseUri, "rest/v1/resource_types?select=resource_type_id");
            using var createRequest = CreateJsonRequest(HttpMethod.Post, createUri, apiKey, payload, "return=representation");
            using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
            if (!createResponse.IsSuccessStatusCode)
            {
                var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Resource type create failed. slug={Slug} Status={StatusCode} Body={Body}",
                    normalizedSlug,
                    (int)createResponse.StatusCode,
                    createBody);

                if (ContainsDuplicateResourceTypeSlugViolation(createBody))
                {
                    return new AdminOperationResult(false, "Resource type slug bestaan reeds.");
                }

                return new AdminOperationResult(false, "Kon nie resource type skep nie.");
            }

            var body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            InvalidateResourceCatalogCache();
            return new AdminOperationResult(true, EntityId: TryReadFirstGuidProperty(body, "resource_type_id"));
        }

        var escapedResourceTypeId = Uri.EscapeDataString(request.ResourceTypeId.Value.ToString("D"));
        var updateUri = new Uri(baseUri, $"rest/v1/resource_types?resource_type_id=eq.{escapedResourceTypeId}");
        using var updateRequest = CreateJsonRequest(new HttpMethod("PATCH"), updateUri, apiKey, payload, "return=minimal");
        using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
        if (updateResponse.IsSuccessStatusCode)
        {
            InvalidateResourceCatalogCache();
            return new AdminOperationResult(true, EntityId: request.ResourceTypeId);
        }

        var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Resource type update failed. resource_type_id={ResourceTypeId} Status={StatusCode} Body={Body}",
            request.ResourceTypeId,
            (int)updateResponse.StatusCode,
            updateBody);

        if (ContainsDuplicateResourceTypeSlugViolation(updateBody))
        {
            return new AdminOperationResult(false, "Resource type slug bestaan reeds.");
        }

        return new AdminOperationResult(false, "Kon nie resource type nou opdateer nie.");
    }

    public async Task<AdminOperationResult> DeleteResourceTypeAsync(
        string? adminEmail,
        Guid resourceTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (resourceTypeId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige resource type.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var escapedResourceTypeId = Uri.EscapeDataString(resourceTypeId.ToString("D"));
        var deleteUri = new Uri(baseUri, $"rest/v1/resource_types?resource_type_id=eq.{escapedResourceTypeId}");
        using var deleteRequest = CreateRequest(HttpMethod.Delete, deleteUri, apiKey);
        using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken);
        if (deleteResponse.IsSuccessStatusCode)
        {
            InvalidateResourceCatalogCache();
            return new AdminOperationResult(true);
        }

        var deleteBody = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Resource type delete failed. resource_type_id={ResourceTypeId} Status={StatusCode} Body={Body}",
            resourceTypeId,
            (int)deleteResponse.StatusCode,
            deleteBody);
        return new AdminOperationResult(false, "Kon nie resource type nou verwyder nie.");
    }

    public async Task<IReadOnlyList<AdminResourceDocumentRecord>> GetResourceDocumentsAsync(
        string? adminEmail,
        Guid resourceTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return Array.Empty<AdminResourceDocumentRecord>();
        }

        if (resourceTypeId == Guid.Empty)
        {
            return Array.Empty<AdminResourceDocumentRecord>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return Array.Empty<AdminResourceDocumentRecord>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<AdminResourceDocumentRecord>();
        }

        var rows = await FetchResourceDocumentsAsync(baseUri, apiKey, resourceTypeId, cancellationToken);
        return rows
            .Where(row => row.ResourceTypeId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Title))
            .Where(row => !string.IsNullOrWhiteSpace(row.FileName))
            .Where(row => !string.IsNullOrWhiteSpace(row.StorageBucket))
            .Where(row => !string.IsNullOrWhiteSpace(row.StorageObjectKey))
            .Select(row => new AdminResourceDocumentRecord(
                ResourceDocumentId: row.ResourceDocumentId,
                ResourceTypeId: row.ResourceTypeId,
                Slug: row.Slug!.Trim(),
                Title: row.Title!.Trim(),
                Description: NormalizeOptionalText(row.Description, 4000),
                FileName: Path.GetFileName(row.FileName!.Trim()),
                ContentType: NormalizeOptionalText(row.ContentType, 120) ?? "application/pdf",
                SizeBytes: Math.Max(0, row.SizeBytes),
                StorageProvider: NormalizeOptionalText(row.StorageProvider, 32) ?? "r2",
                StorageBucket: row.StorageBucket!.Trim(),
                StorageObjectKey: row.StorageObjectKey!.Trim(),
                PreviewImageContentType: NormalizeOptionalText(row.PreviewImageContentType, 120),
                PreviewImageBucket: NormalizeOptionalText(row.PreviewImageBucket, 120),
                PreviewImageObjectKey: NormalizeOptionalText(row.PreviewImageObjectKey, 1024),
                RequiredTierCode: NormalizeOptionalText(row.RequiredTierCode, 64),
                SortOrder: row.SortOrder,
                IsEnabled: row.IsEnabled,
                CreatedAt: row.CreatedAt,
                DocumentUpdatedAt: row.DocumentUpdatedAt ?? row.UpdatedAt ?? row.CreatedAt,
                UpdatedAt: row.UpdatedAt))
            .OrderBy(document => document.SortOrder)
            .ThenBy(document => document.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AdminOperationResult> CreateResourceDocumentAsync(
        string? adminEmail,
        AdminResourceDocumentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (request.ResourceTypeId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige hulpbron tipe.");
        }

        var normalizedSlug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!StorySlugRegex().IsMatch(normalizedSlug))
        {
            return new AdminOperationResult(false, "Resource document slug is ongeldig.");
        }

        var normalizedTitle = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return new AdminOperationResult(false, "Resource document titel is verpligtend.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var normalizedFileName = Path.GetFileName(request.FileName?.Trim());
        if (string.IsNullOrWhiteSpace(normalizedFileName) ||
            !normalizedFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new AdminOperationResult(false, "Resource document file moet 'n PDF wees.");
        }

        var normalizedContentType = NormalizeOptionalText(request.ContentType, 120) ?? "application/pdf";
        if (!string.Equals(normalizedContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new AdminOperationResult(false, "Resource document file moet 'n PDF wees.");
        }

        var normalizedStorageProvider = request.StorageProvider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!string.Equals(normalizedStorageProvider, "r2", StringComparison.OrdinalIgnoreCase))
        {
            return new AdminOperationResult(false, "Resource document storage provider moet 'r2' wees.");
        }

        var normalizedStorageBucket = NormalizeOptionalText(request.StorageBucket, 120);
        var normalizedStorageObjectKey = NormalizeOptionalText(request.StorageObjectKey, 1024);
        if (string.IsNullOrWhiteSpace(normalizedStorageBucket) ||
            string.IsNullOrWhiteSpace(normalizedStorageObjectKey))
        {
            return new AdminOperationResult(false, "Resource document storage metadata ontbreek.");
        }

        var normalizedPreviewContentType = NormalizeOptionalText(request.PreviewImageContentType, 120)?.ToLowerInvariant();
        var normalizedPreviewBucket = NormalizeOptionalText(request.PreviewImageBucket, 120);
        var normalizedPreviewObjectKey = NormalizeOptionalText(request.PreviewImageObjectKey, 1024);
        if (string.IsNullOrWhiteSpace(normalizedPreviewContentType) ||
            string.IsNullOrWhiteSpace(normalizedPreviewBucket) ||
            string.IsNullOrWhiteSpace(normalizedPreviewObjectKey))
        {
            return new AdminOperationResult(false, "Resource document preview metadata ontbreek.");
        }

        if (!string.Equals(normalizedPreviewContentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            return new AdminOperationResult(false, "Resource document preview metadata ontbreek.");
        }

        var normalizedRequiredTierCode = NormalizeOptionalText(request.RequiredTierCode, 64)?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedRequiredTierCode))
        {
            if (!TierCodeRegex().IsMatch(normalizedRequiredTierCode))
            {
                return new AdminOperationResult(false, "Resource document tier is ongeldig.");
            }

            var tierExists = await SubscriptionTierExistsAsync(baseUri, apiKey, normalizedRequiredTierCode, cancellationToken);
            if (!tierExists)
            {
                return new AdminOperationResult(false, "Resource document tier bestaan nie.");
            }
        }

        var payload = new Dictionary<string, object?>
        {
            ["resource_type_id"] = request.ResourceTypeId,
            ["slug"] = normalizedSlug,
            ["title"] = normalizedTitle,
            ["description"] = NormalizeOptionalText(request.Description, 4000),
            ["file_name"] = normalizedFileName,
            ["content_type"] = normalizedContentType,
            ["size_bytes"] = Math.Max(0, request.SizeBytes),
            ["storage_provider"] = "r2",
            ["storage_bucket"] = normalizedStorageBucket,
            ["storage_object_key"] = normalizedStorageObjectKey,
            ["preview_image_content_type"] = normalizedPreviewContentType,
            ["preview_image_bucket"] = normalizedPreviewBucket,
            ["preview_image_object_key"] = normalizedPreviewObjectKey,
            ["preview_generated_at"] = DateTimeOffset.UtcNow,
            ["required_tier_code"] = normalizedRequiredTierCode,
            ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000),
            ["is_enabled"] = request.IsEnabled
        };

        var createUri = new Uri(baseUri, "rest/v1/resource_documents?select=resource_document_id");
        using var createRequest = CreateJsonRequest(HttpMethod.Post, createUri, apiKey, payload, "return=representation");
        using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
        if (createResponse.IsSuccessStatusCode)
        {
            var body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            var createdResourceDocumentId = TryReadFirstGuidProperty(body, "resource_document_id");
            InvalidateResourceCatalogCache();

            if (request.IsEnabled && createdResourceDocumentId.HasValue)
            {
                var resourceType = (await FetchResourceTypesAsync(baseUri, apiKey, cancellationToken))
                    .FirstOrDefault(type => type.ResourceTypeId == request.ResourceTypeId);
                if (resourceType is not null && !string.IsNullOrWhiteSpace(resourceType.Slug))
                {
                    await _userNotificationService.CreatePublishedResourceDocumentNotificationsAsync(
                        new PublishedResourceDocumentNotificationRequest(
                            createdResourceDocumentId.Value,
                            resourceType.Slug.Trim(),
                            string.IsNullOrWhiteSpace(resourceType.Name) ? "Hulpbronne" : resourceType.Name.Trim(),
                            normalizedTitle,
                            $"/media/resources/{createdResourceDocumentId.Value:D}/preview"),
                        cancellationToken);
                }
            }

            return new AdminOperationResult(true, EntityId: createdResourceDocumentId);
        }

        var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Resource document create failed. resource_type_id={ResourceTypeId} slug={Slug} Status={StatusCode} Body={Body}",
            request.ResourceTypeId,
            normalizedSlug,
            (int)createResponse.StatusCode,
            createBody);

        if (ContainsDuplicateResourceDocumentSlugViolation(createBody))
        {
            return new AdminOperationResult(false, "Resource document slug bestaan reeds.");
        }

        return new AdminOperationResult(false, "Kon nie resource document skep nie.");
    }

    public async Task<AdminOperationResult> DeleteResourceDocumentAsync(
        string? adminEmail,
        Guid resourceDocumentId,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (resourceDocumentId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige resource document.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var escapedResourceDocumentId = Uri.EscapeDataString(resourceDocumentId.ToString("D"));
        var deleteUri = new Uri(baseUri, $"rest/v1/resource_documents?resource_document_id=eq.{escapedResourceDocumentId}");
        using var deleteRequest = CreateRequest(HttpMethod.Delete, deleteUri, apiKey);
        using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken);
        if (deleteResponse.IsSuccessStatusCode)
        {
            InvalidateResourceCatalogCache();
            return new AdminOperationResult(true);
        }

        var deleteBody = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Resource document delete failed. resource_document_id={ResourceDocumentId} Status={StatusCode} Body={Body}",
            resourceDocumentId,
            (int)deleteResponse.StatusCode,
            deleteBody);
        return new AdminOperationResult(false, "Kon nie resource document nou verwyder nie.");
    }

    public async Task<AdminOperationResult> UpdateResourceDocumentAccessTierAsync(
        string? adminEmail,
        AdminResourceDocumentAccessTierUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return new AdminOperationResult(false, "Jy het nie admin toegang nie.");
        }

        if (request.ResourceDocumentId == Guid.Empty)
        {
            return new AdminOperationResult(false, "Kies asseblief 'n geldige resource document.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AdminOperationResult(false, "Supabase URL is nog nie opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdminOperationResult(false, "Supabase SecretKey is nog nie opgestel nie.");
        }

        var normalizedRequiredTierCode = NormalizeOptionalText(request.RequiredTierCode, 64)?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedRequiredTierCode))
        {
            if (!TierCodeRegex().IsMatch(normalizedRequiredTierCode))
            {
                return new AdminOperationResult(false, "Resource document tier is ongeldig.");
            }

            var tierExists = await SubscriptionTierExistsAsync(baseUri, apiKey, normalizedRequiredTierCode, cancellationToken);
            if (!tierExists)
            {
                return new AdminOperationResult(false, "Resource document tier bestaan nie.");
            }
        }

        var escapedResourceDocumentId = Uri.EscapeDataString(request.ResourceDocumentId.ToString("D"));
        var updateUri = new Uri(baseUri, $"rest/v1/resource_documents?resource_document_id=eq.{escapedResourceDocumentId}");
        var payload = new Dictionary<string, object?>
        {
            ["required_tier_code"] = normalizedRequiredTierCode,
            ["document_updated_at"] = DateTimeOffset.UtcNow
        };

        using var updateRequest = CreateJsonRequest(new HttpMethod("PATCH"), updateUri, apiKey, payload, "return=minimal");
        using var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);
        if (updateResponse.IsSuccessStatusCode)
        {
            InvalidateResourceCatalogCache();
            return new AdminOperationResult(true, EntityId: request.ResourceDocumentId);
        }

        var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Resource document tier update failed. resource_document_id={ResourceDocumentId} required_tier_code={RequiredTierCode} Status={StatusCode} Body={Body}",
            request.ResourceDocumentId,
            normalizedRequiredTierCode,
            (int)updateResponse.StatusCode,
            updateBody);

        return new AdminOperationResult(false, "Kon nie resource document tier nou opdateer nie.");
    }

    private async Task<IReadOnlyList<SubscriberRow>> FetchSubscribersAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscribers" +
            "?select=subscriber_id,email,first_name,last_name,display_name,mobile_number,profile_image_url,created_at,updated_at,disabled_at,disabled_by_admin_email,disabled_reason" +
            "&order=updated_at.desc" +
            "&limit=5000");
        return await FetchRowsAsync<SubscriberRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<SubscriptionRow>> FetchSubscriptionsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscriptions" +
            "?select=subscription_id,subscriber_id,tier_code,provider,source_system,status,subscribed_at,next_renewal_at,cancelled_at,billing_amount_zar,provider_payment_id,provider_transaction_id" +
            "&order=subscribed_at.desc" +
            "&limit=10000");

        return await FetchRowsAsync<SubscriptionRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<RevenueEventRow>> FetchPaystackRevenueEventsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_events" +
            "?select=provider,event_type,event_status,received_at,payload,provider_payment_id,provider_transaction_id" +
            "&provider=eq.paystack" +
            "&event_type=in.(charge.success,subscription.create,subscription.disable)" +
            "&order=received_at.desc" +
            "&limit=50000");

        return await FetchRowsAsync<RevenueEventRow>(uri, apiKey, cancellationToken);
    }

    private async Task<WordPressSubscriberReportsRpcSnapshot?> FetchWordPressSubscriberReportsAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken) =>
        await InvokeRpcAsync<WordPressSubscriberReportsRpcSnapshot>(
            baseUri,
            apiKey,
            "get_wordpress_subscriber_report_snapshot",
            new { },
            cancellationToken);

    private async Task<IReadOnlyList<SubscriptionTierRow>> FetchSubscriptionTiersAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_tiers" +
            "?select=tier_code,display_name,price_zar,is_active" +
            "&order=display_name.asc" +
            "&limit=100");

        return await FetchRowsAsync<SubscriptionTierRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<SubscriptionRecoveryRow>> FetchSubscriptionRecoveriesAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_payment_recoveries" +
            "?select=subscription_id,created_at,resolved_at,resolution" +
            "&order=created_at.desc" +
            "&limit=50000");

        return await FetchRowsAsync<SubscriptionRecoveryRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<AbandonedCartRecoveryRow>> FetchAbandonedCartRecoveriesAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/abandoned_cart_recoveries" +
            "?select=source_type,source_key,cart_total_zar,created_at,resolved_at,resolution" +
            "&order=created_at.desc" +
            "&limit=50000");

        return await FetchRowsAsync<AbandonedCartRecoveryRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<AuthSessionMetricRow>> FetchAuthSessionsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/auth_sessions" +
            "?select=created_at" +
            "&order=created_at.desc" +
            "&limit=50000");

        return await FetchRowsAsync<AuthSessionMetricRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<StoryViewMetricRow>> FetchStoryViewsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/story_views" +
            "?select=viewed_at" +
            "&order=viewed_at.desc" +
            "&limit=100000");

        return await FetchRowsAsync<StoryViewMetricRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<StoryListenSessionMetricRow>> FetchStoryListenSessionsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/story_listen_events" +
            "?select=session_id,occurred_at" +
            "&order=occurred_at.desc" +
            "&limit=100000");

        return await FetchRowsAsync<StoryListenSessionMetricRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<StoryRow>> FetchStoriesAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/stories" +
            "?select=story_id,slug,title,summary,description,youtube_url,cover_image_path,thumbnail_image_path,audio_provider,audio_bucket,audio_object_key,audio_content_type,access_level,status,sort_order,published_at,duration_seconds,updated_at" +
            "&order=updated_at.desc.nullslast" +
            "&order=sort_order.asc" +
            "&limit=2000");

        return await FetchRowsAsync<StoryRow>(uri, apiKey, cancellationToken);
    }

    private async Task<StoryRow?> FetchStoryByIdAsync(
        Uri baseUri,
        string apiKey,
        Guid storyId,
        CancellationToken cancellationToken)
    {
        if (storyId == Guid.Empty)
        {
            return null;
        }

        var escapedStoryId = Uri.EscapeDataString(storyId.ToString("D"));
        var uri = new Uri(
            baseUri,
            "rest/v1/stories" +
            "?select=story_id,status,published_at" +
            $"&story_id=eq.{escapedStoryId}" +
            "&limit=1");

        var rows = await FetchRowsAsync<StoryRow>(uri, apiKey, cancellationToken);
        return rows
            .Where(row => row.StoryId != Guid.Empty)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<PlaylistRow>> FetchPlaylistsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/story_playlists" +
            "?select=playlist_id,slug,title,playlist_type,system_key,description,logo_image_path,backdrop_image_path,showcase_image_path,sort_order,max_items,is_enabled,show_on_home,include_in_speellyste_carousel,show_showcase_image_on_luister_page,updated_at" +
            "&order=sort_order.asc" +
            "&order=title.asc" +
            "&limit=500");

        return await FetchRowsAsync<PlaylistRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<PlaylistItemRow>> FetchPlaylistItemsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/story_playlist_items" +
            "?select=playlist_id,story_id,sort_order,is_showcase" +
            "&order=sort_order.asc" +
            "&limit=5000");

        return await FetchRowsAsync<PlaylistItemRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<StoryLookupRow>> FetchStoryLookupAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/stories" +
            "?select=story_id,slug,title" +
            "&order=title.asc" +
            "&limit=5000");

        return await FetchRowsAsync<StoryLookupRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<ResourceTypeRow>> FetchResourceTypesAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/resource_types" +
            "?select=resource_type_id,slug,name,description,sort_order,is_enabled,updated_at" +
            "&order=sort_order.asc" +
            "&order=name.asc" +
            "&limit=500");

        return await FetchRowsAsync<ResourceTypeRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<StoreProductRow>> FetchStoreProductsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/store_products" +
            "?select=store_product_id,slug,name,description,image_path,alt_text,theme_class,unit_price_zar,sort_order,is_enabled,updated_at" +
            "&order=sort_order.asc" +
            "&order=name.asc" +
            "&limit=1000");

        return await FetchRowsAsync<StoreProductRow>(uri, apiKey, cancellationToken);
    }

    private static IReadOnlyList<AdminStoreProductRecord> BuildFallbackStoreProductRecords() =>
        StoreProductCatalog.All
            .OrderBy(product => product.SortOrder)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .Select(product => new AdminStoreProductRecord(
                StoreProductId: Guid.Empty,
                Slug: product.Slug,
                Name: product.Name,
                Description: product.Description,
                ImagePath: product.ImagePath,
                AltText: product.AltText,
                ThemeClass: product.ThemeClass,
                UnitPriceZar: product.UnitPriceZar,
                SortOrder: product.SortOrder,
                IsEnabled: product.IsEnabled,
                UpdatedAt: null))
            .ToArray();

    private async Task<IReadOnlyList<ResourceDocumentRow>> FetchResourceDocumentsAsync(
        Uri baseUri,
        string apiKey,
        Guid? resourceTypeId,
        CancellationToken cancellationToken)
    {
        var queryBuilder = new StringBuilder(
            "rest/v1/resource_documents" +
            "?select=resource_document_id,resource_type_id,slug,title,description,file_name,content_type,size_bytes,storage_provider,storage_bucket,storage_object_key,preview_image_content_type,preview_image_bucket,preview_image_object_key,required_tier_code,sort_order,is_enabled,created_at,document_updated_at,updated_at" +
            "&order=sort_order.asc" +
            "&order=title.asc" +
            "&limit=5000");

        if (resourceTypeId is Guid targetId && targetId != Guid.Empty)
        {
            queryBuilder.Append("&resource_type_id=eq.");
            queryBuilder.Append(Uri.EscapeDataString(targetId.ToString("D")));
        }

        var uri = new Uri(baseUri, queryBuilder.ToString());
        return await FetchRowsAsync<ResourceDocumentRow>(uri, apiKey, cancellationToken);
    }

    private async Task<bool> SubscriptionTierExistsAsync(
        Uri baseUri,
        string apiKey,
        string tierCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tierCode))
        {
            return false;
        }

        var escapedTierCode = Uri.EscapeDataString(tierCode);
        var uri = new Uri(
            baseUri,
            "rest/v1/subscription_tiers" +
            "?select=tier_code" +
            $"&tier_code=eq.{escapedTierCode}" +
            "&is_active=eq.true" +
            "&limit=1");

        var rows = await FetchRowsAsync<SubscriptionTierLookupRow>(uri, apiKey, cancellationToken);
        return rows.Any(row => !string.IsNullOrWhiteSpace(row.TierCode));
    }

    private async Task<PlaylistRow?> FetchPlaylistByIdAsync(
        Uri baseUri,
        string apiKey,
        Guid playlistId,
        CancellationToken cancellationToken)
    {
        if (playlistId == Guid.Empty)
        {
            return null;
        }

        var escapedPlaylistId = Uri.EscapeDataString(playlistId.ToString("D"));
        var uri = new Uri(
            baseUri,
            "rest/v1/story_playlists" +
            "?select=playlist_id,slug,title,playlist_type,system_key,description,logo_image_path,backdrop_image_path,showcase_image_path,sort_order,max_items,is_enabled,show_on_home,include_in_speellyste_carousel,show_showcase_image_on_luister_page,updated_at" +
            $"&playlist_id=eq.{escapedPlaylistId}" +
            "&limit=1");

        var rows = await FetchRowsAsync<PlaylistRow>(uri, apiKey, cancellationToken);
        return rows
            .Where(row => row.PlaylistId != Guid.Empty)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<T>> FetchRowsAsync<T>(Uri uri, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase fetch failed. uri={Uri} Status={StatusCode} Body={Body}",
                    uri,
                    (int)response.StatusCode,
                    responseBody);
                return Array.Empty<T>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken)
                ?? [];
            return rows;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase fetch failed unexpectedly. uri={Uri}", uri);
            return Array.Empty<T>();
        }
    }

    private static bool HasStoryBeenPublished(StoryRow? story) =>
        story is not null &&
        (story.PublishedAt.HasValue ||
         string.Equals(story.Status, "published", StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeYouTubeUrl(string? value)
    {
        var normalizedValue = NormalizeOptionalText(value, 2048);
        return normalizedValue is null
            ? null
            : YouTubeUrlHelper.BuildWatchUrl(normalizedValue);
    }

    private async Task<bool> TryResolveAdminContextAsync(string? adminEmail, CancellationToken cancellationToken)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        return await IsAdminCoreAsync(baseUri, apiKey, adminEmail, cancellationToken);
    }

    private async Task<bool> IsAdminCoreAsync(Uri baseUri, string apiKey, string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var uri = new Uri(
            baseUri,
            $"rest/v1/admin_users?select=email&email=eq.{escapedEmail}&is_enabled=eq.true&limit=1");

        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Admin lookup failed. email={Email} Status={StatusCode} Body={Body}",
                    normalizedEmail,
                    (int)response.StatusCode,
                    responseBody);
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<AdminUserRow>>(stream, JsonOptions, cancellationToken)
                ?? [];
            return rows.Count > 0;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Admin lookup failed unexpectedly for {Email}.", normalizedEmail);
            return false;
        }
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

    private string ResolveApiKey() => _options.SecretKey;

    private void InvalidateStoryCatalogCache()
    {
        _memoryCache.Remove(StoryCatalogSnapshotCacheKey);
    }

    private void InvalidateResourceCatalogCache()
    {
        _memoryCache.Remove(ResourceCatalogCacheKeys.Catalog);
    }

    private void InvalidateStoreProductCatalogCache()
    {
        _memoryCache.Remove(StoreProductCatalogCacheKeys.Catalog);
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

    private async Task<T?> InvokeRpcAsync<T>(
        Uri baseUri,
        string apiKey,
        string functionName,
        object payload,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(baseUri, $"rest/v1/rpc/{functionName}");

        try
        {
            using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "return=representation");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase RPC failed. function={FunctionName} Status={StatusCode} Body={Body}",
                    functionName,
                    (int)response.StatusCode,
                    responseBody);
                return default;
            }

            var responseBodyText = await response.Content.ReadAsStringAsync(cancellationToken);
            return DeserializeRpcResponse<T>(responseBodyText, functionName);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase RPC failed unexpectedly. function={FunctionName}", functionName);
            return default;
        }
    }

    private static T? DeserializeRpcResponse<T>(string? responseBody, string functionName)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default;
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty(functionName, out var wrappedValue))
            {
                return wrappedValue.Deserialize<T>(JsonOptions);
            }

            return root.Deserialize<T>(JsonOptions);
        }

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var first = root[0];
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty(functionName, out var wrappedValue))
            {
                return wrappedValue.Deserialize<T>(JsonOptions);
            }

            return first.Deserialize<T>(JsonOptions);
        }

        return default;
    }

    private static Dictionary<Guid, string[]> BuildActiveTierMap(IReadOnlyList<SubscriptionRow> subscriptions)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        return subscriptions
            .Where(subscription => subscription.SubscriberId != Guid.Empty)
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.TierCode))
            .Where(subscription => IsActiveSubscription(subscription, nowUtc))
            .GroupBy(subscription => subscription.SubscriberId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(subscription => subscription.TierCode!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tier => tier, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    private static AdminSubscriberReportsSnapshot BuildSubscriberReportsSnapshot(
        WordPressSubscriberReportsRpcSnapshot? wordPressSubscriberReports,
        IReadOnlyList<SubscriberRow> subscribers,
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlyList<RevenueEventRow> revenueEvents,
        IReadOnlySet<string> paystackSubscriptionCreateIdentifiers,
        IReadOnlyList<SubscriptionTierRow> subscriptionTiers,
        IReadOnlyList<SubscriptionRecoveryRow> recoveries,
        IReadOnlyList<AbandonedCartRecoveryRow> abandonedCartRecoveries,
        IReadOnlyList<AuthSessionMetricRow> authSessions,
        IReadOnlyList<StoryViewMetricRow> storyViews,
        IReadOnlyList<StoryListenSessionMetricRow> storyListenSessions)
    {
        var tierDetails = subscriptionTiers
            .Where(tier => !string.IsNullOrWhiteSpace(tier.TierCode))
            .GroupBy(tier => tier.TierCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        return new AdminSubscriberReportsSnapshot(
            MembershipStats: BuildMembershipStatsMetrics(
                subscriptions,
                paystackSubscriptionCreateIdentifiers,
                revenueEvents),
            MembershipTrend: BuildMembershipTrendMetrics(
                subscriptions,
                paystackSubscriptionCreateIdentifiers,
                revenueEvents),
            ActiveMembersPerLevel: BuildTierDistributionMetrics(wordPressSubscriberReports, subscriptions, tierDetails),
            MembershipDetails: BuildSubscriberMembershipDetails(subscribers, subscriptions, tierDetails, paystackSubscriptionCreateIdentifiers),
            SalesAndRevenue: BuildSalesRevenueMetrics(wordPressSubscriberReports, subscriptions, tierDetails, revenueEvents),
            SalesDetails: BuildSalesRevenueDetails(subscriptions, tierDetails, revenueEvents),
            AbandonedCartRecoveries: BuildRecoveryMetrics(subscriptions, tierDetails, recoveries, abandonedCartRecoveries),
            VisitsViewsAndLogins: BuildVisitsViewsLoginsMetrics(authSessions, storyViews, storyListenSessions));
    }

    private static IReadOnlyList<AdminMembershipStatsMetric> BuildMembershipStatsMetrics(
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlySet<string> paystackSubscriptionCreateIdentifiers,
        IReadOnlyList<RevenueEventRow> revenueEvents)
    {
        var currentSubscriberSubscriptions = subscriptions
            .Where(IsSubscriberCountMetricEligible)
            .ToArray();
        var currentNewSubscriberSubscriptions = currentSubscriberSubscriptions
            .Where(subscription => IsNewSubscriberMetricEligible(subscription, paystackSubscriptionCreateIdentifiers))
            .ToArray();
        var cancellationDateBySubscriber = BuildCancellationDateBySubscriber(
            currentSubscriberSubscriptions,
            revenueEvents);

        return
        [
            new AdminMembershipStatsMetric(
                "today",
                CountNewSubscribersByLocalPeriod(currentNewSubscriberSubscriptions, SubscriberPeriod.Today),
                CountCancelledSubscriptionsByLocalPeriod(cancellationDateBySubscriber, SubscriberPeriod.Today)),
            new AdminMembershipStatsMetric(
                "this_month",
                CountNewSubscribersByLocalPeriod(currentNewSubscriberSubscriptions, SubscriberPeriod.ThisMonth),
                CountCancelledSubscriptionsByLocalPeriod(cancellationDateBySubscriber, SubscriberPeriod.ThisMonth)),
            new AdminMembershipStatsMetric(
                "this_year",
                CountNewSubscribersByLocalPeriod(currentNewSubscriberSubscriptions, SubscriberPeriod.ThisYear),
                CountCancelledSubscriptionsByLocalPeriod(cancellationDateBySubscriber, SubscriberPeriod.ThisYear)),
            new AdminMembershipStatsMetric(
                "all_time",
                CountNewSubscribersByLocalPeriod(currentNewSubscriberSubscriptions, SubscriberPeriod.AllTime),
                CountCancelledSubscriptionsByLocalPeriod(cancellationDateBySubscriber, SubscriberPeriod.AllTime))
        ];
    }

    private static IReadOnlyList<AdminSubscriberTrendMetric> BuildMembershipTrendMetrics(
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlySet<string> paystackSubscriptionCreateIdentifiers,
        IReadOnlyList<RevenueEventRow> revenueEvents)
    {
        var eligibleSubscriptions = subscriptions
            .Where(IsSubscriberCountMetricEligible)
            .ToArray();
        var newSubscriberSubscriptions = eligibleSubscriptions
            .Where(subscription => IsNewSubscriberMetricEligible(subscription, paystackSubscriptionCreateIdentifiers))
            .ToArray();
        var cancellationDateBySubscriber = BuildCancellationDateBySubscriber(
            eligibleSubscriptions,
            revenueEvents);

        if (eligibleSubscriptions.Length == 0)
        {
            return Array.Empty<AdminSubscriberTrendMetric>();
        }

        var firstSignupDateBySubscriber = newSubscriberSubscriptions
            .Where(subscription => subscription.SubscriberId != Guid.Empty && subscription.SubscribedAt is not null)
            .GroupBy(subscription => subscription.SubscriberId)
            .Select(group => group
                .Where(subscription => subscription.SubscribedAt is not null)
                .Select(subscription => subscription.SubscribedAt!.Value.ToLocalTime())
                .Min())
            .ToArray();

        var signupCountByDate = firstSignupDateBySubscriber
            .GroupBy(value => value.Date)
            .ToDictionary(group => group.Key, group => group.Count());

        var firstCancellationDateBySubscriber = cancellationDateBySubscriber
            .Select(item => item.Value.ToLocalTime().Date)
            .ToArray();

        var cancellationCountByDate = firstCancellationDateBySubscriber
            .GroupBy(value => value)
            .ToDictionary(group => group.Key, group => group.Count());

        var signupCountByMonth = firstSignupDateBySubscriber
            .GroupBy(value => value.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .ToDictionary(group => group.Key, group => group.Count());

        var cancellationCountByMonth = firstCancellationDateBySubscriber
            .GroupBy(value => value.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .ToDictionary(group => group.Key, group => group.Count());

        var signupCountByYear = firstSignupDateBySubscriber
            .GroupBy(value => value.Year)
            .ToDictionary(group => group.Key, group => group.Count());

        var cancellationCountByYear = firstCancellationDateBySubscriber
            .GroupBy(value => value.Year)
            .ToDictionary(group => group.Key, group => group.Count());

        var nowLocal = DateTime.Now;
        var dayStart = nowLocal.Date.AddDays(-6);
        var monthStart = new DateTime(nowLocal.Year, nowLocal.Month, 1).AddMonths(-11);
        var yearStart = nowLocal.Year - 5;
        var monthEnd = new DateTime(nowLocal.Year, nowLocal.Month, 1);

        return
        [
            ..GetDayTrendSeries(dayStart, nowLocal.Date, signupCountByDate, cancellationCountByDate),
            ..GetMonthTrendSeries(monthStart, monthEnd, signupCountByMonth, cancellationCountByMonth),
            ..GetYearTrendSeries(yearStart, nowLocal.Year, signupCountByYear, cancellationCountByYear)
        ];
    }

    private static IReadOnlyList<AdminSubscriberTrendMetric> GetDayTrendSeries(
        DateTime dayStart,
        DateTime dayEnd,
        IReadOnlyDictionary<DateTime, int> signupsByDate,
        IReadOnlyDictionary<DateTime, int> cancellationsByDate)
    {
        var metrics = new List<AdminSubscriberTrendMetric>(7);

        for (var date = dayStart; date <= dayEnd; date = date.AddDays(1))
        {
            var periodKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            metrics.Add(new AdminSubscriberTrendMetric(
                "day",
                periodKey,
                date.ToString("dd/MM", CultureInfo.InvariantCulture),
                signupsByDate.GetValueOrDefault(date),
                cancellationsByDate.GetValueOrDefault(date)));
        }

        return metrics;
    }

    private static IReadOnlyList<AdminSubscriberTrendMetric> GetMonthTrendSeries(
        DateTime monthStart,
        DateTime monthEnd,
        IReadOnlyDictionary<string, int> signupsByMonth,
        IReadOnlyDictionary<string, int> cancellationsByMonth)
    {
        var metrics = new List<AdminSubscriberTrendMetric>(12);
        for (var month = monthStart; month <= monthEnd; month = month.AddMonths(1))
        {
            var periodKey = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            metrics.Add(new AdminSubscriberTrendMetric(
                "month",
                periodKey,
                month.ToString("MM/yy", CultureInfo.InvariantCulture),
                signupsByMonth.GetValueOrDefault(periodKey),
                cancellationsByMonth.GetValueOrDefault(periodKey)));
        }

        return metrics;
    }

    private static IReadOnlyList<AdminSubscriberTrendMetric> GetYearTrendSeries(
        int yearStart,
        int endYear,
        IReadOnlyDictionary<int, int> signupsByYear,
        IReadOnlyDictionary<int, int> cancellationsByYear)
    {
        var metrics = new List<AdminSubscriberTrendMetric>(Math.Max(0, endYear - yearStart + 1));
        for (var year = yearStart; year <= endYear; year++)
        {
            var periodKey = year.ToString(CultureInfo.InvariantCulture);
            metrics.Add(new AdminSubscriberTrendMetric(
                "year",
                periodKey,
                periodKey,
                signupsByYear.GetValueOrDefault(year),
                cancellationsByYear.GetValueOrDefault(year)));
        }

        return metrics;
    }

    private static IReadOnlyList<AdminTierDistributionMetric> BuildTierDistributionMetrics(
        WordPressSubscriberReportsRpcSnapshot? wordPressSubscriberReports,
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails)
    {
        var metrics = new List<AdminTierDistributionMetric>();

        if (wordPressSubscriberReports is { HasWordPressData: true })
        {
            var wordPressGrouped = (wordPressSubscriberReports.ActiveMembersPerLevel ?? [])
                .Where(metric => !string.IsNullOrWhiteSpace(metric.TierCode))
                .Where(metric => metric.ActiveMembers > 0)
                .Select(metric =>
                {
                    var tierCode = metric.TierCode!.Trim();
                    var tier = tierDetails.TryGetValue(tierCode, out var detail) ? detail : null;
                    return new
                    {
                        TierCode = tierCode,
                        TierName = NormalizeTierDisplayName(tierCode, tier?.DisplayName),
                        ActiveMembers = metric.ActiveMembers
                    };
                })
                .OrderByDescending(item => item.ActiveMembers)
                .ThenBy(item => item.TierName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var wordPressTotalActiveMembers = wordPressGrouped.Sum(item => item.ActiveMembers);
            if (wordPressTotalActiveMembers > 0)
            {
                metrics.AddRange(wordPressGrouped
                    .Select(item => new AdminTierDistributionMetric(
                        "all_time",
                        item.TierCode,
                        item.TierName,
                        item.ActiveMembers,
                        decimal.Round(item.ActiveMembers * 100m / wordPressTotalActiveMembers, 1, MidpointRounding.AwayFromZero))));
            }
        }
        else
        {
            metrics.AddRange(BuildTierDistributionMetricsForPeriod(
                "all_time",
                subscriptions
                    .Where(subscription => !string.IsNullOrWhiteSpace(subscription.TierCode))
                    .Where(subscription => IsActiveSubscription(subscription, DateTimeOffset.UtcNow)),
                tierDetails));
        }

        metrics.AddRange(BuildTierDistributionMetricsForPeriod(
            "today",
            GetFirstSubscriberSignupSubscriptions(subscriptions).Where(subscription => IsInLocalPeriod(subscription.SubscribedAt, SubscriberPeriod.Today)),
            tierDetails));
        metrics.AddRange(BuildTierDistributionMetricsForPeriod(
            "this_month",
            GetFirstSubscriberSignupSubscriptions(subscriptions).Where(subscription => IsInLocalPeriod(subscription.SubscribedAt, SubscriberPeriod.ThisMonth)),
            tierDetails));
        metrics.AddRange(BuildTierDistributionMetricsForPeriod(
            "this_year",
            GetFirstSubscriberSignupSubscriptions(subscriptions).Where(subscription => IsInLocalPeriod(subscription.SubscribedAt, SubscriberPeriod.ThisYear)),
            tierDetails));

        return metrics.ToArray();
    }

    private static IReadOnlyList<AdminTierDistributionMetric> BuildTierDistributionMetricsForPeriod(
        string periodKey,
        IEnumerable<SubscriptionRow> subscriptions,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails)
    {
        var grouped = subscriptions
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.TierCode))
            .GroupBy(subscription => subscription.TierCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var activeMembers = group.Select(subscription => subscription.SubscriberId).Distinct().Count();
                var tier = tierDetails.TryGetValue(group.Key, out var detail) ? detail : null;
                return new
                {
                    TierCode = group.Key,
                    TierName = NormalizeTierDisplayName(group.Key, tier?.DisplayName),
                    ActiveMembers = activeMembers
                };
            })
            .Where(item => item.ActiveMembers > 0)
            .OrderByDescending(item => item.ActiveMembers)
            .ThenBy(item => item.TierName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var totalActiveMembers = grouped.Sum(item => item.ActiveMembers);
        if (totalActiveMembers <= 0)
        {
            return [];
        }

        return grouped
            .Select(item => new AdminTierDistributionMetric(
                periodKey,
                item.TierCode,
                item.TierName,
                item.ActiveMembers,
                decimal.Round(item.ActiveMembers * 100m / totalActiveMembers, 1, MidpointRounding.AwayFromZero)))
            .ToArray();
    }

    private static IReadOnlyList<AdminSubscriberMembershipDetailRecord> BuildSubscriberMembershipDetails(
        IReadOnlyList<SubscriberRow> subscribers,
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails,
        IReadOnlySet<string> paystackSubscriptionCreateIdentifiers)
    {
        var subscribersById = subscribers
            .Where(subscriber => subscriber.SubscriberId != Guid.Empty)
            .GroupBy(subscriber => subscriber.SubscriberId)
            .ToDictionary(group => group.Key, group => group.First());

        return GetFirstSubscriberSignupSubscriptions(subscriptions)
            .Where(subscription => IsNewSubscriberMetricEligible(subscription, paystackSubscriptionCreateIdentifiers))
            .Where(subscription => subscription.SubscribedAt is not null)
            .Select(subscription =>
            {
                subscribersById.TryGetValue(subscription.SubscriberId, out var subscriber);
                var tierCode = NormalizeOptionalText(subscription.TierCode, 80) ?? "-";
                var tier = tierDetails.TryGetValue(tierCode, out var detail) ? detail : null;
                var email = NormalizeOptionalText(subscriber?.Email, 254) ?? "-";
                var displayName = NormalizeOptionalText(subscriber?.DisplayName, 120);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = string.Join(" ", new[]
                    {
                        NormalizeOptionalText(subscriber?.FirstName, 80),
                        NormalizeOptionalText(subscriber?.LastName, 80)
                    }.Where(part => !string.IsNullOrWhiteSpace(part)));
                }

                return new AdminSubscriberMembershipDetailRecord(
                    subscription.SubscriberId,
                    email,
                    string.IsNullOrWhiteSpace(displayName) ? email : displayName,
                    tierCode,
                    NormalizeTierDisplayName(tierCode, tier?.DisplayName),
                    NormalizeOptionalText(subscription.Provider, 40) ?? "-",
                    NormalizeOptionalText(subscription.SourceSystem, 40) ?? "-",
                    NormalizeOptionalText(subscription.Status, 40) ?? "-",
                    subscription.SubscribedAt!.Value,
                    subscription.CancelledAt);
            })
            .OrderByDescending(detail => detail.SubscribedAt)
            .ToArray();
    }

    private static IReadOnlyList<SubscriptionRow> GetFirstSubscriberSignupSubscriptions(IReadOnlyList<SubscriptionRow> subscriptions) =>
        subscriptions
            .Where(IsSubscriberCountMetricEligible)
            .Where(subscription => subscription.SubscribedAt is not null)
            .GroupBy(subscription => subscription.SubscriberId)
            .Select(group => group
                .OrderBy(subscription => subscription.SubscribedAt)
                .First())
            .ToArray();

    private static IReadOnlyList<AdminSalesRevenueMetric> BuildSalesRevenueMetrics(
        WordPressSubscriberReportsRpcSnapshot? wordPressSubscriberReports,
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails,
        IReadOnlyList<RevenueEventRow> revenueEvents)
    {
        _ = wordPressSubscriberReports;
        _ = tierDetails;

        var eligibleSales = BuildEligibleSalesRevenueCandidates(subscriptions, tierDetails, revenueEvents);

        return
        [
            BuildSalesRevenueMetric("today", eligibleSales, SubscriberPeriod.Today),
            BuildSalesRevenueMetric("this_month", eligibleSales, SubscriberPeriod.ThisMonth),
            BuildSalesRevenueMetric("this_year", eligibleSales, SubscriberPeriod.ThisYear),
            BuildSalesRevenueMetric("all_time", eligibleSales, SubscriberPeriod.AllTime)
        ];
    }

    private static IReadOnlyList<AdminSalesRevenueDetailRecord> BuildSalesRevenueDetails(
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails,
        IReadOnlyList<RevenueEventRow> revenueEvents) =>
        BuildEligibleSalesRevenueCandidates(subscriptions, tierDetails, revenueEvents)
            .Where(candidate => candidate.SubscribedAt is not null)
            .OrderByDescending(candidate => candidate.SubscribedAt)
            .Select(candidate => new AdminSalesRevenueDetailRecord(
                candidate.SubscribedAt!.Value,
                decimal.Round(candidate.Price, 2, MidpointRounding.AwayFromZero),
                candidate.TierCode,
                candidate.TierName,
                candidate.Provider,
                candidate.SourceSystem,
                candidate.Reference))
            .ToArray();

    private static IReadOnlyList<SubscriberSaleMetricCandidate> BuildEligibleSalesRevenueCandidates(
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails,
        IReadOnlyList<RevenueEventRow> revenueEvents)
    {
        var paystackRevenueEvents = revenueEvents
            .Where(IsPaystackRevenueEventEligible)
            .Select(BuildRevenueCandidate)
            .Where(metric => metric.Price > 0m && metric.SubscribedAt is not null)
            .ToArray();

        var earliestPaystackEventDate = paystackRevenueEvents
            .Select(metric => metric.SubscribedAt?.ToLocalTime().Date)
            .Where(date => date is not null)
            .Min();

        return subscriptions
            .Where(subscription => IsRevenueMetricEligible(subscription, earliestPaystackEventDate))
            .Select(subscription => BuildSubscriptionRevenueCandidate(subscription, tierDetails))
            .Where(metric => metric.Price > 0m)
            .Concat(paystackRevenueEvents)
            .ToArray();
    }

    private static IReadOnlyList<AdminRecoveryMetric> BuildRecoveryMetrics(
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails,
        IReadOnlyList<SubscriptionRecoveryRow> recoveries,
        IReadOnlyList<AbandonedCartRecoveryRow> abandonedCartRecoveries)
    {
        var subscriptionPrices = subscriptions
            .Where(subscription => subscription.SubscriptionId != Guid.Empty)
            .GroupBy(subscription => subscription.SubscriptionId)
            .ToDictionary(
                group => group.Key,
                group => ResolveTierPrice(group.First().TierCode, tierDetails));

        return
        [
            BuildRecoveryMetric("past_30_days", recoveries, abandonedCartRecoveries, subscriptionPrices, tierDetails, SubscriberPeriod.Past30Days),
            BuildRecoveryMetric("past_12_months", recoveries, abandonedCartRecoveries, subscriptionPrices, tierDetails, SubscriberPeriod.Past12Months),
            BuildRecoveryMetric("all_time", recoveries, abandonedCartRecoveries, subscriptionPrices, tierDetails, SubscriberPeriod.AllTime)
        ];
    }

    private static IReadOnlyList<AdminVisitsViewsLoginsMetric> BuildVisitsViewsLoginsMetrics(
        IReadOnlyList<AuthSessionMetricRow> authSessions,
        IReadOnlyList<StoryViewMetricRow> storyViews,
        IReadOnlyList<StoryListenSessionMetricRow> storyListenSessions)
    {
        var visitStarts = storyListenSessions
            .Where(row => row.SessionId != Guid.Empty)
            .GroupBy(row => row.SessionId)
            .Select(group => group.Min(row => row.OccurredAt))
            .ToArray();

        return
        [
            new AdminVisitsViewsLoginsMetric(
                "today",
                CountByLocalPeriod(visitStarts, SubscriberPeriod.Today),
                CountByLocalPeriod(storyViews.Select(row => row.ViewedAt), SubscriberPeriod.Today),
                CountByLocalPeriod(authSessions.Select(row => row.CreatedAt), SubscriberPeriod.Today)),
            new AdminVisitsViewsLoginsMetric(
                "this_week",
                CountByLocalPeriod(visitStarts, SubscriberPeriod.ThisWeek),
                CountByLocalPeriod(storyViews.Select(row => row.ViewedAt), SubscriberPeriod.ThisWeek),
                CountByLocalPeriod(authSessions.Select(row => row.CreatedAt), SubscriberPeriod.ThisWeek)),
            new AdminVisitsViewsLoginsMetric(
                "this_month",
                CountByLocalPeriod(visitStarts, SubscriberPeriod.ThisMonth),
                CountByLocalPeriod(storyViews.Select(row => row.ViewedAt), SubscriberPeriod.ThisMonth),
                CountByLocalPeriod(authSessions.Select(row => row.CreatedAt), SubscriberPeriod.ThisMonth)),
            new AdminVisitsViewsLoginsMetric(
                "year_to_date",
                CountByLocalPeriod(visitStarts, SubscriberPeriod.YearToDate),
                CountByLocalPeriod(storyViews.Select(row => row.ViewedAt), SubscriberPeriod.YearToDate),
                CountByLocalPeriod(authSessions.Select(row => row.CreatedAt), SubscriberPeriod.YearToDate)),
            new AdminVisitsViewsLoginsMetric(
                "all_time",
                CountByLocalPeriod(visitStarts, SubscriberPeriod.AllTime),
                CountByLocalPeriod(storyViews.Select(row => row.ViewedAt), SubscriberPeriod.AllTime),
                CountByLocalPeriod(authSessions.Select(row => row.CreatedAt), SubscriberPeriod.AllTime))
        ];
    }

    private static AdminSalesRevenueMetric BuildSalesRevenueMetric(
        string periodKey,
        IEnumerable<SubscriberSaleMetricCandidate> eligibleSales,
        SubscriberPeriod period)
    {
        var matchingSales = eligibleSales
            .Where(item => IsInLocalPeriod(item.SubscribedAt, period))
            .ToArray();

        return new AdminSalesRevenueMetric(
            periodKey,
            matchingSales.Length,
            decimal.Round(matchingSales.Sum(item => item.Price), 2, MidpointRounding.AwayFromZero));
    }

    private static SubscriberSaleMetricCandidate BuildRevenueCandidate(RevenueEventRow row)
    {
        var occurredAt = ResolveRevenueEventOccurredAt(row);
        var amountInCents = TryReadNestedDecimal(row.Payload, "data", "amount") ??
                            TryReadNestedDecimal(row.Payload, "data", "plan", "amount") ??
                            TryReadStringAsDecimal(row.Payload, "amount");
        var price = amountInCents is > 0m
            ? decimal.Round(amountInCents.Value / 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var reference = NormalizeOptionalText(
                TryReadNestedString(row.Payload, "data", "reference") ??
                TryReadNestedString(row.Payload, "data", "id") ??
                TryReadString(row.Payload, "reference"),
                160) ??
            "-";
        var tierCode = NormalizeOptionalText(TryReadNestedString(row.Payload, "data", "plan", "plan_code"), 80) ?? "-";
        var tierName = NormalizeOptionalText(TryReadNestedString(row.Payload, "data", "plan", "name"), 120) ?? tierCode;

        return new SubscriberSaleMetricCandidate(
            occurredAt,
            price,
            tierCode,
            tierName,
            NormalizeOptionalText(row.Provider, 40) ?? "paystack",
            "paystack_event",
            reference);
    }

    private static SubscriberSaleMetricCandidate BuildSubscriptionRevenueCandidate(
        SubscriptionRow subscription,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails)
    {
        var tierCode = NormalizeOptionalText(subscription.TierCode, 80) ?? "-";
        var tierName = tierDetails.TryGetValue(tierCode, out var tier)
            ? NormalizeOptionalText(tier.DisplayName, 120) ?? tierCode
            : tierCode;
        var reference = NormalizeOptionalText(subscription.ProviderTransactionId, 160) ??
                        NormalizeOptionalText(subscription.ProviderPaymentId, 160) ??
                        subscription.SubscriptionId.ToString("D");

        return new SubscriberSaleMetricCandidate(
            subscription.SubscribedAt,
            subscription.BillingAmountZar ?? 0m,
            tierCode,
            tierName,
            NormalizeOptionalText(subscription.Provider, 40) ?? "-",
            NormalizeOptionalText(subscription.SourceSystem, 40) ?? "-",
            reference);
    }

    private static DateTimeOffset? ResolveRevenueEventOccurredAt(RevenueEventRow row) =>
        TryParseDateTimeOffset(
            TryReadNestedString(row.Payload, "data", "paid_at") ??
            TryReadNestedString(row.Payload, "data", "paidAt") ??
            TryReadNestedString(row.Payload, "data", "transaction_date") ??
            TryReadString(row.Payload, "paid_at")) ??
        row.ReceivedAt;

    private static AdminRecoveryMetric BuildRecoveryMetric(
        string periodKey,
        IReadOnlyList<SubscriptionRecoveryRow> recoveries,
        IReadOnlyList<AbandonedCartRecoveryRow> abandonedCartRecoveries,
        IReadOnlyDictionary<Guid, decimal> subscriptionPrices,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails,
        SubscriberPeriod period)
    {
        var subscriptionRecoveryAttempts = recoveries.Count(recovery => IsInLocalPeriod(recovery.CreatedAt, period));
        var abandonedCartRecoveryAttempts = abandonedCartRecoveries.Count(recovery => IsInLocalPeriod(recovery.CreatedAt, period));
        var subscriptionRecoveredRows = recoveries
            .Where(recovery => string.Equals(recovery.Resolution, "recovered", StringComparison.OrdinalIgnoreCase))
            .Where(recovery => IsInLocalPeriod(recovery.ResolvedAt, period))
            .ToArray();
        var abandonedCartRecoveredRows = abandonedCartRecoveries
            .Where(recovery => string.Equals(recovery.Resolution, "paid", StringComparison.OrdinalIgnoreCase))
            .Where(recovery => IsInLocalPeriod(recovery.ResolvedAt, period))
            .ToArray();

        var subscriptionRecoveredRevenue = subscriptionRecoveredRows.Sum(recovery =>
            subscriptionPrices.TryGetValue(recovery.SubscriptionId, out var price) ? price : 0m);
        var abandonedCartRecoveredRevenue = abandonedCartRecoveredRows.Sum(recovery =>
            ResolveAbandonedCartRecoveredRevenue(recovery, tierDetails));

        return new AdminRecoveryMetric(
            periodKey,
            decimal.Round(subscriptionRecoveredRevenue + abandonedCartRecoveredRevenue, 2, MidpointRounding.AwayFromZero),
            subscriptionRecoveredRows.Length + abandonedCartRecoveredRows.Length,
            subscriptionRecoveryAttempts + abandonedCartRecoveryAttempts);
    }

    private static decimal ResolveAbandonedCartRecoveredRevenue(
        AbandonedCartRecoveryRow recovery,
        IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails)
    {
        if (recovery.CartTotalZar is > 0m)
        {
            return recovery.CartTotalZar.Value;
        }

        return string.Equals(recovery.SourceType, "subscription", StringComparison.OrdinalIgnoreCase)
            ? ResolveTierPrice(recovery.SourceKey, tierDetails)
            : 0m;
    }

    private async Task<WordPressSubscriberReportsRpcSnapshot?> TrySyncAndFetchWordPressSubscriberReportsAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var syncResult = await _wordPressMigrationService.SyncAsync(cancellationToken);
            if (syncResult.Errors.Count > 0)
            {
                _logger.LogWarning(
                    "WordPress subscriber report sync completed with {ErrorCount} error(s): {Errors}",
                    syncResult.Errors.Count,
                    string.Join(" | ", syncResult.Errors));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "WordPress subscriber report sync fallback failed.");
        }

        return await FetchWordPressSubscriberReportsAsync(baseUri, apiKey, cancellationToken);
    }

    private static AdminMembershipStatsMetric CreateWordPressMembershipStatsMetric(
        IReadOnlyDictionary<string, WordPressMembershipStatsRpcMetric> metrics,
        string periodKey)
    {
        if (!metrics.TryGetValue(periodKey, out var metric))
        {
            return new AdminMembershipStatsMetric(periodKey, 0, 0);
        }

        return new AdminMembershipStatsMetric(periodKey, metric.Signups, metric.Cancellations);
    }

    private static AdminSalesRevenueMetric CreateWordPressSalesRevenueMetric(
        IReadOnlyDictionary<string, WordPressSalesRevenueRpcMetric> metrics,
        string periodKey)
    {
        if (!metrics.TryGetValue(periodKey, out var metric))
        {
            return new AdminSalesRevenueMetric(periodKey, 0, 0m);
        }

        return new AdminSalesRevenueMetric(periodKey, metric.Sales, metric.Revenue);
    }

    private static int CountByLocalPeriod(IEnumerable<DateTimeOffset?> values, SubscriberPeriod period) =>
        values.Count(value => IsInLocalPeriod(value, period));

    private static int CountByLocalPeriod(IEnumerable<DateTimeOffset> values, SubscriberPeriod period) =>
        values.Count(value => IsInLocalPeriod(value, period));

    private static int CountNewSubscribersByLocalPeriod(
        IReadOnlyList<SubscriptionRow> subscriptions,
        SubscriberPeriod period) =>
        subscriptions
            .Where(subscription => subscription.SubscribedAt is not null)
            .GroupBy(subscription => subscription.SubscriberId)
            .Select(group => group
                .Select(subscription => subscription.SubscribedAt)
                .Where(subscribedAt => subscribedAt is not null)
                .Min())
            .Count(subscribedAt => IsInLocalPeriod(subscribedAt, period));

    private static int CountCancelledSubscriptionsByLocalPeriod(
        IReadOnlyDictionary<Guid, DateTimeOffset> cancellationDateBySubscriber,
        SubscriberPeriod period) =>
        cancellationDateBySubscriber
            .Values
            .Count(cancelledAt => IsInLocalPeriod(cancelledAt, period));

    private static Dictionary<Guid, DateTimeOffset> BuildCancellationDateBySubscriber(
        IReadOnlyList<SubscriptionRow> subscriptions,
        IReadOnlyList<RevenueEventRow> revenueEvents)
    {
        var cancellationDateBySubscriber = new Dictionary<Guid, DateTimeOffset>();
        var eligibleSubscriptions = subscriptions
            .Where(IsSubscriberCountMetricEligible)
            .ToArray();
        var subscriptionIdLookup = BuildSubscriptionIdentifierLookup(eligibleSubscriptions);

        foreach (var cancellationEvent in revenueEvents.Where(IsPaystackCancellationEvent))
        {
            foreach (var identifier in GetCancellationEventIdentifiers(cancellationEvent))
            {
                if (!subscriptionIdLookup.TryGetValue(identifier, out var subscriberIds))
                {
                    continue;
                }

                foreach (var subscriberId in subscriberIds)
                {
                    if (!cancellationDateBySubscriber.TryGetValue(subscriberId, out var cancellationDate) ||
                        cancellationEvent.ReceivedAt > cancellationDate)
                    {
                        cancellationDateBySubscriber[subscriberId] = cancellationEvent.ReceivedAt;
                    }
                }
            }
        }

        foreach (var subscription in eligibleSubscriptions)
        {
            if (!IsPaystackOrUnknownPaystackSubscription(subscription))
            {
                continue;
            }

            if (subscription.CancelledAt is null)
            {
                continue;
            }

            if (!cancellationDateBySubscriber.TryGetValue(subscription.SubscriberId, out var cancellationDate) ||
                subscription.CancelledAt.Value > cancellationDate)
            {
                cancellationDateBySubscriber[subscription.SubscriberId] = subscription.CancelledAt.Value;
            }
        }

        return cancellationDateBySubscriber;
    }

    private static bool IsPaystackOrUnknownPaystackSubscription(SubscriptionRow subscription) =>
        string.IsNullOrWhiteSpace(subscription.Provider) ||
        string.Equals(subscription.Provider, "paystack", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, HashSet<Guid>> BuildSubscriptionIdentifierLookup(
        IReadOnlyList<SubscriptionRow> subscriptions)
    {
        var lookup = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var subscription in subscriptions)
        {
            AddSubscriptionIdentifierLookupValue(lookup, subscription.ProviderPaymentId, subscription.SubscriberId);
            AddSubscriptionIdentifierLookupValue(lookup, subscription.ProviderTransactionId, subscription.SubscriberId);
        }

        return lookup;
    }

    private static void AddSubscriptionIdentifierLookupValue(
        Dictionary<string, HashSet<Guid>> lookup,
        string? identifier,
        Guid subscriberId)
    {
        var normalizedIdentifier = NormalizeOptionalText(identifier, 160);
        if (string.IsNullOrWhiteSpace(normalizedIdentifier))
        {
            return;
        }

        if (!lookup.TryGetValue(normalizedIdentifier, out var subscribers))
        {
            subscribers = [];
            lookup[normalizedIdentifier] = subscribers;
        }

        subscribers.Add(subscriberId);
    }

    private static string[] GetCancellationEventIdentifiers(RevenueEventRow cancellationEvent)
    {
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddIfNotWhiteSpace(
            identifiers,
            NormalizeOptionalText(cancellationEvent.ProviderPaymentId, 160));
        AddIfNotWhiteSpace(
            identifiers,
            NormalizeOptionalText(cancellationEvent.ProviderTransactionId, 160));
        AddIfNotWhiteSpace(
            identifiers,
            NormalizeOptionalText(TryReadNestedString(cancellationEvent.Payload, "data", "subscription", "subscription_code"), 160));
        AddIfNotWhiteSpace(
            identifiers,
            NormalizeOptionalText(TryReadNestedString(cancellationEvent.Payload, "data", "subscription_code"), 160));
        AddIfNotWhiteSpace(
            identifiers,
            NormalizeOptionalText(TryReadNestedString(cancellationEvent.Payload, "data", "id"), 160));
        AddIfNotWhiteSpace(
            identifiers,
            NormalizeOptionalText(TryReadString(cancellationEvent.Payload, "reference"), 160));

        return [.. identifiers];
    }

    private static bool IsPaystackCancellationEvent(RevenueEventRow row) =>
        string.Equals(row.Provider, "paystack", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(row.EventType, "subscription.disable", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(row.EventStatus, "cancelled", StringComparison.OrdinalIgnoreCase));

    private static bool IsInLocalPeriod(DateTimeOffset? value, SubscriberPeriod period)
    {
        if (value is null)
        {
            return false;
        }

        if (period == SubscriberPeriod.AllTime)
        {
            return true;
        }

        var localValue = value.Value.ToLocalTime().DateTime;
        var nowLocal = DateTime.Now;
        var start = period switch
        {
            SubscriberPeriod.Today => nowLocal.Date,
            SubscriberPeriod.ThisWeek => GetStartOfWeek(nowLocal.Date),
            SubscriberPeriod.ThisMonth => new DateTime(nowLocal.Year, nowLocal.Month, 1),
            SubscriberPeriod.ThisYear or SubscriberPeriod.YearToDate => new DateTime(nowLocal.Year, 1, 1),
            SubscriberPeriod.Past30Days => nowLocal.Date.AddDays(-29),
            SubscriberPeriod.Past12Months => nowLocal.Date.AddMonths(-12).AddDays(1),
            _ => DateTime.MinValue
        };

        return localValue >= start && localValue <= nowLocal;
    }

    private static DateTime GetStartOfWeek(DateTime value)
    {
        var dayOfWeek = value.DayOfWeek;
        var daysSinceMonday = ((int)dayOfWeek + 6) % 7;
        return value.AddDays(-daysSinceMonday);
    }

    private static bool IsSubscriberCountMetricEligible(SubscriptionRow subscription) =>
        subscription.SubscriberId != Guid.Empty &&
        !string.Equals(subscription.Status, "failed", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subscription.SourceSystem, "wordpress_pmpro", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subscription.SourceSystem, "admin_override", StringComparison.OrdinalIgnoreCase) &&
        !IsLegacyImportedGratisSubscription(subscription) &&
        subscription.SubscribedAt is not null;

    private static bool IsNewSubscriberMetricEligible(
        SubscriptionRow subscription,
        IReadOnlySet<string> paystackSubscriptionCreateIdentifiers)
    {
        if (!IsSubscriberCountMetricEligible(subscription))
        {
            return false;
        }

        if (paystackSubscriptionCreateIdentifiers.Count == 0)
        {
            return false;
        }

        if (string.Equals(subscription.SourceSystem, "payfast", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(subscription.Provider, "payfast", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(subscription.Provider) &&
            !string.Equals(subscription.Provider, "paystack", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!SubscriptionHasPaystackCreateSignature(subscription, paystackSubscriptionCreateIdentifiers))
        {
            return false;
        }

        return true;
    }

    private static bool SubscriptionHasPaystackCreateSignature(
        SubscriptionRow subscription,
        IReadOnlySet<string> paystackSubscriptionCreateIdentifiers)
    {
        var providerPaymentId = NormalizeOptionalText(subscription.ProviderPaymentId, 160);
        if (!string.IsNullOrWhiteSpace(providerPaymentId) &&
            paystackSubscriptionCreateIdentifiers.Contains(providerPaymentId))
        {
            return true;
        }

        var providerTransactionId = NormalizeOptionalText(subscription.ProviderTransactionId, 160);
        return !string.IsNullOrWhiteSpace(providerTransactionId) &&
               paystackSubscriptionCreateIdentifiers.Contains(providerTransactionId);
    }

    private static IReadOnlySet<string> BuildPaystackSubscriptionCreateIdentifiers(
        IReadOnlyList<RevenueEventRow> revenueEvents)
    {
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in revenueEvents.Where(IsPaystackSubscriptionCreateEvent))
        {
            AddIfNotWhiteSpace(identifiers, NormalizeOptionalText(row.ProviderPaymentId, 160));
            AddIfNotWhiteSpace(identifiers, NormalizeOptionalText(row.ProviderTransactionId, 160));
            AddIfNotWhiteSpace(
                identifiers,
                NormalizeOptionalText(TryReadNestedString(row.Payload, "data", "subscription", "subscription_code"), 160));
            AddIfNotWhiteSpace(
                identifiers,
                NormalizeOptionalText(TryReadNestedString(row.Payload, "data", "subscription_code"), 160));
            AddIfNotWhiteSpace(
                identifiers,
                NormalizeOptionalText(TryReadNestedString(row.Payload, "data", "id"), 160));
            AddIfNotWhiteSpace(identifiers, NormalizeOptionalText(TryReadString(row.Payload, "reference"), 160));
        }

        return identifiers;
    }

    private static bool IsPaystackSubscriptionCreateEvent(RevenueEventRow row) =>
        string.Equals(row.Provider, "paystack", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(row.EventType, "subscription.create", StringComparison.OrdinalIgnoreCase) &&
        (IsSuccessfulRevenueEventStatus(row.EventStatus) ||
         string.Equals(row.EventStatus, "active", StringComparison.OrdinalIgnoreCase));

    private static void AddIfNotWhiteSpace(HashSet<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    private static bool IsLegacyImportedGratisSubscription(SubscriptionRow subscription) =>
        string.Equals(subscription.SourceSystem, "shink_app", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(subscription.TierCode, "gratis", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(subscription.ProviderPaymentId) &&
        subscription.ProviderPaymentId!.StartsWith("gratis-", StringComparison.OrdinalIgnoreCase);

    private static bool IsRevenueMetricEligible(SubscriptionRow subscription, DateTime? earliestPaystackEventDate = null) =>
        subscription.SubscribedAt is not null &&
        !string.Equals(subscription.Status, "failed", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subscription.SourceSystem, "wordpress_pmpro", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subscription.SourceSystem, "admin_override", StringComparison.OrdinalIgnoreCase) &&
        !ShouldUsePaystackRevenueEventSource(subscription, earliestPaystackEventDate) &&
        (string.Equals(subscription.Status, "active", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(subscription.Status, "cancelled", StringComparison.OrdinalIgnoreCase));

    private static bool IsPaystackRevenueEventEligible(RevenueEventRow row) =>
        string.Equals(row.Provider, "paystack", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(row.EventType, "charge.success", StringComparison.OrdinalIgnoreCase) &&
        IsSuccessfulRevenueEventStatus(row.EventStatus);

    private static bool IsSuccessfulRevenueEventStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "successful", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldUsePaystackRevenueEventSource(SubscriptionRow subscription, DateTime? earliestPaystackEventDate)
    {
        if (!string.Equals(subscription.Provider, "paystack", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (subscription.SubscribedAt is null)
        {
            return true;
        }

        return earliestPaystackEventDate is null ||
               subscription.SubscribedAt.Value.ToLocalTime().Date >= earliestPaystackEventDate.Value;
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
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
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
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
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

    private static DateTimeOffset? TryParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static decimal ResolveTierPrice(string? tierCode, IReadOnlyDictionary<string, SubscriptionTierRow> tierDetails)
    {
        if (string.IsNullOrWhiteSpace(tierCode))
        {
            return 0m;
        }

        return tierDetails.TryGetValue(tierCode.Trim(), out var tier)
            ? Math.Max(0m, tier.PriceZar)
            : 0m;
    }

    private static string NormalizeTierDisplayName(string tierCode, string? displayName)
    {
        var normalized = NormalizeOptionalText(displayName, 120);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        return tierCode.Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal);
    }

    private static Dictionary<Guid, SubscriptionSummary> BuildSubscriptionSummaryMap(IReadOnlyList<SubscriptionRow> subscriptions)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        return subscriptions
            .Where(subscription => subscription.SubscriberId != Guid.Empty)
            .GroupBy(subscription => subscription.SubscriberId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var selected = group
                        .OrderByDescending(subscription => GetSubscriptionPriority(subscription, nowUtc))
                        .ThenByDescending(subscription => subscription.SubscribedAt ?? DateTimeOffset.MinValue)
                        .First();

                    return new SubscriptionSummary(
                        NormalizeOptionalText(selected.Provider, 40),
                        NormalizeOptionalText(selected.SourceSystem, 40),
                        NormalizeOptionalText(selected.Status, 40),
                        selected.SubscribedAt,
                        selected.NextRenewalAt,
                        selected.CancelledAt);
                });
    }

    private static AdminSubscriberRecord MapSubscriberRecord(SubscriberRow row, SubscriptionSummary? summary)
    {
        return new AdminSubscriberRecord(
            SubscriberId: row.SubscriberId,
            Email: row.Email.Trim().ToLowerInvariant(),
            FirstName: NormalizeOptionalText(row.FirstName, 80),
            LastName: NormalizeOptionalText(row.LastName, 80),
            DisplayName: NormalizeOptionalText(row.DisplayName, 120),
            MobileNumber: NormalizeOptionalText(row.MobileNumber, 32),
            ProfileImageUrl: NormalizeOptionalText(row.ProfileImageUrl, 2048),
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt,
            ActiveTierCodes: Array.Empty<string>(),
            PaymentProvider: summary?.PaymentProvider,
            SubscriptionSourceSystem: summary?.SourceSystem,
            SubscriptionStatus: row.DisabledAt is not null ? "disabled" : summary?.Status,
            SubscribedAt: summary?.SubscribedAt,
            NextPaymentDueAt: summary?.NextRenewalAt,
            CancelledAt: summary?.CancelledAt,
            DisabledAt: row.DisabledAt,
            DisabledByAdminEmail: NormalizeOptionalText(row.DisabledByAdminEmail, 320),
            DisabledReason: NormalizeOptionalText(row.DisabledReason, 400));
    }

    private static AdminSubscriberSubscriptionRecord MapSubscriberSubscription(
        SubscriptionRow row,
        IReadOnlyDictionary<string, string> tierNames)
    {
        var tierCode = NormalizeOptionalText(row.TierCode, 80) ?? string.Empty;
        var sourceSystem = NormalizeOptionalText(row.SourceSystem, 40) ?? "shink_app";
        var isAdminOverride = string.Equals(sourceSystem, "admin_override", StringComparison.OrdinalIgnoreCase);
        return new AdminSubscriberSubscriptionRecord(
            row.SubscriptionId,
            tierCode,
            tierNames.GetValueOrDefault(tierCode, tierCode),
            NormalizeOptionalText(row.Provider, 40) ?? string.Empty,
            sourceSystem,
            NormalizeOptionalText(row.Status, 40) ?? string.Empty,
            row.SubscribedAt,
            row.NextRenewalAt,
            row.CancelledAt,
            NormalizeOptionalText(row.ProviderPaymentId, 160),
            NormalizeOptionalText(row.ProviderTransactionId, 160),
            !string.IsNullOrWhiteSpace(row.ProviderToken),
            isAdminOverride,
            !isAdminOverride);
    }

    private static int GetSubscriptionPriority(SubscriptionRow subscription, DateTimeOffset nowUtc)
    {
        if (IsActiveSubscription(subscription, nowUtc))
        {
            return 3;
        }

        if (string.Equals(subscription.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(subscription.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static bool IsActiveSubscription(SubscriptionRow subscription, DateTimeOffset nowUtc)
    {
        if (!string.Equals(subscription.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (subscription.CancelledAt is not null && subscription.CancelledAt <= nowUtc)
        {
            return false;
        }

        if (subscription.NextRenewalAt is not null && subscription.NextRenewalAt < nowUtc)
        {
            return false;
        }

        return true;
    }

    private static bool IsAdminCancellablePaidSubscription(SubscriptionRow subscription, DateTimeOffset nowUtc)
    {
        if (!IsActiveSubscription(subscription, nowUtc))
        {
            return false;
        }

        if (string.Equals(subscription.SourceSystem, "admin_override", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(subscription.TierCode, "gratis", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (PaystackSubscriptionCodeResolver.ResolveSubscriptionCode(
                subscription.Provider,
                subscription.SourceSystem,
                subscription.ProviderPaymentId,
                subscription.ProviderTransactionId) is not null)
        {
            return true;
        }

        return string.Equals(subscription.Provider, "payfast", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(subscription.ProviderToken);
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string? NormalizeEmail(string? value)
    {
        var normalized = NormalizeOptionalText(value, 320)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Count(character => character == '@') != 1 ||
            normalized.StartsWith('@') ||
            normalized.EndsWith('@'))
        {
            return null;
        }

        return normalized;
    }

    private static string NormalizePlaylistImagePath(string? value) =>
        NormalizeOptionalText(value, 1024) ?? string.Empty;

    private static string NormalizeStoreProductImagePath(string? value)
    {
        var normalized = NormalizeOptionalText(value, 1024) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            if (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return absoluteUri.ToString();
            }

            return string.Empty;
        }

        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            normalized = $"/{normalized[2..]}";
        }

        normalized = normalized.Replace('\\', '/');
        return normalized.StartsWith("/", StringComparison.Ordinal)
            ? normalized
            : $"/{normalized.TrimStart('/')}";
    }

    private static bool IsSystemPlaylistType(string? playlistType) =>
        string.Equals(playlistType?.Trim(), "system", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeSystemKey(string? systemKey)
    {
        var normalized = NormalizeOptionalText(systemKey, 80);
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToLowerInvariant();
    }

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
        if (!MobileNumberRegex().IsMatch(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static string? NormalizeSearchTerm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? NormalizeSubscriberFilterToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeSubscriberSortLabel(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "subscriber" => "subscriber",
            "mobile" => "mobile",
            "tiers" => "tiers",
            "source" => "source",
            "provider" => "provider",
            "status" => "status",
            "subscribed_at" => "subscribed_at",
            "next_payment" => "next_payment",
            _ => "subscriber"
        };

    private static bool MatchesSubscriberSearch(SubscriberRow row, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        return ContainsIgnoreCase(row.Email, searchTerm) ||
               ContainsIgnoreCase(row.FirstName, searchTerm) ||
               ContainsIgnoreCase(row.LastName, searchTerm) ||
               ContainsIgnoreCase(row.DisplayName, searchTerm) ||
               ContainsIgnoreCase(row.MobileNumber, searchTerm);
    }

    private static bool MatchesStorySearch(StoryRow row, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        return ContainsIgnoreCase(row.Title, searchTerm) ||
               ContainsIgnoreCase(row.Slug, searchTerm) ||
               ContainsIgnoreCase(row.Summary, searchTerm) ||
               ContainsIgnoreCase(row.Description, searchTerm);
    }

    private static bool ContainsIgnoreCase(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static Guid? TryReadFirstGuidProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var first = document.RootElement[0];
            if (first.ValueKind != JsonValueKind.Object ||
                !first.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(value.GetString(), out var parsedGuid))
            {
                return parsedGuid;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool ContainsDuplicateSlugViolation(string? responseBody) =>
        !string.IsNullOrWhiteSpace(responseBody) &&
        responseBody.Contains("stories_slug_key", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsDuplicateResourceTypeSlugViolation(string? responseBody) =>
        !string.IsNullOrWhiteSpace(responseBody) &&
        responseBody.Contains("resource_types_slug_key", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsDuplicateResourceDocumentSlugViolation(string? responseBody) =>
        !string.IsNullOrWhiteSpace(responseBody) &&
        responseBody.Contains("resource_documents_resource_type_id_slug_key", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StorySlugRegex();

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TierCodeRegex();

    [GeneratedRegex("^\\+?[0-9]{7,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex MobileNumberRegex();

    private sealed record AdminOperationContext(Uri BaseUri, string ApiKey, string AdminEmail);

    private sealed class AdminUserRow
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    private sealed class AdminSubscriberPageRpcResponse
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("items")]
        public List<AdminSubscriberPageItemRpc>? Items { get; set; }
    }

    private sealed class AdminSubscriberPageItemRpc
    {
        [JsonPropertyName("subscriber_id")]
        public Guid SubscriberId { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

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

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("active_tier_codes")]
        public List<string>? ActiveTierCodes { get; set; }

        [JsonPropertyName("payment_provider")]
        public string? PaymentProvider { get; set; }

        [JsonPropertyName("subscription_source_system")]
        public string? SubscriptionSourceSystem { get; set; }

        [JsonPropertyName("subscription_status")]
        public string? SubscriptionStatus { get; set; }

        [JsonPropertyName("subscribed_at")]
        public DateTimeOffset? SubscribedAt { get; set; }

        [JsonPropertyName("next_payment_due_at")]
        public DateTimeOffset? NextPaymentDueAt { get; set; }

        [JsonPropertyName("cancelled_at")]
        public DateTimeOffset? CancelledAt { get; set; }

        [JsonPropertyName("disabled_at")]
        public DateTimeOffset? DisabledAt { get; set; }

        [JsonPropertyName("disabled_by_admin_email")]
        public string? DisabledByAdminEmail { get; set; }

        [JsonPropertyName("disabled_reason")]
        public string? DisabledReason { get; set; }
    }

    private sealed class SubscriberRow
    {
        [JsonPropertyName("subscriber_id")]
        public Guid SubscriberId { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

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

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("disabled_at")]
        public DateTimeOffset? DisabledAt { get; set; }

        [JsonPropertyName("disabled_by_admin_email")]
        public string? DisabledByAdminEmail { get; set; }

        [JsonPropertyName("disabled_reason")]
        public string? DisabledReason { get; set; }
    }

    private sealed class SubscriberDisabledStateRow
    {
        [JsonPropertyName("subscriber_id")]
        public Guid SubscriberId { get; set; }

        [JsonPropertyName("disabled_at")]
        public DateTimeOffset? DisabledAt { get; set; }

        [JsonPropertyName("disabled_by_admin_email")]
        public string? DisabledByAdminEmail { get; set; }

        [JsonPropertyName("disabled_reason")]
        public string? DisabledReason { get; set; }
    }

    private sealed class SubscriptionRow
    {
        [JsonPropertyName("subscription_id")]
        public Guid SubscriptionId { get; set; }

        [JsonPropertyName("subscriber_id")]
        public Guid SubscriberId { get; set; }

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("source_system")]
        public string? SourceSystem { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("subscribed_at")]
        public DateTimeOffset? SubscribedAt { get; set; }

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }

        [JsonPropertyName("cancelled_at")]
        public DateTimeOffset? CancelledAt { get; set; }

        [JsonPropertyName("billing_amount_zar")]
        public decimal? BillingAmountZar { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("provider_token")]
        public string? ProviderToken { get; set; }

        [JsonPropertyName("provider_email_token")]
        public string? ProviderEmailToken { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }
    }

    private sealed class SubscriptionTierRow
    {
        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("price_zar")]
        public decimal PriceZar { get; set; }

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; }
    }

    private sealed class WordPressSubscriberReportsRpcSnapshot
    {
        [JsonPropertyName("has_wordpress_data")]
        public bool HasWordPressData { get; set; }

        [JsonPropertyName("membership_stats")]
        public List<WordPressMembershipStatsRpcMetric>? MembershipStats { get; set; }

        [JsonPropertyName("active_members_per_level")]
        public List<WordPressActiveMembersPerLevelRpcMetric>? ActiveMembersPerLevel { get; set; }

        [JsonPropertyName("sales_and_revenue")]
        public List<WordPressSalesRevenueRpcMetric>? SalesAndRevenue { get; set; }
    }

    private sealed class WordPressMembershipStatsRpcMetric
    {
        [JsonPropertyName("period_key")]
        public string? PeriodKey { get; set; }

        [JsonPropertyName("signups")]
        public int Signups { get; set; }

        [JsonPropertyName("cancellations")]
        public int Cancellations { get; set; }
    }

    private sealed class WordPressActiveMembersPerLevelRpcMetric
    {
        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("active_members")]
        public int ActiveMembers { get; set; }
    }

    private sealed class WordPressSalesRevenueRpcMetric
    {
        [JsonPropertyName("period_key")]
        public string? PeriodKey { get; set; }

        [JsonPropertyName("sales")]
        public int Sales { get; set; }

        [JsonPropertyName("revenue")]
        public decimal Revenue { get; set; }
    }

    private sealed class RevenueEventRow
    {
        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("event_type")]
        public string? EventType { get; set; }

        [JsonPropertyName("event_status")]
        public string? EventStatus { get; set; }

        [JsonPropertyName("received_at")]
        public DateTimeOffset ReceivedAt { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("payload")]
        public JsonElement Payload { get; set; }
    }

    private sealed class SubscriptionRecoveryRow
    {
        [JsonPropertyName("subscription_id")]
        public Guid SubscriptionId { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("resolved_at")]
        public DateTimeOffset? ResolvedAt { get; set; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; set; }
    }

    private sealed class SubscriptionRecoveryDetailRow
    {
        [JsonPropertyName("recovery_id")]
        public Guid RecoveryId { get; set; }

        [JsonPropertyName("subscription_id")]
        public Guid SubscriptionId { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("resolved_at")]
        public DateTimeOffset? ResolvedAt { get; set; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; set; }
    }

    private sealed record SubscriptionRecoveryAction(
        string Url,
        string ActionLabel,
        string Context,
        string ActionKey,
        string PlanName);

    private sealed class SubscriptionEventDetailRow
    {
        [JsonPropertyName("received_at")]
        public DateTimeOffset ReceivedAt { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("event_type")]
        public string? EventType { get; set; }

        [JsonPropertyName("event_status")]
        public string? EventStatus { get; set; }

        [JsonPropertyName("provider_payment_id")]
        public string? ProviderPaymentId { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }
    }

    private sealed class StoreOrderDetailRow
    {
        [JsonPropertyName("order_id")]
        public Guid OrderId { get; set; }

        [JsonPropertyName("order_reference")]
        public string? OrderReference { get; set; }

        [JsonPropertyName("product_name")]
        public string? ProductName { get; set; }

        [JsonPropertyName("total_price_zar")]
        public decimal TotalPriceZar { get; set; }

        [JsonPropertyName("payment_status")]
        public string? PaymentStatus { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("provider_transaction_id")]
        public string? ProviderTransactionId { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("paid_at")]
        public DateTimeOffset? PaidAt { get; set; }
    }

    private sealed class AuthSessionDetailRow
    {
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonPropertyName("revoked_at")]
        public DateTimeOffset? RevokedAt { get; set; }
    }

    private sealed class StoryViewDetailRow
    {
        [JsonPropertyName("story_slug")]
        public string? StorySlug { get; set; }

        [JsonPropertyName("story_path")]
        public string? StoryPath { get; set; }

        [JsonPropertyName("viewed_at")]
        public DateTimeOffset ViewedAt { get; set; }
    }

    private sealed class StoryListenDetailRow
    {
        [JsonPropertyName("story_slug")]
        public string? StorySlug { get; set; }

        [JsonPropertyName("event_type")]
        public string? EventType { get; set; }

        [JsonPropertyName("listened_seconds")]
        public decimal ListenedSeconds { get; set; }

        [JsonPropertyName("occurred_at")]
        public DateTimeOffset OccurredAt { get; set; }
    }

    private sealed class StoryFavoriteDetailRow
    {
        [JsonPropertyName("story_slug")]
        public string? StorySlug { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class SubscriberNotificationDetailRow
    {
        [JsonPropertyName("notification_id")]
        public Guid NotificationId { get; set; }

        [JsonPropertyName("notification_type")]
        public string? NotificationType { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("read_at")]
        public DateTimeOffset? ReadAt { get; set; }

        [JsonPropertyName("cleared_at")]
        public DateTimeOffset? ClearedAt { get; set; }
    }

    private sealed class AbandonedCartRecoveryDetailRow
    {
        [JsonPropertyName("recovery_id")]
        public Guid RecoveryId { get; set; }

        [JsonPropertyName("source_type")]
        public string? SourceType { get; set; }

        [JsonPropertyName("source_key")]
        public string? SourceKey { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("resolved_at")]
        public DateTimeOffset? ResolvedAt { get; set; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; set; }
    }

    private sealed class SubscriberAdminAuditRow
    {
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("admin_email")]
        public string? AdminEmail { get; set; }

        [JsonPropertyName("action_key")]
        public string? ActionKey { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }

    private sealed class AbandonedCartRecoveryRow
    {
        [JsonPropertyName("source_type")]
        public string? SourceType { get; set; }

        [JsonPropertyName("source_key")]
        public string? SourceKey { get; set; }

        [JsonPropertyName("cart_total_zar")]
        public decimal? CartTotalZar { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("resolved_at")]
        public DateTimeOffset? ResolvedAt { get; set; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; set; }
    }

    private sealed class AuthSessionMetricRow
    {
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class StoryViewMetricRow
    {
        [JsonPropertyName("viewed_at")]
        public DateTimeOffset ViewedAt { get; set; }
    }

    private sealed class StoryListenSessionMetricRow
    {
        [JsonPropertyName("session_id")]
        public Guid SessionId { get; set; }

        [JsonPropertyName("occurred_at")]
        public DateTimeOffset OccurredAt { get; set; }
    }

    private sealed record SubscriptionSummary(
        string? PaymentProvider,
        string? SourceSystem,
        string? Status,
        DateTimeOffset? SubscribedAt,
        DateTimeOffset? NextRenewalAt,
        DateTimeOffset? CancelledAt);

    private sealed record SubscriberSaleMetricCandidate(
        DateTimeOffset? SubscribedAt,
        decimal Price,
        string TierCode,
        string TierName,
        string Provider,
        string SourceSystem,
        string Reference);

    private sealed class SubscriptionTierLookupRow
    {
        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }
    }

    private sealed class StoryRow
    {
        [JsonPropertyName("story_id")]
        public Guid StoryId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("youtube_url")]
        public string? YouTubeUrl { get; set; }

        [JsonPropertyName("cover_image_path")]
        public string? CoverImagePath { get; set; }

        [JsonPropertyName("thumbnail_image_path")]
        public string? ThumbnailImagePath { get; set; }

        [JsonPropertyName("audio_provider")]
        public string? AudioProvider { get; set; }

        [JsonPropertyName("audio_bucket")]
        public string? AudioBucket { get; set; }

        [JsonPropertyName("audio_object_key")]
        public string? AudioObjectKey { get; set; }

        [JsonPropertyName("audio_content_type")]
        public string? AudioContentType { get; set; }

        [JsonPropertyName("access_level")]
        public string? AccessLevel { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("duration_seconds")]
        public int? DurationSeconds { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class PlaylistRow
    {
        [JsonPropertyName("playlist_id")]
        public Guid PlaylistId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("playlist_type")]
        public string? PlaylistType { get; set; }

        [JsonPropertyName("system_key")]
        public string? SystemKey { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("logo_image_path")]
        public string? LogoImagePath { get; set; }

        [JsonPropertyName("backdrop_image_path")]
        public string? BackdropImagePath { get; set; }

        [JsonPropertyName("showcase_image_path")]
        public string? ShowcaseImagePath { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("max_items")]
        public int? MaxItems { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("show_on_home")]
        public bool ShowOnHome { get; set; }

        [JsonPropertyName("include_in_speellyste_carousel")]
        public bool IncludeInSpeellysteCarousel { get; set; }

        [JsonPropertyName("show_showcase_image_on_luister_page")]
        public bool ShowShowcaseImageOnLuisterPage { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class PlaylistItemRow
    {
        [JsonPropertyName("playlist_id")]
        public Guid PlaylistId { get; set; }

        [JsonPropertyName("story_id")]
        public Guid StoryId { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("is_showcase")]
        public bool IsShowcase { get; set; }
    }

    private sealed class StoryLookupRow
    {
        [JsonPropertyName("story_id")]
        public Guid StoryId { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    private sealed class ResourceTypeRow
    {
        [JsonPropertyName("resource_type_id")]
        public Guid ResourceTypeId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class StoreProductRow
    {
        [JsonPropertyName("store_product_id")]
        public Guid StoreProductId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("image_path")]
        public string? ImagePath { get; set; }

        [JsonPropertyName("alt_text")]
        public string? AltText { get; set; }

        [JsonPropertyName("theme_class")]
        public string? ThemeClass { get; set; }

        [JsonPropertyName("unit_price_zar")]
        public decimal UnitPriceZar { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class ResourceDocumentRow
    {
        [JsonPropertyName("resource_document_id")]
        public Guid ResourceDocumentId { get; set; }

        [JsonPropertyName("resource_type_id")]
        public Guid ResourceTypeId { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("storage_provider")]
        public string? StorageProvider { get; set; }

        [JsonPropertyName("storage_bucket")]
        public string? StorageBucket { get; set; }

        [JsonPropertyName("storage_object_key")]
        public string? StorageObjectKey { get; set; }

        [JsonPropertyName("preview_image_content_type")]
        public string? PreviewImageContentType { get; set; }

        [JsonPropertyName("preview_image_bucket")]
        public string? PreviewImageBucket { get; set; }

        [JsonPropertyName("preview_image_object_key")]
        public string? PreviewImageObjectKey { get; set; }

        [JsonPropertyName("required_tier_code")]
        public string? RequiredTierCode { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("document_updated_at")]
        public DateTimeOffset? DocumentUpdatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private enum SubscriberPeriod
    {
        Today,
        ThisWeek,
        ThisMonth,
        ThisYear,
        YearToDate,
        Past30Days,
        Past12Months,
        AllTime
    }
}
