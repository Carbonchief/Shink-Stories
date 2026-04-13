using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseUserNotificationService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    ICharacterCatalogService characterCatalogService,
    IStoryTrackingService storyTrackingService,
    ILogger<SupabaseUserNotificationService> logger) : IUserNotificationService
{
    private const string SubscriberCachePrefix = "user-notifications:subscriber:";
    private static readonly TimeSpan SubscriberCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ICharacterCatalogService _characterCatalogService = characterCatalogService;
    private readonly IStoryTrackingService _storyTrackingService = storyTrackingService;
    private readonly ILogger<SupabaseUserNotificationService> _logger = logger;

    public async Task<IReadOnlyList<UserAppNotificationItem>> GetNotificationsAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Array.Empty<UserAppNotificationItem>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase notifications lookup skipped: URL is not configured.");
            return Array.Empty<UserAppNotificationItem>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase notifications lookup skipped: ServiceRoleKey is not configured.");
            return Array.Empty<UserAppNotificationItem>();
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return Array.Empty<UserAppNotificationItem>();
            }

            await SyncCharacterUnlockNotificationsAsync(email, cancellationToken: cancellationToken);

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var requestUri = new Uri(
                baseUri,
                "rest/v1/subscriber_notifications" +
                "?select=notification_id,notification_type,title,body,image_path,image_alt,href,created_at,read_at" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                "&cleared_at=is.null" +
                "&order=created_at.desc");

            using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase notifications lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return Array.Empty<UserAppNotificationItem>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<NotificationRow>>(stream, JsonOptions, cancellationToken)
                ?? [];

            return rows
                .Where(row => row.NotificationId != Guid.Empty)
                .Select(MapNotification)
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase notifications lookup failed unexpectedly.");
            return Array.Empty<UserAppNotificationItem>();
        }
    }

    public async Task<int> MarkAllNotificationsReadAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return 0;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase notifications mark-read skipped: URL is not configured.");
            return 0;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase notifications mark-read skipped: ServiceRoleKey is not configured.");
            return 0;
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return 0;
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var requestUri = new Uri(
                baseUri,
                "rest/v1/subscriber_notifications" +
                "?select=notification_id" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                "&cleared_at=is.null" +
                "&read_at=is.null");

            using var request = CreateJsonRequest(
                HttpMethod.Patch,
                requestUri,
                apiKey,
                new
                {
                    read_at = DateTimeOffset.UtcNow
                },
                "return=representation");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase notifications mark-read failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return 0;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<InsertedNotificationRow>>(stream, JsonOptions, cancellationToken)
                ?? [];
            return rows.Count(row => row.NotificationId != Guid.Empty);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase notifications mark-read failed unexpectedly.");
            return 0;
        }
    }

    public async Task<int> ClearNotificationsAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return 0;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase notifications clear skipped: URL is not configured.");
            return 0;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase notifications clear skipped: ServiceRoleKey is not configured.");
            return 0;
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return 0;
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var requestUri = new Uri(
                baseUri,
                "rest/v1/subscriber_notifications" +
                "?select=notification_id" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                "&cleared_at=is.null");

            using var request = CreateJsonRequest(
                HttpMethod.Patch,
                requestUri,
                apiKey,
                new
                {
                    cleared_at = DateTimeOffset.UtcNow,
                    read_at = DateTimeOffset.UtcNow
                },
                "return=representation");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase notifications clear failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return 0;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<InsertedNotificationRow>>(stream, JsonOptions, cancellationToken)
                ?? [];
            return rows.Count(row => row.NotificationId != Guid.Empty);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase notifications clear failed unexpectedly.");
            return 0;
        }
    }

    public async Task<int> CreatePublishedStoryNotificationsAsync(
        PublishedStoryNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.StoryId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Slug) ||
            string.IsNullOrWhiteSpace(request.Title))
        {
            return 0;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase story notification sync skipped: URL is not configured.");
            return 0;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase story notification sync skipped: ServiceRoleKey is not configured.");
            return 0;
        }

        try
        {
            var subscriberIds = await GetAllSubscriberIdsAsync(baseUri, apiKey, cancellationToken);
            if (subscriberIds.Count == 0)
            {
                return 0;
            }

            var normalizedAccessLevel = string.IsNullOrWhiteSpace(request.AccessLevel)
                ? "subscriber"
                : request.AccessLevel.Trim().ToLowerInvariant();
            var normalizedSlug = request.Slug.Trim().ToLowerInvariant();
            var imagePath = ResolveStoryNotificationImagePath(request);
            var body = BuildPublishedStoryNotificationBody(request);
            var href = BuildPublishedStoryNotificationHref(normalizedSlug, normalizedAccessLevel);
            var sourceKey = BuildPublishedStorySourceKey(request.StoryId);

            var notificationsToInsert = subscriberIds
                .Select(subscriberId => new
                {
                    subscriber_id = subscriberId,
                    notification_type = "story_published",
                    source_key = sourceKey,
                    title = "Nuwe storie beskikbaar",
                    body,
                    image_path = imagePath,
                    image_alt = $"Voorblad van {request.Title.Trim()}",
                    href,
                    metadata = new
                    {
                        story_id = request.StoryId,
                        story_slug = normalizedSlug,
                        story_title = request.Title.Trim(),
                        access_level = normalizedAccessLevel
                    }
                })
                .Cast<object>()
                .ToArray();

            return await InsertNotificationsAsync(baseUri, apiKey, notificationsToInsert, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase story notification sync failed unexpectedly.");
            return 0;
        }
    }

    public async Task<NotificationSyncResult> SyncCharacterUnlockNotificationsAsync(
        string? email,
        string? storySlug = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new NotificationSyncResult(0);
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase notifications sync skipped: URL is not configured.");
            return new NotificationSyncResult(0);
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase notifications sync skipped: ServiceRoleKey is not configured.");
            return new NotificationSyncResult(0);
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return new NotificationSyncResult(0);
            }

            var normalizedStorySlug = NormalizeOptionalSlug(storySlug);
            var characters = await _characterCatalogService.GetPublishedCharactersAsync(cancellationToken);
            if (characters.Count == 0)
            {
                return new NotificationSyncResult(0);
            }

            var candidateCharacters = string.IsNullOrWhiteSpace(normalizedStorySlug)
                ? characters
                : characters
                    .Where(character => BuildUnlockStorySlugs(character)
                        .Contains(normalizedStorySlug, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
            if (candidateCharacters.Count == 0)
            {
                return new NotificationSyncResult(0);
            }

            var progressItems = await _storyTrackingService.GetUserStoryProgressAsync(email, cancellationToken);
            if (progressItems.Count == 0)
            {
                return new NotificationSyncResult(0);
            }

            var progressLookup = progressItems
                .GroupBy(progress => progress.StorySlug, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.LastListenedAtUtc).First(),
                    StringComparer.OrdinalIgnoreCase);

            var unlockedCharacters = candidateCharacters
                .Where(character => BuildUnlockStorySlugs(character)
                    .Any(slug => IsCharacterUnlocked(character, slug, progressLookup)))
                .ToArray();
            if (unlockedCharacters.Length == 0)
            {
                return new NotificationSyncResult(0);
            }

            var candidateSourceKeys = unlockedCharacters
                .Select(BuildCharacterUnlockSourceKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var existingSourceKeys = await GetExistingSourceKeysAsync(
                baseUri,
                apiKey,
                subscriberId,
                candidateSourceKeys,
                cancellationToken);

            var notificationsToInsert = unlockedCharacters
                .Where(character => !existingSourceKeys.Contains(BuildCharacterUnlockSourceKey(character)))
                .Select(character => new
                {
                    subscriber_id = subscriberId,
                    notification_type = "character_unlock",
                    source_key = BuildCharacterUnlockSourceKey(character),
                    title = "Nuwe karakter oopgesluit",
                    body = $"{character.DisplayName} is nou beskikbaar. Tik om die karakterprofiel te sien.",
                    image_path = ResolveNotificationImagePath(character),
                    image_alt = $"Illustrasie van {character.DisplayName}",
                    href = $"/karakters?karakter={Uri.EscapeDataString(character.Slug)}",
                    metadata = new
                    {
                        character_id = character.CharacterId,
                        character_slug = character.Slug,
                        character_name = character.DisplayName
                    }
                })
                .ToArray();

            if (notificationsToInsert.Length == 0)
            {
                return new NotificationSyncResult(0);
            }

            var insertedCount = await InsertNotificationsAsync(baseUri, apiKey, notificationsToInsert, cancellationToken);
            return new NotificationSyncResult(insertedCount);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase notifications sync failed unexpectedly.");
            return new NotificationSyncResult(0);
        }
    }

    private async Task<int> InsertNotificationsAsync(
        Uri baseUri,
        string apiKey,
        object[] notifications,
        CancellationToken cancellationToken)
    {
        if (notifications.Length == 0)
        {
            return 0;
        }

        var requestUri = new Uri(
            baseUri,
            "rest/v1/subscriber_notifications" +
            "?on_conflict=subscriber_id,source_key");

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            requestUri,
            apiKey,
            notifications,
            "resolution=ignore-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase notifications insert failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return 0;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var insertedRows = await JsonSerializer.DeserializeAsync<List<InsertedNotificationRow>>(stream, JsonOptions, cancellationToken)
            ?? [];
        return insertedRows.Count(row => row.NotificationId != Guid.Empty);
    }

    private async Task<HashSet<string>> GetExistingSourceKeysAsync(
        Uri baseUri,
        string apiKey,
        string subscriberId,
        IReadOnlyList<string> sourceKeys,
        CancellationToken cancellationToken)
    {
        if (sourceKeys.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
        var joinedSourceKeys = string.Join(',', sourceKeys);
        var requestUri = new Uri(
            baseUri,
            "rest/v1/subscriber_notifications" +
            "?select=source_key" +
            $"&subscriber_id=eq.{escapedSubscriberId}" +
            $"&source_key=in.({joinedSourceKeys})");

        using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase notifications source key lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
            }

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<NotificationSourceKeyRow>>(stream, JsonOptions, cancellationToken)
            ?? [];

        return rows
            .Select(row => row.SourceKey)
            .Where(static sourceKey => !string.IsNullOrWhiteSpace(sourceKey))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<string>> GetAllSubscriberIdsAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var subscriberIds = new List<string>();
        const int pageSize = 1000;
        var offset = 0;

        while (true)
        {
            var requestUri = new Uri(
                baseUri,
                "rest/v1/subscribers" +
                "?select=subscriber_id" +
                $"&limit={pageSize}" +
                $"&offset={offset}");

            using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase subscriber list lookup for notifications failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<SubscriberLookupRow>>(stream, JsonOptions, cancellationToken)
                ?? [];
            var pageSubscriberIds = rows
                .Select(row => row.SubscriberId)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray();

            if (pageSubscriberIds.Length == 0)
            {
                break;
            }

            subscriberIds.AddRange(pageSubscriberIds);
            if (pageSubscriberIds.Length < pageSize)
            {
                break;
            }

            offset += pageSize;
        }

        return subscriberIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
                await using var lookupStream = await lookupResponse.Content.ReadAsStreamAsync(cancellationToken);
                var rows = await JsonSerializer.DeserializeAsync<List<SubscriberLookupRow>>(lookupStream, JsonOptions, cancellationToken)
                    ?? [];
                var subscriberId = rows
                    .Select(row => row.SubscriberId)
                    .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
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
                    "Supabase subscriber lookup for notifications failed. Status={StatusCode} Body={Body}",
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
            var body = await upsertResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase subscriber upsert for notifications failed. Status={StatusCode} Body={Body}",
                (int)upsertResponse.StatusCode,
                body);
            return null;
        }

        await using var upsertStream = await upsertResponse.Content.ReadAsStreamAsync(cancellationToken);
        var upsertRows = await JsonSerializer.DeserializeAsync<List<SubscriberLookupRow>>(upsertStream, JsonOptions, cancellationToken)
            ?? [];
        var createdSubscriberId = upsertRows
            .Select(row => row.SubscriberId)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
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

    private string ResolveApiKey() => _options.ServiceRoleKey;

    private static string NormalizeOptionalSlug(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

    private static bool IsCharacterUnlocked(
        StoryCharacterItem character,
        string storySlug,
        IReadOnlyDictionary<string, UserStoryProgressItem> progressLookup)
    {
        if (!progressLookup.TryGetValue(storySlug, out var progress))
        {
            return false;
        }

        return progress.IsCompleted || progress.TotalListenedSeconds >= character.UnlockThresholdSeconds;
    }

    private static IReadOnlyList<string> BuildUnlockStorySlugs(StoryCharacterItem character)
    {
        var slugs = new List<string>();
        if (!string.IsNullOrWhiteSpace(character.UnlockStorySlug))
        {
            slugs.Add(character.UnlockStorySlug.Trim());
        }

        slugs.AddRange(character.RelatedStorySlugs.Where(slug => !string.IsNullOrWhiteSpace(slug)).Select(slug => slug.Trim()));
        return slugs
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildCharacterUnlockSourceKey(StoryCharacterItem character) =>
        $"character-unlocked-{character.CharacterId:N}";

    private static string BuildPublishedStorySourceKey(Guid storyId) =>
        $"story-published-{storyId:N}";

    private static string BuildPublishedStoryNotificationBody(PublishedStoryNotificationRequest request)
    {
        var normalizedTitle = request.Title.Trim();
        var summary = string.IsNullOrWhiteSpace(request.Summary)
            ? null
            : request.Summary.Trim();

        if (string.IsNullOrWhiteSpace(summary))
        {
            return $"{normalizedTitle} is nou beskikbaar. Tik om te luister.";
        }

        return $"{normalizedTitle} is nou beskikbaar. {summary}";
    }

    private static string ResolveStoryNotificationImagePath(PublishedStoryNotificationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ThumbnailImagePath))
        {
            return request.ThumbnailImagePath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.CoverImagePath))
        {
            return request.CoverImagePath.Trim();
        }

        return "/branding/schink-logo-green.png";
    }

    private static string BuildPublishedStoryNotificationHref(string slug, string accessLevel) =>
        string.Equals(accessLevel, "free", StringComparison.OrdinalIgnoreCase)
            ? $"/gratis/{Uri.EscapeDataString(slug)}"
            : $"/luister/{Uri.EscapeDataString(slug)}";

    private static string ResolveNotificationImagePath(StoryCharacterItem character)
    {
        if (!string.IsNullOrWhiteSpace(character.ImagePath))
        {
            return character.ImagePath;
        }

        if (!string.IsNullOrWhiteSpace(character.MysteryImagePath))
        {
            return character.MysteryImagePath;
        }

        return "/branding/schink-logo-green.png";
    }

    private static UserAppNotificationItem MapNotification(NotificationRow row) =>
        new(
            row.NotificationId,
            row.NotificationType ?? string.Empty,
            row.Title ?? string.Empty,
            row.Body,
            row.ImagePath,
            row.ImageAlt,
            row.Href,
            row.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            row.ReadAtUtc is not null);

    private sealed class SubscriberLookupRow
    {
        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; init; }
    }

    private sealed class NotificationSourceKeyRow
    {
        [JsonPropertyName("source_key")]
        public string? SourceKey { get; init; }
    }

    private sealed class NotificationRow
    {
        [JsonPropertyName("notification_id")]
        public Guid NotificationId { get; init; }

        [JsonPropertyName("notification_type")]
        public string? NotificationType { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("image_path")]
        public string? ImagePath { get; init; }

        [JsonPropertyName("image_alt")]
        public string? ImageAlt { get; init; }

        [JsonPropertyName("href")]
        public string? Href { get; init; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAtUtc { get; init; }

        [JsonPropertyName("read_at")]
        public DateTimeOffset? ReadAtUtc { get; init; }
    }

    private sealed class InsertedNotificationRow
    {
        [JsonPropertyName("notification_id")]
        public Guid NotificationId { get; init; }
    }
}
