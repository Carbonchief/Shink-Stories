using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileImageCachingSourceTests
{
    [TestMethod]
    public void ProgressiveImageLoadsPreviewThenFullWithoutRebuildingPages()
    {
        var control = Read("Shink.Mobile", "Pages", "ProgressiveCachedImage.cs");
        var helper = Read("Shink.Mobile", "Pages", "PageHelpers.cs");
        var luister = Read("Shink.Mobile", "Pages", "LuisterPage.cs");

        StringAssert.Contains(control, "request.PreviewImageUrl");
        StringAssert.Contains(control, "await _apiClient.CacheImageSourceAsync(");
        StringAssert.Contains(control, "if (request is null || !_isLoaded)");
        StringAssert.Contains(control, "Source = null;");
        StringAssert.Contains(control, "FadeToAsync(1, FadeInDurationMilliseconds");
        StringAssert.Contains(control, "DeviceInfo.Current.Platform != DevicePlatform.Android");
        StringAssert.Contains(control, "private void OnUnloaded");
        var unloadedStart = control.IndexOf("private void OnUnloaded", StringComparison.Ordinal);
        var applyRequestStart = control.IndexOf("private void ApplyRequest", unloadedStart, StringComparison.Ordinal);
        var showRequestStart = control.IndexOf("private void ShowCurrentRequest", applyRequestStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, unloadedStart);
        Assert.IsGreaterThan(unloadedStart, applyRequestStart);
        Assert.IsGreaterThan(applyRequestStart, showRequestStart);
        var unloadedBody = control[unloadedStart..applyRequestStart];
        var applyRequestBody = control[applyRequestStart..showRequestStart];
        Assert.IsFalse(unloadedBody.Contains("ResetVisual();", StringComparison.Ordinal));
        StringAssert.Contains(unloadedBody, "Opacity = Source is null ? 0 : 1;");
        StringAssert.Contains(applyRequestBody, "ResetVisual();");
        var previewLoad = control.IndexOf("request.PreviewImageUrl,", StringComparison.Ordinal);
        var fullLoad = control.IndexOf("request.FullImageUrl,", previewLoad, StringComparison.Ordinal);
        Assert.IsGreaterThan(previewLoad, fullLoad);
        StringAssert.Contains(control, "Source = source;");
        StringAssert.Contains(helper, "BuildStoryImageRequest(");
        StringAssert.Contains(helper, "BuildStoryCardImageRequest(");
        StringAssert.Contains(helper, "story.ImageUrl");
        StringAssert.Contains(helper, "story.ThumbnailUrl");
        StringAssert.Contains(luister, "PageHelpers.BuildStoryCardImageRequest(");
        Assert.IsFalse(luister.Contains("RenderPlaylistContent(); // image", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ImageDownloadsAreDeduplicatedBoundedAndPersistedOnDisk()
    {
        var client = Read("Shink.Mobile", "Services", "MobileApiClient.cs");

        StringAssert.Contains(client, "ConcurrentDictionary<string, Task<string?>> _imageCacheTasks");
        StringAssert.Contains(client, "ImageCacheConcurrency = 2");
        StringAssert.Contains(client, "SemaphoreSlim _imageDownloadSlots = new(ImageCacheConcurrency, ImageCacheConcurrency)");
        StringAssert.Contains(client, "_imageCacheTasks.GetOrAdd(");
        StringAssert.Contains(client, "System.IO.Path.Combine(FileSystem.CacheDirectory, \"story-images\")");
        StringAssert.Contains(client, "const long maxCacheBytes = 512L * 1024L * 1024L;");
        StringAssert.Contains(client, "TimeSpan.FromDays(180)");
        StringAssert.Contains(client, "HttpCompletionOption.ResponseHeadersRead");
        StringAssert.Contains(client, "contentType.StartsWith(\"image/\"");
    }

    [TestMethod]
    public void PlatformDecodeCachesBoundFeedArtworkWithoutLosingTabletQuality()
    {
        var android = Read("Shink.Mobile", "Platforms", "Android", "AndroidImageCacheOptimizer.cs");
        var ios = Read("Shink.Mobile", "Platforms", "iOS", "IosImageCacheOptimizer.cs");

        StringAssert.Contains(android, "PhoneMaxPixelDimension = 1280");
        StringAssert.Contains(android, "TabletMaxPixelDimension = 2048");
        StringAssert.Contains(ios, "PhoneMaxPixelDimension = 1280");
        StringAssert.Contains(ios, "TabletMaxPixelDimension = 2048");
        StringAssert.Contains(ios, "PhoneOptimizedSuffix = \".ios-feed\"");
        StringAssert.Contains(ios, "TryResolveDisplayPath(");
        Assert.IsFalse(android.Contains("Math.Max(display.Width, display.Height)", StringComparison.Ordinal));
        Assert.IsFalse(ios.Contains("Math.Max(display.Width, display.Height)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void IosImagePreparationNeverRunsInsideSynchronousCellBinding()
    {
        var client = Read("Shink.Mobile", "Services", "MobileApiClient.cs");

        StringAssert.Contains(client, "if (!IosImageCacheOptimizer.TryResolveDisplayPath(cachedPath, out cachedPath))");
        StringAssert.Contains(client, "Task.Run(async () =>");
        StringAssert.Contains(client, "CacheImageCoreWithinSlotAsync(imageUrl, activityToken).ConfigureAwait(false)");
    }

    [TestMethod]
    public void RemotePageImagesUseTheSharedProgressiveControl()
    {
        var pagesDirectory = Path.Combine(FindRepoRoot(), "Shink.Mobile", "Pages");
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(pagesDirectory, "*.cs")
                .Select(path => File.ReadAllText(path)));

        StringAssert.Contains(source, "new ProgressiveCachedImage(");
        Assert.IsFalse(source.Contains("Source = _apiClient.BuildImageUrl(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Source = ImageSource.FromUri(new Uri(imageUrl", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CharacterApiPublishesExistingThumbnailAssetsAsPreviewUrls()
    {
        var program = Read("Shink", "Program.cs");
        var models = Read("Shink.Mobile", "Models", "MobileApiModels.cs");

        StringAssert.Contains(program, "ResolveMobileCharacterPreviewImageUrl(");
        StringAssert.Contains(program, "environment.WebRootPath");
        StringAssert.Contains(program, "File.Exists(physicalPath)");
        StringAssert.Contains(program, "PreviewImageUrl: ResolveMobileCharacterPreviewImageUrl(");
        StringAssert.Contains(models, "string? PreviewImageUrl = null");
        StringAssert.Contains(models, "string? MatchPreviewImageUrl = null");
        StringAssert.Contains(models, "string? MysteryPreviewImageUrl = null");
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepoRoot(), .. segments]));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Shink-Stories.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
