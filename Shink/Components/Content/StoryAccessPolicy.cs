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
                AllStoriesMonthlyTierCode,
                AllStoriesYearlyTierCode
            ],
            StoryAccessRequirement.AllStoriesOnly =>
            [
                AllStoriesMonthlyTierCode,
                AllStoriesYearlyTierCode
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

        return ContainsStoryCornerMarker(story.AudioFileName) ||
               ContainsStoryCornerMarker(story.Title) ||
               ContainsStoryCornerMarker(story.Slug);
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
