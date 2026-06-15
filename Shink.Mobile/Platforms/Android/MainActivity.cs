using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Shink.Mobile;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    Icon = "@mipmap/schink_appicon",
    RoundIcon = "@mipmap/schink_appicon_round",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
