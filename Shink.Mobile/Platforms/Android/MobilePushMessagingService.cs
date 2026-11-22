using Android.App;
using Android.Content;
using Android.Graphics;
using AndroidX.Core.App;
using Firebase.Messaging;
using Shink.Mobile.Services;

namespace Shink.Mobile.Platforms.Android;

[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class MobilePushMessagingService : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        MobilePushPlatform.ReceiveToken(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        var data = message.Data;
        var push = MobilePushOpen.FromValues(Value(data, "notification_id"), Value(data, "registration_id"),
            Value(data, "notification_type"), Value(data, "href"));
        if (push is null || !MobilePushPlatform.BuildEnabled || !MobilePushInstallation.CanDisplay(push.RegistrationId.ToString("D"))) return;
        MobilePushPlatform.EnsureChannel();
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetAction("com.schink.stories.mobile.PUSH." + push.NotificationId.ToString("D"));
        intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        intent.PutExtra("notification_id", push.NotificationId.ToString("D"));
        intent.PutExtra("registration_id", push.RegistrationId.ToString("D"));
        intent.PutExtra("notification_type", push.Type);
        intent.PutExtra("href", push.Href ?? string.Empty);
        var pending = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        using var builder = new NotificationCompat.Builder(this, MobilePushPlatform.ChannelId);
        builder.SetSmallIcon(Resource.Drawable.schink_notification);
        builder.SetContentTitle(Value(data, "title") ?? "Schink Stories");
        builder.SetContentText(Value(data, "body") ?? string.Empty);
        using var style = new NotificationCompat.BigTextStyle();
        style.BigText(Value(data, "body") ?? string.Empty);
        builder.SetStyle(style);
        builder.SetContentIntent(pending);
        builder.SetAutoCancel(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetVisibility(NotificationCompat.VisibilityPrivate);
        // FCM invokes this callback on a worker thread. Finish the bounded download before
        // returning so Android does not suspend the process before we post the notification.
        var bytes = MobilePushArtworkDownload.TryDownloadAsync(push.Type, Value(data, "image_url"), CancellationToken.None)
            .GetAwaiter().GetResult();
        using var artwork = DecodeLargeIcon(bytes);
        if (artwork is not null) builder.SetLargeIcon(artwork);
        if (!MobilePushInstallation.CanDisplay(push.RegistrationId.ToString("D"))) return;
        var notification = builder.Build();
        // A stable tag prevents a retry from creating a second tray entry.
        if (notification is not null)
        {
            try { NotificationManagerCompat.From(this)?.Notify(push.NotificationId.ToString("D"), 0, notification); }
            catch (Java.Lang.SecurityException) { /* Permission can be revoked between receipt and display. */ }
        }
    }

    private static Bitmap? DecodeLargeIcon(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) return null;

        var longestEdge = Math.Max(bounds.OutWidth, bounds.OutHeight);
        var sampleSize = 1;
        while (longestEdge / sampleSize > 256) sampleSize *= 2;
        using var options = new BitmapFactory.Options { InSampleSize = sampleSize };
        return BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
    }

    public static void HandleNotificationIntent(Intent? intent)
    {
        var push = MobilePushOpen.FromValues(intent?.GetStringExtra("notification_id"), intent?.GetStringExtra("registration_id"),
            intent?.GetStringExtra("notification_type"), intent?.GetStringExtra("href"));
        if (push is null) return;
        intent?.RemoveExtra("notification_id");
        MobilePushPlatform.OpenNotification(push);
    }

    private static string? Value(IDictionary<string, string> data, string key) => data.TryGetValue(key, out var value) ? value : null;
}
