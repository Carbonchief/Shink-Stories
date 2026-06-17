using Foundation;
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
}
