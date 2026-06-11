using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public sealed class PlaylistPlaybackState
{
    private IReadOnlyList<MobileStorySummary> _stories = Array.Empty<MobileStorySummary>();

    public string? Slug { get; private set; }

    public string? Title { get; private set; }

    public IReadOnlyList<MobileStorySummary> Stories => _stories;

    public void Set(MobilePlaylist playlist)
    {
        Slug = playlist.Slug;
        Title = playlist.Title;
        _stories = playlist.Stories.ToArray();
    }

    public void Clear()
    {
        Slug = null;
        Title = null;
        _stories = Array.Empty<MobileStorySummary>();
    }
}
