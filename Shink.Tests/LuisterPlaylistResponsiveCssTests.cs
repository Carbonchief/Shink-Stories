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
    public void LuisterShowcaseImageIsLargerOnDesktopAndSquareOnMobile()
    {
        var css = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "Luister.razor.css"));

        StringAssert.Contains(css, "@media (min-width: 981px)");
        StringAssert.Contains(css, "width: min(100%, 936px);");
        StringAssert.Contains(css, "@media (max-width: 680px)");
        StringAssert.Contains(css, ".luister-playlist-showcase-cover");
        StringAssert.Contains(css, "aspect-ratio: 1 / 1;");
    }

    [TestMethod]
    public void LuisterMusicStoryCardsUseSquareArtwork()
    {
        var markup = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "Luister.razor"));
        var css = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "Luister.razor.css"));
        var catalogService = File.ReadAllText(GetRepoPath(
            "Shink",
            "Services",
            "SupabaseStoryCatalogService.cs"));

        StringAssert.Contains(catalogService, "story_type,access_level");
        StringAssert.Contains(catalogService, "StoryType: NormalizeStoryType(row.StoryType)");
        StringAssert.Contains(markup, "IsMusicStory(story)");
        StringAssert.Contains(markup, "classes.Add(\"is-music\")");
        StringAssert.Contains(css, ".story-carousel-item.is-music .story-carousel-cover");
        StringAssert.Contains(css, ".story-carousel-item.is-music .story-carousel-image");
        StringAssert.Contains(css, "aspect-ratio: 1 / 1;");
    }

    [TestMethod]
    public void GratisStoriesSectionUsesBadgeWrapperOnLuisterPage()
    {
        var markup = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "Luister.razor"));
        var css = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "Luister.razor.css"));

        StringAssert.Contains(markup, "BuildPlaylistSectionClass(playlist)");
        StringAssert.Contains(markup, "StoryAccessPolicy.GratisPlaylistSlug");
        StringAssert.Contains(markup, "luister-gratis-stories-section");
        StringAssert.Contains(css, ".luister-gratis-stories-section .stories-carousel-shell");
        StringAssert.Contains(css, "border: 4px solid #f2b705;");
        StringAssert.Contains(css, "linear-gradient(180deg, #ffe970 0%, #ffdf41 55%, #ffd928 100%);");
        StringAssert.Contains(css, ".luister-gratis-stories-section .stories-carousel");
        StringAssert.Contains(css, "scroll-padding-inline: 0;");
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
