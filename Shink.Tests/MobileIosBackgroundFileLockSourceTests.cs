using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileIosBackgroundFileLockSourceTests
{
    [TestMethod]
    public void ImageCacheWorkersStopBeforeIosSuspendsTheApp()
    {
        var lifecycle = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Services",
            "MobileAppLifecycleService.cs"));
        var apiClient = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Services",
            "MobileApiClient.cs"));

        StringAssert.Contains(lifecycle, "_apiClient.SuspendImageCacheActivity();");
        StringAssert.Contains(lifecycle, "BeginIosImageCacheQuiescence(imageCacheQuiescence);");
        StringAssert.Contains(lifecycle, "application.BeginBackgroundTask(");
        StringAssert.Contains(lifecycle, "_apiClient.ResumeImageCacheActivity();");
        StringAssert.Contains(apiClient, "GetImageCacheActivityToken()");
        StringAssert.Contains(apiClient, "CacheImageCoreWithinSlotAsync(imageUrl, activityToken)");
        StringAssert.Contains(apiClient, "CopyToAsync(fileStream, cancellationToken)");
        StringAssert.Contains(apiClient, "IosImageCacheOptimizer.EnsureOptimized(cachePath, cancellationToken)");
        Assert.IsFalse(
            ExtractImageCacheCore(apiClient).Contains("CancellationToken.None", StringComparison.Ordinal),
            "Image cache file work must remain cancellable before iOS suspension.");
    }

    private static string ExtractImageCacheCore(string source)
    {
        var start = source.IndexOf(
            "private Task<string?> CacheImageCoreAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private static void MaintainImageCache",
            start,
            StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, "Could not locate the image cache worker source.");
        return source[start..end];
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
