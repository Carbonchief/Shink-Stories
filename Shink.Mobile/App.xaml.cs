using Shink.Mobile.Services;

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
        // Let iOS dismiss the native launch screen as soon as the Shell window
        // is ready. A second runtime splash page can remain visible when page
        // lifecycle callbacks are missed during native startup.
        var window = new Window(_shell);
        window.Stopped += (_, _) => _lifecycleService.OnStopped();
        window.Resumed += (_, _) => _lifecycleService.OnResumed();
        window.Destroying += (_, _) => _lifecycleService.OnDestroying();
        _analytics.TrackAppOpened();
        _analytics.IdentifyCurrentSession();
        return window;
    }
}
