using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Shink.Services;

public sealed class AudioAccessService(IDataProtectionProvider dataProtectionProvider) : IAudioAccessService
{
    private const string ProtectorPurpose = "Shink.Audio.StreamToken.v1";
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromHours(4);
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public string CreateSignedAudioUrl(string slug, TimeSpan? lifetime = null, bool allowsGuestPlayback = false)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Story slug is required.", nameof(slug));
        }

        var expiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime ?? DefaultTokenLifetime);
        var payload = new AudioTokenPayload(slug.Trim(), expiresAtUtc.ToUnixTimeSeconds(), allowsGuestPlayback);
        var json = JsonSerializer.Serialize(payload);
        var protectedToken = _protector.Protect(json);

        return $"/media/audio/{Uri.EscapeDataString(payload.Slug)}?token={Uri.EscapeDataString(protectedToken)}";
    }

    public bool IsTokenValid(string slug, string? token, out bool allowsGuestPlayback)
    {
        allowsGuestPlayback = false;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        AudioTokenPayload? payload;
        try
        {
            var json = _protector.Unprotect(token);
            payload = JsonSerializer.Deserialize<AudioTokenPayload>(json);
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
        if (!hasSameSlug || !isNotExpired)
            return false;

        allowsGuestPlayback = payload.AllowsGuestPlayback;
        return true;
    }

    private sealed record AudioTokenPayload(string Slug, long ExpiresAtUnixSeconds, bool AllowsGuestPlayback = false);
}
