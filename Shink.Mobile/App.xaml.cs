using Shink.Mobile.Services;
using Shink.Mobile.Pages;

namespace Shink.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly MobileAppLifecycleService _lifecycleService;
    private readonly MobileAnalyticsService _analytics;

    public App(AppShell shell, MobileAppLifecycleService lifecycleService, MobileAnalyticsService analytics)
    {
        InitializeComponent();
        _shell = shell;
        _lifecycleService = lifecycleService;
        _analytics = analytics;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var splashPage = new FullScreenSplashPage();
        var window = new Window(splashPage);
        splashPage.Loaded += ShowShellAfterSplash;
        window.Stopped += (_, _) => _lifecycleService.OnStopped();
        window.Resumed += (_, _) => _lifecycleService.OnResumed();
        window.Destroying += (_, _) => _lifecycleService.OnDestroying();
        _analytics.TrackAppOpened();
        _analytics.IdentifyCurrentSession();
        return window;

        async void ShowShellAfterSplash(object? sender, EventArgs args)
        {
            splashPage.Loaded -= ShowShellAfterSplash;
            await Task.Delay(300);
            await splashPage.FadeToAsync(0, 150, Easing.CubicIn);

            if (ReferenceEquals(window.Page, splashPage))
            {
                window.Page = _shell;
            }
        }
    }
}
