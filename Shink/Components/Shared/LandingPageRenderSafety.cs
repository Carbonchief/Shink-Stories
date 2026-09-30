using Shink.Services;

namespace Shink.Components.Shared;

/// <summary>
/// Keeps editable landing-page values safe when they are rendered in either
/// the public page or an incomplete admin preview.
/// </summary>
public static class LandingPageRenderSafety
{
    public static string CssColor(string? value, string fallback)
    {
        var candidate = value?.Trim();
        if (candidate is null || candidate.Length is not (4 or 5 or 7 or 9) || candidate[0] != '#')
        {
            return fallback;
        }

        return candidate[1..].All(Uri.IsHexDigit)
            ? candidate
            : fallback;
    }

    public static string? LinkUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || HasUnsafeCharacters(value))
        {
            return null;
        }

        var candidate = value?.Trim();
        return LandingPageContentValidator.IsSafeLinkUrl(candidate) ? candidate : null;
    }

    public static string? ImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || HasUnsafeCharacters(value))
        {
            return null;
        }

        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return null;
        }

        if (IsSafeSiteRelativeUrl(candidate))
        {
            return candidate;
        }

        if (!HasUnsafeCharacters(candidate) &&
            !candidate.Contains('\\') &&
            Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(uri.Host) &&
            string.IsNullOrEmpty(uri.UserInfo))
        {
            return candidate;
        }

        return null;
    }

    public static string AlignmentClass(string? alignment) =>
        alignment?.Trim().ToLowerInvariant() switch
        {
            "center" => "landing-align-center",
            "right" => "landing-align-right",
            _ => "landing-align-left"
        };

    private static bool HasUnsafeCharacters(string value) => value.Any(char.IsControl);

    private static bool IsSafeSiteRelativeUrl(string value)
    {
        if (value.Length == 0 ||
            value[0] != '/' ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.Contains('\\') ||
            HasUnsafeCharacters(value))
        {
            return false;
        }

        var path = value.Split(['?', '#'], 2)[0];
        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return false;
        }

        return !decodedPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
    }
}
