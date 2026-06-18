namespace Shink.Mobile.Models;

public sealed record MobileSession(
    bool IsSignedIn,
    string? Email,
    string? DisplayName,
    string? ProfileImageUrl,
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    bool HasPaidSubscription,
    IReadOnlyList<string> FavoriteStorySlugs,
    string LoginUrl,
    string SignupUrl,
    string PlansUrl);

public sealed record MobileProfileUpdateResponse(string Message, MobileSession Session);

public sealed record MobileStoryPreview(
    string Title,
    string ImageUrl,
    string DetailUrl);

public sealed record MobileStorySummary(
    string Slug,
    string Title,
    string Description,
    string ImageUrl,
    string ThumbnailUrl,
    string Source,
    bool IsLocked,
    bool IsFavorite,
    string DetailUrl,
    decimal? DurationSeconds);

public sealed record MobileHomeResponse(
    string HeroTitle,
    string HeroSubtitle,
    string HeroImageUrl,
    string LogoImageUrl,
    IReadOnlyList<MobileStoryPreview> NewestStories,
    IReadOnlyList<MobileStoryPreview> BibleStories,
    IReadOnlyList<MobileStorySummary> FreeStories);

public sealed record MobileStoryCollectionResponse(
    string Title,
    string Description,
    IReadOnlyList<MobileStorySummary> Stories);

public sealed record MobilePlaylist(
    string Slug,
    string Title,
    string? Description,
    string ArtworkUrl,
    string BackdropUrl,
    IReadOnlyList<MobileStorySummary> Stories,
    bool? ShowShowcaseImageOnLuisterPage = null,
    MobileStorySummary? ShowcaseStory = null);

public sealed record MobileLuisterResponse(
    bool HasPaidSubscription,
    IReadOnlyList<MobilePlaylist> Playlists,
    IReadOnlyList<MobileLuisterSection>? Sections);

public sealed record MobileLuisterSection(
    string Kind,
    string Title,
    int SortOrder,
    MobilePlaylist? Playlist,
    IReadOnlyList<MobilePlaylist> Playlists);

public sealed record MobileStoryDetailResponse(
    MobileStorySummary Story,
    string? AudioUrl,
    string ShareUrl,
    bool RequiresSubscription,
    MobileStorySummary? PreviousStory,
    MobileStorySummary? NextStory,
    IReadOnlyList<MobileStorySummary> RelatedStories,
    string? Summary,
    IReadOnlyList<string> Lessons,
    IReadOnlyList<string> ValueTags,
    IReadOnlyList<string> ConversationQuestions,
    IReadOnlyList<string> Characters,
    IReadOnlyList<MobileStoryCharacter> CharacterTiles,
    string? YouTubeUrl,
    IReadOnlyList<MobileStoryTestQuestion> TestQuestions,
    string LoginUrl,
    string PlansUrl);

public sealed record MobileStoryCharacter(
    string DisplayName,
    string? ImageUrl,
    string? ImageAlt,
    bool IsTextOnly);

public sealed record MobileStoryTestQuestion(
    string Question,
    string OptionA,
    string OptionB,
    string CorrectOption,
    string? OptionC);

public sealed record MobileContentBlock(
    string Key,
    string Title,
    string Body,
    string ImageUrl);

public sealed record MobileAboutResponse(IReadOnlyList<MobileContentBlock> Blocks);

public sealed record AuthResponse(string? Message, string? RedirectPath);

public sealed record MobileNotificationPage(
    int Count,
    int UnreadCount,
    bool HasMore,
    bool HasHistory,
    IReadOnlyList<MobileNotificationItem> Notifications);

public sealed record MobileNotificationItem(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? ImagePath,
    string? ImageAlt,
    string? Href,
    DateTimeOffset CreatedAt,
    bool IsRead,
    bool IsCleared);

public sealed record MobileNotificationMutationResponse(
    int? MarkedCount,
    bool? Marked,
    int? ClearedCount,
    bool? Cleared);
