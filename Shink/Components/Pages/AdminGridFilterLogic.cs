using Shink.Services;

namespace Shink.Components.Pages;

public static class AdminGridFilterOptionValues
{
    public const string All = "";
    public const string None = "__none";
    public const string True = "true";
    public const string False = "false";
}

public sealed class AdminSubscriberColumnFilters
{
    public string SubscriberText { get; set; } = string.Empty;
    public string MobileText { get; set; } = string.Empty;
    public string TierText { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string PaymentProvider { get; set; } = string.Empty;
    public string SubscriptionStatus { get; set; } = string.Empty;

    public bool HasActiveFilters =>
        HasValue(SubscriberText) ||
        HasValue(MobileText) ||
        HasValue(TierText) ||
        HasValue(SourceSystem) ||
        HasValue(PaymentProvider) ||
        HasValue(SubscriptionStatus);

    public void Clear()
    {
        SubscriberText = string.Empty;
        MobileText = string.Empty;
        TierText = string.Empty;
        SourceSystem = string.Empty;
        PaymentProvider = string.Empty;
        SubscriptionStatus = string.Empty;
    }

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value);
}

public sealed class AdminStoryColumnFilters
{
    public string TitleText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AccessLevel { get; set; } = string.Empty;

    public bool HasActiveFilters =>
        HasValue(TitleText) ||
        HasValue(Status) ||
        HasValue(AccessLevel);

    public void Clear()
    {
        TitleText = string.Empty;
        Status = string.Empty;
        AccessLevel = string.Empty;
    }

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value);
}

public sealed class AdminPlaylistColumnFilters
{
    public string TitleText { get; set; } = string.Empty;
    public string IsEnabled { get; set; } = string.Empty;
    public string ShowOnHome { get; set; } = string.Empty;
    public string ShowShowcase { get; set; } = string.Empty;

    public bool HasActiveFilters =>
        HasValue(TitleText) ||
        HasValue(IsEnabled) ||
        HasValue(ShowOnHome) ||
        HasValue(ShowShowcase);

    public void Clear()
    {
        TitleText = string.Empty;
        IsEnabled = string.Empty;
        ShowOnHome = string.Empty;
        ShowShowcase = string.Empty;
    }

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value);
}

public static class AdminGridFilterLogic
{
    public static IReadOnlyList<AdminStoryRecord> FilterStories(
        IReadOnlyList<AdminStoryRecord> source,
        string? searchTerm,
        AdminStoryColumnFilters? filters)
    {
        if (source.Count == 0)
        {
            return source;
        }

        var normalizedSearch = NormalizeText(searchTerm);
        var normalizedTitle = NormalizeText(filters?.TitleText);
        var normalizedStatus = NormalizeToken(filters?.Status);
        var normalizedAccessLevel = NormalizeToken(filters?.AccessLevel);

        if (normalizedSearch is null &&
            normalizedTitle is null &&
            normalizedStatus is null &&
            normalizedAccessLevel is null)
        {
            return source;
        }

        return source
            .Where(story =>
                MatchesSearch(story, normalizedSearch) &&
                MatchesText(story.Title, normalizedTitle) &&
                MatchesToken(story.Status, normalizedStatus) &&
                MatchesToken(story.AccessLevel, normalizedAccessLevel))
            .ToArray();
    }

    public static IReadOnlyList<AdminPlaylistRecord> FilterPlaylists(
        IReadOnlyList<AdminPlaylistRecord> source,
        string? searchTerm,
        AdminPlaylistColumnFilters? filters)
    {
        if (source.Count == 0)
        {
            return source;
        }

        var normalizedSearch = NormalizeText(searchTerm);
        var normalizedTitle = NormalizeText(filters?.TitleText);
        var normalizedEnabled = NormalizeToken(filters?.IsEnabled);
        var normalizedShowOnHome = NormalizeToken(filters?.ShowOnHome);
        var normalizedShowShowcase = NormalizeToken(filters?.ShowShowcase);

        if (normalizedSearch is null &&
            normalizedTitle is null &&
            normalizedEnabled is null &&
            normalizedShowOnHome is null &&
            normalizedShowShowcase is null)
        {
            return source;
        }

        return source
            .Where(playlist =>
                MatchesPlaylistSearch(playlist, normalizedSearch) &&
                MatchesText(playlist.Title, normalizedTitle) &&
                MatchesBool(playlist.IsEnabled, normalizedEnabled) &&
                MatchesBool(playlist.ShowOnHome, normalizedShowOnHome) &&
                MatchesBool(playlist.ShowShowcaseImageOnLuisterPage, normalizedShowShowcase))
            .ToArray();
    }

    private static bool MatchesSearch(AdminStoryRecord story, string? searchTerm)
    {
        if (searchTerm is null)
        {
            return true;
        }

        return story.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
               story.Slug.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPlaylistSearch(AdminPlaylistRecord playlist, string? searchTerm)
    {
        if (searchTerm is null)
        {
            return true;
        }

        return playlist.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
               playlist.Slug.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesText(string? value, string? filterText)
    {
        if (filterText is null)
        {
            return true;
        }

        return value?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private static bool MatchesToken(string? value, string? filterToken)
    {
        if (filterToken is null)
        {
            return true;
        }

        return string.Equals(value?.Trim(), filterToken, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesBool(bool value, string? filterToken) =>
        filterToken switch
        {
            AdminGridFilterOptionValues.True => value,
            AdminGridFilterOptionValues.False => !value,
            _ => true
        };

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }
}
