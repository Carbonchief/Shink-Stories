using Shink.Mobile.Pages;
using Shink.Mobile.Services;
using Microsoft.Maui.ApplicationModel;

namespace Shink.Mobile;

public partial class AppShell : Shell
{
    private readonly IServiceProvider _services;
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private bool? _isSignedInRendered;
    private bool _isInitializing;
    private bool _isRendering;
    private bool _hasCheckedSession;

    public AppShell(IServiceProvider services, MobileApiClient apiClient, SessionState sessionState)
    {
        InitializeComponent();
        _services = services;
        _apiClient = apiClient;
        _sessionState = sessionState;
        Shell.SetTabBarForegroundColor(this, Color.FromArgb("#146D69"));
        Shell.SetTabBarUnselectedColor(this, Color.FromArgb("#7C817C"));
        Shell.SetTabBarTitleColor(this, Color.FromArgb("#146D69"));
        Shell.SetTabBarBackgroundColor(this, Color.FromArgb("#FFF7E8"));

        Items.Clear();
        Routing.RegisterRoute(nameof(StoryDetailPage), typeof(StoryDetailPage));

        _sessionState.Changed += _ => MainThread.BeginInvokeOnMainThread(RenderShellFromSessionState);
        _isSignedInRendered = null;
        BuildSignedOutShell();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_hasCheckedSession)
        {
            return;
        }

        _hasCheckedSession = true;
        _ = RefreshSessionAndRenderShell();
    }

    private async Task RefreshSessionAndRenderShell()
    {
        if (_isInitializing)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _apiClient.GetSessionAsync(timeout.Token);
        }
        catch
        {
            // If the session endpoint is unavailable, keep the user on the login screen.
        }
        finally
        {
            _isInitializing = false;
        }

        RenderShellFromSessionState();
    }

    private void RenderShellFromSessionState()
    {
        if (_isRendering)
        {
            return;
        }

        if (_isSignedInRendered == _sessionState.Current.IsSignedIn)
        {
            return;
        }

        _isRendering = true;
        try
        {
            if (_sessionState.Current.IsSignedIn)
            {
                BuildSignedInShell();
                _isSignedInRendered = true;
            }
            else
            {
                BuildSignedOutShell();
                _isSignedInRendered = false;
            }
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void BuildSignedOutShell()
    {
        if (_isSignedInRendered == false)
        {
            return;
        }

        Items.Clear();
        Items.Add(new ShellContent
        {
            Title = "Teken in",
            ContentTemplate = new DataTemplate(() => _services.GetRequiredService<AccountPage>())
        });
        _isSignedInRendered = false;
    }

    private void BuildSignedInShell()
    {
        if (_isSignedInRendered == true)
        {
            return;
        }

        Items.Clear();
        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Luister", "tab_luister.png", () => _services.GetRequiredService<LuisterPage>()));
        tabs.Items.Add(CreateTab("Rekening", "tab_rekening.png", () => _services.GetRequiredService<AccountPage>()));
        Items.Add(tabs);
        _isSignedInRendered = true;
    }

    private static Tab CreateTab(string title, string icon, Func<Page> pageFactory)
    {
        var tab = new Tab { Title = title, Icon = icon };
        tab.Items.Add(new ShellContent
        {
            Title = title,
            Icon = icon,
            ContentTemplate = new DataTemplate(pageFactory)
        });
        return tab;
    }
}
