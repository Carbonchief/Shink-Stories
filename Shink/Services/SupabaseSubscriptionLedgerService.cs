using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed partial class SupabaseSubscriptionLedgerService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    ILogger<SupabaseSubscriptionLedgerService> logger) : ISubscriptionLedgerService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly ILogger<SupabaseSubscriptionLedgerService> _logger = logger;

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
            var upsertResult = await UpsertActiveSubscriptionAsync(baseUri, apiKey, formCollection, nowUtc, cancellationToken);
            if (!upsertResult.IsSuccess)
            {
                return upsertResult;
            }

            subscriptionId = upsertResult.SubscriptionId;
        }
        else if (string.Equals(paymentStatus, "CANCELLED", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(mPaymentId))
        {
            subscriptionId = await MarkSubscriptionCancelledAsync(baseUri, apiKey, mPaymentId, nowUtc, cancellationToken);
        }

        var eventInserted = await InsertSubscriptionEventAsync(
            baseUri,
            apiKey,
            subscriptionId,
            mPaymentId,
            pfPaymentId,
            paymentStatus,
            rawPayload,
            cancellationToken);

        if (!eventInserted)
        {
            return new SubscriptionPersistResult(false, "Could not persist subscription event.", subscriptionId);
        }

        return new SubscriptionPersistResult(true, null, subscriptionId);
    }

    private async Task<SubscriptionPersistResult> UpsertActiveSubscriptionAsync(
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
        var planSlug = ResolvePlanSlug(formCollection);
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
            mPaymentId,
            pfPaymentId,
            token,
            nowUtc,
            nextRenewalAtUtc,
            cancellationToken);

        if (subscriptionId is null)
        {
            return new SubscriptionPersistResult(false, "Could not upsert subscription record.");
        }

        return new SubscriptionPersistResult(true, null, subscriptionId);
    }

    private async Task<string?> UpsertSubscriberAsync(
        Uri baseUri,
        string apiKey,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            email = email.ToLowerInvariant(),
            first_name = string.IsNullOrWhiteSpace(firstName) ? null : firstName,
            last_name = string.IsNullOrWhiteSpace(lastName) ? null : lastName
        };

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
        string providerPaymentId,
        string providerTransactionId,
        string providerToken,
        DateTimeOffset subscribedAtUtc,
        DateTimeOffset nextRenewalAtUtc,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            subscriber_id = subscriberId,
            tier_code = tierCode,
            provider = "payfast",
            provider_payment_id = providerPaymentId,
            provider_transaction_id = string.IsNullOrWhiteSpace(providerTransactionId) ? null : providerTransactionId,
            provider_token = string.IsNullOrWhiteSpace(providerToken) ? null : providerToken,
            status = "active",
            subscribed_at = subscribedAtUtc.UtcDateTime,
            next_renewal_at = nextRenewalAtUtc.UtcDateTime,
            cancelled_at = (DateTime?)null
        };

        var uri = new Uri(baseUri, "rest/v1/subscriptions?on_conflict=provider_payment_id&select=subscription_id");
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

    private async Task<string?> MarkSubscriptionCancelledAsync(
        Uri baseUri,
        string apiKey,
        string providerPaymentId,
        DateTimeOffset cancelledAtUtc,
        CancellationToken cancellationToken)
    {
        var filterPaymentId = Uri.EscapeDataString(providerPaymentId);
        var uri = new Uri(baseUri, $"rest/v1/subscriptions?provider_payment_id=eq.{filterPaymentId}&select=subscription_id");
        var payload = new
        {
            status = "cancelled",
            cancelled_at = cancelledAtUtc.UtcDateTime
        };

        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscription cancellation update failed. Status={StatusCode} Body={Body}",
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
        string providerPaymentId,
        string providerTransactionId,
        string eventStatus,
        Dictionary<string, string> payload,
        CancellationToken cancellationToken)
    {
        var eventPayload = new
        {
            subscription_id = string.IsNullOrWhiteSpace(subscriptionId) ? null : subscriptionId,
            provider = "payfast",
            provider_payment_id = string.IsNullOrWhiteSpace(providerPaymentId) ? null : providerPaymentId,
            provider_transaction_id = string.IsNullOrWhiteSpace(providerTransactionId) ? null : providerTransactionId,
            event_type = "payfast_itn",
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
                "Supabase subscription event insert failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return false;
        }

        return true;
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

    private string ResolveApiKey()
    {
        return _options.ServiceRoleKey;
    }

    private static string? ResolvePlanSlug(IFormCollection formCollection)
    {
        var explicitSlug = formCollection["custom_str1"].ToString().Trim();
        if (!string.IsNullOrWhiteSpace(explicitSlug))
        {
            return explicitSlug;
        }

        var paymentId = formCollection["m_payment_id"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return null;
        }

        var suffixMatch = PaymentIdSuffixRegex().Match(paymentId);
        return suffixMatch.Success && suffixMatch.Index > 0
            ? paymentId[..suffixMatch.Index]
            : null;
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
                first.TryGetProperty(propertyName, out var node) &&
                node.ValueKind == JsonValueKind.String)
            {
                return node.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    [GeneratedRegex("-\\d{14}-[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex PaymentIdSuffixRegex();
}
