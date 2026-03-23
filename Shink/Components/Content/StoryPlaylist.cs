namespace Shink.Components.Content;

public sealed record StoryPlaylist(
    string Slug,
    string Title,
    string? Description,
    int SortOrder,
    IReadOnlyList<StoryItem> Stories);
