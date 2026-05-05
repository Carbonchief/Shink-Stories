using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class StoryTrackingSourceTests
{
    [TestMethod]
    public void SharedStoryPlayerRefreshesTrackingWhenPlaylistAudioChanges()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor.js"));

        StringAssert.Contains(script, "function refreshStoryTrackingState(audioElement, shouldTrackView)");
        StringAssert.Contains(script, "trackingStateMatchesAudio(currentState, audioElement)");
        StringAssert.Contains(script, "storyTrackingStateCache.delete(audioElement);");
        StringAssert.Contains(script, "refreshStoryTrackingState(audioElement, true);");
        StringAssert.Contains(script, "const trackingState = getStoryTrackingState(audioElement) ?? refreshStoryTrackingState(audioElement, true);");
    }

    [TestMethod]
    public void MobileStoryPlayerPostsViewAndListenTracking()
    {
        var page = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));

        StringAssert.Contains(page, "TrackStoryViewAsync(detail.Story.Slug, detail.Story.Source)");
        StringAssert.Contains(page, "schink-track://listen?");
        StringAssert.Contains(page, "TrackMobileListenAsync(detail, trackingSessionId, uri)");
        StringAssert.Contains(page, "visibilityhidden");
        StringAssert.Contains(client, "TrackStoryViewAsync(string slug, string source");
        StringAssert.Contains(client, "TrackStoryListenAsync(");
        StringAssert.Contains(client, "\"/api/stories/{Uri.EscapeDataString(slug)}/view\"");
        StringAssert.Contains(client, "\"/api/stories/{Uri.EscapeDataString(slug)}/listen\"");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
