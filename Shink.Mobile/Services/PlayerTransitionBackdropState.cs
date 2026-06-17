using Microsoft.Maui.Media;

namespace Shink.Mobile.Services;

public sealed class PlayerTransitionBackdropState
{
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private string? _capturedPath;

    public ImageSource? BuildImageSource()
    {
        if (string.IsNullOrWhiteSpace(_capturedPath) || !File.Exists(_capturedPath))
        {
            return null;
        }

        return ImageSource.FromFile(_capturedPath);
    }

    public async Task CaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!Screenshot.Default.IsCaptureSupported)
        {
            return;
        }

        await _captureLock.WaitAsync(cancellationToken);
        try
        {
            var screenshot = await Screenshot.Default.CaptureAsync();
            if (screenshot is null)
            {
                return;
            }

            var cacheDirectory = System.IO.Path.Combine(FileSystem.CacheDirectory, "player-transition");
            Directory.CreateDirectory(cacheDirectory);

            var targetPath = System.IO.Path.Combine(cacheDirectory, "backdrop.png");
            var temporaryPath = $"{targetPath}.tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            await using (var sourceStream = await screenshot.OpenReadAsync())
            await using (var fileStream = File.Create(temporaryPath))
            {
                await sourceStream.CopyToAsync(fileStream, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(temporaryPath, targetPath);
            _capturedPath = targetPath;
        }
        finally
        {
            _captureLock.Release();
        }
    }
}
