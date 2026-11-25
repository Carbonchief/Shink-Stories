using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public sealed record StoryPlaybackItem(
    MobileStorySummary Story,
    string PlaybackUrl,
    string ArtworkUrl,
    string? PlaylistSlug,
    string? PlaylistTitle,
    MobilePlaylist? OriginPlaylist,
    PlaylistPlaybackState PlaybackQueue,
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

    public bool IsCurrentStory(MobileStorySummary? story, PlaylistPlaybackState queue) =>
        ReferenceEquals(_current?.PlaybackQueue, queue) && IsCurrentStory(story);

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
        MobilePlaylist? originPlaylist = null,
        PlaylistPlaybackState? playbackQueue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playbackUrl);

        var current = _current;
        playbackQueue ??= !string.IsNullOrWhiteSpace(playlistSlug) &&
                          string.Equals(playlistSlug, _playlistPlaybackState.Slug, StringComparison.OrdinalIgnoreCase)
            ? _playlistPlaybackState
            : PlaylistPlaybackState.ForStory(originPlaylist, story);
        var isSamePlayback = IsCurrentStory(story) &&
                             ReferenceEquals(current?.PlaybackQueue, playbackQueue) &&
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
            playbackQueue,
            catalogDurationSeconds ?? story.DurationSeconds,
            isSamePlayback && current is not null ? current.TrackingSessionId : Guid.NewGuid());

        _isChangingPlayback = true;
        try
        {
            var playedUrl = await PlayAudioWithRetryAsync(playbackItem, playbackUrl);
            playbackItem = playbackItem with { PlaybackUrl = playedUrl };
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
            ResolveDurationSeconds(playbackItem),
            autoplayEnabled: playbackQueue.IsAutoplayEnabled);
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

        var download = await _offlineDownloadService.GetDownloadAsync(current.Story.Slug, current.Story.Source);
        var localAudio = download is null ? null : await _offlineDownloadService.ResolvePlayableAudioAsync(
            _offlineDownloadService.CreateOfflineDetail(download));
        if (!ReferenceEquals(_current, current)) return;
        if (current.PlaybackQueue.IsOfflineQueue && string.IsNullOrWhiteSpace(localAudio))
        {
            throw new InvalidOperationException("Die afgelaaide storie is nie meer beskikbaar vir aflyn speel nie.");
        }
        var switchToDownload = !string.IsNullOrWhiteSpace(localAudio) &&
            !string.Equals(current.PlaybackUrl, localAudio, StringComparison.Ordinal);
        var position = _audioPlaybackService.CurrentPosition;
        var playbackUrl = switchToDownload ? localAudio! : current.PlaybackUrl;

        _isChangingPlayback = true;
        try
        {
            var playedUrl = await PlayAudioWithRetryAsync(current, playbackUrl);
            if (!string.Equals(current.PlaybackUrl, playedUrl, StringComparison.Ordinal))
            {
                _current = current with { PlaybackUrl = playedUrl };
                await _audioPlaybackService.SeekAsync(position);
            }
        }
        finally
        {
            _isChangingPlayback = false;
        }

        _lastTrackedPosition = _audioPlaybackService.CurrentPosition;
        StartTrackingTimer();
        RaiseChanged();
    }

    private Task<string> PlayAudioWithRetryAsync(StoryPlaybackItem item, string playbackUrl) =>
        AudioPlaybackRetry.PlayAsync(playbackUrl,
            url => _audioPlaybackService.PlayAsync(url,
                new AudioPlaybackMetadata(item.Story.Title, "Schink Stories", item.ArtworkUrl,
                    item.Story.Slug, item.Story.Source, item.PlaylistSlug, ContentType: "story",
                    PlaybackSource: AudioPlaybackMetadata.ResolvePlaybackSource(url))),
            async () =>
            {
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return null;
                // The authorization endpoint issues a fresh, expiring audio URL.
                var detail = await _apiClient.GetStoryAsync(item.Story.Slug, item.Story.Source);
                if (detail is null || detail.RequiresSubscription || string.IsNullOrWhiteSpace(detail.AudioUrl))
                    return null;
                return await _apiClient.PrepareAudioPlaybackSourceAsync(
                    detail.AudioUrl, item.Story.Slug, item.Story.Source);
            });

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
        if (!ReferenceEquals(_current, current)) return;
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

    public void RefreshAutoplayPreparation(PlaylistPlaybackState queue)
    {
        if (_current is not { } current || !ReferenceEquals(current.PlaybackQueue, queue)) return;

        _continueListeningState.UpdateAutoplay(current.Story, queue.IsAutoplayEnabled);
        if (_audioPlaybackService.IsPlaying)
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
        if (!current.PlaybackQueue.IsAutoplayEnabled || ResolveOriginPlaylist(current) is null)
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
            var nextStory = playlist is null ? null : current.PlaybackQueue.GetNextAudioStory(current.Story);
            if (playlist is null || nextStory is null || nextStory.IsLocked)
            {
                return;
            }

            var download = await _offlineDownloadService.GetDownloadAsync(
                nextStory.Slug, nextStory.Source, cancellationToken);
            var detail = download is null ? null : _offlineDownloadService.CreateOfflineDetail(download);
            var localAudio = detail is null ? null : await _offlineDownloadService.ResolvePlayableAudioAsync(detail, cancellationToken);
            if (!current.PlaybackQueue.IsOfflineQueue && string.IsNullOrWhiteSpace(localAudio))
            {
                detail = await _apiClient.GetStoryAsync(nextStory.Slug, nextStory.Source, cancellationToken);
                if (detail is not null) await _offlineDownloadService.RefreshAccessAsync(detail, cancellationToken);
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
            if (current.PlaybackQueue.IsOfflineQueue && string.IsNullOrWhiteSpace(offlinePlaybackUrl))
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
            // Autoplay preparation is best-effort; the completion path retries once.
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
                !endedItem.PlaybackQueue.CanAutoplayAdvance(endedItem.Story))
            {
                return;
            }

            if (_autoplayPreparationTask is { } preparationTask)
            {
                await preparationTask;
            }

            if (_current?.TrackingSessionId != endedItem.TrackingSessionId)
            {
                return;
            }
            var prepared = _preparedAutoplay;
            if (prepared is null)
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
                _current?.TrackingSessionId != endedItem.TrackingSessionId ||
                !endedItem.PlaybackQueue.CanAutoplayAdvance(endedItem.Story))
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
                endedItem.PlaybackQueue.IsOfflineQueue ? null : prepared.Playlist,
                endedItem.PlaybackQueue);
            endedItem.PlaybackQueue.TrackAutoplayAdvance(prepared.Detail.Story);
            RaiseAutoplayAdvanced(prepared.Detail, prepared.Playlist);
        }
        catch
        {
            // Never surface a delayed background connection alert when the app is reopened.
        }
        finally
        {
            if (_current?.TrackingSessionId == endedItem.TrackingSessionId &&
                !_audioPlaybackService.IsPlaying)
            {
                _audioPlaybackService.SetBackgroundPlaybackActive(false);
            }

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

        var currentPlaylist = current.PlaybackQueue.CurrentPlaylist;
        return currentPlaylist is not null &&
               string.Equals(currentPlaylist.Slug, current.PlaylistSlug, StringComparison.OrdinalIgnoreCase)
            ? currentPlaylist
            : null;
    }

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
