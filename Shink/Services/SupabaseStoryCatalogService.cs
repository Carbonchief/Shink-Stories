using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed class SupabaseStoryCatalogService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    ILogger<SupabaseStoryCatalogService> logger) : IStoryCatalogService
{
    private const string CacheKey = "stories:catalog:v2";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FavouritesCacheDuration = TimeSpan.FromSeconds(30);
    private const int SoundbiteMaxDurationSeconds = 60;
    private const string FavouritesSystemKey = "favourites";
    private static readonly string[] FavouriteTableNames = ["story_favorites", "story_favourites"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] KleuterStoryTitlesInOrder =
    [
        "Tiekie Tik Tik Tok",
        "Die Kwaaibok se Klip",
        "Rammetjie Uitnek",
        "Die Kwaai Grommel",
        "Hailey Hasie se Groentetuin",
        "Maniere wys jou spiere",
        "Seekoei Sluit sy mond toe",
        "Koalabeertjie Klou",
        "Robot doen reg",
        "Babbelbessie",
        "Dankie en die mislike skree",
        "Dankie en die lelike praat",
        "Fantjie Leer Skryf"
    ];

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<SupabaseStoryCatalogService> _logger = logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<IReadOnlyList<StoryItem>> GetFreeStoriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await GetPublishedRowsAsync(cancellationToken);
        return rows
            .Where(row => string.Equals(row.AccessLevel, "free", StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Select(MapToStoryItem)
            .ToArray();
    }

    public async Task<IReadOnlyList<StoryItem>> GetLuisterStoriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await GetPublishedRowsAsync(cancellationToken);
        return rows
            .Where(IsLuisterStoryRow)
            .OrderByDescending(row => row.PublishedAt)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Select(MapToStoryItem)
            .ToArray();
    }

    public async Task<IReadOnlyList<StoryPlaylist>> GetLuisterPlaylistsAsync(string? userEmail = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetCatalogSnapshotAsync(cancellationToken);
        var normalizedEmail = NormalizeUserEmail(userEmail);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return snapshot.LuisterPlaylists;
        }

        return await BuildPersonalizedPlaylistsAsync(snapshot, normalizedEmail, cancellationToken);
    }

    public async Task<IReadOnlyList<StoryPreviewItem>> GetNewestTop10Async(CancellationToken cancellationToken = default)
    {
        var rows = await GetPublishedRowsAsync(cancellationToken);
        return BuildNewestTopRows(rows, 10)
            .Select(row =>
            {
                var story = MapToStoryItem(row);
                return new StoryPreviewItem(
                    Title: story.Title,
                    CoverPath: story.ImagePath,
                    LinkPath: $"/luister/{Uri.EscapeDataString(story.Slug)}");
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<StoryPreviewItem>> GetBibleStoriesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await GetPublishedRowsAsync(cancellationToken);

        return rows
            .Where(IsSubscriberStoryRow)
            .Where(IsBibleStoryRow)
            .OrderByDescending(row => row.PublishedAt)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(row =>
            {
                var story = MapToStoryItem(row);
                return new StoryPreviewItem(
                    Title: story.Title,
                    CoverPath: story.ImagePath,
                    LinkPath: $"/luister/{Uri.EscapeDataString(story.Slug)}");
            })
            .ToArray();
    }

    public async Task<StoryItem?> FindFreeBySlugAsync(string? slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var rows = await GetPublishedRowsAsync(cancellationToken);
        var row = rows.FirstOrDefault(candidate =>
            string.Equals(candidate.AccessLevel, "free", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Slug, slug, StringComparison.OrdinalIgnoreCase));

        return row is null ? null : MapToStoryItem(row);
    }

    public async Task<StoryItem?> FindLuisterBySlugAsync(string? slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var rows = await GetPublishedRowsAsync(cancellationToken);
        var row = rows.FirstOrDefault(candidate =>
            IsLuisterStoryRow(candidate) &&
            string.Equals(candidate.Slug, slug, StringComparison.OrdinalIgnoreCase));

        return row is null ? null : MapToStoryItem(row);
    }

    public async Task<StoryItem?> FindAnyBySlugAsync(string? slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var rows = await GetPublishedRowsAsync(cancellationToken);
        var row = rows.FirstOrDefault(candidate =>
            string.Equals(candidate.Slug, slug, StringComparison.OrdinalIgnoreCase));

        return row is null ? null : MapToStoryItem(row);
    }

    private async Task<IReadOnlyList<StoryCatalogRow>> GetPublishedRowsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetCatalogSnapshotAsync(cancellationToken);
        return snapshot.StoryRows;
    }

    private async Task<StoryCatalogSnapshot> GetCatalogSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(CacheKey, out StoryCatalogSnapshot? cachedSnapshot) &&
            cachedSnapshot is not null)
        {
            return cachedSnapshot;
        }

        await _refreshLock.WaitAsync(CancellationToken.None);
        try
        {
            if (_memoryCache.TryGetValue(CacheKey, out cachedSnapshot) &&
                cachedSnapshot is not null)
            {
                return cachedSnapshot;
            }

            var snapshot = await FetchCatalogSnapshotAsync(CancellationToken.None);
            _memoryCache.Set(CacheKey, snapshot, CacheDuration);
            return snapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<StoryCatalogSnapshot> FetchCatalogSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase stories lookup skipped: URL is not configured.");
            var fallbackRows = BuildLegacyFallbackRows();
            return new StoryCatalogSnapshot(
                StoryRows: fallbackRows,
                PlaylistRows: Array.Empty<StoryPlaylistRow>(),
                PlaylistItemRows: Array.Empty<StoryPlaylistItemRow>(),
                LuisterPlaylists: BuildDefaultLuisterPlaylists(fallbackRows));
        }

        var apiKey = ResolveReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase stories lookup skipped: AnonKey is not configured.");
            var fallbackRows = BuildLegacyFallbackRows();
            return new StoryCatalogSnapshot(
                StoryRows: fallbackRows,
                PlaylistRows: Array.Empty<StoryPlaylistRow>(),
                PlaylistItemRows: Array.Empty<StoryPlaylistItemRow>(),
                LuisterPlaylists: BuildDefaultLuisterPlaylists(fallbackRows));
        }

        var rows = await FetchPublishedRowsAsync(baseUri, apiKey, cancellationToken);
        var playlistRows = await FetchStoryPlaylistRowsAsync(baseUri, apiKey, cancellationToken);
        var playlistItemRows = await FetchStoryPlaylistItemRowsAsync(baseUri, apiKey, cancellationToken);
        var playlists = BuildLuisterPlaylistsFromConfiguredTables(rows, playlistRows, playlistItemRows);
        if (playlists.Count == 0)
        {
            playlists = BuildDefaultLuisterPlaylists(rows);
        }

        return new StoryCatalogSnapshot(
            StoryRows: rows,
            PlaylistRows: playlistRows,
            PlaylistItemRows: playlistItemRows,
            LuisterPlaylists: playlists);
    }

    private async Task<IReadOnlyList<StoryCatalogRow>> FetchPublishedRowsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(
            baseUri,
            "rest/v1/stories" +
            "?select=story_id,slug,title,summary,description,cover_image_path,thumbnail_image_path,audio_provider,audio_bucket,audio_object_key,audio_content_type,access_level,status,sort_order,published_at,duration_seconds,tags,metadata" +
            "&status=eq.published" +
            "&order=published_at.desc.nullslast" +
            "&order=sort_order.asc");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase stories lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return BuildLegacyFallbackRows();
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoryCatalogRow>>(responseStream, JsonOptions, cancellationToken)
                ?? [];

            return rows
                .Where(IsPublishedAndUsable)
                .OrderByDescending(row => row.PublishedAt)
                .ThenBy(row => row.SortOrder)
                .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase stories lookup failed unexpectedly. Falling back to legacy in-memory catalog.");
            return BuildLegacyFallbackRows();
        }
    }

    private async Task<IReadOnlyList<StoryPlaylistRow>> FetchStoryPlaylistRowsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(
            baseUri,
            "rest/v1/story_playlists" +
            "?select=playlist_id,slug,title,playlist_type,system_key,description,sort_order,max_items,is_enabled,show_on_home,show_showcase_image_on_luister_page,logo_image_path,backdrop_image_path" +
            "&is_enabled=eq.true" +
            "&order=sort_order.asc" +
            "&order=title.asc");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Supabase playlist lookup skipped: table story_playlists is not available yet.");
                return Array.Empty<StoryPlaylistRow>();
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase playlists lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return Array.Empty<StoryPlaylistRow>();
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoryPlaylistRow>>(responseStream, JsonOptions, cancellationToken)
                ?? [];

            return rows
                .Where(row => row.PlaylistId != Guid.Empty)
                .Where(row => row.IsEnabled)
                .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
                .Where(row => !string.IsNullOrWhiteSpace(row.Title))
                .OrderBy(row => row.SortOrder)
                .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase playlists lookup failed unexpectedly.");
            return Array.Empty<StoryPlaylistRow>();
        }
    }

    private async Task<IReadOnlyList<StoryPlaylistItemRow>> FetchStoryPlaylistItemRowsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(
            baseUri,
            "rest/v1/story_playlist_items" +
            "?select=playlist_id,story_id,sort_order,is_showcase" +
            "&order=sort_order.asc");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Supabase playlist item lookup skipped: table story_playlist_items is not available yet.");
                return Array.Empty<StoryPlaylistItemRow>();
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase playlist item lookup failed. Status={StatusCode} Body={Body}",
                    (int)response.StatusCode,
                    body);
                return Array.Empty<StoryPlaylistItemRow>();
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoryPlaylistItemRow>>(responseStream, JsonOptions, cancellationToken)
                ?? [];

            return rows
                .Where(row => row.PlaylistId != Guid.Empty)
                .Where(row => row.StoryId != Guid.Empty)
                .OrderBy(row => row.SortOrder)
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase playlist item lookup failed unexpectedly.");
            return Array.Empty<StoryPlaylistItemRow>();
        }
    }

    private async Task<IReadOnlyList<StoryPlaylist>> BuildPersonalizedPlaylistsAsync(
        StoryCatalogSnapshot snapshot,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var favouritesConfig = snapshot.PlaylistRows
            .FirstOrDefault(IsFavouritesSystemPlaylist);
        if (favouritesConfig is null)
        {
            return snapshot.LuisterPlaylists;
        }

        var favouriteStorySlugs = await FetchUserFavouriteStorySlugsAsync(normalizedEmail, cancellationToken);
        var luisterStoriesBySlug = snapshot.StoryRows
            .Where(IsLuisterStoryRow)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .GroupBy(row => row.Slug.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var favouriteStories = favouriteStorySlugs
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(slug => luisterStoriesBySlug.TryGetValue(slug, out var row) ? MapToStoryItem(row) : null)
            .Where(story => story is not null)
            .Cast<StoryItem>();

        var limitedStories = favouritesConfig.MaxItems is > 0
            ? favouriteStories.Take(favouritesConfig.MaxItems.Value).ToArray()
            : favouriteStories.ToArray();

        var favouritesPlaylist = new StoryPlaylist(
            Slug: favouritesConfig.Slug.Trim(),
            Title: favouritesConfig.Title.Trim(),
            Description: NormalizeOptionalText(favouritesConfig.Description),
            SortOrder: favouritesConfig.SortOrder,
            Stories: limitedStories,
            ShowOnHome: favouritesConfig.ShowOnHome,
            ShowShowcaseImageOnLuisterPage: favouritesConfig.ShowShowcaseImageOnLuisterPage,
            LogoImagePath: NormalizeOptionalText(favouritesConfig.LogoImagePath),
            BackdropImagePath: NormalizeOptionalText(favouritesConfig.BackdropImagePath),
            IsSystemPlaylist: true,
            SystemKey: NormalizeSystemKey(favouritesConfig.SystemKey),
            ShowcaseStorySlug: null);

        return snapshot.LuisterPlaylists
            .Where(playlist => !(playlist.IsSystemPlaylist &&
                                 string.Equals(NormalizeSystemKey(playlist.SystemKey), FavouritesSystemKey, StringComparison.Ordinal)))
            .Append(favouritesPlaylist)
            .OrderBy(playlist => playlist.SortOrder)
            .ThenBy(playlist => playlist.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> FetchUserFavouriteStorySlugsAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var cacheKey = $"{CacheKey}:favourites:{normalizedEmail}";
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<string>? cachedSlugs) &&
            cachedSlugs is not null)
        {
            return cachedSlugs;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return Array.Empty<string>();
        }

        var apiKey = ResolveServiceRoleKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<string>();
        }

        var subscriberId = await ResolveSubscriberIdAsync(baseUri, apiKey, normalizedEmail, cancellationToken);
        if (subscriberId is null)
        {
            var noSubscriber = Array.Empty<string>();
            _memoryCache.Set(cacheKey, noSubscriber, FavouritesCacheDuration);
            return noSubscriber;
        }

        var favouriteRows = await FetchFavouriteRowsAsync(baseUri, apiKey, subscriberId.Value, cancellationToken);
        var favouriteSlugs = favouriteRows
            .Where(row => !string.IsNullOrWhiteSpace(row.StorySlug))
            .OrderByDescending(row => row.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(row => row.StorySlug.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _memoryCache.Set(cacheKey, favouriteSlugs, FavouritesCacheDuration);
        return favouriteSlugs;
    }

    private async Task<Guid?> ResolveSubscriberIdAsync(
        Uri baseUri,
        string apiKey,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var requestUri = new Uri(
            baseUri,
            "rest/v1/subscribers" +
            "?select=subscriber_id" +
            $"&email=eq.{escapedEmail}" +
            "&limit=1");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<SubscriberLookupRow>>(stream, JsonOptions, cancellationToken)
                ?? [];

            return rows
                .Select(row => row.SubscriberId)
                .FirstOrDefault(id => id != Guid.Empty);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase subscriber lookup for favourites failed unexpectedly.");
            return null;
        }
    }

    private async Task<IReadOnlyList<StoryFavouriteRow>> FetchFavouriteRowsAsync(
        Uri baseUri,
        string apiKey,
        Guid subscriberId,
        CancellationToken cancellationToken)
    {
        var escapedSubscriberId = Uri.EscapeDataString(subscriberId.ToString("D"));
        var combinedRows = new List<StoryFavouriteRow>();

        foreach (var tableName in FavouriteTableNames)
        {
            var requestUri = new Uri(
                baseUri,
                $"rest/v1/{tableName}" +
                "?select=story_slug,created_at" +
                $"&subscriber_id=eq.{escapedSubscriberId}" +
                "&order=created_at.desc" +
                "&limit=5000");

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.TryAddWithoutValidation("apikey", apiKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogInformation(
                        "Supabase favourites lookup skipped: table {TableName} is not available yet.",
                        tableName);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Supabase favourites lookup failed for {TableName}. Status={StatusCode} Body={Body}",
                        tableName,
                        (int)response.StatusCode,
                        body);
                    continue;
                }

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var rows = await JsonSerializer.DeserializeAsync<List<StoryFavouriteRow>>(responseStream, JsonOptions, cancellationToken)
                    ?? [];

                if (rows.Count > 0)
                {
                    combinedRows.AddRange(rows);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(
                    exception,
                    "Supabase favourites lookup failed unexpectedly for {TableName}.",
                    tableName);
            }
        }

        return combinedRows
            .OrderByDescending(row => row.CreatedAt ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = default!;
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var parsedUri) ||
            parsedUri is null)
        {
            return false;
        }

        baseUri = parsedUri;
        return true;
    }

    private string ResolveReadApiKey() => _options.AnonKey;

    private string ResolveServiceRoleKey() => _options.ServiceRoleKey;

    private static string? NormalizeUserEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static bool IsFavouritesSystemPlaylist(StoryPlaylistRow row) =>
        row.IsEnabled &&
        row.IsSystemPlaylist &&
        string.Equals(NormalizeSystemKey(row.SystemKey), FavouritesSystemKey, StringComparison.Ordinal);

    private static string? NormalizeSystemKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static bool IsPublishedAndUsable(StoryCatalogRow row) =>
        row.StoryId != Guid.Empty &&
        string.Equals(row.Status, "published", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(row.Slug) &&
        !string.IsNullOrWhiteSpace(row.Title) &&
        !string.IsNullOrWhiteSpace(row.AudioObjectKey);

    private static bool IsSubscriberStoryRow(StoryCatalogRow row) =>
        string.Equals(row.AccessLevel, "subscriber", StringComparison.OrdinalIgnoreCase) &&
        !IsSoundbiteRow(row);

    private static bool IsLuisterStoryRow(StoryCatalogRow row) =>
        (string.Equals(row.AccessLevel, "subscriber", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(row.AccessLevel, "free", StringComparison.OrdinalIgnoreCase)) &&
        !IsSoundbiteRow(row);

    private static bool IsSoundbiteRow(StoryCatalogRow row)
    {
        if (row.DurationSeconds is > 0 and <= SoundbiteMaxDurationSeconds)
        {
            return true;
        }

        if (IsSoundbiteAudioObjectKey(row.AudioObjectKey))
        {
            return true;
        }

        return MetadataSuggestsSoundbite(row.Metadata);
    }

    private static bool IsSoundbiteAudioObjectKey(string? audioObjectKey)
    {
        if (string.IsNullOrWhiteSpace(audioObjectKey))
        {
            return false;
        }

        var normalized = audioObjectKey.Replace('\\', '/').Trim();
        return normalized.StartsWith("imported/soundbites/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("imported/non-story-audio/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBibleStoryRow(StoryCatalogRow row)
    {
        if (ContainsBybelKeyword(row.AudioObjectKey) ||
            ContainsBybelKeyword(row.Title) ||
            ContainsBybelKeyword(row.Slug))
        {
            return true;
        }

        if (row.Tags is not null &&
            row.Tags.Any(tag => string.Equals(tag, "bybel", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(tag, "byble", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (row.Metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return false;
        }

        var raw = row.Metadata.GetRawText();
        return raw.Contains("bybel", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("byble", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsBybelKeyword(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("bybel", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("byble", StringComparison.OrdinalIgnoreCase));

    private static bool MetadataSuggestsSoundbite(JsonElement metadata)
    {
        if (metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return false;
        }

        if (metadata.ValueKind == JsonValueKind.Object &&
            metadata.TryGetProperty("soundbite", out var soundbiteFlag) &&
            soundbiteFlag.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        return metadata.GetRawText().Contains("soundbite", StringComparison.OrdinalIgnoreCase);
    }

    private static StoryItem MapToStoryItem(StoryCatalogRow row)
    {
        var title = row.Title.Trim();
        var storyDetails = ReadStoryDetails(row.Metadata);
        var summary = !string.IsNullOrWhiteSpace(row.Summary)
            ? row.Summary.Trim()
            : storyDetails.Synopsis;

        var description = !string.IsNullOrWhiteSpace(row.Description)
            ? row.Description.Trim()
            : !string.IsNullOrWhiteSpace(summary)
                ? summary
                : $"Luister na {title} op Schink Stories.";

        var imageFileName = !string.IsNullOrWhiteSpace(row.CoverImagePath)
            ? row.CoverImagePath.Trim()
            : "/branding/schink-logo-green.png";

        var thumbnailFileName = !string.IsNullOrWhiteSpace(row.ThumbnailImagePath)
            ? row.ThumbnailImagePath.Trim()
            : imageFileName;

        return new StoryItem(
            Slug: row.Slug.Trim(),
            Title: title,
            Description: description,
            ImageFileName: imageFileName,
            AudioFileName: row.AudioObjectKey.Trim(),
            ThumbnailFileName: thumbnailFileName,
            AudioProvider: string.IsNullOrWhiteSpace(row.AudioProvider) ? "local" : row.AudioProvider.Trim(),
            AudioBucket: string.IsNullOrWhiteSpace(row.AudioBucket) ? null : row.AudioBucket.Trim(),
            AudioContentType: string.IsNullOrWhiteSpace(row.AudioContentType) ? null : row.AudioContentType.Trim(),
            AccessLevel: string.IsNullOrWhiteSpace(row.AccessLevel) ? "subscriber" : row.AccessLevel.Trim().ToLowerInvariant(),
            Summary: summary,
            Lessons: storyDetails.Lessons,
            ValueTags: storyDetails.Values,
            ConversationQuestions: storyDetails.ConversationQuestions,
            Characters: storyDetails.Characters);
    }

    private static StoryDetails ReadStoryDetails(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty("story_details", out var storyDetailsNode) ||
            storyDetailsNode.ValueKind != JsonValueKind.Object)
        {
            return StoryDetails.Empty;
        }

        var values = ReadMetadataStringArray(storyDetailsNode, "values");
        if (values.Count == 0)
        {
            values = ReadMetadataStringArray(storyDetailsNode, "value_tags");
        }

        return new StoryDetails(
            Synopsis: ReadMetadataString(storyDetailsNode, "synopsis"),
            Lessons: ReadMetadataStringArray(storyDetailsNode, "lessons"),
            Values: values,
            ConversationQuestions: ReadMetadataStringArray(storyDetailsNode, "conversation_questions"),
            Characters: ReadMetadataStringArray(storyDetailsNode, "characters"));
    }

    private static string? ReadMetadataString(JsonElement node, string propertyName)
    {
        if (node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyList<string> ReadMetadataStringArray(JsonElement node, string propertyName)
    {
        if (node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values.Count == 0 ? Array.Empty<string>() : values;
    }

    private static IReadOnlyList<StoryPlaylist> BuildLuisterPlaylistsFromConfiguredTables(
        IReadOnlyList<StoryCatalogRow> storyRows,
        IReadOnlyList<StoryPlaylistRow> playlistRows,
        IReadOnlyList<StoryPlaylistItemRow> playlistItemRows)
    {
        var luisterRows = storyRows
            .Where(IsLuisterStoryRow)
            .ToArray();

        if (luisterRows.Length == 0)
        {
            return Array.Empty<StoryPlaylist>();
        }

        var storiesById = luisterRows
            .GroupBy(row => row.StoryId)
            .ToDictionary(group => group.Key, group => group.First());

        var enabledPlaylistRows = playlistRows
            .Where(row => row.IsEnabled)
            .ToArray();

        var playlistRowsById = enabledPlaylistRows
            .Where(row => row.PlaylistId != Guid.Empty)
            .GroupBy(row => row.PlaylistId)
            .ToDictionary(group => group.Key, group => group.First());

        var normalizedItemRows = playlistItemRows
            .Where(item => playlistRowsById.ContainsKey(item.PlaylistId))
            .OrderBy(item => item.SortOrder)
            .ToArray();

        var playlistItemsByPlaylistId = normalizedItemRows
            .GroupBy(item => item.PlaylistId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var assignedStoryIds = new HashSet<Guid>();
        var playlists = new List<StoryPlaylist>();
        StoryPlaylistRow? allStoriesConfig = null;

        foreach (var playlistRow in enabledPlaylistRows
                     .OrderBy(row => row.SortOrder)
                     .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (IsFavouritesSystemPlaylist(playlistRow))
            {
                continue;
            }

            if (string.Equals(playlistRow.Slug, "all-stories", StringComparison.OrdinalIgnoreCase))
            {
                allStoriesConfig = playlistRow;
                continue;
            }

            if (!playlistItemsByPlaylistId.TryGetValue(playlistRow.PlaylistId, out var items) ||
                items.Length == 0)
            {
                continue;
            }

            var playlistStoryIds = new HashSet<Guid>();
            var mappedStories = new List<StoryItem>();
            foreach (var item in items)
            {
                if (!playlistStoryIds.Add(item.StoryId) ||
                    !storiesById.TryGetValue(item.StoryId, out var storyRow))
                {
                    continue;
                }

                mappedStories.Add(MapToStoryItem(storyRow));
                assignedStoryIds.Add(storyRow.StoryId);
            }

            if (mappedStories.Count == 0)
            {
                continue;
            }

            var limitedStories = playlistRow.MaxItems is > 0
                ? mappedStories.Take(playlistRow.MaxItems.Value).ToArray()
                : mappedStories.ToArray();

            string? showcaseStorySlug = null;
            var showcaseItem = items.FirstOrDefault(item => item.IsShowcase);
            if (showcaseItem is not null &&
                storiesById.TryGetValue(showcaseItem.StoryId, out var showcaseStoryRow) &&
                !string.IsNullOrWhiteSpace(showcaseStoryRow.Slug))
            {
                showcaseStorySlug = showcaseStoryRow.Slug.Trim();
            }
            playlists.Add(new StoryPlaylist(
                Slug: playlistRow.Slug.Trim(),
                Title: playlistRow.Title.Trim(),
                Description: NormalizeOptionalText(playlistRow.Description),
                SortOrder: playlistRow.SortOrder,
                Stories: limitedStories,
                ShowOnHome: playlistRow.ShowOnHome,
                ShowShowcaseImageOnLuisterPage: playlistRow.ShowShowcaseImageOnLuisterPage,
                LogoImagePath: NormalizeOptionalText(playlistRow.LogoImagePath),
                BackdropImagePath: NormalizeOptionalText(playlistRow.BackdropImagePath),
                IsSystemPlaylist: playlistRow.IsSystemPlaylist,
                SystemKey: NormalizeSystemKey(playlistRow.SystemKey),
                ShowcaseStorySlug: showcaseStorySlug));
        }

        var unassignedStories = luisterRows
            .Where(row => !assignedStoryIds.Contains(row.StoryId))
            .OrderByDescending(row => row.PublishedAt)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Select(MapToStoryItem)
            .ToArray();

        if (unassignedStories.Length > 0)
        {
            var allStoriesMaxItems = allStoriesConfig?.MaxItems;
            var allStoriesItems = allStoriesMaxItems is > 0
                ? unassignedStories.Take(allStoriesMaxItems.Value).ToArray()
                : unassignedStories;

            playlists.Add(new StoryPlaylist(
                Slug: string.IsNullOrWhiteSpace(allStoriesConfig?.Slug) ? "all-stories" : allStoriesConfig!.Slug.Trim(),
                Title: string.IsNullOrWhiteSpace(allStoriesConfig?.Title) ? "Alle stories" : allStoriesConfig!.Title.Trim(),
                Description: NormalizeOptionalText(allStoriesConfig?.Description) ?? "Stories wat nie in ander playlists is nie.",
                SortOrder: allStoriesConfig?.SortOrder ?? int.MaxValue,
                Stories: allStoriesItems,
                ShowOnHome: allStoriesConfig?.ShowOnHome ?? false,
                ShowShowcaseImageOnLuisterPage: allStoriesConfig?.ShowShowcaseImageOnLuisterPage ?? false,
                LogoImagePath: NormalizeOptionalText(allStoriesConfig?.LogoImagePath),
                BackdropImagePath: NormalizeOptionalText(allStoriesConfig?.BackdropImagePath),
                IsSystemPlaylist: allStoriesConfig?.IsSystemPlaylist ?? false,
                SystemKey: NormalizeSystemKey(allStoriesConfig?.SystemKey),
                ShowcaseStorySlug: null));
        }

        return playlists
            .OrderBy(playlist => playlist.SortOrder)
            .ThenBy(playlist => playlist.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<StoryPlaylist> BuildDefaultLuisterPlaylists(IReadOnlyList<StoryCatalogRow> storyRows)
    {
        var luisterRows = storyRows
            .Where(IsLuisterStoryRow)
            .ToArray();

        if (luisterRows.Length == 0)
        {
            return Array.Empty<StoryPlaylist>();
        }

        var assignedStoryIds = new HashSet<Guid>();
        var playlists = new List<StoryPlaylist>();

        var freeRows = luisterRows
            .Where(row => string.Equals(row.AccessLevel, "free", StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (freeRows.Length > 0)
        {
            playlists.Add(new StoryPlaylist(
                Slug: "gratis-stories",
                Title: "Gratis stories",
                Description: "Dis op die huis!",
                SortOrder: 10,
                Stories: freeRows.Select(MapToStoryItem).ToArray(),
                ShowOnHome: true,
                ShowcaseStorySlug: freeRows.Select(row => row.Slug.Trim()).FirstOrDefault()));

            assignedStoryIds.UnionWith(freeRows.Select(row => row.StoryId));
        }

        var newestRows = BuildNewestTopRows(storyRows, 10).ToArray();
        if (newestRows.Length > 0)
        {
            playlists.Add(new StoryPlaylist(
                Slug: "top-10-nuutste-stories",
                Title: "Top 10 nuutste stories",
                Description: "Kry 'n voorsmakie van ons nuutste uitgawes.",
                SortOrder: 20,
                Stories: newestRows.Select(MapToStoryItem).ToArray(),
                ShowOnHome: false,
                ShowcaseStorySlug: newestRows.Select(row => row.Slug.Trim()).FirstOrDefault()));

            assignedStoryIds.UnionWith(newestRows.Select(row => row.StoryId));
        }

        var kleuterRows = BuildKleuterRows(luisterRows).ToArray();
        if (kleuterRows.Length > 0)
        {
            playlists.Add(new StoryPlaylist(
                Slug: "stories-vir-kleuters",
                Title: "Stories vir Kleuters",
                Description: "Verken stories spesiaal vir kleuters.",
                SortOrder: 30,
                Stories: kleuterRows.Select(MapToStoryItem).ToArray(),
                ShowOnHome: false,
                ShowcaseStorySlug: kleuterRows.Select(row => row.Slug.Trim()).FirstOrDefault()));

            assignedStoryIds.UnionWith(kleuterRows.Select(row => row.StoryId));
        }

        var bibleRows = luisterRows
            .Where(IsSubscriberStoryRow)
            .Where(IsBibleStoryRow)
            .OrderByDescending(row => row.PublishedAt)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        if (bibleRows.Length > 0)
        {
            playlists.Add(new StoryPlaylist(
                Slug: "bybelstories",
                Title: "Bybelstories",
                Description: "Luister na ons Bybelstories vir kinders.",
                SortOrder: 40,
                Stories: bibleRows.Select(MapToStoryItem).ToArray(),
                ShowOnHome: false,
                ShowcaseStorySlug: bibleRows.Select(row => row.Slug.Trim()).FirstOrDefault()));

            assignedStoryIds.UnionWith(bibleRows.Select(row => row.StoryId));
        }

        var unassignedRows = luisterRows
            .Where(row => !assignedStoryIds.Contains(row.StoryId))
            .OrderByDescending(row => row.PublishedAt)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unassignedRows.Length > 0)
        {
            playlists.Add(new StoryPlaylist(
                Slug: "all-stories",
                Title: "Alle stories",
                Description: "Stories wat nie in ander playlists is nie.",
                SortOrder: 50,
                Stories: unassignedRows.Select(MapToStoryItem).ToArray(),
                ShowOnHome: false,
                ShowcaseStorySlug: unassignedRows.Select(row => row.Slug.Trim()).FirstOrDefault()));
        }

        return playlists
            .OrderBy(playlist => playlist.SortOrder)
            .ThenBy(playlist => playlist.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<StoryCatalogRow> BuildNewestTopRows(IReadOnlyList<StoryCatalogRow> rows, int maxCount)
    {
        return rows
            .Where(IsSubscriberStoryRow)
            .OrderByDescending(row => row.PublishedAt)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();
    }

    private static IReadOnlyList<StoryCatalogRow> BuildKleuterRows(IReadOnlyList<StoryCatalogRow> luisterRows)
    {
        var selectedRows = new List<StoryCatalogRow>();
        var selectedIds = new HashSet<Guid>();

        foreach (var preferredTitle in KleuterStoryTitlesInOrder)
        {
            var matchedRow = FindKleuterStoryRow(preferredTitle, luisterRows);
            if (matchedRow is null || !selectedIds.Add(matchedRow.StoryId))
            {
                continue;
            }

            selectedRows.Add(matchedRow);
        }

        return selectedRows;
    }

    private static StoryCatalogRow? FindKleuterStoryRow(string preferredTitle, IReadOnlyList<StoryCatalogRow> luisterRows)
    {
        var normalizedPreferred = NormalizeTitleKey(preferredTitle);
        if (string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            return null;
        }

        var exactMatch = luisterRows.FirstOrDefault(row =>
            string.Equals(NormalizeTitleKey(row.Title), normalizedPreferred, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        var containsMatch = luisterRows.FirstOrDefault(row =>
        {
            var normalizedCandidate = NormalizeTitleKey(row.Title);
            return normalizedCandidate.Contains(normalizedPreferred, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPreferred.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase);
        });
        if (containsMatch is not null)
        {
            return containsMatch;
        }

        var preferredTokens = normalizedPreferred
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (preferredTokens.Length == 0)
        {
            return null;
        }

        var requiredSharedTokens = Math.Min(2, preferredTokens.Length);
        return luisterRows
            .Select(row =>
            {
                var normalizedCandidate = NormalizeTitleKey(row.Title);
                var candidateTokens = normalizedCandidate
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var sharedTokenCount = preferredTokens.Count(token =>
                    candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));

                return new
                {
                    Row = row,
                    SharedTokenCount = sharedTokenCount,
                    LengthDelta = Math.Abs(normalizedCandidate.Length - normalizedPreferred.Length)
                };
            })
            .Where(match => match.SharedTokenCount >= requiredSharedTokens)
            .OrderByDescending(match => match.SharedTokenCount)
            .ThenBy(match => match.LengthDelta)
            .Select(match => match.Row)
            .FirstOrDefault();
    }

    private static string NormalizeTitleKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
                continue;
            }

            if (previousWasSpace)
            {
                continue;
            }

            builder.Append(' ');
            previousWasSpace = true;
        }

        return builder.ToString().Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static IReadOnlyList<StoryCatalogRow> BuildLegacyFallbackRows()
    {
        var previewOrderBySlug = StoryCatalog.NewestTop10
            .Select((preview, index) => new
            {
                Slug = TryParseSlugFromLinkPath(preview.LinkPath),
                SortOrder = index + 1
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Slug))
            .ToDictionary(
                item => item.Slug!,
                item => item.SortOrder,
                StringComparer.OrdinalIgnoreCase);

        var freeOrderBySlug = StoryCatalog.All
            .Select((story, index) => new
            {
                story.Slug,
                SortOrder = index + 1
            })
            .ToDictionary(item => item.Slug, item => item.SortOrder, StringComparer.OrdinalIgnoreCase);

        var rows = new List<StoryCatalogRow>();
        foreach (var story in StoryCatalog.LuisterStories)
        {
            var isFree = freeOrderBySlug.ContainsKey(story.Slug);
            var isTop10PreviewStory = previewOrderBySlug.TryGetValue(story.Slug, out var previewSortOrder);
            var sortOrder = isFree
                ? freeOrderBySlug[story.Slug]
                : isTop10PreviewStory
                    ? previewSortOrder
                    : 1_000;

            rows.Add(new StoryCatalogRow
            {
                StoryId = CreateDeterministicGuid($"story:{story.Slug}"),
                Slug = story.Slug,
                Title = story.Title,
                Summary = story.Description,
                Description = story.Description,
                CoverImagePath = story.ImageFileName,
                ThumbnailImagePath = story.ThumbnailFileName,
                AudioProvider = story.AudioProvider,
                AudioBucket = story.AudioBucket,
                AudioObjectKey = story.AudioFileName,
                AudioContentType = story.AudioContentType,
                AccessLevel = isFree ? "free" : "subscriber",
                Status = "published",
                SortOrder = sortOrder,
                PublishedAt = null,
                Tags = isFree ? ["free"] : ["subscriber"],
                Metadata = default
            });
        }

        return rows
            .GroupBy(row => row.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static Guid CreateDeterministicGuid(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string? TryParseSlugFromLinkPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("/luister/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var slug = value["/luister/".Length..].Trim('/');
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return Uri.UnescapeDataString(slug);
    }

    private sealed record StoryCatalogSnapshot(
        IReadOnlyList<StoryCatalogRow> StoryRows,
        IReadOnlyList<StoryPlaylistRow> PlaylistRows,
        IReadOnlyList<StoryPlaylistItemRow> PlaylistItemRows,
        IReadOnlyList<StoryPlaylist> LuisterPlaylists);

    private sealed record StoryDetails(
        string? Synopsis,
        IReadOnlyList<string> Lessons,
        IReadOnlyList<string> Values,
        IReadOnlyList<string> ConversationQuestions,
        IReadOnlyList<string> Characters)
    {
        public static StoryDetails Empty { get; } = new(
            Synopsis: null,
            Lessons: Array.Empty<string>(),
            Values: Array.Empty<string>(),
            ConversationQuestions: Array.Empty<string>(),
            Characters: Array.Empty<string>());
    }

    private sealed class StoryCatalogRow
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
        public string AudioObjectKey { get; set; } = string.Empty;

        [JsonPropertyName("audio_content_type")]
        public string? AudioContentType { get; set; }

        [JsonPropertyName("access_level")]
        public string AccessLevel { get; set; } = "subscriber";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "published";

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("duration_seconds")]
        public int? DurationSeconds { get; set; }

        [JsonPropertyName("tags")]
        public string[]? Tags { get; set; }

        [JsonPropertyName("metadata")]
        public JsonElement Metadata { get; set; }
    }

    private sealed class StoryPlaylistRow
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

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("max_items")]
        public int? MaxItems { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("show_on_home")]
        public bool ShowOnHome { get; set; }

        [JsonPropertyName("show_showcase_image_on_luister_page")]
        public bool ShowShowcaseImageOnLuisterPage { get; set; }

        [JsonPropertyName("logo_image_path")]
        public string? LogoImagePath { get; set; }

        [JsonPropertyName("backdrop_image_path")]
        public string? BackdropImagePath { get; set; }

        [JsonIgnore]
        public bool IsSystemPlaylist =>
            string.Equals(PlaylistType, "system", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StoryPlaylistItemRow
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

    private sealed class SubscriberLookupRow
    {
        [JsonPropertyName("subscriber_id")]
        public Guid SubscriberId { get; set; }
    }

    private sealed class StoryFavouriteRow
    {
        [JsonPropertyName("story_slug")]
        public string StorySlug { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }
    }
}
