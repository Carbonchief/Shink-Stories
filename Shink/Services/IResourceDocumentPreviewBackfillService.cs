namespace Shink.Services;

public interface IResourceDocumentPreviewBackfillService
{
    Task<ResourceDocumentPreviewBackfillResult> BackfillMissingPreviewsAsync(
        CancellationToken cancellationToken = default);
}

public sealed record ResourceDocumentPreviewBackfillResult(
    int ScannedCount,
    int CreatedCount,
    IReadOnlyList<string> Errors);
