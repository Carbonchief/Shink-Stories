using Shink.Mobile.Services;

namespace Shink.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly MobileAppLifecycleService _lifecycleService;

    public App(AppShell shell, MobileAppLifecycleService lifecycleService)
    {
        InitializeComponent();
        _shell = shell;
        _lifecycleService = lifecycleService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell);
        window.Stopped += (_, _) => _lifecycleService.OnStopped();
        window.Resumed += (_, _) => _lifecycleService.OnResumed();
        window.Destroying += (_, _) => _lifecycleService.OnDestroying();
        return window;
    }
}
