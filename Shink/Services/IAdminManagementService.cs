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

    Task<AdminOperationResult> CreateSubscriberAsync(
        string? adminEmail,
        AdminSubscriberCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminSubscriberDetailSnapshot?> GetSubscriberDetailAsync(
        string? adminEmail,
        Guid subscriberId,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SetSubscriberDisabledAsync(
        string? adminEmail,
        AdminSubscriberDisabledUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> GrantSubscriberAccessAsync(
        string? adminEmail,
        AdminSubscriberAccessGrantRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> CancelSubscriberAccessAsync(
        string? adminEmail,
        AdminSubscriberAccessCancelRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> CancelSubscriberPaidSubscriptionAsync(
        string? adminEmail,
        AdminSubscriberPaidSubscriptionCancelRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SendSubscriberPasswordResetAsync(
        string? adminEmail,
        Guid subscriberId,
        string resetUrl,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> ResendSubscriberRecoveryEmailAsync(
        string? adminEmail,
        Guid subscriberId,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SendSubscriberSubscriptionRecoveryEmailAsync(
        string? adminEmail,
        Guid subscriberId,
        CancellationToken cancellationToken = default);

    Task<string> ExportSubscribersCsvAsync(
        string? adminEmail,
        AdminSubscriberExportRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminBulkOperationResult> RunSubscriberBulkActionAsync(
        string? adminEmail,
        AdminSubscriberBulkActionRequest request,
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

    Task<AdminSubscriptionSettingsSnapshot> GetSubscriptionSettingsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SaveSubscriptionSettingsAsync(
        string? adminEmail,
        AdminSubscriptionSettingsUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SaveSubscriptionTypeAsync(
        string? adminEmail,
        AdminSubscriptionTypeSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SaveSubscriptionDiscountCodeAsync(
        string? adminEmail,
        AdminSubscriptionDiscountCodeSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> ImportWordPressSubscriptionDiscountCodesAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<AdminSubscriberReportsSnapshot> GetSubscriberReportsAsync(
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

    Task<AdminOperationResult> UpdateResourceDocumentAccessTierAsync(
        string? adminEmail,
        AdminResourceDocumentAccessTierUpdateRequest request,
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
    DateTimeOffset? CancelledAt,
    DateTimeOffset? DisabledAt = null,
    string? DisabledByAdminEmail = null,
    string? DisabledReason = null);

public sealed record AdminSubscriberUpdateRequest(
    Guid SubscriberId,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? MobileNumber,
    string? Email = null);

public sealed record AdminSubscriberCreateRequest(
    string? Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? MobileNumber,
    bool SendPasswordReset,
    string? ResetUrl = null);

public sealed record AdminSubscriberDisabledUpdateRequest(
    Guid SubscriberId,
    bool IsDisabled,
    string? Reason);

public sealed record AdminSubscriberAccessGrantRequest(
    Guid SubscriberId,
    string? TierCode,
    DateTimeOffset? ExpiresAt,
    string? Reason);

public sealed record AdminSubscriberAccessCancelRequest(
    Guid SubscriberId,
    Guid SubscriptionId,
    string? Reason);

public sealed record AdminSubscriberPaidSubscriptionCancelRequest(
    Guid SubscriberId,
    Guid SubscriptionId,
    string? Reason);

public sealed record AdminSubscriberExportRequest(
    AdminSubscriberPageRequest PageRequest,
    IReadOnlyList<Guid>? SelectedSubscriberIds = null);

public sealed record AdminSubscriberBulkActionRequest(
    AdminSubscriberBulkAction Action,
    IReadOnlyList<Guid> SubscriberIds,
    string? Reason,
    string? ResetUrl = null);

public sealed record AdminBulkOperationResult(
    bool IsSuccess,
    int RequestedCount,
    int SucceededCount,
    IReadOnlyList<string> ErrorMessages);

public enum AdminSubscriberBulkAction
{
    Disable = 0,
    Enable = 1,
    SendPasswordReset = 2
}

public sealed record AdminSubscriberDetailSnapshot(
    AdminSubscriberRecord Subscriber,
    IReadOnlyList<AdminSubscriberSubscriptionRecord> Subscriptions,
    IReadOnlyList<AdminSubscriberBillingEventRecord> BillingEvents,
    IReadOnlyList<AdminSubscriberStoreOrderRecord> StoreOrders,
    IReadOnlyList<AdminSubscriberActivityRecord> Activity,
    IReadOnlyList<AdminSubscriberRecoveryRecord> Recoveries,
    IReadOnlyList<AdminSubscriberNotificationRecord> Notifications,
    IReadOnlyList<AdminSubscriptionTierOption> TierOptions,
    IReadOnlyList<AdminSubscriberAuditRecord> AuditTrail)
{
    public static AdminSubscriberDetailSnapshot Empty(AdminSubscriberRecord subscriber) => new(
        subscriber,
        Array.Empty<AdminSubscriberSubscriptionRecord>(),
        Array.Empty<AdminSubscriberBillingEventRecord>(),
        Array.Empty<AdminSubscriberStoreOrderRecord>(),
        Array.Empty<AdminSubscriberActivityRecord>(),
        Array.Empty<AdminSubscriberRecoveryRecord>(),
        Array.Empty<AdminSubscriberNotificationRecord>(),
        Array.Empty<AdminSubscriptionTierOption>(),
        Array.Empty<AdminSubscriberAuditRecord>());
}

public sealed record AdminSubscriberSubscriptionRecord(
    Guid SubscriptionId,
    string TierCode,
    string TierName,
    string Provider,
    string SourceSystem,
    string Status,
    DateTimeOffset? SubscribedAt,
    DateTimeOffset? NextRenewalAt,
    DateTimeOffset? CancelledAt,
    string? ProviderPaymentId,
    string? ProviderTransactionId,
    bool HasProviderToken,
    bool IsAdminOverride,
    bool IsReadOnly);

public sealed record AdminSubscriberBillingEventRecord(
    DateTimeOffset ReceivedAt,
    string Provider,
    string? EventType,
    string? EventStatus,
    string? ProviderPaymentId,
    string? ProviderTransactionId);

public sealed record AdminSubscriberStoreOrderRecord(
    Guid OrderId,
    string OrderReference,
    string ProductName,
    decimal TotalPriceZar,
    string PaymentStatus,
    string Provider,
    string? ProviderTransactionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt);

public sealed record AdminSubscriberActivityRecord(
    DateTimeOffset OccurredAt,
    string ActivityType,
    string Summary,
    string? Details);

public sealed record AdminSubscriberRecoveryRecord(
    string RecoveryId,
    string RecoveryType,
    string Status,
    string? SourceKey,
    string? Provider,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? Resolution);

public sealed record AdminSubscriberNotificationRecord(
    Guid NotificationId,
    string NotificationType,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset? ClearedAt);

public sealed record AdminSubscriptionTierOption(
    string TierCode,
    string DisplayName,
    decimal PriceZar,
    bool IsActive);

public sealed record AdminSubscriberAuditRecord(
    DateTimeOffset CreatedAt,
    string AdminEmail,
    string ActionKey,
    string? Notes);

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
    string? ShowcaseImagePath,
    int SortOrder,
    int? MaxItems,
    bool IsEnabled,
    bool ShowOnHome,
    bool IncludeInSpeellysteCarousel,
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
    string? ShowcaseImagePath,
    int SortOrder,
    int? MaxItems,
    bool IsEnabled,
    bool ShowOnHome,
    bool IncludeInSpeellysteCarousel,
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

public sealed record AdminSubscriptionSettingsSnapshot(
    bool SignupCodeBypassEnabled,
    IReadOnlyList<AdminSubscriptionTypeRecord> SubscriptionTypes,
    IReadOnlyList<AdminSubscriptionDiscountCodeRecord> DiscountCodes);

public sealed record AdminSubscriptionSettingsUpdateRequest(
    bool SignupCodeBypassEnabled);

public sealed record AdminSubscriptionTypeRecord(
    string TierCode,
    string DisplayName,
    string? Description,
    int BillingPeriodMonths,
    decimal PriceZar,
    string PayFastPlanSlug,
    string? PaystackPlanCode,
    bool IsActive);

public sealed record AdminSubscriptionTypeSaveRequest(
    string TierCode,
    string DisplayName,
    string? Description,
    int BillingPeriodMonths,
    decimal PriceZar,
    string PayFastPlanSlug,
    string? PaystackPlanCode,
    bool IsActive);

public sealed record AdminSubscriptionDiscountCodeRecord(
    Guid DiscountCodeId,
    string Code,
    string? DisplayName,
    string? Description,
    bool IsGroupCode,
    Guid? ParentDiscountCodeId,
    string? ParentCode,
    DateTimeOffset? StartsAt,
    DateTimeOffset? ExpiresAt,
    int MaxUses,
    bool OneUsePerUser,
    bool BypassPayment,
    bool IsActive,
    string SourceSystem,
    int UsedCount,
    int GroupCodeCount,
    IReadOnlyList<AdminSubscriptionDiscountCodeTierRecord> TierMappings,
    IReadOnlyList<AdminSubscriptionDiscountCodeUseRecord> Uses);

public sealed record AdminSubscriptionDiscountCodeTierRecord(
    string TierCode,
    string TierName,
    decimal InitialPaymentZar,
    decimal BillingAmountZar,
    int CycleNumber,
    string? CyclePeriod,
    int? BillingLimit,
    decimal TrialAmountZar,
    int TrialLimit,
    int? ExpirationNumber,
    string? ExpirationPeriod);

public sealed record AdminSubscriptionDiscountCodeUseRecord(
    Guid? RedemptionId,
    string Email,
    string? TierCode,
    DateTimeOffset RedeemedAt,
    DateTimeOffset? AccessEndsAt,
    string SourceSystem);

public sealed record AdminSubscriptionDiscountCodeSaveRequest(
    Guid? DiscountCodeId,
    string Code,
    string? DisplayName,
    string? Description,
    bool IsGroupCode,
    Guid? ParentDiscountCodeId,
    DateTimeOffset? StartsAt,
    DateTimeOffset? ExpiresAt,
    int MaxUses,
    bool OneUsePerUser,
    bool BypassPayment,
    bool IsActive,
    IReadOnlyList<AdminSubscriptionDiscountCodeTierSaveItem> TierMappings);

public sealed record AdminSubscriptionDiscountCodeTierSaveItem(
    string TierCode,
    decimal InitialPaymentZar,
    decimal BillingAmountZar,
    int CycleNumber,
    string? CyclePeriod,
    int? BillingLimit,
    decimal TrialAmountZar,
    int TrialLimit,
    int? ExpirationNumber,
    string? ExpirationPeriod);

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

public sealed record AdminSubscriberReportsSnapshot(
    IReadOnlyList<AdminMembershipStatsMetric> MembershipStats,
    IReadOnlyList<AdminSubscriberTrendMetric> MembershipTrend,
    IReadOnlyList<AdminTierDistributionMetric> ActiveMembersPerLevel,
    IReadOnlyList<AdminSalesRevenueMetric> SalesAndRevenue,
    IReadOnlyList<AdminRecoveryMetric> AbandonedCartRecoveries,
    IReadOnlyList<AdminVisitsViewsLoginsMetric> VisitsViewsAndLogins)
{
    public static AdminSubscriberReportsSnapshot Empty { get; } = new(
        MembershipStats: [],
        MembershipTrend: [],
        ActiveMembersPerLevel: [],
        SalesAndRevenue: [],
        AbandonedCartRecoveries: [],
        VisitsViewsAndLogins: []);
}

public sealed record AdminMembershipStatsMetric(
    string PeriodKey,
    int Signups,
    int Cancellations);

public sealed record AdminSubscriberTrendMetric(
    string PeriodType,
    string PeriodKey,
    string PeriodLabel,
    int Signups,
    int Cancellations);

public sealed record AdminTierDistributionMetric(
    string TierCode,
    string TierName,
    int ActiveMembers,
    decimal Percentage);

public sealed record AdminSalesRevenueMetric(
    string PeriodKey,
    int SalesCount,
    decimal RevenueZar);

public sealed record AdminRecoveryMetric(
    string PeriodKey,
    decimal RecoveredRevenueZar,
    int RecoveredOrders,
    int RecoveryAttempts);

public sealed record AdminVisitsViewsLoginsMetric(
    string PeriodKey,
    int Visits,
    int Views,
    int Logins);

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
    string? RequiredTierCode,
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
    string? RequiredTierCode,
    int SortOrder,
    bool IsEnabled);

public sealed record AdminResourceDocumentAccessTierUpdateRequest(
    Guid ResourceDocumentId,
    string? RequiredTierCode);
