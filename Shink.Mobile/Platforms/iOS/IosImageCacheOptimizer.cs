using Foundation;
using ImageIO;

namespace Shink.Mobile.Platforms.iOS;

internal static class IosImageCacheOptimizer
{
    // A full-width iPhone 15 Pro image needs about 1,180 physical pixels.
    // Keeping a small margin above that avoids decoding the 1,980px originals
    // while preserving native-resolution artwork on the device.
    private const int MaxPixelDimension = 1280;
    private const string OptimizedSuffix = ".ios-feed";

    public static string ResolveDisplayPath(string cachePath)
    {
        var optimizedPath = BuildOptimizedPath(cachePath);
        return File.Exists(optimizedPath) && new FileInfo(optimizedPath).Length > 0
            ? optimizedPath
            : cachePath;
    }

    public static void EnsureOptimized(string cachePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var optimizedPath = BuildOptimizedPath(cachePath);
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
                    MaxPixelSize = MaxPixelDimension
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

    private static string BuildOptimizedPath(string cachePath)
    {
        var directory = System.IO.Path.GetDirectoryName(cachePath) ?? string.Empty;
        var fileName = System.IO.Path.GetFileNameWithoutExtension(cachePath);
        var extension = System.IO.Path.GetExtension(cachePath);
        return System.IO.Path.Combine(directory, $"{fileName}{OptimizedSuffix}{extension}");
    }
}
