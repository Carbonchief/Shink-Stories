using PDFtoImage;
using SkiaSharp;

namespace Shink.Services;

public sealed class ResourceDocumentPreviewService : IResourceDocumentPreviewService
{
    private static readonly RenderOptions PreviewRenderOptions = new()
    {
        Width = 420,
        WithAspectRatio = true,
        BackgroundColor = SKColors.White,
        Dpi = 144
    };

    public async Task<GeneratedResourceDocumentPreview> GeneratePreviewAsync(
        Stream pdfStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
        {
            throw new PlatformNotSupportedException("PDF preview generation is not supported on this platform.");
        }

        await using var pdfBuffer = new MemoryStream();
        await pdfStream.CopyToAsync(pdfBuffer, cancellationToken);
        if (pdfBuffer.Length == 0)
        {
            throw new InvalidOperationException("Could not generate the PDF preview image.");
        }

        pdfBuffer.Position = 0;

        await using var previewBuffer = new MemoryStream();

        try
        {
#pragma warning disable CA1416
            Conversion.SavePng(
                previewBuffer,
                pdfBuffer,
                0,
                leaveOpen: true,
                password: null,
                options: PreviewRenderOptions);
#pragma warning restore CA1416
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or IOException)
        {
            throw new InvalidOperationException("Could not generate the PDF preview image.", exception);
        }

        if (previewBuffer.Length == 0)
        {
            throw new InvalidOperationException("Could not generate the PDF preview image.");
        }

        return new GeneratedResourceDocumentPreview(
            Content: previewBuffer.ToArray(),
            ContentType: "image/png");
    }
}
