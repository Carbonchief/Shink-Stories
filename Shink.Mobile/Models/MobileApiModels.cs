namespace Shink.Mobile.Models;

public sealed record MobileSession(
    bool IsSignedIn,
    string? Email,
    bool HasPaidSubscription,
    IReadOnlyList<string> FavoriteStorySlugs,
    string LoginUrl,
    string SignupUrl,
    string PlansUrl);

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
    string DetailUrl);

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
    IReadOnlyList<MobileStorySummary> Stories);

public sealed record MobileLuisterResponse(
    bool HasPaidSubscription,
    IReadOnlyList<MobilePlaylist> Playlists);

public sealed record MobileStoryDetailResponse(
    MobileStorySummary Story,
    string? AudioUrl,
    string ShareUrl,
    bool RequiresSubscription,
    MobileStorySummary? PreviousStory,
    MobileStorySummary? NextStory,
    IReadOnlyList<MobileStorySummary> RelatedStories,
    string LoginUrl,
    string PlansUrl);

public sealed record MobileContentBlock(
    string Key,
    string Title,
    string Body,
    string ImageUrl);

public sealed record MobileAboutResponse(IReadOnlyList<MobileContentBlock> Blocks);

public sealed record AuthResponse(string? Message, string? RedirectPath);
