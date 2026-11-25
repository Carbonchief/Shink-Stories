using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public class MobileAutoplayDefaultsTests
{
    private static readonly MobileStorySummary First = new("first", "First", "", "", "", "luister", false, false, "", 10);
    private static readonly MobileStorySummary Next = First with { Slug = "next", Title = "Next" };
    private static readonly MobilePlaylist Playlist = new("bedtime", "Slaaptyd", null, "", "", [First, Next]);

    [TestMethod]
    public void AutoplayIsOffUntilAPlaylistIsSelected()
    {
        var state = new PlaylistPlaybackState();
        Assert.IsFalse(state.CanAutoplayAdvance(First));
        state.Set(Playlist, First);
        Assert.IsTrue(state.CanAutoplayAdvance(First));
        state.SetAutoplay(false);
        state.Set(Playlist, Next);
        Assert.IsFalse(state.CanAutoplayAdvance(Next));
        state.Set(Playlist with { Slug = "another" });
        Assert.IsTrue(state.CanAutoplayAdvance(First));
        state.Clear();
        Assert.IsFalse(state.CanAutoplayAdvance(First));
    }

    [TestMethod]
    public void SelectingAStoryKeepsItsNeighboursWithoutEnablingAutoplay()
    {
        var selected = new PlaylistPlaybackState();
        selected.Set(Playlist);
        var story = PlaylistPlaybackState.ForStory(Playlist, First);
        Assert.AreEqual(Next, story.GetNextAudioStory(First));
        Assert.IsFalse(story.CanAutoplayAdvance(First));
        Assert.IsTrue(selected.CanAutoplayAdvance(First));
        story.SetAutoplay(true);
        Assert.IsTrue(story.CanAutoplayAdvance(First));
        Assert.IsFalse(PlaylistPlaybackState.ForStory(Playlist, Next).CanAutoplayAdvance(Next));
    }

    [TestMethod]
    public void ResumingRestoresAutoplayOnlyWhenTheSavedQueueIsAvailable()
    {
        Assert.IsFalse(PlaylistPlaybackState.ForStory(Playlist, First, autoplay: false).CanAutoplayAdvance(First));
        Assert.IsTrue(PlaylistPlaybackState.ForStory(Playlist, First, autoplay: true).CanAutoplayAdvance(First));
        Assert.IsFalse(PlaylistPlaybackState.ForStory(null, First, autoplay: true).CanAutoplayAdvance(First));
        Assert.IsFalse(PlaylistPlaybackState.CreateOfflineQueue([First, Next], autoplay: false).CanAutoplayAdvance(First));
        Assert.IsTrue(PlaylistPlaybackState.CreateOfflineQueue([First, Next], autoplay: true).CanAutoplayAdvance(First));
    }
}
