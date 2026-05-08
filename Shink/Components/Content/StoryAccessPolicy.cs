namespace Shink.Components.Content;

public enum StoryAccessRequirement
{
    Unknown = 0,
    Free = 1,
    StoryCornerOrAllStories = 2,
    AllStoriesOnly = 3
}

public static class StoryAccessPolicy
{
    public const string StoryCornerMonthlyTierCode = "story_corner_monthly";
    public const string AllStoriesMonthlyTierCode = "all_stories_monthly";
    public const string AllStoriesYearlyTierCode = "all_stories_yearly";
    public const string SchoolSmallYearlyTierCode = "school_small_yearly";
    public const string SchoolMediumYearlyTierCode = "school_medium_yearly";
    public const string SchoolLargeYearlyTierCode = "school_large_yearly";
    public const string GratisPlaylistSlug = "gratis-stories";
    public const string StoryCornerPlaylistSlug = "storie-hoekie";

    public static IReadOnlyList<string> AllStoriesTierCodes { get; } =
    [
        AllStoriesMonthlyTierCode,
        AllStoriesYearlyTierCode,
        SchoolSmallYearlyTierCode,
        SchoolMediumYearlyTierCode,
        SchoolLargeYearlyTierCode
    ];

    public static StoryAccessRequirement ResolveRequirement(string? source, StoryItem? story)
    {
        if (string.Equals(source, "gratis", StringComparison.OrdinalIgnoreCase))
        {
            return StoryAccessRequirement.Free;
        }

        if (string.Equals(source, "luister", StringComparison.OrdinalIgnoreCase))
        {
            if (story is not null && string.Equals(story.AccessLevel, "free", StringComparison.OrdinalIgnoreCase))
            {
                return StoryAccessRequirement.Free;
            }

            if (story is not null && IsStoryCornerStory(story))
            {
                return StoryAccessRequirement.StoryCornerOrAllStories;
            }

            return StoryAccessRequirement.AllStoriesOnly;
        }

        return StoryAccessRequirement.Unknown;
    }

    public static IReadOnlyList<string> GetAllowedTierCodes(StoryAccessRequirement requirement) =>
        requirement switch
        {
            StoryAccessRequirement.StoryCornerOrAllStories =>
            [
                StoryCornerMonthlyTierCode,
                ..AllStoriesTierCodes
            ],
            StoryAccessRequirement.AllStoriesOnly =>
            [
                ..AllStoriesTierCodes
            ],
            _ => Array.Empty<string>()
        };

    public static bool RequiresPaidSubscription(StoryAccessRequirement requirement) =>
        requirement is StoryAccessRequirement.StoryCornerOrAllStories or StoryAccessRequirement.AllStoriesOnly;

    public static bool TryParseStoryPath(string? pathAndQuery, out string source, out string slug)
    {
        source = string.Empty;
        slug = string.Empty;

        if (string.IsNullOrWhiteSpace(pathAndQuery))
        {
            return false;
        }

        var candidate = pathAndQuery.Trim();
        var queryIndex = candidate.IndexOf('?');
        if (queryIndex >= 0)
        {
            candidate = candidate[..queryIndex];
        }

        if (!candidate.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var trimmed = candidate.Trim('/');
        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        var candidateSource = segments[0];
        if (!string.Equals(candidateSource, "gratis", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidateSource, "luister", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encodedSlug = segments[1];
        if (string.IsNullOrWhiteSpace(encodedSlug))
        {
            return false;
        }

        try
        {
            slug = Uri.UnescapeDataString(encodedSlug).Trim();
        }
        catch
        {
            slug = encodedSlug.Trim();
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        source = candidateSource.ToLowerInvariant();
        return true;
    }

    public static bool IsStoryCornerStory(StoryItem story)
    {
        if (story is null)
        {
            return false;
        }

        if (story.PlaylistSlugs is { Count: > 0 })
        {
            return ContainsStoryCornerPlaylistSlug(story.PlaylistSlugs);
        }

        return ContainsStoryCornerMarker(story.AudioFileName) ||
               ContainsStoryCornerMarker(story.Title) ||
               ContainsStoryCornerMarker(story.Slug);
    }

    private static bool ContainsStoryCornerPlaylistSlug(IReadOnlyList<string>? playlistSlugs)
    {
        if (playlistSlugs is null || playlistSlugs.Count == 0)
        {
            return false;
        }

        return playlistSlugs.Any(slug =>
            string.Equals(slug?.Trim(), StoryCornerPlaylistSlug, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsStoryCornerMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value
            .Trim()
            .ToLowerInvariant()
            .Replace('_', ' ')
            .Replace('-', ' ');

        return normalized.Contains("storie hoekie", StringComparison.Ordinal) ||
               normalized.Contains("story hoekie", StringComparison.Ordinal);
    }
}
