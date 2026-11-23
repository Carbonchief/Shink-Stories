namespace Shink.Services;

public interface IAudioAccessService
{
    string CreateSignedAudioUrl(string slug, TimeSpan? lifetime = null, bool allowsGuestPlayback = false);
    bool IsTokenValid(string slug, string? token, out bool allowsGuestPlayback);
}
