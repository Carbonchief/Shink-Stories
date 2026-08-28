using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public sealed record StoryPlaybackItem(
    MobileStorySummary Story,
    string PlaybackUrl,
    string ArtworkUrl,
    string? PlaylistSlug,
    string? PlaylistTitle,
    MobilePlaylist? OriginPlaylist,
    decimal? CatalogDurationSeconds,
    Guid TrackingSessionId);

public sealed class StoryPlaybackSession
{
    private const double ListenFlushThresholdSeconds = 12;
    private const double ListenMaxEventSeconds = 600;
    private const double ListenMinEventSeconds = 0.2;

    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly MobileApiClient _apiClient;
    private readonly ContinueListeningState _continueListeningState;
    private readonly MobileAppLifecycleService _lifecycleService;
    private IDispatcherTimer? _trackingTimer;
    private StoryPlaybackItem? _current;
    private double _pendingListenSeconds;
    private TimeSpan? _lastTrackedPosition;
    private bool _isChangingPlayback;

    public StoryPlaybackSession(
        IAudioPlaybackService audioPlaybackService,
        MobileApiClient apiClient,
        ContinueListeningState continueListeningState,
        MobileAppLifecycleService lifecycleService)
    {
        _audioPlaybackService = audioPlaybackService;
        _apiClient = apiClient;
        _continueListeningState = continueListeningState;
        _lifecycleService = lifecycleService;

        _audioPlaybackService.PlaybackStateChanged += OnAudioPlaybackStateChanged;
        _audioPlaybackService.PlaybackEnded += OnAudioPlaybackEnded;
        _lifecycleService.Stopping += OnAppStopping;
        _lifecycleService.Destroying += OnAppDestroying;
    }

    public StoryPlaybackItem? Current => _current;

    internal MobileApiClient ImageApiClient => _apiClient;

    public bool HasActiveStory => _current is not null;

    public bool IsPlaying => _current is not null && _audioPlaybackService.IsPlaying;

    public TimeSpan CurrentPosition => _current is null
        ? TimeSpan.Zero
        : _audioPlaybackService.CurrentPosition;

    public TimeSpan? Duration => _current is null
        ? null
        : _audioPlaybackService.Duration ?? ToTimeSpan(_current.CatalogDurationSeconds);

    public event EventHandler? Changed;

    public bool IsCurrentStory(MobileStorySummary? story) =>
        story is not null && IsCurrentStory(story.Slug, story.Source);

    public bool IsCurrentStory(string? slug, string? source)
    {
        var current = _current;
        return current is not null &&
               !string.IsNullOrWhiteSpace(slug) &&
               string.Equals(current.Story.Slug, slug, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   NormalizeSource(current.Story.Source),
                   NormalizeSource(source),
                   StringComparison.OrdinalIgnoreCase);
    }

    public async Task PlayAsync(
        string playbackUrl,
        MobileStorySummary story,
        string artworkUrl,
        string? playlistSlug = null,
        string? playlistTitle = null,
        decimal? catalogDurationSeconds = null,
        MobilePlaylist? originPlaylist = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playbackUrl);

        var current = _current;
        var isSamePlayback = IsCurrentStory(story) &&
                             string.Equals(current?.PlaybackUrl, playbackUrl, StringComparison.Ordinal);
        if (!isSamePlayback)
        {
            FlushPendingListen("replaced", force: true);
            StopTrackingTimer();
        }

        var playbackItem = new StoryPlaybackItem(
            story,
            playbackUrl,
            artworkUrl,
            playlistSlug,
            playlistTitle,
            originPlaylist,
            catalogDurationSeconds ?? story.DurationSeconds,
            isSamePlayback && current is not null ? current.TrackingSessionId : Guid.NewGuid());

        _isChangingPlayback = true;
        try
        {
            await _audioPlaybackService.PlayAsync(
                playbackUrl,
                new AudioPlaybackMetadata(story.Title, "Schink Stories", artworkUrl));
        }
        catch
        {
            if (!isSamePlayback)
            {
                _current = null;
                ResetTracking();
                RaiseChanged();
            }

            throw;
        }
        finally
        {
            _isChangingPlayback = false;
        }

        _current = playbackItem;
        _pendingListenSeconds = 0;
        _lastTrackedPosition = _audioPlaybackService.CurrentPosition;
        _continueListeningState.Save(
            story,
            playlistSlug,
            playlistTitle,
            NormalizeTrackingSeconds(_audioPlaybackService.CurrentPosition.TotalSeconds),
            ResolveDurationSeconds(playbackItem));
        StartTrackingTimer();
        RaiseChanged();
    }

    public async Task ResumeAsync()
    {
        var current = _current;
        if (current is null)
        {
            return;
        }

        _isChangingPlayback = true;
        try
        {
            await _audioPlaybackService.PlayAsync(
                current.PlaybackUrl,
                new AudioPlaybackMetadata(current.Story.Title, "Schink Stories", current.ArtworkUrl));
        }
        finally
        {
            _isChangingPlayback = false;
        }

        _lastTrackedPosition = _audioPlaybackService.CurrentPosition;
        StartTrackingTimer();
        RaiseChanged();
    }

    public void Pause()
    {
        if (_current is null)
        {
            return;
        }

        _audioPlaybackService.Pause();
    }

    public void Stop()
    {
        if (_current is null)
        {
            return;
        }

        FlushPendingListen("stop", force: true);
        StopTrackingTimer();
        _isChangingPlayback = true;
        try
        {
            _audioPlaybackService.Stop();
        }
        finally
        {
            _isChangingPlayback = false;
        }

        _current = null;
        ResetTracking();
        RaiseChanged();
    }

    public void NotifyPageHidden() =>
        FlushPendingListen("pagehide", force: true);

    private void OnAudioPlaybackStateChanged(object? sender, EventArgs args)
    {
        if (_isChangingPlayback || _current is null)
        {
            return;
        }

        if (_audioPlaybackService.IsPlaying)
        {
            _lastTrackedPosition = _audioPlaybackService.CurrentPosition;
            StartTrackingTimer();
        }
        else
        {
            FlushPendingListen("pause", force: true);
            StopTrackingTimer();
        }

        RaiseChanged();
    }

    private void OnAudioPlaybackEnded(object? sender, EventArgs args)
    {
        if (_current is null)
        {
            return;
        }

        FlushPendingListen("ended", force: true, isCompleted: true);
        StopTrackingTimer();
        RaiseChanged();
    }

    private void OnAppStopping(object? sender, EventArgs args) =>
        FlushPendingListen("appstop", force: true);

    private void OnAppDestroying(object? sender, EventArgs args) =>
        FlushPendingListen("appdestroy", force: true);

    private void StartTrackingTimer()
    {
        if (!_audioPlaybackService.IsPlaying || _current is null)
        {
            return;
        }

        if (_trackingTimer is null)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            _trackingTimer = dispatcher.CreateTimer();
            _trackingTimer.Interval = TimeSpan.FromSeconds(1);
            _trackingTimer.Tick += (_, _) =>
            {
                if (_audioPlaybackService.IsPlaying && _current is not null)
                {
                    FlushPendingListen("progress", force: false);
                }
            };
        }

        if (!_trackingTimer.IsRunning)
        {
            _trackingTimer.Start();
        }
    }

    private void StopTrackingTimer() => _trackingTimer?.Stop();

    private void ResetTracking()
    {
        _pendingListenSeconds = 0;
        _lastTrackedPosition = null;
    }

    private void CaptureListenProgressDelta()
    {
        if (_current is null)
        {
            return;
        }

        var currentPosition = _audioPlaybackService.CurrentPosition;
        var previousPosition = _lastTrackedPosition ?? currentPosition;
        _lastTrackedPosition = currentPosition;

        var elapsedSeconds = (currentPosition - previousPosition).TotalSeconds;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
        {
            return;
        }

        _pendingListenSeconds += elapsedSeconds;
    }

    private void FlushPendingListen(string eventType, bool force, bool isCompleted = false)
    {
        var current = _current;
        if (current is null)
        {
            return;
        }

        CaptureListenProgressDelta();
        while (true)
        {
            var pendingSeconds = _pendingListenSeconds;
            if ((!force && pendingSeconds < ListenFlushThresholdSeconds) ||
                pendingSeconds < ListenMinEventSeconds)
            {
                return;
            }

            var listenedSeconds = Math.Min(pendingSeconds, ListenMaxEventSeconds);
            _pendingListenSeconds = Math.Max(0, pendingSeconds - listenedSeconds);
            var currentPosition = NormalizeTrackingSeconds(_audioPlaybackService.CurrentPosition.TotalSeconds);
            var durationSeconds = ResolveDurationSeconds(current);

            _continueListeningState.UpdateProgress(
                current.Story.Slug,
                NormalizeSource(current.Story.Source),
                currentPosition,
                durationSeconds);

            _ = _apiClient.TrackStoryListenAsync(
                current.Story.Slug,
                NormalizeSource(current.Story.Source),
                current.TrackingSessionId,
                eventType,
                decimal.Round((decimal)listenedSeconds, 3, MidpointRounding.AwayFromZero),
                currentPosition,
                durationSeconds,
                isCompleted);

            if (!force)
            {
                return;
            }
        }
    }

    private decimal? ResolveDurationSeconds(StoryPlaybackItem item) =>
        NormalizeTrackingSeconds(_audioPlaybackService.Duration?.TotalSeconds) ??
        NormalizeTrackingSeconds((double?)item.CatalogDurationSeconds);

    private static decimal? NormalizeTrackingSeconds(double? seconds)
    {
        if (seconds is not > 0 || !double.IsFinite(seconds.Value))
        {
            return null;
        }

        return decimal.Round((decimal)seconds.Value, 3, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeSource(string? source) =>
        string.IsNullOrWhiteSpace(source) ? "luister" : source;

    private static TimeSpan? ToTimeSpan(decimal? seconds) =>
        seconds is > 0 ? TimeSpan.FromSeconds((double)seconds.Value) : null;

    private void RaiseChanged()
    {
        if (MainThread.IsMainThread)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
