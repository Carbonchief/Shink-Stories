using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public sealed class PlaylistPlaybackState
{
    private IReadOnlyList<MobileStorySummary> _stories = Array.Empty<MobileStorySummary>();
    private IReadOnlyList<string> _shuffleOrder = Array.Empty<string>();

    public string? Slug { get; private set; }

    public string? Title { get; private set; }

    public MobilePlaylist? CurrentPlaylist { get; private set; }

    public IReadOnlyList<MobileStorySummary> Stories => _stories;

    public bool IsOfflineQueue { get; private set; }

    public bool IsAutoplayEnabled { get; private set; }

    public bool IsShuffleEnabled { get; private set; }

    public int? AutoplayLimitStories { get; private set; }

    public int AutoplayStoriesPlayedInRun { get; private set; }

    public void Set(MobilePlaylist playlist, MobileStorySummary? currentStory = null) =>
        SetCore(playlist, currentStory, isOfflineQueue: false, autoplay: true);

    // The catalogue can supply neighbouring stories even when the listener selected only a story.
    public static PlaylistPlaybackState ForStory(MobilePlaylist? playlist, MobileStorySummary story, bool autoplay = false)
    {
        var queue = new PlaylistPlaybackState();
        if (playlist is not null)
        {
            queue.SetCore(playlist, story, isOfflineQueue: false, autoplay: autoplay);
        }
        return queue;
    }

    public static PlaylistPlaybackState CreateOfflineQueue(IEnumerable<MobileStorySummary> downloads, bool autoplay)
    {
        var stories = downloads.Where(story =>
            !string.Equals(story.StoryType, "video", StringComparison.OrdinalIgnoreCase)).ToArray();
        var artwork = stories.FirstOrDefault()?.ImageUrl ?? string.Empty;
        var queue = new PlaylistPlaybackState();
        queue.SetCore(new MobilePlaylist("afgelaai-aflyn", "Afgelaai", "Jou afgelaaide stories",
            artwork, artwork, stories), currentStory: null, isOfflineQueue: true, autoplay: autoplay);
        return queue;
    }

    private void SetCore(
        MobilePlaylist playlist,
        MobileStorySummary? currentStory,
        bool isOfflineQueue,
        bool autoplay)
    {
        var isNewPlaylistContext = CurrentPlaylist is null ||
                                   !string.Equals(CurrentPlaylist.Slug, playlist.Slug, StringComparison.OrdinalIgnoreCase) ||
                                   IsOfflineQueue != isOfflineQueue;
        if (isNewPlaylistContext)
        {
            IsAutoplayEnabled = autoplay;
            AutoplayStoriesPlayedInRun = 0;
        }

        var normalizedStories = playlist.Stories
            .Select(story => NormalizePlaylistStory(story, preserveSource: isOfflineQueue))
            .ToArray();
        CurrentPlaylist = playlist with
        {
            Stories = normalizedStories,
            ShowcaseStory = playlist.ShowcaseStory is null
                ? null
                : NormalizePlaylistStory(playlist.ShowcaseStory, preserveSource: isOfflineQueue)
        };
        Slug = CurrentPlaylist.Slug;
        Title = CurrentPlaylist.Title;
        IsOfflineQueue = isOfflineQueue;
        _stories = normalizedStories;
        RefreshShuffleOrder(currentStory is null
            ? null
            : NormalizePlaylistStory(currentStory, preserveSource: isOfflineQueue));
    }

    public IReadOnlyList<MobileStorySummary> GetPlaybackStories(MobileStorySummary? currentStory = null)
    {
        if (!IsShuffleEnabled || _stories.Count <= 1)
        {
            return _stories;
        }

        RefreshShuffleOrder(currentStory is null
            ? null
            : NormalizePlaylistStory(currentStory, preserveSource: IsOfflineQueue));

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

    public MobileStorySummary? GetNextAudioStory(MobileStorySummary currentStory)
    {
        var currentKey = GetStoryKey(currentStory);
        return GetPlaybackStories(currentStory)
            .SkipWhile(story => !string.Equals(GetStoryKey(story), currentKey, StringComparison.OrdinalIgnoreCase))
            .Skip(1)
            .FirstOrDefault(story => !string.Equals(story.StoryType, "video", StringComparison.OrdinalIgnoreCase));
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
        CurrentPlaylist = null;
        _stories = Array.Empty<MobileStorySummary>();
        _shuffleOrder = Array.Empty<string>();
        IsOfflineQueue = false;
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

        if (hasSameStories)
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
        $"{(string.IsNullOrWhiteSpace(story.Source) ? "luister" : story.Source)}|{story.Slug}";

    private static MobileStorySummary NormalizePlaylistStory(MobileStorySummary story, bool preserveSource = false) =>
        preserveSource || string.Equals(story.Source, "luister", StringComparison.OrdinalIgnoreCase)
            ? story
            : story with { Source = "luister" };
}
