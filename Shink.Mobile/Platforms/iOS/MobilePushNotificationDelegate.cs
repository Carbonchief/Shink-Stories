using Foundation;
using Shink.Mobile.Services;
using UserNotifications;

namespace Shink.Mobile.Platforms.iOS;

public sealed class MobilePushNotificationDelegate : UNUserNotificationCenterDelegate
{
    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
    {
        var push = Read(notification.Request.Content.UserInfo);
        completionHandler(push is not null && MobilePushInstallation.CanDisplay(push.RegistrationId.ToString("D"))
            ? UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound
            : UNNotificationPresentationOptions.None);
    }

    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
    {
        try
        {
            var push = Read(response.Notification.Request.Content.UserInfo);
            if (push is not null && response.IsDefaultAction)
                MobilePushPlatform.OpenNotification(push);
        }
        finally { completionHandler(); }
    }

    private static MobilePushOpen? Read(NSDictionary info) => MobilePushOpen.FromValues(
        info["notification_id"]?.ToString(), info["registration_id"]?.ToString(),
        info["notification_type"]?.ToString(), info["href"]?.ToString());
}
