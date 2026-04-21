using System.Globalization;
using MySqlConnector;

namespace Shink.Services;

public static class WordPressImportDateConverter
{
    public static DateTimeOffset? ConvertToNullableDateTime(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return value switch
        {
            DateTimeOffset dateTimeOffset => Normalize(dateTimeOffset),
            DateTime dateTime => Normalize(dateTime),
            MySqlDateTime mySqlDateTime => Normalize(mySqlDateTime),
            string text => Parse(text),
            _ => null
        };
    }

    public static DateTimeOffset ResolveSubscribedAt(DateTimeOffset? userRegistered, params DateTimeOffset?[] candidates)
    {
        var earliestHistoricalDate = candidates
            .Where(candidate => candidate.HasValue)
            .Select(candidate => candidate!.Value)
            .OrderBy(candidate => candidate)
            .FirstOrDefault();

        return earliestHistoricalDate != default
            ? earliestHistoricalDate
            : userRegistered ?? DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset? Normalize(DateTimeOffset value) =>
        value == DateTimeOffset.MinValue ? null : value.ToUniversalTime();

    private static DateTimeOffset? Normalize(DateTime value)
    {
        if (value == DateTime.MinValue)
        {
            return null;
        }

        var utcDateTime = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utcDateTime);
    }

    private static DateTimeOffset? Normalize(MySqlDateTime value)
    {
        if (!value.IsValidDateTime)
        {
            return null;
        }

        return Normalize(value.GetDateTime());
    }

    private static DateTimeOffset? Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedOffset))
        {
            return Normalize(parsedOffset);
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedDateTime))
        {
            return Normalize(parsedDateTime);
        }

        return null;
    }
}
