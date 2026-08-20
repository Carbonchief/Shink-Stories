using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobilePlatformChromeSourceTests
{
    [TestMethod]
    public void TopBarUsesTransparentChromeAndTheReferenceActionLayout()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));

        StringAssert.Contains(source, "Func<Task>? searchAction = null,");
        StringAssert.Contains(source, "BuildBrandMark()");
        StringAssert.Contains(source, "schink_stories_logo_white.png");
        StringAssert.Contains(source, "MobileAndroidIcon.Search");
        StringAssert.Contains(source, "MobileAndroidIcon.Bell");
        StringAssert.Contains(source, "MobileAndroidIcon.CaretDown");
        StringAssert.Contains(source, "BackgroundColor = backgroundColor ?? Colors.Transparent,");
        StringAssert.Contains(source, "MobileAndroidChromePalette.ProfileBackground");
        StringAssert.Contains(source, "backgroundColor ?? Colors.Transparent");
    }

    [TestMethod]
    public void NotificationBellUsesTheSameFontAwesomeSolidGlyphAsTheWebsiteHeader()
    {
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));
        var webLayout = File.ReadAllText(GetRepoPath("Shink", "Components", "Layout", "MainLayout.razor"));

        StringAssert.Contains(webLayout, "fa-solid fa-bell");
        StringAssert.Contains(mobileTopBar, "NotificationBellGlyph = \"\\uf0f3\"");
        StringAssert.Contains(mobileTopBar, "NotificationBellAppleFontFamily = \"Font Awesome 6 Free Solid\"");
        StringAssert.Contains(mobileTopBar, "NotificationBellAndroidFontFamily = \"FontAwesomeSolid\"");
        StringAssert.Contains(mobileTopBar, "Text = NotificationBellGlyph");
    }

    [TestMethod]
    public void BottomBarUsesLargeGlassTabsMatchingTheReferenceNavigation()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileBottomBar.cs"));

        StringAssert.Contains(source, "Label: \"Stories\"");
        StringAssert.Contains(source, "Label: \"Karakters\"");
        StringAssert.Contains(source, "var itemView = BuildBottomTabItem(item.Label, item.AndroidIcon, isSelected);");
        StringAssert.Contains(source, "MobileAndroidChromePalette.BarBackground");
        StringAssert.Contains(source, "BackgroundColor = MobileAndroidChromePalette.BarBackground");
        StringAssert.Contains(source, "Stroke = Colors.Transparent,");
        StringAssert.Contains(source, "StrokeShape = new RoundRectangle { CornerRadius = 0 },");
        StringAssert.Contains(source, "VerticalOptions = LayoutOptions.End,");
        StringAssert.Contains(source, "Padding = new Thickness(10, 2, 10, 4),");
        StringAssert.Contains(source, "Margin = Thickness.Zero,");
        StringAssert.Contains(source, "bar.HeightRequest = 114;");
        StringAssert.Contains(source, "MobileLiquidGlass.Apply(bar, 0, MobileAndroidChromePalette.BarBackground);");
        StringAssert.Contains(source, "SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(source, "WidthRequest = 32,");
        StringAssert.Contains(source, "FontSize = 14,");
    }

    [TestMethod]
    public void AndroidChromeUsesSharedVectorIconDrawable()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileAndroidIcons.cs"));

        StringAssert.Contains(source, "internal sealed class MobileAndroidIconDrawable");
        StringAssert.Contains(source, "MobileAndroidIcon.Menu");
        StringAssert.Contains(source, "MobileAndroidIcon.Bell");
        StringAssert.Contains(source, "MobileAndroidIcon.Download");
        StringAssert.Contains(source, "MobileAndroidIcon.CaretDown");
        StringAssert.Contains(source, "canvas.DrawCircle");
        StringAssert.Contains(source, "BarBackground = Colors.Transparent");
        StringAssert.Contains(source, "SecondaryIcon = Colors.White");
        StringAssert.Contains(source, "TopBarBackground = Colors.Transparent");
    }

    [TestMethod]
    public void NativeGlassUsesColourPreservingDarkBlurRatherThanMilkyRegularGlass()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileLiquidGlass.cs"));

        StringAssert.Contains(source, "UIBlurEffectStyle.SystemUltraThinMaterialDark");
        Assert.IsFalse(source.Contains("UIGlassEffectStyle.Regular", StringComparison.Ordinal));
    }

    private static string GetRepoPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)}.");
    }
}
