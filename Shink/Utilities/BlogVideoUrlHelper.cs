namespace Shink.Utilities;

public static class BlogVideoUrlHelper
{
    public static BlogVideoEmbed? ResolveCloudflareVideo(string? value, string? publicBaseUrl)
    {
        if (!TryCreateHttpsUri(value, out var candidate))
        {
            return null;
        }

        if (IsConfiguredDirectVideo(candidate, publicBaseUrl))
        {
            return new BlogVideoEmbed(candidate.ToString(), BlogVideoEmbedKind.DirectVideo);
        }

        var host = candidate.Host.Trim().ToLowerInvariant();
        if (!IsCloudflareStreamHost(host))
        {
            return null;
        }

        if (HasSupportedVideoExtension(candidate.AbsolutePath))
        {
            return new BlogVideoEmbed(candidate.ToString(), BlogVideoEmbedKind.DirectVideo);
        }

        var pathSegments = candidate.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var isVideoDeliveryEmbed =
            string.Equals(host, "iframe.videodelivery.net", StringComparison.OrdinalIgnoreCase) &&
            pathSegments.Length >= 1;
        var isCloudflareStreamEmbed =
            host.EndsWith(".cloudflarestream.com", StringComparison.OrdinalIgnoreCase) &&
            pathSegments.Length >= 2 &&
            string.Equals(pathSegments[^1], "iframe", StringComparison.OrdinalIgnoreCase);

        return isVideoDeliveryEmbed || isCloudflareStreamEmbed
            ? new BlogVideoEmbed(candidate.ToString(), BlogVideoEmbedKind.Iframe)
            : null;
    }

    private static bool IsConfiguredDirectVideo(Uri candidate, string? publicBaseUrl)
    {
        if (!HasSupportedVideoExtension(candidate.AbsolutePath) ||
            !TryCreateHttpsBaseUri(publicBaseUrl, out var publicBaseUri))
        {
            return false;
        }

        return publicBaseUri.IsBaseOf(candidate);
    }

    private static bool IsCloudflareStreamHost(string host) =>
        string.Equals(host, "iframe.videodelivery.net", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "videodelivery.net", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".cloudflarestream.com", StringComparison.OrdinalIgnoreCase);

    private static bool HasSupportedVideoExtension(string path)
    {
        var extension = Path.GetExtension(path).Trim().ToLowerInvariant();
        return extension is ".mp4" or ".webm";
    }

    private static bool TryCreateHttpsUri(string? value, out Uri uri)
    {
        uri = default!;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate) ||
            candidate is null ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(candidate.Host) ||
            !string.IsNullOrWhiteSpace(candidate.UserInfo))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool TryCreateHttpsBaseUri(string? value, out Uri uri)
    {
        uri = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (!candidate.EndsWith("/", StringComparison.Ordinal))
        {
            candidate += "/";
        }

        return TryCreateHttpsUri(candidate, out uri);
    }
}

public sealed record BlogVideoEmbed(
    string Url,
    BlogVideoEmbedKind Kind);

public enum BlogVideoEmbedKind
{
    DirectVideo,
    Iframe
}
