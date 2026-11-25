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
        return NotificationManagerCompat.From(Platform.AppContext)?.AreNotificationsEnabled() == true;
    }

    public static bool IsChannelEnabled()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return true;
        var manager = (NotificationManager?)Platform.AppContext.GetSystemService(global::Android.Content.Context.NotificationService);
        return manager?.GetNotificationChannel(ChannelId) is { Importance: not NotificationImportance.None };
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
        manager?.CreateNotificationChannel(new NotificationChannel(ChannelId, "Schink-kennisgewings", NotificationImportance.High)
        {
            Description = "Nuwe stories, karakters, blogs en hulpbronne."
        });
    }

    public static async Task<bool> OfferBannerSettingsAsync(Page page, Func<bool> isCurrentAccount)
    {
        if (!await HasPermissionAsync(requestPermission: false) || !isCurrentAccount()) return false;
        var open = await page.DisplayAlertAsync("Opspringkennisgewings",
            "Wil jy kennisgewings boaan jou skerm sien? Maak jou selfoon se instellings oop en skakel die opspringopsie vir Schink-kennisgewings aan as dit af is.",
            "Maak instellings oop", "Slaan oor");
        if (open && isCurrentAccount() && MobilePushInstallation.Enabled) OpenNotificationSettings();
        return true;
    }

    public static void OpenNotificationSettings()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var context = Platform.AppContext;
            EnsureChannel();
            var appEnabled = NotificationManagerCompat.From(context)?.AreNotificationsEnabled() == true;
            using var intent = new global::Android.Content.Intent(appEnabled
                ? global::Android.Provider.Settings.ActionChannelNotificationSettings
                : global::Android.Provider.Settings.ActionAppNotificationSettings);
            intent.PutExtra(global::Android.Provider.Settings.ExtraAppPackage, context.PackageName);
            if (appEnabled) intent.PutExtra(global::Android.Provider.Settings.ExtraChannelId, ChannelId);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            try
            {
                context.StartActivity(intent);
                return;
            }
            catch (global::Android.Content.ActivityNotFoundException) { /* Use app settings on devices without this screen. */ }
        }
        AppInfo.Current.ShowSettingsUI();
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
