using System.Net;
using System.Globalization;
using System.Text;
using System.Threading;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class LuisterPage : ContentPage, IQueryAttributable
{
    private static readonly Color LuisterModalBackgroundColor = Color.FromArgb("#FFF7E8");
    private static readonly LinearGradientBrush LuisterBackgroundBrush = new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb("#408D93"), 0),
            new GradientStop(Color.FromArgb("#4F9DB3"), 0.22f),
            new GradientStop(Color.FromArgb("#D4CF69"), 0.56f),
            new GradientStop(Color.FromArgb("#EFEFEF"), 0.86f),
            new GradientStop(Color.FromArgb("#EFEFEF"), 1)
        }
    };
    private const double PageHorizontalPadding = 18;
    private const double LuisterGradientMinimumTravelDistance = 4200;
    // Keep the first feed item below the native-style app bar and the last item
    // above the persistent bottom navigation on every device size.
    private const double FloatingTopBarContentInset = 92;
    private const double BottomBarContentInset = 136;
    private const double BottomBarOverlayHeight = 152;
    private const double StoriesHeroHeight = 262;
    private const double OortjiesPeekWidth = 64;
    private const double OortjiesPeekHeight = 71;
    private const int MaxOortjiesPeeksPerWindow = 2;
    private static readonly TimeSpan OortjiesPeekWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OortjiesPeekVisibleDuration = TimeSpan.FromMilliseconds(3600);
    private static readonly TimeSpan OortjiesInitialDelayMin = TimeSpan.FromMilliseconds(22000);
    private static readonly TimeSpan OortjiesInitialDelayMax = TimeSpan.FromMilliseconds(58000);
    private static readonly TimeSpan OortjiesNextDelayMin = TimeSpan.FromMilliseconds(78000);
    private static readonly TimeSpan OortjiesNextDelayMax = TimeSpan.FromMilliseconds(178000);
    private static readonly TimeSpan NotificationBadgeRefreshInterval = TimeSpan.FromSeconds(45);
    private static bool IsAndroid => DeviceInfo.Current.Platform == DevicePlatform.Android;
    private static bool UsesCollectionViewFeed => IsAndroid;
    private readonly MobileApiClient _apiClient;
    private readonly IServiceProvider _services;
    private readonly MobileAnalyticsService _analytics;
    private readonly SessionState _sessionState;
    private readonly IOfflineStoryDownloadService _offlineDownloadService;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly ContinueListeningState _continueListeningState;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly NavigationGate _navigationGate = new();
    private readonly Grid _rootLayout;
    private readonly Grid _topBarOverlay;
    private readonly Grid _bottomBarOverlay;
    private readonly Image _oortjiesPeekMascot;
    private Grid? _menuOverlay;
    private VerticalStackLayout? _content;
    private readonly RefreshView _refreshView;
    private readonly ScrollView? _scrollView;
    private ScrollView? _activeScrollView;
    private readonly CollectionView? _feedView;
    private readonly Entry _searchEntry;
    private readonly Entry _loginEmailEntry;
    private readonly Entry _loginPasswordEntry;
    private readonly Label _loginStatusLabel;
    private readonly Dictionary<string, ImageSource> _imageSourceCache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MobileLuisterSection> _sections = Array.Empty<MobileLuisterSection>();
    private IReadOnlyList<OfflineStoryDownload> _downloadedStories = Array.Empty<OfflineStoryDownload>();
    private MobileNotificationPage? _notificationPage;
    private string? _loadErrorMessage;
    private bool _hasLoaded;
    private bool _isPageActive;
    private bool _isSearchVisible;
    private bool _isRefreshingNotifications;
    private readonly Border _floatingTopBarHost;
    private readonly ContentView _bottomBarHost;
    private IDispatcherTimer? _notificationRefreshTimer;
    private IDispatcherTimer? _oortjiesPeekTimer;
    private IDispatcherTimer? _oortjiesHideTimer;
    private CancellationTokenSource? _imageWarmupCancellation;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _searchDebounceCancellation;
    private CancellationTokenSource? _pageActivityCancellation;
    private readonly HashSet<string> _favoriteRequestsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DateTimeOffset> _recentOortjiesPeekTimes = new();
    private OortjiesPeekSide? _lastOortjiesPeekSide;
    private OortjiesPeekPlacement? _activeOortjiesPeekPlacement;
    private bool _isOortjiesPeekVisible;
    private bool _isPageEventsSubscribed;
    private bool _hasStartedKaraktersDestinationWarmup;
    private string? _pendingSurface;
    private bool _isApplyingPendingSurface;
    private double _lastResponsiveWidth = -1;
    private double _lastGradientScrollOffset;
    private double _lastGradientViewportHeight;
    private bool _responsiveRenderQueued;
    public LuisterPage(
        MobileApiClient apiClient,
        IServiceProvider services,
        MobileAnalyticsService analytics,
        SessionState sessionState,
        IOfflineStoryDownloadService offlineDownloadService,
        PlaylistPlaybackState playlistPlaybackState,
        ContinueListeningState continueListeningState,
        PlayerTransitionBackdropState transitionBackdropState)
    {
        _apiClient = apiClient;
        _services = services;
        _analytics = analytics;
        _sessionState = sessionState;
        _offlineDownloadService = offlineDownloadService;
        _playlistPlaybackState = playlistPlaybackState;
        _continueListeningState = continueListeningState;
        _transitionBackdropState = transitionBackdropState;
        Title = "Luister";
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);

        _searchEntry = new Entry
        {
            Placeholder = "Soek stories",
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            Keyboard = Keyboard.Text,
            TextColor = Color.FromArgb("#243238"),
            PlaceholderColor = Color.FromArgb("#7C817C")
        };
        _searchEntry.TextChanged += (_, _) => QueueSearchRender();

        _loginEmailEntry = new Entry
        {
            Placeholder = "E-pos",
            Keyboard = Keyboard.Email
        };
        _loginPasswordEntry = new Entry
        {
            Placeholder = "Wagwoord",
            IsPassword = true
        };
        _loginStatusLabel = new Label
        {
            TextColor = Color.FromArgb("#5F5F5F"),
            FontSize = 13
        };

        if (UsesCollectionViewFeed)
        {
            _feedView = new CollectionView
            {
                Background = Brush.Transparent,
                ItemsSource = Array.Empty<LuisterFeedItem>(),
                ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
                SelectionMode = SelectionMode.None,
                ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical)
                {
                    ItemSpacing = 14
                },
                ItemTemplate = new DataTemplate(BuildFeedItemView),
                Header = BuildStoriesPageHeader(),
                Footer = new BoxView
                {
                    HeightRequest = BottomBarContentInset,
                    Color = Colors.Transparent
                },
                // Keep the feed itself edge-to-edge on Android so a carousel's
                // negative side margin can reach the screen edge. Individual
                // feed items apply the normal page gutter below.
                Margin = Thickness.Zero,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                VerticalScrollBarVisibility = ScrollBarVisibility.Never
            };
            _feedView.Scrolled += OnFeedViewScrolled;
        }
        else
        {
            _content = new VerticalStackLayout
            {
                SafeAreaEdges = new SafeAreaEdges(
                    SafeAreaRegions.None,
                    SafeAreaRegions.Container,
                    SafeAreaRegions.None,
                    SafeAreaRegions.None),
                Padding = new Thickness(0, 0, 0, 28),
                Spacing = 16
            };
            _scrollView = new ScrollView
            {
                SafeAreaEdges = SafeAreaEdges.None,
                Background = Brush.Transparent,
                Content = _content
            };
            _scrollView.Scrolled += OnScrollViewScrolled;
        }

        _refreshView = new RefreshView
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Background = Brush.Transparent,
            Content = UsesCollectionViewFeed ? _feedView : _scrollView,
            Command = new Command(() => _ = TriggerRefreshAsync())
        };

        _topBarOverlay = new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(
                SafeAreaRegions.Container,
                SafeAreaRegions.Container,
                SafeAreaRegions.Container,
                SafeAreaRegions.None),
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(0, 0, 0, 16),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = false,
            ZIndex = 100
        };

        _floatingTopBarHost = new Border
        {
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Margin = Thickness.Zero,
            HeightRequest = 62,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = false,
            ZIndex = 101
        };
        _topBarOverlay.Children.Add(_floatingTopBarHost);

        _bottomBarOverlay = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            HeightRequest = BottomBarOverlayHeight,
            InputTransparent = false,
            ZIndex = 100
        };
        _bottomBarHost = new ContentView
        {
            Content = MobileBottomBar.Build(this, "listen", ToggleSearchAsync),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            HeightRequest = BottomBarOverlayHeight,
            ZIndex = 101
        };
        _bottomBarOverlay.Children.Add(_bottomBarHost);

        _oortjiesPeekMascot = new Image
        {
            Source = "oortjies_website.png",
            Aspect = Aspect.AspectFit,
            WidthRequest = OortjiesPeekWidth,
            HeightRequest = OortjiesPeekHeight,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Opacity = 0,
            IsVisible = false,
            ZIndex = 230,
            Shadow = BuildScrollContentShadow(new SolidColorBrush(Color.FromArgb("#303032")), new Point(0, 10), 16, 0.20f)
        };
        var oortjiesTap = new TapGestureRecognizer();
        oortjiesTap.Tapped += (_, _) => HideOortjiesPeekMascot(jump: true);
        _oortjiesPeekMascot.GestureRecognizers.Add(oortjiesTap);

        _rootLayout = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Background = LuisterBackgroundBrush,
            Children =
            {
                _refreshView,
                _topBarOverlay,
                _bottomBarOverlay,
                _oortjiesPeekMascot
            }
        };
        Content = _rootLayout;
        RenderFloatingTopBar();
        RenderLoadingState();
        Loaded += (_, _) => _ = StartPageActivityAsync();
        HandlerChanged += (_, _) =>
        {
            if (!_isPageActive || !_hasLoaded)
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isPageActive && _hasLoaded)
                {
                    RenderContent();
                }
            });
        };
        SizeChanged += (_, _) => HandleResponsiveSizeChanged();
        SizeChanged += (_, _) => UpdateLuisterGradientForScroll(_lastGradientScrollOffset);
        HandleResponsiveSizeChanged();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await StartPageActivityAsync();
        await ApplyPendingSurfaceAsync();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("surface", out var value))
        {
            return;
        }

        var surface = Uri.UnescapeDataString(value?.ToString() ?? string.Empty);
        if (surface is not ("search" or "notifications"))
        {
            return;
        }

        _pendingSurface = surface;
        _ = ApplyPendingSurfaceAsync();
    }

    private async Task ApplyPendingSurfaceAsync()
    {
        if (!_isPageActive || _isApplyingPendingSurface || string.IsNullOrWhiteSpace(_pendingSurface))
        {
            return;
        }

        var surface = _pendingSurface;
        _pendingSurface = null;
        _isApplyingPendingSurface = true;
        try
        {
            if (surface == "search")
            {
                if (!_isSearchVisible)
                {
                    await ToggleSearchAsync();
                }

                return;
            }

            await ShowNotificationsAsync();
        }
        finally
        {
            _isApplyingPendingSurface = false;
        }
    }

    private async Task StartPageActivityAsync()
    {
        if (_isPageActive)
        {
            return;
        }

        _isPageActive = true;
        _pageActivityCancellation?.Cancel();
        _pageActivityCancellation?.Dispose();
        _pageActivityCancellation = new CancellationTokenSource();
        SubscribePageEvents();
        StartKaraktersDestinationWarmup(_pageActivityCancellation.Token);
        if (!_hasLoaded)
        {
            await LoadAsync();
        }
        else
        {
            _ = RefreshVisibleStateAfterNavigationAsync(_pageActivityCancellation.Token);
        }

        StartNotificationRefreshTimer();
        StartOortjiesPeekMascot();
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        StopNotificationRefreshTimer();
        UnsubscribePageEvents();
        _loadCancellation?.Cancel();
        _imageWarmupCancellation?.Cancel();
        _searchDebounceCancellation?.Cancel();
        _pageActivityCancellation?.Cancel();
        HideMenu();
        StopOortjiesPeekMascot();
        base.OnDisappearing();
    }

    private void SubscribePageEvents()
    {
        if (_isPageEventsSubscribed)
        {
            return;
        }

        _offlineDownloadService.DownloadsChanged += OnDownloadsChanged;
        _apiClient.NewNotificationsAvailable += OnNewNotificationsAvailable;
        _continueListeningState.Changed += OnContinueListeningChanged;
        _sessionState.Changed += OnSessionStateChanged;
        _isPageEventsSubscribed = true;
    }

    private void UnsubscribePageEvents()
    {
        if (!_isPageEventsSubscribed)
        {
            return;
        }

        _offlineDownloadService.DownloadsChanged -= OnDownloadsChanged;
        _apiClient.NewNotificationsAvailable -= OnNewNotificationsAvailable;
        _continueListeningState.Changed -= OnContinueListeningChanged;
        _sessionState.Changed -= OnSessionStateChanged;
        _isPageEventsSubscribed = false;
    }

    private void OnDownloadsChanged(object? sender, EventArgs args)
    {
        if (!_isPageActive)
        {
            return;
        }

        _ = RefreshDownloadsInBackgroundAsync();
    }

    private void OnNewNotificationsAvailable(int count)
    {
        if (!_isPageActive)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => _ = RefreshNotificationsInBackgroundAsync());
    }

    private void OnContinueListeningChanged(ContinueListeningItem? item)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isPageActive && _hasLoaded)
            {
                RenderPlaylistContent();
            }
        });
    }

    private void OnSessionStateChanged(MobileSession session)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isPageActive)
            {
                return;
            }

            if (!session.IsSignedIn)
            {
                _notificationPage = null;
            }

            RenderContent();
            if (session.IsSignedIn)
            {
                _ = RefreshNotificationsInBackgroundAsync();
            }
        });
    }

    private async Task LoadAsync(bool forceRefresh = false)
    {
        if (_hasLoaded && !forceRefresh)
        {
            RenderContent();
            return;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        var downloadsTask = LoadPlayableDownloadsSafelyAsync(cancellationToken);
        var renderedCachedData = !forceRefresh && await TryRenderCachedLuisterAsync(downloadsTask, cancellationToken);

        if (!renderedCachedData)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!_isPageActive || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                RenderLoadingState();
            });
        }

        try
        {
            var sessionTask = _apiClient.GetSessionAsync(cancellationToken);
            var luisterTask = _apiClient.GetLuisterAsync(cancellationToken);
            await Task.WhenAll(sessionTask, luisterTask, downloadsTask);

            var response = await luisterTask;
            if (cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return;
            }

            if (response is null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (!_isPageActive || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    RenderNoticeState("Kon nie luister stories laai nie.");
                });
                return;
            }

            ApplyLuisterResponse(response, await downloadsTask);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!_isPageActive || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                RenderContent();
            });
            StartImageWarmup();
            _ = RefreshNotificationsInBackgroundAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return;
            }

            if (renderedCachedData)
            {
                return;
            }

            _sections = Array.Empty<MobileLuisterSection>();
            _downloadedStories = await LoadPlayableDownloadsSafelyAsync(cancellationToken);
            _loadErrorMessage = ex.Message;
            _hasLoaded = true;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!_isPageActive || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                RenderContent();
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => _refreshView.IsRefreshing = false);
        }
    }

    private async Task<bool> TryRenderCachedLuisterAsync(
        Task<IReadOnlyList<OfflineStoryDownload>> downloadsTask,
        CancellationToken cancellationToken)
    {
        try
        {
            var cachedResponse = await _apiClient.GetCachedLuisterAsync(cancellationToken);
            if (cachedResponse is null || cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return false;
            }

            ApplyLuisterResponse(cachedResponse, await downloadsTask);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!_isPageActive || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                RenderContent();
            });
            StartImageWarmup();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyLuisterResponse(
        MobileLuisterResponse response,
        IReadOnlyList<OfflineStoryDownload> downloadedStories)
    {
        var sections = response.Sections is { Count: > 0 }
            ? response.Sections
            : BuildLegacySections(response.Playlists);
        _sections = ApplyCurrentFavoriteState(sections);
        _downloadedStories = downloadedStories;
        _loadErrorMessage = null;
        _hasLoaded = true;
    }

    private async Task<IReadOnlyList<OfflineStoryDownload>> LoadPlayableDownloadsSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _offlineDownloadService.GetPlayableDownloadsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<OfflineStoryDownload>();
        }
    }

    private async Task RefreshDownloadsInBackgroundAsync()
    {
        var cancellationToken = _pageActivityCancellation?.Token ?? default;
        IReadOnlyList<OfflineStoryDownload> downloads;
        try
        {
            downloads = await _offlineDownloadService.GetPlayableDownloadsAsync(cancellationToken);
        }
        catch
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!_isPageActive || DownloadsMatch(_downloadedStories, downloads))
            {
                return;
            }

            _downloadedStories = downloads;
            if (UsesCollectionViewFeed)
            {
                RenderPlaylistContent();
            }
            else
            {
                RenderContent();
            }
        });
    }

    private async Task RefreshVisibleStateAfterNavigationAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Do not let cached file checks or session work run inside the Shell switch.
            await Task.Delay(120, cancellationToken);
            await Task.WhenAll(
                RefreshDownloadsInBackgroundAsync(),
                RefreshSessionInBackgroundAsync());
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool DownloadsMatch(
        IReadOnlyList<OfflineStoryDownload> current,
        IReadOnlyList<OfflineStoryDownload> next)
    {
        if (ReferenceEquals(current, next))
        {
            return true;
        }

        if (current.Count != next.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (current[index] != next[index])
            {
                return false;
            }
        }

        return true;
    }

    private async Task TriggerRefreshAsync()
    {
        try
        {
            await LoadAsync(forceRefresh: true);
        }
        catch
        {
            await MainThread.InvokeOnMainThreadAsync(() => _refreshView.IsRefreshing = false);
        }
    }

    private async Task RefreshSessionInBackgroundAsync()
    {
        var cancellationToken = _pageActivityCancellation?.Token ?? default;
        try
        {
            await _apiClient.GetSessionAsync(cancellationToken);
            MainThread.BeginInvokeOnMainThread(RenderFloatingTopBar);
            await RefreshNotificationsInBackgroundAsync();
        }
        catch
        {
            // Keep cached Luister content visible if session refresh is temporarily unavailable.
        }
    }

    private void RenderContent()
    {
        if (!_hasLoaded || !_isPageActive || Handler is null)
        {
            return;
        }

        try
        {
            RenderFloatingTopBar();
            RenderBottomBar();
            if (UsesCollectionViewFeed)
            {
                RenderPlaylistContent();
            }
            else
            {
                RebuildIOSFeedRoot();
            }
        }
        catch (ObjectDisposedException)
        {
            _isPageActive = false;
        }
    }

    private void RenderLoadingState()
    {
        if (UsesCollectionViewFeed)
        {
            ReplaceFeedItems(new[] { LuisterFeedItem.Loading() });
            return;
        }

        _content!.Children.Clear();
        _content.Children.Add(BuildStoriesPageHeader());
        _content.Children.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#0F766E") });
    }

    private void RenderNoticeState(string message)
    {
        if (UsesCollectionViewFeed)
        {
            ReplaceFeedItems(new[] { LuisterFeedItem.Notice(message) });
            return;
        }

        _content!.Children.Clear();
        _content.Children.Add(BuildStoriesPageHeader());
        _content.Children.Add(new Label { Text = message, Margin = new Thickness(PageHorizontalPadding, 0) });
    }

    private void RebuildIOSFeedRoot()
    {
        var content = new VerticalStackLayout
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Padding = new Thickness(0, 0, 0, BottomBarContentInset),
            Spacing = 16
        };
        content.Children.Add(BuildStoriesPageHeader());
        var feedContent = new VerticalStackLayout
        {
            Padding = new Thickness(PageHorizontalPadding, 0),
            Spacing = 16
        };
        foreach (var item in BuildFeedItems())
        {
            feedContent.Children.Add(BuildFeedItemContent(item));
        }
        content.Children.Add(feedContent);

        var scrollView = new ScrollView
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Background = Brush.Transparent,
            Content = content
        };
        scrollView.Scrolled += OnScrollViewScrolled;
        _activeScrollView = scrollView;
        _refreshView.Content = scrollView;
    }

    private void RenderFloatingTopBar()
    {
        _floatingTopBarHost.Content = BuildLuisterTopBar();
        _floatingTopBarHost.TranslationY = 0;
        _floatingTopBarHost.Opacity = 1;
        _floatingTopBarHost.InputTransparent = false;
        _topBarOverlay.InputTransparent = false;
    }

    private void OnFeedViewScrolled(object? sender, ItemsViewScrolledEventArgs args)
    {
        UpdateLuisterGradientForScroll(args.VerticalOffset);
    }

    private void OnScrollViewScrolled(object? sender, ScrolledEventArgs args) =>
        UpdateLuisterGradientForScroll(args.ScrollY);

    private void UpdateLuisterGradientForScroll(double scrollOffset)
    {
        var viewportHeight = Height > 0 ? Height : _rootLayout.Height;
        if (viewportHeight <= 0)
        {
            return;
        }

        var measuredContentHeight = _feedView?.Height ?? _activeScrollView?.Content?.Height ?? _scrollView?.Content?.Height ?? 0;
        var travelDistance = Math.Max(
            measuredContentHeight,
            Math.Max(viewportHeight * 4, LuisterGradientMinimumTravelDistance));

        if (Math.Abs(scrollOffset - _lastGradientScrollOffset) < 1 &&
            Math.Abs(viewportHeight - _lastGradientViewportHeight) < 1)
        {
            return;
        }

        _lastGradientScrollOffset = Math.Max(0, scrollOffset);
        _lastGradientViewportHeight = viewportHeight;
        LuisterBackgroundBrush.StartPoint = new Point(0, -_lastGradientScrollOffset / viewportHeight);
        LuisterBackgroundBrush.EndPoint = new Point(
            0,
            (travelDistance - _lastGradientScrollOffset) / viewportHeight);
    }

    private void RenderBottomBar()
    {
        _bottomBarHost.Content = MobileBottomBar.Build(
            this,
            _isSearchVisible || !string.IsNullOrWhiteSpace(_searchEntry.Text) ? "search" : "listen",
            ToggleSearchAsync);
    }

    private Task ToggleSearchAsync()
    {
        _isSearchVisible = !_isSearchVisible;
        _ = ResetScrollPositionAsync();
        RenderContent();
        if (_isSearchVisible)
        {
            MainThread.BeginInvokeOnMainThread(() => _searchEntry.Focus());
        }

        return Task.CompletedTask;
    }

    private void RenderPlaylistContent()
    {
        if (!_isPageActive)
        {
            return;
        }

        _searchDebounceCancellation?.Cancel();
        var nextItems = BuildFeedItems();
        if (UsesCollectionViewFeed)
        {
            ReplaceFeedItems(nextItems);
        }
        else
        {
            RebuildIOSFeedRoot();
        }
    }

    private List<LuisterFeedItem> BuildFeedItems()
    {
        var nextItems = new List<LuisterFeedItem>();
        if (_isSearchVisible || !string.IsNullOrWhiteSpace(_searchEntry.Text))
        {
            nextItems.Add(LuisterFeedItem.Search());
        }

        if (!_sessionState.Current.IsSignedIn)
        {
            nextItems.Add(LuisterFeedItem.Account());
        }

        if (_continueListeningState.Current is not null)
        {
            nextItems.Add(LuisterFeedItem.ContinueListening());
        }

        var downloadedStories = ResolveVisibleDownloadedStories();
        if (downloadedStories.Count > 0)
        {
            nextItems.Add(LuisterFeedItem.Downloaded(downloadedStories));
        }

        var filteredSections = FilterSections(_sections, _searchEntry.Text).ToArray();
        if (filteredSections.Length == 0)
        {
            if (downloadedStories.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(_loadErrorMessage))
                {
                    nextItems.Add(LuisterFeedItem.Notice("Jy is offline. Jou afgelaaide stories is beskikbaar."));
                }

                return nextItems;
            }

            nextItems.Add(LuisterFeedItem.Notice(string.IsNullOrWhiteSpace(_loadErrorMessage)
                ? "Geen stories pas by jou soektog nie."
                : _loadErrorMessage));
            return nextItems;
        }

        foreach (var section in filteredSections)
        {
            if (IsSpeellysteSection(section))
            {
                nextItems.Add(LuisterFeedItem.PlaylistShowcase(section));
                continue;
            }

            if (section.Playlist is not null)
            {
                nextItems.Add(LuisterFeedItem.PlaylistSection(section.Playlist));
            }
        }

        return nextItems;
    }

    private void HandleResponsiveSizeChanged()
    {
        var width = MobileResponsiveLayout.ResolveWidth(Width);
        if (IsAndroid)
        {
            var phoneChromeWidth = Math.Max(280, width - 36);
            _floatingTopBarHost.WidthRequest = phoneChromeWidth;
            _floatingTopBarHost.MaximumWidthRequest = phoneChromeWidth;
            _floatingTopBarHost.HorizontalOptions = LayoutOptions.Center;
        }
        else
        {
            MobileResponsiveLayout.ApplyCenteredContent(_floatingTopBarHost, width, 1040);
        }

        if (_lastResponsiveWidth < 0)
        {
            _lastResponsiveWidth = width;
            return;
        }

        if (Math.Abs(width - _lastResponsiveWidth) < 32 ||
            !_hasLoaded ||
            !_isPageActive ||
            _responsiveRenderQueued)
        {
            return;
        }

        _lastResponsiveWidth = width;
        _responsiveRenderQueued = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _responsiveRenderQueued = false;
            if (_isPageActive && _hasLoaded)
            {
                RenderContent();
            }
        });
    }

    private View BuildFeedItemView()
    {
        var container = new ContentView
        {
            Padding = new Thickness(PageHorizontalPadding, 0)
        };
        container.BindingContextChanged += (_, _) =>
        {
            container.Content = container.BindingContext is LuisterFeedItem item
                ? BuildFeedItemContent(item)
                : null;
        };
        return container;
    }

    private static View BuildStoriesPageHeader() => new VerticalStackLayout
    {
        Spacing = 0,
        Children =
        {
            // Keep the hero fully below the persistent top action row at first render.
            new BoxView { HeightRequest = FloatingTopBarContentInset, Color = Colors.Transparent },
            BuildStoriesHero()
        }
    };

    private static View BuildStoriesHero() => new Grid
    {
        HeightRequest = StoriesHeroHeight,
        BackgroundColor = Colors.Transparent,
        Children =
        {
            new Image
            {
                Source = "stories_hero_overlay.png",
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                InputTransparent = true
            }
        }
    };

    private View BuildFeedItemContent(LuisterFeedItem item) =>
        item.Kind switch
        {
            LuisterFeedItemKind.Loading => new ActivityIndicator
            {
                IsRunning = true,
                Color = Color.FromArgb("#0F766E"),
                Margin = new Thickness(0, 28)
            },
            LuisterFeedItemKind.Search => BuildSearchBox(),
            LuisterFeedItemKind.Account => BuildAccountPanel(),
            LuisterFeedItemKind.ContinueListening => BuildContinueListeningCard() ?? new BoxView { HeightRequest = 0 },
            LuisterFeedItemKind.Downloaded => BuildDownloadedSection(item.Downloads) ?? new BoxView { HeightRequest = 0 },
            LuisterFeedItemKind.Notice => BuildInlineNotice(item.Message ?? string.Empty),
            LuisterFeedItemKind.PlaylistShowcase when item.Section is not null => BuildPlaylistShowcase(item.Section),
            LuisterFeedItemKind.PlaylistShowcase => BuildPlaylistShowcase(item.Title, item.Playlists),
            LuisterFeedItemKind.PlaylistSection when item.Playlist is not null => BuildPlaylistSection(item.Playlist),
            _ => new BoxView { HeightRequest = 0 }
        };

    private View BuildPlaylistShowcase(MobileLuisterSection section) =>
        BuildPlaylistShowcase(section.Title, section.Playlists);

    private void ReplaceFeedItems(IReadOnlyList<LuisterFeedItem> nextItems)
    {
        _feedView!.ItemsSource = nextItems.ToArray();
    }

    private ImageSource BuildLuisterImageSource(string? url, string? fallbackFile = null)
    {
        var cacheKey = $"{url?.Trim() ?? string.Empty}|{fallbackFile ?? string.Empty}";
        if (_imageSourceCache.TryGetValue(cacheKey, out var source))
        {
            return source;
        }

        source = _apiClient.BuildCachedImageSource(url, fallbackFile);
        _imageSourceCache[cacheKey] = source;
        return source;
    }

    private static Shadow BuildScrollContentShadow(Brush brush, Point offset, float radius, float opacity) =>
        IsAndroid
            ? new Shadow
            {
                Brush = Brush.Transparent,
                Offset = Point.Zero,
                Radius = 0,
                Opacity = 0
            }
            : new Shadow
            {
                Brush = brush,
                Offset = offset,
                Radius = radius,
                Opacity = opacity
            };

    private static IShape? BuildArtworkShape(double cornerRadius) =>
        new RoundRectangle { CornerRadius = cornerRadius };

    private void QueueSearchRender()
    {
        if (!_hasLoaded)
        {
            return;
        }

        _searchDebounceCancellation?.Cancel();
        _searchDebounceCancellation?.Dispose();
        _searchDebounceCancellation = new CancellationTokenSource();
        var token = _searchDebounceCancellation.Token;
        _ = DebounceSearchRenderAsync(token);
    }

    private async Task DebounceSearchRenderAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(220, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _ = ResetScrollPositionAsync();
                RenderPlaylistContent();
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ResetScrollPositionAsync()
    {
        if (!_isPageActive)
        {
            return;
        }

        try
        {
            if (UsesCollectionViewFeed)
            {
                _feedView!.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
            }
            else
            {
                await (_activeScrollView ?? _scrollView)!.ScrollToAsync(0, 0, false);
            }

            await Task.CompletedTask;
        }
        catch
        {
        }
    }

    private View BuildLuisterTopBar()
    {
        return MobileTopBar.Build(
            this,
            _apiClient,
            _sessionState.Current,
            title: "Schink Stories",
            notificationAction: ShowNotificationsAsync,
            notificationCount: _notificationPage?.UnreadCount ?? 0,
            backgroundColor: MobileAndroidChromePalette.TopBarBackground,
            showProfile: false,
            brandLeadingInset: 4);
    }

    private View BuildSearchBox()
    {
        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E6DDCA"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = new Thickness(16, 4),
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 4),
                Radius = 12,
                Opacity = 0.05f
            },
            Content = _searchEntry
        };
    }

    private static Border BuildMenuCircleButton(Color lineColor, Color backgroundColor) =>
        new()
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                WidthRequest = 18,
                HeightRequest = 14,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
                Children =
                {
                    BuildMenuLine(lineColor),
                    BuildMenuLine(lineColor),
                    BuildMenuLine(lineColor)
                }
            }
        };

    private static BoxView BuildMenuLine(Color color) =>
        new()
        {
            Color = color,
            WidthRequest = 18,
            HeightRequest = 2,
            HorizontalOptions = LayoutOptions.Center
        };

    private static Border BuildHeaderCircleButton(string text, double fontSize, Color textColor, Color backgroundColor) =>
        new()
        {
            BackgroundColor = backgroundColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = text == "⌕" ? new Thickness(0, -2, 0, 0) : Thickness.Zero,
                InputTransparent = true
            }
        };

    private View BuildNotificationButton()
    {
        var unreadCount = _notificationPage?.UnreadCount ?? 0;
        var container = new Grid
        {
            WidthRequest = 50,
            HeightRequest = 50,
            VerticalOptions = LayoutOptions.Center
        };
        var notificationSurface = BuildHeaderCircleButton("🔔", 20, Color.FromArgb("#0B3534"), Color.FromArgb("#F4E9D1"));
        notificationSurface.InputTransparent = true;
        container.Children.Add(notificationSurface);

        if (unreadCount > 0)
        {
            container.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#E11D48"),
                Stroke = Colors.White,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 999 },
                WidthRequest = unreadCount > 9 ? 28 : 22,
                HeightRequest = 22,
                Padding = 0,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                InputTransparent = true,
                Content = new Label
                {
                    Text = FormatNotificationCount(unreadCount),
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            });
        }

        return container;
    }

    private static string FormatNotificationCount(int unreadCount) =>
        unreadCount > 99 ? "99+" : unreadCount.ToString(CultureInfo.InvariantCulture);

    private static Border BuildNotificationCloseButton() =>
        new()
        {
            BackgroundColor = Color.FromArgb("#F4E9D1"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            VerticalOptions = LayoutOptions.Center,
            Content = new GraphicsView
            {
                Drawable = new NotificationDownCaretDrawable(),
                WidthRequest = 22,
                HeightRequest = 22,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            }
        };

    private Border BuildProfileButton()
    {
        var session = _sessionState.Current;
        if (string.IsNullOrWhiteSpace(session.ProfileImageUrl))
        {
            return BuildHeaderCircleButton(BuildInitials(session), 15, Color.FromArgb("#0B3534"), Color.FromArgb("#FFD45A"));
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb("#F7EAD0"),
            Stroke = Colors.White,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            WidthRequest = 46,
            HeightRequest = 46,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center,
            Content = new Image
            {
                Source = BuildLuisterImageSource(session.ProfileImageUrl),
                Aspect = Aspect.AspectFill,
                WidthRequest = 46,
                HeightRequest = 46,
                InputTransparent = true
            }
        };
    }

    private void ShowMenu()
    {
        if (_menuOverlay is null)
        {
            _menuOverlay = MobileMenuSheet.BuildOverlay(
                "Menu",
                HandleMenuChoiceAsync,
                "Karakters",
                "Karakter-pare",
                "Wie is dié Karakter?",
                "Afgelaai",
                "Instellings",
                "Bestuur rekening");
            _menuOverlay.IsVisible = false;
            _menuOverlay.ZIndex = 300;
            _rootLayout.Children.Add(_menuOverlay);
        }

        _menuOverlay.IsVisible = true;
    }

    private void HideMenu()
    {
        if (_menuOverlay is not null)
        {
            _menuOverlay.IsVisible = false;
        }
    }

    private Task HandleMenuChoiceAsync(string? choice)
    {
        HideMenu();
        if (choice is null)
        {
            return Task.CompletedTask;
        }

        return _navigationGate.RunAsync(async () =>
        {
            try
            {
                switch (choice)
                {
                    case "Karakters":
                        await Shell.Current.GoToAsync("//Karakters", animate: false);
                        break;
                    case "Karakter-pare":
                        await Shell.Current.GoToAsync(nameof(KarakterPareGamePage), animate: true);
                        break;
                    case "Wie is dié Karakter?":
                        await Shell.Current.GoToAsync(nameof(KarakterRaaiGamePage), animate: true);
                        break;
                    case "Afgelaai":
                        await Shell.Current.GoToAsync(nameof(DownloadedPage), animate: true);
                        break;
                    case "Instellings":
                        await Shell.Current.GoToAsync(nameof(SettingsPage), animate: true);
                        break;
                    case "Bestuur rekening":
                        await OpenAccountCoreAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                _analytics.TrackException(ex, "mobile_menu_navigation_failed", new Dictionary<string, object>
                {
                    ["menu_choice"] = choice
                });
                await DisplayAlertAsync(
                    "Kon nie oopmaak nie",
                    "Dié blad kon nie nou oopmaak nie. Probeer asseblief weer.",
                    "Reg so");
            }
        });
    }

    private void StartKaraktersDestinationWarmup(CancellationToken cancellationToken)
    {
        if (_hasStartedKaraktersDestinationWarmup)
        {
            return;
        }

        _hasStartedKaraktersDestinationWarmup = true;
        _ = WarmKaraktersDestinationAsync(cancellationToken);
    }

    private async Task WarmKaraktersDestinationAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Let Luister finish its first frame before constructing the hidden destination.
            await Task.Delay(180, cancellationToken);

            KaraktersPage? karaktersPage = null;
            await MainThread.InvokeOnMainThreadAsync(() =>
                karaktersPage = _services.GetRequiredService<KaraktersPage>());

            await _apiClient.WarmCharactersCacheAsync(cancellationToken);
            if (karaktersPage is not null)
            {
                await karaktersPage.PreloadCachedContentAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A direct tap can win the warmup race; the visible page then loads normally.
            _hasStartedKaraktersDestinationWarmup = false;
        }
        catch
        {
            // Preloading is best-effort and must never affect Luister.
            _hasStartedKaraktersDestinationWarmup = false;
        }
    }

    private async Task RefreshNotificationsInBackgroundAsync()
    {
        var cancellationToken = _pageActivityCancellation?.Token ?? default;
        if (!_sessionState.Current.IsSignedIn || _isRefreshingNotifications)
        {
            return;
        }

        _isRefreshingNotifications = true;
        try
        {
            _notificationPage = await _apiClient.GetNotificationsAsync(cancellationToken: cancellationToken);
            MainThread.BeginInvokeOnMainThread(RenderFloatingTopBar);
        }
        catch
        {
            // Notification badges are helpful, but must never block the Luister page.
        }
        finally
        {
            _isRefreshingNotifications = false;
        }
    }

    private void StartNotificationRefreshTimer()
    {
        if (_notificationRefreshTimer is not null)
        {
            _notificationRefreshTimer.Start();
            return;
        }

        _notificationRefreshTimer = Dispatcher.CreateTimer();
        _notificationRefreshTimer.Interval = NotificationBadgeRefreshInterval;
        _notificationRefreshTimer.Tick += (_, _) =>
        {
            if (_isPageActive && _sessionState.Current.IsSignedIn)
            {
                _ = RefreshNotificationsInBackgroundAsync();
            }
        };
        _notificationRefreshTimer.Start();
    }

    private void StopNotificationRefreshTimer()
    {
        _notificationRefreshTimer?.Stop();
    }

    private void StartOortjiesPeekMascot()
    {
        _recentOortjiesPeekTimes.Clear();
        ScheduleOortjiesPeek(RandomDelay(OortjiesInitialDelayMin, OortjiesInitialDelayMax));
    }

    private void StopOortjiesPeekMascot()
    {
        _oortjiesPeekTimer?.Stop();
        _oortjiesHideTimer?.Stop();
        _oortjiesPeekMascot.CancelAnimations();
        _oortjiesPeekMascot.Opacity = 0;
        _oortjiesPeekMascot.IsVisible = false;
        _oortjiesPeekMascot.InputTransparent = true;
        _isOortjiesPeekVisible = false;
        _lastOortjiesPeekSide = null;
        _activeOortjiesPeekPlacement = null;
        _recentOortjiesPeekTimes.Clear();
    }

    private void ScheduleOortjiesPeek(TimeSpan delay)
    {
        _oortjiesPeekTimer?.Stop();
        _oortjiesPeekTimer = Dispatcher.CreateTimer();
        _oortjiesPeekTimer.Interval = delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
        _oortjiesPeekTimer.Tick += (_, _) =>
        {
            _oortjiesPeekTimer?.Stop();
            ShowOortjiesPeekMascot();
        };
        _oortjiesPeekTimer.Start();
    }

    private void ScheduleOortjiesHide()
    {
        _oortjiesHideTimer?.Stop();
        _oortjiesHideTimer = Dispatcher.CreateTimer();
        _oortjiesHideTimer.Interval = OortjiesPeekVisibleDuration;
        _oortjiesHideTimer.Tick += (_, _) =>
        {
            _oortjiesHideTimer?.Stop();
            HideOortjiesPeekMascot(jump: false);
        };
        _oortjiesHideTimer.Start();
    }

    private void ShowOortjiesPeekMascot()
    {
        if (!_isPageActive || Handler is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _recentOortjiesPeekTimes.RemoveAll(timestamp => now - timestamp >= OortjiesPeekWindow);
        if (_recentOortjiesPeekTimes.Count >= MaxOortjiesPeeksPerWindow)
        {
            var nextAllowedAt = _recentOortjiesPeekTimes[0] + OortjiesPeekWindow;
            ScheduleOortjiesPeek(nextAllowedAt - now + RandomDelay(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(24)));
            return;
        }

        var placement = BuildOortjiesPeekPlacement(ChooseOortjiesPeekSide());
        _activeOortjiesPeekPlacement = placement;
        _recentOortjiesPeekTimes.Add(now);
        _oortjiesPeekMascot.CancelAnimations();
        _oortjiesPeekMascot.WidthRequest = OortjiesPeekWidth;
        _oortjiesPeekMascot.HeightRequest = OortjiesPeekHeight;
        _oortjiesPeekMascot.Rotation = placement.Rotation;
        _oortjiesPeekMascot.TranslationX = placement.HiddenX;
        _oortjiesPeekMascot.TranslationY = placement.HiddenY;
        _oortjiesPeekMascot.Opacity = 0;
        _oortjiesPeekMascot.InputTransparent = false;
        _oortjiesPeekMascot.IsVisible = true;
        _isOortjiesPeekVisible = true;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.WhenAll(
                    _oortjiesPeekMascot.TranslateToAsync(placement.VisibleX, placement.VisibleY, 740, Easing.CubicOut),
                    _oortjiesPeekMascot.FadeToAsync(1, 240, Easing.CubicOut));
            }
            catch
            {
                _oortjiesPeekMascot.TranslationX = placement.VisibleX;
                _oortjiesPeekMascot.TranslationY = placement.VisibleY;
                _oortjiesPeekMascot.Opacity = 1;
            }

            if (_isPageActive && _isOortjiesPeekVisible)
            {
                ScheduleOortjiesHide();
            }
        });
    }

    private void HideOortjiesPeekMascot(bool jump)
    {
        if (!_isOortjiesPeekVisible)
        {
            return;
        }

        _oortjiesHideTimer?.Stop();
        _isOortjiesPeekVisible = false;
        _oortjiesPeekMascot.InputTransparent = true;
        if (_activeOortjiesPeekPlacement is not { } placement)
        {
            _oortjiesPeekMascot.Opacity = 0;
            _oortjiesPeekMascot.IsVisible = false;
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                _oortjiesPeekMascot.CancelAnimations();
                var awayStart = jump
                    ? _oortjiesPeekMascot.TranslateToAsync(placement.JumpX, placement.JumpY, 180, Easing.CubicOut)
                    : _oortjiesPeekMascot.TranslateToAsync(placement.WiggleX, placement.WiggleY, 220, Easing.CubicOut);
                await awayStart;
                await Task.WhenAll(
                    _oortjiesPeekMascot.TranslateToAsync(placement.HiddenX, placement.HiddenY, jump ? 360u : 520u, Easing.CubicIn),
                    _oortjiesPeekMascot.FadeToAsync(0, 220, Easing.CubicOut));
            }
            catch
            {
                _oortjiesPeekMascot.TranslationX = placement.HiddenX;
                _oortjiesPeekMascot.TranslationY = placement.HiddenY;
                _oortjiesPeekMascot.Opacity = 0;
            }

            _oortjiesPeekMascot.IsVisible = false;
            _activeOortjiesPeekPlacement = null;
            if (_isPageActive)
            {
                ScheduleOortjiesPeek(RandomDelay(OortjiesNextDelayMin, OortjiesNextDelayMax));
            }
        });
    }

    private OortjiesPeekSide ChooseOortjiesPeekSide()
    {
        var sides = new[]
        {
            OortjiesPeekSide.Left,
            OortjiesPeekSide.Right,
            OortjiesPeekSide.Top,
            OortjiesPeekSide.Bottom
        };
        var candidates = sides.Where(side => side != _lastOortjiesPeekSide).ToArray();
        var side = candidates[Random.Shared.Next(candidates.Length)];
        _lastOortjiesPeekSide = side;
        return side;
    }

    private OortjiesPeekPlacement BuildOortjiesPeekPlacement(OortjiesPeekSide side)
    {
        var viewport = GetOortjiesViewportSize();
        var topClearance = FloatingTopBarContentInset + 8;
        var bottomClearance = BottomBarContentInset;
        var verticalCenter = RandomBetween(
            Math.Min(viewport.Height - bottomClearance, topClearance + OortjiesPeekHeight / 2),
            Math.Max(topClearance + OortjiesPeekHeight / 2, viewport.Height - bottomClearance));
        var horizontalCenter = RandomBetween(
            OortjiesPeekWidth * 0.7,
            Math.Max(OortjiesPeekWidth * 0.7, viewport.Width - OortjiesPeekWidth * 0.7));

        return side switch
        {
            OortjiesPeekSide.Right => new OortjiesPeekPlacement(
                HiddenX: viewport.Width + OortjiesPeekWidth * 0.08,
                HiddenY: verticalCenter - OortjiesPeekHeight / 2,
                VisibleX: viewport.Width - OortjiesPeekWidth * 0.58,
                VisibleY: verticalCenter - OortjiesPeekHeight / 2,
                WiggleX: viewport.Width - OortjiesPeekWidth * 0.66,
                WiggleY: verticalCenter - OortjiesPeekHeight * 0.515,
                JumpX: viewport.Width - OortjiesPeekWidth * 0.72,
                JumpY: verticalCenter - OortjiesPeekHeight * 0.54,
                Rotation: -90),
            OortjiesPeekSide.Top => new OortjiesPeekPlacement(
                HiddenX: horizontalCenter - OortjiesPeekWidth / 2,
                HiddenY: -OortjiesPeekHeight * 1.08,
                VisibleX: horizontalCenter - OortjiesPeekWidth / 2,
                VisibleY: -OortjiesPeekHeight * 0.42,
                WiggleX: horizontalCenter - OortjiesPeekWidth * 0.515,
                WiggleY: -OortjiesPeekHeight * 0.34,
                JumpX: horizontalCenter - OortjiesPeekWidth * 0.54,
                JumpY: -OortjiesPeekHeight * 0.28,
                Rotation: 180),
            OortjiesPeekSide.Bottom => new OortjiesPeekPlacement(
                HiddenX: horizontalCenter - OortjiesPeekWidth / 2,
                HiddenY: viewport.Height + OortjiesPeekHeight * 0.08,
                VisibleX: horizontalCenter - OortjiesPeekWidth / 2,
                VisibleY: viewport.Height - OortjiesPeekHeight * 0.58,
                WiggleX: horizontalCenter - OortjiesPeekWidth * 0.515,
                WiggleY: viewport.Height - OortjiesPeekHeight * 0.66,
                JumpX: horizontalCenter - OortjiesPeekWidth * 0.54,
                JumpY: viewport.Height - OortjiesPeekHeight * 0.72,
                Rotation: 0),
            _ => new OortjiesPeekPlacement(
                HiddenX: -OortjiesPeekWidth * 1.08,
                HiddenY: verticalCenter - OortjiesPeekHeight / 2,
                VisibleX: -OortjiesPeekWidth * 0.42,
                VisibleY: verticalCenter - OortjiesPeekHeight / 2,
                WiggleX: -OortjiesPeekWidth * 0.34,
                WiggleY: verticalCenter - OortjiesPeekHeight * 0.515,
                JumpX: -OortjiesPeekWidth * 0.28,
                JumpY: verticalCenter - OortjiesPeekHeight * 0.54,
                Rotation: 90)
        };
    }

    private (double Width, double Height) GetOortjiesViewportSize()
    {
        var width = _rootLayout.Width;
        var height = _rootLayout.Height;
        if (width > 0 && height > 0)
        {
            return (width, height);
        }

        var display = DeviceDisplay.MainDisplayInfo;
        return (display.Width / display.Density, display.Height / display.Density);
    }

    private static TimeSpan RandomDelay(TimeSpan min, TimeSpan max) =>
        TimeSpan.FromMilliseconds(RandomBetween(min.TotalMilliseconds, max.TotalMilliseconds));

    private static double RandomBetween(double min, double max)
    {
        if (max <= min)
        {
            return min;
        }

        return min + Random.Shared.NextDouble() * (max - min);
    }

    private async Task ShowNotificationsAsync()
    {
        if (!_sessionState.Current.IsSignedIn)
        {
            await DisplayAlertAsync("Kennisgewings", "Teken in om kennisgewings te sien.", "Reg so");
            return;
        }

        var titleLabel = new Label
        {
            Text = "Kennisgewings",
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0B3534"),
            VerticalTextAlignment = TextAlignment.Center
        };
        var countLabel = new Label
        {
            FontSize = 13,
            TextColor = Color.FromArgb("#6B7280")
        };
        var statusLabel = new Label
        {
            Text = "Laai kennisgewings...",
            FontSize = 14,
            TextColor = Color.FromArgb("#6B7280"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        var list = new VerticalStackLayout { Spacing = 10 };
        var clearButton = new Button
        {
            Text = "Maak skoon",
            BackgroundColor = Color.FromArgb("#F4E9D1"),
            TextColor = Color.FromArgb("#0B3534"),
            CornerRadius = 16,
            HeightRequest = 42,
            Padding = new Thickness(14, 0)
        };
        var loadMoreButton = new Button
        {
            Text = "Wys vorige kennisgewings",
            BackgroundColor = Color.FromArgb("#123F3F"),
            TextColor = Colors.White,
            CornerRadius = 16,
            HeightRequest = 48,
            IsVisible = false
        };
        var closeButton = BuildNotificationCloseButton();
        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += async (_, _) => await Navigation.PopModalAsync();
        closeButton.GestureRecognizers.Add(closeTap);

        var titleStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { titleLabel, countLabel }
        };
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 12,
            Children =
            {
                closeButton,
                titleStack,
                clearButton
            }
        };
        Grid.SetColumn(titleStack, 1);
        Grid.SetColumn(clearButton, 2);

        var notificationScrollView = new ScrollView
        {
            Content = list,
            VerticalOptions = LayoutOptions.Fill
        };
        var modalLayout = new Grid
        {
            Padding = new Thickness(18, 18, 18, 28),
            RowSpacing = 16,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            },
            Children =
            {
                header,
                statusLabel,
                notificationScrollView,
                loadMoreButton
            }
        };
        Grid.SetRow(statusLabel, 1);
        Grid.SetRow(notificationScrollView, 2);
        Grid.SetRow(loadMoreButton, 3);

        var modal = new ContentPage
        {
            Title = "Kennisgewings",
            BackgroundColor = LuisterModalBackgroundColor,
            Content = modalLayout
        };

        clearButton.Clicked += async (_, _) =>
            await ClearNotificationsAsync(list, countLabel, statusLabel, clearButton, loadMoreButton);
        loadMoreButton.Clicked += async (_, _) =>
            await LoadMoreNotificationsAsync(list, countLabel, statusLabel, clearButton, loadMoreButton);

        var renderedCachedNotifications = await TryRenderCachedNotificationsAsync(
            list,
            countLabel,
            statusLabel,
            clearButton,
            loadMoreButton);

        await Navigation.PushModalAsync(modal, true);
        _ = LoadNotificationsAsync(
            list,
            countLabel,
            statusLabel,
            clearButton,
            loadMoreButton,
            renderedCachedNotifications);
    }

    private async Task<bool> TryRenderCachedNotificationsAsync(
        VerticalStackLayout list,
        Label countLabel,
        Label statusLabel,
        Button clearButton,
        Button loadMoreButton)
    {
        var cachedPage = _notificationPage ?? await _apiClient.GetCachedNotificationsAsync();
        if (cachedPage is null)
        {
            return false;
        }

        _notificationPage = cachedPage;
        RenderNotificationModalState(list, countLabel, statusLabel, clearButton, loadMoreButton);
        return true;
    }

    private async Task LoadNotificationsAsync(
        VerticalStackLayout list,
        Label countLabel,
        Label statusLabel,
        Button clearButton,
        Button loadMoreButton,
        bool hasRenderedCachedNotifications = false)
    {
        if (!hasRenderedCachedNotifications)
        {
            SetNotificationControlsBusy(statusLabel, clearButton, loadMoreButton, "Laai kennisgewings...");
        }

        try
        {
            _notificationPage = await _apiClient.GetNotificationsAsync();
            RenderNotificationModalState(list, countLabel, statusLabel, clearButton, loadMoreButton);

            if (_notificationPage?.UnreadCount > 0)
            {
                MarkAllNotificationsReadLocally();
                RenderNotificationModalState(list, countLabel, statusLabel, clearButton, loadMoreButton);
                RenderContent();
                await _apiClient.MarkAllNotificationsReadAsync();
                _ = _apiClient.SaveNotificationsCacheAsync(_notificationPage);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _notificationPage = null;
            statusLabel.IsVisible = true;
            statusLabel.Text = "Teken in om kennisgewings te sien.";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("status 401", StringComparison.OrdinalIgnoreCase))
        {
            _notificationPage = null;
            statusLabel.IsVisible = true;
            statusLabel.Text = "Teken in om kennisgewings te sien.";
        }
        catch
        {
            if (!hasRenderedCachedNotifications)
            {
                statusLabel.IsVisible = true;
                statusLabel.Text = "Ons kon nie nou die kennisgewings laai nie.";
            }
        }
        finally
        {
            clearButton.IsEnabled = true;
            loadMoreButton.IsEnabled = true;
            RenderContent();
        }
    }

    private async Task LoadMoreNotificationsAsync(
        VerticalStackLayout list,
        Label countLabel,
        Label statusLabel,
        Button clearButton,
        Button loadMoreButton)
    {
        if (_notificationPage is null)
        {
            return;
        }

        var before = _notificationPage.Notifications.LastOrDefault()?.CreatedAt;
        if (before is null)
        {
            return;
        }

        SetNotificationControlsBusy(statusLabel, clearButton, loadMoreButton, "Laai vorige kennisgewings...");
        try
        {
            var loadedPage = await _apiClient.GetNotificationsAsync(before: before, history: _notificationPage.HasHistory);
            if (loadedPage is not null)
            {
                _notificationPage = MergeNotificationPages(_notificationPage, loadedPage);
                RenderNotificationModalState(list, countLabel, statusLabel, clearButton, loadMoreButton);
            }
        }
        finally
        {
            clearButton.IsEnabled = true;
            loadMoreButton.IsEnabled = true;
        }
    }

    private async Task ClearNotificationsAsync(
        VerticalStackLayout list,
        Label countLabel,
        Label statusLabel,
        Button clearButton,
        Button loadMoreButton)
    {
        if (_notificationPage?.Notifications.Count > 0 != true)
        {
            return;
        }

        clearButton.IsEnabled = false;
        try
        {
            await _apiClient.ClearNotificationsAsync();
            _notificationPage = _notificationPage with
            {
                Count = 0,
                UnreadCount = 0,
                HasMore = false,
                HasHistory = false,
                Notifications = Array.Empty<MobileNotificationItem>()
            };
            _ = _apiClient.SaveNotificationsCacheAsync(_notificationPage);
            RenderNotificationModalState(list, countLabel, statusLabel, clearButton, loadMoreButton);
            RenderContent();
        }
        finally
        {
            clearButton.IsEnabled = true;
        }
    }

    private static void SetNotificationControlsBusy(
        Label statusLabel,
        Button clearButton,
        Button loadMoreButton,
        string message)
    {
        statusLabel.IsVisible = true;
        statusLabel.Text = message;
        clearButton.IsEnabled = false;
        loadMoreButton.IsEnabled = false;
    }

    private void RenderNotificationModalState(
        VerticalStackLayout list,
        Label countLabel,
        Label statusLabel,
        Button clearButton,
        Button loadMoreButton)
    {
        var page = _notificationPage;
        var notifications = page?.Notifications ?? Array.Empty<MobileNotificationItem>();
        list.Children.Clear();

        countLabel.Text = page?.UnreadCount > 0
            ? $"{page.UnreadCount} ongelees"
            : "Geen ongelees";
        clearButton.IsVisible = notifications.Count > 0;
        loadMoreButton.IsVisible = page is not null && (page.HasMore || page.HasHistory);

        if (notifications.Count == 0)
        {
            statusLabel.IsVisible = true;
            statusLabel.Text = "Geen kennisgewings nog nie.";
            return;
        }

        statusLabel.IsVisible = false;
        foreach (var notification in notifications)
        {
            list.Children.Add(BuildNotificationItem(notification, list, countLabel, statusLabel, clearButton, loadMoreButton));
        }
    }

    private View BuildNotificationItem(
        MobileNotificationItem notification,
        VerticalStackLayout list,
        Label countLabel,
        Label statusLabel,
        Button clearButton,
        Button loadMoreButton)
    {
        var isClearing = false;
        var clearItemButton = new Button
        {
            Text = "×",
            FontSize = 22,
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#6B7280"),
            WidthRequest = 40,
            HeightRequest = 40,
            Padding = 0
        };

        async Task ClearNotificationAsync()
        {
            if (isClearing)
            {
                return;
            }

            isClearing = true;
            clearItemButton.IsEnabled = false;
            try
            {
                await _apiClient.ClearNotificationAsync(notification.Id);
                RemoveNotificationLocally(notification.Id);
                if (_notificationPage is not null)
                {
                    _ = _apiClient.SaveNotificationsCacheAsync(_notificationPage);
                }

                RenderNotificationModalState(list, countLabel, statusLabel, clearButton, loadMoreButton);
                RenderContent();
            }
            finally
            {
                isClearing = false;
                clearItemButton.IsEnabled = true;
            }
        }

        clearItemButton.Clicked += async (_, _) => await ClearNotificationAsync();

        var copy = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        BuildNotificationTypeLabel(notification.Type),
                        new Label
                        {
                            Text = FormatNotificationDate(notification.CreatedAt),
                            FontSize = 11,
                            TextColor = Color.FromArgb("#6B7280"),
                            VerticalTextAlignment = TextAlignment.Center
                        }
                    }
                },
                new Label
                {
                    Text = string.IsNullOrWhiteSpace(notification.Title) ? "Kennisgewing" : notification.Title,
                    FontSize = 15,
                    FontAttributes = notification.IsRead ? FontAttributes.None : FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1B2231"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 2
                },
                new Label
                {
                    Text = notification.Body,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#5F5F5F"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 2
                }
            }
        };
        var imageFrame = new Border
        {
            WidthRequest = 58,
            HeightRequest = 58,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new Image
            {
                Source = BuildLuisterImageSource(notification.ImagePath, "schink_background.jpeg"),
                Aspect = Aspect.AspectFill,
                WidthRequest = 58,
                HeightRequest = 58
            }
        };
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 12,
            Children =
            {
                imageFrame,
                copy,
                clearItemButton
            }
        };
        Grid.SetColumn(copy, 1);
        Grid.SetColumn(clearItemButton, 2);

        var row = new Border
        {
            BackgroundColor = notification.IsRead ? Colors.White : Color.FromArgb("#EEF8F5"),
            Stroke = notification.IsRead ? Color.FromArgb("#EFE4D0") : Color.FromArgb("#80A7DCCB"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 12,
            Content = grid
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            await OpenNotificationAsync(notification);
            RenderNotificationModalState(list, countLabel, statusLabel, clearButton, loadMoreButton);
            RenderContent();
        };
        row.GestureRecognizers.Add(tap);

        var removeSwipeItem = new SwipeItem
        {
            Text = "Verwyder",
            BackgroundColor = Color.FromArgb("#E11D48")
        };
        removeSwipeItem.Invoked += async (_, _) => await ClearNotificationAsync();

        var swipeItems = new SwipeItems
        {
            Mode = SwipeMode.Reveal,
            SwipeBehaviorOnInvoked = SwipeBehaviorOnInvoked.Close
        };
        swipeItems.Add(removeSwipeItem);

        return new SwipeView
        {
            RightItems = swipeItems,
            Content = row
        };
    }

    private async Task OpenNotificationAsync(MobileNotificationItem notification)
    {
        await _apiClient.MarkNotificationReadAsync(notification.Id);
        MarkNotificationReadLocally(notification.Id);
        if (_notificationPage is not null)
        {
            _ = _apiClient.SaveNotificationsCacheAsync(_notificationPage);
        }

        var href = ResolveNotificationHref(notification);
        if (!string.IsNullOrWhiteSpace(href))
        {
            await Browser.OpenAsync(_apiClient.BuildAbsoluteUrl(href), BrowserLaunchMode.External);
        }
    }

    private static Label BuildNotificationTypeLabel(string notificationType) =>
        new()
        {
            Text = GetNotificationTypeLabel(notificationType),
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F766E"),
            VerticalTextAlignment = TextAlignment.Center
        };

    private static string GetNotificationTypeLabel(string notificationType) =>
        notificationType.Trim().ToLowerInvariant() switch
        {
            "character_unlock" => "Karakter",
            "story_published" => "Nuwe storie",
            "blog_published" => "Nuwe blog",
            "resource_document_published" => "Nuwe hulpbron",
            _ => "Kennisgewing"
        };

    private static string FormatNotificationDate(DateTimeOffset createdAt) =>
        createdAt.LocalDateTime.ToString("dd MMM", CultureInfo.CurrentCulture);

    private static MobileNotificationPage MergeNotificationPages(
        MobileNotificationPage currentPage,
        MobileNotificationPage loadedPage)
    {
        var existingIds = currentPage.Notifications.Select(notification => notification.Id).ToHashSet();
        var mergedNotifications = currentPage.Notifications
            .Concat(loadedPage.Notifications.Where(notification => existingIds.Add(notification.Id)))
            .ToArray();

        return loadedPage with
        {
            Count = mergedNotifications.Length,
            UnreadCount = currentPage.UnreadCount,
            Notifications = mergedNotifications
        };
    }

    private void MarkAllNotificationsReadLocally()
    {
        if (_notificationPage is null)
        {
            return;
        }

        _notificationPage = _notificationPage with
        {
            UnreadCount = 0,
            Notifications = _notificationPage.Notifications
                .Select(notification => notification with { IsRead = true })
                .ToArray()
        };
    }

    private void MarkNotificationReadLocally(Guid notificationId)
    {
        if (_notificationPage is null)
        {
            return;
        }

        var notifications = _notificationPage.Notifications
            .Select(notification => notification.Id == notificationId
                ? notification with { IsRead = true }
                : notification)
            .ToArray();

        _notificationPage = _notificationPage with
        {
            Notifications = notifications,
            UnreadCount = notifications.Count(notification => !notification.IsRead)
        };
    }

    private void RemoveNotificationLocally(Guid notificationId)
    {
        if (_notificationPage is null)
        {
            return;
        }

        var notifications = _notificationPage.Notifications
            .Where(notification => notification.Id != notificationId)
            .ToArray();

        _notificationPage = _notificationPage with
        {
            Count = notifications.Length,
            Notifications = notifications,
            UnreadCount = notifications.Count(notification => !notification.IsRead)
        };
    }

    private static string? ResolveNotificationHref(MobileNotificationItem notification)
    {
        var href = notification.Href?.Trim();
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (!string.Equals(notification.Type, "character_unlock", StringComparison.OrdinalIgnoreCase))
        {
            return href;
        }

        var path = href;
        if (Uri.TryCreate(href, UriKind.Absolute, out var parsedUrl))
        {
            path = $"{parsedUrl.AbsolutePath}{parsedUrl.Query}{parsedUrl.Fragment}";
        }

        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = $"/{path.TrimStart('/')}";
        }

        if (path.Equals("/karakters", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/karakters?", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/karakters#", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var routePath = path.Split('?', '#')[0];
        var segments = routePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var route = segments.FirstOrDefault()?.ToLowerInvariant();
        if ((route is "karakter" or "karakters" or "character" or "characters") && segments.Length > 1)
        {
            return $"/karakters?karakter={Uri.EscapeDataString(segments[1])}";
        }

        return "/karakters";
    }

    private Task OpenAccountAsync() =>
        _navigationGate.RunAsync(OpenAccountCoreAsync);

    private static Task OpenAccountCoreAsync() =>
        Shell.Current.GoToAsync(nameof(AccountPage), animate: true);

    private Task OpenProfileAsync() =>
        _navigationGate.RunAsync(() => Shell.Current.GoToAsync(nameof(ProfilePage), animate: true));

    private static string BuildInitials(MobileSession session)
    {
        var source = !string.IsNullOrWhiteSpace(session.DisplayName)
            ? session.DisplayName
            : session.Email;

        if (string.IsNullOrWhiteSpace(source))
        {
            return "S";
        }

        var localName = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;
        var tokens = localName
            .Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();

        if (tokens.Length >= 2)
        {
            return $"{char.ToUpperInvariant(tokens[0][0])}{char.ToUpperInvariant(tokens[1][0])}";
        }

        if (tokens.Length == 1)
        {
            var token = tokens[0];
            return token.Length >= 2
                ? $"{char.ToUpperInvariant(token[0])}{char.ToUpperInvariant(token[1])}"
                : char.ToUpperInvariant(token[0]).ToString();
        }

        return "S";
    }

    private View BuildAccountPanel()
    {
        var loginButton = new Button
        {
            Text = "Teken in",
            BackgroundColor = Color.FromArgb("#0F766E"),
            TextColor = Colors.White,
            CornerRadius = 16
        };
        loginButton.Clicked += async (_, _) => await SignInAsync();

        var plansButton = new Button
        {
            Text = "Sien opsies",
            BackgroundColor = Color.FromArgb("#F3F4F6"),
            TextColor = Color.FromArgb("#222222"),
            CornerRadius = 16
        };
        plansButton.Clicked += async (_, _) =>
        {
            await _navigationGate.RunAsync(() => OpenPlansAsync());
        };

        var panel = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = 16,
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label
                    {
                        Text = "Teken in vir jou volle luistertoegang",
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 18,
                        TextColor = Color.FromArgb("#222222")
                    },
                    new Label
                    {
                        Text = "Jy kan steeds rondkyk, maar geslote stories maak oop wanneer jou rekening aktief is.",
                        TextColor = Color.FromArgb("#5F5F5F"),
                        FontSize = 13
                    },
                    _loginEmailEntry,
                    _loginPasswordEntry,
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children = { loginButton, plansButton }
                    },
                    _loginStatusLabel
                }
            }
        };
        MobileResponsiveLayout.ApplyCenteredContent(panel, Width, 720);
        return panel;
    }

    private async Task SignInAsync()
    {
        try
        {
            _loginStatusLabel.Text = "Teken in...";
            _loginStatusLabel.TextColor = Color.FromArgb("#5F5F5F");
            var result = await _apiClient.SignInAsync(_loginEmailEntry.Text ?? string.Empty, _loginPasswordEntry.Text ?? string.Empty);
            _loginPasswordEntry.Text = string.Empty;
            _loginStatusLabel.Text = result.Message;
            _loginStatusLabel.TextColor = Color.FromArgb("#0F766E");
            await LoadAsync(forceRefresh: true);
        }
        catch (Exception ex)
        {
            _loginStatusLabel.Text = ex.Message;
            _loginStatusLabel.TextColor = Color.FromArgb("#B42318");
        }
    }

    private View BuildPlaylistShowcase(string title, IReadOnlyList<MobilePlaylist> playlists)
    {
        var section = new VerticalStackLayout { Spacing = 10 };
        MobileResponsiveLayout.ApplyCenteredContent(section, Width, 1100);
        section.Children.Add(PageHelpers.BuildSectionTitle(string.IsNullOrWhiteSpace(title) ? "Speellyste" : title));

        section.Children.Add(BuildHorizontalCarousel(
            playlists,
            GetPlaylistCarouselHeight(),
            playlist => BuildPlaylistCard(playlist)));

        return section;
    }

    private View BuildPlaylistCard(MobilePlaylist playlist)
    {
        var imageSource = BuildLuisterImageSource(playlist.ArtworkUrl, "schink_background.jpeg");
        var cardWidth = MobileResponsiveLayout.ResolvePlaylistCarouselCardWidth(Width, IsAndroid);
        var artworkHeight = MobileResponsiveLayout.IsWide(Width)
            ? Math.Clamp(cardWidth * 0.58, 150, 180)
            : IsAndroid ? 126 : 138;
        var card = new Border
        {
            WidthRequest = cardWidth,
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Content = new VerticalStackLayout
            {
                Spacing = 9,
                Children =
                {
                    new Border
                    {
                        StrokeThickness = 0,
                        StrokeShape = BuildArtworkShape(16),
                        HeightRequest = artworkHeight,
                        Content = new Grid
                        {
                            Children =
                            {
                                new Image
                                {
                                    Source = imageSource,
                                    HeightRequest = artworkHeight,
                                    Aspect = Aspect.AspectFill
                                },
                                BuildCoverPlayBadge("▦", 38, 19, 0)
                            }
                        },
                    },
                    new Label
                    {
                        Text = playlist.Title,
                        FontSize = 17,
                        TextColor = Color.FromArgb("#1B2231"),
                        MaxLines = 2,
                        LineBreakMode = LineBreakMode.TailTruncation,
                        LineHeight = 1.15
                    }
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenPlaylistAsync(playlist);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private View BuildPlaylistSection(MobilePlaylist playlist)
    {
        var section = new VerticalStackLayout { Spacing = 10 };
        MobileResponsiveLayout.ApplyCenteredContent(section, Width, 1100);
        section.Children.Add(new Label
        {
            Text = playlist.Title,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#222222"),
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.Center
        });

        if (!string.IsNullOrWhiteSpace(playlist.Description))
        {
            section.Children.Add(new Label
            {
                Text = playlist.Description,
                FontSize = 14,
                TextColor = Color.FromArgb("#5F5F5F"),
                HorizontalOptions = LayoutOptions.Fill,
                HorizontalTextAlignment = TextAlignment.Center
            });
        }

        var showcaseStory = ResolvePlaylistShowcaseStory(playlist);
        if (showcaseStory is not null && ShouldShowPlaylistShowcase(playlist))
        {
            section.Children.Add(BuildPlaylistShowcaseStory(playlist, showcaseStory));
        }

        section.Children.Add(IsWeeklyPopularPlaylist(playlist)
            ? BuildRankedStoryCarousel(playlist)
            : BuildHorizontalCarousel(
                playlist.Stories,
                GetStoryCarouselHeight(),
                story => BuildLuisterStoryCarouselCard(playlist, story)));

        return section;
    }

    private View BuildPlaylistShowcaseStory(MobilePlaylist playlist, MobileStorySummary story)
    {
        var wideLayout = MobileResponsiveLayout.IsWide(Width);
        var pageWidth = MobileResponsiveLayout.ResolveWidth(Width);
        var image = new Image
        {
            Source = BuildLuisterImageSource(
                PageHelpers.ResolveStoryCardImageSource(story, _apiClient)),
            Aspect = Aspect.AspectFill,
            InputTransparent = false,
            ZIndex = 0
        };
        var title = new Label
        {
            Text = string.IsNullOrWhiteSpace(story.Title) ? playlist.Title : story.Title,
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1B2231"),
            HorizontalTextAlignment = TextAlignment.Center,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.TailTruncation,
            LineHeight = 1.2
        };
        var openStoryTap = new TapGestureRecognizer();
        openStoryTap.Tapped += async (_, _) => await OpenPlaylistStoryAsync(story, playlist);
        image.GestureRecognizers.Add(openStoryTap);
        var openStoryFromTitleTap = new TapGestureRecognizer();
        openStoryFromTitleTap.Tapped += async (_, _) => await OpenPlaylistStoryAsync(story, playlist);
        title.GestureRecognizers.Add(openStoryFromTitleTap);
        var cover = new Border
        {
            Stroke = Color.FromArgb("#AA0F766E"),
            StrokeThickness = 3,
            StrokeShape = BuildArtworkShape(16),
            HeightRequest = wideLayout
                ? Math.Clamp(Math.Min(640, pageWidth - 48) * 0.82, 360, 540)
                : IsAndroid ? 282 : 320,
            Shadow = BuildScrollContentShadow(Brush.Black, new Point(0, 12), 26, 0.22f),
            Content = new Grid
            {
                Children =
                {
                    image,
                    BuildFavoriteOverlay(story),
                    BuildCoverPlayBadge("▶", 52, 22, 3)
                }
            }
        };
        if (wideLayout)
        {
            cover.WidthRequest = Math.Min(640, pageWidth - 48);
            cover.HorizontalOptions = LayoutOptions.Center;
        }
        cover.SizeChanged += (_, _) =>
        {
            if (cover.Width > 0)
            {
                var minimumHeight = wideLayout ? 360 : IsAndroid ? 220 : 248;
                var maximumHeight = wideLayout ? 540 : IsAndroid ? 308 : 360;
                var targetHeight = Math.Min(Math.Max(cover.Width, minimumHeight), maximumHeight);
                if (Math.Abs(cover.HeightRequest - targetHeight) > 0.5)
                {
                    cover.HeightRequest = targetHeight;
                }
            }
        };

        var showcase = new VerticalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(0, 2, 0, 6),
            Children =
            {
                cover,
                title
            }
        };
        MobileResponsiveLayout.ApplyCenteredContent(showcase, Width, wideLayout ? 720 : 1100);
        return showcase;
    }

    private static MobileStorySummary? ResolvePlaylistShowcaseStory(MobilePlaylist playlist) =>
        playlist.ShowcaseStory ?? playlist.Stories.FirstOrDefault();

    private static bool ShouldShowPlaylistShowcase(MobilePlaylist playlist) =>
        playlist.ShowShowcaseImageOnLuisterPage == true;

    private static bool IsWeeklyPopularPlaylist(MobilePlaylist playlist) =>
        string.Equals(playlist.Slug, "popular-stories-this-week", StringComparison.OrdinalIgnoreCase);

    private View BuildRankedStoryCarousel(MobilePlaylist playlist)
    {
        var rankedStories = playlist.Stories
            .Select((story, index) => new RankedLuisterStory(story, index + 1))
            .ToArray();

        return BuildHorizontalCarousel(
            rankedStories,
            GetStoryCarouselHeight(isRanked: true),
            rankedStory => BuildLuisterStoryCarouselCard(playlist, rankedStory.Story, rankedStory.Rank));
    }

    private double GetStoryCarouselCardWidth()
    {
        return MobileResponsiveLayout.ResolveStoryCarouselCardWidth(Width, IsAndroid);
    }

    private double GetStoryCarouselCoverHeight()
    {
        var width = GetStoryCarouselCardWidth();
        return Math.Round(width * (IsAndroid ? 1.27 : 1.30));
    }

    private double GetStoryCarouselHeight(bool isRanked = false)
    {
        var coverHeight = GetStoryCarouselCoverHeight();
        return coverHeight + (isRanked ? 84 : 70);
    }

    private double GetPlaylistCarouselHeight()
    {
        if (!MobileResponsiveLayout.IsWide(Width))
        {
            return IsAndroid ? 172 : 186;
        }

        var cardWidth = MobileResponsiveLayout.ResolvePlaylistCarouselCardWidth(Width, IsAndroid);
        var artworkHeight = Math.Clamp(cardWidth * 0.58, 150, 180);
        return artworkHeight + 70;
    }

    private static CollectionView BuildHorizontalCarousel<T>(
        IReadOnlyList<T> items,
        double heightRequest,
        Func<T, View> buildItem)
    {
        var carousel = new CollectionView
        {
            ItemsSource = items,
            HeightRequest = heightRequest,
            Margin = new Thickness(-PageHorizontalPadding, 0),
            Header = new BoxView
            {
                WidthRequest = PageHorizontalPadding,
                Color = Colors.Transparent
            },
            Footer = new BoxView
            {
                WidthRequest = PageHorizontalPadding,
                Color = Colors.Transparent
            },
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal)
            {
                ItemSpacing = 14,
                SnapPointsType = SnapPointsType.None
            },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
                new CarouselItemView<T>(buildItem))
        };

        return carousel;
    }

    private sealed class CarouselItemView<T>(Func<T, View> buildItem) : ContentView
    {
        private T? _renderedItem;

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
            if (BindingContext is not T item)
            {
                _renderedItem = default;
                Content = null;
                return;
            }

            if (EqualityComparer<T>.Default.Equals(_renderedItem, item))
            {
                return;
            }

            _renderedItem = item;
            Content = buildItem(item);
        }
    }

    private View BuildLuisterStoryCarouselCard(MobilePlaylist playlist, MobileStorySummary story, int? rank = null)
    {
        var isRanked = rank is not null;
        var cardWidth = GetStoryCarouselCardWidth();
        var coverHeight = GetStoryCarouselCoverHeight();
        var artwork = new Image
        {
            Source = BuildLuisterImageSource(
                PageHelpers.ResolveStoryCardImageSource(story, _apiClient)),
            Aspect = Aspect.AspectFill,
            HeightRequest = coverHeight,
            InputTransparent = false,
            ZIndex = 0
        };
        var openStoryTap = new TapGestureRecognizer();
        openStoryTap.Tapped += async (_, _) => await OpenPlaylistStoryAsync(story, playlist);
        artwork.GestureRecognizers.Add(openStoryTap);
        var title = new Label
        {
            Text = story.Title,
            FontSize = 16,
            TextColor = Color.FromArgb("#1B2231"),
            InputTransparent = false,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.TailTruncation,
            LineHeight = 1.16
        };
        var openStoryFromTitleTap = new TapGestureRecognizer();
        openStoryFromTitleTap.Tapped += async (_, _) => await OpenPlaylistStoryAsync(story, playlist);
        title.GestureRecognizers.Add(openStoryFromTitleTap);
        var coverGrid = new Grid
        {
            HeightRequest = coverHeight,
            Children =
            {
                artwork,
                BuildFavoriteOverlay(story),
                BuildCoverPlayBadge("▶", 38, 17, 2)
            }
        };

        var cover = new Border
        {
            StrokeThickness = 0,
            StrokeShape = BuildArtworkShape(16),
            HeightRequest = coverHeight,
            Margin = isRanked ? new Thickness(0, 13, 0, 0) : Thickness.Zero,
            Content = coverGrid
        };

        var cardShell = new Grid
        {
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 9,
                    Children =
                    {
                        cover,
                        title
                    }
                }
            }
        };
        if (rank is not null)
        {
            cardShell.Children.Add(BuildStoryRankBadge(rank.Value));
        }

        var card = new Border
        {
            WidthRequest = IsAndroid ? 148 : 168,
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Margin = new Thickness(0, 0, 0, 10),
            Content = cardShell
        };
        if (MobileResponsiveLayout.IsWide(Width))
        {
            card.WidthRequest = cardWidth;
        }
        return card;
    }

    private static View BuildStoryRankBadge(int rank) =>
        new Label
        {
            Text = rank.ToString(CultureInfo.InvariantCulture),
            TextColor = Color.FromArgb("#FFFEF8"),
            FontFamily = "Arial Rounded MT Bold",
            FontSize = 76,
            FontAttributes = FontAttributes.Bold,
            LineHeight = 0.82,
            Margin = new Thickness(0, -1, 0, 0),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
            Shadow = BuildScrollContentShadow(new SolidColorBrush(Color.FromArgb("#344146")), new Point(0, 6), 12, 0.24f)
        };

    private View BuildFavoriteOverlay(MobileStorySummary story)
    {
        var target = MobileFavoriteHeart.CreateButton(story.IsFavorite, 25);
        target.Shadow = BuildScrollContentShadow(
            Brush.Black,
            new Point(0, 2),
            7,
            story.IsFavorite ? 0.28f : 0.95f);
        target.WidthRequest = 44;
        target.HeightRequest = 44;
        target.Margin = new Thickness(0, 6, 6, 0);
        target.HorizontalOptions = LayoutOptions.End;
        target.VerticalOptions = LayoutOptions.Start;
        target.ZIndex = 20;
        target.AutomationId = $"favorite-{story.Slug}";
        SemanticProperties.SetDescription(
            target,
            story.IsFavorite ? "Verwyder gunsteling" : "Voeg by gunsteling");
        target.Clicked += async (_, _) => await ToggleFavoriteAsync(story);
        return target;
    }

    private static View BuildCoverPlayBadge(string icon, double size, double fontSize, double leftOffset) =>
        new Grid
        {
            InputTransparent = true,
            Children =
            {
                new Border
                {
                    WidthRequest = size,
                    HeightRequest = size,
                    BackgroundColor = Color.FromArgb("#8AF3B23F"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 999 },
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = icon,
                        Opacity = 0.78,
                        TextColor = Color.FromArgb("#2A1C05"),
                        FontSize = fontSize,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center,
                        Margin = new Thickness(leftOffset, 0, 0, 0)
                    }
                }
            }
        };

    private View? BuildContinueListeningCard()
    {
        var item = _continueListeningState.Current;
        if (item is null)
        {
            return null;
        }

        var resolvedStory = ResolveContinueListeningStory(item);
        var story = resolvedStory?.Story ?? ToMobileStorySummary(item);
        var playlistTitle = resolvedStory?.Playlist?.Title ?? item.PlaylistTitle;
        var imageUrl = PageHelpers.ResolveStoryCardImageSource(story, _apiClient);
        var progress = CalculateContinueProgress(item.PositionSeconds, story.DurationSeconds ?? item.DurationSeconds);

        var artwork = new Border
        {
            WidthRequest = 82,
            HeightRequest = 82,
            StrokeThickness = 0,
            StrokeShape = BuildArtworkShape(14),
            Content = new Image
            {
                Source = BuildLuisterImageSource(imageUrl),
                Aspect = Aspect.AspectFill
            }
        };

        var playButton = new Border
        {
            WidthRequest = 48,
            HeightRequest = 48,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            BackgroundColor = Color.FromArgb("#F4C044"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "▶",
                Margin = new Thickness(3, 0, 0, 0),
                FontSize = 21,
                TextColor = Color.FromArgb("#26302F"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        var playTap = new TapGestureRecognizer();
        playTap.Tapped += async (_, _) => await OpenContinueListeningAsync(item);
        playButton.GestureRecognizers.Add(playTap);

        var details = new VerticalStackLayout
        {
            Spacing = 5,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = story.Title,
                    FontSize = 17,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1B2231"),
                    MaxLines = 2,
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = string.IsNullOrWhiteSpace(playlistTitle)
                        ? "Gaan voort waar jy laas geluister het"
                        : playlistTitle,
                    FontSize = 13,
                    TextColor = Color.FromArgb("#686F6D"),
                    MaxLines = 1,
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new ProgressBar
                {
                    Progress = progress,
                    ProgressColor = Color.FromArgb("#0F766E"),
                    BackgroundColor = Color.FromArgb("#E5DDC8"),
                    HeightRequest = 4
                },
                new Label
                {
                    Text = BuildContinueTimeText(item.PositionSeconds, story.DurationSeconds ?? item.DurationSeconds),
                    FontSize = 12,
                    TextColor = Color.FromArgb("#767B78")
                }
            }
        };

        var cardGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 94 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 54 }
            },
            ColumnSpacing = 8,
            Children =
            {
                artwork,
                details,
                playButton
            }
        };
        Grid.SetColumn(artwork, 0);
        Grid.SetColumn(details, 1);
        Grid.SetColumn(playButton, 2);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#FBF7EC"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            Padding = 12,
            Shadow = BuildScrollContentShadow(Brush.Black, new Point(0, 8), 18, 0.12f),
            Content = cardGrid
        };

        var cardTap = new TapGestureRecognizer();
        cardTap.Tapped += async (_, _) => await OpenContinueListeningAsync(item);
        card.GestureRecognizers.Add(cardTap);

        var clearButton = new Button
        {
            Text = "Maak skoon",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F766E"),
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(8, 0),
            HeightRequest = 34,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };
        clearButton.Clicked += (_, _) => ClearContinueListening();

        var heading = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                new Label
                {
                    Text = "Gaan voort met luister",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1B2231"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                },
                clearButton
            }
        };
        Grid.SetColumn(clearButton, 1);

        var section = new VerticalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                heading,
                card
            }
        };
        MobileResponsiveLayout.ApplyCenteredContent(section, Width, 820);
        return section;
    }

    private void ClearContinueListening()
    {
        _continueListeningState.Clear();
        RenderPlaylistContent();
    }

    private (MobileStorySummary Story, MobilePlaylist? Playlist)? ResolveContinueListeningStory(ContinueListeningItem item)
    {
        foreach (var playlist in EnumerateLuisterPlaylists())
        {
            var story = playlist.Stories.FirstOrDefault(candidate => StoryMatchesContinueItem(candidate, item));
            if (story is not null)
            {
                return (story, playlist);
            }

            if (playlist.ShowcaseStory is { } showcaseStory && StoryMatchesContinueItem(showcaseStory, item))
            {
                return (showcaseStory, playlist);
            }
        }

        return null;
    }

    private IEnumerable<MobilePlaylist> EnumerateLuisterPlaylists()
    {
        foreach (var section in _sections)
        {
            if (section.Playlist is not null)
            {
                yield return section.Playlist;
            }

            foreach (var playlist in section.Playlists)
            {
                yield return playlist;
            }
        }
    }

    private static bool StoryMatchesContinueItem(MobileStorySummary story, ContinueListeningItem item) =>
        string.Equals(story.Slug, item.Slug, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(item.Source) ||
         string.IsNullOrWhiteSpace(story.Source) ||
         string.Equals(story.Source, item.Source, StringComparison.OrdinalIgnoreCase));

    private static MobileStorySummary ToMobileStorySummary(ContinueListeningItem item) =>
        new(
            item.Slug,
            item.Title,
            item.Description,
            item.ImageUrl,
            item.ThumbnailUrl,
            string.IsNullOrWhiteSpace(item.Source) ? "luister" : item.Source,
            IsLocked: false,
            IsFavorite: false,
            DetailUrl: string.Empty,
            DurationSeconds: item.DurationSeconds);

    private static double CalculateContinueProgress(decimal? positionSeconds, decimal? durationSeconds)
    {
        if (positionSeconds is not > 0 || durationSeconds is not > 0)
        {
            return 0;
        }

        return Math.Clamp((double)(positionSeconds.Value / durationSeconds.Value), 0, 1);
    }

    private static string BuildContinueTimeText(decimal? positionSeconds, decimal? durationSeconds)
    {
        if (positionSeconds is not > 0 && durationSeconds is not > 0)
        {
            return "Gereed om voort te luister";
        }

        if (durationSeconds is > 0)
        {
            return $"{FormatContinueTime(positionSeconds)} / {FormatContinueTime(durationSeconds)}";
        }

        return $"{FormatContinueTime(positionSeconds)} geluister";
    }

    private static string FormatContinueTime(decimal? seconds)
    {
        if (seconds is not > 0)
        {
            return "0:00";
        }

        var value = TimeSpan.FromSeconds((double)seconds.Value);
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    private IReadOnlyList<OfflineStoryDownload> ResolveVisibleDownloadedStories()
    {
        var downloads = _downloadedStories;
        var normalizedQuery = NormalizeSearchValue(_searchEntry.Text);
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            downloads = downloads
                .Where(download =>
                    ContainsNormalized(download.Title, normalizedQuery) ||
                    ContainsNormalized(download.Description, normalizedQuery) ||
                    ContainsNormalized(download.Slug, normalizedQuery) ||
                    ContainsNormalized(download.Source, normalizedQuery))
                .ToArray();
        }

        return downloads;
    }

    private View? BuildDownloadedSection(IReadOnlyList<OfflineStoryDownload> downloads)
    {
        if (downloads.Count == 0)
        {
            return null;
        }

        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label
                {
                    Text = "Afgelaai",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#222222"),
                    HorizontalOptions = LayoutOptions.Fill,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                BuildHorizontalCarousel(downloads, GetDownloadedCarouselHeight(), BuildDownloadedStoryCard)
            }
        };
    }

    private double GetDownloadedCarouselHeight()
    {
        if (!MobileResponsiveLayout.IsWide(Width))
        {
            return IsAndroid ? 252 : 286;
        }

        var cardWidth = MobileResponsiveLayout.ResolveDownloadedCardWidth(Width, IsAndroid);
        var coverHeight = Math.Clamp(cardWidth * 1.27, 228, 300);
        return coverHeight + 70;
    }

    private View BuildDownloadedStoryCard(OfflineStoryDownload download)
    {
        var story = ToMobileStorySummary(download);
        var wideLayout = MobileResponsiveLayout.IsWide(Width);
        var cardWidth = MobileResponsiveLayout.ResolveDownloadedCardWidth(Width, IsAndroid);
        var coverHeight = wideLayout
            ? Math.Clamp(cardWidth * 1.27, 228, 300)
            : IsAndroid ? 188 : 218;
        var coverGrid = new Grid
        {
            HeightRequest = coverHeight,
            Children =
            {
                new Image
                {
                    Source = BuildLuisterImageSource(
                        PageHelpers.ResolveStoryCardImageSource(story, _apiClient)),
                    Aspect = Aspect.AspectFill,
                    HeightRequest = coverHeight
                },
                BuildCoverPlayBadge("▶", 38, 17, 2)
            }
        };

        var card = new Border
        {
            WidthRequest = cardWidth,
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Margin = new Thickness(0, 0, 0, 10),
            Content = new VerticalStackLayout
            {
                Spacing = 9,
                Children =
                {
                    new Border
                    {
                        StrokeThickness = 0,
                        StrokeShape = BuildArtworkShape(16),
                        HeightRequest = coverHeight,
                        Content = coverGrid
                    },
                    new Label
                    {
                        Text = download.Title,
                        FontSize = 16,
                        TextColor = Color.FromArgb("#1B2231"),
                        InputTransparent = true,
                        MaxLines = 2,
                        LineBreakMode = LineBreakMode.TailTruncation,
                        LineHeight = 1.16
                    }
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenDownloadedStoryAsync(download);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private static View BuildInlineNotice(string message) =>
        new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 16,
            Content = new Label
            {
                Text = message,
                TextColor = Color.FromArgb("#5F5F5F")
            }
        };

    private static MobileStorySummary ToMobileStorySummary(OfflineStoryDownload download) =>
        new(
            Slug: download.Slug,
            Title: download.Title,
            Description: download.Description,
            ImageUrl: download.ImageUrl,
            ThumbnailUrl: download.ThumbnailUrl,
            Source: download.Source,
            IsLocked: false,
            IsFavorite: false,
            DetailUrl: download.DetailUrl,
            DurationSeconds: download.DurationSeconds);

    private static IReadOnlyList<MobileLuisterSection> BuildLegacySections(IReadOnlyList<MobilePlaylist> playlists) =>
        playlists
            .Select((playlist, index) => new MobileLuisterSection(
                Kind: "playlist",
                Title: playlist.Title,
                SortOrder: index,
                Playlist: playlist,
                Playlists: Array.Empty<MobilePlaylist>()))
            .ToArray();

    private static IEnumerable<MobileLuisterSection> FilterSections(IReadOnlyList<MobileLuisterSection> sections, string? query)
    {
        var normalizedQuery = NormalizeSearchValue(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return sections.Where(SectionHasContent);
        }

        return sections
            .Select(section =>
            {
                if (IsSpeellysteSection(section))
                {
                    var filteredPlaylists = FilterPlaylists(section.Playlists, normalizedQuery).ToArray();
                    return section with { Playlists = filteredPlaylists };
                }

                if (section.Playlist is null)
                {
                    return section;
                }

                return FilterPlaylist(section.Playlist, normalizedQuery) is { } playlist
                    ? section with { Playlist = playlist }
                    : section with { Playlist = null };
            })
            .Where(SectionHasContent);
    }

    private static IEnumerable<MobilePlaylist> FilterPlaylists(IReadOnlyList<MobilePlaylist> playlists, string normalizedQuery)
    {
        return playlists
            .Select(playlist => FilterPlaylist(playlist, normalizedQuery))
            .Where(playlist => playlist is not null)
            .Cast<MobilePlaylist>();
    }

    private static MobilePlaylist? FilterPlaylist(MobilePlaylist playlist, string normalizedQuery)
    {
        var playlistMatches =
            ContainsNormalized(playlist.Title, normalizedQuery) ||
            ContainsNormalized(playlist.Description, normalizedQuery) ||
            ContainsNormalized(playlist.Slug, normalizedQuery);
        var matchingStories = playlist.Stories
            .Where(story => StoryMatches(story, normalizedQuery))
            .ToArray();
        var showcaseMatches = playlist.ShowcaseStory is not null &&
            StoryMatches(playlist.ShowcaseStory, normalizedQuery);

        return playlistMatches || matchingStories.Length > 0 || showcaseMatches
            ? playlist with { Stories = playlistMatches ? playlist.Stories : matchingStories }
            : null;
    }

    private static bool StoryMatches(MobileStorySummary story, string normalizedQuery) =>
        ContainsNormalized(story.Title, normalizedQuery) ||
        ContainsNormalized(story.Description, normalizedQuery) ||
        ContainsNormalized(story.Slug, normalizedQuery) ||
        ContainsNormalized(story.Source, normalizedQuery);

    private static bool IsSpeellysteSection(MobileLuisterSection section) =>
        string.Equals(section.Kind, "speellyste", StringComparison.OrdinalIgnoreCase);

    private static bool SectionHasContent(MobileLuisterSection section) =>
        IsSpeellysteSection(section)
            ? section.Playlists.Count > 0
            : section.Playlist is not null;

    private static bool ContainsNormalized(string? value, string normalizedQuery) =>
        !string.IsNullOrWhiteSpace(value) &&
        NormalizeSearchValue(value).Contains(normalizedQuery, StringComparison.Ordinal);

    private static string NormalizeSearchValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private async Task OpenStoryAsync(MobileStorySummary story)
    {
        await _navigationGate.RunAsync(async () =>
        {
            if (story.IsLocked)
            {
                await OpenPlansAsync(BuildStoryReturnPath(story));
                return;
            }

            _playlistPlaybackState.Clear();
            await CapturePlayerTransitionBackdropAsync();
            await Shell.Current.GoToAsync(
                $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source=luister",
                animate: false,
                parameters: new Dictionary<string, object>
                {
                    ["preview"] = story
                });
        });
    }

    private async Task OpenDownloadedStoryAsync(OfflineStoryDownload download)
    {
        await _navigationGate.RunAsync(async () =>
        {
            _playlistPlaybackState.Clear();
            await CapturePlayerTransitionBackdropAsync();
            var story = ToMobileStorySummary(download);
            await Shell.Current.GoToAsync(
                $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(download.Slug)}&source={Uri.EscapeDataString(download.Source)}",
                animate: false,
                parameters: new Dictionary<string, object>
                {
                    ["preview"] = story
                });
        });
    }

    private void StartImageWarmup()
    {
        _imageWarmupCancellation?.Cancel();
        _imageWarmupCancellation?.Dispose();
        _imageWarmupCancellation = new CancellationTokenSource();
        var token = _imageWarmupCancellation.Token;
        var imageUrls = EnumeratePrioritizedLuisterImageUrls().ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                await _apiClient.CacheImagesAsync(
                    imageUrls,
                    token,
                    maxImages: IsAndroid ? 56 : 80,
                    maxDegreeOfParallelism: IsAndroid ? 3 : 4);
                if (token.IsCancellationRequested || !_isPageActive)
                {
                    return;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (token.IsCancellationRequested || !_isPageActive)
                    {
                        return;
                    }

                    _imageSourceCache.Clear();
                    RenderPlaylistContent();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Image warmup is best-effort; the remote image source remains available.
            }
        }, token);
    }

    private IEnumerable<string?> EnumeratePrioritizedLuisterImageUrls()
    {
        if (_continueListeningState.Current is { } continueListening)
        {
            var resolvedStory = ResolveContinueListeningStory(continueListening);
            yield return PageHelpers.ResolveStoryCardImageSource(
                resolvedStory?.Story ?? ToMobileStorySummary(continueListening),
                _apiClient);
        }

        foreach (var download in ResolveVisibleDownloadedStories().Take(8))
        {
            yield return PageHelpers.ResolveStoryCardImageSource(ToMobileStorySummary(download), _apiClient);
        }

        foreach (var section in FilterSections(_sections, _searchEntry.Text))
        {
            if (IsSpeellysteSection(section))
            {
                foreach (var playlist in section.Playlists.Take(8))
                {
                    yield return playlist.ArtworkUrl;
                    if (playlist.ShowcaseStory is not null)
                    {
                        yield return PageHelpers.ResolveStoryCardImageSource(playlist.ShowcaseStory, _apiClient);
                    }
                }

                continue;
            }

            if (section.Playlist is null)
            {
                continue;
            }

            yield return section.Playlist.ArtworkUrl;
            if (section.Playlist.ShowcaseStory is not null)
            {
                yield return PageHelpers.ResolveStoryCardImageSource(section.Playlist.ShowcaseStory, _apiClient);
            }

            foreach (var story in section.Playlist.Stories.Take(10))
            {
                yield return PageHelpers.ResolveStoryCardImageSource(story, _apiClient);
            }
        }

        foreach (var imageUrl in EnumerateLuisterImageUrls())
        {
            yield return imageUrl;
        }
    }

    private IEnumerable<string?> EnumerateLuisterImageUrls()
    {
        foreach (var section in _sections)
        {
            if (IsSpeellysteSection(section))
            {
                foreach (var playlist in section.Playlists)
                {
                    yield return playlist.ArtworkUrl;
                    if (playlist.ShowcaseStory is not null)
                    {
                        yield return PageHelpers.ResolveStoryCardImageSource(playlist.ShowcaseStory, _apiClient);
                    }

                    foreach (var story in playlist.Stories)
                    {
                        yield return PageHelpers.ResolveStoryCardImageSource(story, _apiClient);
                    }
                }

                continue;
            }

            if (section.Playlist is null)
            {
                continue;
            }

            yield return section.Playlist.ArtworkUrl;
            if (section.Playlist.ShowcaseStory is not null)
            {
                yield return PageHelpers.ResolveStoryCardImageSource(section.Playlist.ShowcaseStory, _apiClient);
            }

            foreach (var story in section.Playlist.Stories)
            {
                yield return PageHelpers.ResolveStoryCardImageSource(story, _apiClient);
            }
        }
    }

    private Task OpenPlaylistAsync(MobilePlaylist playlist)
    {
        var firstStory = playlist.Stories.FirstOrDefault();
        return firstStory is null
            ? Task.CompletedTask
            : OpenPlaylistStoryAsync(firstStory, playlist);
    }

    private async Task OpenContinueListeningAsync(ContinueListeningItem item)
    {
        await _navigationGate.RunAsync(async () =>
        {
            var resolvedStory = ResolveContinueListeningStory(item);
            if (resolvedStory.HasValue && resolvedStory.Value.Playlist is { } playlist)
            {
                await OpenPlaylistStoryCoreAsync(MergeContinueListeningMetadata(resolvedStory.Value.Story, item), playlist);
                return;
            }

            var story = resolvedStory is { } resolved
                ? MergeContinueListeningMetadata(resolved.Story, item)
                : ToMobileStorySummary(item);
            _playlistPlaybackState.Clear();
            await CapturePlayerTransitionBackdropAsync();
            await Shell.Current.GoToAsync(
                $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source={Uri.EscapeDataString(story.Source)}",
                animate: false,
                parameters: new Dictionary<string, object>
                {
                    ["preview"] = story
                });
        });
    }

    private static MobileStorySummary MergeContinueListeningMetadata(MobileStorySummary story, ContinueListeningItem item) =>
        story with
        {
            ImageUrl = string.IsNullOrWhiteSpace(story.ImageUrl) ? item.ImageUrl : story.ImageUrl,
            ThumbnailUrl = string.IsNullOrWhiteSpace(story.ThumbnailUrl) ? item.ThumbnailUrl : story.ThumbnailUrl,
            DurationSeconds = story.DurationSeconds is > 0 ? story.DurationSeconds : item.DurationSeconds
        };

    private async Task OpenPlaylistStoryAsync(MobileStorySummary story, MobilePlaylist playlist)
    {
        await _navigationGate.RunAsync(() => OpenPlaylistStoryCoreAsync(story, playlist));
    }

    private async Task OpenPlaylistStoryCoreAsync(MobileStorySummary story, MobilePlaylist playlist)
    {
        if (story.IsLocked)
        {
            await OpenPlansAsync(BuildStoryReturnPath(story));
            return;
        }

        _playlistPlaybackState.Set(playlist, story);
        await CapturePlayerTransitionBackdropAsync();
        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source=luister",
            animate: false,
            parameters: new Dictionary<string, object>
            {
                ["preview"] = story,
                ["playlistTitle"] = playlist.Title,
                ["playlistSlug"] = playlist.Slug
            });
    }

    private Task OpenPlansAsync(string? returnUrl = null)
    {
        var route = nameof(PlansPage);
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            route = $"{route}?returnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        return Shell.Current.GoToAsync(route, animate: true);
    }

    private static string BuildStoryReturnPath(MobileStorySummary story)
    {
        var source = string.Equals(story.Source, "gratis", StringComparison.OrdinalIgnoreCase)
            ? "gratis"
            : "luister";
        return $"/{source}/{Uri.EscapeDataString(story.Slug)}";
    }

    private async Task CapturePlayerTransitionBackdropAsync()
    {
        try
        {
            await _transitionBackdropState.CaptureAsync();
        }
        catch
        {
            // Transition backdrop capture should never block opening the player.
        }
    }

    private async Task ToggleFavoriteAsync(MobileStorySummary story)
    {
        if (!_sessionState.Current.IsSignedIn)
        {
            await DisplayAlertAsync("Teken in", "Teken eers in om gunstelinge te stoor.", "Reg so");
            return;
        }

        var favoriteKey = BuildFavoriteRequestKey(story);
        if (!_favoriteRequestsInFlight.Add(favoriteKey))
        {
            return;
        }

        var previousIsFavorite = story.IsFavorite;
        UpdateFavoriteState(story.Slug, !previousIsFavorite);
        RenderPlaylistContent();
        try
        {
            var isFavorite = await _apiClient.SetFavoriteAsync(story.Slug, story.Source, !previousIsFavorite);
            UpdateFavoriteState(story.Slug, isFavorite);
            RenderPlaylistContent();
        }
        catch (Exception ex)
        {
            UpdateFavoriteState(story.Slug, previousIsFavorite);
            RenderPlaylistContent();
            await DisplayAlertAsync("Kon nie stoor nie", ex.Message, "Reg so");
        }
        finally
        {
            _favoriteRequestsInFlight.Remove(favoriteKey);
            RenderPlaylistContent();
        }
    }

    private static string BuildFavoriteRequestKey(MobileStorySummary story) =>
        $"{story.Source}:{story.Slug}";

    private void UpdateFavoriteState(string slug, bool isFavorite)
    {
        _sections = _sections
            .Select(section => section with
            {
                Playlist = section.Playlist is null
                    ? null
                    : UpdatePlaylistFavoriteState(section.Playlist, slug, isFavorite),
                Playlists = section.Playlists
                    .Select(playlist => UpdatePlaylistFavoriteState(playlist, slug, isFavorite))
                    .ToArray()
            })
            .ToArray();
    }

    private IReadOnlyList<MobileLuisterSection> ApplyCurrentFavoriteState(IReadOnlyList<MobileLuisterSection> sections)
    {
        var favoriteSlugs = (_sessionState.Current.FavoriteStorySlugs ?? Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (favoriteSlugs.Count == 0)
        {
            return sections
                .Select(section => ApplyFavoriteSetToSection(section, favoriteSlugs))
                .ToArray();
        }

        return sections
            .Select(section => ApplyFavoriteSetToSection(section, favoriteSlugs))
            .ToArray();
    }

    private static MobileLuisterSection ApplyFavoriteSetToSection(
        MobileLuisterSection section,
        IReadOnlySet<string> favoriteSlugs) =>
        section with
        {
            Playlist = section.Playlist is null ? null : ApplyFavoriteSetToPlaylist(section.Playlist, favoriteSlugs),
            Playlists = section.Playlists
                .Select(playlist => ApplyFavoriteSetToPlaylist(playlist, favoriteSlugs))
                .ToArray()
        };

    private static MobilePlaylist ApplyFavoriteSetToPlaylist(
        MobilePlaylist playlist,
        IReadOnlySet<string> favoriteSlugs) =>
        playlist with
        {
            Stories = playlist.Stories
                .Select(story => story with { IsFavorite = favoriteSlugs.Contains(story.Slug) })
                .ToArray(),
            ShowcaseStory = playlist.ShowcaseStory is null
                ? null
                : playlist.ShowcaseStory with { IsFavorite = favoriteSlugs.Contains(playlist.ShowcaseStory.Slug) }
        };

    private static MobilePlaylist UpdatePlaylistFavoriteState(MobilePlaylist playlist, string slug, bool isFavorite) =>
        playlist with
        {
            Stories = playlist.Stories
                .Select(story => UpdateStoryFavoriteState(story, slug, isFavorite))
                .ToArray(),
            ShowcaseStory = playlist.ShowcaseStory is null ? null : UpdateStoryFavoriteState(playlist.ShowcaseStory, slug, isFavorite)
        };

    private static MobileStorySummary UpdateStoryFavoriteState(MobileStorySummary story, string slug, bool isFavorite) =>
        string.Equals(story.Slug, slug, StringComparison.OrdinalIgnoreCase)
            ? story with { IsFavorite = isFavorite }
            : story;

    private sealed record RankedLuisterStory(MobileStorySummary Story, int Rank);

    private enum OortjiesPeekSide
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private sealed record OortjiesPeekPlacement(
        double HiddenX,
        double HiddenY,
        double VisibleX,
        double VisibleY,
        double WiggleX,
        double WiggleY,
        double JumpX,
        double JumpY,
        double Rotation);

    private enum LuisterFeedItemKind
    {
        Loading,
        Search,
        Account,
        ContinueListening,
        Downloaded,
        Notice,
        PlaylistShowcase,
        PlaylistSection
    }

    private sealed record LuisterFeedItem(
        LuisterFeedItemKind Kind,
        string? Message = null,
        string Title = "",
        IReadOnlyList<MobilePlaylist>? PlaylistsValue = null,
        MobilePlaylist? Playlist = null,
        IReadOnlyList<OfflineStoryDownload>? DownloadsValue = null,
        MobileLuisterSection? Section = null)
    {
        public IReadOnlyList<MobilePlaylist> Playlists => PlaylistsValue ?? Array.Empty<MobilePlaylist>();

        public IReadOnlyList<OfflineStoryDownload> Downloads => DownloadsValue ?? Array.Empty<OfflineStoryDownload>();

        public static LuisterFeedItem Loading() => new(LuisterFeedItemKind.Loading);

        public static LuisterFeedItem Search() => new(LuisterFeedItemKind.Search);

        public static LuisterFeedItem Account() => new(LuisterFeedItemKind.Account);

        public static LuisterFeedItem ContinueListening() => new(LuisterFeedItemKind.ContinueListening);

        public static LuisterFeedItem Downloaded(IReadOnlyList<OfflineStoryDownload> downloads) =>
            new(LuisterFeedItemKind.Downloaded, DownloadsValue: downloads);

        public static LuisterFeedItem Notice(string message) => new(LuisterFeedItemKind.Notice, Message: message);

        public static LuisterFeedItem PlaylistShowcase(MobileLuisterSection section) =>
            new(
                LuisterFeedItemKind.PlaylistShowcase,
                Title: section.Title,
                PlaylistsValue: section.Playlists,
                Section: section);

        public static LuisterFeedItem PlaylistSection(MobilePlaylist playlist) =>
            new(LuisterFeedItemKind.PlaylistSection, Playlist: playlist);
    }

    private sealed class NotificationDownCaretDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = Color.FromArgb("#0B3534");
            canvas.StrokeSize = 3.4f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var centerX = dirtyRect.Center.X;
            var centerY = dirtyRect.Center.Y + dirtyRect.Height * 0.04f;
            var halfWidth = dirtyRect.Width * 0.26f;
            var halfHeight = dirtyRect.Height * 0.16f;

            canvas.DrawLine(centerX - halfWidth, centerY - halfHeight, centerX, centerY + halfHeight);
            canvas.DrawLine(centerX, centerY + halfHeight, centerX + halfWidth, centerY - halfHeight);
        }
    }
}
