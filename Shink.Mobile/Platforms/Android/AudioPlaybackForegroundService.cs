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

        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
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
