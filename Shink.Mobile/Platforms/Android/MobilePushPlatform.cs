using Android.App;
using Android.Gms.Common;
using Android.OS;
using AndroidX.Core.App;
using Firebase.Messaging;

namespace Shink.Mobile.Services;

public static partial class MobilePushPlatform
{
    public const string ChannelId = "schink_updates";
    public static partial bool IsSupported() => BuildEnabled
        && GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(Platform.AppContext) == ConnectionResult.Success
        && Firebase.FirebaseApp.InitializeApp(Platform.AppContext) is not null;

    public static partial async Task<bool> HasPermissionAsync(bool requestPermission)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            var status = await Permissions.CheckStatusAsync<PushPermission>();
            if (status != PermissionStatus.Granted && requestPermission)
                status = await Permissions.RequestAsync<PushPermission>();
            if (status != PermissionStatus.Granted) return false;
        }
        EnsureChannel();
        var manager = (NotificationManager?)Platform.AppContext.GetSystemService(global::Android.Content.Context.NotificationService);
        return NotificationManagerCompat.From(Platform.AppContext)?.AreNotificationsEnabled() == true
            && (Build.VERSION.SdkInt < BuildVersionCodes.O || manager?.GetNotificationChannel(ChannelId)?.Importance != NotificationImportance.None);
    }

    public static partial async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messaging = FirebaseMessaging.Instance ?? throw new InvalidOperationException("Kennisgewings is nie beskikbaar nie.");
        var task = messaging.GetToken() ?? throw new InvalidOperationException("Toestelregistrasie het misluk.");
        task.AddOnCompleteListener(new TokenListener(source));
        var token = await source.Task.WaitAsync(cancellationToken);
        ReceiveToken(token);
        return token;
    }

    public static void EnsureChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var manager = (NotificationManager?)Platform.AppContext.GetSystemService(global::Android.Content.Context.NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(ChannelId, "Schink-kennisgewings", NotificationImportance.Default)
        {
            Description = "Nuwe stories, karakters, blogs en hulpbronne."
        });
    }

    public static partial void ClearDeliveredNotifications() => NotificationManagerCompat.From(Platform.AppContext)?.CancelAll();

    public sealed class PushPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            [(global::Android.Manifest.Permission.PostNotifications, true)];
    }

    private sealed class TokenListener(TaskCompletionSource<string> source) : Java.Lang.Object, global::Android.Gms.Tasks.IOnCompleteListener
    {
        public void OnComplete(global::Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful && task.Result?.ToString() is { Length: > 0 } token) source.TrySetResult(token);
            else source.TrySetException(new InvalidOperationException("Toestelregistrasie het misluk."));
        }
    }
}
