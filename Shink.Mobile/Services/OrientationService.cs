#if ANDROID
using Android.Content.PM;
#elif IOS || MACCATALYST
using Foundation;
using UIKit;
#endif

namespace Shink.Mobile.Services;

public interface IOrientationService
{
    void RequestLandscape();

    void RequestPortrait();
}

public sealed class OrientationService : IOrientationService
{
#if IOS || MACCATALYST
    internal static UIInterfaceOrientationMask CurrentIosOrientationMask { get; private set; } =
        UIInterfaceOrientationMask.Portrait;
#endif

    public void RequestLandscape()
    {
#if IOS || MACCATALYST
        CurrentIosOrientationMask = UIInterfaceOrientationMask.Landscape;
        RequestIosOrientation(UIInterfaceOrientation.LandscapeRight);
#elif ANDROID
        RequestAndroidOrientation(ScreenOrientation.SensorLandscape);
#endif
    }

    public void RequestPortrait()
    {
#if IOS || MACCATALYST
        CurrentIosOrientationMask = UIInterfaceOrientationMask.Portrait;
        RequestIosOrientation(UIInterfaceOrientation.Portrait);
#elif ANDROID
        RequestAndroidOrientation(ScreenOrientation.Portrait);
#endif
    }

#if IOS || MACCATALYST
    private static void RequestIosOrientation(UIInterfaceOrientation orientation)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var rootViewController = UIApplication.SharedApplication.ConnectedScenes
                .OfType<UIWindowScene>()
                .SelectMany(scene => scene.Windows)
                .FirstOrDefault(window => window.IsKeyWindow)
                ?.RootViewController;

            if (OperatingSystem.IsIOSVersionAtLeast(16))
            {
                rootViewController?.SetNeedsUpdateOfSupportedInterfaceOrientations();
                var windowScene = rootViewController?.View?.Window?.WindowScene
                    ?? UIApplication.SharedApplication.ConnectedScenes
                        .OfType<UIWindowScene>()
                        .FirstOrDefault(scene => scene.ActivationState == UISceneActivationState.ForegroundActive);

                windowScene?.RequestGeometryUpdate(
                    new UIWindowSceneGeometryPreferencesIOS(CurrentIosOrientationMask),
                    _ => { });
            }

            UIDevice.CurrentDevice.SetValueForKey(new NSNumber((int)orientation), new NSString("orientation"));
            UIViewController.AttemptRotationToDeviceOrientation();
        });
    }
#elif ANDROID
    private static void RequestAndroidOrientation(ScreenOrientation orientation)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity is not null)
            {
                activity.RequestedOrientation = orientation;
            }
        });
    }
#endif
}
