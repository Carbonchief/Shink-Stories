namespace Shink.Services;

public interface IVideoAccessService
{
    string CreateSignedVideoUrl(string slug, TimeSpan? lifetime = null, bool allowsGuestPlayback = false);
    bool IsTokenValid(string slug, string? token, out bool allowsGuestPlayback);
}
