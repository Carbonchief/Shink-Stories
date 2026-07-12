using System.Text.Json;
using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public sealed class ContinueListeningState
{
    private const string PreferenceKey = "schink_continue_listening_story_v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private ContinueListeningItem? _current = LoadCurrent();

    public event Action<ContinueListeningItem?>? Changed;

    public ContinueListeningItem? Current => _current;

    public void Save(
        MobileStorySummary story,
        string? playlistSlug,
        string? playlistTitle,
        decimal? positionSeconds = null,
        decimal? durationSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(story.Slug))
        {
            return;
        }

        var current = _current;
        var preservedDurationSeconds = current is not null &&
            string.Equals(current.Slug, story.Slug, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(story.Source) ||
             string.IsNullOrWhiteSpace(current.Source) ||
             string.Equals(current.Source, story.Source, StringComparison.OrdinalIgnoreCase))
                ? current.DurationSeconds
                : null;

        var item = new ContinueListeningItem(
            story.Slug,
            string.IsNullOrWhiteSpace(story.Source) ? "luister" : story.Source,
            story.Title,
            story.Description,
            story.ImageUrl,
            story.ThumbnailUrl,
            NormalizeSeconds(durationSeconds) ?? story.DurationSeconds ?? preservedDurationSeconds,
            NormalizeSeconds(positionSeconds),
            playlistSlug,
            playlistTitle,
            DateTimeOffset.UtcNow);

        SaveItem(item);
    }

    public void UpdateProgress(string slug, string source, decimal? positionSeconds, decimal? durationSeconds)
    {
        var current = _current;
        if (current is null ||
            !string.Equals(current.Slug, slug, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.Source, source, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SaveItem(current with
        {
            PositionSeconds = NormalizeSeconds(positionSeconds) ?? current.PositionSeconds,
            DurationSeconds = NormalizeSeconds(durationSeconds) ?? current.DurationSeconds,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public void Clear()
    {
        Preferences.Default.Remove(PreferenceKey);
        _current = null;
        Changed?.Invoke(null);
    }

    private static decimal? NormalizeSeconds(decimal? seconds)
    {
        if (seconds is not > 0)
        {
            return null;
        }

        return decimal.Round(seconds.Value, 3, MidpointRounding.AwayFromZero);
    }

    private static ContinueListeningItem? LoadCurrent()
    {
        var json = Preferences.Default.Get(PreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ContinueListeningItem>(json, JsonOptions);
        }
        catch (JsonException)
        {
            Preferences.Default.Remove(PreferenceKey);
            return null;
        }
    }

    private void SaveItem(ContinueListeningItem item)
    {
        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(item, JsonOptions));
        _current = item;
        Changed?.Invoke(item);
    }
}

public sealed record ContinueListeningItem(
    string Slug,
    string Source,
    string Title,
    string Description,
    string ImageUrl,
    string ThumbnailUrl,
    decimal? DurationSeconds,
    decimal? PositionSeconds,
    string? PlaylistSlug,
    string? PlaylistTitle,
    DateTimeOffset UpdatedAtUtc);
