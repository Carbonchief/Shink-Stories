using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class LuisterPlaylistResponsiveCssTests
{
    [TestMethod]
    public void PlaylistCoverUsesWideLayoutBetweenTabletAndLaptop()
    {
        var css = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterPlaylist.razor.css"));
        var markup = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterPlaylist.razor"));

        StringAssert.Contains(css, "@media (max-width: 920px)");
        StringAssert.Contains(css, "width: min(100%, calc(100vw - 2rem), 900px);");
        StringAssert.Contains(css, "max-width: none;");
        StringAssert.Contains(markup, "sizes=\"(max-width: 920px) calc(100vw - 2rem), 900px\"");
    }

    [TestMethod]
    public void PlaylistBlockModeCoverUsesViewportWidthWithPadding()
    {
        var css = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterPlaylist.razor.css"));

        StringAssert.Contains(css, "@media (max-width: 720px)");
        StringAssert.Contains(css, "width: min(100%, calc(100vw - 1.8rem));");
        StringAssert.Contains(css, "aspect-ratio: 1 / 1;");
    }

    [TestMethod]
    public void PlaylistBlockModeFavoriteButtonStartsBelowBurgerMenu()
    {
        var css = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterPlaylist.razor.css"));

        StringAssert.Contains(css, ".playlist-page:not(.manual-fullscreen-focus) .story-favorite-toggle");
        StringAssert.Contains(css, "top: max(4.6rem, calc(env(safe-area-inset-top) + 4.1rem));");
        Assert.IsFalse(css.Contains("right: max(4.6rem, calc(env(safe-area-inset-right) + 4.1rem));"));
    }

    [TestMethod]
    public void PlaylistLaptopLayoutRemovesWhiteOuterCorners()
    {
        var css = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "LuisterPlaylist.razor.css"));

        StringAssert.Contains(css, "@media (max-width: 1024px)");
        StringAssert.Contains(css, "border-radius: 0;");
        StringAssert.Contains(css, "background: #222222;");
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
