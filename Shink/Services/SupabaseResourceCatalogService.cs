using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseResourceCatalogService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IMemoryCache memoryCache,
    IWebHostEnvironment webHostEnvironment,
    ILogger<SupabaseResourceCatalogService> logger) : IResourceCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;
    private readonly ILogger<SupabaseResourceCatalogService> _logger = logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<IReadOnlyList<ResourceTypeCatalog>> GetResourceTypesAsync(CancellationToken cancellationToken = default)
    {
        if (_memoryCache.TryGetValue(ResourceCatalogCacheKeys.Catalog, out IReadOnlyList<ResourceTypeCatalog>? cachedCatalog) &&
            cachedCatalog is not null)
        {
            return cachedCatalog;
        }

        await _refreshLock.WaitAsync(CancellationToken.None);
        try
        {
            if (_memoryCache.TryGetValue(ResourceCatalogCacheKeys.Catalog, out cachedCatalog) &&
                cachedCatalog is not null)
            {
                return cachedCatalog;
            }

            var catalog = await FetchCatalogAsync(CancellationToken.None);
            _memoryCache.Set(ResourceCatalogCacheKeys.Catalog, catalog, CacheDuration);
            return catalog;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<ResourceDocumentDownload?> GetDocumentDownloadAsync(
        string? typeSlug,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(typeSlug) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return null;
        }

        var apiKey = ResolveReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var escapedSlug = Uri.EscapeDataString(typeSlug.Trim().ToLowerInvariant());
        var requestUri = new Uri(
            baseUri,
            "rest/v1/resource_types" +
            "?select=resource_type_id,slug,name,description,source_directory,sort_order,is_enabled" +
            $"&slug=eq.{escapedSlug}" +
            "&is_enabled=eq.true" +
            "&limit=1");

        var rows = await FetchRowsAsync<ResourceTypeRow>(requestUri, apiKey, cancellationToken);
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        var requestedFileName = Path.GetFileName(Uri.UnescapeDataString(fileName.Trim()));
        if (string.IsNullOrWhiteSpace(requestedFileName) ||
            !requestedFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var file = ResourceFileSystemHelper.FindPdfFile(
            row.SourceDirectory,
            requestedFileName,
            _webHostEnvironment.ContentRootPath,
            _webHostEnvironment.WebRootPath);

        return file is null
            ? null
            : new ResourceDocumentDownload(
                PhysicalPath: file.FullPath,
                DownloadFileName: file.FileName,
                ContentType: "application/pdf");
    }

    private async Task<IReadOnlyList<ResourceTypeCatalog>> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Supabase resources lookup skipped: URL is not configured.");
            return Array.Empty<ResourceTypeCatalog>();
        }

        var apiKey = ResolveReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Supabase resources lookup skipped: AnonKey is not configured.");
            return Array.Empty<ResourceTypeCatalog>();
        }

        var requestUri = new Uri(
            baseUri,
            "rest/v1/resource_types" +
            "?select=resource_type_id,slug,name,description,source_directory,sort_order,is_enabled" +
            "&is_enabled=eq.true" +
            "&order=sort_order.asc" +
            "&order=name.asc" +
            "&limit=200");

        var rows = await FetchRowsAsync<ResourceTypeRow>(requestUri, apiKey, cancellationToken);
        return rows
            .Where(row => row.ResourceTypeId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(MapToCatalog)
            .OrderBy(type => type.SortOrder)
            .ThenBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ResourceTypeCatalog MapToCatalog(ResourceTypeRow row)
    {
        var documents = ResourceFileSystemHelper.GetPdfFiles(
                row.SourceDirectory,
                _webHostEnvironment.ContentRootPath,
                _webHostEnvironment.WebRootPath)
            .Select(file => new ResourceDocumentItem(
                FileName: file.FileName,
                Title: file.Title,
                Url: $"/media/resources/{Uri.EscapeDataString(row.Slug.Trim())}/{Uri.EscapeDataString(file.FileName)}",
                SizeBytes: file.SizeBytes,
                LastModified: file.LastModified))
            .ToArray();

        return new ResourceTypeCatalog(
            ResourceTypeId: row.ResourceTypeId,
            Slug: row.Slug.Trim(),
            Name: row.Name.Trim(),
            Description: NormalizeOptionalText(row.Description, 4000),
            SortOrder: row.SortOrder,
            Documents: documents);
    }

    private async Task<IReadOnlyList<T>> FetchRowsAsync<T>(Uri uri, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("apikey", apiKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase resources fetch failed. uri={Uri} Status={StatusCode} Body={Body}",
                    uri,
                    (int)response.StatusCode,
                    responseBody);
                return Array.Empty<T>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken)
                ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase resources fetch failed unexpectedly. uri={Uri}", uri);
            return Array.Empty<T>();
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

    private string ResolveReadApiKey() => _options.AnonKey;

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

    private sealed class ResourceTypeRow
    {
        [JsonPropertyName("resource_type_id")]
        public Guid ResourceTypeId { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("source_directory")]
        public string? SourceDirectory { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }
    }
}
