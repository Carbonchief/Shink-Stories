namespace Shink.Services;

public interface IUserNotificationService
{
    Task<UserNotificationPageResult> GetNotificationsAsync(
        string? email,
        int take = 10,
        DateTimeOffset? before = null,
        bool history = false,
        CancellationToken cancellationToken = default);

    Task<UserNotificationPageResult> GetNotificationSummaryAsync(
        string? email, CancellationToken cancellationToken = default) =>
        GetNotificationsAsync(email, take: 1, cancellationToken: cancellationToken);

    Task<NotificationSyncResult> SyncCharacterUnlockNotificationsAsync(
        string? email,
        string? storySlug = null,
        CancellationToken cancellationToken = default);

    Task<int> MarkAllNotificationsReadAsync(
        string? email,
        CancellationToken cancellationToken = default);

    Task<bool> MarkNotificationReadAsync(
        string? email,
        Guid notificationId,
        CancellationToken cancellationToken = default);

    Task<int> ClearNotificationsAsync(
        string? email,
        CancellationToken cancellationToken = default);

    Task<bool> ClearNotificationAsync(
        string? email,
        Guid notificationId,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationSyncResult(int CreatedCount);

public sealed record UserNotificationPageResult(
    IReadOnlyList<UserAppNotificationItem> Notifications,
    int UnreadCount,
    bool HasMore,
    bool HasHistory);

public sealed record UserAppNotificationItem(
    Guid NotificationId,
    string NotificationType,
    string Title,
    string? Body,
    string? ImagePath,
    string? ImageAlt,
    string? Href,
    DateTimeOffset CreatedAtUtc,
    bool IsRead,
    bool IsCleared);
