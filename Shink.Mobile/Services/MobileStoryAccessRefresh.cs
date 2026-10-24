using Shink.Mobile.Models;
using StoryCatalogueResponse = Shink.Mobile.Models.MobileLuisterResponse;

namespace Shink.Mobile.Services;

// Story and playlist locks come from the catalogue response, independently of
// the session. Keep track of the access used to fetch that response, including
// when a page misses session events while the paywall is visible.
public sealed class MobileStoryAccessRefresh
{
    private MobileSession? _loadedSession;
    private Task? _refreshTask;

    public void MarkRefreshed(MobileSession session) => _loadedSession = session;

    public static bool HasSameAccess(MobileSession? previous, MobileSession current) =>
        previous is not null &&
        previous.IsSignedIn == current.IsSignedIn &&
        string.Equals(previous.Email, current.Email, StringComparison.OrdinalIgnoreCase) &&
        previous.HasPaidSubscription == current.HasPaidSubscription &&
        previous.HasFullStoryAccess == current.HasFullStoryAccess;

    public async Task RefreshAsync(
        Func<MobileSession> getSession,
        Func<CancellationToken, Task<StoryCatalogueResponse?>> load,
        Action<StoryCatalogueResponse> apply,
        CancellationToken cancellationToken)
    {
        if (_refreshTask is { IsCompleted: false } pending)
        {
            try { await pending.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
        cancellationToken.ThrowIfCancellationRequested();
        await (_refreshTask = RefreshCoreAsync(getSession, load, apply, cancellationToken));
    }

    private async Task RefreshCoreAsync(
        Func<MobileSession> getSession,
        Func<CancellationToken, Task<StoryCatalogueResponse?>> load,
        Action<StoryCatalogueResponse> apply,
        CancellationToken cancellationToken)
    {
        while (!HasSameAccess(_loadedSession, getSession()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requestedSession = getSession();
            var response = await load(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (response is null) return;
            // A response requested before a purchase/account change must not
            // overwrite the locks for the newly refreshed session.
            if (!HasSameAccess(requestedSession, getSession())) continue;
            apply(response);
            MarkRefreshed(requestedSession);
        }
    }

    public static MobilePlaylist? FindPlaylist(StoryCatalogueResponse response, string slug) =>
        response.Playlists.Concat((response.Sections ?? [])
                .SelectMany(section => section.Playlist is { } playlist
                    ? section.Playlists.Prepend(playlist)
                    : section.Playlists))
            .FirstOrDefault(playlist => string.Equals(playlist.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
