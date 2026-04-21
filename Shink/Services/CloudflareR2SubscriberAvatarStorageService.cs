using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class CloudflareR2SubscriberAvatarStorageService(
    IOptions<CloudflareR2Options> options,
    ILogger<CloudflareR2SubscriberAvatarStorageService> logger) : ISubscriberAvatarStorageService, IDisposable
{
    private readonly CloudflareR2Options _options = options.Value;
    private readonly ILogger<CloudflareR2SubscriberAvatarStorageService> _logger = logger;
    private IAmazonS3? _client;

    public async Task<UploadedSubscriberAvatar> UploadAvatarAsync(
        string email,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var normalizedContentType = ResolveImageContentType(contentType, fileName);
        var objectKey = BuildObjectKey(normalizedEmail, fileName, normalizedContentType);

        await UploadObjectAsync(objectKey, normalizedContentType, content, cancellationToken);

        return new UploadedSubscriberAvatar(
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
            _logger.LogWarning(exception, "Failed to delete R2 subscriber avatar {ObjectKey}.", objectKey);
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

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"shink-avatar-{Guid.NewGuid():N}.tmp");

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
            _logger.LogError(exception, "Failed to upload R2 subscriber avatar {ObjectKey}.", objectKey);
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
                _logger.LogWarning(exception, "Failed to clean up temporary avatar upload file {Path}.", tempFilePath);
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
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });

        return _client;
    }

    private string BuildPublicObjectUrl(string objectKey)
    {
        var baseUrl = _options.PublicBaseUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Cloudflare R2 is not fully configured for subscriber avatar uploads.");
        }

        var escapedSegments = objectKey
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        return $"{baseUrl}/{string.Join('/', escapedSegments)}";
    }

    private static string BuildObjectKey(string normalizedEmail, string fileName, string contentType)
    {
        using var sha256 = SHA256.Create();
        var emailHash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedEmail))).ToLowerInvariant();
        var timestamp = DateTimeOffset.UtcNow;
        var extension = ResolveExtension(fileName, contentType);
        return $"uploaded/subscribers/avatars/{timestamp:yyyy}/{timestamp:MM}/{emailHash}-avatar{extension}";
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("A valid email is required to upload a subscriber avatar.");
        }

        return normalized;
    }

    private static string ResolveImageContentType(string? contentType, string fileName)
    {
        var normalized = contentType?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized switch
            {
                "image/jpeg" or "image/jpg" => "image/jpeg",
                "image/png" => "image/png",
                "image/webp" => "image/webp",
                "image/gif" => "image/gif",
                _ => Path.GetExtension(fileName).Trim().ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    ".gif" => "image/gif",
                    _ => throw new InvalidOperationException("Unsupported avatar image type. Use JPG, PNG, WEBP, or GIF.")
                }
            };
        }

        return Path.GetExtension(fileName).Trim().ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => throw new InvalidOperationException("Unsupported avatar image type. Use JPG, PNG, WEBP, or GIF.")
        };
    }

    private static string ResolveExtension(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension;
        }

        return contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".img"
        };
    }

    private void EnsureUploadConfigured()
    {
        if (IsUploadConfigured())
        {
            return;
        }

        throw new InvalidOperationException("Cloudflare R2 is not fully configured for subscriber avatar uploads.");
    }

    private bool IsUploadConfigured() =>
        !string.IsNullOrWhiteSpace(_options.AccountId) &&
        !string.IsNullOrWhiteSpace(_options.BucketName) &&
        !string.IsNullOrWhiteSpace(_options.AccessKeyId) &&
        !string.IsNullOrWhiteSpace(_options.SecretAccessKey) &&
        !string.IsNullOrWhiteSpace(_options.PublicBaseUrl);
}
