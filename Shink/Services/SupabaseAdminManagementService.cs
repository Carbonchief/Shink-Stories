using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed partial class SupabaseAdminManagementService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    ILogger<SupabaseAdminManagementService> logger) : IAdminManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string StoryCatalogSnapshotCacheKey = "stories:catalog:v2";

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
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
            _logger.LogWarning("Admin lookup skipped: Supabase ServiceRoleKey is not configured.");
            return false;
        }

        return await IsAdminCoreAsync(baseUri, apiKey, email, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminSubscriberRecord>> GetSubscribersAsync(
        string? adminEmail,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!await TryResolveAdminContextAsync(adminEmail, cancellationToken))
        {
            return Array.Empty<AdminSubscriberRecord>();
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return Array.Empty<AdminSubscriberRecord>();
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<AdminSubscriberRecord>();
        }

        var subscribersTask = FetchSubscribersAsync(baseUri, apiKey, cancellationToken);
        var subscriptionsTask = FetchSubscriptionsAsync(baseUri, apiKey, cancellationToken);
        await Task.WhenAll(subscribersTask, subscriptionsTask);

        var activeTiersBySubscriber = BuildActiveTierMap(subscriptionsTask.Result);
        var subscriptionSummaryBySubscriber = BuildSubscriptionSummaryMap(subscriptionsTask.Result);
        var normalizedSearch = NormalizeSearchTerm(search);

        return subscribersTask.Result
            .Where(row => row.SubscriberId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Email))
            .Where(row => MatchesSubscriberSearch(row, normalizedSearch))
            .Select(row =>
            {
                var activeTierCodes = activeTiersBySubscriber.TryGetValue(row.SubscriberId, out var tierCodes)
                    ? tierCodes
                    : Array.Empty<string>();
                var subscriptionSummary = subscriptionSummaryBySubscriber.TryGetValue(row.SubscriberId, out var summary)
                    ? summary
                    : null;

                return new AdminSubscriberRecord(
                    SubscriberId: row.SubscriberId,
                    Email: row.Email.Trim().ToLowerInvariant(),
                    FirstName: NormalizeOptionalText(row.FirstName, 80),
                    LastName: NormalizeOptionalText(row.LastName, 80),
                    DisplayName: NormalizeOptionalText(row.DisplayName, 120),
                    MobileNumber: NormalizeOptionalText(row.MobileNumber, 32),
                    CreatedAt: row.CreatedAt,
                    UpdatedAt: row.UpdatedAt,
                    ActiveTierCodes: activeTierCodes,
                    PaymentProvider: subscriptionSummary?.PaymentProvider,
                    SubscriptionStatus: subscriptionSummary?.Status,
                    SubscribedAt: subscriptionSummary?.SubscribedAt,
                    NextPaymentDueAt: subscriptionSummary?.NextRenewalAt,
                    CancelledAt: subscriptionSummary?.CancelledAt);
            })
            .OrderByDescending(row => row.UpdatedAt)
            .ThenBy(row => row.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
            return new AdminOperationResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
        }

        var normalizedFirstName = NormalizeOptionalText(request.FirstName, 80);
        var normalizedLastName = NormalizeOptionalText(request.LastName, 80);
        var normalizedDisplayName = NormalizeOptionalText(request.DisplayName, 120);
        var normalizedMobileNumber = NormalizeMobileNumber(request.MobileNumber);
        if (!string.IsNullOrWhiteSpace(request.MobileNumber) && normalizedMobileNumber is null)
        {
            return new AdminOperationResult(false, "Selfoonnommer moet 7 tot 20 syfers wees.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["first_name"] = normalizedFirstName,
            ["last_name"] = normalizedLastName,
            ["display_name"] = normalizedDisplayName,
            ["mobile_number"] = normalizedMobileNumber
        };

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
                CoverImagePath: NormalizeOptionalText(row.CoverImagePath, 1024),
                ThumbnailImagePath: NormalizeOptionalText(row.ThumbnailImagePath, 1024),
                AudioProvider: string.IsNullOrWhiteSpace(row.AudioProvider) ? "local" : row.AudioProvider.Trim().ToLowerInvariant(),
                AudioBucket: NormalizeOptionalText(row.AudioBucket, 120),
                AudioObjectKey: NormalizeOptionalText(row.AudioObjectKey, 1024),
                AudioContentType: NormalizeOptionalText(row.AudioContentType, 100),
                AccessLevel: string.IsNullOrWhiteSpace(row.AccessLevel) ? "subscriber" : row.AccessLevel.Trim().ToLowerInvariant(),
                Status: string.IsNullOrWhiteSpace(row.Status) ? "draft" : row.Status.Trim().ToLowerInvariant(),
                IsFeatured: row.IsFeatured,
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
            return new AdminOperationResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["slug"] = normalizedSlug,
            ["title"] = normalizedTitle,
            ["summary"] = normalizedSummary,
            ["description"] = normalizedDescription,
            ["cover_image_path"] = normalizedCoverImagePath,
            ["thumbnail_image_path"] = normalizedThumbnailPath,
            ["audio_provider"] = normalizedAudioProvider,
            ["audio_bucket"] = normalizedAudioBucket,
            ["audio_object_key"] = normalizedAudioObjectKey,
            ["audio_content_type"] = normalizedAudioContentType,
            ["access_level"] = normalizedAccessLevel,
            ["status"] = normalizedStatus,
            ["is_featured"] = request.IsFeatured,
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
            return new AdminOperationResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["slug"] = normalizedSlug,
            ["title"] = normalizedTitle,
            ["summary"] = normalizedSummary,
            ["description"] = normalizedDescription,
            ["cover_image_path"] = normalizedCoverImagePath,
            ["thumbnail_image_path"] = normalizedThumbnailPath,
            ["audio_provider"] = "r2",
            ["audio_bucket"] = normalizedAudioBucket,
            ["audio_object_key"] = normalizedAudioObjectKey,
            ["audio_content_type"] = normalizedAudioContentType,
            ["access_level"] = normalizedAccessLevel,
            ["status"] = normalizedStatus,
            ["is_featured"] = request.IsFeatured,
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
                                SortOrder: item.SortOrder);
                        }

                        return new AdminPlaylistStoryItem(
                            StoryId: story.StoryId,
                            StorySlug: story.Slug?.Trim() ?? string.Empty,
                            StoryTitle: story.Title?.Trim() ?? story.Slug?.Trim() ?? $"Storie {story.StoryId:D}",
                            SortOrder: item.SortOrder);
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
                    LogoImagePath: NormalizePlaylistImagePath(row.LogoImagePath, StoryPlaylist.DefaultLogoImagePath),
                    BackdropImagePath: NormalizePlaylistImagePath(row.BackdropImagePath, StoryPlaylist.DefaultBackdropImagePath),
                    SortOrder: row.SortOrder,
                    MaxItems: row.MaxItems is > 0 ? row.MaxItems : null,
                    IsEnabled: row.IsEnabled,
                    ShowOnHome: row.ShowOnHome,
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
            return new AdminOperationResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
        }

        if (request.PlaylistId is null || request.PlaylistId == Guid.Empty)
        {
            var createPayload = new Dictionary<string, object?>
            {
                ["slug"] = normalizedSlug,
                ["title"] = normalizedTitle,
                ["description"] = NormalizeOptionalText(request.Description, 4000),
                ["logo_image_path"] = NormalizePlaylistImagePath(request.LogoImagePath, StoryPlaylist.DefaultLogoImagePath),
                ["backdrop_image_path"] = NormalizePlaylistImagePath(request.BackdropImagePath, StoryPlaylist.DefaultBackdropImagePath),
                ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000),
                ["max_items"] = request.MaxItems is > 0 ? request.MaxItems : null,
                ["is_enabled"] = request.IsEnabled,
                ["show_on_home"] = request.ShowOnHome,
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
                ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000),
                ["is_enabled"] = request.IsEnabled,
                ["show_on_home"] = request.ShowOnHome
            }
            : new Dictionary<string, object?>
            {
                ["slug"] = normalizedSlug,
                ["title"] = normalizedTitle,
                ["description"] = NormalizeOptionalText(request.Description, 4000),
                ["logo_image_path"] = NormalizePlaylistImagePath(request.LogoImagePath, StoryPlaylist.DefaultLogoImagePath),
                ["backdrop_image_path"] = NormalizePlaylistImagePath(request.BackdropImagePath, StoryPlaylist.DefaultBackdropImagePath),
                ["sort_order"] = Math.Clamp(request.SortOrder, -500_000, 500_000),
                ["max_items"] = request.MaxItems is > 0 ? request.MaxItems : null,
                ["is_enabled"] = request.IsEnabled,
                ["show_on_home"] = request.ShowOnHome
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
            return new AdminOperationResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
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
        IReadOnlyList<Guid> orderedStoryIds,
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
            return new AdminOperationResult(false, "Supabase ServiceRoleKey is nog nie opgestel nie.");
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

        var normalizedStoryIds = orderedStoryIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (normalizedStoryIds.Length == 0)
        {
            InvalidateStoryCatalogCache();
            return new AdminOperationResult(true);
        }

        var payload = normalizedStoryIds
            .Select((storyId, index) => new Dictionary<string, object?>
            {
                ["playlist_id"] = playlistId,
                ["story_id"] = storyId,
                ["sort_order"] = index + 1
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

    private async Task<IReadOnlyList<SubscriberRow>> FetchSubscribersAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscribers" +
            "?select=subscriber_id,email,first_name,last_name,display_name,mobile_number,created_at,updated_at" +
            "&order=updated_at.desc" +
            "&limit=1000");
        return await FetchRowsAsync<SubscriberRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<SubscriptionRow>> FetchSubscriptionsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/subscriptions" +
            "?select=subscriber_id,tier_code,provider,status,subscribed_at,next_renewal_at,cancelled_at" +
            "&order=subscribed_at.desc" +
            "&limit=5000");

        return await FetchRowsAsync<SubscriptionRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<StoryRow>> FetchStoriesAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/stories" +
            "?select=story_id,slug,title,summary,description,cover_image_path,thumbnail_image_path,audio_provider,audio_bucket,audio_object_key,audio_content_type,access_level,status,is_featured,sort_order,published_at,duration_seconds,updated_at" +
            "&order=updated_at.desc.nullslast" +
            "&order=sort_order.asc" +
            "&limit=2000");

        return await FetchRowsAsync<StoryRow>(uri, apiKey, cancellationToken);
    }

    private async Task<IReadOnlyList<PlaylistRow>> FetchPlaylistsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            baseUri,
            "rest/v1/story_playlists" +
            "?select=playlist_id,slug,title,playlist_type,system_key,description,logo_image_path,backdrop_image_path,sort_order,max_items,is_enabled,show_on_home,updated_at" +
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
            "?select=playlist_id,story_id,sort_order" +
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
            "?select=playlist_id,slug,title,playlist_type,system_key,description,logo_image_path,backdrop_image_path,sort_order,max_items,is_enabled,show_on_home,updated_at" +
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

    private string ResolveApiKey() => _options.ServiceRoleKey;

    private void InvalidateStoryCatalogCache()
    {
        _memoryCache.Remove(StoryCatalogSnapshotCacheKey);
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
                        NormalizeOptionalText(selected.Status, 40),
                        selected.SubscribedAt,
                        selected.NextRenewalAt,
                        selected.CancelledAt);
                });
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

    private static string NormalizePlaylistImagePath(string? value, string fallbackPath)
    {
        var normalized = NormalizeOptionalText(value, 1024);
        return string.IsNullOrWhiteSpace(normalized) ? fallbackPath : normalized;
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

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StorySlugRegex();

    [GeneratedRegex("^\\+?[0-9]{7,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex MobileNumberRegex();

    private sealed class AdminUserRow
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
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

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class SubscriptionRow
    {
        [JsonPropertyName("subscriber_id")]
        public Guid SubscriberId { get; set; }

        [JsonPropertyName("tier_code")]
        public string? TierCode { get; set; }

        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("subscribed_at")]
        public DateTimeOffset? SubscribedAt { get; set; }

        [JsonPropertyName("next_renewal_at")]
        public DateTimeOffset? NextRenewalAt { get; set; }

        [JsonPropertyName("cancelled_at")]
        public DateTimeOffset? CancelledAt { get; set; }
    }

    private sealed record SubscriptionSummary(
        string? PaymentProvider,
        string? Status,
        DateTimeOffset? SubscribedAt,
        DateTimeOffset? NextRenewalAt,
        DateTimeOffset? CancelledAt);

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

        [JsonPropertyName("is_featured")]
        public bool IsFeatured { get; set; }

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

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("max_items")]
        public int? MaxItems { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("show_on_home")]
        public bool ShowOnHome { get; set; }

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
}
