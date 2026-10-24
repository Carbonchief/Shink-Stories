using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Models;
using Shink.Mobile.Services;
using StoryCatalogueResponse = Shink.Mobile.Models.MobileLuisterResponse;

namespace Shink.Tests;

[TestClass]
public sealed class MobileStoryAccessRefreshTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    public async Task PurchaseRefreshesLockedCatalogueWithoutSigningOut(bool wasPaid, bool hadFullAccess)
    {
        var session = Session(wasPaid, hadFullAccess);
        var refresh = new MobileStoryAccessRefresh();
        refresh.MarkRefreshed(session);
        var visible = Catalogue(locked: true);

        // A purchase/restore refreshes the same signed-in account, while the
        // catalogue page can be hidden behind the paywall and miss its event.
        session = session with { HasPaidSubscription = true, HasFullStoryAccess = true };
        var calls = 0;
        await refresh.RefreshAsync(() => session, _ =>
        {
            calls++;
            return Task.FromResult<StoryCatalogueResponse?>(Catalogue(locked: false));
        }, response => visible = response, default);

        Assert.AreEqual(1, calls);
        Assert.IsTrue(session.IsSignedIn);
        var playlist = MobileStoryAccessRefresh.FindPlaylist(visible, "stories");
        Assert.IsNotNull(playlist);
        Assert.IsFalse(playlist.Stories.Single().IsLocked);
        Assert.IsFalse(playlist.ShowcaseStory!.IsLocked);
    }

    [TestMethod]
    public async Task ProfileAndFavouriteChangesDoNotReloadTheCatalogue()
    {
        var original = Session(true, true);
        var refresh = new MobileStoryAccessRefresh();
        refresh.MarkRefreshed(original);
        var updated = original with { DisplayName = "New name", FavoriteStorySlugs = ["story"], Email = "READER@example.com" };
        await refresh.RefreshAsync(() => updated,
            _ => throw new AssertFailedException("Unchanged access should reuse the catalogue."),
            _ => Assert.Fail("The catalogue should not be rebuilt."), default);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedRefreshCanRetryWithoutLosingTheSignedInSession(bool returnsNull)
    {
        var session = Session(true, true);
        var refresh = new MobileStoryAccessRefresh();
        refresh.MarkRefreshed(Session(false, false));
        var applied = 0;
        try
        {
            await refresh.RefreshAsync(() => session,
                _ => returnsNull ? Task.FromResult<StoryCatalogueResponse?>(null) : throw new HttpRequestException(),
                _ => applied++, default);
        }
        catch (HttpRequestException) when (!returnsNull) { }

        Assert.AreEqual(0, applied);
        await refresh.RefreshAsync(() => session,
            _ => Task.FromResult<StoryCatalogueResponse?>(Catalogue(false)), _ => applied++, default);
        Assert.AreEqual(1, applied);
        Assert.IsTrue(session.IsSignedIn);
    }

    [TestMethod]
    public async Task ResponseStartedBeforePurchaseCannotRestoreOldLocks()
    {
        var session = Session(false, false);
        var refresh = new MobileStoryAccessRefresh();
        var oldRequest = new TaskCompletionSource<StoryCatalogueResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var applied = new List<StoryCatalogueResponse>();
        var pending = refresh.RefreshAsync(() => session,
            _ => ++calls == 1 ? oldRequest.Task : Task.FromResult<StoryCatalogueResponse?>(Catalogue(false)),
            applied.Add, default);

        session = session with { HasPaidSubscription = true, HasFullStoryAccess = true };
        oldRequest.SetResult(Catalogue(true));
        await pending;

        Assert.AreEqual(2, calls);
        Assert.HasCount(1, applied);
        Assert.IsFalse(MobileStoryAccessRefresh.FindPlaylist(applied.Single(), "stories")!.Stories.Single().IsLocked);
    }

    [TestMethod]
    public async Task ConcurrentAccessEventsShareOneRefresh()
    {
        var session = Session(true, true);
        var refresh = new MobileStoryAccessRefresh();
        var request = new TaskCompletionSource<StoryCatalogueResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var applied = 0;
        Task<StoryCatalogueResponse?> Load(CancellationToken _) { calls++; return request.Task; }
        var first = refresh.RefreshAsync(() => session, Load, _ => applied++, default);
        var second = refresh.RefreshAsync(() => session, Load, _ => applied++, default);
        request.SetResult(Catalogue(false));
        await Task.WhenAll(first, second);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(1, applied);
    }

    [TestMethod]
    public async Task ReturningPageCanRetryARefreshCancelledWhenItWasHidden()
    {
        var session = Session(true, true);
        var refresh = new MobileStoryAccessRefresh();
        using var hiddenPage = new CancellationTokenSource();
        var oldRequest = new TaskCompletionSource<StoryCatalogueResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRefresh = refresh.RefreshAsync(() => session, _ => oldRequest.Task,
            _ => Assert.Fail("Hidden page must not apply a response."), hiddenPage.Token);
        hiddenPage.Cancel();
        var applied = 0;
        var returnedRefresh = refresh.RefreshAsync(() => session,
            _ => Task.FromResult<StoryCatalogueResponse?>(Catalogue(false)), _ => applied++, default);
        oldRequest.SetResult(Catalogue(true));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => oldRefresh);
        await returnedRefresh;
        Assert.AreEqual(1, applied);
    }

    [TestMethod]
    public async Task AccountChangesAndRevokedAccessReloadServerLocks()
    {
        var session = Session(true, true);
        var refresh = new MobileStoryAccessRefresh();
        refresh.MarkRefreshed(session);
        session = session with { Email = "another@example.com", HasPaidSubscription = false, HasFullStoryAccess = false };
        StoryCatalogueResponse? visible = null;
        await refresh.RefreshAsync(() => session,
            _ => Task.FromResult<StoryCatalogueResponse?>(Catalogue(true)), response => visible = response, default);
        Assert.IsTrue(MobileStoryAccessRefresh.FindPlaylist(visible!, "stories")!.Stories.Single().IsLocked);
    }

    [TestMethod]
    public async Task RefreshPreservesLocksReturnedByTheServer()
    {
        var session = Session(true, true);
        var refresh = new MobileStoryAccessRefresh();
        StoryCatalogueResponse? visible = null;
        await refresh.RefreshAsync(() => session,
            _ => Task.FromResult<StoryCatalogueResponse?>(Catalogue(true)), response => visible = response, default);
        Assert.IsTrue(MobileStoryAccessRefresh.FindPlaylist(visible!, "stories")!.Stories.Single().IsLocked);
    }

    [TestMethod]
    public void PlaylistLookupSupportsCompactAndLegacyResponses()
    {
        var compact = Catalogue(false);
        var playlist = MobileStoryAccessRefresh.FindPlaylist(compact, "STORIES");
        Assert.IsNotNull(playlist);
        Assert.AreSame(playlist, MobileStoryAccessRefresh.FindPlaylist(new(false, [playlist], null), "stories"));
        Assert.AreSame(playlist, MobileStoryAccessRefresh.FindPlaylist(new(false, [],
            [new("speellyste", "Playlists", 0, null, [playlist])]), "stories"));
        Assert.IsNull(MobileStoryAccessRefresh.FindPlaylist(compact, "missing"));
    }

    private static MobileSession Session(bool paid, bool fullAccess) => new(
        true, "reader@example.com", "Reader", null, null, null, null,
        paid, fullAccess, [], "", "", "");

    private static StoryCatalogueResponse Catalogue(bool locked)
    {
        var story = new MobileStorySummary("story", "Story", "", "", "", "luister", locked, false, "", null);
        var playlist = new MobilePlaylist("stories", "Stories", null, "", "", [story], ShowcaseStory: story);
        return new(!locked, [], [new("playlist", "Stories", 0, playlist, [])]);
    }
}
