using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace Shink.Mobile;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[]
    {
        Intent.CategoryDefault,
        Intent.CategoryBrowsable
    },
    DataScheme = "schinkstories",
    DataHost = "auth",
    DataPath = "/google")]
public sealed class GoogleAuthCallbackActivity : WebAuthenticatorCallbackActivity
{
}

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[]
    {
        Intent.CategoryDefault,
        Intent.CategoryBrowsable
    },
    AutoVerify = true,
    DataScheme = "https",
    DataHost = "www.schink.co.za",
    DataPath = "/mobile-auth/google/callback")]
public sealed class GoogleAuthAppLinkCallbackActivity : WebAuthenticatorCallbackActivity
{
}
