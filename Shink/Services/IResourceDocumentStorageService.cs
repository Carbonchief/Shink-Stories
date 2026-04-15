namespace Shink.Services;

public interface IResourceDocumentStorageService
{
    Task<UploadedResourceDocument> UploadDocumentAsync(
        string resourceTypeSlug,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<ResourceDocumentStream?> OpenReadAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task DeleteObjectIfExistsAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}

public sealed record UploadedResourceDocument(
    string Bucket,
    string ObjectKey,
    string ContentType);

public sealed record ResourceDocumentStream(
    Stream Content,
    string ContentType,
    long? ContentLength,
    DateTimeOffset? LastModified);
