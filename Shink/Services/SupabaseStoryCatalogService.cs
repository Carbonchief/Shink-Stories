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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private const int SoundbiteMaxDurationSeconds = 60;
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

    public async Task<IReadOnlyList<StoryPlaylist>> GetLuisterPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetCatalogSnapshotAsync(cancellationToken);
        return snapshot.LuisterPlaylists;
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
                fallbackRows,
                BuildDefaultLuisterPlaylists(fallbackRows));
        }

        var apiKey = ResolveReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase stories lookup skipped: AnonKey is not configured.");
            var fallbackRows = BuildLegacyFallbackRows();
            return new StoryCatalogSnapshot(
                fallbackRows,
                BuildDefaultLuisterPlaylists(fallbackRows));
        }

        var rows = await FetchPublishedRowsAsync(baseUri, apiKey, cancellationToken);
        var playlists = await FetchLuisterPlaylistsAsync(baseUri, apiKey, rows, cancellationToken);
        if (playlists.Count == 0)
        {
            playlists = BuildDefaultLuisterPlaylists(rows);
        }

        return new StoryCatalogSnapshot(rows, playlists);
    }

    private async Task<IReadOnlyList<StoryCatalogRow>> FetchPublishedRowsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(
            baseUri,
            "rest/v1/stories" +
            "?select=story_id,slug,title,summary,description,cover_image_path,thumbnail_image_path,audio_provider,audio_bucket,audio_object_key,audio_content_type,access_level,status,is_featured,sort_order,published_at,duration_seconds,tags,metadata" +
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

    private async Task<IReadOnlyList<StoryPlaylist>> FetchLuisterPlaylistsAsync(
        Uri baseUri,
        string apiKey,
        IReadOnlyList<StoryCatalogRow> storyRows,
        CancellationToken cancellationToken)
    {
        var playlistRows = await FetchStoryPlaylistRowsAsync(baseUri, apiKey, cancellationToken);
        if (playlistRows.Count == 0)
        {
            return Array.Empty<StoryPlaylist>();
        }

        var playlistItemRows = await FetchStoryPlaylistItemRowsAsync(baseUri, apiKey, cancellationToken);
        return BuildLuisterPlaylistsFromConfiguredTables(storyRows, playlistRows, playlistItemRows);
    }

    private async Task<IReadOnlyList<StoryPlaylistRow>> FetchStoryPlaylistRowsAsync(Uri baseUri, string apiKey, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(
            baseUri,
            "rest/v1/story_playlists" +
            "?select=playlist_id,slug,title,description,sort_order,max_items,is_enabled,show_on_home,logo_image_path,backdrop_image_path" +
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
            "?select=playlist_id,story_id,sort_order" +
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
        var description = !string.IsNullOrWhiteSpace(row.Description)
            ? row.Description.Trim()
            : !string.IsNullOrWhiteSpace(row.Summary)
                ? row.Summary.Trim()
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
            AccessLevel: string.IsNullOrWhiteSpace(row.AccessLevel) ? "subscriber" : row.AccessLevel.Trim().ToLowerInvariant());
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

            playlists.Add(new StoryPlaylist(
                Slug: playlistRow.Slug.Trim(),
                Title: playlistRow.Title.Trim(),
                Description: NormalizeOptionalText(playlistRow.Description),
                SortOrder: playlistRow.SortOrder,
                Stories: limitedStories,
                ShowOnHome: playlistRow.ShowOnHome,
                LogoImagePath: NormalizeOptionalText(playlistRow.LogoImagePath),
                BackdropImagePath: NormalizeOptionalText(playlistRow.BackdropImagePath)));
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
                LogoImagePath: NormalizeOptionalText(allStoriesConfig?.LogoImagePath),
                BackdropImagePath: NormalizeOptionalText(allStoriesConfig?.BackdropImagePath)));
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
                ShowOnHome: true));

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
                ShowOnHome: false));

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
                ShowOnHome: false));

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
                ShowOnHome: false));

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
                ShowOnHome: false));
        }

        return playlists
            .OrderBy(playlist => playlist.SortOrder)
            .ThenBy(playlist => playlist.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<StoryCatalogRow> BuildNewestTopRows(IReadOnlyList<StoryCatalogRow> rows, int maxCount)
    {
        var luisterRows = rows
            .Where(IsSubscriberStoryRow)
            .ToArray();

        var featuredRows = luisterRows
            .Where(row => row.IsFeatured)
            .OrderBy(row => row.SortOrder)
            .ThenByDescending(row => row.PublishedAt)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (featuredRows.Count < maxCount)
        {
            var featuredIds = featuredRows
                .Select(row => row.StoryId)
                .ToHashSet();

            var remainder = luisterRows
                .Where(row => !featuredIds.Contains(row.StoryId))
                .OrderByDescending(row => row.PublishedAt)
                .ThenBy(row => row.SortOrder)
                .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase);

            foreach (var row in remainder)
            {
                if (featuredRows.Count >= maxCount)
                {
                    break;
                }

                featuredRows.Add(row);
            }
        }

        return featuredRows
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
        var featuredOrderBySlug = StoryCatalog.NewestTop10
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
            var isFeatured = featuredOrderBySlug.TryGetValue(story.Slug, out var featuredSortOrder);
            var sortOrder = isFree
                ? freeOrderBySlug[story.Slug]
                : isFeatured
                    ? featuredSortOrder
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
                IsFeatured = isFeatured,
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
        IReadOnlyList<StoryPlaylist> LuisterPlaylists);

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

        [JsonPropertyName("is_featured")]
        public bool IsFeatured { get; set; }

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

        [JsonPropertyName("logo_image_path")]
        public string? LogoImagePath { get; set; }

        [JsonPropertyName("backdrop_image_path")]
        public string? BackdropImagePath { get; set; }
    }

    private sealed class StoryPlaylistItemRow
    {
        [JsonPropertyName("playlist_id")]
        public Guid PlaylistId { get; set; }

        [JsonPropertyName("story_id")]
        public Guid StoryId { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }
    }
}
