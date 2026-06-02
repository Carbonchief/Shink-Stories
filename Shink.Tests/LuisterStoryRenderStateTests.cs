using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class LuisterStoryRenderStateTests
{
    [TestMethod]
    public void LuisterStoryKeepsPlayerShellDuringInteractiveStateRestore()
    {
        var source = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterStory.razor"));

        StringAssert.Contains(source, "@inject PersistentComponentState PersistentComponentState");
        StringAssert.Contains(source, "@if (ShouldShowStoryLoading)");
        StringAssert.Contains(source, "private bool ShouldShowStoryLoading => IsStoryLoading && CurrentStory is null;");
        StringAssert.Contains(source, "TryRestorePersistedStoryState();");
        StringAssert.Contains(source, "IsStoryLoading = CurrentStory is null;");
        StringAssert.Contains(source, "PersistentComponentState.PersistAsJson(");
    }

    [TestMethod]
    public void LuisterStoryInitializesPlayerDurationFromCatalogState()
    {
        var source = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterStory.razor"));

        StringAssert.Contains(source, "data-story-duration-seconds=\"@FormatStoryDurationSeconds(CurrentStory.DurationSeconds)\"");
        StringAssert.Contains(source, "<span class=\"story-time-total\">@FormatStoryTime(CurrentStory.DurationSeconds)</span>");
        StringAssert.Contains(source, "private static string FormatStoryTime(decimal? value)");
        StringAssert.Contains(source, "private static string FormatStoryDurationSeconds(decimal? value)");
        Assert.IsFalse(
            source.Contains("<span class=\"story-time-total\">0:00</span>", StringComparison.Ordinal),
            "The prerendered total duration should not reset to 0:00 when the catalog already knows the story duration.");
    }

    [TestMethod]
    public void LuisterStoryShowsCompactTimeSpinnerUntilAudioMetadataIsReady()
    {
        var source = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterStory.razor"));
        var css = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterStory.razor.css"));

        StringAssert.Contains(source, "<div class=\"story-time-row is-loading\" aria-live=\"polite\" aria-busy=\"true\">");
        StringAssert.Contains(source, "<i class=\"story-time-loading\" aria-hidden=\"true\"></i>");
        StringAssert.Contains(css, ".story-time-row.is-loading .story-time-loading");
        StringAssert.Contains(css, ".story-time-row.is-loading .story-time-current,");
        StringAssert.Contains(css, "animation: story-loading-spin 760ms linear infinite;");
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
