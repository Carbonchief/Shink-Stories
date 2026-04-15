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
    ILogger<SupabaseResourceCatalogService> logger) : IResourceCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;
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
        Guid resourceDocumentId,
        CancellationToken cancellationToken = default)
    {
        if (resourceDocumentId == Guid.Empty)
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

        var escapedDocumentId = Uri.EscapeDataString(resourceDocumentId.ToString("D"));
        var documentUri = new Uri(
            baseUri,
            "rest/v1/resource_documents" +
            "?select=resource_document_id,resource_type_id,file_name,content_type,storage_bucket,storage_object_key,updated_at,is_enabled" +
            $"&resource_document_id=eq.{escapedDocumentId}" +
            "&is_enabled=eq.true" +
            "&limit=1");

        var documents = await FetchRowsAsync<ResourceDocumentRow>(documentUri, apiKey, cancellationToken);
        var document = documents.FirstOrDefault();
        if (document is null ||
            document.ResourceDocumentId == Guid.Empty ||
            document.ResourceTypeId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.StorageBucket) ||
            string.IsNullOrWhiteSpace(document.StorageObjectKey))
        {
            return null;
        }

        var escapedTypeId = Uri.EscapeDataString(document.ResourceTypeId.ToString("D"));
        var typeUri = new Uri(
            baseUri,
            "rest/v1/resource_types" +
            "?select=resource_type_id" +
            $"&resource_type_id=eq.{escapedTypeId}" +
            "&is_enabled=eq.true" +
            "&limit=1");

        var types = await FetchRowsAsync<ResourceTypeLookupRow>(typeUri, apiKey, cancellationToken);
        if (types.Count == 0)
        {
            return null;
        }

        var fileName = Path.GetFileName(document.FileName?.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{document.ResourceDocumentId:D}.pdf";
        }

        var contentType = string.IsNullOrWhiteSpace(document.ContentType)
            ? "application/pdf"
            : document.ContentType.Trim();

        return new ResourceDocumentDownload(
            ResourceDocumentId: document.ResourceDocumentId,
            DownloadFileName: fileName,
            ContentType: contentType,
            StorageBucket: document.StorageBucket.Trim(),
            StorageObjectKey: document.StorageObjectKey.Trim(),
            LastModified: document.UpdatedAt);
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

        var resourceTypesUri = new Uri(
            baseUri,
            "rest/v1/resource_types" +
            "?select=resource_type_id,slug,name,description,sort_order" +
            "&is_enabled=eq.true" +
            "&order=sort_order.asc" +
            "&order=name.asc" +
            "&limit=200");

        var documentsUri = new Uri(
            baseUri,
            "rest/v1/resource_documents" +
            "?select=resource_document_id,resource_type_id,title,file_name,size_bytes,created_at,updated_at,sort_order" +
            "&is_enabled=eq.true" +
            "&order=sort_order.asc" +
            "&order=title.asc" +
            "&limit=5000");

        var resourceTypesTask = FetchRowsAsync<ResourceTypeRow>(resourceTypesUri, apiKey, cancellationToken);
        var documentsTask = FetchRowsAsync<ResourceDocumentListRow>(documentsUri, apiKey, cancellationToken);
        await Task.WhenAll(resourceTypesTask, documentsTask);

        var documentsByType = documentsTask.Result
            .Where(row => row.ResourceDocumentId != Guid.Empty && row.ResourceTypeId != Guid.Empty)
            .GroupBy(row => row.ResourceTypeId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ResourceDocumentItem>)group
                    .OrderBy(row => row.SortOrder)
                    .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new ResourceDocumentItem(
                        ResourceDocumentId: row.ResourceDocumentId,
                        FileName: Path.GetFileName(row.FileName?.Trim()) ?? $"{row.ResourceDocumentId:D}.pdf",
                        Title: NormalizeOptionalText(row.Title, 240) ?? Path.GetFileNameWithoutExtension(row.FileName?.Trim()) ?? "PDF",
                        Url: $"/media/resources/{row.ResourceDocumentId:D}",
                        SizeBytes: Math.Max(0, row.SizeBytes),
                        LastModified: row.UpdatedAt ?? row.CreatedAt))
                    .ToArray());

        return resourceTypesTask.Result
            .Where(row => row.ResourceTypeId != Guid.Empty)
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new ResourceTypeCatalog(
                ResourceTypeId: row.ResourceTypeId,
                Slug: row.Slug.Trim(),
                Name: row.Name.Trim(),
                Description: NormalizeOptionalText(row.Description, 4000),
                SortOrder: row.SortOrder,
                Documents: documentsByType.TryGetValue(row.ResourceTypeId, out var documents)
                    ? documents
                    : Array.Empty<ResourceDocumentItem>()))
            .OrderBy(type => type.SortOrder)
            .ThenBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }
    }

    private sealed class ResourceTypeLookupRow
    {
        [JsonPropertyName("resource_type_id")]
        public Guid ResourceTypeId { get; set; }
    }

    private sealed class ResourceDocumentListRow
    {
        [JsonPropertyName("resource_document_id")]
        public Guid ResourceDocumentId { get; set; }

        [JsonPropertyName("resource_type_id")]
        public Guid ResourceTypeId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }
    }

    private sealed class ResourceDocumentRow
    {
        [JsonPropertyName("resource_document_id")]
        public Guid ResourceDocumentId { get; set; }

        [JsonPropertyName("resource_type_id")]
        public Guid ResourceTypeId { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }

        [JsonPropertyName("storage_bucket")]
        public string? StorageBucket { get; set; }

        [JsonPropertyName("storage_object_key")]
        public string? StorageObjectKey { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
