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
    public void SharedStoryPlayerKeepsInitialDurationWhileMetadataLoads()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor.js"));

        StringAssert.Contains(script, "function resolveAudioDurationForDisplay(audioElement)");
        StringAssert.Contains(script, "audioElement.dataset.storyDurationSeconds");
        StringAssert.Contains(script, "function resolveAudioCurrentTimeForDisplay(audioElement, duration)");
        StringAssert.Contains(script, "loadStoryProgress(audioElement)");
        StringAssert.Contains(script, "const duration = resolveAudioDurationForDisplay(audioElement);");
        StringAssert.Contains(script, "const currentTime = resolveAudioCurrentTimeForDisplay(audioElement, duration);");
        Assert.IsFalse(
            script.Contains("const duration = Number.isFinite(audioElement.duration) ? audioElement.duration : 0;", StringComparison.Ordinal),
            "The custom player should not overwrite an initial catalog duration with 0 while browser metadata is pending.");
    }

    [TestMethod]
    public void SharedStoryPlayerKeepsTimeRowBusyUntilMetadataIsReady()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor.js"));

        StringAssert.Contains(script, "const MEDIA_HAVE_METADATA = 1;");
        StringAssert.Contains(script, "const TIME_ROW_SELECTOR = \".story-time-row\";");
        StringAssert.Contains(script, "function isAudioTimeDisplayLoading(audioElement)");
        StringAssert.Contains(script, "audioElement.readyState < MEDIA_HAVE_METADATA");
        StringAssert.Contains(script, "timeRow.classList.toggle(\"is-loading\", isLoading);");
        StringAssert.Contains(script, "timeRow.setAttribute(\"aria-busy\", String(isLoading));");
        StringAssert.Contains(script, "audioElement.addEventListener(\"loadstart\", updateCustomPlayerState);");
        StringAssert.Contains(script, "audioElement.addEventListener(\"emptied\", updateCustomPlayerState);");
        StringAssert.Contains(script, "audioElement.addEventListener(\"canplay\", updateCustomPlayerState);");
    }

    [TestMethod]
    public void MobileStoryPlayerPostsViewAndListenTracking()
    {
        var page = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));

        StringAssert.Contains(page, "TrackStoryViewAsync(detail.Story.Slug, detail.Story.Source)");
        StringAssert.Contains(page, "FlushPendingListen(\"progress\", force: false)");
        StringAssert.Contains(page, "FlushPendingListen(\"pause\", force: true)");
        StringAssert.Contains(page, "FlushPendingListen(\"ended\", force: true, isCompleted: true)");
        StringAssert.Contains(page, "FlushPendingListen(\"pagehide\", force: true)");
        StringAssert.Contains(page, "BeginListenTracking(detail, trackingSessionId);");
        Assert.IsFalse(page.Contains("eventType,\n            \"play\",", StringComparison.Ordinal));
        StringAssert.Contains(page, "StartProgressTimer()");
        StringAssert.Contains(page, "UpdateProgressState()");
        Assert.IsFalse(page.Contains("schink-track://listen?", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains("visibilityhidden", StringComparison.Ordinal));
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
