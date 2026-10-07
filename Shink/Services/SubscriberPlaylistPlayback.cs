using Shink.Components.Content;

namespace Shink.Services;

public static class SubscriberPlaylistPlayback
{
    // Only current catalog metadata is used. Saved playlists contain slugs, never
    // audio locations; the player's existing access checks sign each playable item.
    public static StoryPlaylist BuildPlaylist(SubscriberPlaylist playlist, IReadOnlyList<StoryItem> catalog)
    {
        var storiesBySlug = catalog.Where(SupabaseSubscriberPlaylistService.IsAudioStory)
            .DistinctBy(story => story.Slug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(story => story.Slug, StringComparer.OrdinalIgnoreCase);
        var stories = playlist.StorySlugs.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(slug => storiesBySlug.GetValueOrDefault(slug)).OfType<StoryItem>().ToArray();
        return new StoryPlaylist(playlist.PlaylistId.ToString("D"), playlist.Title, null, 0, stories);
    }
}
