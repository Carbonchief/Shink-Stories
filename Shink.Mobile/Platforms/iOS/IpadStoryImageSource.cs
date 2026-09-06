using Foundation;
using ImageIO;
using Shink.Mobile.Models;
using UIKit;

namespace Shink.Mobile.Platforms.iOS;

internal sealed class IpadStoryImageSource : ImageSource, IFileImageSource
{
    public string File { get; init; } = string.Empty;
    bool IImageSource.IsEmpty => string.IsNullOrEmpty(File);
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public bool AspectFill { get; init; }
}

/// <summary>
/// Reuses prepared pixels across recycled feed cells. Cache misses complete
/// during binding so asynchronously arriving images cannot force another
/// layout of the nested carousels in the middle of a scrolling frame.
/// </summary>
internal sealed class IpadStoryImageSourceService : ImageSourceService,
    IImageSourceService<IpadStoryImageSource>
{
    // UIKit releases entries under memory pressure. Each displayed image owns
    // a separate UIImage wrapper over the cached pixels so recycling one cell
    // cannot dispose another cell's artwork.
    private static readonly NSCache DecodedImages = new()
    {
        TotalCostLimit = 48 * 1024 * 1024,
        CountLimit = 64
    };

    public override Task<IImageSourceServiceResult<UIImage>?> GetImageAsync(
        IImageSource imageSource,
        float scale = 1,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Load(imageSource, scale, cancellationToken));

    private static IImageSourceServiceResult<UIImage>? Load(
        IImageSource imageSource, float scale, CancellationToken cancellationToken)
    {
        if (imageSource is not IpadStoryImageSource source || string.IsNullOrEmpty(source.File))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var cacheKey = new NSString($"{source.File}|{source.PixelWidth}|{source.PixelHeight}|{source.AspectFill}|{System.IO.File.GetLastWriteTimeUtc(source.File).Ticks}");
            if (TryGetDecoded(cacheKey, scale) is { } cached)
            {
                return cached;
            }

            using var pool = new NSAutoreleasePool();
            using var url = NSUrl.FromFilename(source.File);
            using var encoded = CGImageSource.FromUrl(url);
            var properties = encoded?.GetProperties(0, new CGImageOptions { ShouldCache = false });
            var maxPixelSize = StoryImageDecodeSize.Resolve(
                properties?.PixelWidth ?? 0,
                properties?.PixelHeight ?? 0,
                source.PixelWidth,
                source.PixelHeight,
                source.AspectFill);
            using var decoded = encoded?.CreateThumbnail(0, new CGImageThumbnailOptions
            {
                CreateThumbnailFromImageAlways = true,
                CreateThumbnailWithTransform = true,
                MaxPixelSize = maxPixelSize,
                ShouldCache = true,
                ShouldCacheImmediately = true
            });
            if (decoded is null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var cachedImage = UIImage.FromImage(decoded);
            DecodedImages.SetCost(cachedImage, cacheKey, (nuint)(decoded.BytesPerRow * decoded.Height));
            var displayImage = UIImage.FromImage(decoded, scale, UIImageOrientation.Up);
            return new ImageSourceServiceResult(displayImage, displayImage.Dispose);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to prepare story artwork: {exception.Message}");
            return null;
        }
    }

    private static IImageSourceServiceResult<UIImage>? TryGetDecoded(NSString key, float scale)
    {
        var cached = DecodedImages.ObjectForKey(key) as UIImage;
        if (cached is null)
        {
            return null;
        }

        using var pixels = cached.CGImage;
        if (pixels is null)
        {
            return null;
        }

        var image = UIImage.FromImage(pixels, scale, UIImageOrientation.Up);
        return new ImageSourceServiceResult(image, image.Dispose);
    }
}
