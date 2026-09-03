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
        StringAssert.Contains(source, "applyMaterial: false");
        StringAssert.Contains(source, "Background = applyMaterial");
        StringAssert.Contains(source, "public static void ApplyStoriesBackdrop(View overlay, View? captureExclusion = null)");
        StringAssert.Contains(source, "public static View BuildStoriesBackdropLayer(View safeAreaOverlay)");
        StringAssert.Contains(source, "internal const double StoriesBackdropHeight = 92;");
        StringAssert.Contains(source, "DeviceInfo.Current.Platform != DevicePlatform.Android");
        StringAssert.Contains(source, "ApplyStoriesBackdrop(backdropLayer, safeAreaOverlay)");
        StringAssert.Contains(source, "overlay.Background = BuildMaterialBackdropBrush(");
        StringAssert.Contains(source, "MobileLiquidGlass.ApplyTopBar(");
        StringAssert.Contains(source, "TopBarNativeBlurTint");
        StringAssert.Contains(source, "TopBarSurfaceStartTint");
        StringAssert.Contains(source, "TopBarSurfaceEndTint");
        StringAssert.Contains(source, "new GradientStop(Colors.Transparent, 1)");
        StringAssert.Contains(source, "MobileAndroidChromePalette.ProfileBackground");
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
    public void BottomBarUsesFeatheredGlassWithSharedNativeBackdropBlur()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileBottomBar.cs"));
        var palette = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileAndroidIcons.cs"));

        StringAssert.Contains(source, "Label: \"Stories\"");
        StringAssert.Contains(source, "Label: \"Karakters\"");
        StringAssert.Contains(source, "var itemView = BuildBottomTabItem(item.Label, item.AndroidIcon, isSelected);");
        StringAssert.Contains(source, "MobileAndroidChromePalette.BarSurfaceTint");
        StringAssert.Contains(source, "BackgroundColor = Colors.Transparent");
        StringAssert.Contains(source, "Stroke = Colors.Transparent,");
        StringAssert.Contains(source, "StrokeShape = new RoundRectangle { CornerRadius = 0 },");
        StringAssert.Contains(source, "VerticalOptions = LayoutOptions.End,");
        StringAssert.Contains(source, "Padding = new Thickness(10, 2, 10, 4),");
        StringAssert.Contains(source, "Margin = Thickness.Zero,");
        StringAssert.Contains(source, "internal const double NavigationHeight = 124;");
        StringAssert.Contains(source, "internal const double TabSurfaceHeight = 92;");
        StringAssert.Contains(source, "private const double BackdropFeatherHeight = NavigationHeight - TabSurfaceHeight;");
        StringAssert.Contains(source, "private const float BackdropFeatherEnd = (float)(BackdropFeatherHeight / NavigationHeight);");
        StringAssert.Contains(source, "bar.HeightRequest = TabSurfaceHeight;");
        StringAssert.Contains(source, "Background = BuildMaterialBackdropBrush()");
        StringAssert.Contains(source, "new GradientStop(Colors.Transparent, 0)");
        StringAssert.Contains(source, "BarFeatherSoftTint, BackdropFeatherEnd * 0.34f");
        StringAssert.Contains(source, "BarFeatherMidTint, BackdropFeatherEnd * 0.72f");
        StringAssert.Contains(source, "BarSurfaceTint, BackdropFeatherEnd * 1.35f");
        StringAssert.Contains(source, "new GradientStop(MobileAndroidChromePalette.BarSurfaceTint, 1)");
        StringAssert.Contains(source, "Grid.SetRowSpan(materialBackdrop, 2);");
        StringAssert.Contains(source, "Children = { materialBackdrop, bar }");
        StringAssert.Contains(palette, "BarSurfaceTint = Color.FromArgb(\"#10FFFEFA\")");
        StringAssert.Contains(palette, "BarFeatherSoftTint = Color.FromArgb(\"#02FFFEFA\")");
        StringAssert.Contains(palette, "BarFeatherMidTint = Color.FromArgb(\"#08FFFEFA\")");
        StringAssert.Contains(palette, "BarNativeBlurTint = Color.FromArgb(\"#14005E68\")");
        StringAssert.Contains(palette, "TopBarNativeBlurTint = Color.FromArgb(\"#14005E68\")");
        StringAssert.Contains(palette, "PrimaryIcon = Colors.White");
        StringAssert.Contains(source, "MobileLiquidGlass.ApplyBottomBar(");
        StringAssert.Contains(source, "SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(source, "WidthRequest = 32,");
        StringAssert.Contains(source, "FontSize = 14,");
    }

    [TestMethod]
    public void KaraktersUsesTheStoriesSafeAreaChromeHosts()
    {
        var karakters = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KaraktersPage.cs"));

        StringAssert.Contains(karakters, "private const double FloatingTopBarContentInset = 92;");
        StringAssert.Contains(karakters, "private const double BottomBarOverlayHeight = MobileBottomBar.NavigationHeight;");
        StringAssert.Contains(karakters, "private readonly Border _floatingTopBarHost;");
        StringAssert.Contains(karakters, "private readonly Grid _bottomBarOverlay;");
        StringAssert.Contains(karakters, "private readonly ContentView _bottomBarHost;");
        StringAssert.Contains(karakters, "_topBarOverlay.Children.Add(_floatingTopBarHost);");
        StringAssert.Contains(karakters, "MobileTopBar.BuildStoriesBackdropLayer(_topBarOverlay);");
        StringAssert.Contains(karakters, "MobileTopBar.BuildStoriesTopBar(");
        StringAssert.Contains(karakters, "MobileBottomBar.Build(this, \"characters\", OpenStoriesSearchAsync)");
        Assert.DoesNotContain("searchAction: OpenStoriesSearchAsync", karakters, StringComparison.Ordinal);
        StringAssert.Contains(karakters, "_refreshView,\n                topBarBackdropLayer,\n                _topBarOverlay,\n                _bottomBarOverlay");
        StringAssert.Contains(karakters, "SafeAreaEdges = SafeAreaEdges.None,");
        Assert.DoesNotContain("HeightRequest = 70", karakters, StringComparison.Ordinal);
        Assert.DoesNotContain("MobileBottomBar.Build(this, \"characters\")", karakters, StringComparison.Ordinal);
    }

    [TestMethod]
    public void DownloadedUsesTheSharedStoriesChromeAndSelectedDestination()
    {
        var downloaded = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "DownloadedPage.cs"));

        StringAssert.Contains(downloaded, "private const double FloatingTopBarContentInset = 92;");
        StringAssert.Contains(downloaded, "private const double BottomBarOverlayHeight = MobileBottomBar.NavigationHeight;");
        StringAssert.Contains(downloaded, "SafeAreaEdges = SafeAreaEdges.None;");
        StringAssert.Contains(downloaded, "MobileTopBar.BuildStoriesTopBar(");
        StringAssert.Contains(downloaded, "MobileTopBar.BuildStoriesBackdropLayer(topBarOverlay);");
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
        StringAssert.Contains(luister, "MobileTopBar.BuildStoriesBackdropLayer(_topBarOverlay);");
        StringAssert.Contains(search, "MobileTopBar.BuildStoriesBackdropLayer(_topBarOverlay);");
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
    public void NativeBarsUseResizableWebsiteStyleBackdropBlurWithoutLiquidGlassLens()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileLiquidGlass.cs"));

        StringAssert.Contains(source, "UIBlurEffectStyle.SystemUltraThinMaterialDark");
        StringAssert.Contains(source, "GlassContainerTag");
        StringAssert.Contains(source, "private sealed class GlassContainerView : UIView");
        StringAssert.Contains(source, "fadeMask.Frame = Bounds");
        StringAssert.Contains(source, "ApplyEdgeFadeMask(glassContainer, fadeFromTop, fadeFromBottom)");
        StringAssert.Contains(source, "glassView.ContentView.Opaque = false");
        StringAssert.Contains(source, "private const float NativeBarBlurOpacity = 0.88f;");
        StringAssert.Contains(source, "glassView.Alpha = fadeFromTop || fadeFromBottom");
        StringAssert.Contains(source, "? NativeBarBlurOpacity");
        StringAssert.Contains(source, "nativeView.Opaque = false");
        Assert.DoesNotContain("UIGlassEffect.Create", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void NativeGlassAppliesToIphoneAndIpad()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileLiquidGlass.cs"));

        StringAssert.Contains(source, "DeviceInfo.Idiom == DeviceIdiom.Phone || DeviceInfo.Idiom == DeviceIdiom.Tablet");
        StringAssert.Contains(source, "OperatingSystem.IsIOSVersionAtLeast(26)");
        StringAssert.Contains(source, "OperatingSystem.IsIOSVersionAtLeast(15)");
        StringAssert.Contains(source, "ApplyEdgeFadeMask(glassContainer, fadeFromTop, fadeFromBottom)");
        StringAssert.Contains(source, "NSNumber.FromDouble(0.42)");
        StringAssert.Contains(source, "NSNumber.FromDouble(0.92)");
    }

    [TestMethod]
    public void AndroidTopAndBottomBarsRefreshCachedBlurDuringScroll()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileLiquidGlass.cs"));
        var chrome = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileAndroidIcons.cs"));
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));

        StringAssert.Contains(project, "AndroidLibrary Remove=\"Platforms/Android/Libs/BlurView-version-2.0.6.aar\"");
        StringAssert.Contains(source, "AndroidBarGlassView");
        StringAssert.Contains(source, ": Android.Views.View(context)");
        StringAssert.Contains(source, "private void CaptureBackdrop()");
        StringAssert.Contains(source, "captureExclusion?.Handler?.PlatformView as Android.Views.View");
        StringAssert.Contains(source, "blurRoot.Draw(captureCanvas)");
        StringAssert.Contains(source, "ApplyBoxBlur(bitmap, BackdropBlurRadius, BackdropBlurPasses)");
        StringAssert.Contains(source, "BackdropDownsample = 6");
        StringAssert.Contains(source, "BackdropBlurRadius = 4");
        StringAssert.Contains(source, "BackdropBlurPasses = 3");
        StringAssert.Contains(source, "Alpha = NativeBarBlurOpacity");
        StringAssert.Contains(source, "fadeFromTop: false,\n            fadeFromBottom: true");
        StringAssert.Contains(source, "fadeFromTop: true,\n            fadeFromBottom: false");
        StringAssert.Contains(source, "Android.Graphics.PorterDuff.Mode.DstIn");
        StringAssert.Contains(source, "new[] { 0f, 0.24f, 0.68f, 0.94f, 1f, 1f }");
        StringAssert.Contains(source, "new[] { 1f, 1f, 0.78f, 0.32f, 0f }");
        StringAssert.Contains(source, "private bool _scrollCapturePosted;");
        StringAssert.Contains(source, "private Java.Lang.Runnable? _scrollCaptureRunnable;");
        StringAssert.Contains(source, "_scrollCaptureRunnable ??= new(RefreshBackdropForScroll);");
        StringAssert.Contains(source, "if (_isDetached || _scrollCapturePosted)");
        StringAssert.Contains(source, "PostOnAnimation(ScrollCaptureRunnable);");
        StringAssert.Contains(source, "private void RefreshBackdropForScroll()");
        Assert.DoesNotContain("refreshDelay = fadeFromTop ? 180L : 260L", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_scrollRefreshGeneration", source, StringComparison.Ordinal);
        StringAssert.Contains(source, "ScheduleBackdropCapture(650)");
        StringAssert.Contains(source, "PostInvalidateOnAnimation()");
        StringAssert.Contains(chrome, "BarSurfaceTint = Color.FromArgb(\"#10FFFEFA\")");
        StringAssert.Contains(chrome, "BarNativeBlurTint = Color.FromArgb(\"#14005E68\")");
        StringAssert.Contains(chrome, "TopBarNativeBlurTint = Color.FromArgb(\"#14005E68\")");
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
