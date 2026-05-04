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
    ICharacterTrackingService characterTrackingService,
    IStoryTrackingService storyTrackingService,
    ILogger<SupabaseUserNotificationService> logger) : IUserNotificationService
{
    private const string CharacterUnlockSourceKeyPrefix = "character-unlocked-";
    private const string SubscriberCachePrefix = "user-notifications:subscriber:";
    private static readonly TimeSpan SubscriberCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ICharacterCatalogService _characterCatalogService = characterCatalogService;
    private readonly ICharacterTrackingService _characterTrackingService = characterTrackingService;
    private readonly IStoryTrackingService _storyTrackingService = storyTrackingService;
    private readonly ILogger<SupabaseUserNotificationService> _logger = logger;
    private const int DefaultNotificationPageSize = 10;
    private const int MaxNotificationPageSize = 25;

    public async Task<UserNotificationPageResult> GetNotificationsAsync(
        string? email,
        int take = DefaultNotificationPageSize,
        DateTimeOffset? before = null,
        bool history = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return EmptyNotificationPageResult;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase notifications lookup skipped: URL is not configured.");
            return EmptyNotificationPageResult;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase notifications lookup skipped: ServiceRoleKey is not configured.");
            return EmptyNotificationPageResult;
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return EmptyNotificationPageResult;
            }

            await SyncCharacterUnlockNotificationsAsync(email, cancellationToken: cancellationToken);

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var pageSize = Math.Clamp(take, 1, MaxNotificationPageSize);
            var unreadCount = await GetNotificationCountAsync(
                baseUri,
                apiKey,
                escapedSubscriberId,
                unreadOnly: true,
                includeCleared: false,
                before: null,
                cancellationToken);
            var requestUri = new Uri(
                baseUri,
                "rest/v1/subscriber_notifications" +
                "?select=notification_id,notification_type,title,body,image_path,image_alt,href,created_at,read_at,cleared_at" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                (history ? string.Empty : "&cleared_at=is.null") +
                (before is DateTimeOffset beforeValue
                    ? $"&created_at=lt.{Uri.EscapeDataString(beforeValue.UtcDateTime.ToString("O"))}"
                    : string.Empty) +
                "&order=created_at.desc" +
                $"&limit={pageSize + 1}");

            using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase notifications lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return EmptyNotificationPageResult with { UnreadCount = unreadCount };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<NotificationRow>>(stream, JsonOptions, cancellationToken)
                ?? [];

            var hasMore = rows.Count > pageSize;
            var notifications = rows
                .Take(pageSize)
                .Where(row => row.NotificationId != Guid.Empty)
                .Select(MapNotification)
                .ToArray();
            var oldestLoadedNotificationCreatedAt = notifications.LastOrDefault()?.CreatedAtUtc;
            var hasHistory = await GetNotificationCountAsync(
                baseUri,
                apiKey,
                escapedSubscriberId,
                unreadOnly: false,
                includeCleared: true,
                before: history
                    ? oldestLoadedNotificationCreatedAt
                    : notifications.Length == 0 ? null : oldestLoadedNotificationCreatedAt,
                cancellationToken) > 0;

            return new UserNotificationPageResult(
                notifications,
                unreadCount,
                hasMore,
                hasHistory);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase notifications lookup failed unexpectedly.");
            return EmptyNotificationPageResult;
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

    public async Task<bool> MarkNotificationReadAsync(
        string? email,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || notificationId == Guid.Empty)
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase notification mark-read skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase notification mark-read skipped: ServiceRoleKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return false;
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var escapedNotificationId = Uri.EscapeDataString(notificationId.ToString());
            var requestUri = new Uri(
                baseUri,
                "rest/v1/subscriber_notifications" +
                "?select=notification_id" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                $"&notification_id=eq.{escapedNotificationId}" +
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
                    "Supabase notification mark-read failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<InsertedNotificationRow>>(stream, JsonOptions, cancellationToken)
                ?? [];
            return rows.Any(row => row.NotificationId == notificationId);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase notification mark-read failed unexpectedly.");
            return false;
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

    public async Task<bool> ClearNotificationAsync(
        string? email,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || notificationId == Guid.Empty)
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase notification clear skipped: URL is not configured.");
            return false;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase notification clear skipped: ServiceRoleKey is not configured.");
            return false;
        }

        try
        {
            var subscriberId = await ResolveOrCreateSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return false;
            }

            var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
            var escapedNotificationId = Uri.EscapeDataString(notificationId.ToString());
            var requestUri = new Uri(
                baseUri,
                "rest/v1/subscriber_notifications" +
                "?select=notification_id" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                $"&notification_id=eq.{escapedNotificationId}" +
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
                    "Supabase notification clear failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<InsertedNotificationRow>>(stream, JsonOptions, cancellationToken)
                ?? [];
            return rows.Any(row => row.NotificationId == notificationId);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase notification clear failed unexpectedly.");
            return false;
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
            var subscriberIds = await GetActiveSubscriberIdsAsync(baseUri, apiKey, cancellationToken);
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

    public async Task<int> CreatePublishedBlogNotificationsAsync(
        PublishedBlogNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PostId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Slug) ||
            string.IsNullOrWhiteSpace(request.Title))
        {
            return 0;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase blog notification sync skipped: URL is not configured.");
            return 0;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase blog notification sync skipped: ServiceRoleKey is not configured.");
            return 0;
        }

        try
        {
            var subscriberIds = await GetActiveSubscriberIdsAsync(baseUri, apiKey, cancellationToken);
            if (subscriberIds.Count == 0)
            {
                return 0;
            }

            var normalizedSlug = request.Slug.Trim().ToLowerInvariant();
            var normalizedTitle = request.Title.Trim();
            var imagePath = ResolveBlogNotificationImagePath(request);
            var href = BuildPublishedBlogNotificationHref(normalizedSlug);
            var sourceKey = BuildPublishedBlogSourceKey(request.PostId);

            var notificationsToInsert = subscriberIds
                .Select(subscriberId => new
                {
                    subscriber_id = subscriberId,
                    notification_type = "blog_published",
                    source_key = sourceKey,
                    title = normalizedTitle,
                    body = (string?)null,
                    image_path = imagePath,
                    image_alt = $"Hoofprent vir {normalizedTitle}",
                    href,
                    metadata = new
                    {
                        post_id = request.PostId,
                        post_slug = normalizedSlug,
                        post_title = normalizedTitle
                    }
                })
                .Cast<object>()
                .ToArray();

            return await InsertNotificationsAsync(baseUri, apiKey, notificationsToInsert, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase blog notification sync failed unexpectedly.");
            return 0;
        }
    }

    public async Task<int> CreatePublishedResourceDocumentNotificationsAsync(
        PublishedResourceDocumentNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ResourceDocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ResourceTypeSlug) ||
            string.IsNullOrWhiteSpace(request.Title))
        {
            return 0;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase resource notification sync skipped: URL is not configured.");
            return 0;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase resource notification sync skipped: ServiceRoleKey is not configured.");
            return 0;
        }

        try
        {
            var subscriberIds = await GetActiveSubscriberIdsAsync(baseUri, apiKey, cancellationToken);
            if (subscriberIds.Count == 0)
            {
                return 0;
            }

            var normalizedResourceTypeSlug = request.ResourceTypeSlug.Trim().ToLowerInvariant();
            var normalizedResourceTypeName = string.IsNullOrWhiteSpace(request.ResourceTypeName)
                ? "Hulpbronne"
                : request.ResourceTypeName.Trim();
            var normalizedTitle = request.Title.Trim();
            var href = BuildPublishedResourceDocumentNotificationHref(normalizedResourceTypeSlug);
            var sourceKey = BuildPublishedResourceDocumentSourceKey(request.ResourceDocumentId);
            var imagePath = ResolveResourceDocumentNotificationImagePath(request);

            var notificationsToInsert = subscriberIds
                .Select(subscriberId => new
                {
                    subscriber_id = subscriberId,
                    notification_type = "resource_document_published",
                    source_key = sourceKey,
                    title = "Nuwe hulpbron beskikbaar",
                    body = $"{normalizedTitle} is nou beskikbaar.",
                    image_path = imagePath,
                    image_alt = $"Voorskou van {normalizedTitle}",
                    href,
                    metadata = new
                    {
                        resource_document_id = request.ResourceDocumentId,
                        resource_type_slug = normalizedResourceTypeSlug,
                        resource_type_name = normalizedResourceTypeName,
                        resource_document_title = normalizedTitle
                    }
                })
                .Cast<object>()
                .ToArray();

            return await InsertNotificationsAsync(baseUri, apiKey, notificationsToInsert, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase resource notification sync failed unexpectedly.");
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
            var subscriberId = await ResolveActiveSubscriberIdAsync(baseUri, apiKey, email, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return new NotificationSyncResult(0);
            }

            var characters = await _characterCatalogService.GetPublishedCharactersAsync(cancellationToken);
            if (characters.Count == 0)
            {
                return new NotificationSyncResult(0);
            }

            var progressItems = await _storyTrackingService.GetUserStoryProgressAsync(email, cancellationToken);
            var progressLookup = progressItems
                .GroupBy(progress => progress.StorySlug, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.LastListenedAtUtc).First(),
                    StringComparer.OrdinalIgnoreCase);
            var profileListenItems = await _characterTrackingService.GetUserProfileListenStatsAsync(email, cancellationToken);
            var profileListenLookup = profileListenItems
                .GroupBy(item => item.CharacterSlug, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.ListenCount),
                    StringComparer.OrdinalIgnoreCase);
            var unlockStateLookup = CharacterUnlockEvaluator.EvaluateUnlockStates(
                characters,
                progressLookup,
                profileListenLookup);

            var trackedUnlockStates = await GetCharacterUnlockStatesAsync(
                baseUri,
                apiKey,
                subscriberId,
                cancellationToken);
            var historicalUnlockCounts = await GetCharacterUnlockNotificationCountsAsync(
                baseUri,
                apiKey,
                subscriberId,
                cancellationToken);

            var notificationsToInsert = new List<object>();
            var stateUpdates = new List<object>();
            var nowUtc = DateTimeOffset.UtcNow;

            foreach (var character in characters)
            {
                var isUnlocked = unlockStateLookup.TryGetValue(character.CharacterId, out var unlocked) && unlocked;
                historicalUnlockCounts.TryGetValue(character.CharacterId, out var historicalUnlockCount);

                if (trackedUnlockStates.TryGetValue(character.CharacterId, out var trackedState))
                {
                    var trackedUnlockCount = Math.Max(trackedState.UnlockCount, historicalUnlockCount);
                    if (trackedState.IsUnlocked == isUnlocked)
                    {
                        if (trackedState.UnlockCount != trackedUnlockCount)
                        {
                            stateUpdates.Add(BuildCharacterUnlockStatePayload(
                                subscriberId,
                                character.CharacterId,
                                isUnlocked,
                                trackedUnlockCount,
                                nowUtc));
                        }

                        continue;
                    }

                    if (isUnlocked)
                    {
                        var nextUnlockCount = trackedUnlockCount + 1;
                        notificationsToInsert.Add(BuildCharacterUnlockNotificationPayload(subscriberId, character, nextUnlockCount));
                        stateUpdates.Add(BuildCharacterUnlockStatePayload(
                            subscriberId,
                            character.CharacterId,
                            true,
                            nextUnlockCount,
                            nowUtc));
                    }
                    else
                    {
                        stateUpdates.Add(BuildCharacterUnlockStatePayload(
                            subscriberId,
                            character.CharacterId,
                            false,
                            trackedUnlockCount,
                            nowUtc));
                    }

                    continue;
                }

                if (isUnlocked)
                {
                    if (historicalUnlockCount > 0)
                    {
                        stateUpdates.Add(BuildCharacterUnlockStatePayload(
                            subscriberId,
                            character.CharacterId,
                            true,
                            historicalUnlockCount,
                            nowUtc));
                    }
                    else
                    {
                        notificationsToInsert.Add(BuildCharacterUnlockNotificationPayload(subscriberId, character, 1));
                        stateUpdates.Add(BuildCharacterUnlockStatePayload(
                            subscriberId,
                            character.CharacterId,
                            true,
                            1,
                            nowUtc));
                    }
                }
                else if (historicalUnlockCount > 0)
                {
                    stateUpdates.Add(BuildCharacterUnlockStatePayload(
                        subscriberId,
                        character.CharacterId,
                        false,
                        historicalUnlockCount,
                        nowUtc));
                }
            }

            var insertedCount = await InsertNotificationsAsync(baseUri, apiKey, [.. notificationsToInsert], cancellationToken);
            await UpsertCharacterUnlockStatesAsync(baseUri, apiKey, [.. stateUpdates], cancellationToken);
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

    private async Task<Dictionary<Guid, CharacterUnlockTrackedState>> GetCharacterUnlockStatesAsync(
        Uri baseUri,
        string apiKey,
        string subscriberId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
        var requestUri = new Uri(
            baseUri,
            "rest/v1/subscriber_character_unlock_states" +
            "?select=character_id,is_unlocked,unlock_count" +
            $"&subscriber_id=eq.{escapedSubscriberId}");

        using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase character unlock state lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
            }

            return new Dictionary<Guid, CharacterUnlockTrackedState>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<CharacterUnlockStateRow>>(stream, JsonOptions, cancellationToken)
            ?? [];

        return rows
            .Where(row => row.CharacterId != Guid.Empty)
            .GroupBy(row => row.CharacterId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group.Last();
                    return new CharacterUnlockTrackedState(row.IsUnlocked, Math.Max(0, row.UnlockCount));
                });
    }

    private async Task<Dictionary<Guid, int>> GetCharacterUnlockNotificationCountsAsync(
        Uri baseUri,
        string apiKey,
        string subscriberId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId);
        var requestUri = new Uri(
            baseUri,
            "rest/v1/subscriber_notifications" +
            "?select=source_key" +
            $"&subscriber_id=eq.{escapedSubscriberId}" +
            "&notification_type=eq.character_unlock" +
            "&limit=5000");

        using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase character unlock notification history lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
            }

            return new Dictionary<Guid, int>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<NotificationSourceKeyRow>>(stream, JsonOptions, cancellationToken)
            ?? [];

        var counts = new Dictionary<Guid, int>();
        foreach (var row in rows)
        {
            if (!TryParseCharacterUnlockSourceKey(row.SourceKey, out var characterId, out var unlockCount))
            {
                continue;
            }

            counts[characterId] = counts.TryGetValue(characterId, out var existingCount)
                ? Math.Max(existingCount, unlockCount)
                : unlockCount;
        }

        return counts;
    }

    private async Task UpsertCharacterUnlockStatesAsync(
        Uri baseUri,
        string apiKey,
        object[] states,
        CancellationToken cancellationToken)
    {
        if (states.Length == 0)
        {
            return;
        }

        var requestUri = new Uri(
            baseUri,
            "rest/v1/subscriber_character_unlock_states" +
            "?on_conflict=subscriber_id,character_id");

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            requestUri,
            apiKey,
            states,
            "resolution=merge-duplicates,return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase character unlock state upsert failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
        }
    }

    private async Task<int> GetNotificationCountAsync(
        Uri baseUri,
        string apiKey,
        string escapedSubscriberId,
        bool unreadOnly,
        bool includeCleared,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(
            baseUri,
            "rest/v1/subscriber_notifications" +
            "?select=notification_id" +
            $"&subscriber_id=eq.{escapedSubscriberId}" +
            (includeCleared ? string.Empty : "&cleared_at=is.null") +
            (unreadOnly ? "&read_at=is.null" : string.Empty) +
            (before is DateTimeOffset beforeValue
                ? $"&created_at=lt.{Uri.EscapeDataString(beforeValue.UtcDateTime.ToString("O"))}"
                : string.Empty) +
            "&limit=1");

        using var request = CreateRequest(HttpMethod.Get, requestUri, apiKey);
        request.Headers.TryAddWithoutValidation("Prefer", "count=exact");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase notification count lookup failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);
            return 0;
        }

        return TryParseContentRangeCount(response, out var count)
            ? count
            : 0;
    }

    private async Task<IReadOnlyList<string>> GetActiveSubscriberIdsAsync(
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var subscriberIds = new List<string>();
        const int pageSize = 1000;
        var offset = 0;
        var nowUtc = DateTimeOffset.UtcNow;

        while (true)
        {
            var requestUri = new Uri(
                baseUri,
                "rest/v1/subscribers" +
                "?select=subscriber_id,subscriptions!inner(status,next_renewal_at,cancelled_at)" +
                "&disabled_at=is.null" +
                "&subscriptions.status=eq.active" +
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
            var rows = await JsonSerializer.DeserializeAsync<List<ActiveSubscriberLookupRow>>(stream, JsonOptions, cancellationToken)
                ?? [];
            var pageSubscriberIds = rows
                .Where(row => row.Subscriptions?.Any(subscription => IsCurrentlyActiveSubscription(subscription, nowUtc)) == true)
                .Select(row => row.SubscriberId)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray();

            if (rows.Count == 0)
            {
                break;
            }

            subscriberIds.AddRange(pageSubscriberIds);
            if (rows.Count < pageSize)
            {
                break;
            }

            offset += pageSize;
        }

        return subscriberIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string?> ResolveActiveSubscriberIdAsync(
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

        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var lookupUri = new Uri(
            baseUri,
            "rest/v1/subscribers" +
            "?select=subscriber_id,subscriptions!inner(status,next_renewal_at,cancelled_at)" +
            $"&email=eq.{escapedEmail}" +
            "&disabled_at=is.null" +
            "&subscriptions.status=eq.active" +
            "&limit=1");

        using var lookupRequest = CreateRequest(HttpMethod.Get, lookupUri, apiKey);
        using var lookupResponse = await _httpClient.SendAsync(lookupRequest, cancellationToken);
        if (!lookupResponse.IsSuccessStatusCode)
        {
            var body = await lookupResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase active subscriber lookup for notifications failed. Status={StatusCode} Body={Body}",
                (int)lookupResponse.StatusCode,
                body);
            return null;
        }

        await using var lookupStream = await lookupResponse.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<ActiveSubscriberLookupRow>>(lookupStream, JsonOptions, cancellationToken)
            ?? [];
        var nowUtc = DateTimeOffset.UtcNow;

        return rows
            .Where(row => row.Subscriptions?.Any(subscription => IsCurrentlyActiveSubscription(subscription, nowUtc)) == true)
            .Select(row => row.SubscriberId)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
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

    private static object BuildCharacterUnlockNotificationPayload(
        string subscriberId,
        StoryCharacterItem character,
        int unlockCount) =>
        new
        {
            subscriber_id = subscriberId,
            notification_type = "character_unlock",
            source_key = BuildCharacterUnlockSourceKey(character.CharacterId, unlockCount),
            title = "Nuwe karakter oopgesluit",
            body = $"{character.DisplayName} is nou beskikbaar. Tik om die karakterprofiel te sien.",
            image_path = ResolveNotificationImagePath(character),
            image_alt = $"Illustrasie van {character.DisplayName}",
            href = $"/karakters?karakter={Uri.EscapeDataString(character.Slug)}",
            metadata = new
            {
                character_id = character.CharacterId,
                character_slug = character.Slug,
                character_name = character.DisplayName,
                unlock_count = unlockCount
            }
        };

    private static object BuildCharacterUnlockStatePayload(
        string subscriberId,
        Guid characterId,
        bool isUnlocked,
        int unlockCount,
        DateTimeOffset updatedAtUtc) =>
        new
        {
            subscriber_id = subscriberId,
            character_id = characterId,
            is_unlocked = isUnlocked,
            unlock_count = Math.Max(0, unlockCount),
            updated_at = updatedAtUtc
        };

    private static string BuildCharacterUnlockSourceKey(Guid characterId, int unlockCount)
    {
        var normalizedUnlockCount = Math.Max(1, unlockCount);
        return $"{CharacterUnlockSourceKeyPrefix}{characterId:N}-{normalizedUnlockCount}";
    }

    private static bool TryParseCharacterUnlockSourceKey(string? sourceKey, out Guid characterId, out int unlockCount)
    {
        characterId = Guid.Empty;
        unlockCount = 0;

        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return false;
        }

        var trimmed = sourceKey.Trim();
        if (!trimmed.StartsWith(CharacterUnlockSourceKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = trimmed[CharacterUnlockSourceKeyPrefix.Length..];
        if (remainder.Length < 32)
        {
            return false;
        }

        var dashIndex = remainder.IndexOf('-');
        var characterIdPart = dashIndex >= 0
            ? remainder[..dashIndex]
            : remainder;
        if (!Guid.TryParseExact(characterIdPart, "N", out characterId))
        {
            return false;
        }

        unlockCount = 1;
        if (dashIndex >= 0)
        {
            var countPart = remainder[(dashIndex + 1)..];
            if (int.TryParse(countPart, out var parsedCount) && parsedCount > 0)
            {
                unlockCount = parsedCount;
            }
        }

        return true;
    }

    private static string BuildPublishedBlogSourceKey(Guid postId) =>
        $"blog-published-{postId:N}";

    private static string BuildPublishedResourceDocumentSourceKey(Guid resourceDocumentId) =>
        $"resource-document-published-{resourceDocumentId:N}";

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

    private static string ResolveBlogNotificationImagePath(PublishedBlogNotificationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FeaturedImageUrl))
        {
            return request.FeaturedImageUrl.Trim();
        }

        return "/branding/schink-logo-green.png";
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

    private static string ResolveResourceDocumentNotificationImagePath(PublishedResourceDocumentNotificationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.PreviewImageUrl))
        {
            return request.PreviewImageUrl.Trim();
        }

        return "/branding/schink-logo-green.png";
    }

    private static string BuildPublishedStoryNotificationHref(string slug, string accessLevel) =>
        string.Equals(accessLevel, "free", StringComparison.OrdinalIgnoreCase)
            ? $"/luister/{Uri.EscapeDataString(slug)}"
            : $"/luister/{Uri.EscapeDataString(slug)}";

    private static string BuildPublishedBlogNotificationHref(string slug) =>
        $"/blog/{Uri.EscapeDataString(slug)}";

    private static string BuildPublishedResourceDocumentNotificationHref(string resourceTypeSlug) =>
        $"/resources?tipe={Uri.EscapeDataString(resourceTypeSlug)}";

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
            NormalizeNotificationHref(row.NotificationType, row.Href),
            row.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            row.ReadAtUtc is not null,
            row.ClearedAtUtc is not null);

    private static string? NormalizeNotificationHref(string? notificationType, string? href)
    {
        if (string.Equals(notificationType, "character_unlock", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeCharacterUnlockHref(href);
        }

        return href;
    }

    private static string NormalizeCharacterUnlockHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return "/karakters";
        }

        var trimmed = href.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            trimmed = absoluteUri.PathAndQuery;
        }

        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = $"/{trimmed.TrimStart('/')}";
        }

        if (trimmed.Equals("/karakters", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/karakters?", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/karakters#", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var pathOnly = trimmed.Split('?', '#')[0].Trim('/');
        var segments = pathOnly.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length >= 2 &&
            IsCharacterRouteSegment(segments[0]) &&
            !string.IsNullOrWhiteSpace(segments[1]))
        {
            return $"/karakters?karakter={Uri.EscapeDataString(segments[1])}";
        }

        return "/karakters";
    }

    private static bool IsCharacterRouteSegment(string value) =>
        value.Equals("karakter", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("karakters", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("character", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("characters", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseContentRangeCount(HttpResponseMessage response, out int count)
    {
        count = 0;
        if (!response.Headers.TryGetValues("Content-Range", out var values) &&
            !response.Content.Headers.TryGetValues("Content-Range", out values))
        {
            return false;
        }

        var contentRange = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(contentRange))
        {
            return false;
        }

        var slashIndex = contentRange.LastIndexOf('/');
        if (slashIndex < 0 || slashIndex >= contentRange.Length - 1)
        {
            return false;
        }

        return int.TryParse(contentRange[(slashIndex + 1)..], out count);
    }

    private static bool IsCurrentlyActiveSubscription(NotificationSubscriptionStatusRow row, DateTimeOffset nowUtc)
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

    private static readonly UserNotificationPageResult EmptyNotificationPageResult =
        new([], 0, false, false);

    private sealed class SubscriberLookupRow
    {
        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; init; }
    }

    private sealed class ActiveSubscriberLookupRow
    {
        [JsonPropertyName("subscriber_id")]
        public string? SubscriberId { get; init; }

        [JsonPropertyName("subscriptions")]
        public List<NotificationSubscriptionStatusRow>? Subscriptions { get; init; }
    }

    private sealed class NotificationSubscriptionStatusRow
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; init; }

        [JsonPropertyName("cancelled_at")]
        public DateTimeOffset? CancelledAt { get; init; }
    }

    private sealed class NotificationSourceKeyRow
    {
        [JsonPropertyName("source_key")]
        public string? SourceKey { get; init; }
    }

    private sealed class CharacterUnlockStateRow
    {
        [JsonPropertyName("character_id")]
        public Guid CharacterId { get; init; }

        [JsonPropertyName("is_unlocked")]
        public bool IsUnlocked { get; init; }

        [JsonPropertyName("unlock_count")]
        public int UnlockCount { get; init; }
    }

    private sealed record CharacterUnlockTrackedState(
        bool IsUnlocked,
        int UnlockCount);

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

        [JsonPropertyName("cleared_at")]
        public DateTimeOffset? ClearedAtUtc { get; init; }
    }

    private sealed class InsertedNotificationRow
    {
        [JsonPropertyName("notification_id")]
        public Guid NotificationId { get; init; }
    }
}
