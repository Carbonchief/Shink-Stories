using Shink.Mobile.Pages;
using Shink.Mobile.Services;
using Microsoft.Maui.ApplicationModel;

namespace Shink.Mobile;

public partial class AppShell : Shell
{
    private const string OnboardingCompletedPreferenceKey = "mobile_onboarding_completed_v1";
    private readonly IServiceProvider _services;
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly MobileAnalyticsService _analytics;
    private readonly MobilePushService _push;
    private string? _renderedShellState;
    private bool _isInitializing;
    private bool _isRendering;
    private bool _hasCheckedSession;

    public AppShell(
        IServiceProvider services,
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics,
        MobilePushService push)
    {
        InitializeComponent();
        _services = services;
        _apiClient = apiClient;
        _sessionState = sessionState;
        _analytics = analytics;
        _push = push;

        Items.Clear();
        Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));
        Routing.RegisterRoute(nameof(PlansPage), typeof(PlansPage));
        Routing.RegisterRoute(nameof(ProfilePage), typeof(ProfilePage));
        Routing.RegisterRoute(nameof(AvatarSelectionPage), typeof(AvatarSelectionPage));
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
        Routing.RegisterRoute(nameof(PlaylistStoriesPage), typeof(PlaylistStoriesPage));
        Routing.RegisterRoute(nameof(PlaylistDetailPage), typeof(PlaylistDetailPage));
        Routing.RegisterRoute(nameof(StoryDetailPage), typeof(StoryDetailPage));
        Routing.RegisterRoute(nameof(DownloadedPage), typeof(DownloadedPage));
        Routing.RegisterRoute(nameof(SearchPage), typeof(SearchPage));
        Routing.RegisterRoute(nameof(KarakterPareGamePage), typeof(KarakterPareGamePage));
        Routing.RegisterRoute(nameof(KarakterPareConfigPage), typeof(KarakterPareConfigPage));
        Routing.RegisterRoute(nameof(KarakterRaaiConfigPage), typeof(KarakterRaaiConfigPage));
        Routing.RegisterRoute(nameof(KarakterRaaiGamePage), typeof(KarakterRaaiGamePage));
        Routing.RegisterRoute(nameof(KennisgewingsPage), typeof(KennisgewingsPage));

        _sessionState.Changed += _ => MainThread.BeginInvokeOnMainThread(() => RenderShellFromSessionState());
        Navigated += OnShellNavigated;
        RenderShellFromSessionState();
        _ = _sessionState.HydrateSensitiveCacheAsync();
        _ = _apiClient.MaintainImageCacheAsync();
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
            _analytics.TrackRecoverableFailure(ex, "mobile_session_startup_refresh");
            // If the session endpoint is unavailable, keep the current cached shell.
        }
        finally
        {
            _isInitializing = false;
        }

        RenderShellFromSessionState();
        _push.OnShellReady();
    }

    private void RenderShellFromSessionState(OnboardingPage.Destination destination = OnboardingPage.Destination.Account)
    {
        if (_isRendering)
        {
            return;
        }

        var isSignedIn = _sessionState.Current.IsSignedIn;
        var hasCompletedOnboarding = Preferences.Default.Get(OnboardingCompletedPreferenceKey, false);
        var shellState = isSignedIn
            ? _sessionState.Current.RequiresAvatarSelection ? "avatar_setup" : "signed_in"
            : hasCompletedOnboarding ? "signed_out" : "onboarding";
        if (_renderedShellState == shellState)
        {
            return;
        }

        _isRendering = true;
        try
        {
            if (isSignedIn)
            {
                // Existing accounts should not see first-run onboarding after
                // a later sign-out, including when upgrading to this version.
                if (!hasCompletedOnboarding)
                    Preferences.Default.Set(OnboardingCompletedPreferenceKey, true);
                if (_sessionState.Current.RequiresAvatarSelection)
                    BuildAvatarSetupShell();
                else
                    BuildSignedInShell();
            }
            else if (shellState == "onboarding")
            {
                BuildOnboardingShell();
            }
            else
            {
                BuildSignedOutShell(destination);
            }
            _renderedShellState = shellState;
            _analytics.TrackEvent("mobile_shell_rendered", new Dictionary<string, object>
            {
                ["shell_state"] = shellState
            });
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void BuildOnboardingShell()
    {
        Items.Clear();
        Items.Add(new ShellContent
        {
            Title = "Welkom",
            Route = "Onboarding",
            ContentTemplate = new DataTemplate(() =>
            {
                var page = _services.GetRequiredService<OnboardingPage>();
                page.Completed += OnOnboardingCompleted;
                return page;
            })
        });
    }

    private async void OnOnboardingCompleted(object? sender, EventArgs args)
    {
        if (sender is not OnboardingPage page)
            return;

        page.Completed -= OnOnboardingCompleted;

        Preferences.Default.Set(OnboardingCompletedPreferenceKey, true);
        RenderShellFromSessionState(page.CompletionDestination);

        if (page.CompletionDestination == OnboardingPage.Destination.Plans && _renderedShellState == "signed_out")
        {
            try
            {
                await GoToAsync(nameof(PlansPage), animate: false);
            }
            catch (Exception ex)
            {
                _analytics.TrackRecoverableFailure(ex, "mobile_onboarding_plans_navigation");
                await DisplayAlertAsync("Intekeninge", "Ons kon nie nou die planne oopmaak nie.", "Reg so");
            }
        }
    }

    private void BuildSignedOutShell(OnboardingPage.Destination destination)
    {
        Items.Clear();
        Items.Add(new ShellContent
        {
            Title = "Teken in",
            ContentTemplate = new DataTemplate(() =>
            {
                var page = _services.GetRequiredService<AccountPage>();
                if (destination == OnboardingPage.Destination.SignIn)
                    page.ShowSignIn();
                else if (destination == OnboardingPage.Destination.FreeSignup)
                    page.ShowFreeSignup();
                return page;
            })
        });
    }

    private void BuildAvatarSetupShell()
    {
        Items.Clear();
        Items.Add(new ShellContent
        {
            Title = "Profielprentjie",
            Route = "AvatarSetup",
            ContentTemplate = new DataTemplate(() => _services.GetRequiredService<AvatarSelectionPage>())
        });
    }

    private void BuildSignedInShell()
    {
        Items.Clear();
        Items.Add(new ShellContent
        {
            Title = "Luister",
            Route = "Luister",
            ContentTemplate = new DataTemplate(() => _services.GetRequiredService<LuisterPage>())
        });
        var karaktersContent = new ShellContent
        {
            Title = "Karakters",
            Route = "Karakters",
            ContentTemplate = new DataTemplate(() => _services.GetRequiredService<KaraktersPage>())
        };
        Shell.SetFlyoutItemIsVisible(karaktersContent, false);
        Items.Add(karaktersContent);
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs args)
    {
        var location = args.Current?.Location.OriginalString ?? string.Empty;
        _analytics.TrackScreenView(
            MobileAnalyticsSchema.ScreenName(CurrentPage?.GetType().Name, location),
            new Dictionary<string, object>
            {
                ["route"] = MobileAnalyticsSchema.CleanRoute(location),
                ["navigation_source"] = args.Source.ToString()
            });
    }

}
