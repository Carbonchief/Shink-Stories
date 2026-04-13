namespace Shink.Services;

public interface IUserNotificationService
{
    Task<IReadOnlyList<UserAppNotificationItem>> GetNotificationsAsync(
        string? email,
        CancellationToken cancellationToken = default);

    Task<NotificationSyncResult> SyncCharacterUnlockNotificationsAsync(
        string? email,
        string? storySlug = null,
        CancellationToken cancellationToken = default);

    Task<int> MarkAllNotificationsReadAsync(
        string? email,
        CancellationToken cancellationToken = default);

    Task<int> ClearNotificationsAsync(
        string? email,
        CancellationToken cancellationToken = default);

    Task<int> CreatePublishedStoryNotificationsAsync(
        PublishedStoryNotificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationSyncResult(int CreatedCount);

public sealed record UserAppNotificationItem(
    Guid NotificationId,
    string NotificationType,
    string Title,
    string? Body,
    string? ImagePath,
    string? ImageAlt,
    string? Href,
    DateTimeOffset CreatedAtUtc,
    bool IsRead);

public sealed record PublishedStoryNotificationRequest(
    Guid StoryId,
    string Slug,
    string Title,
    string AccessLevel,
    string? Summary,
    string? ThumbnailImagePath,
    string? CoverImagePath);
