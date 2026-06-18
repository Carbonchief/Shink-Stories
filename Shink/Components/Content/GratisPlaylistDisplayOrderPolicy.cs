namespace Shink.Components.Content;

public static class GratisPlaylistDisplayOrderPolicy
{
    public static IReadOnlyList<TItem> MoveGratisPlaylistNearTop<TItem>(
        IReadOnlyList<TItem> items,
        bool isGratisOnlyUser,
        Func<TItem, string?> playlistSlugSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(playlistSlugSelector);

        if (!isGratisOnlyUser || items.Count < 2)
        {
            return items;
        }

        var gratisIndex = -1;
        for (var index = 0; index < items.Count; index++)
        {
            if (string.Equals(
                    playlistSlugSelector(items[index])?.Trim(),
                    StoryAccessPolicy.GratisPlaylistSlug,
                    StringComparison.OrdinalIgnoreCase))
            {
                gratisIndex = index;
                break;
            }
        }

        if (gratisIndex <= 0)
        {
            return items;
        }

        var reordered = items.ToList();
        var gratisItem = reordered[gratisIndex];
        reordered.RemoveAt(gratisIndex);
        reordered.Insert(1, gratisItem);
        return reordered.ToArray();
    }
}
