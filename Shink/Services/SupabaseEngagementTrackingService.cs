using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseEngagementTrackingService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    ILogger<SupabaseEngagementTrackingService> logger) : IEngagementTrackingService
{
    private const string SubscriberCachePrefix = "engagement-tracking:subscriber:";
    private static readonly TimeSpan SubscriberCacheDuration = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<SupabaseEngagementTrackingService> _logger = logger;

    public async Task<bool> RecordResourceDownloadAsync(
        string? email,
        Guid resourceDocumentId,
        string? downloadPath,
        CancellationToken cancellationToken = default)
    {
        if (resourceDocumentId == Guid.Empty || string.IsNullOrWhiteSpace(downloadPath))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase resource download tracking skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase resource download tracking skipped: ServiceRoleKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await ResolveSubscriberIdIfAvailableAsync(baseUri, apiKey, email, cancellationToken);
            var payload = new
            {
                resource_document_id = resourceDocumentId,
                subscriber_id = subscriberId,
                download_path = NormalizeOptionalText(downloadPath)
            };

            return await InsertAsync(baseUri, apiKey, "rest/v1/resource_document_download_events", payload, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase resource download tracking failed unexpectedly.");
            return false;
        }
    }

    public async Task<bool> RecordBlogVisitAsync(
        string? email,
        Guid? postId,
        string? postSlug,
        string? visitPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(visitPath))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase blog visit tracking skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase blog visit tracking skipped: ServiceRoleKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await ResolveSubscriberIdIfAvailableAsync(baseUri, apiKey, email, cancellationToken);
            var payload = new
            {
                post_id = postId.HasValue && postId.Value != Guid.Empty ? postId.Value : (Guid?)null,
                subscriber_id = subscriberId,
                post_slug = NormalizeOptionalText(postSlug),
                visit_path = NormalizeOptionalText(visitPath)
            };

            return await InsertAsync(baseUri, apiKey, "rest/v1/blog_visit_events", payload, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase blog visit tracking failed unexpectedly.");
            return false;
        }
    }

    private async Task<string?> ResolveSubscriberIdIfAvailableAsync(
        Uri baseUri,
        string apiKey,
        string? email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return null;
        }

        var cacheKey = $"{SubscriberCachePrefix}{normalizedEmail}";
        if (_memoryCache.TryGetValue(cacheKey, out string? cachedSubscriberId) &&
            !string.IsNullOrWhiteSpace(cachedSubscriberId))
        {
            return cachedSubscriberId;
        }

        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var lookupUri = new Uri(baseUri, $"rest/v1/subscribers?select=subscriber_id&email=eq.{escapedEmail}&limit=1");

        using (var lookupRequest = CreateRequest(HttpMethod.Get, lookupUri, apiKey))
        using (var lookupResponse = await _httpClient.SendAsync(lookupRequest, cancellationToken))
        {
            if (lookupResponse.IsSuccessStatusCode)
            {
                var body = await lookupResponse.Content.ReadAsStringAsync(cancellationToken);
                var subscriberId = ReadFirstStringProperty(body, "subscriber_id");
                if (!string.IsNullOrWhiteSpace(subscriberId))
                {
                    _memoryCache.Set(cacheKey, subscriberId, SubscriberCacheDuration);
                    return subscriberId;
                }
            }
            else
            {
                var body = await lookupResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase subscriber lookup for engagement tracking failed. Status={StatusCode} Body={Body}",
                    (int)lookupResponse.StatusCode,
                    body);
            }
        }

        var upsertPayload = new
        {
            email = normalizedEmail
        };
        var upsertUri = new Uri(baseUri, "rest/v1/subscribers?on_conflict=email&select=subscriber_id");

        using var upsertRequest = CreateRequest(HttpMethod.Post, upsertUri, apiKey);
        upsertRequest.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");
        upsertRequest.Content = new StringContent(
            JsonSerializer.Serialize(upsertPayload),
            Encoding.UTF8,
            "application/json");

        using var upsertResponse = await _httpClient.SendAsync(upsertRequest, cancellationToken);
        if (!upsertResponse.IsSuccessStatusCode)
        {
            var body = await upsertResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscriber upsert for engagement tracking failed. Status={StatusCode} Body={Body}",
                (int)upsertResponse.StatusCode,
                body);
            return null;
        }

        var upsertBody = await upsertResponse.Content.ReadAsStringAsync(cancellationToken);
        var createdSubscriberId = ReadFirstStringProperty(upsertBody, "subscriber_id");
        if (!string.IsNullOrWhiteSpace(createdSubscriberId))
        {
            _memoryCache.Set(cacheKey, createdSubscriberId, SubscriberCacheDuration);
        }

        return createdSubscriberId;
    }

    private async Task<bool> InsertAsync(
        Uri baseUri,
        string apiKey,
        string relativeUri,
        object payload,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(baseUri, relativeUri);
        using var request = CreateRequest(HttpMethod.Post, uri, apiKey);
        request.Headers.Add("Prefer", "return=minimal");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase engagement insert failed. Uri={Uri} Status={StatusCode} Body={Body}",
            uri,
            (int)response.StatusCode,
            body);
        return false;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("apikey", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out baseUri!))
        {
            return false;
        }

        return string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveApiKey() =>
        string.IsNullOrWhiteSpace(_options.ServiceRoleKey)
            ? _options.AnonKey
            : _options.ServiceRoleKey;

    private static string? ReadFirstStringProperty(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var first = document.RootElement[0];
        return first.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
