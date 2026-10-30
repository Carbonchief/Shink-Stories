using Shink.Mobile.Services;

namespace Shink.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly MobileAppLifecycleService _lifecycleService;
    private readonly MobileAnalyticsService _analytics;
    private readonly MobileStoreRecoveryService _storeRecovery;
    private readonly MobilePushService _push;

    public App(AppShell shell, MobileAppLifecycleService lifecycleService, MobileAnalyticsService analytics, MobileStoreRecoveryService storeRecovery, MobilePushService push)
    {
        InitializeComponent();
        _shell = shell;
        _lifecycleService = lifecycleService;
        _analytics = analytics;
        _storeRecovery = storeRecovery;
        _push = push;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Let iOS dismiss the native launch screen as soon as the Shell window
        // is ready. A second runtime splash page can remain visible when page
        // lifecycle callbacks are missed during native startup.
        var window = new Window(_shell);
        _storeRecovery.Start();
        _push.Start();
        window.Activated += (_, _) => _push.Refresh();
        _shell.Navigated += (_, _) => _push.OnShellNavigated();
        window.Stopped += (_, _) => _lifecycleService.OnStopped();
        window.Resumed += (_, _) => _lifecycleService.OnResumed();
        window.Destroying += (_, _) => _lifecycleService.OnDestroying();
        _analytics.TrackAppOpened();
        _analytics.IdentifyCurrentSession();
        return window;
    }
}
