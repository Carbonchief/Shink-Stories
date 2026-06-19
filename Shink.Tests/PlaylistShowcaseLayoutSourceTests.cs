using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class PlaylistShowcaseLayoutSourceTests
{
    [TestMethod]
    public void ThreeStoryShowcaseGridsUseCenteredLayout()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterPlaylistShowcase.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterPlaylistShowcase.razor.css"));

        StringAssert.Contains(markup, "PlaylistShowcaseGridClass");
        StringAssert.Contains(markup, "CurrentPlaylistStories.Count == 3");
        StringAssert.Contains(markup, "\"playlist-showcase-grid is-three-story-grid\"");
        StringAssert.Contains(css, ".playlist-showcase-grid.is-three-story-grid");
        StringAssert.Contains(css, "grid-template-columns: repeat(3, 220px);");
        StringAssert.Contains(css, ".playlist-showcase-grid.is-three-story-grid .playlist-showcase-card:nth-child(3):last-child");
        StringAssert.Contains(css, "grid-column: 1 / -1;");
        StringAssert.Contains(css, "justify-self: center;");
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
