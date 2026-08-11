using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Shink.Services;

public sealed class VideoAccessService(IDataProtectionProvider dataProtectionProvider) : IVideoAccessService
{
    private const string ProtectorPurpose = "Shink.Video.StreamToken.v1";
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromHours(4);
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public string CreateSignedVideoUrl(string slug, TimeSpan? lifetime = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Story slug is required.", nameof(slug));
        }

        var expiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime ?? DefaultTokenLifetime);
        var payload = new VideoTokenPayload(slug.Trim(), expiresAtUtc.ToUnixTimeSeconds());
        var json = JsonSerializer.Serialize(payload);
        var protectedToken = _protector.Protect(json);

        return $"/media/video/{Uri.EscapeDataString(payload.Slug)}?token={Uri.EscapeDataString(protectedToken)}";
    }

    public bool IsTokenValid(string slug, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        VideoTokenPayload? payload;
        try
        {
            var json = _protector.Unprotect(token);
            payload = JsonSerializer.Deserialize<VideoTokenPayload>(json);
        }
        catch
        {
            return false;
        }

        if (payload is null)
        {
            return false;
        }

        var hasSameSlug = string.Equals(payload.Slug, slug, StringComparison.OrdinalIgnoreCase);
        var isNotExpired = DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= payload.ExpiresAtUnixSeconds;
        return hasSameSlug && isNotExpired;
    }

    private sealed record VideoTokenPayload(string Slug, long ExpiresAtUnixSeconds);
}
