using Foundation;
using Microsoft.Maui.Authentication;
using Shink.Mobile.Services;
using UIKit;

namespace Shink.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

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
