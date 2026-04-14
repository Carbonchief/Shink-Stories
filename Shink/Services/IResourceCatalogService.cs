namespace Shink.Services;

public interface IResourceCatalogService
{
    Task<IReadOnlyList<ResourceTypeCatalog>> GetResourceTypesAsync(CancellationToken cancellationToken = default);

    Task<ResourceDocumentDownload?> GetDocumentDownloadAsync(
        string? typeSlug,
        string? fileName,
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
    string FileName,
    string Title,
    string Url,
    long SizeBytes,
    DateTimeOffset LastModified);

public sealed record ResourceDocumentDownload(
    string PhysicalPath,
    string DownloadFileName,
    string ContentType);
