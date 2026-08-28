using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileResponsiveLayoutSourceTests
{
    [TestMethod]
    public void PhoneLayoutsUseFiniteMaximumWidth()
    {
        var responsiveLayout = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "MobileResponsiveLayout.cs"));

        StringAssert.Contains(
            responsiveLayout,
            "var phoneContentWidth = Math.Max(320, availableWidth - 36);");
        Assert.IsFalse(
            responsiveLayout.Contains(
                "view.MaximumWidthRequest = double.PositiveInfinity;",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void StoryCollectionsUseTabletColumnsAndProportionalArtwork()
    {
        var responsiveLayout = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "MobileResponsiveLayout.cs"));
        var pageHelpers = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "PageHelpers.cs"));

        StringAssert.Contains(responsiveLayout, "ResolveStoryGridColumns");
        StringAssert.Contains(responsiveLayout, "ThreeColumnStoryBreakpoint = 960");
        StringAssert.Contains(responsiveLayout, "ResolveStoryCardArtworkHeight");
        StringAssert.Contains(pageHelpers, "var columns = MobileResponsiveLayout.ResolveStoryGridColumns(width);");
        StringAssert.Contains(pageHelpers, "artworkHeight: artworkHeight");
    }

    [TestMethod]
    public void CharacterGalleryUsesThreeColumnsOnPhonesAndScalesItsArtwork()
    {
        var responsiveLayout = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "MobileResponsiveLayout.cs"));

        StringAssert.Contains(responsiveLayout, "public static int ResolveCharacterGridSpan(double width)");
        StringAssert.Contains(responsiveLayout, "_ => 3");
        StringAssert.Contains(responsiveLayout, "Math.Clamp(cardWidth * 0.86, 88");
    }

    [TestMethod]
    public void MobileMenuUsesCompactCloseControlAndTabletGrid()
    {
        var menuSheet = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "MobileMenuSheet.cs"));

        StringAssert.Contains(menuSheet, "private const double TabletMenuMaximumWidth = 720;");
        StringAssert.Contains(menuSheet, "private const string CloseIconGlyph = \"\\uf00d\";");
        StringAssert.Contains(menuSheet, "AutomationId = \"mobile-menu-close\"");
        StringAssert.Contains(menuSheet, "SemanticProperties.SetDescription(button, \"Maak menu toe\")");
        StringAssert.Contains(menuSheet, "var columnCount = useTabletLayout ? 2 : 1;");
        StringAssert.Contains(menuSheet, "cardHost.VerticalOptions = LayoutOptions.Center;");
        Assert.IsFalse(menuSheet.Contains("BuildActionButton(\"Kanselleer\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterCarouselsMatchWebArtworkAspectRatios()
    {
        var mobileLuister = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "LuisterPage.cs"));
        var webLuisterStyles = File.ReadAllText(GetRepoPath(
            "Shink",
            "Components",
            "Pages",
            "Luister.razor.css"));

        StringAssert.Contains(webLuisterStyles, "aspect-ratio: 3 / 4;");
        StringAssert.Contains(webLuisterStyles, "aspect-ratio: 16 / 9;");
        StringAssert.Contains(mobileLuister, "private const double StoryCarouselImageAspectRatio = 3d / 4d;");
        StringAssert.Contains(mobileLuister, "private const double PlaylistCarouselImageAspectRatio = 16d / 9d;");
        StringAssert.Contains(mobileLuister, "return width / StoryCarouselImageAspectRatio;");
        StringAssert.Contains(mobileLuister, "var artworkHeight = cardWidth / PlaylistCarouselImageAspectRatio;");
        StringAssert.Contains(mobileLuister, "var coverHeight = GetStoryCarouselCoverHeight();");
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
