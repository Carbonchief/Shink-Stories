using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileLuisterSafeAreaSourceTests
{
    [TestMethod]
    public void LuisterNativeAppChromeUsesSeparateTopAndBottomSafeAreas()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(source, "private const double FloatingTopBarContentInset = 92;");
        StringAssert.Contains(source, "private const double BottomBarContentInset = 216;");
        StringAssert.Contains(source, "private const double BottomBarOverlayHeight = 152;");
        StringAssert.Contains(source, "Margin = Thickness.Zero,");
        StringAssert.Contains(source, "HeightRequest = 62,");
        StringAssert.Contains(source, "BackgroundColor = Colors.Transparent,");
        StringAssert.Contains(source, "InputTransparent = false,");
        StringAssert.Contains(source, "_bottomBarOverlay = new Grid");
        StringAssert.Contains(source, "HeightRequest = BottomBarOverlayHeight,");
        StringAssert.Contains(source, "MobileBottomBar.Build(this, \"listen\", OpenStoriesSearchAsync)");
    }

    [TestMethod]
    public void LuisterTopMenuHostExistsBeforeTheFirstLayoutPass()
    {
        var source = File.ReadAllText(FindLuisterPage());
        var hostIndex = source.IndexOf("_floatingTopBarHost = new Border", StringComparison.Ordinal);
        var rootIndex = source.IndexOf("_rootLayout = new Grid", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, hostIndex);
        Assert.IsGreaterThan(hostIndex, rootIndex);
        StringAssert.Contains(source, "private readonly Border _floatingTopBarHost;");
        StringAssert.Contains(source, "_topBarOverlay.Children.Add(_floatingTopBarHost);");
        Assert.DoesNotContain("_floatingTopBarHost is null", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void LuisterStoriesHeroStartsBelowTheTransparentTopButtons()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(source, "private const double FloatingTopBarContentInset = 92;");
        StringAssert.Contains(source, "Header = BuildStoriesPageHeader(),");
        StringAssert.Contains(source, "content.Children.Add(BuildStoriesPageHeader());");
        StringAssert.Contains(source, "private static View BuildStoriesPageHeader()");
        StringAssert.Contains(source, "new BoxView { HeightRequest = FloatingTopBarContentInset, Color = Colors.Transparent },");
        StringAssert.Contains(source, "private static View BuildStoriesHero()");
        StringAssert.Contains(source, "Source = \"stories_hero_overlay.png\"");
        StringAssert.Contains(source, "BackgroundColor = Colors.Transparent,");
        StringAssert.Contains(source, "HeightRequest = StoriesHeroHeight,");
        StringAssert.Contains(source, "ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,");
        StringAssert.Contains(source, "Margin = Thickness.Zero,");
        StringAssert.Contains(source, "HeightRequest = BottomBarContentInset,");
    }

    [TestMethod]
    public void LuisterNativeAppBarKeepsMenuNotificationsAndProfileActions()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(source, "return MobileTopBar.BuildStoriesTopBar(");
        Assert.DoesNotContain("searchAction: OpenStoriesSearchAsync", source, StringComparison.Ordinal);
        StringAssert.Contains(source, "notificationAction: ShowNotificationsAsync");
        StringAssert.Contains(source, "notificationCount: _notificationPage?.UnreadCount ?? 0");
    }

    [TestMethod]
    public void LuisterArtworkDoesNotShowLockedTextBadges()
    {
        var source = File.ReadAllText(FindLuisterPage());

        Assert.DoesNotContain("BuildLockedBadge", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Gesluit\"", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void LuisterScrollsEdgeToEdgeWhileKeepingBothBarsInsideTheSafeArea()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(
            source,
            "Header = BuildStoriesPageHeader(),");
        StringAssert.Contains(source, "SafeAreaRegions.Container,");
        StringAssert.Contains(
            source,
            "_refreshView = new RefreshView\n        {\n            SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(
            source,
            "_rootLayout = new Grid\n        {\n            SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(
            source,
            "_topBarOverlay = new Grid\n        {\n            SafeAreaEdges = new SafeAreaEdges(\n                SafeAreaRegions.Container,\n                SafeAreaRegions.Container,\n                SafeAreaRegions.Container,\n                SafeAreaRegions.None),");
        StringAssert.Contains(source, "_bottomBarOverlay = new Grid");
        StringAssert.Contains(source, "SafeAreaEdges = SafeAreaEdges.None,");
    }

    [TestMethod]
    public void AndroidFeedKeepsGuttersOnItemsSoCarouselsCanReachTheScreenEdge()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(
            source,
            "// Keep the feed itself edge-to-edge on Android so a carousel's\n                // negative side margin can reach the screen edge.");
        StringAssert.Contains(source, "Margin = Thickness.Zero,");
        StringAssert.Contains(
            source,
            "var container = new ContentView\n        {\n            Padding = new Thickness(PageHorizontalPadding, 0)\n        };");
    }

    [TestMethod]
    public void LuisterCarouselArtworkAlignsWithShowcaseArtwork()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(source, "private const double CarouselItemSpacing = 14;");
        StringAssert.Contains(
            source,
            "private const double CarouselEdgeSpacerWidth = PageHorizontalPadding - CarouselItemSpacing;");
        StringAssert.Contains(
            source,
            "IsIOS ? PageHorizontalPadding : CarouselEdgeSpacerWidth;");
        StringAssert.Contains(source, "WidthRequest = ResolveCarouselEdgeSpacerWidth(),");
        StringAssert.Contains(source, "ItemSpacing = CarouselItemSpacing,");
    }

    private static string FindLuisterPage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Shink.Mobile", "Pages", "LuisterPage.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate Shink.Mobile/Pages/LuisterPage.cs.");
    }
}
