using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileIosCollectionViewCrashSourceTests
{
    [TestMethod]
    public void IosCollectionViewsDisableOffscreenCellPrefetch()
    {
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));

        StringAssert.Contains(project, "<MauiVersion>10.0.100</MauiVersion>");
        StringAssert.Contains(
            mauiProgram,
            "UIKit.UIDevice.CurrentDevice.UserInterfaceIdiom == UIKit.UIUserInterfaceIdiom.Pad");
        StringAssert.Contains(
            mauiProgram,
            "Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler>();");
        StringAssert.Contains(mauiProgram, "ConfigureCollectionViewStability();");
        StringAssert.Contains(mauiProgram, "ViewHandler.ViewMapper.AppendToMapping(");
        StringAssert.Contains(mauiProgram, "view is CollectionView");
        StringAssert.Contains(mauiProgram, "handler.PlatformView is UIKit.UICollectionView collectionView");
        StringAssert.Contains(mauiProgram, "collectionView.PrefetchingEnabled = false;");
    }

    [TestMethod]
    public void IpadCollectionHandlerKeepsTheFullVisualTreatment()
    {
        var liquidGlass = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileLiquidGlass.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var progressiveImage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "ProgressiveCachedImage.cs"));

        StringAssert.Contains(
            liquidGlass,
            "UIBlurEffect.FromStyle(");
        StringAssert.Contains(liquidGlass, "new UIVisualEffectView(materialEffect)");
        Assert.IsFalse(liquidGlass.Contains("SetIpadScrollActive", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "IsAndroid\n            ? null!");
        StringAssert.Contains(progressiveImage, "FadeToAsync(1, FadeInDurationMilliseconds, Easing.CubicOut)");
    }

    [TestMethod]
    public void IosLuisterFeedUsesVirtualizedCollectionView()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(
            luisterPage,
            "private static bool UsesCollectionViewFeed => IsAndroid || IsIOS;");
        StringAssert.Contains(luisterPage, "ItemTemplate = new LuisterFeedTemplateSelector(this)");
        StringAssert.Contains(luisterPage, "ReplaceFeedItems(nextItems);");
    }

    [TestMethod]
    public void IosLuisterPlaylistRowsReuseControlsAndDisplaySizedArtwork()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var apiClient = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var optimizer = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Platforms",
            "iOS",
            "IosImageCacheOptimizer.cs"));

        StringAssert.Contains(luisterPage, "new ReusablePlaylistSectionView(owner)");
        StringAssert.Contains(luisterPage, "new ReusableStoryCarouselCardView(owner)");
        StringAssert.Contains(
            luisterPage,
            "if (!ReferenceEquals(_playlist, playlist) || Math.Abs(_lastWidth - _owner.Width) >= 1)");
        StringAssert.Contains(luisterPage, "if (!string.Equals(_imageKey, imageKey, StringComparison.Ordinal))");
        StringAssert.Contains(luisterPage, "PageHelpers.BuildStoryImageRequest(");
        StringAssert.Contains(luisterPage, "AutomationId = \"favorite-carousel-story\"");
        StringAssert.Contains(luisterPage, "_story = _owner.ResolveCurrentFavoriteState(item.Story);");
        StringAssert.Contains(luisterPage, "ApplyFavoriteOverlayState(_favoriteButton, _story, updateAutomationId: false)");
        StringAssert.Contains(luisterPage, "_image.WidthRequest = coverWidth;");
        StringAssert.Contains(luisterPage, "_image.HeightRequest = coverHeight;");
        StringAssert.Contains(luisterPage, "_artwork.WidthRequest = cardWidth;");
        StringAssert.Contains(luisterPage, "_artwork.HeightRequest = coverHeight;");
        StringAssert.Contains(luisterPage, "HorizontalOptions = LayoutOptions.Fill,");
        StringAssert.Contains(luisterPage, "VerticalOptions = LayoutOptions.Fill,");
        StringAssert.Contains(luisterPage, "maxDegreeOfParallelism: IsAndroid || IsIOS ? 1 : 4");
        StringAssert.Contains(apiClient, "IosImageCacheOptimizer.TryResolveDisplayPath(cachedPath, out cachedPath)");
        StringAssert.Contains(apiClient, "IosImageCacheOptimizer.EnsureOptimized(cachePath, cancellationToken)");
        StringAssert.Contains(optimizer, "ResolveMaxPixelDimension()");
        StringAssert.Contains(optimizer, "PhoneMaxPixelDimension = 1280");
        StringAssert.Contains(optimizer, "TabletMaxPixelDimension = 1280");
        StringAssert.Contains(optimizer, "CreateThumbnailFromImageAlways = true");
        StringAssert.Contains(optimizer, "MaxPixelSize = maxPixelDimension");
    }

    [TestMethod]
    public void IosTabletFeedAvoidsOversizedDecodes()
    {
        var optimizer = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Platforms",
            "iOS",
            "IosImageCacheOptimizer.cs"));
        StringAssert.Contains(optimizer, "TabletMaxPixelDimension = 1280");
    }

    private static string GetRepoPath(params string[] relativeSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file: {Path.Combine(relativeSegments)}");
    }
}
