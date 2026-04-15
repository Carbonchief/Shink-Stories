using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseResourceDocumentPreviewBackfillService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IResourceDocumentStorageService resourceDocumentStorageService,
    IResourceDocumentPreviewService resourceDocumentPreviewService,
    IMemoryCache memoryCache,
    ILogger<SupabaseResourceDocumentPreviewBackfillService> logger) : IResourceDocumentPreviewBackfillService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly IResourceDocumentStorageService _resourceDocumentStorageService = resourceDocumentStorageService;
    private readonly IResourceDocumentPreviewService _resourceDocumentPreviewService = resourceDocumentPreviewService;
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly ILogger<SupabaseResourceDocumentPreviewBackfillService> _logger = logger;

    public async Task<ResourceDocumentPreviewBackfillResult> BackfillMissingPreviewsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new ResourceDocumentPreviewBackfillResult(0, 0, ["Supabase URL is not configured."]);
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ResourceDocumentPreviewBackfillResult(0, 0, ["Supabase ServiceRoleKey is not configured."]);
        }

        var resourceTypesUri = new Uri(
            baseUri,
            "rest/v1/resource_types" +
            "?select=resource_type_id,slug" +
            "&limit=500");

        var documentsUri = new Uri(
            baseUri,
            "rest/v1/resource_documents" +
            "?select=resource_document_id,resource_type_id,file_name,storage_bucket,storage_object_key" +
            "&or=(preview_image_content_type.is.null,preview_image_bucket.is.null,preview_image_object_key.is.null)" +
            "&order=created_at.asc" +
            "&limit=5000");

        var resourceTypesTask = FetchRowsAsync<ResourceTypeRow>(resourceTypesUri, apiKey, cancellationToken);
        var documentsTask = FetchRowsAsync<ResourceDocumentRow>(documentsUri, apiKey, cancellationToken);
        await Task.WhenAll(resourceTypesTask, documentsTask);

        var typeSlugsById = resourceTypesTask.Result
            .Where(row => row.ResourceTypeId != Guid.Empty && !string.IsNullOrWhiteSpace(row.Slug))
            .ToDictionary(
                row => row.ResourceTypeId,
                row => row.Slug!.Trim(),
                EqualityComparer<Guid>.Default);

        var documents = documentsTask.Result
            .Where(row => row.ResourceDocumentId != Guid.Empty && row.ResourceTypeId != Guid.Empty)
            .ToArray();

        var errors = new List<string>();
        var createdCount = 0;

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safeFileName = Path.GetFileName(document.FileName?.Trim());
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                safeFileName = $"{document.ResourceDocumentId:D}.pdf";
            }

            if (!typeSlugsById.TryGetValue(document.ResourceTypeId, out var resourceTypeSlug) ||
                string.IsNullOrWhiteSpace(resourceTypeSlug))
            {
                errors.Add($"{safeFileName}: resource type slug is missing.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(document.StorageBucket) ||
                string.IsNullOrWhiteSpace(document.StorageObjectKey))
            {
                errors.Add($"{safeFileName}: document storage metadata is missing.");
                continue;
            }

            UploadedResourcePreviewImage? uploadedPreview = null;

            try
            {
                var documentStream = await _resourceDocumentStorageService.OpenReadAsync(
                    document.StorageBucket.Trim(),
                    document.StorageObjectKey.Trim(),
                    cancellationToken);

                if (documentStream is null)
                {
                    errors.Add($"{safeFileName}: source PDF was not found in storage.");
                    continue;
                }

                await using var sourceStream = documentStream.Content;
                await using (var pdfBuffer = new MemoryStream())
                {
                    await sourceStream.CopyToAsync(pdfBuffer, cancellationToken);
                    pdfBuffer.Position = 0;

                    var preview = await _resourceDocumentPreviewService.GeneratePreviewAsync(pdfBuffer, cancellationToken);
                    await using var previewStream = new MemoryStream(preview.Content, writable: false);

                    uploadedPreview = await _resourceDocumentStorageService.UploadPreviewImageAsync(
                        resourceTypeSlug,
                        safeFileName,
                        preview.ContentType,
                        previewStream,
                        cancellationToken);
                }

                var metadataSaved = await UpdatePreviewMetadataAsync(
                    baseUri,
                    apiKey,
                    document.ResourceDocumentId,
                    uploadedPreview!,
                    cancellationToken);

                if (!metadataSaved)
                {
                    await _resourceDocumentStorageService.DeleteObjectIfExistsAsync(uploadedPreview!.ObjectKey, cancellationToken);
                    errors.Add($"{safeFileName}: preview metadata could not be saved.");
                    continue;
                }

                createdCount++;
            }
            catch (Exception exception)
            {
                if (uploadedPreview is not null)
                {
                    await _resourceDocumentStorageService.DeleteObjectIfExistsAsync(uploadedPreview.ObjectKey, cancellationToken);
                }

                _logger.LogError(
                    exception,
                    "Failed to backfill preview image for resource document {ResourceDocumentId}.",
                    document.ResourceDocumentId);

                errors.Add($"{safeFileName}: preview generation failed.");
            }
        }

        if (createdCount > 0)
        {
            _memoryCache.Remove(ResourceCatalogCacheKeys.Catalog);
        }

        return new ResourceDocumentPreviewBackfillResult(documents.Length, createdCount, errors);
    }

    private async Task<bool> UpdatePreviewMetadataAsync(
        Uri baseUri,
        string apiKey,
        Guid resourceDocumentId,
        UploadedResourcePreviewImage uploadedPreview,
        CancellationToken cancellationToken)
    {
        var escapedDocumentId = Uri.EscapeDataString(resourceDocumentId.ToString("D"));
        var updateUri = new Uri(baseUri, $"rest/v1/resource_documents?resource_document_id=eq.{escapedDocumentId}");
        var payload = new Dictionary<string, object?>
        {
            ["preview_image_content_type"] = uploadedPreview.ContentType,
            ["preview_image_bucket"] = uploadedPreview.Bucket,
            ["preview_image_object_key"] = uploadedPreview.ObjectKey,
            ["preview_generated_at"] = DateTimeOffset.UtcNow
        };

        using var request = CreateJsonRequest(HttpMethod.Patch, updateUri, apiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Resource preview metadata update failed. resource_document_id={ResourceDocumentId} Status={StatusCode} Body={Body}",
            resourceDocumentId,
            (int)response.StatusCode,
            responseBody);
        return false;
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
                    "Supabase preview backfill fetch failed. uri={Uri} Status={StatusCode} Body={Body}",
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
            _logger.LogWarning(exception, "Supabase preview backfill fetch failed unexpectedly. uri={Uri}", uri);
            return Array.Empty<T>();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        Uri uri,
        string apiKey,
        object payload,
        string? preferHeader = null)
    {
        var request = CreateRequest(method, uri, apiKey);
        if (!string.IsNullOrWhiteSpace(preferHeader))
        {
            request.Headers.TryAddWithoutValidation("Prefer", preferHeader);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
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

    private sealed class ResourceTypeRow
    {
        [JsonPropertyName("resource_type_id")]
        public Guid ResourceTypeId { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }
    }

    private sealed class ResourceDocumentRow
    {
        [JsonPropertyName("resource_document_id")]
        public Guid ResourceDocumentId { get; set; }

        [JsonPropertyName("resource_type_id")]
        public Guid ResourceTypeId { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("storage_bucket")]
        public string? StorageBucket { get; set; }

        [JsonPropertyName("storage_object_key")]
        public string? StorageObjectKey { get; set; }
    }
}
