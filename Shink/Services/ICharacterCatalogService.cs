namespace Shink.Services;

public interface ICharacterCatalogService
{
    Task<IReadOnlyList<StoryCharacterItem>> GetPublishedCharactersAsync(CancellationToken cancellationToken = default);

    Task<CharacterAudioClipItem?> FindPublishedAudioClipByStreamSlugAsync(
        string? streamSlug,
        CancellationToken cancellationToken = default);
}

public interface ICharacterAdminService
{
    Task<IReadOnlyList<AdminCharacterRecord>> GetCharactersAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> SaveCharacterAsync(
        string? adminEmail,
        AdminCharacterSaveRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record StoryCharacterItem(
    Guid CharacterId,
    string Slug,
    string DisplayName,
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
    string? ImagePath,
    string? MysteryImagePath,
    string? UnlockStorySlug,
    IReadOnlyList<string> RelatedStorySlugs,
    IReadOnlyList<CharacterAudioClipItem> AudioClips,
    int UnlockThresholdSeconds,
    int SortOrder);

public sealed record AdminCharacterRecord(
    Guid CharacterId,
    string Slug,
    string DisplayName,
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
    string? ImagePath,
    string? MysteryImagePath,
    string? ImageDriveFileId,
    string? MysteryImageDriveFileId,
    string? UnlockStorySlug,
    IReadOnlyList<string> RelatedStorySlugs,
    IReadOnlyList<CharacterAudioClipItem> AudioClips,
    int UnlockThresholdSeconds,
    string Status,
    int SortOrder,
    DateTimeOffset? UpdatedAt);

public sealed record AdminCharacterSaveRequest(
    Guid? CharacterId,
    string? Slug,
    string DisplayName,
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
    string? ImagePath,
    string? MysteryImagePath,
    string? ImageDriveFileId,
    string? MysteryImageDriveFileId,
    string? UnlockStorySlug,
    IReadOnlyList<string>? RelatedStorySlugs,
    IReadOnlyList<CharacterAudioClipItem>? AudioClips,
    int UnlockThresholdSeconds,
    string Status,
    int SortOrder);

public sealed record CharacterAudioClipItem(
    string StreamSlug,
    string Title,
    string AudioProvider,
    string AudioObjectKey,
    string? AudioContentType,
    int SortOrder);
