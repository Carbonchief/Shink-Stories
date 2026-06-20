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

        Items.Clear();
        Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));
        Routing.RegisterRoute(nameof(ProfilePage), typeof(ProfilePage));
        Routing.RegisterRoute(nameof(StoryDetailPage), typeof(StoryDetailPage));
        Routing.RegisterRoute(nameof(DownloadedPage), typeof(DownloadedPage));

        _sessionState.Changed += _ => MainThread.BeginInvokeOnMainThread(RenderShellFromSessionState);
        _isSignedInRendered = null;
        RenderShellFromSessionState();
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
            // If the session endpoint is unavailable, keep the current cached shell.
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
        Items.Add(new ShellContent
        {
            Title = "Luister",
            Route = "Luister",
            ContentTemplate = new DataTemplate(() => _services.GetRequiredService<LuisterPage>())
        });
        _isSignedInRendered = true;
    }
}
