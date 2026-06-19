namespace Shink.Mobile.Services;

public sealed record AudioPlaybackMetadata(
    string Title,
    string? Artist = null,
    string? ArtworkUrl = null);

public interface IAudioPlaybackService
{
    bool IsPlaying { get; }

    TimeSpan CurrentPosition { get; }

    TimeSpan? Duration { get; }

    Task<TimeSpan?> GetDurationAsync(string audioUrl, CancellationToken cancellationToken = default);

    event EventHandler? PlaybackEnded;

    event EventHandler? PlaybackStateChanged;

    Task PlayAsync(string audioUrl, AudioPlaybackMetadata? metadata = null);

    void Pause();

    void Stop();
}

#if IOS
public sealed class AudioPlaybackService : IAudioPlaybackService
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ArtworkTimeout = TimeSpan.FromSeconds(4);
    private static readonly HttpClient ArtworkHttpClient = new();

    private AVFoundation.AVPlayer? _player;
    private Foundation.NSObject? _endedObserver;
    private readonly List<Foundation.NSObject> _remoteCommandTargets = [];
    private string? _currentAudioUrl;
    private AudioPlaybackMetadata? _metadata;
    private MediaPlayer.MPMediaItemArtwork? _artwork;
    private string? _artworkUrl;
    private CancellationTokenSource? _artworkLoadCts;

    public bool IsPlaying { get; private set; }

    public TimeSpan CurrentPosition => ToTimeSpan(_player?.CurrentTime ?? CoreMedia.CMTime.Zero) ?? TimeSpan.Zero;

    public TimeSpan? Duration => _player?.CurrentItem is null
        ? null
        : ToTimeSpan(_player.CurrentItem.Duration);

    public event EventHandler? PlaybackEnded;

    public event EventHandler? PlaybackStateChanged;

    public async Task<TimeSpan?> GetDurationAsync(string audioUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            return null;
        }

        if (string.Equals(_currentAudioUrl, audioUrl, StringComparison.Ordinal) && Duration is { } currentDuration)
        {
            return currentDuration;
        }

        AVFoundation.AVPlayerItem? playerItem = null;
        AVFoundation.AVPlayer? probePlayer = null;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var playerUrl = Foundation.NSUrl.FromString(audioUrl);
            if (playerUrl is null)
            {
                throw new InvalidOperationException("Die audio URL is ongeldig.");
            }

            playerItem = AVFoundation.AVPlayerItem.FromUrl(playerUrl);
            probePlayer = new AVFoundation.AVPlayer(playerItem);
        });

        try
        {
            await WaitUntilReadyToPlayAsync(playerItem, cancellationToken);
            return await MainThread.InvokeOnMainThreadAsync(() => ToTimeSpan(playerItem?.Duration ?? CoreMedia.CMTime.Zero));
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                probePlayer?.Pause();
                probePlayer?.Dispose();
                playerItem?.Dispose();
            });
        }
    }

    public async Task PlayAsync(string audioUrl, AudioPlaybackMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            throw new InvalidOperationException("Geen audio URL is beskikbaar nie.");
        }

        AVFoundation.AVPlayerItem? playerItem = null;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _metadata = metadata;
            ConfigureAudioSession();
            ConfigureRemoteCommands();

            if (!string.Equals(_currentAudioUrl, audioUrl, StringComparison.Ordinal))
            {
                var playerUrl = Foundation.NSUrl.FromString(audioUrl);
                if (playerUrl is null)
                {
                    throw new InvalidOperationException("Die audio URL is ongeldig.");
                }

                Stop();
                _currentAudioUrl = audioUrl;
                _metadata = metadata;
                playerItem = AVFoundation.AVPlayerItem.FromUrl(playerUrl);
                _player = new AVFoundation.AVPlayer(playerItem);
                _endedObserver = Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
                    AVFoundation.AVPlayerItem.DidPlayToEndTimeNotification,
                    _ => OnPlaybackEnded(),
                    playerItem);
            }
            else
            {
                playerItem = _player?.CurrentItem;
            }
        });

        var artworkLoadTask = LoadArtworkForMetadataAsync(metadata);
        await WaitUntilReadyToPlayAsync(playerItem);
        await artworkLoadTask;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _player?.Play();
            IsPlaying = true;
            UpdateNowPlayingInfo();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Pause()
    {
        _player?.Pause();
        IsPlaying = false;
        UpdateNowPlayingInfo();
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _player?.Pause();
        _player = null;
        _currentAudioUrl = null;
        _metadata = null;
        _artwork = null;
        _artworkUrl = null;
        _artworkLoadCts?.Cancel();
        _artworkLoadCts?.Dispose();
        _artworkLoadCts = null;
        IsPlaying = false;
        ClearNowPlayingInfo();
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);

        if (_endedObserver is not null)
        {
            Foundation.NSNotificationCenter.DefaultCenter.RemoveObserver(_endedObserver);
            _endedObserver = null;
        }
    }

    private void OnPlaybackEnded()
    {
        IsPlaying = false;
        UpdateNowPlayingInfo();
        MainThread.BeginInvokeOnMainThread(() => PlaybackEnded?.Invoke(this, EventArgs.Empty));
    }

    private static void ConfigureAudioSession()
    {
        var session = AVFoundation.AVAudioSession.SharedInstance();
        session.SetCategory(AVFoundation.AVAudioSessionCategory.Playback);
        session.SetActive(true);
        UIKit.UIApplication.SharedApplication.BeginReceivingRemoteControlEvents();
    }

    private void ConfigureRemoteCommands()
    {
        if (_remoteCommandTargets.Count > 0)
        {
            return;
        }

        var commandCenter = MediaPlayer.MPRemoteCommandCenter.Shared;
        commandCenter.PlayCommand.Enabled = true;
        commandCenter.PauseCommand.Enabled = true;
        commandCenter.TogglePlayPauseCommand.Enabled = true;
        commandCenter.StopCommand.Enabled = false;
        commandCenter.NextTrackCommand.Enabled = false;
        commandCenter.PreviousTrackCommand.Enabled = false;

        _remoteCommandTargets.Add(commandCenter.PlayCommand.AddTarget(_ =>
        {
            MainThread.BeginInvokeOnMainThread(PlayFromRemoteCommand);
            return MediaPlayer.MPRemoteCommandHandlerStatus.Success;
        }));
        _remoteCommandTargets.Add(commandCenter.PauseCommand.AddTarget(_ =>
        {
            MainThread.BeginInvokeOnMainThread(Pause);
            return MediaPlayer.MPRemoteCommandHandlerStatus.Success;
        }));
        _remoteCommandTargets.Add(commandCenter.TogglePlayPauseCommand.AddTarget(_ =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (IsPlaying)
                {
                    Pause();
                }
                else
                {
                    PlayFromRemoteCommand();
                }
            });
            return MediaPlayer.MPRemoteCommandHandlerStatus.Success;
        }));
    }

    private void PlayFromRemoteCommand()
    {
        if (_player is null)
        {
            return;
        }

        ConfigureAudioSession();
        _player.Play();
        IsPlaying = true;
        UpdateNowPlayingInfo();
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateNowPlayingInfo()
    {
        if (_player is null)
        {
            ClearNowPlayingInfo();
            return;
        }

        var metadata = _metadata;
        var info = new MediaPlayer.MPNowPlayingInfo
        {
            Title = string.IsNullOrWhiteSpace(metadata?.Title) ? "Schink Stories" : metadata.Title,
            Artist = string.IsNullOrWhiteSpace(metadata?.Artist) ? "Schink Stories" : metadata.Artist,
            ElapsedPlaybackTime = CurrentPosition.TotalSeconds,
            PlaybackRate = IsPlaying ? 1 : 0
        };

        if (Duration is { TotalSeconds: > 0 } duration)
        {
            info.PlaybackDuration = duration.TotalSeconds;
        }

        if (_artwork is not null)
        {
            info.Artwork = _artwork;
        }

        MediaPlayer.MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = info;
    }

    private async Task LoadArtworkForMetadataAsync(AudioPlaybackMetadata? metadata)
    {
        var artworkUrl = metadata?.ArtworkUrl;
        if (string.IsNullOrWhiteSpace(artworkUrl))
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _artwork = null;
                _artworkUrl = null;
            });
            return;
        }

        if (string.Equals(_artworkUrl, artworkUrl, StringComparison.Ordinal) && _artwork is not null)
        {
            return;
        }

        _artworkLoadCts?.Cancel();
        _artworkLoadCts?.Dispose();
        _artworkLoadCts = new CancellationTokenSource(ArtworkTimeout);
        var cancellationToken = _artworkLoadCts.Token;

        try
        {
            var imageBytes = await ArtworkHttpClient.GetByteArrayAsync(artworkUrl, cancellationToken);
            if (imageBytes.Length == 0 || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                using var imageData = Foundation.NSData.FromArray(imageBytes);
                var image = UIKit.UIImage.LoadFromData(imageData);
                if (image is null || image.Size.Width <= 0 || image.Size.Height <= 0)
                {
                    return;
                }

                _artwork = new MediaPlayer.MPMediaItemArtwork(image.Size, _ => image);
                _artworkUrl = artworkUrl;
                UpdateNowPlayingInfo();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _artwork = null;
                _artworkUrl = null;
            });
        }
    }

    private static void ClearNowPlayingInfo()
    {
        MediaPlayer.MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = null!;
    }

    private static async Task WaitUntilReadyToPlayAsync(
        AVFoundation.AVPlayerItem? playerItem,
        CancellationToken cancellationToken = default)
    {
        if (playerItem is null)
        {
            throw new InvalidOperationException("Kon nie die audio laai nie.");
        }

        var deadline = DateTimeOffset.UtcNow.Add(ReadyTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await MainThread.InvokeOnMainThreadAsync(() => playerItem.Status);
            if (status == AVFoundation.AVPlayerItemStatus.ReadyToPlay)
            {
                return;
            }

            if (status == AVFoundation.AVPlayerItemStatus.Failed)
            {
                var errorMessage = await MainThread.InvokeOnMainThreadAsync(() =>
                    playerItem.Error?.LocalizedDescription);
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(errorMessage)
                        ? "Kon nie die audio stroom oopmaak nie."
                        : $"Kon nie die audio stroom oopmaak nie: {errorMessage}");
            }

            await Task.Delay(150, cancellationToken);
        }

        throw new TimeoutException("Die audio het nie betyds begin laai nie.");
    }

    private static TimeSpan? ToTimeSpan(CoreMedia.CMTime time)
    {
        var seconds = time.Seconds;
        return seconds > 0 && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }
}
#elif ANDROID
public sealed class AudioPlaybackService : IAudioPlaybackService
{
    private Android.Media.MediaPlayer? _player;
    private string? _currentAudioUrl;

    public bool IsPlaying { get; private set; }

    public TimeSpan CurrentPosition => TimeSpan.FromMilliseconds(_player?.CurrentPosition ?? 0);

    public TimeSpan? Duration
    {
        get
        {
            var durationMilliseconds = _player?.Duration ?? 0;
            return durationMilliseconds > 0
                ? TimeSpan.FromMilliseconds(durationMilliseconds)
                : null;
        }
    }

    public event EventHandler? PlaybackEnded;

    public event EventHandler? PlaybackStateChanged;

    public async Task<TimeSpan?> GetDurationAsync(string audioUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            return null;
        }

        if (string.Equals(_currentAudioUrl, audioUrl, StringComparison.Ordinal) && Duration is { } currentDuration)
        {
            return currentDuration;
        }

        var player = new Android.Media.MediaPlayer();
        try
        {
            var audioAttributesBuilder = new Android.Media.AudioAttributes.Builder();
            audioAttributesBuilder.SetUsage(Android.Media.AudioUsageKind.Media);
            audioAttributesBuilder.SetContentType(Android.Media.AudioContentType.Speech);
            var audioAttributes = audioAttributesBuilder.Build();
            if (audioAttributes is not null)
            {
                player.SetAudioAttributes(audioAttributes);
            }

            player.SetDataSource(audioUrl);

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.Prepared += (_, _) => ready.TrySetResult();
            player.Error += (_, args) =>
            {
                args.Handled = true;
                ready.TrySetResult();
            };
            using var registration = cancellationToken.Register(() => ready.TrySetCanceled(cancellationToken));
            player.PrepareAsync();
            await ready.Task;

            var durationMilliseconds = player.Duration;
            return durationMilliseconds > 0
                ? TimeSpan.FromMilliseconds(durationMilliseconds)
                : null;
        }
        finally
        {
            player.Release();
            player.Dispose();
        }
    }

    public async Task PlayAsync(string audioUrl, AudioPlaybackMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            return;
        }

        if (string.Equals(_currentAudioUrl, audioUrl, StringComparison.Ordinal) && _player is not null)
        {
            _player.Start();
            IsPlaying = true;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Stop();
        _currentAudioUrl = audioUrl;
        var player = new Android.Media.MediaPlayer();
        _player = player;
        var audioAttributesBuilder = new Android.Media.AudioAttributes.Builder();
        audioAttributesBuilder.SetUsage(Android.Media.AudioUsageKind.Media);
        audioAttributesBuilder.SetContentType(Android.Media.AudioContentType.Speech);
        var audioAttributes = audioAttributesBuilder.Build();
        if (audioAttributes is not null)
        {
            player.SetAudioAttributes(audioAttributes);
        }

        player.SetDataSource(audioUrl);

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Prepared += (_, _) => ready.TrySetResult();
        player.Error += (_, args) =>
        {
            args.Handled = true;
            ready.TrySetException(new InvalidOperationException("Kon nie die audio stroom oopmaak nie."));
        };
        player.Completion += (_, _) =>
        {
            IsPlaying = false;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            MainThread.BeginInvokeOnMainThread(() => PlaybackEnded?.Invoke(this, EventArgs.Empty));
        };
        player.PrepareAsync();
        await ready.Task;
        player.Start();
        IsPlaying = true;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        _player?.Pause();
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _player?.Stop();
        _player?.Release();
        _player?.Dispose();
        _player = null;
        _currentAudioUrl = null;
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
#else
public sealed class AudioPlaybackService : IAudioPlaybackService
{
    public bool IsPlaying => false;

    public TimeSpan CurrentPosition => TimeSpan.Zero;

    public TimeSpan? Duration => null;

    public event EventHandler? PlaybackEnded;

    public event EventHandler? PlaybackStateChanged;

    public Task<TimeSpan?> GetDurationAsync(string audioUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<TimeSpan?>(null);

    public Task PlayAsync(string audioUrl, AudioPlaybackMetadata? metadata = null) => Task.CompletedTask;

    public void Pause()
    {
    }

    public void Stop()
    {
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }
}
#endif
