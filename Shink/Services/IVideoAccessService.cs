namespace Shink.Services;

public interface IVideoAccessService
{
    string CreateSignedVideoUrl(string slug, TimeSpan? lifetime = null);
    bool IsTokenValid(string slug, string? token);
}
