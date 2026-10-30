using UIKit;
using UserNotifications;

namespace Shink.Mobile.Services;

public static partial class MobilePushPlatform
{
    public static partial bool IsSupported() => BuildEnabled;

    public static partial async Task<bool> HasPermissionAsync(bool requestPermission)
    {
        var center = UNUserNotificationCenter.Current;
        var settings = await center.GetNotificationSettingsAsync();
        if (settings.AuthorizationStatus is UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional or UNAuthorizationStatus.Ephemeral)
            return true;
        if (!requestPermission || settings.AuthorizationStatus != UNAuthorizationStatus.NotDetermined) return false;
        var result = await center.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound);
        return result.Item1;
    }

    public static partial async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokenSource.Task.IsFaulted)
            _tokenSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.RegisterForRemoteNotifications();
        return await _tokenSource.Task.WaitAsync(cancellationToken);
    }

    public static partial void ClearDeliveredNotifications() => MainThread.BeginInvokeOnMainThread(() =>
    {
        UNUserNotificationCenter.Current.RemoveAllDeliveredNotifications();
        UNUserNotificationCenter.Current.RemoveAllPendingNotificationRequests();
    });
}
