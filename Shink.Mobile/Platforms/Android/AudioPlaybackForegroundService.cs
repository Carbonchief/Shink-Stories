#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Android.Content.PM;

namespace Shink.Mobile.Platforms.Android;

[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class AudioPlaybackForegroundService : Service
{
    private const int NotificationId = 2401;
    private const string NotificationChannelId = "schink_audio_playback";

    private static readonly object LifecycleGate = new();
    private static AudioPlaybackForegroundService? _runningService;
    private static int _pendingStarts;
    private static bool _playbackRequested;

    public static void RequestStart(Context context)
    {
        lock (LifecycleGate)
        {
            _playbackRequested = true;
            _pendingStarts++;
            try
            {
                var intent = new Intent(context, typeof(AudioPlaybackForegroundService));
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O) context.StartForegroundService(intent);
                else context.StartService(intent);
            }
            catch
            {
                _pendingStarts--;
                throw;
            }
        }
    }

    public static void RequestStop()
    {
        lock (LifecycleGate)
        {
            _playbackRequested = false;
            StopWhenStarted();
        }
    }

    // A foreground-service start must be acknowledged with StartForeground before
    // it can be stopped, even when a short local clip or a track switch finishes first.
    private static void StopWhenStarted()
    {
        if (_playbackRequested || _pendingStarts != 0 || _runningService is null) return;
        var service = _runningService;
        _runningService = null;
        service.StopSelf();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        CreateNotificationChannel();
        var notification = BuildNotification();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        lock (LifecycleGate)
        {
            _runningService = this;
            _pendingStarts = Math.Max(0, _pendingStarts - 1);
            StopWhenStarted();
        }
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        lock (LifecycleGate)
        {
            if (ReferenceEquals(_runningService, this)) _runningService = null;
        }
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var notificationManager = GetSystemService(NotificationService) as NotificationManager;
        notificationManager?.CreateNotificationChannel(new NotificationChannel(
            NotificationChannelId,
            "Schink Stories klank",
            NotificationImportance.Low));
    }

    private Notification BuildNotification()
    {
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, NotificationChannelId)
            : new Notification.Builder(this);

        return builder
            .SetSmallIcon(Resource.Mipmap.schink_appicon)
            .SetContentTitle("Schink Stories")
            .SetContentText("Speel stories in die agtergrond")
            .SetOngoing(true)
            .SetCategory(Notification.CategoryTransport)
            .SetShowWhen(false)
            .Build();
    }
}
#endif
