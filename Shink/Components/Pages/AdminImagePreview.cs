using Microsoft.JSInterop;

namespace Shink.Components.Pages;

internal static class AdminImagePreview
{
    public static async Task<string?> CreateAsync(IJSRuntime js, string inputId, ILogger logger)
    {
        const long maxThumbnailBytes = 1024 * 1024;
        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "/js/admin-image-preview.js");
            await using var thumbnail = await module.InvokeAsync<IJSStreamReference?>("createThumbnail", inputId);
            if (thumbnail is null || thumbnail.Length is <= 0 or > maxThumbnailBytes)
            {
                return null;
            }

            await using var stream = await thumbnail.OpenReadStreamAsync(maxThumbnailBytes);
            using var buffer = new MemoryStream((int)thumbnail.Length);
            await stream.CopyToAsync(buffer);
            return "data:image/png;base64," + Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JSException or OperationCanceledException)
        {
            logger.LogDebug(exception, "Admin image thumbnail could not be generated.");
            return null;
        }
    }
}
