using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class CloudflareR2ResourceDocumentStorageService(
    IOptions<CloudflareR2Options> options,
    ILogger<CloudflareR2ResourceDocumentStorageService> logger) : IResourceDocumentStorageService, IDisposable
{
    private readonly CloudflareR2Options _options = options.Value;
    private readonly ILogger<CloudflareR2ResourceDocumentStorageService> _logger = logger;
    private IAmazonS3? _client;

    public async Task<UploadedResourceDocument> UploadDocumentAsync(
        string resourceTypeSlug,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var normalizedTypeSlug = NormalizeSlug(resourceTypeSlug, "Resource type slug is required for document uploads.");
        var normalizedContentType = ResolveDocumentContentType(contentType, fileName);
        var objectKey = BuildObjectKey(normalizedTypeSlug, fileName);

        await UploadObjectAsync(objectKey, normalizedContentType, content, cancellationToken);

        return new UploadedResourceDocument(
            Bucket: _options.BucketName.Trim(),
            ObjectKey: objectKey,
            ContentType: normalizedContentType);
    }

    public async Task<UploadedResourcePreviewImage> UploadPreviewImageAsync(
        string resourceTypeSlug,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var normalizedTypeSlug = NormalizeSlug(resourceTypeSlug, "Resource type slug is required for preview uploads.");
        var normalizedContentType = ResolvePreviewContentType(contentType, fileName);
        var objectKey = BuildPreviewObjectKey(normalizedTypeSlug);

        await UploadObjectAsync(objectKey, normalizedContentType, content, cancellationToken);

        return new UploadedResourcePreviewImage(
            Bucket: _options.BucketName.Trim(),
            ObjectKey: objectKey,
            ContentType: normalizedContentType);
    }

    public async Task<ResourceDocumentStream?> OpenReadAsync(
        string bucket,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        EnsureConfigured();

        try
        {
            var response = await GetClient().GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = bucket.Trim(),
                    Key = objectKey.Trim()
                },
                cancellationToken);

            var contentType = NormalizeContentType(response.Headers.ContentType);
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "application/pdf";
            }

            return new ResourceDocumentStream(
                Content: new OwnedResponseStream(response),
                ContentType: contentType,
                ContentLength: response.Headers.ContentLength,
                LastModified: response.LastModified is DateTime lastModified && lastModified != DateTime.MinValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(lastModified, DateTimeKind.Utc))
                    : null);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Failed to open R2 resource document {ObjectKey}.", objectKey);
            return null;
        }
    }

    public async Task DeleteObjectIfExistsAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || !IsConfigured())
        {
            return;
        }

        try
        {
            await GetClient().DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = _options.BucketName.Trim(),
                    Key = objectKey.Trim()
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Failed to delete R2 resource document {ObjectKey}.", objectKey);
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    private async Task UploadObjectAsync(
        string objectKey,
        string contentType,
        Stream content,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"shink-resource-upload-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var tempFileStream = File.Create(tempFilePath))
            {
                await content.CopyToAsync(tempFileStream, cancellationToken);
            }

            await GetClient().PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _options.BucketName.Trim(),
                    Key = objectKey,
                    FilePath = tempFilePath,
                    ContentType = contentType,
                    DisablePayloadSigning = true,
                    DisableDefaultChecksumValidation = true,
                    UseChunkEncoding = false
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogError(exception, "Failed to upload R2 resource document {ObjectKey}.", objectKey);
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Failed to clean up temporary resource upload file {Path}.", tempFilePath);
            }
        }
    }

    private IAmazonS3 GetClient()
    {
        EnsureConfigured();

        _client ??= new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKeyId.Trim(), _options.SecretAccessKey.Trim()),
            new AmazonS3Config
            {
                ServiceURL = $"https://{_options.AccountId.Trim()}.r2.cloudflarestorage.com",
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });

        return _client;
    }

    private void EnsureConfigured()
    {
        if (IsConfigured())
        {
            return;
        }

        throw new InvalidOperationException("Cloudflare R2 is not fully configured for resource uploads.");
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_options.AccountId) &&
        !string.IsNullOrWhiteSpace(_options.BucketName) &&
        !string.IsNullOrWhiteSpace(_options.AccessKeyId) &&
        !string.IsNullOrWhiteSpace(_options.SecretAccessKey);

    private static string BuildObjectKey(string resourceTypeSlug, string fileName)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return $"uploaded/resources/{timestamp:yyyy}/{timestamp:MM}/{resourceTypeSlug}/{timestamp:yyyyMMddHHmmss}-{Guid.NewGuid():N}{ResolveExtension(fileName)}";
    }

    private static string BuildPreviewObjectKey(string resourceTypeSlug)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return $"uploaded/resource-previews/{timestamp:yyyy}/{timestamp:MM}/{resourceTypeSlug}/{timestamp:yyyyMMddHHmmss}-{Guid.NewGuid():N}.png";
    }

    private static string ResolveDocumentContentType(string? contentType, string fileName)
    {
        var normalized = NormalizeContentType(contentType);
        if (normalized == "application/pdf")
        {
            return normalized;
        }

        if (Path.GetExtension(fileName).Trim().ToLowerInvariant() == ".pdf")
        {
            return "application/pdf";
        }

        throw new InvalidOperationException("Unsupported document file type. Use PDF files only.");
    }

    private static string ResolveExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(extension) ? ".pdf" : extension;
    }

    private static string ResolvePreviewContentType(string? contentType, string fileName)
    {
        var normalized = NormalizeContentType(contentType);
        if (normalized == "image/png")
        {
            return normalized;
        }

        if (Path.GetExtension(fileName).Trim().Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return "image/png";
        }

        throw new InvalidOperationException("Unsupported preview image file type. Use PNG files only.");
    }

    private static string NormalizeSlug(string value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType)
            ? string.Empty
            : contentType.Trim().ToLowerInvariant();

    private sealed class OwnedResponseStream(GetObjectResponse response) : Stream
    {
        private readonly GetObjectResponse _response = response;
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
            _inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) =>
            _inner.Write(buffer);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(buffer, cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.WriteAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            _response.Dispose();
            await base.DisposeAsync();
        }
    }
}
