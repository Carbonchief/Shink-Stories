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
    bool HasFullStoryAccess,
    IReadOnlyList<string> FavoriteStorySlugs,
    string LoginUrl,
    string SignupUrl,
    string PlansUrl,
    bool RequiresAvatarSelection = false,
    bool SupportsProfileAvatars = false);

public sealed record MobilePlansResponse(IReadOnlyList<MobilePlan> Plans);

public sealed record MobilePlan(
    string ProductId,
    string Slug,
    string Name,
    string Description,
    decimal Amount,
    int BillingPeriodMonths);

public sealed record MobileStorePurchaseRequest(
    string Provider,
    string ProductId,
    string ProviderPaymentId,
    string? ProviderTransactionId,
    string? ProviderToken,
    string? ExpectedAccountEmail = null);

public sealed record MobileStoreEntitlementResponse(
    bool IsActive,
    string Message,
    string? Provider,
    string? ProductId,
    DateTimeOffset? AccessEndsAtUtc,
    bool IsRetryable = false);

public sealed record MobileProfileUpdateResponse(string Message, MobileSession Session);

public sealed record MobileAvatarSelectionResponse(string Message, string AvatarId, string ProfileImageUrl);

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
    decimal? DurationSeconds,
    string StoryType = "story",
    bool RequiresFullStoryAccess = true);

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

public sealed record MobileStoryLibrary(
    IReadOnlyList<MobileStorySummary> Catalog,
    IReadOnlyList<string> PlaylistStorySlugs,
    IReadOnlyList<MobileStoryProgress> Progress,
    IReadOnlyList<MobilePersonalPlaylist> Playlists,
    string? PlaylistError)
{
    public IReadOnlyDictionary<string, MobileStorySummary> GetPlaylistCatalog()
    {
        var eligible = PlaylistStorySlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Catalog.Where(story => eligible.Contains(story.Slug))
            .OrderBy(story => story.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(story => story.Slug, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record MobileStoryProgress(string StorySlug, bool IsCompleted, decimal? LastPositionSeconds,
    decimal? DurationSeconds, decimal TotalListenedSeconds);

public sealed record MobilePersonalPlaylist(Guid PlaylistId, string Title, IReadOnlyList<string> StorySlugs)
{
    public MobilePlaylist ToPlaybackPlaylist(IReadOnlyDictionary<string, MobileStorySummary> catalog)
    {
        var stories = StorySlugs.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(slug => catalog.GetValueOrDefault(slug)).OfType<MobileStorySummary>().ToArray();
        var cover = stories.FirstOrDefault()?.ImageUrl ?? string.Empty;
        return new(PlaylistId.ToString("D"), Title, null, cover, cover, stories, PersonalPlaylistId: PlaylistId);
    }
}

public sealed record MobilePlaylist(
    string Slug,
    string Title,
    string? Description,
    string ArtworkUrl,
    string BackdropUrl,
    IReadOnlyList<MobileStorySummary> Stories,
    bool? ShowShowcaseImageOnLuisterPage = null,
    MobileStorySummary? ShowcaseStory = null,
    string? LogoUrl = null,
    string? BackgroundStartColorHex = null,
    string? BackgroundEndColorHex = null,
    string? FontColorHex = null,
    Guid? PersonalPlaylistId = null);

public sealed record MobileLuisterResponse(
    bool HasPaidSubscription,
    IReadOnlyList<MobilePlaylist> Playlists,
    IReadOnlyList<MobileLuisterSection>? Sections,
    Guid? FirstGratisVisitId = null)
{
    public IReadOnlyList<MobileLuisterSection> GetSectionsForVisit(Guid visitId)
    {
        var sections = Sections is { Count: > 0 } ? Sections : Playlists
            .Select((playlist, index) => new MobileLuisterSection("playlist", playlist.Title, index, playlist, []))
            .ToArray();
        if (FirstGratisVisitId != visitId || HasPaidSubscription)
            return sections;

        return Shink.Components.Content.GratisPlaylistDisplayOrderPolicy.MoveGratisPlaylistNearTop(
            sections, isGratisOnlyUser: true, static section => section.Playlist?.Slug, isFirstVisit: true);
    }
}

public sealed record MobileLuisterSection(
    string Kind,
    string Title,
    int SortOrder,
    MobilePlaylist? Playlist,
    IReadOnlyList<MobilePlaylist> Playlists);

public sealed record MobileCharactersResponse(
    bool IsSignedIn,
    int UnlockedCount,
    int TotalCount,
    IReadOnlyList<MobileCharacterCard> Characters);

public sealed record MobileCharacterCard(
    Guid CharacterId,
    string Slug,
    string DisplayName,
    string Heading,
    string SummaryText,
    string ImageUrl,
    string? MatchImageUrl,
    string? MysteryImageUrl,
    string ImageAlt,
    bool IsUnlocked,
    int DisplayOrder,
    string? Tagline,
    string? Species,
    string? Habitat,
    string? Catchphrase,
    string? FavoriteThing,
    string? CharacterTrait,
    string? GoldenLesson,
    string? CoreValue,
    string? FirstAppearance,
    string? Friends,
    string? ReflectionQuestion,
    string? ChallengeText,
    IReadOnlyList<MobileCharacterAudioClip> PreviewAudioClips,
    MobileCharacterStoryLink? PrimaryStory,
    IReadOnlyList<MobileCharacterStoryLink> RelatedStories,
    string CallToActionLabel,
    string? PreviewImageUrl = null,
    string? MatchPreviewImageUrl = null,
    string? MysteryPreviewImageUrl = null);

public sealed record MobileCharacterAudioClip(
    string Title,
    string StreamSlug,
    string AudioUrl);

public sealed record MobileCharacterStoryLink(
    string Slug,
    string Title,
    string Source,
    string ImageUrl);

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
    string PlansUrl,
    string? VideoUrl = null)
{
    public bool IsVideo => string.Equals(Story.StoryType, "video", StringComparison.OrdinalIgnoreCase) ||
                           !string.IsNullOrWhiteSpace(VideoUrl);
}

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

public sealed record MobileGoogleAuthCompleteResponse(string Message, MobileSession Session, bool RequiresAvatarSelection = false);

public sealed record MobileAppleAuthCompleteResponse(string Message, MobileSession Session, bool RequiresAvatarSelection = false);

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
