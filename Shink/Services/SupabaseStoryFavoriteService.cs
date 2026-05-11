using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseStoryFavoriteService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    ILogger<SupabaseStoryFavoriteService> logger) : IStoryFavoriteService
{
    private const string SubscriberCachePrefix = "story-favorites:subscriber:";
    private static readonly TimeSpan SubscriberCacheDuration = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<SupabaseStoryFavoriteService> _logger = logger;

    public async Task<IReadOnlyList<string>> GetFavoriteStorySlugsAsync(
        string? email,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Array.Empty<string>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase story favorites lookup skipped: URL is not configured.");
            return Array.Empty<string>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase story favorites lookup skipped: SecretKey is not configured.");
            return Array.Empty<string>();
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return Array.Empty<string>();
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var normalizedSource = NormalizeSource(source);
            var sourceFilter = string.IsNullOrWhiteSpace(normalizedSource)
                ? string.Empty
                : $"&source=eq.{Uri.EscapeDataString(normalizedSource)}";

            var requestUri = new Uri(
                baseUri,
                "rest/v1/story_favorites" +
                "?select=story_slug" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                sourceFilter +
                "&order=updated_at.desc" +
                "&limit=5000");

            using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase story favorites lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return Array.Empty<string>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoryFavoriteLookupRow>>(stream, cancellationToken: cancellationToken)
                ?? [];

            return rows
                .Select(row => NormalizeStorySlug(row.StorySlug))
                .Where(slug => !string.IsNullOrWhiteSpace(slug))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase story favorites lookup failed unexpectedly.");
            return Array.Empty<string>();
        }
    }

    public async Task<bool> SetStoryFavoriteAsync(
        string? email,
        StoryFavoriteMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var normalizedStorySlug = NormalizeStorySlug(request.StorySlug);
        var normalizedStoryPath = NormalizeStoryPath(request.StoryPath);
        var normalizedSource = NormalizeSource(request.Source);
        if (string.IsNullOrWhiteSpace(normalizedStorySlug) ||
            string.IsNullOrWhiteSpace(normalizedStoryPath) ||
            string.IsNullOrWhiteSpace(normalizedSource))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase story favorites mutation skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase story favorites mutation skipped: SecretKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return false;
            }

            if (request.IsFavorite)
            {
                var payload = new
                {
                    subscriber_id = subscriberId,
                    story_slug = normalizedStorySlug,
                    story_path = normalizedStoryPath,
                    source = normalizedSource,
                    metadata = new
                    {
                        playlist_slug = NormalizeOptionalText(request.PlaylistSlug, 128)
                    }
                };

                var upsertUri = new Uri(baseUri, "rest/v1/story_favorites?on_conflict=subscriber_id,story_slug,source");
                using var upsertRequest = CreateJsonRequest(
                    HttpMethod.Post,
                    upsertUri,
                    apiKey,
                    payload,
                    "resolution=merge-duplicates,return=minimal");
                using var upsertResponse = await _httpClient.SendAsync(upsertRequest, cancellationToken);
                if (upsertResponse.IsSuccessStatusCode)
                {
                    return true;
                }

                var upsertBody = await upsertResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase story favorite upsert failed. Status={StatusCode} Body={Body}",
                    (int)upsertResponse.StatusCode,
                    upsertBody);
                return false;
            }
            else
            {
                var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
                var escapedStorySlug = Uri.EscapeDataString(normalizedStorySlug);
                var escapedSource = Uri.EscapeDataString(normalizedSource);
                var deleteUri = new Uri(
                    baseUri,
                    $"rest/v1/story_favorites?subscriber_id=eq.{escapedSubscriberId}&story_slug=eq.{escapedStorySlug}&source=eq.{escapedSource}");

                using var deleteRequest = CreateRequest(HttpMethod.Delete, deleteUri, apiKey);
                deleteRequest.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

                using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken);
                if (deleteResponse.IsSuccessStatusCode)
                {
                    return true;
                }

                var deleteBody = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase story favorite delete failed. Status={StatusCode} Body={Body}",
                    (int)deleteResponse.StatusCode,
                    deleteBody);
                return false;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase story favorites mutation failed unexpectedly.");
            return false;
        }
    }

    private async Task<string?> ResolveOrCreateSubscriberIdAsync(
        Uri baseUri,
        string apiKey,
        string email,
        CancellationToken cancellationToken)
    {
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
                    "Supabase subscriber lookup for story favorites failed. Status={StatusCode} Body={Body}",
                    (int)lookupResponse.StatusCode,
                    body);
            }
        }

        var upsertPayload = new
        {
            email = normalizedEmail
        };

        var upsertUri = new Uri(baseUri, "rest/v1/subscribers?on_conflict=email&select=subscriber_id");
        using var upsertRequest = CreateJsonRequest(
            HttpMethod.Post,
            upsertUri,
            apiKey,
            upsertPayload,
            "resolution=merge-duplicates,return=representation");
        using var upsertResponse = await _httpClient.SendAsync(upsertRequest, cancellationToken);
        if (!upsertResponse.IsSuccessStatusCode)
        {
            var upsertBody = await upsertResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscriber upsert for story favorites failed. Status={StatusCode} Body={Body}",
                (int)upsertResponse.StatusCode,
                upsertBody);
            return null;
        }

        var upsertResponseBody = await upsertResponse.Content.ReadAsStringAsync(cancellationToken);
        var createdSubscriberId = ReadFirstStringProperty(upsertResponseBody, "subscriber_id");
        if (!string.IsNullOrWhiteSpace(createdSubscriberId))
        {
            _memoryCache.Set(cacheKey, createdSubscriberId, SubscriberCacheDuration);
        }

        return createdSubscriberId;
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

    private string ResolveApiKey() => _options.SecretKey;

    private static string? NormalizeStorySlug(string? storySlug)
    {
        if (string.IsNullOrWhiteSpace(storySlug))
        {
            return null;
        }

        var normalized = storySlug.Trim().ToLowerInvariant();
        return normalized.Length <= 160
            ? normalized
            : normalized[..160];
    }

    private static string? NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var normalized = source.Trim().ToLowerInvariant();
        return normalized is "gratis" or "luister"
            ? normalized
            : null;
    }

    private static string? NormalizeStoryPath(string? storyPath)
    {
        if (string.IsNullOrWhiteSpace(storyPath))
        {
            return null;
        }

        var normalized = storyPath.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized.Length <= 256
            ? normalized
            : normalized[..256];
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

    private sealed class StoryFavoriteLookupRow
    {
        [JsonPropertyName("story_slug")]
        public string? StorySlug { get; set; }
    }
}
