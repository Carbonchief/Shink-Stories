using Shink.Mobile.Pages;
using Shink.Mobile.Services;
using Microsoft.Maui.ApplicationModel;

namespace Shink.Mobile;

public partial class AppShell : Shell
{
    private readonly IServiceProvider _services;
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly MobileAnalyticsService _analytics;
    private bool? _isSignedInRendered;
    private bool _isInitializing;
    private bool _isRendering;
    private bool _hasCheckedSession;

    public AppShell(
        IServiceProvider services,
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics)
    {
        InitializeComponent();
        _services = services;
        _apiClient = apiClient;
        _sessionState = sessionState;
        _analytics = analytics;

        Items.Clear();
        Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));
        Routing.RegisterRoute(nameof(ProfilePage), typeof(ProfilePage));
        Routing.RegisterRoute(nameof(StoryDetailPage), typeof(StoryDetailPage));
        Routing.RegisterRoute(nameof(DownloadedPage), typeof(DownloadedPage));
        Routing.RegisterRoute(nameof(KaraktersPage), typeof(KaraktersPage));

        _sessionState.Changed += _ => MainThread.BeginInvokeOnMainThread(RenderShellFromSessionState);
        Navigated += OnShellNavigated;
        _isSignedInRendered = null;
        RenderShellFromSessionState();
        _ = _sessionState.HydrateSensitiveCacheAsync();
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
        catch (Exception ex)
        {
            _analytics.TrackException(ex, "mobile_session_startup_refresh");
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
                _analytics.TrackEvent("mobile_shell_rendered", new Dictionary<string, object>
                {
                    ["shell_state"] = "signed_in"
                });
            }
            else
            {
                BuildSignedOutShell();
                _isSignedInRendered = false;
                _analytics.TrackEvent("mobile_shell_rendered", new Dictionary<string, object>
                {
                    ["shell_state"] = "signed_out"
                });
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

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs args)
    {
        var location = args.Current?.Location.OriginalString ?? string.Empty;
        _analytics.TrackScreenView(
            ResolveScreenName(location),
            new Dictionary<string, object>
            {
                ["route"] = location,
                ["navigation_source"] = args.Source.ToString()
            });
    }

    private static string ResolveScreenName(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return "unknown";
        }

        var route = location.Trim('/').Split('?', '#')[0];
        return string.IsNullOrWhiteSpace(route)
            ? "home"
            : route.Replace("/", "_", StringComparison.Ordinal).ToLowerInvariant();
    }
}
