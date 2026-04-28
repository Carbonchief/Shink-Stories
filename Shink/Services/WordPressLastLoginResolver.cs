using System.Globalization;
using System.Text.RegularExpressions;

namespace Shink.Services;

public static partial class WordPressLastLoginResolver
{
    public static DateTimeOffset? Resolve(
        DateTimeOffset? socialLoginAt,
        string? pmproLoginsSerialized,
        string? sessionTokensSerialized)
    {
        var latest = socialLoginAt;
        latest = Max(latest, ParsePmProLastLogin(pmproLoginsSerialized));
        latest = Max(latest, ParseSessionTokenLastLogin(sessionTokensSerialized));
        return latest;
    }

    public static DateTimeOffset? ParsePmProLastLogin(string? serializedValue)
    {
        if (string.IsNullOrWhiteSpace(serializedValue))
        {
            return null;
        }

        var match = PmProLastLoginRegex().Match(serializedValue);
        if (!match.Success)
        {
            return null;
        }

        var dateText = match.Groups["date"].Value.Trim();
        if (string.IsNullOrWhiteSpace(dateText))
        {
            return null;
        }

        if (!DateTime.TryParse(
                dateText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsedDate))
        {
            return null;
        }

        // PMPro stores only the calendar date, so keep it conservative and let
        // more precise sources on the same day win.
        return new DateTimeOffset(DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc));
    }

    public static DateTimeOffset? ParseSessionTokenLastLogin(string? serializedValue)
    {
        if (string.IsNullOrWhiteSpace(serializedValue))
        {
            return null;
        }

        long latestUnixTimestamp = 0;
        var hasValue = false;
        foreach (Match match in SessionTokenLoginRegex().Matches(serializedValue))
        {
            if (!match.Success ||
                !long.TryParse(match.Groups["unix"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var unixTimestamp) ||
                unixTimestamp <= 0)
            {
                continue;
            }

            if (!hasValue || unixTimestamp > latestUnixTimestamp)
            {
                latestUnixTimestamp = unixTimestamp;
                hasValue = true;
            }
        }

        return hasValue ? DateTimeOffset.FromUnixTimeSeconds(latestUnixTimestamp) : null;
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return right > left ? right : left;
    }

    [GeneratedRegex("s:4:\"last\";s:\\d+:\"(?<date>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex PmProLastLoginRegex();

    [GeneratedRegex("s:5:\"login\";i:(?<unix>\\d+);", RegexOptions.CultureInvariant)]
    private static partial Regex SessionTokenLoginRegex();
}
