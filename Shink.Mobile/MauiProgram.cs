using Microsoft.Extensions.Logging;
using PostHog.Config;
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
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("fa-regular-400.ttf", "FontAwesomeRegular");
                fonts.AddFont("fa-solid-900.ttf", "FontAwesomeSolid");
                fonts.AddFont("Poppins-Regular.ttf", "Poppins");
                fonts.AddFont("Poppins-SemiBold.ttf", "PoppinsSemiBold");
                fonts.AddFont("Poppins-Bold.ttf", "PoppinsBold");
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if IOS
                handlers.AddHandler<CastRoutePickerView, Shink.Mobile.Platforms.iOS.CastRoutePickerViewHandler>();
                handlers.AddHandler<AppleSignInButton, Shink.Mobile.Platforms.iOS.AppleSignInButtonHandler>();
#endif
            });
        ConfigureEntryChrome();
        ConfigureCollectionViewStability();

        var mobileAppSettings = new MobileAppSettings();
        mobileAppSettings.BaseUrl = ResolveMobileApiBaseUrl(mobileAppSettings.BaseUrl);
        var analyticsSettings = MobileAnalyticsSettings.FromEnvironment();
        builder.Services.AddPostHog(postHog =>
        {
            postHog.PostConfigure(options =>
            {
                options.ProjectToken = analyticsSettings.ProjectApiKey;
                options.HostUrl = Uri.TryCreate(analyticsSettings.HostUrl, UriKind.Absolute, out var hostUrl)
                    ? hostUrl
                    : new Uri(MobileAnalyticsSettings.DefaultHostUrl);
                options.Disabled = !analyticsSettings.IsConfigured;
                options.IsServer = false;
                options.FlushAt = 10;
                options.FlushInterval = TimeSpan.FromSeconds(15);
                options.SuperProperties["app"] = "schink_stories_mobile";
                options.SuperProperties["platform"] = DeviceInfo.Platform.ToString();
            });
        });
        builder.Services.AddSingleton(mobileAppSettings);
        builder.Services.AddSingleton(analyticsSettings);
        builder.Services.AddSingleton<SessionState>();
        builder.Services.AddSingleton<MobileAnalyticsService>();
        builder.Services.AddSingleton<MobileCrashReporter>();
        builder.Services.AddSingleton<PlaylistPlaybackState>();
        builder.Services.AddSingleton<ContinueListeningState>();
        builder.Services.AddSingleton<PlayerTransitionBackdropState>();
        builder.Services.AddSingleton<MobileAppLifecycleService>();
        builder.Services.AddSingleton<MobileApiClient>();
        builder.Services.AddSingleton<IMobileStoreBillingService, MobileStoreBillingService>();
        builder.Services.AddSingleton<IOfflineStoryDownloadService, OfflineStoryDownloadService>();
        builder.Services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
        builder.Services.AddSingleton<StoryPlaybackSession>();
        builder.Services.AddSingleton<IOrientationService, OrientationService>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<GratisPage>();
        builder.Services.AddTransient<LuisterPage>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<DownloadedPage>();
        builder.Services.AddTransient<KarakterPareGamePage>();
        builder.Services.AddTransient<KarakterPareConfigPage>();
        builder.Services.AddTransient<KarakterRaaiConfigPage>();
        builder.Services.AddTransient<KarakterRaaiGamePage>();
        builder.Services.AddTransient<KennisgewingsPage>();
        // Shell keeps the hidden Karakters destination alive so opening it never has to
        // construct another large gallery page on the user's tap.
        builder.Services.AddSingleton<KaraktersPage>();
        builder.Services.AddTransient<AboutPage>();
        builder.Services.AddTransient<AccountPage>();
        builder.Services.AddTransient<PlansPage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<PlaylistStoriesPage>();
        builder.Services.AddTransient<PlaylistDetailPage>();
        builder.Services.AddTransient<StoryDetailPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        app.Services.GetRequiredService<MobileCrashReporter>().Start();
        return app;
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

    private static void ConfigureCollectionViewStability()
    {
#if IOS
        // Incident 5028E205-2F20-45B9-B759-5ADCB4B69A9B crashed while
        // CollectionView2 was creating an off-screen prefetched cell. These feeds
        // contain rich, nested templates, so build cells only when UIKit needs to
        // display them instead of letting UICollectionView prefetch them while idle.
        Microsoft.Maui.Handlers.ViewHandler.ViewMapper.AppendToMapping(
            "SchinkDisableCollectionViewPrefetch",
            (handler, view) =>
            {
                if (view is CollectionView &&
                    handler.PlatformView is UIKit.UICollectionView collectionView)
                {
                    collectionView.PrefetchingEnabled = false;
                }
            });
#elif ANDROID
        // Luister nests fixed-height horizontal carousels inside its vertical feed.
        // Keep a small native cache and prepare the first cards while the parent row
        // is still approaching the viewport, so mounting a new carousel does not
        // interrupt an in-progress fling.
        Microsoft.Maui.Handlers.ViewHandler.ViewMapper.AppendToMapping(
            "SchinkLuisterCollectionViewPerformance",
            (handler, view) =>
            {
                if (view is not CollectionView collectionView ||
                    handler.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView recyclerView ||
                    collectionView.AutomationId is not ("luister-feed" or "luister-carousel" or "characters-grid"))
                {
                    return;
                }

                recyclerView.SetItemAnimator(null);
                recyclerView.SetItemViewCacheSize(collectionView.AutomationId switch
                {
                    "luister-carousel" => 2,
                    "characters-grid" => 12,
                    _ => 8
                });
                if (collectionView.AutomationId == "luister-carousel")
                {
                    recyclerView.HasFixedSize = true;
                    recyclerView.NestedScrollingEnabled = false;
                    if (recyclerView.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager layoutManager)
                    {
                        // Keep only the row shell ahead of the vertical viewport.
                        // Horizontal artwork is bound when its native cell is
                        // attached, then fades in from the disk cache.
                        layoutManager.ItemPrefetchEnabled = false;
                        layoutManager.InitialPrefetchItemCount = 0;
                    }
                }
                else if (collectionView.AutomationId == "characters-grid")
                {
                    recyclerView.HasFixedSize = true;
                    if (recyclerView.GetLayoutManager() is AndroidX.RecyclerView.Widget.GridLayoutManager layoutManager)
                    {
                        layoutManager.ItemPrefetchEnabled = true;
                        layoutManager.InitialPrefetchItemCount = 9;
                    }
                }
                else if (collectionView.AutomationId == "luister-feed")
                {
                    recyclerView.HasFixedSize = true;
                    if (recyclerView.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager layoutManager)
                    {
                        layoutManager.ItemPrefetchEnabled = true;
                        layoutManager.InitialPrefetchItemCount = 3;
                    }
                }
            });
#endif
    }
}
