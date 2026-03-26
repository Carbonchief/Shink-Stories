namespace Shink.Services;

public interface IAdminManagementService
{
    Task<bool> IsAdminAsync(string? email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminSubscriberRecord>> GetSubscribersAsync(
        string? adminEmail,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> UpdateSubscriberAsync(
        string? adminEmail,
        AdminSubscriberUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminStoryRecord>> GetStoriesAsync(
        string? adminEmail,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> UpdateStoryAsync(
        string? adminEmail,
        AdminStoryUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> CreateStoryAsync(
        string? adminEmail,
        AdminStoryCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminPlaylistRecord>> GetPlaylistsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SavePlaylistAsync(
        string? adminEmail,
        AdminPlaylistUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SavePlaylistOrderAsync(
        string? adminEmail,
        IReadOnlyList<Guid> orderedPlaylistIds,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SavePlaylistStoriesAsync(
        string? adminEmail,
        Guid playlistId,
        IReadOnlyList<Guid> orderedStoryIds,
        CancellationToken cancellationToken = default);
}

public sealed record AdminOperationResult(bool IsSuccess, string? ErrorMessage = null, Guid? EntityId = null);

public sealed record AdminSubscriberRecord(
    Guid SubscriberId,
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? MobileNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> ActiveTierCodes,
    string? PaymentProvider,
    string? SubscriptionStatus,
    DateTimeOffset? SubscribedAt,
    DateTimeOffset? NextPaymentDueAt,
    DateTimeOffset? CancelledAt);

public sealed record AdminSubscriberUpdateRequest(
    Guid SubscriberId,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? MobileNumber);

public sealed record AdminStoryRecord(
    Guid StoryId,
    string Slug,
    string Title,
    string? Summary,
    string? Description,
    string? CoverImagePath,
    string? ThumbnailImagePath,
    string AudioProvider,
    string? AudioBucket,
    string? AudioObjectKey,
    string? AudioContentType,
    string AccessLevel,
    string Status,
    bool IsFeatured,
    int SortOrder,
    DateTimeOffset? PublishedAt,
    int? DurationSeconds,
    DateTimeOffset? UpdatedAt);

public sealed record AdminStoryUpdateRequest(
    Guid StoryId,
    string Slug,
    string Title,
    string? Summary,
    string? Description,
    string? CoverImagePath,
    string? ThumbnailImagePath,
    string AudioProvider,
    string? AudioBucket,
    string? AudioObjectKey,
    string? AudioContentType,
    string AccessLevel,
    string Status,
    bool IsFeatured,
    int SortOrder,
    DateTimeOffset? PublishedAt,
    int? DurationSeconds);

public sealed record AdminStoryCreateRequest(
    string Slug,
    string Title,
    string? Summary,
    string? Description,
    string? CoverImagePath,
    string? ThumbnailImagePath,
    string AudioBucket,
    string AudioObjectKey,
    string? AudioContentType,
    string AccessLevel,
    string Status,
    bool IsFeatured,
    int SortOrder,
    DateTimeOffset? PublishedAt,
    int? DurationSeconds);

public sealed record AdminPlaylistRecord(
    Guid PlaylistId,
    string Slug,
    string Title,
    string? Description,
    int SortOrder,
    int? MaxItems,
    bool IsEnabled,
    bool ShowOnHome,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AdminPlaylistStoryItem> Stories);

public sealed record AdminPlaylistStoryItem(
    Guid StoryId,
    string StorySlug,
    string StoryTitle,
    int SortOrder);

public sealed record AdminPlaylistUpdateRequest(
    Guid? PlaylistId,
    string Slug,
    string Title,
    string? Description,
    int SortOrder,
    int? MaxItems,
    bool IsEnabled,
    bool ShowOnHome);
