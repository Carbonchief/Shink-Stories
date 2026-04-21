namespace Shink.Services;

public interface ISubscriberAvatarStorageService
{
    Task<UploadedSubscriberAvatar> UploadAvatarAsync(
        string email,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task DeleteObjectIfExistsAsync(string objectKey, CancellationToken cancellationToken = default);
}

public sealed record UploadedSubscriberAvatar(
    string ObjectKey,
    string PublicUrl,
    string ContentType);
