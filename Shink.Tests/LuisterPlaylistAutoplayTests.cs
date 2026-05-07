using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class LuisterPlaylistAutoplayTests
{
    [TestMethod]
    public void PlaylistAutoplayRetriesWhenSourceIsAlreadyReady()
    {
        var script = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "GratisStory.razor.js"));

        StringAssert.Contains(script, "function queueAutoplayAfterSourceChange(audioElement)");
        StringAssert.Contains(script, "audioElement.addEventListener(\"loadeddata\", tryPlay, { once: true });");
        StringAssert.Contains(script, "audioElement.addEventListener(\"canplay\", tryPlay, { once: true });");
        StringAssert.Contains(script, "audioElement.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA");
    }

    [TestMethod]
    public void PlaylistAutoplayRequestIsClearedBeforePlaybackAttempt()
    {
        var script = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "GratisStory.razor.js"));

        var clearIndex = script.IndexOf("audioElement.dataset.autoplayRequested = \"false\";", StringComparison.Ordinal);
        var playIndex = script.IndexOf("playAudioSafely(audioElement);", clearIndex, StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, clearIndex, "Autoplay requests should be cleared before trying playback.");
        Assert.IsGreaterThan(clearIndex, playIndex, "Playback should still be attempted after clearing the request.");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            Path.GetDirectoryName(GetSourceFilePath())!,
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
