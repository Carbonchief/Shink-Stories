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
        StringAssert.Contains(source, "CreatePackageImageSource(\"schink_stories_logo_white_raw.png\")");
        StringAssert.Contains(source, "BackgroundColor = Colors.Transparent");
        StringAssert.Contains(source, "MobileAndroidIcon.Search");
        StringAssert.Contains(source, "BuildNotificationButton(notificationCount)");
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
        StringAssert.Contains(mobileTopBar, "AutomationId = \"mobile-top-notifications\"");
        StringAssert.Contains(mobileTopBar, "SemanticProperties.SetDescription(container, \"Kennisgewings\")");
    }

    [TestMethod]
    public void BottomBarUsesLargeTransparentTabsWithoutLiveBackdropBlur()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileBottomBar.cs"));
        var palette = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileAndroidIcons.cs"));

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
        StringAssert.Contains(source, "Color = MobileAndroidChromePalette.BarBackdropTint");
        StringAssert.Contains(source, "Children = { staticBackdrop, bar }");
        StringAssert.Contains(palette, "BarBackdropTint = Color.FromArgb(\"#CC12343B\")");
        Assert.DoesNotContain("MobileLiquidGlass.Apply", source, StringComparison.Ordinal);
        StringAssert.Contains(source, "SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(source, "WidthRequest = 32,");
        StringAssert.Contains(source, "FontSize = 14,");
    }

    [TestMethod]
    public void KaraktersUsesTheStoriesSafeAreaChromeHosts()
    {
        var karakters = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KaraktersPage.cs"));

        StringAssert.Contains(karakters, "private const double FloatingTopBarContentInset = 92;");
        StringAssert.Contains(karakters, "private const double BottomBarOverlayHeight = 152;");
        StringAssert.Contains(karakters, "private readonly Border _floatingTopBarHost;");
        StringAssert.Contains(karakters, "private readonly Grid _bottomBarOverlay;");
        StringAssert.Contains(karakters, "private readonly ContentView _bottomBarHost;");
        StringAssert.Contains(karakters, "_topBarOverlay.Children.Add(_floatingTopBarHost);");
        StringAssert.Contains(karakters, "MobileTopBar.BuildStoriesTopBar(");
        StringAssert.Contains(karakters, "MobileBottomBar.Build(this, \"characters\", OpenStoriesSearchAsync)");
        Assert.DoesNotContain("searchAction: OpenStoriesSearchAsync", karakters, StringComparison.Ordinal);
        StringAssert.Contains(karakters, "_refreshView,\n                _topBarOverlay,\n                _bottomBarOverlay");
        StringAssert.Contains(karakters, "SafeAreaEdges = SafeAreaEdges.None,");
        Assert.DoesNotContain("HeightRequest = 70", karakters, StringComparison.Ordinal);
        Assert.DoesNotContain("MobileBottomBar.Build(this, \"characters\")", karakters, StringComparison.Ordinal);
    }

    [TestMethod]
    public void DownloadedUsesTheSharedStoriesChromeAndSelectedDestination()
    {
        var downloaded = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "DownloadedPage.cs"));

        StringAssert.Contains(downloaded, "private const double FloatingTopBarContentInset = 92;");
        StringAssert.Contains(downloaded, "private const double BottomBarOverlayHeight = 152;");
        StringAssert.Contains(downloaded, "SafeAreaEdges = SafeAreaEdges.None;");
        StringAssert.Contains(downloaded, "MobileTopBar.BuildStoriesTopBar(");
        StringAssert.Contains(downloaded, "MobileBottomBar.Build(this, \"downloads\")");
        StringAssert.Contains(downloaded, "ApplyStoriesTopBar(_topBarHost, width, 1040)");
        StringAssert.Contains(downloaded, "new PersistentNowPlayingBar(_storyPlaybackSession)");
        Assert.DoesNotContain("Shell.Current.GoToAsync(\"..\"", downloaded, StringComparison.Ordinal);
    }

    [TestMethod]
    public void StoriesTopBarsFillTheIpadSafeAreaWidth()
    {
        var responsiveLayout = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileResponsiveLayout.cs"));
        var luister = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var karakters = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KaraktersPage.cs"));
        var search = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SearchPage.cs"));

        StringAssert.Contains(responsiveLayout, "public static void ApplyStoriesTopBar");
        StringAssert.Contains(responsiveLayout, "DeviceInfo.Current.Platform == DevicePlatform.iOS");
        StringAssert.Contains(responsiveLayout, "DeviceInfo.Current.Idiom == DeviceIdiom.Tablet");
        StringAssert.Contains(responsiveLayout, "view.WidthRequest = -1;");
        StringAssert.Contains(responsiveLayout, "view.MaximumWidthRequest = Math.Max(320, availableWidth);");
        StringAssert.Contains(responsiveLayout, "view.HorizontalOptions = LayoutOptions.Fill;");
        StringAssert.Contains(luister, "ApplyStoriesTopBar(_floatingTopBarHost, width, 1040)");
        StringAssert.Contains(karakters, "ApplyStoriesTopBar(_floatingTopBarHost, width, 1040)");
        StringAssert.Contains(search, "ApplyStoriesTopBar(_topBarHost, width, 1040)");
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

    [TestMethod]
    public void NativeGlassAppliesToIphoneAndIpad()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileLiquidGlass.cs"));

        StringAssert.Contains(source, "DeviceInfo.Idiom == DeviceIdiom.Phone || DeviceInfo.Idiom == DeviceIdiom.Tablet");
        StringAssert.Contains(source, "OperatingSystem.IsIOSVersionAtLeast(26)");
    }

    [TestMethod]
    public void AndroidBottomBarAvoidsLiveBackdropCaptureDuringScrolling()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileLiquidGlass.cs"));
        var chrome = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileAndroidIcons.cs"));
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));

        Assert.DoesNotContain("BlurView-version-2.0.6.aar", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Eightbitlab.Com.Blurview.BlurView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PostInvalidateOnAnimation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("#elif ANDROID", source, StringComparison.Ordinal);
        StringAssert.Contains(chrome, "BarBackground = Colors.Transparent");
    }

    [TestMethod]
    public void IosAppIconKeepsCanonicalScaleWhileRemovingAlpha()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));

        StringAssert.Contains(project, "<MauiIcon Include=\"Resources/AppIcon/schink_appicon.png\" />");
        Assert.IsFalse(project.Contains("<MauiIcon Include=\"Resources/AppIcon/schink_appicon.png\" Resize=\"False\"", StringComparison.Ordinal));
        StringAssert.Contains(project, "sips -s format pbm");
        StringAssert.Contains(project, "sips -s format png");
        Assert.IsFalse(project.Contains("pngcrush", StringComparison.Ordinal));
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
