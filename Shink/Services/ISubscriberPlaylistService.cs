namespace Shink.Services;

// Identity is resolved from the authenticated server session, never from a form.
public interface ISubscriberPlaylistService
{
    Task<SubscriberPlaylistListResult> GetPlaylistsAsync(Guid? playlistId = null, CancellationToken cancellationToken = default);
    Task<SubscriberPlaylistSaveResult> SavePlaylistAsync(Guid? playlistId, string title, IReadOnlyList<string> storySlugs, CancellationToken cancellationToken = default);
    Task<bool> DeletePlaylistAsync(Guid playlistId, CancellationToken cancellationToken = default);
}

public sealed record SubscriberPlaylist(Guid PlaylistId, string Title, IReadOnlyList<string> StorySlugs);
public sealed record SubscriberPlaylistListResult(bool IsSuccess, IReadOnlyList<SubscriberPlaylist> Playlists, string? ErrorMessage = null);
public sealed record SubscriberPlaylistSaveResult(bool IsSuccess, SubscriberPlaylist? Playlist = null, string? ErrorMessage = null);
