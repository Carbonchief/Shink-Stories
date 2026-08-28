using Foundation;
using ImageIO;

namespace Shink.Mobile.Platforms.iOS;

internal static class IosImageCacheOptimizer
{
    // Feed artwork is never displayed against the device's longest edge. Using
    // that value made an iPhone 15 Pro prepare 2556px images for ~250px carousel
    // cards, multiplying decode memory whenever a new row entered the viewport.
    // Keep the proven phone cache suffix so existing 1280px files remain useful.
    private const int PhoneMaxPixelDimension = 1280;
    private const int TabletMaxPixelDimension = 2048;
    private const string PhoneOptimizedSuffix = ".ios-feed";

    public static string ResolveDisplayPath(string cachePath)
    {
        return TryResolveDisplayPath(cachePath, out var displayPath)
            ? displayPath
            : cachePath;
    }

    public static bool TryResolveDisplayPath(string cachePath, out string displayPath)
    {
        var optimizedPath = BuildOptimizedPath(cachePath, ResolveMaxPixelDimension());
        if (File.Exists(optimizedPath) && new FileInfo(optimizedPath).Length > 0)
        {
            displayPath = optimizedPath;
            return true;
        }

        displayPath = string.Empty;
        return false;
    }

    public static void EnsureOptimized(string cachePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var maxPixelDimension = ResolveMaxPixelDimension();
        var optimizedPath = BuildOptimizedPath(cachePath, maxPixelDimension);
        if (File.Exists(optimizedPath) && new FileInfo(optimizedPath).Length > 0)
        {
            return;
        }

        var temporaryPath = $"{optimizedPath}.tmp";
        try
        {
            using var sourceUrl = NSUrl.FromFilename(cachePath);
            using var source = CGImageSource.FromUrl(sourceUrl);
            if (source is null || source.ImageCount == 0)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var thumbnail = source.CreateThumbnail(
                0,
                new CGImageThumbnailOptions
                {
                    CreateThumbnailFromImageAlways = true,
                    CreateThumbnailWithTransform = true,
                    MaxPixelSize = maxPixelDimension
                });
            if (thumbnail is null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            using var destinationUrl = NSUrl.FromFilename(temporaryPath);
            using var destination = CGImageDestination.Create(
                destinationUrl,
                source.TypeIdentifier ?? MobileCoreServices.UTType.JPEG,
                1);
            if (destination is null)
            {
                return;
            }

            destination.AddImage(thumbnail);
            if (!destination.Close())
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, optimizedPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static int ResolveMaxPixelDimension()
        => DeviceInfo.Current.Idiom == DeviceIdiom.Tablet
            ? TabletMaxPixelDimension
            : PhoneMaxPixelDimension;

    private static string BuildOptimizedPath(string cachePath, int maxPixelDimension)
    {
        var directory = System.IO.Path.GetDirectoryName(cachePath) ?? string.Empty;
        var fileName = System.IO.Path.GetFileNameWithoutExtension(cachePath);
        var extension = System.IO.Path.GetExtension(cachePath);
        var optimizedSuffix = maxPixelDimension == PhoneMaxPixelDimension
            ? PhoneOptimizedSuffix
            : $".ios-{maxPixelDimension}";
        return System.IO.Path.Combine(directory, $"{fileName}{optimizedSuffix}{extension}");
    }
}
