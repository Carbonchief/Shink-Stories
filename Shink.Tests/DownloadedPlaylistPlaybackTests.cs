using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public class DownloadedPlaylistPlaybackTests
{
    [TestMethod]
    public void StartingInTheMiddleFollowsSavedAudioOrderAndKeepsTheStorySource()
    {
        var paid = Story("shared", "luister");
        var free = Story("shared", "gratis");
        var last = Story("last", "luister");
        var queue = PlaylistPlaybackState.CreateOfflineQueue([paid, free, Story("video", "luister", "video"), last], autoplay: true);

        Assert.IsTrue(queue.CanAutoplayAdvance(free));
        Assert.AreEqual(free, queue.GetNextAudioStory(paid));
        Assert.AreEqual(last, queue.GetNextAudioStory(free));
        Assert.IsNull(queue.GetNextAudioStory(last));
        Assert.IsNull(queue.GetNextAudioStory(Story("absent", "gratis")));
    }

    [TestMethod]
    public void ShuffleVisitsEachDownloadOnceAndStopsAtTheEnd()
    {
        var stories = Enumerable.Range(1, 6).Select(i => Story(i.ToString(), "gratis")).ToArray();
        var queue = PlaylistPlaybackState.CreateOfflineQueue(stories, autoplay: true);
        var selected = stories[3];
        queue.SetShuffle(true, selected);
        var order = queue.GetPlaybackStories(selected).ToArray();

        Assert.AreEqual(selected, order[0]);
        CollectionAssert.AreEquivalent(stories, order);
        for (var index = 0; index < order.Length; index++)
        {
            Assert.AreEqual(order.ElementAtOrDefault(index + 1), queue.GetNextAudioStory(order[index]));
        }
    }

    [TestMethod]
    public void PreparingAnotherQueueLeavesThePlayingQueuesLimitAndSettingsIntact()
    {
        var stories = Enumerable.Range(1, 4).Select(i => Story(i.ToString(), "gratis")).ToArray();
        var playing = PlaylistPlaybackState.CreateOfflineQueue(stories, autoplay: true);
        playing.SetAutoplayLimit(3, stories[0]);
        var browsing = PlaylistPlaybackState.CreateOfflineQueue(stories.Reverse(), autoplay: false);
        Assert.IsFalse(browsing.CanAutoplayAdvance(stories[1]));

        Assert.IsTrue(playing.CanAutoplayAdvance(stories[0]));
        playing.TrackAutoplayAdvance(stories[1]);
        Assert.IsTrue(playing.CanAutoplayAdvance(stories[1]));
        playing.TrackAutoplayAdvance(stories[2]);
        Assert.IsFalse(playing.CanAutoplayAdvance(stories[2]));
        Assert.AreEqual(stories[1], playing.GetNextAudioStory(stories[0]));
    }

    private static MobileStorySummary Story(string slug, string source, string type = "story") =>
        new(slug, slug, "", "", "", source, false, false, "", 10, type);
}
