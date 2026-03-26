namespace Shink.Services;

public interface IStoryMediaStorageService
{
    Task<UploadedStoryAudio> UploadAudioAsync(
        string slug,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<UploadedStoryImage> UploadImageAsync(
        string slug,
        StoryImageKind kind,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task DeleteObjectIfExistsAsync(string objectKey, CancellationToken cancellationToken = default);
}

public sealed record UploadedStoryAudio(
    string Bucket,
    string ObjectKey,
    string ContentType);

public sealed record UploadedStoryImage(
    string ObjectKey,
    string PublicUrl,
    string ContentType);

public enum StoryImageKind
{
    Cover,
    Thumbnail
}
