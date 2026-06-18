using Microsoft.Extensions.Logging;
using Shink.Mobile.Pages;
using Shink.Mobile.Services;
using Shink.Mobile.Views;

namespace Shink.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureMauiHandlers(handlers =>
            {
#if IOS
                handlers.AddHandler<CastRoutePickerView, Shink.Mobile.Platforms.iOS.CastRoutePickerViewHandler>();
#endif
            });
        ConfigureEntryChrome();

        var mobileAppSettings = new MobileAppSettings();
        mobileAppSettings.BaseUrl = ResolveMobileApiBaseUrl(mobileAppSettings.BaseUrl);
        builder.Services.AddSingleton(mobileAppSettings);
        builder.Services.AddSingleton<SessionState>();
        builder.Services.AddSingleton<PlaylistPlaybackState>();
        builder.Services.AddSingleton<PlayerTransitionBackdropState>();
        builder.Services.AddSingleton<MobileApiClient>();
        builder.Services.AddSingleton<IOfflineStoryDownloadService, OfflineStoryDownloadService>();
        builder.Services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
        builder.Services.AddSingleton<IOrientationService, OrientationService>();
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

    private static void ConfigureEntryChrome()
    {
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("SchinkPlainEntryChrome", (handler, _) =>
        {
#if IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
            handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
#elif ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetPadding(0, 0, 0, 0);
#endif
        });
    }
}
