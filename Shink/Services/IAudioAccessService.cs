namespace Shink.Services;

public interface IAudioAccessService
{
    string CreateSignedAudioUrl(string slug, TimeSpan? lifetime = null);
    bool IsTokenValid(string slug, string? token);
}
