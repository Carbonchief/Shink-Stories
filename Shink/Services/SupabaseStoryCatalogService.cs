using System.Net.Http.Headers;
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
    private const string CacheKey = "stories:published:v1";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private const int SoundbiteMaxDurationSeconds = 60;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            .Where(IsSubscriberStoryRow)
            .OrderByDescending(row => row.PublishedAt)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Select(MapToStoryItem)
            .ToArray();
    }

    public async Task<IReadOnlyList<StoryPreviewItem>> GetNewestTop10Async(CancellationToken cancellationToken = default)
    {
        var rows = await GetPublishedRowsAsync(cancellationToken);
        var luisterRows = rows
            .Where(IsSubscriberStoryRow)
            .ToArray();

        var featuredRows = luisterRows
            .Where(row => row.IsFeatured)
            .OrderBy(row => row.SortOrder)
            .ThenByDescending(row => row.PublishedAt)
            .ToList();

        if (featuredRows.Count < 10)
        {
            var featuredSlugs = featuredRows
                .Select(row => row.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var remainder = luisterRows
                .Where(row => !featuredSlugs.Contains(row.Slug))
                .OrderByDescending(row => row.PublishedAt)
                .ThenBy(row => row.SortOrder);

            foreach (var row in remainder)
            {
                if (featuredRows.Count >= 10)
                {
                    break;
                }

                featuredRows.Add(row);
            }
        }

        return featuredRows
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
            string.Equals(candidate.AccessLevel, "subscriber", StringComparison.OrdinalIgnoreCase) &&
            !IsSoundbiteRow(candidate) &&
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
        if (_memoryCache.TryGetValue(CacheKey, out IReadOnlyList<StoryCatalogRow>? cachedRows) &&
            cachedRows is not null)
        {
            return cachedRows;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_memoryCache.TryGetValue(CacheKey, out cachedRows) &&
                cachedRows is not null)
            {
                return cachedRows;
            }

            var rows = await FetchPublishedRowsAsync(cancellationToken);
            _memoryCache.Set(CacheKey, rows, CacheDuration);
            return rows;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<IReadOnlyList<StoryCatalogRow>> FetchPublishedRowsAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase stories lookup skipped: URL is not configured.");
            return BuildLegacyFallbackRows();
        }

        var apiKey = ResolveReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase stories lookup skipped: AnonKey is not configured.");
            return BuildLegacyFallbackRows();
        }

        var requestUri = new Uri(
            baseUri,
            "rest/v1/stories" +
            "?select=slug,title,summary,description,cover_image_path,thumbnail_image_path,audio_provider,audio_bucket,audio_object_key,audio_content_type,access_level,status,is_featured,sort_order,published_at,duration_seconds,metadata" +
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
        string.Equals(row.Status, "published", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(row.Slug) &&
        !string.IsNullOrWhiteSpace(row.Title) &&
        !string.IsNullOrWhiteSpace(row.AudioObjectKey);

    private static bool IsSubscriberStoryRow(StoryCatalogRow row) =>
        string.Equals(row.AccessLevel, "subscriber", StringComparison.OrdinalIgnoreCase) &&
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

        if (row.Metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return false;
        }

        return row.Metadata.GetRawText().Contains("bybel", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsBybelKeyword(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("bybel", StringComparison.OrdinalIgnoreCase);

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
            AudioContentType: string.IsNullOrWhiteSpace(row.AudioContentType) ? null : row.AudioContentType.Trim());
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
                PublishedAt = null
            });
        }

        return rows
            .GroupBy(row => row.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
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

    private sealed class StoryCatalogRow
    {
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

        [JsonPropertyName("metadata")]
        public JsonElement Metadata { get; set; }
    }
}
