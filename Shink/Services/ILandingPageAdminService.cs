namespace Shink.Services;

public interface ILandingPageAdminService
{
    Task<IReadOnlyList<LandingPageRecord>> GetPagesAsync(CancellationToken cancellationToken = default);

    Task<LandingPageOperationResult> SaveDraftAsync(
        LandingPageSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<LandingPageOperationResult> PublishAsync(
        Guid pageId,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    Task<LandingPageOperationResult> UnpublishAsync(
        Guid pageId,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    Task<LandingPageOperationResult> DeleteAsync(
        Guid pageId,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    Task<bool> EnsureAdminAsync(CancellationToken cancellationToken = default);
}
