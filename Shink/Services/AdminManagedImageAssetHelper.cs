namespace Shink.Services;

internal static class AdminManagedImageAssetHelper
{
    public static bool AreEquivalent(string? left, string? right) =>
        string.Equals(NormalizeAssetPath(left), NormalizeAssetPath(right), StringComparison.OrdinalIgnoreCase);

    public static string? TryResolveR2ObjectKey(string? assetPath, string? publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(assetPath.Trim(), UriKind.Absolute, out var assetUri) ||
            !Uri.TryCreate(publicBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        var normalizedBase = baseUri.AbsoluteUri.TrimEnd('/');
        var normalizedAsset = assetUri.GetLeftPart(UriPartial.Path);
        if (!normalizedAsset.StartsWith($"{normalizedBase}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var encodedRelativePath = normalizedAsset[(normalizedBase.Length + 1)..];
        if (string.IsNullOrWhiteSpace(encodedRelativePath))
        {
            return null;
        }

        var segments = encodedRelativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        return segments.Length == 0
            ? null
            : string.Join('/', segments);
    }

    public static string? TryResolveLocalUploadedFilePath(string? assetPath, string? webRootPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(webRootPath))
        {
            return null;
        }

        var candidatePath = assetPath.Trim();
        if (Uri.TryCreate(candidatePath, UriKind.Absolute, out var absoluteUri))
        {
            candidatePath = absoluteUri.AbsolutePath;
        }

        if (candidatePath.StartsWith("~/", StringComparison.Ordinal))
        {
            candidatePath = $"/{candidatePath[2..]}";
        }

        candidatePath = candidatePath.Replace('\\', '/');
        if (!candidatePath.StartsWith("/branding/uploaded/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativeSegments = candidatePath
            .TrimStart('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (relativeSegments.Length == 0)
        {
            return null;
        }

        var rootPath = Path.GetFullPath(webRootPath);
        var physicalPath = Path.GetFullPath(Path.Combine(rootPath, Path.Combine(relativeSegments)));
        return physicalPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
            ? physicalPath
            : null;
    }

    private static string? NormalizeAssetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith("~/", StringComparison.Ordinal))
        {
            candidate = $"/{candidate[2..]}";
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        return candidate.Replace('\\', '/').TrimEnd('/');
    }
}
