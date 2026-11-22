namespace Shink.Shared;

internal static class MobilePushArtwork
{
    private static readonly Uri Website = new("https://www.schink.co.za/");

    public static Uri? Resolve(string? type, string? path)
    {
        if (type is not ("story_published" or "character_unlock") || string.IsNullOrWhiteSpace(path)
            || path.Length > 1024 || path.Contains('\\') || path.StartsWith("//", StringComparison.Ordinal)
            || !Uri.TryCreate(Website, path, out var uri) || !IsTrusted(uri))
            return null;

        if (uri.Host == Website.Host && uri.AbsolutePath == "/media/image")
        {
            var source = System.Web.HttpUtility.ParseQueryString(uri.Query)["src"];
            if (!Uri.TryCreate(source, UriKind.Absolute, out uri) || !IsTrusted(uri) || uri.Host != "media.prioritybit.co.za")
                return null;
        }

        return uri.AbsolutePath == "/branding/schink-placeholder.png" ? null : uri;
    }

    private static bool IsTrusted(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Host is "www.schink.co.za" or "media.prioritybit.co.za";
}
