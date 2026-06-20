using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public sealed class PlaylistPlaybackState
{
    private IReadOnlyList<MobileStorySummary> _stories = Array.Empty<MobileStorySummary>();
    private IReadOnlyList<string> _shuffleOrder = Array.Empty<string>();

    public string? Slug { get; private set; }

    public string? Title { get; private set; }

    public IReadOnlyList<MobileStorySummary> Stories => _stories;

    public bool IsAutoplayEnabled { get; private set; }

    public bool IsShuffleEnabled { get; private set; }

    public int? AutoplayLimitStories { get; private set; }

    public int AutoplayStoriesPlayedInRun { get; private set; }

    public void Set(MobilePlaylist playlist, MobileStorySummary? currentStory = null)
    {
        Slug = playlist.Slug;
        Title = playlist.Title;
        _stories = playlist.Stories.ToArray();
        RefreshShuffleOrder(currentStory);
    }

    public IReadOnlyList<MobileStorySummary> GetPlaybackStories(MobileStorySummary? currentStory = null)
    {
        if (!IsShuffleEnabled || _stories.Count <= 1)
        {
            return _stories;
        }

        RefreshShuffleOrder(currentStory);

        var storiesByKey = _stories.ToDictionary(GetStoryKey, StringComparer.OrdinalIgnoreCase);
        var orderedStories = _shuffleOrder
            .Select(key => storiesByKey.TryGetValue(key, out var story) ? story : null)
            .Where(story => story is not null)
            .Cast<MobileStorySummary>()
            .ToList();

        foreach (var story in _stories)
        {
            if (!orderedStories.Any(existing => string.Equals(GetStoryKey(existing), GetStoryKey(story), StringComparison.OrdinalIgnoreCase)))
            {
                orderedStories.Add(story);
            }
        }

        return orderedStories;
    }

    public void SetAutoplay(bool isEnabled)
    {
        IsAutoplayEnabled = isEnabled;
        ResetAutoplayProgress();
    }

    public void SetAutoplayLimit(int? limitStories, MobileStorySummary? currentStory = null)
    {
        AutoplayLimitStories = limitStories is 3 or 5 ? limitStories : null;
        ResetAutoplayProgress(currentStory);
    }

    public void SetShuffle(bool isEnabled, MobileStorySummary? currentStory = null)
    {
        IsShuffleEnabled = isEnabled;
        RefreshShuffleOrder(currentStory);
    }

    public void Clear()
    {
        Slug = null;
        Title = null;
        _stories = Array.Empty<MobileStorySummary>();
        _shuffleOrder = Array.Empty<string>();
        IsAutoplayEnabled = false;
        IsShuffleEnabled = false;
        AutoplayLimitStories = null;
        AutoplayStoriesPlayedInRun = 0;
    }

    public bool CanAutoplayAdvance(MobileStorySummary? currentStory)
    {
        if (!IsAutoplayEnabled)
        {
            return false;
        }

        if (!AutoplayLimitStories.HasValue)
        {
            return true;
        }

        if (AutoplayStoriesPlayedInRun <= 0 && currentStory is not null)
        {
            AutoplayStoriesPlayedInRun = 1;
        }

        return AutoplayStoriesPlayedInRun < AutoplayLimitStories.Value;
    }

    public void TrackManualStorySelection(MobileStorySummary? currentStory)
    {
        ResetAutoplayProgress(currentStory);
    }

    public void TrackAutoplayAdvance(MobileStorySummary? currentStory)
    {
        if (!IsAutoplayEnabled || !AutoplayLimitStories.HasValue || currentStory is null)
        {
            AutoplayStoriesPlayedInRun = 0;
            return;
        }

        AutoplayStoriesPlayedInRun = Math.Max(1, AutoplayStoriesPlayedInRun + 1);
    }

    private void ResetAutoplayProgress(MobileStorySummary? currentStory = null)
    {
        if (!IsAutoplayEnabled || !AutoplayLimitStories.HasValue || currentStory is null)
        {
            AutoplayStoriesPlayedInRun = 0;
            return;
        }

        AutoplayStoriesPlayedInRun = 1;
    }

    private void RefreshShuffleOrder(MobileStorySummary? currentStory)
    {
        if (!IsShuffleEnabled || _stories.Count <= 1)
        {
            _shuffleOrder = Array.Empty<string>();
            return;
        }

        var storyKeys = _stories
            .Select(GetStoryKey)
            .ToArray();
        var hasSameStories =
            _shuffleOrder.Count == storyKeys.Length &&
            _shuffleOrder.All(key => storyKeys.Contains(key, StringComparer.OrdinalIgnoreCase));
        var currentStoryKey = currentStory is null ? null : GetStoryKey(currentStory);

        if (hasSameStories &&
            (string.IsNullOrWhiteSpace(currentStoryKey) ||
             string.Equals(_shuffleOrder.FirstOrDefault(), currentStoryKey, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var remainingKeys = storyKeys
            .Where(key => !string.Equals(key, currentStoryKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        if (!string.IsNullOrWhiteSpace(currentStoryKey) &&
            storyKeys.Contains(currentStoryKey, StringComparer.OrdinalIgnoreCase))
        {
            remainingKeys.Insert(0, currentStoryKey);
        }

        _shuffleOrder = remainingKeys;
    }

    private static string GetStoryKey(MobileStorySummary story) =>
        $"{story.Source}|{story.Slug}";
}
