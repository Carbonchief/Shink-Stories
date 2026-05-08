namespace Shink.Services;

public interface IEngagementTrackingService
{
    Task<bool> RecordResourceDownloadAsync(
        string? email,
        Guid resourceDocumentId,
        string? downloadPath,
        CancellationToken cancellationToken = default);

    Task<bool> RecordBlogVisitAsync(
        string? email,
        Guid? postId,
        string? postSlug,
        string? visitPath,
        CancellationToken cancellationToken = default);
}
