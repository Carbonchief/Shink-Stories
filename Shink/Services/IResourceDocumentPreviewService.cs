namespace Shink.Services;

public interface IResourceDocumentPreviewService
{
    Task<GeneratedResourceDocumentPreview> GeneratePreviewAsync(
        Stream pdfStream,
        CancellationToken cancellationToken = default);
}

public sealed record GeneratedResourceDocumentPreview(
    byte[] Content,
    string ContentType);
