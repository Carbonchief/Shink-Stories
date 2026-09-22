using Foundation;
using Microsoft.Maui.Authentication;
using UIKit;

namespace Shink.Mobile;

// iOS 27 requires scene lifecycle adoption for apps linked against its SDK.
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        base.WillConnect(scene, session, connectionOptions);
        if (connectionOptions.UrlContexts is { } urls)
        {
            OpenUrl(scene, urls);
        }
        if (connectionOptions.UserActivities is { } activities)
        {
            foreach (var activity in activities)
            {
                ContinueUserActivity(scene, activity);
            }
        }
    }

    public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
    {
        var handled = base.OpenUrl(scene, urlContexts);
        foreach (var context in urlContexts)
        {
            handled = WebAuthenticator.Default.OpenUrl(
                UIApplication.SharedApplication, context.Url, new NSDictionary()) || handled;
        }
        return handled;
    }

    public override bool ContinueUserActivity(UIScene scene, NSUserActivity userActivity) =>
        WebAuthenticator.Default.ContinueUserActivity(
            UIApplication.SharedApplication, userActivity, _ => { }) ||
        base.ContinueUserActivity(scene, userActivity);
}
