namespace Shink.Components.Content;

public static class GratisPlaylistDisplayOrderPolicy
{
    public const string GratisPlaylistSlug = "gratis-stories";

    public static IReadOnlyList<TItem> MoveGratisPlaylistNearTop<TItem>(
        IReadOnlyList<TItem> items,
        bool isGratisOnlyUser,
        Func<TItem, string?> playlistSlugSelector,
        bool isFirstVisit = false)
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
                    GratisPlaylistSlug,
                    StringComparison.OrdinalIgnoreCase))
            {
                gratisIndex = index;
                break;
            }
        }

        var targetIndex = isFirstVisit ? 0 : 1;
        if (gratisIndex <= targetIndex)
        {
            return items;
        }

        var reordered = items.ToList();
        var gratisItem = reordered[gratisIndex];
        reordered.RemoveAt(gratisIndex);
        reordered.Insert(targetIndex, gratisItem);
        return reordered.ToArray();
    }
}
