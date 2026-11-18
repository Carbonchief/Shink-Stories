namespace Shink.Components.Content;

public static class SpeellysteCarouselOrderPolicy
{
    public static IReadOnlyList<TItem> Apply<TItem>(
        IEnumerable<TItem> playlists,
        IReadOnlyList<Guid>? playlistOrder,
        Func<TItem, Guid> playlistIdSelector)
    {
        if (playlistOrder is not { Count: > 0 })
        {
            return playlists.ToArray();
        }

        var positions = new Dictionary<Guid, int>(playlistOrder.Count);
        foreach (var playlistId in playlistOrder)
        {
            positions.TryAdd(playlistId, positions.Count);
        }

        // OrderBy keeps unlisted playlists in their existing relative order.
        return playlists
            .OrderBy(playlist => positions.GetValueOrDefault(playlistIdSelector(playlist), int.MaxValue))
            .ToArray();
    }
}
