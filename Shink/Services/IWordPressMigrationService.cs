namespace Shink.Services;

public interface IWordPressMigrationService
{
    Task<WordPressSyncResult> SyncAsync(CancellationToken cancellationToken = default);

    Task<bool> SyncImportedUserProfileAndAccessAsync(string? email, CancellationToken cancellationToken = default);

    Task<WordPressImportedUser?> GetImportedUserByEmailAsync(string? email, CancellationToken cancellationToken = default);

    Task MarkPasswordMigratedAsync(long wordpressUserId, CancellationToken cancellationToken = default);
}

public sealed record WordPressSyncResult(
    int ImportedUsers,
    int UpsertedSubscribers,
    int UploadedAvatars,
    int UpsertedMembershipPeriods,
    int UpsertedMembershipOrders,
    int UpsertedSubscriptions,
    int UpsertedCurrentEntitlements,
    int CancelledCurrentEntitlements,
    int BackfilledAuthSubscribers,
    IReadOnlyList<string> Errors);

public sealed record WordPressImportedUser(
    long WordPressUserId,
    string Email,
    string? PasswordHash,
    string? PasswordHashFormat,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? MobileNumber,
    string? ProfileImageUrl,
    string? ProfileImageObjectKey,
    string? ProfileImageContentType);
