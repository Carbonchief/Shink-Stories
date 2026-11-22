using Shink.Shared;

namespace Shink.Mobile.Services;

internal static class MobilePushArtworkDownload
{
    // Short, anonymous requests leave time for the OS to display the original alert.
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(4),
        MaxResponseContentBufferSize = 4 * 1024 * 1024
    };

    public static async Task<byte[]?> TryDownloadAsync(string? type, string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(url) || MobilePushArtwork.Resolve(type, url) is not { } image)
            return null;

        try
        {
            return await Client.GetByteArrayAsync(image, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return null;
        }
    }
}
