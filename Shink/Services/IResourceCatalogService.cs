namespace Shink.Services;

public interface IResourceCatalogService
{
    Task<IReadOnlyList<ResourceTypeCatalog>> GetResourceTypesAsync(CancellationToken cancellationToken = default);

    Task<ResourceDocumentDownload?> GetDocumentDownloadAsync(
        Guid resourceDocumentId,
        CancellationToken cancellationToken = default);

    Task<ResourceDocumentPreviewDownload?> GetDocumentPreviewAsync(
        Guid resourceDocumentId,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceTypeCatalog(
    Guid ResourceTypeId,
    string Slug,
    string Name,
    string? Description,
    int SortOrder,
    IReadOnlyList<ResourceDocumentItem> Documents);

public sealed record ResourceDocumentItem(
    Guid ResourceDocumentId,
    string FileName,
    string Title,
    string Url,
    string? PreviewUrl,
    string? RequiredTierCode,
    long SizeBytes,
    DateTimeOffset LastModified);

public sealed record ResourceDocumentDownload(
    Guid ResourceDocumentId,
    string DownloadFileName,
    string ContentType,
    string StorageBucket,
    string StorageObjectKey,
    string? RequiredTierCode,
    DateTimeOffset? LastModified);

public sealed record ResourceDocumentPreviewDownload(
    Guid ResourceDocumentId,
    string ContentType,
    string StorageBucket,
    string StorageObjectKey,
    DateTimeOffset? LastModified);
