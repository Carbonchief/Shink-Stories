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
        StringAssert.Contains(menuSheet, "var phoneCardWidth = Math.Max(280, resolvedWidth - 40);");
        Assert.IsFalse(menuSheet.Contains("card.MaximumWidthRequest = -1;", StringComparison.Ordinal));
        Assert.IsFalse(menuSheet.Contains("BuildActionButton(\"Kanselleer\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileMenuHasTopRightAndBelowSettingsCloseControls()
    {
        var menuSheet = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "MobileMenuSheet.cs"));

        StringAssert.Contains(menuSheet, "var cardContent = new Grid");
        StringAssert.Contains(menuSheet, "var panelContent = new Grid");
        StringAssert.Contains(menuSheet, "var bottomCloseButton = BuildGameConfigCloseButton(() => onSelection(null));");
        StringAssert.Contains(menuSheet, "Children =\n                        {\n                            heading,\n                            actionGrid,\n                            bottomCloseButton");
        StringAssert.Contains(menuSheet, "AutomationId = \"mobile-menu-close-bottom\"");
        StringAssert.Contains(menuSheet, "BackgroundColor = Color.FromArgb(\"#FFF4F2\")");
        StringAssert.Contains(menuSheet, "Stroke = Color.FromArgb(\"#E77B78\")");
        StringAssert.Contains(menuSheet, "Color = Color.FromArgb(\"#C93F45\")");
    }

    [TestMethod]
    public void GameDifficultyLabelsRemoveAndroidFontPadding()
    {
        var karakterPareConfig = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "KarakterPareConfigPage.cs"));
        var karakterRaaiConfig = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "KarakterRaaiConfigPage.cs"));

        StringAssert.Contains(karakterPareConfig, "ConfigureAndroidDifficultyLabel(title);");
        StringAssert.Contains(karakterPareConfig, "ConfigureAndroidDifficultyLabel(pairs);");
        StringAssert.Contains(karakterPareConfig, "nativeLabel.SetIncludeFontPadding(false);");
        StringAssert.Contains(karakterRaaiConfig, "ConfigureAndroidDifficultyLabel(title);");
        StringAssert.Contains(karakterRaaiConfig, "ConfigureAndroidDifficultyLabel(rounds);");
        StringAssert.Contains(karakterRaaiConfig, "nativeLabel.SetIncludeFontPadding(false);");
    }

    [TestMethod]
    public void MobileBurgerMenuSlidesInFromRight()
    {
        var menuSheet = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "MobileMenuSheet.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "MobileTopBar.cs"));

        StringAssert.Contains(mobileTopBar, "MobileMenuSheet.ShowFromRightAsync(");
        StringAssert.Contains(menuSheet, "AutomationId = \"mobile-menu-drawer\"");
        StringAssert.Contains(menuSheet, "HorizontalOptions = LayoutOptions.End");
        StringAssert.Contains(menuSheet, "StrokeShape = new RoundRectangle { CornerRadius = 0 }");
        StringAssert.Contains(menuSheet, "Margin = Thickness.Zero");
        StringAssert.Contains(menuSheet, "VerticalOptions = LayoutOptions.Fill");
        StringAssert.Contains(menuSheet, "panel.TranslationX = visuals.ClosedTranslation;");
        StringAssert.Contains(menuSheet, "drawer.Panel.TranslateToAsync(0, 0, DrawerOpenDurationMilliseconds, Easing.CubicOut)");
        StringAssert.Contains(menuSheet, "drawer.Panel.TranslateToAsync(");
        StringAssert.Contains(menuSheet, "drawer.ClosedTranslation");
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
