using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class CloudflareR2StoryMediaStorageService(
    IOptions<CloudflareR2Options> options,
    ILogger<CloudflareR2StoryMediaStorageService> logger) : IStoryMediaStorageService, IDisposable
{
    private readonly CloudflareR2Options _options = options.Value;
    private readonly ILogger<CloudflareR2StoryMediaStorageService> _logger = logger;
    private IAmazonS3? _client;

    public Task<DirectStoryMediaUpload> CreateAudioDirectUploadAsync(
        string slug,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedSlug = NormalizeSlug(slug);
        var normalizedContentType = ResolveAudioContentType(contentType, fileName);
        var objectKey = BuildObjectKey(
            "uploaded/stories/audio",
            normalizedSlug,
            "audio",
            fileName,
            normalizedContentType);

        return Task.FromResult(BuildDirectUpload(objectKey, normalizedContentType, includePublicUrl: false));
    }

    public Task<DirectStoryMediaUpload> CreateImageDirectUploadAsync(
        string slug,
        StoryImageKind kind,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedSlug = NormalizeSlug(slug);
        var normalizedContentType = ResolveImageContentType(contentType, fileName);
        var suffix = kind == StoryImageKind.Thumbnail ? "thumbnail" : "cover";
        var objectKey = BuildObjectKey(
            "uploaded/stories/images",
            normalizedSlug,
            suffix,
            fileName,
            normalizedContentType);

        return Task.FromResult(BuildDirectUpload(objectKey, normalizedContentType, includePublicUrl: true));
    }

    public async Task<UploadedStoryAudio> UploadAudioAsync(
        string slug,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = NormalizeSlug(slug);
        var normalizedContentType = ResolveAudioContentType(contentType, fileName);
        var objectKey = BuildObjectKey(
            "uploaded/stories/audio",
            normalizedSlug,
            "audio",
            fileName,
            normalizedContentType);

        await UploadObjectAsync(objectKey, normalizedContentType, content, cancellationToken);

        return new UploadedStoryAudio(
            Bucket: _options.BucketName.Trim(),
            ObjectKey: objectKey,
            ContentType: normalizedContentType);
    }

    public async Task<UploadedStoryImage> UploadImageAsync(
        string slug,
        StoryImageKind kind,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = NormalizeSlug(slug);
        var normalizedContentType = ResolveImageContentType(contentType, fileName);
        var suffix = kind == StoryImageKind.Thumbnail ? "thumbnail" : "cover";
        var objectKey = BuildObjectKey(
            "uploaded/stories/images",
            normalizedSlug,
            suffix,
            fileName,
            normalizedContentType);

        await UploadObjectAsync(objectKey, normalizedContentType, content, cancellationToken);

        return new UploadedStoryImage(
            ObjectKey: objectKey,
            PublicUrl: BuildPublicObjectUrl(objectKey),
            ContentType: normalizedContentType);
    }

    public async Task DeleteObjectIfExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || !IsUploadConfigured())
        {
            return;
        }

        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _options.BucketName.Trim(),
                Key = objectKey.Trim()
            };

            await GetClient().DeleteObjectAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Failed to delete R2 object {ObjectKey}.", objectKey);
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
        EnsureUploadConfigured();

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"shink-upload-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var tempFileStream = File.Create(tempFilePath))
            {
                await content.CopyToAsync(tempFileStream, cancellationToken);
            }

            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName.Trim(),
                Key = objectKey,
                FilePath = tempFilePath,
                ContentType = contentType,
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true,
                UseChunkEncoding = false
            };

            await GetClient().PutObjectAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is AmazonS3Exception or HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogError(exception, "Failed to upload R2 object {ObjectKey}.", objectKey);
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
                _logger.LogWarning(exception, "Failed to clean up temporary upload file {Path}.", tempFilePath);
            }
        }
    }

    private IAmazonS3 GetClient()
    {
        EnsureUploadConfigured();

        _client ??= new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKeyId.Trim(), _options.SecretAccessKey.Trim()),
            new AmazonS3Config
            {
                ServiceURL = $"https://{_options.AccountId.Trim()}.r2.cloudflarestorage.com",
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
                // R2 rejects AWS SDK v4's optional checksum trailer mode.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });

        return _client;
    }

    private void EnsureUploadConfigured()
    {
        if (IsUploadConfigured())
        {
            return;
        }

        throw new InvalidOperationException("Cloudflare R2 is not fully configured for admin uploads.");
    }

    private bool IsUploadConfigured() =>
        !string.IsNullOrWhiteSpace(_options.AccountId) &&
        !string.IsNullOrWhiteSpace(_options.BucketName) &&
        !string.IsNullOrWhiteSpace(_options.AccessKeyId) &&
        !string.IsNullOrWhiteSpace(_options.SecretAccessKey);

    private DirectStoryMediaUpload BuildDirectUpload(
        string objectKey,
        string contentType,
        bool includePublicUrl)
    {
        EnsureUploadConfigured();

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName.Trim(),
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Protocol = Protocol.HTTPS,
            ContentType = contentType,
            Expires = DateTime.UtcNow.AddMinutes(10)
        };

        var uploadUrl = GetClient().GetPreSignedURL(request);
        return new DirectStoryMediaUpload(
            Bucket: _options.BucketName.Trim(),
            ObjectKey: objectKey,
            ContentType: contentType,
            PublicUrl: includePublicUrl ? BuildPublicObjectUrl(objectKey) : null,
            UploadUrl: uploadUrl,
            Method: "PUT");
    }

    private string BuildPublicObjectUrl(string objectKey)
    {
        var baseUrl = _options.PublicBaseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Cloudflare R2 is not fully configured for admin uploads.");
        }

        var escapedSegments = objectKey
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        return $"{baseUrl}/{string.Join('/', escapedSegments)}";
    }

    private static string BuildObjectKey(
        string prefix,
        string slug,
        string suffix,
        string fileName,
        string contentType)
    {
        var extension = ResolveExtension(fileName, contentType);
        var timestamp = DateTimeOffset.UtcNow;
        return $"{prefix}/{timestamp:yyyy}/{timestamp:MM}/{slug}-{suffix}-{timestamp:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
    }

    private static string ResolveAudioContentType(string? contentType, string fileName)
    {
        var normalized = NormalizeContentType(contentType);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized switch
            {
                "audio/mpeg" or "audio/mp3" => "audio/mpeg",
                "audio/mp4" or "audio/x-m4a" => "audio/mp4",
                "audio/wav" or "audio/x-wav" or "audio/wave" => "audio/wav",
                "audio/ogg" => "audio/ogg",
                _ => Path.GetExtension(fileName).Trim().ToLowerInvariant() switch
                {
                    ".mp3" or ".mpeg" => "audio/mpeg",
                    ".m4a" => "audio/mp4",
                    ".wav" => "audio/wav",
                    ".ogg" => "audio/ogg",
                    _ => throw new InvalidOperationException("Unsupported audio file type. Use MP3, M4A, WAV, or OGG.")
                }
            };
        }

        return Path.GetExtension(fileName).Trim().ToLowerInvariant() switch
        {
            ".mp3" or ".mpeg" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            _ => throw new InvalidOperationException("Unsupported audio file type. Use MP3, M4A, WAV, or OGG.")
        };
    }

    private static string ResolveImageContentType(string? contentType, string fileName)
    {
        var normalized = NormalizeContentType(contentType);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized switch
            {
                "image/jpeg" or "image/jpg" => "image/jpeg",
                "image/png" => "image/png",
                "image/webp" => "image/webp",
                _ => Path.GetExtension(fileName).Trim().ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => throw new InvalidOperationException("Unsupported image file type. Use JPG, PNG, or WEBP.")
                }
            };
        }

        return Path.GetExtension(fileName).Trim().ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => throw new InvalidOperationException("Unsupported image file type. Use JPG, PNG, or WEBP.")
        };
    }

    private static string ResolveExtension(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension;
        }

        return NormalizeContentType(contentType) switch
        {
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "audio/wav" => ".wav",
            "audio/ogg" => ".ogg",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".bin"
        };
    }

    private static string NormalizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new InvalidOperationException("Story slug is required for media uploads.");
        }

        return slug.Trim().ToLowerInvariant();
    }

    private static string NormalizeContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType)
            ? string.Empty
            : contentType.Trim().ToLowerInvariant();
}
