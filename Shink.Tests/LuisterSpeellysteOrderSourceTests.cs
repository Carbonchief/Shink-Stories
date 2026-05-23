using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class LuisterSpeellysteOrderSourceTests
{
    [TestMethod]
    public void LuisterRendersSpeellysteThroughOrderedPlaylistSections()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Luister.razor"));
        var catalogService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoryCatalogService.cs"));

        StringAssert.Contains(markup, "BuildOrderedLuisterSections()");
        StringAssert.Contains(markup, "LuisterSectionKind.Speellyste");
        StringAssert.Contains(markup, "RenderSpeellysteSection(section.SpeellystePlaylists)");
        Assert.IsFalse(
            markup.Contains("var speellysteCarouselPlaylists = GetSpeellysteCarouselPlaylists();", StringComparison.Ordinal),
            "Speellyste should no longer be hardcoded ahead of the playlist loop.");

        StringAssert.Contains(catalogService, "SpeellysteSystemKey");
        StringAssert.Contains(catalogService, "IsSpeellysteSystemPlaylist");
        StringAssert.Contains(catalogService, "BuildSpeellysteSystemPlaylist");
        StringAssert.Contains(catalogService, "BuildSpeellysteSystemPlaylist(playlistRow)");
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
