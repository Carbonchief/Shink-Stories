using Android.Graphics;

namespace Shink.Mobile.Platforms.Android;

internal static class AndroidImageCacheOptimizer
{
    private const int PhoneMaxPixelDimension = 1280;
    private const int TabletMaxPixelDimension = 2048;

    public static string ResolveDisplayPath(string cachePath)
    {
        var optimizedPath = BuildOptimizedPath(cachePath, ResolveMaxPixelDimension());
        return File.Exists(optimizedPath) && new FileInfo(optimizedPath).Length > 0
            ? optimizedPath
            : cachePath;
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

        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(cachePath, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
        {
            return;
        }

        // Android's image handler can use the cached original directly when it
        // already fits the target display. Avoid a needless decode/re-encode pass
        // competing with RecyclerView while the first rows are being laid out.
        if (Math.Max(bounds.OutWidth, bounds.OutHeight) <= maxPixelDimension)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sampleSize = ResolveSampleSize(bounds.OutWidth, bounds.OutHeight, maxPixelDimension);
        using var options = new BitmapFactory.Options
        {
            InSampleSize = sampleSize,
            InPreferredConfig = Bitmap.Config.Argb8888
        };
        using var decoded = BitmapFactory.DecodeFile(cachePath, options);
        if (decoded is null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scale = Math.Min(1, maxPixelDimension / (double)Math.Max(decoded.Width, decoded.Height));
        var targetWidth = Math.Max(1, (int)Math.Round(decoded.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(decoded.Height * scale));
        using var scaled = scale < 1
            ? Bitmap.CreateScaledBitmap(decoded, targetWidth, targetHeight, filter: true)
            : null;
        var displayBitmap = scaled ?? decoded;
        if (displayBitmap is null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var temporaryPath = $"{optimizedPath}.{Environment.CurrentManagedThreadId}.tmp";
        try
        {
            using (var output = File.Create(temporaryPath))
            {
                var format = displayBitmap.HasAlpha
                    ? Bitmap.CompressFormat.Png!
                    : Bitmap.CompressFormat.Jpeg!;
                if (!displayBitmap.Compress(format, quality: 92, output))
                {
                    return;
                }
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

    private static int ResolveSampleSize(int width, int height, int maxPixelDimension)
    {
        var largestDimension = Math.Max(width, height);
        var sampleSize = 1;
        while (largestDimension / (sampleSize * 2) >= maxPixelDimension)
        {
            sampleSize *= 2;
        }

        return sampleSize;
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
        return System.IO.Path.Combine(directory, $"{fileName}.android-{maxPixelDimension}{extension}");
    }
}
