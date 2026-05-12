using Microsoft.Extensions.Logging;
using Shink.Mobile.Pages;
using Shink.Mobile.Services;

namespace Shink.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        var mobileAppSettings = new MobileAppSettings();
        mobileAppSettings.BaseUrl = ResolveMobileApiBaseUrl(mobileAppSettings.BaseUrl);
        builder.Services.AddSingleton(mobileAppSettings);
        builder.Services.AddSingleton<SessionState>();
        builder.Services.AddSingleton<MobileApiClient>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<GratisPage>();
        builder.Services.AddTransient<LuisterPage>();
        builder.Services.AddTransient<AboutPage>();
        builder.Services.AddTransient<AccountPage>();
        builder.Services.AddTransient<StoryDetailPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static string ResolveMobileApiBaseUrl(string configuredBaseUrl)
    {
        var normalizedConfigured = MobileAppSettings.NormalizeBaseUrl(configuredBaseUrl);
        if (MobileAppSettings.IsValidMobileBaseUrl(normalizedConfigured))
        {
            return normalizedConfigured;
        }

        var overrideUrl = ResolveMobileApiBaseUrlFromWebProject();
        if (!string.IsNullOrWhiteSpace(overrideUrl))
        {
            return overrideUrl;
        }

        return MobileAppSettings.DefaultBaseUrl;
    }

    private static string? ResolveMobileApiBaseUrlFromWebProject()
    {
        var webProjectUrl = Environment.GetEnvironmentVariable("MOBILE_WEB_API_BASE_URL");
        if (TryNormalizeWebProjectUrl(webProjectUrl, out var normalizedUrl) &&
            MobileAppSettings.IsValidMobileBaseUrl(normalizedUrl))
        {
            return normalizedUrl;
        }

        return null;
    }

    private static bool TryNormalizeWebProjectUrl(string? url, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsedUrl))
        {
            return false;
        }

        normalizedUrl = parsedUrl.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

}
