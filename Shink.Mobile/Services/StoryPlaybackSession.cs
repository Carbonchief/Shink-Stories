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

public sealed record StoryAutoplayAdvancedEventArgs(
    MobileStoryDetailResponse Detail,
    MobilePlaylist Playlist);

public sealed class StoryPlaybackSession
{
    private const double ListenFlushThresholdSeconds = 12;
    private const double ListenMaxEventSeconds = 600;
    private const double ListenMinEventSeconds = 0.2;

    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly MobileApiClient _apiClient;
    private readonly ContinueListeningState _continueListeningState;
    private readonly MobileAppLifecycleService _lifecycleService;
    private readonly IOfflineStoryDownloadService _offlineDownloadService;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private IDispatcherTimer? _trackingTimer;
    private StoryPlaybackItem? _current;
    private PreparedAutoplayItem? _preparedAutoplay;
    private CancellationTokenSource? _autoplayPreparationCts;
    private Task? _autoplayPreparationTask;
    private double _pendingListenSeconds;
    private TimeSpan? _lastTrackedPosition;
    private bool _isChangingPlayback;
    private int _isAutoplayAdvancing;

    public StoryPlaybackSession(
        IAudioPlaybackService audioPlaybackService,
        MobileApiClient apiClient,
        ContinueListeningState continueListeningState,
        MobileAppLifecycleService lifecycleService,
        IOfflineStoryDownloadService offlineDownloadService,
        PlaylistPlaybackState playlistPlaybackState)
    {
        _audioPlaybackService = audioPlaybackService;
        _apiClient = apiClient;
        _continueListeningState = continueListeningState;
        _lifecycleService = lifecycleService;
        _offlineDownloadService = offlineDownloadService;
        _playlistPlaybackState = playlistPlaybackState;

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

    public event EventHandler<StoryAutoplayAdvancedEventArgs>? AutoplayAdvanced;

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
            CancelAutoplayPreparation();
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
        ScheduleAutoplayPreparation(playbackItem);
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

    public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        var current = _current;
        if (current is null)
        {
            return;
        }

        var duration = Duration;
        var maximumSeconds = duration is { TotalSeconds: > 0 }
            ? duration.Value.TotalSeconds
            : Math.Max(0, position.TotalSeconds);
        var target = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, maximumSeconds));

        FlushPendingListen("seek", force: true);
        await _audioPlaybackService.SeekAsync(target, cancellationToken);
        _lastTrackedPosition = target;
        _continueListeningState.UpdateProgress(
            current.Story.Slug,
            NormalizeSource(current.Story.Source),
            decimal.Round((decimal)target.TotalSeconds, 3, MidpointRounding.AwayFromZero),
            ResolveDurationSeconds(current));
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
        CancelAutoplayPreparation();
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

    public void RefreshAutoplayPreparation()
    {
        if (_current is { } current && _audioPlaybackService.IsPlaying)
        {
            ScheduleAutoplayPreparation(current);
        }
        else
        {
            CancelAutoplayPreparation();
        }
    }

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
        var endedItem = _current;
        if (endedItem is null)
        {
            return;
        }

        FlushPendingListen("ended", force: true, isCompleted: true);
        StopTrackingTimer();
        RaiseChanged();
        _ = AdvanceAutoplayAsync(endedItem);
    }

    private void ScheduleAutoplayPreparation(StoryPlaybackItem current)
    {
        CancelAutoplayPreparation();
        if (!_playlistPlaybackState.IsAutoplayEnabled || ResolveOriginPlaylist(current) is null)
        {
            return;
        }

        _autoplayPreparationCts = new CancellationTokenSource();
        _autoplayPreparationTask = PrepareNextAutoplayAsync(current, _autoplayPreparationCts.Token);
    }

    private async Task PrepareNextAutoplayAsync(
        StoryPlaybackItem current,
        CancellationToken cancellationToken)
    {
        try
        {
            var playlist = ResolveOriginPlaylist(current);
            var nextStory = playlist is null ? null : ResolveNextStory(current.Story);
            if (playlist is null || nextStory is null || nextStory.IsLocked)
            {
                return;
            }

            MobileStoryDetailResponse? detail;
            if (_playlistPlaybackState.IsOfflineQueue)
            {
                var download = await _offlineDownloadService.GetDownloadAsync(
                    nextStory.Slug,
                    nextStory.Source,
                    cancellationToken);
                detail = download is null ? null : _offlineDownloadService.CreateOfflineDetail(download);
            }
            else
            {
                try
                {
                    detail = await _apiClient.GetStoryAsync(nextStory.Slug, "luister", cancellationToken);
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    var download = await _offlineDownloadService.GetDownloadAsync(
                        nextStory.Slug,
                        "luister",
                        cancellationToken);
                    detail = download is null ? null : _offlineDownloadService.CreateOfflineDetail(download);
                }
            }

            if (detail is null ||
                detail.RequiresSubscription ||
                string.IsNullOrWhiteSpace(detail.AudioUrl))
            {
                return;
            }

            var offlinePlaybackUrl = await _offlineDownloadService.ResolvePlayableAudioAsync(
                detail,
                cancellationToken);
            if (_playlistPlaybackState.IsOfflineQueue && string.IsNullOrWhiteSpace(offlinePlaybackUrl))
            {
                return;
            }

            var playbackUrl = string.IsNullOrWhiteSpace(offlinePlaybackUrl)
                ? await _apiClient.PrepareAudioPlaybackSourceAsync(
                    detail.AudioUrl,
                    detail.Story.Slug,
                    detail.Story.Source,
                    cancellationToken)
                : offlinePlaybackUrl;
            await _audioPlaybackService.PrepareAsync(playbackUrl, cancellationToken);
            var artworkUrl = _apiClient.BuildImageUrl(detail.Story.ImageUrl);

            if (!cancellationToken.IsCancellationRequested &&
                _current?.TrackingSessionId == current.TrackingSessionId)
            {
                _preparedAutoplay = new PreparedAutoplayItem(
                    current.TrackingSessionId,
                    detail,
                    playbackUrl,
                    artworkUrl,
                    playlist);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Autoplay preparation is best-effort. A foreground completion gets one fresh retry.
        }
    }

    private async Task AdvanceAutoplayAsync(StoryPlaybackItem endedItem)
    {
        if (Interlocked.Exchange(ref _isAutoplayAdvancing, 1) == 1)
        {
            return;
        }

        try
        {
            if (_current?.TrackingSessionId != endedItem.TrackingSessionId ||
                !_playlistPlaybackState.CanAutoplayAdvance(endedItem.Story))
            {
                return;
            }

            if (_autoplayPreparationTask is { } preparationTask)
            {
                await preparationTask;
            }

            var prepared = _preparedAutoplay;
            if (prepared is null && !_lifecycleService.IsBackgrounded)
            {
                ScheduleAutoplayPreparation(endedItem);
                if (_autoplayPreparationTask is { } retryTask)
                {
                    await retryTask;
                }
                prepared = _preparedAutoplay;
            }

            if (prepared is null ||
                prepared.PreviousTrackingSessionId != endedItem.TrackingSessionId ||
                _current?.TrackingSessionId != endedItem.TrackingSessionId)
            {
                return;
            }

            _preparedAutoplay = null;
            await PlayAsync(
                prepared.PlaybackUrl,
                prepared.Detail.Story,
                prepared.ArtworkUrl,
                prepared.Playlist.Slug,
                prepared.Playlist.Title,
                prepared.Detail.Story.DurationSeconds,
                _playlistPlaybackState.IsOfflineQueue ? null : prepared.Playlist);
            _playlistPlaybackState.TrackAutoplayAdvance(prepared.Detail.Story);
            RaiseAutoplayAdvanced(prepared.Detail, prepared.Playlist);
        }
        catch
        {
            // Never surface a delayed background connection alert when the app is reopened.
        }
        finally
        {
            Interlocked.Exchange(ref _isAutoplayAdvancing, 0);
        }
    }

    private MobilePlaylist? ResolveOriginPlaylist(StoryPlaybackItem current)
    {
        if (current.OriginPlaylist is { } originPlaylist &&
            !string.IsNullOrWhiteSpace(current.PlaylistSlug) &&
            string.Equals(originPlaylist.Slug, current.PlaylistSlug, StringComparison.OrdinalIgnoreCase))
        {
            return originPlaylist;
        }

        var currentPlaylist = _playlistPlaybackState.CurrentPlaylist;
        return currentPlaylist is not null &&
               string.Equals(currentPlaylist.Slug, current.PlaylistSlug, StringComparison.OrdinalIgnoreCase)
            ? currentPlaylist
            : null;
    }

    private MobileStorySummary? ResolveNextStory(MobileStorySummary currentStory)
    {
        var stories = _playlistPlaybackState.GetPlaybackStories(currentStory);
        var currentIndex = stories.ToList().FindIndex(story => SameStory(story, currentStory));
        return currentIndex >= 0 && currentIndex < stories.Count - 1
            ? stories[currentIndex + 1]
            : null;
    }

    private static bool SameStory(MobileStorySummary left, MobileStorySummary right) =>
        string.Equals(left.Slug, right.Slug, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(NormalizeSource(left.Source), NormalizeSource(right.Source), StringComparison.OrdinalIgnoreCase);

    private void CancelAutoplayPreparation()
    {
        _autoplayPreparationCts?.Cancel();
        _autoplayPreparationCts?.Dispose();
        _autoplayPreparationCts = null;
        _autoplayPreparationTask = null;
        _preparedAutoplay = null;
    }

    private void RaiseAutoplayAdvanced(MobileStoryDetailResponse detail, MobilePlaylist playlist)
    {
        void Raise() => AutoplayAdvanced?.Invoke(this, new StoryAutoplayAdvancedEventArgs(detail, playlist));
        if (MainThread.IsMainThread)
        {
            Raise();
            return;
        }

        MainThread.BeginInvokeOnMainThread(Raise);
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

    private sealed record PreparedAutoplayItem(
        Guid PreviousTrackingSessionId,
        MobileStoryDetailResponse Detail,
        string PlaybackUrl,
        string ArtworkUrl,
        MobilePlaylist Playlist);
}
