using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseStoryTrackingService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    ILogger<SupabaseStoryTrackingService> logger) : IStoryTrackingService
{
    private const string SubscriberCachePrefix = "story-tracking:subscriber:";
    private static readonly TimeSpan SubscriberCacheDuration = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<SupabaseStoryTrackingService> _logger = logger;

    public async Task<bool> RecordStoryViewAsync(string? email, StoryViewTrackingRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(request.StorySlug) ||
            string.IsNullOrWhiteSpace(request.StoryPath))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase story view tracking skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase story view tracking skipped: ServiceRoleKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return false;
            }

            var payload = new
            {
                subscriber_id = subscriberId,
                story_slug = request.StorySlug,
                story_path = request.StoryPath,
                metadata = new
                {
                    source = NormalizeOptionalText(request.Source),
                    referrer_path = NormalizeOptionalText(request.ReferrerPath)
                }
            };

            return await InsertAsync(baseUri, apiKey, "rest/v1/story_views", payload, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase story view tracking failed unexpectedly.");
            return false;
        }
    }

    public async Task<bool> RecordStoryListenAsync(string? email, StoryListenTrackingRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(request.StorySlug) ||
            string.IsNullOrWhiteSpace(request.StoryPath) ||
            request.ListenedSeconds <= 0)
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase story listen tracking skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase story listen tracking skipped: ServiceRoleKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return false;
            }

            var payload = new
            {
                subscriber_id = subscriberId,
                story_slug = request.StorySlug,
                story_path = request.StoryPath,
                session_id = request.SessionId,
                event_type = request.EventType,
                listened_seconds = request.ListenedSeconds,
                position_seconds = request.PositionSeconds,
                duration_seconds = request.DurationSeconds,
                metadata = new
                {
                    source = NormalizeOptionalText(request.Source),
                    is_completed = request.IsCompleted
                }
            };

            return await InsertAsync(baseUri, apiKey, "rest/v1/story_listen_events", payload, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase story listen tracking failed unexpectedly.");
            return false;
        }
    }

    public async Task<IReadOnlyList<UserStoryProgressItem>> GetUserStoryProgressAsync(string? email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Array.Empty<UserStoryProgressItem>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase story progress lookup skipped: URL is not configured.");
            return Array.Empty<UserStoryProgressItem>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase story progress lookup skipped: ServiceRoleKey is not configured.");
            return Array.Empty<UserStoryProgressItem>();
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return Array.Empty<UserStoryProgressItem>();
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var requestUri = new Uri(
                baseUri,
                "rest/v1/story_listen_events" +
                "?select=story_slug,story_path,session_id,event_type,listened_seconds,position_seconds,duration_seconds,occurred_at,metadata" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                "&order=occurred_at.desc" +
                "&limit=2500");

            using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase story progress lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return Array.Empty<UserStoryProgressItem>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoryListenEventRow>>(stream, cancellationToken: cancellationToken)
                ?? [];

            return BuildProgressItems(rows);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase story progress lookup failed unexpectedly.");
            return Array.Empty<UserStoryProgressItem>();
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
                    "Supabase subscriber lookup for story tracking failed. Status={StatusCode} Body={Body}",
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
                "Supabase subscriber upsert for story tracking failed. Status={StatusCode} Body={Body}",
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

    private async Task<bool> InsertAsync(
        Uri baseUri,
        string apiKey,
        string relativePath,
        object payload,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(baseUri, relativePath);
        using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase story tracking insert failed. Path={Path} Status={StatusCode} Body={Body}",
                relativePath,
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

    private static string? NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length > 256
            ? normalized[..256]
            : normalized;
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

    private static IReadOnlyList<UserStoryProgressItem> BuildProgressItems(IEnumerable<StoryListenEventRow> rows)
    {
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.StorySlug))
            .GroupBy(row => row.StorySlug.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .Select(group =>
            {
                var latest = group
                    .OrderByDescending(row => row.OccurredAt)
                    .First();

                var totalListenedSeconds = decimal.Round(
                    group.Sum(row => row.ListenedSeconds <= 0m ? 0m : row.ListenedSeconds),
                    3,
                    MidpointRounding.AwayFromZero);

                var sessions = group
                    .GroupBy(row => row.SessionId ?? Guid.Empty)
                    .ToArray();

                var listenCount = sessions.Count(sessionGroup =>
                    sessionGroup.Key != Guid.Empty &&
                    sessionGroup.Any(row => row.ListenedSeconds > 0m));

                var completedListenCount = sessions.Count(sessionGroup =>
                    sessionGroup.Key != Guid.Empty &&
                    IsSessionCompleted(sessionGroup));

                var latestPositionSeconds = latest.PositionSeconds;
                var latestDurationSeconds = latest.DurationSeconds;
                if (latestDurationSeconds is null || latestDurationSeconds <= 0m)
                {
                    latestDurationSeconds = group
                        .Where(row => row.DurationSeconds is > 0m)
                        .OrderByDescending(row => row.OccurredAt)
                        .Select(row => row.DurationSeconds)
                        .FirstOrDefault();
                }

                var completedFromEvent = group.Any(row =>
                    string.Equals(row.EventType, "ended", StringComparison.OrdinalIgnoreCase) ||
                    ReadMetadataCompletedFlag(row.Metadata));
                var completedFromPosition = latestPositionSeconds is > 0m &&
                                            latestDurationSeconds is > 0m &&
                                            latestPositionSeconds >= (latestDurationSeconds * 0.95m);

                var source = ReadMetadataSource(latest.Metadata) ??
                             InferSourceFromPath(latest.StoryPath) ??
                             "unknown";

                return new UserStoryProgressItem(
                    StorySlug: latest.StorySlug.Trim().ToLowerInvariant(),
                    StoryPath: NormalizeStoryPath(latest.StoryPath, latest.StorySlug, source),
                    Source: source,
                    LastListenedAtUtc: latest.OccurredAt,
                    TotalListenedSeconds: totalListenedSeconds,
                    ListenCount: listenCount,
                    CompletedListenCount: completedListenCount,
                    LastPositionSeconds: NormalizeOptionalMetric(latestPositionSeconds),
                    DurationSeconds: NormalizeOptionalMetric(latestDurationSeconds),
                    IsCompleted: completedFromEvent || completedFromPosition);
            })
            .OrderByDescending(item => item.LastListenedAtUtc)
            .ToArray();
    }

    private static string? InferSourceFromPath(string? storyPath)
    {
        if (string.IsNullOrWhiteSpace(storyPath))
        {
            return null;
        }

        var normalized = storyPath.Trim();
        if (normalized.StartsWith("/gratis/", StringComparison.OrdinalIgnoreCase))
        {
            return "gratis";
        }

        if (normalized.StartsWith("/luister/", StringComparison.OrdinalIgnoreCase))
        {
            return "luister";
        }

        return null;
    }

    private static bool IsSessionCompleted(IGrouping<Guid, StoryListenEventRow> sessionGroup)
    {
        var latestRow = sessionGroup
            .OrderByDescending(row => row.OccurredAt)
            .First();

        var latestDurationSeconds = latestRow.DurationSeconds;
        if (latestDurationSeconds is null || latestDurationSeconds <= 0m)
        {
            latestDurationSeconds = sessionGroup
                .Where(row => row.DurationSeconds is > 0m)
                .OrderByDescending(row => row.OccurredAt)
                .Select(row => row.DurationSeconds)
                .FirstOrDefault();
        }

        var completedFromEvent = sessionGroup.Any(row =>
            string.Equals(row.EventType, "ended", StringComparison.OrdinalIgnoreCase) ||
            ReadMetadataCompletedFlag(row.Metadata));
        var completedFromPosition = latestRow.PositionSeconds is > 0m &&
                                    latestDurationSeconds is > 0m &&
                                    latestRow.PositionSeconds >= (latestDurationSeconds * 0.95m);

        return completedFromEvent || completedFromPosition;
    }

    private static string NormalizeStoryPath(string? storyPath, string? storySlug, string source)
    {
        if (!string.IsNullOrWhiteSpace(storyPath))
        {
            var normalizedPath = storyPath.Trim();
            if (normalizedPath.StartsWith("/", StringComparison.Ordinal) &&
                !normalizedPath.StartsWith("//", StringComparison.Ordinal))
            {
                return normalizedPath.Length > 256
                    ? normalizedPath[..256]
                    : normalizedPath;
            }
        }

        var normalizedSlug = string.IsNullOrWhiteSpace(storySlug)
            ? string.Empty
            : storySlug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return "/luister";
        }

        return source switch
        {
            "gratis" => $"/gratis/{Uri.EscapeDataString(normalizedSlug)}",
            _ => $"/luister/{Uri.EscapeDataString(normalizedSlug)}"
        };
    }

    private static decimal? NormalizeOptionalMetric(decimal? value)
    {
        if (value is null || value <= 0m)
        {
            return null;
        }

        return decimal.Round(value.Value, 3, MidpointRounding.AwayFromZero);
    }

    private static bool ReadMetadataCompletedFlag(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty("is_completed", out var node))
        {
            return false;
        }

        return node.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(node.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static string? ReadMetadataSource(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty("source", out var node))
        {
            return null;
        }

        if (node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var normalized = node.GetString()?.Trim().ToLowerInvariant();
        return normalized is "gratis" or "luister"
            ? normalized
            : null;
    }

    private sealed class StoryListenEventRow
    {
        [JsonPropertyName("story_slug")]
        public string StorySlug { get; set; } = string.Empty;

        [JsonPropertyName("story_path")]
        public string? StoryPath { get; set; }

        [JsonPropertyName("session_id")]
        public Guid? SessionId { get; set; }

        [JsonPropertyName("event_type")]
        public string? EventType { get; set; }

        [JsonPropertyName("listened_seconds")]
        public decimal ListenedSeconds { get; set; }

        [JsonPropertyName("position_seconds")]
        public decimal? PositionSeconds { get; set; }

        [JsonPropertyName("duration_seconds")]
        public decimal? DurationSeconds { get; set; }

        [JsonPropertyName("occurred_at")]
        public DateTimeOffset OccurredAt { get; set; }

        [JsonPropertyName("metadata")]
        public JsonElement Metadata { get; set; }
    }
}
