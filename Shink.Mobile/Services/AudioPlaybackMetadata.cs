namespace Shink.Mobile.Services;

public sealed record AudioPlaybackMetadata(
    string Title,
    string? Artist = null,
    string? ArtworkUrl = null,
    string? StorySlug = null,
    string? StorySource = null,
    string? PlaylistSlug = null,
    string ContentType = "audio",
    string? CharacterSlug = null,
    string? PlaybackSource = null)
{
    public Dictionary<string, object> ToAnalyticsProperties(double position, double duration, double speed)
    {
        var properties = new Dictionary<string, object>
        {
            ["title"] = string.IsNullOrWhiteSpace(Title) ? "Schink Stories" : Title,
            ["artist"] = string.IsNullOrWhiteSpace(Artist) ? "Schink Stories" : Artist,
            ["position_seconds"] = position,
            ["duration_seconds"] = duration,
            ["playback_speed"] = speed,
            ["content_type"] = ContentType
        };
        Add("story_slug", StorySlug);
        Add("source", StorySource);
        Add("playlist_slug", PlaylistSlug);
        Add("character_slug", CharacterSlug);
        Add("playback_source", PlaybackSource);
        return properties;

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) properties[key] = value;
        }
    }

    // Report the delivery mode, never the signed URL or local file path.
    // local_file includes both downloaded stories and the playback cache.
    public static string ResolvePlaybackSource(string audioUrl) =>
        Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? "streaming" : "local_file";
}
