namespace Shink.Mobile.Navigation;

public enum MobileNotificationNavigationKind
{
    None,
    Story,
    Character,
    ResourceWebsite
}

public sealed record MobileNotificationNavigationTarget(
    MobileNotificationNavigationKind Kind,
    string? Value = null,
    string? Source = null);

public static class MobileNotificationNavigation
{
    public static MobileNotificationNavigationTarget Resolve(string? notificationType, string? href)
    {
        var normalizedType = notificationType?.Trim().ToLowerInvariant();
        return normalizedType switch
        {
            "story_published" => ResolveStory(href),
            "character_unlock" => ResolveCharacter(href),
            "resource_document_published" => ResolveResourceWebsite(href),
            _ => new MobileNotificationNavigationTarget(MobileNotificationNavigationKind.None)
        };
    }

    private static MobileNotificationNavigationTarget ResolveStory(string? href)
    {
        var path = ExtractPathAndQuery(href);
        var segments = path
            .Split('?', '#')[0]
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2 ||
            !(segments[0].Equals("luister", StringComparison.OrdinalIgnoreCase) ||
              segments[0].Equals("gratis", StringComparison.OrdinalIgnoreCase)))
        {
            return new MobileNotificationNavigationTarget(MobileNotificationNavigationKind.Story);
        }

        var slug = Decode(segments[1]);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return new MobileNotificationNavigationTarget(MobileNotificationNavigationKind.Story);
        }

        var source = segments[0].Equals("gratis", StringComparison.OrdinalIgnoreCase)
            ? "gratis"
            : "luister";
        return new MobileNotificationNavigationTarget(
            MobileNotificationNavigationKind.Story,
            slug,
            source);
    }

    private static MobileNotificationNavigationTarget ResolveCharacter(string? href)
    {
        var path = ExtractPathAndQuery(href);
        var characterSlug = ReadQueryValue(path, "karakter");
        if (string.IsNullOrWhiteSpace(characterSlug))
        {
            var segments = path
                .Split('?', '#')[0]
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > 1 && IsCharacterRoute(segments[0]))
            {
                characterSlug = Decode(segments[1]);
            }
        }

        return new MobileNotificationNavigationTarget(
            MobileNotificationNavigationKind.Character,
            string.IsNullOrWhiteSpace(characterSlug) ? null : characterSlug);
    }

    private static MobileNotificationNavigationTarget ResolveResourceWebsite(string? href)
    {
        var trimmedHref = href?.Trim();
        return string.IsNullOrWhiteSpace(trimmedHref)
            ? new MobileNotificationNavigationTarget(MobileNotificationNavigationKind.None)
            : new MobileNotificationNavigationTarget(
                MobileNotificationNavigationKind.ResourceWebsite,
                trimmedHref);
    }

    private static string ExtractPathAndQuery(string? href)
    {
        var trimmedHref = href?.Trim() ?? string.Empty;
        return Uri.TryCreate(trimmedHref, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri.PathAndQuery
            : trimmedHref;
    }

    private static string? ReadQueryValue(string pathAndQuery, string key)
    {
        var queryIndex = pathAndQuery.IndexOf('?');
        if (queryIndex < 0 || queryIndex == pathAndQuery.Length - 1)
        {
            return null;
        }

        var fragmentIndex = pathAndQuery.IndexOf('#', queryIndex + 1);
        var query = fragmentIndex >= 0
            ? pathAndQuery[(queryIndex + 1)..fragmentIndex]
            : pathAndQuery[(queryIndex + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            var encodedKey = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            if (!Decode(encodedKey).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var encodedValue = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
            return Decode(encodedValue.Replace('+', ' '));
        }

        return null;
    }

    private static bool IsCharacterRoute(string route) =>
        route.Equals("karakter", StringComparison.OrdinalIgnoreCase) ||
        route.Equals("karakters", StringComparison.OrdinalIgnoreCase) ||
        route.Equals("character", StringComparison.OrdinalIgnoreCase) ||
        route.Equals("characters", StringComparison.OrdinalIgnoreCase);

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }
}
