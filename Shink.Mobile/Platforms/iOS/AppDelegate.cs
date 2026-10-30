using Foundation;
using Microsoft.Maui.Authentication;
using Shink.Mobile.Services;
using UIKit;

namespace Shink.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    private readonly Platforms.iOS.MobilePushNotificationDelegate _pushDelegate = new();
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        UserNotifications.UNUserNotificationCenter.Current.Delegate = _pushDelegate;
        return base.FinishedLaunching(application, launchOptions);
    }

    [Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]
    public void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken) =>
        MobilePushPlatform.ReceiveToken(Convert.ToHexStringLower(deviceToken.ToArray()));

    [Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
    public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error) =>
        MobilePushPlatform.RegistrationFailed();

    [Export("application:supportedInterfaceOrientationsForWindow:")]
    public UIInterfaceOrientationMask GetSupportedInterfaceOrientations(
        UIApplication application,
        UIWindow? forWindow) =>
        OrientationService.CurrentIosOrientationMask;

    public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options) =>
        WebAuthenticator.Default.OpenUrl(app, url, options) ||
        base.OpenUrl(app, url, options);

    public override bool ContinueUserActivity(
        UIApplication application,
        NSUserActivity userActivity,
        UIApplicationRestorationHandler completionHandler) =>
        WebAuthenticator.Default.ContinueUserActivity(application, userActivity, completionHandler) ||
        base.ContinueUserActivity(application, userActivity, completionHandler);
}
