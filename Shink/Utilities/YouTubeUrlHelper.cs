using Microsoft.AspNetCore.WebUtilities;

namespace Shink.Utilities;

public static class YouTubeUrlHelper
{
    public static string? BuildEmbedUrl(string? value)
    {
        return TryGetVideoId(value, out var videoId)
            ? $"https://www.youtube-nocookie.com/embed/{videoId}?rel=0"
            : null;
    }

    public static string? BuildWatchUrl(string? value)
    {
        return TryGetVideoId(value, out var videoId)
            ? $"https://www.youtube.com/watch?v={videoId}"
            : null;
    }

    public static bool TryGetVideoId(string? value, out string? videoId)
    {
        videoId = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (IsValidVideoId(candidate))
        {
            videoId = candidate;
            return true;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            !Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out uri))
        {
            return false;
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        candidate = host switch
        {
            "youtu.be" or "www.youtu.be" => uri.AbsolutePath.Trim('/'),
            _ when host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase)
                => ResolveFromYouTubeUri(uri),
            _ => null
        };

        if (!IsValidVideoId(candidate))
        {
            return false;
        }

        videoId = candidate;
        return true;
    }

    private static string? ResolveFromYouTubeUri(Uri uri)
    {
        var pathSegments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathSegments.Length == 0)
        {
            return null;
        }

        if (string.Equals(pathSegments[0], "watch", StringComparison.OrdinalIgnoreCase))
        {
            var query = QueryHelpers.ParseQuery(uri.Query);
            if (query.TryGetValue("v", out var videoId))
            {
                return videoId.FirstOrDefault();
            }

            return null;
        }

        if (pathSegments.Length >= 2 &&
            (string.Equals(pathSegments[0], "embed", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(pathSegments[0], "shorts", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(pathSegments[0], "live", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(pathSegments[0], "v", StringComparison.OrdinalIgnoreCase)))
        {
            return pathSegments[1];
        }

        return null;
    }

    private static bool IsValidVideoId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Length == 11 &&
               value.IndexOfAny([' ', '\t', '\r', '\n', '?', '&', '/', '#', '\\']) < 0;
    }
}
