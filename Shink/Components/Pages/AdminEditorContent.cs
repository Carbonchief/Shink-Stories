using Microsoft.JSInterop;

namespace Shink.Components.Pages;

internal static class AdminEditorContent
{
    public static async Task<string> ReadAsync(string? html, IJSStreamReference? content)
    {
        if (content is null) return html ?? string.Empty;

        await using var stream = await content.OpenReadStreamAsync(maxAllowedSize: 4 * 1024 * 1024);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
