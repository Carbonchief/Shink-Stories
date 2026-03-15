using Shink.Components.Content;

namespace Shink.Services;

public interface IStoryCatalogService
{
    Task<IReadOnlyList<StoryItem>> GetFreeStoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoryItem>> GetLuisterStoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoryPreviewItem>> GetNewestTop10Async(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoryPreviewItem>> GetBibleStoriesAsync(CancellationToken cancellationToken = default);
    Task<StoryItem?> FindFreeBySlugAsync(string? slug, CancellationToken cancellationToken = default);
    Task<StoryItem?> FindLuisterBySlugAsync(string? slug, CancellationToken cancellationToken = default);
    Task<StoryItem?> FindAnyBySlugAsync(string? slug, CancellationToken cancellationToken = default);
}
