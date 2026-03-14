using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Shink.Components.Content;

namespace Shink.Services;

public sealed class AudioAccessService(IDataProtectionProvider dataProtectionProvider) : IAudioAccessService
{
    private const string ProtectorPurpose = "Shink.Audio.StreamToken.v1";
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public string CreateSignedAudioUrl(string slug, TimeSpan? lifetime = null)
    {
        var story = StoryCatalog.FindAnyBySlug(slug)
            ?? throw new InvalidOperationException("Unknown story slug.");

        var expiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime ?? DefaultTokenLifetime);
        var payload = new AudioTokenPayload(story.Slug, expiresAtUtc.ToUnixTimeSeconds());
        var json = JsonSerializer.Serialize(payload);
        var protectedToken = _protector.Protect(json);

        return $"/media/audio/{Uri.EscapeDataString(story.Slug)}?token={Uri.EscapeDataString(protectedToken)}";
    }

    public bool IsTokenValid(string slug, string? token)
    {
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
        return hasSameSlug && isNotExpired;
    }

    private sealed record AudioTokenPayload(string Slug, long ExpiresAtUnixSeconds);
}
