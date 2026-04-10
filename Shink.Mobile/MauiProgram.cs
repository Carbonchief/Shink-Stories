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

        builder.Services.AddSingleton<MobileAppSettings>();
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
}
