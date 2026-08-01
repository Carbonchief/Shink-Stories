using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileLuisterSafeAreaSourceTests
{
    [TestMethod]
    public void LuisterTopMenuRowSitsAtTheTopOfTheSafeArea()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(source, "private const double FloatingTopBarContentInset = 132;");
        StringAssert.Contains(source, "Margin = new Thickness(18, 0, 18, 0),");
        StringAssert.Contains(source, "Padding = new Thickness(0, 0, 0, 16),");
        StringAssert.Contains(source, "InputTransparent = false,");
        Assert.DoesNotContain("Margin = new Thickness(18, 18, 18, 0),", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HeightRequest = FloatingTopBarContentInset,", source, StringComparison.Ordinal);
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
    public void LuisterInitialContentStartsBelowTheTopButtons()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(source, "private const double FloatingTopBarContentInset = 132;");
        StringAssert.Contains(source, "HeightRequest = FloatingTopBarContentInset - 14,");
        StringAssert.Contains(
            source,
            "Padding = new Thickness(PageHorizontalPadding, FloatingTopBarContentInset, PageHorizontalPadding, 28),");
    }

    [TestMethod]
    public void LuisterTopMenuDecorationsDoNotInterceptTaps()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(source, "var notificationSurface = BuildHeaderCircleButton");
        StringAssert.Contains(source, "notificationSurface.InputTransparent = true;");
        StringAssert.Contains(source, "VerticalOptions = LayoutOptions.Center,\n                InputTransparent = true,\n                Children =");
        StringAssert.Contains(source, "Margin = text == \"⌕\" ? new Thickness(0, -2, 0, 0) : Thickness.Zero,\n                InputTransparent = true");
        StringAssert.Contains(source, "HeightRequest = 46,\n                InputTransparent = true");
    }

    [TestMethod]
    public void LuisterArtworkDoesNotShowLockedTextBadges()
    {
        var source = File.ReadAllText(FindLuisterPage());

        Assert.DoesNotContain("BuildLockedBadge", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Gesluit\"", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void LuisterScrollsEdgeToEdgeWhileKeepingTheTopBarInsideTheSafeArea()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(
            source,
            "Header = new Grid\n                {\n                    SafeAreaEdges = new SafeAreaEdges(\n                        SafeAreaRegions.None,\n                        SafeAreaRegions.Container,\n                        SafeAreaRegions.None,\n                        SafeAreaRegions.None),");
        StringAssert.Contains(
            source,
            "_content = new VerticalStackLayout\n            {\n                SafeAreaEdges = new SafeAreaEdges(\n                    SafeAreaRegions.None,\n                    SafeAreaRegions.Container,\n                    SafeAreaRegions.None,\n                    SafeAreaRegions.None),");
        StringAssert.Contains(
            source,
            "_scrollView = new ScrollView\n            {\n                SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(
            source,
            "_refreshView = new RefreshView\n        {\n            SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(
            source,
            "_rootLayout = new Grid\n        {\n            SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(
            source,
            "_topBarOverlay = new Grid\n        {\n            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),");
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
