namespace Shink.Services;

public interface IStoryFavoriteService
{
    Task<IReadOnlyList<string>> GetFavoriteStorySlugsAsync(
        string? email,
        string? source = null,
        CancellationToken cancellationToken = default);

    Task<bool> SetStoryFavoriteAsync(
        string? email,
        StoryFavoriteMutationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record StoryFavoriteMutationRequest(
    string StorySlug,
    string StoryPath,
    string Source,
    bool IsFavorite,
    string? PlaylistSlug);
