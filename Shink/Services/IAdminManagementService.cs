using System.Text.Json.Serialization;

namespace Shink.Services;

public interface IAdminManagementService
{
    Task<bool> IsAdminAsync(string? email, CancellationToken cancellationToken = default);
    Task<bool> ChangeAdminEmailAsync(
        string? currentEmail,
        string? newEmail,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminSubscriberRecord>> GetSubscribersAsync(
        string? adminEmail,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<AdminSubscriberPageResult> GetSubscribersPageAsync(
        string? adminEmail,
        AdminSubscriberPageRequest request,
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
        IReadOnlyList<AdminPlaylistStorySaveItem> orderedStories,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminStoreProductRecord>> GetStoreProductsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SaveStoreProductAsync(
        string? adminEmail,
        AdminStoreProductSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> DeleteStoreProductAsync(
        string? adminEmail,
        Guid storeProductId,
        CancellationToken cancellationToken = default);

    Task<AdminAnalyticsSnapshot> GetAnalyticsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminResourceTypeRecord>> GetResourceTypesAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SaveResourceTypeAsync(
        string? adminEmail,
        AdminResourceTypeUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> DeleteResourceTypeAsync(
        string? adminEmail,
        Guid resourceTypeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminResourceDocumentRecord>> GetResourceDocumentsAsync(
        string? adminEmail,
        Guid resourceTypeId,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> CreateResourceDocumentAsync(
        string? adminEmail,
        AdminResourceDocumentCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> DeleteResourceDocumentAsync(
        string? adminEmail,
        Guid resourceDocumentId,
        CancellationToken cancellationToken = default);
}

public sealed record AdminOperationResult(bool IsSuccess, string? ErrorMessage = null, Guid? EntityId = null);

public sealed record AdminSubscriberPageRequest(
    int PageIndex,
    int PageSize,
    string? Search = null,
    string? SortLabel = null,
    bool SortDescending = false,
    string? SubscriberText = null,
    string? MobileText = null,
    string? TierText = null,
    string? SourceSystem = null,
    string? PaymentProvider = null,
    string? SubscriptionStatus = null);

public sealed record AdminSubscriberPageResult(
    IReadOnlyList<AdminSubscriberRecord> Items,
    int TotalCount);

public sealed record AdminSubscriberRecord(
    Guid SubscriberId,
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? MobileNumber,
    string? ProfileImageUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> ActiveTierCodes,
    string? PaymentProvider,
    string? SubscriptionSourceSystem,
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
    string? YouTubeUrl,
    string? CoverImagePath,
    string? ThumbnailImagePath,
    string AudioProvider,
    string? AudioBucket,
    string? AudioObjectKey,
    string? AudioContentType,
    string AccessLevel,
    string Status,
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
    string? YouTubeUrl,
    string? CoverImagePath,
    string? ThumbnailImagePath,
    string AudioProvider,
    string? AudioBucket,
    string? AudioObjectKey,
    string? AudioContentType,
    string AccessLevel,
    string Status,
    int SortOrder,
    DateTimeOffset? PublishedAt,
    int? DurationSeconds);

public sealed record AdminStoryCreateRequest(
    string Slug,
    string Title,
    string? Summary,
    string? Description,
    string? YouTubeUrl,
    string? CoverImagePath,
    string? ThumbnailImagePath,
    string AudioBucket,
    string AudioObjectKey,
    string? AudioContentType,
    string AccessLevel,
    string Status,
    int SortOrder,
    DateTimeOffset? PublishedAt,
    int? DurationSeconds);

public sealed record AdminPlaylistRecord(
    Guid PlaylistId,
    string Slug,
    string Title,
    bool IsSystemPlaylist,
    string? SystemKey,
    string? Description,
    string? LogoImagePath,
    string? BackdropImagePath,
    int SortOrder,
    int? MaxItems,
    bool IsEnabled,
    bool ShowOnHome,
    bool ShowShowcaseImageOnLuisterPage,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AdminPlaylistStoryItem> Stories);

public sealed record AdminPlaylistStoryItem(
    Guid StoryId,
    string StorySlug,
    string StoryTitle,
    int SortOrder,
    bool IsShowcase);

public sealed record AdminPlaylistStorySaveItem(
    Guid StoryId,
    bool IsShowcase);

public sealed record AdminPlaylistUpdateRequest(
    Guid? PlaylistId,
    string Slug,
    string Title,
    string? Description,
    string? LogoImagePath,
    string? BackdropImagePath,
    int SortOrder,
    int? MaxItems,
    bool IsEnabled,
    bool ShowOnHome,
    bool ShowShowcaseImageOnLuisterPage);

public sealed record AdminStoreProductRecord(
    Guid StoreProductId,
    string Slug,
    string Name,
    string? Description,
    string ImagePath,
    string AltText,
    string? ThemeClass,
    decimal UnitPriceZar,
    int SortOrder,
    bool IsEnabled,
    DateTimeOffset? UpdatedAt);

public sealed record AdminStoreProductSaveRequest(
    Guid? StoreProductId,
    string Slug,
    string Name,
    string? Description,
    string ImagePath,
    string? AltText,
    string? ThemeClass,
    decimal UnitPriceZar,
    int SortOrder,
    bool IsEnabled);

public sealed record AdminStoryAnalyticsSummary(
    [property: JsonPropertyName("total_views")] int TotalViews,
    [property: JsonPropertyName("unique_viewers")] int UniqueViewers,
    [property: JsonPropertyName("unique_viewed_stories")] int UniqueViewedStories,
    [property: JsonPropertyName("last_view_at")] DateTimeOffset? LastViewAt,
    [property: JsonPropertyName("total_listen_events")] int TotalListenEvents,
    [property: JsonPropertyName("unique_listeners")] int UniqueListeners,
    [property: JsonPropertyName("unique_listened_stories")] int UniqueListenedStories,
    [property: JsonPropertyName("total_listen_sessions")] int TotalListenSessions,
    [property: JsonPropertyName("total_listened_seconds")] decimal TotalListenedSeconds,
    [property: JsonPropertyName("average_listened_seconds_per_session")] decimal AverageListenedSecondsPerSession,
    [property: JsonPropertyName("last_listen_at")] DateTimeOffset? LastListenAt,
    [property: JsonPropertyName("total_favorites")] int TotalFavorites,
    [property: JsonPropertyName("unique_favoriters")] int UniqueFavoriters,
    [property: JsonPropertyName("last_favorite_at")] DateTimeOffset? LastFavoriteAt);

public sealed record AdminAnalyticsDailyActivityPoint(
    [property: JsonPropertyName("activity_date")] DateOnly ActivityDate,
    [property: JsonPropertyName("total_views")] int TotalViews,
    [property: JsonPropertyName("total_listen_sessions")] int TotalListenSessions,
    [property: JsonPropertyName("total_listened_seconds")] decimal TotalListenedSeconds,
    [property: JsonPropertyName("total_favorites")] int TotalFavorites);

public sealed record AdminAnalyticsTopStoryRecord(
    [property: JsonPropertyName("story_slug")] string StorySlug,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("total_views")] int TotalViews,
    [property: JsonPropertyName("unique_viewers")] int UniqueViewers,
    [property: JsonPropertyName("total_listen_sessions")] int TotalListenSessions,
    [property: JsonPropertyName("total_listened_seconds")] decimal TotalListenedSeconds,
    [property: JsonPropertyName("total_favorites")] int TotalFavorites,
    [property: JsonPropertyName("last_activity_at")] DateTimeOffset? LastActivityAt);

public sealed record AdminCharacterAnalyticsSummary(
    [property: JsonPropertyName("total_audio_plays")] int TotalAudioPlays,
    [property: JsonPropertyName("unique_subscribers")] int UniqueSubscribers,
    [property: JsonPropertyName("unique_characters")] int UniqueCharacters,
    [property: JsonPropertyName("last_audio_play_at")] DateTimeOffset? LastAudioPlayAt);

public sealed record AdminAnalyticsTopCharacterRecord(
    [property: JsonPropertyName("character_slug")] string CharacterSlug,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("total_audio_plays")] int TotalAudioPlays,
    [property: JsonPropertyName("unique_subscribers")] int UniqueSubscribers,
    [property: JsonPropertyName("last_activity_at")] DateTimeOffset? LastActivityAt);

public sealed record AdminAnalyticsSnapshot(
    [property: JsonPropertyName("generated_at")] DateTimeOffset? GeneratedAt,
    [property: JsonPropertyName("story_summary")] AdminStoryAnalyticsSummary StorySummary,
    [property: JsonPropertyName("daily_activity")] IReadOnlyList<AdminAnalyticsDailyActivityPoint> DailyActivity,
    [property: JsonPropertyName("top_stories")] IReadOnlyList<AdminAnalyticsTopStoryRecord> TopStories,
    [property: JsonPropertyName("character_summary")] AdminCharacterAnalyticsSummary CharacterSummary,
    [property: JsonPropertyName("top_characters")] IReadOnlyList<AdminAnalyticsTopCharacterRecord> TopCharacters)
{
    public static AdminAnalyticsSnapshot Empty { get; } = new(
        GeneratedAt: null,
        StorySummary: new AdminStoryAnalyticsSummary(
            TotalViews: 0,
            UniqueViewers: 0,
            UniqueViewedStories: 0,
            LastViewAt: null,
            TotalListenEvents: 0,
            UniqueListeners: 0,
            UniqueListenedStories: 0,
            TotalListenSessions: 0,
            TotalListenedSeconds: 0,
            AverageListenedSecondsPerSession: 0,
            LastListenAt: null,
            TotalFavorites: 0,
            UniqueFavoriters: 0,
            LastFavoriteAt: null),
        DailyActivity: [],
        TopStories: [],
        CharacterSummary: new AdminCharacterAnalyticsSummary(
            TotalAudioPlays: 0,
            UniqueSubscribers: 0,
            UniqueCharacters: 0,
            LastAudioPlayAt: null),
        TopCharacters: []);
}

public sealed record AdminResourceTypeRecord(
    Guid ResourceTypeId,
    string Slug,
    string Name,
    string? Description,
    int SortOrder,
    bool IsEnabled,
    int DocumentCount,
    DateTimeOffset? UpdatedAt);

public sealed record AdminResourceTypeUpdateRequest(
    Guid? ResourceTypeId,
    string Slug,
    string Name,
    string? Description,
    int SortOrder,
    bool IsEnabled);

public sealed record AdminResourceDocumentRecord(
    Guid ResourceDocumentId,
    Guid ResourceTypeId,
    string Slug,
    string Title,
    string? Description,
    string FileName,
    string ContentType,
    long SizeBytes,
    string StorageProvider,
    string StorageBucket,
    string StorageObjectKey,
    string? PreviewImageContentType,
    string? PreviewImageBucket,
    string? PreviewImageObjectKey,
    int SortOrder,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset DocumentUpdatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record AdminResourceDocumentCreateRequest(
    Guid ResourceTypeId,
    string Slug,
    string Title,
    string? Description,
    string FileName,
    string ContentType,
    long SizeBytes,
    string StorageProvider,
    string StorageBucket,
    string StorageObjectKey,
    string PreviewImageContentType,
    string PreviewImageBucket,
    string PreviewImageObjectKey,
    int SortOrder,
    bool IsEnabled);
