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
    ILogger<SupabaseSubscriptionLedgerService> logger) : ISubscriptionLedgerService
{
    private const string GratisTierCode = "gratis";
    private const string GratisProvider = "paystack";
    private const string GratisPlanSlug = "gratis";

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
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
            $"rest/v1/subscribers?select=email,first_name,last_name,display_name,mobile_number&email=eq.{escapedEmail}&limit=1");

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
                return new SubscriberProfile(normalizedEmail, null, null, null, null);
            }

            return new SubscriberProfile(
                Email: string.IsNullOrWhiteSpace(profile.Email) ? normalizedEmail : profile.Email.Trim().ToLowerInvariant(),
                FirstName: NormalizeOptionalText(profile.FirstName, 80),
                LastName: NormalizeOptionalText(profile.LastName, 80),
                DisplayName: NormalizeOptionalText(profile.DisplayName, 120),
                MobileNumber: NormalizeOptionalText(profile.MobileNumber, 32));
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
                cancellationToken);
            return !string.IsNullOrWhiteSpace(subscriberId);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase subscriber upsert failed unexpectedly.");
            return false;
        }
    }

    public async Task<bool> EnsureGratisAccessAsync(
        string? email,
        string? firstName,
        string? lastName,
        string? displayName,
        string? mobileNumber,
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

    private async Task<bool> HasActiveSubscriptionAsync(
        string? email,
        string? tierCode,
        bool excludeGratisTier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var normalizedTierCode = string.IsNullOrWhiteSpace(tierCode)
            ? null
            : tierCode.Trim();

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase subscription lookup skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase subscription lookup skipped: ServiceRoleKey is not configured.");
            return false;
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
                return false;
            }

            var subscriberResponseBody = await subscriberResponse.Content.ReadAsStringAsync(cancellationToken);
            var subscriberId = ReadFirstStringProperty(subscriberResponseBody, "subscriber_id");
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return false;
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
                return false;
            }

            await using var subscriptionsStream = await subscriptionsResponse.Content.ReadAsStreamAsync(cancellationToken);
            var subscriptions = await JsonSerializer.DeserializeAsync<List<SubscriptionStatusRow>>(subscriptionsStream, cancellationToken: cancellationToken)
                ?? [];

            var nowUtc = DateTimeOffset.UtcNow;
            return subscriptions.Any(IsActiveSubscriptionRow);

            bool IsActiveSubscriptionRow(SubscriptionStatusRow row)
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
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase subscription lookup failed unexpectedly.");
            return false;
        }
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

        string? subscriptionId = null;
        if (string.Equals(paymentStatus, "COMPLETE", StringComparison.OrdinalIgnoreCase))
        {
            var upsertResult = await UpsertActivePayFastSubscriptionAsync(baseUri, apiKey, formCollection, nowUtc, cancellationToken);
            if (!upsertResult.IsSuccess)
            {
                return upsertResult;
            }

            subscriptionId = upsertResult.SubscriptionId;
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

        if (!eventInserted)
        {
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

            string? subscriptionId = null;
            if (ShouldActivatePaystackSubscription(eventType, eventStatus))
            {
                var upsertResult = await UpsertActivePaystackSubscriptionAsync(baseUri, apiKey, data, nowUtc, cancellationToken);
                if (!upsertResult.IsSuccess)
                {
                    return new SubscriptionPersistResult(false, upsertResult.ErrorMessage, upsertResult.SubscriptionId);
                }

                subscriptionId = upsertResult.SubscriptionId;
                providerPaymentId ??= upsertResult.ProviderPaymentId;
                providerTransactionId ??= upsertResult.ProviderTransactionId;
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
            }
            else if (ShouldFailPaystackSubscription(eventType, eventStatus) &&
                     !string.IsNullOrWhiteSpace(providerPaymentId))
            {
                subscriptionId = await MarkSubscriptionStatusAsync(
                    baseUri,
                    apiKey,
                    provider: "paystack",
                    providerPaymentId: providerPaymentId,
                    status: "failed",
                    changedAtUtc: nowUtc,
                    cancellationToken);
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

            if (!eventInserted)
            {
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

    private async Task<bool> InsertSubscriptionEventAsync(
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
            return false;
        }

        return true;
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
    }
}
