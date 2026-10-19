// These doubles deliberately throw from native event accessors after disposal,
// matching the binding behavior in the reported Android stack. No Android runtime
// is loaded here; the Android-targeted build separately checks the real API binding.
namespace Java.Lang
{
    public sealed class IllegalStateException : Exception;
}

namespace Android.App
{
    public static class Application
    {
        public static object Context { get; } = new();
    }
}

namespace Android.OS
{
    public enum WakeLockFlags { Partial }
}

namespace Android.Media
{
    public enum AudioUsageKind { Media }
    public enum AudioContentType { Speech }
    public enum MediaPlayerSeekMode { Closest }

    public sealed class AudioAttributes
    {
        public sealed class Builder
        {
            public void SetUsage(AudioUsageKind usage) { }
            public void SetContentType(AudioContentType type) { }
            public AudioAttributes Build() => new();
        }
    }

    public sealed class PlaybackParams
    {
        public void SetSpeed(float speed) { }
    }

    public sealed class MediaPlayer : IDisposable
    {
        private EventHandler? _seekComplete;
        public bool IsDisposed { get; private set; }
        public bool IsReleased { get; private set; }
        public int ListenerCount => _seekComplete?.GetInvocationList().Length ?? 0;
        public int RemoveAttempts { get; private set; }
        public int AccessesAfterRelease { get; private set; }
        public List<string> Calls { get; } = [];
        public Exception? SeekException { get; set; }
        public Exception? AddException { get; set; }
        public Exception? RemoveException { get; set; }
        public Action? OnSeek { get; set; }
        public int LastSeekPosition { get; private set; }
        public int DurationValue { get; set; } = 120_000;
        public int CurrentPosition { get { CheckAlive(); return LastSeekPosition; } }
        public int Duration { get { CheckAlive(); return DurationValue; } }
        public PlaybackParams? PlaybackParams { get; set; }
        public event EventHandler? Prepared;
        public event EventHandler? Completion;
        public event EventHandler<ErrorEventArgs>? Error;
        public event EventHandler SeekComplete
        {
            add
            {
                CheckAlive();
                Calls.Add("add");
                if (AddException is not null) throw AddException;
                _seekComplete += value;
            }
            remove
            {
                RemoveAttempts++;
                CheckAlive();
                Calls.Add("remove");
                _seekComplete -= value;
                if (RemoveException is not null) throw RemoveException;
            }
        }

        public void CompleteSeek() => _seekComplete?.Invoke(this, EventArgs.Empty);
        public void CompletePlayback() => Completion?.Invoke(this, EventArgs.Empty);
        public void Fail() => Error?.Invoke(this, new ErrorEventArgs());
        public EventHandler? CaptureSeekCallback() => _seekComplete;
        public void SetDataSource(string url) { CheckAlive(); }
        public void SetAudioAttributes(AudioAttributes attributes) { }
        public void SetWakeMode(object context, Android.OS.WakeLockFlags flags) { }
        public void PrepareAsync() => Prepared?.Invoke(this, EventArgs.Empty);
        public void Start() { CheckAlive(); }
        public void Pause() { CheckAlive(); }
        public void Stop() { CheckAlive(); Calls.Add("stop"); }
        public void Release() { CheckAlive(); Calls.Add("release"); IsReleased = true; }
        public void Dispose() { Calls.Add("dispose"); IsDisposed = true; }
        public void SeekTo(int position, MediaPlayerSeekMode mode) => SeekTo(position);
        public void SeekTo(int position)
        {
            CheckAlive();
            Calls.Add("seek");
            LastSeekPosition = position;
            OnSeek?.Invoke();
            if (SeekException is not null) throw SeekException;
        }

        private void CheckAlive()
        {
            if (IsDisposed || IsReleased)
            {
                AccessesAfterRelease++;
                throw new ObjectDisposedException(nameof(MediaPlayer));
            }
        }

        public sealed class ErrorEventArgs : EventArgs
        {
            public bool Handled { get; set; }
        }
    }
}

namespace Shink.Mobile.Services
{
    public sealed class MobileAnalyticsService
    {
        public List<string> Events { get; } = [];
        public void TrackEvent(string name, Dictionary<string, object> properties) => Events.Add(name);
        public void TrackException(Exception exception, string context) { }
    }

    internal static class MainThread
    {
        public static void BeginInvokeOnMainThread(Action action) => action();
    }
}

namespace Shink.Mobile.Platforms.Android
{
    internal static class AudioPlaybackForegroundService
    {
        public static void RequestStart(object context) { }
        public static void RequestStop() { }
    }
}
