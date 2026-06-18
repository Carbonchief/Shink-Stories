using System.Net;
using System.Globalization;
using System.Text;
using System.Threading;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class LuisterPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly IOfflineStoryDownloadService _offlineDownloadService;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly VerticalStackLayout _content;
    private readonly VerticalStackLayout _playlistContent;
    private readonly RefreshView _refreshView;
    private readonly Entry _searchEntry;
    private readonly Entry _loginEmailEntry;
    private readonly Entry _loginPasswordEntry;
    private readonly Label _loginStatusLabel;
    private IReadOnlyList<MobileLuisterSection> _sections = Array.Empty<MobileLuisterSection>();
    private IReadOnlyList<OfflineStoryDownload> _downloadedStories = Array.Empty<OfflineStoryDownload>();
    private MobileNotificationPage? _notificationPage;
    private string? _loadErrorMessage;
    private bool _hasLoaded;
    private bool _isPageActive;
    private bool _isSearchVisible;
    private bool _isRefreshingNotifications;
    private CancellationTokenSource? _imageWarmupCancellation;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _searchDebounceCancellation;
    private readonly HashSet<string> _favoriteRequestsInFlight = new(StringComparer.OrdinalIgnoreCase);

    public LuisterPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        IOfflineStoryDownloadService offlineDownloadService,
        PlaylistPlaybackState playlistPlaybackState,
        PlayerTransitionBackdropState transitionBackdropState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _offlineDownloadService = offlineDownloadService;
        _playlistPlaybackState = playlistPlaybackState;
        _transitionBackdropState = transitionBackdropState;
        Title = "Luister";
        BackgroundColor = Color.FromArgb("#FFF7E8");
        Shell.SetNavBarIsVisible(this, false);

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 18, 18, 28),
            Spacing = 16
        };
        _playlistContent = new VerticalStackLayout
        {
            Spacing = 14
        };

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

        _refreshView = new RefreshView
        {
            Content = new ScrollView { Content = _content },
            Command = new Command(() => _ = TriggerRefreshAsync())
        };
        Content = _refreshView;
        _offlineDownloadService.DownloadsChanged += (_, _) => _ = RefreshDownloadsInBackgroundAsync();

        _sessionState.Changed += session => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_sessionState.Current.IsSignedIn)
            {
                _notificationPage = null;
            }

            RenderContent();
            if (_sessionState.Current.IsSignedIn)
            {
                _ = RefreshNotificationsInBackgroundAsync();
            }
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        if (!_hasLoaded)
        {
            await LoadAsync(forceRefresh: true);
        }
        else
        {
            RenderContent();
            _ = RefreshDownloadsInBackgroundAsync();
            _ = RefreshSessionInBackgroundAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _loadCancellation?.Cancel();
        _imageWarmupCancellation?.Cancel();
        _searchDebounceCancellation?.Cancel();
        base.OnDisappearing();
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

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!_isPageActive || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _content.Children.Clear();
            _content.Children.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#0F766E") });
        });

        try
        {
            var downloadsTask = LoadPlayableDownloadsSafelyAsync(cancellationToken);
            var sessionTask = _apiClient.GetSessionAsync();
            var luisterTask = _apiClient.GetLuisterAsync();
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

                    _content.Children.Clear();
                    _content.Children.Add(new Label { Text = "Kon nie luister stories laai nie." });
                });
                return;
            }

            _sections = response.Sections is { Count: > 0 }
                ? response.Sections
                : BuildLegacySections(response.Playlists);
            _downloadedStories = await downloadsTask;
            _loadErrorMessage = null;
            _hasLoaded = true;
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
        IReadOnlyList<OfflineStoryDownload> downloads;
        try
        {
            downloads = await _offlineDownloadService.GetPlayableDownloadsAsync();
        }
        catch
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!_isPageActive)
            {
                return;
            }

            _downloadedStories = downloads;
            RenderContent();
        });
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
        try
        {
            await _apiClient.GetSessionAsync();
            MainThread.BeginInvokeOnMainThread(RenderContent);
            await RefreshNotificationsInBackgroundAsync();
        }
        catch
        {
            // Keep cached Luister content visible if session refresh is temporarily unavailable.
        }
    }

    private void RenderContent()
    {
        if (!_hasLoaded || !_isPageActive)
        {
            return;
        }

        _content.Children.Clear();
        _content.Children.Add(BuildLuisterTopBar());
        if (_isSearchVisible || !string.IsNullOrWhiteSpace(_searchEntry.Text))
        {
            _content.Children.Add(BuildSearchBox());
        }

        if (!_sessionState.Current.IsSignedIn)
        {
            _content.Children.Add(BuildAccountPanel());
        }
        _content.Children.Add(_playlistContent);

        RenderPlaylistContent();
    }

    private void RenderPlaylistContent()
    {
        if (!_isPageActive)
        {
            return;
        }

        _searchDebounceCancellation?.Cancel();
        _playlistContent.Children.Clear();
        var downloadedSection = BuildDownloadedSection();
        if (downloadedSection is not null)
        {
            _playlistContent.Children.Add(downloadedSection);
        }

        var filteredSections = FilterSections(_sections, _searchEntry.Text).ToArray();
        if (filteredSections.Length == 0)
        {
            if (downloadedSection is not null)
            {
                if (!string.IsNullOrWhiteSpace(_loadErrorMessage))
                {
                    _playlistContent.Children.Add(BuildInlineNotice("Jy is offline. Jou afgelaaide stories is beskikbaar."));
                }

                return;
            }

            _playlistContent.Children.Add(new Border
            {
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Padding = 16,
                Content = new Label
                {
                    Text = string.IsNullOrWhiteSpace(_loadErrorMessage)
                        ? "Geen stories pas by jou soektog nie."
                        : _loadErrorMessage,
                    TextColor = Color.FromArgb("#5F5F5F")
                }
            });
            return;
        }

        foreach (var section in filteredSections)
        {
            if (IsSpeellysteSection(section))
            {
                _playlistContent.Children.Add(BuildPlaylistShowcase(section.Title, section.Playlists));
                continue;
            }

            if (section.Playlist is not null)
            {
                _playlistContent.Children.Add(BuildPlaylistSection(section.Playlist));
            }
        }
    }

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

            MainThread.BeginInvokeOnMainThread(RenderPlaylistContent);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private View BuildLuisterTopBar()
    {
        var menuButton = BuildMenuCircleButton(Colors.White, Color.FromArgb("#123F3F"));
        var menuTap = new TapGestureRecognizer();
        menuTap.Tapped += async (_, _) => await ShowMenuAsync();
        menuButton.GestureRecognizers.Add(menuTap);

        var searchButton = BuildHeaderCircleButton("⌕", 25, Color.FromArgb("#0B3534"), Color.FromArgb("#F4E9D1"));
        var searchTap = new TapGestureRecognizer();
        searchTap.Tapped += (_, _) =>
        {
            _isSearchVisible = !_isSearchVisible;
            RenderContent();
            if (_isSearchVisible)
            {
                MainThread.BeginInvokeOnMainThread(() => _searchEntry.Focus());
            }
        };
        searchButton.GestureRecognizers.Add(searchTap);

        var notificationButton = BuildNotificationButton();
        var notificationTap = new TapGestureRecognizer();
        notificationTap.Tapped += async (_, _) => await ShowNotificationsAsync();
        notificationButton.GestureRecognizers.Add(notificationTap);

        var profileButton = BuildProfileButton();
        var profileTap = new TapGestureRecognizer();
        profileTap.Tapped += (_, _) => OpenAccountTab();
        profileButton.GestureRecognizers.Add(profileTap);

        var rightActions = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.End,
            Children =
            {
                searchButton,
                notificationButton,
                profileButton
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
            Children =
            {
                menuButton,
                rightActions
            }
        };

        Grid.SetColumn(rightActions, 2);
        return grid;
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
                Margin = text == "⌕" ? new Thickness(0, -2, 0, 0) : Thickness.Zero
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
        container.Children.Add(BuildHeaderCircleButton("🔔", 20, Color.FromArgb("#0B3534"), Color.FromArgb("#F4E9D1")));

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
                Source = _apiClient.BuildCachedImageSource(session.ProfileImageUrl),
                Aspect = Aspect.AspectFill,
                WidthRequest = 46,
                HeightRequest = 46
            }
        };
    }

    private async Task ShowMenuAsync()
    {
        var choice = await DisplayActionSheetAsync("Menu", "Kanselleer", null, "Settings", "Manage Account");
        switch (choice)
        {
            case "Settings":
                await DisplayAlertAsync("Settings", "Instellings kom binnekort.", "Reg so");
                break;
            case "Manage Account":
                OpenAccountTab();
                break;
        }
    }

    private async Task RefreshNotificationsInBackgroundAsync()
    {
        if (!_sessionState.Current.IsSignedIn || _isRefreshingNotifications)
        {
            return;
        }

        _isRefreshingNotifications = true;
        try
        {
            _notificationPage = await _apiClient.GetNotificationsAsync();
            MainThread.BeginInvokeOnMainThread(RenderContent);
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
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            Content = modalLayout
        };

        clearButton.Clicked += async (_, _) =>
            await ClearNotificationsAsync(list, countLabel, statusLabel, clearButton, loadMoreButton);
        loadMoreButton.Clicked += async (_, _) =>
            await LoadMoreNotificationsAsync(list, countLabel, statusLabel, clearButton, loadMoreButton);

        await Navigation.PushModalAsync(modal, true);
        await LoadNotificationsAsync(list, countLabel, statusLabel, clearButton, loadMoreButton);
    }

    private async Task LoadNotificationsAsync(
        VerticalStackLayout list,
        Label countLabel,
        Label statusLabel,
        Button clearButton,
        Button loadMoreButton)
    {
        SetNotificationControlsBusy(statusLabel, clearButton, loadMoreButton, "Laai kennisgewings...");

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
            statusLabel.IsVisible = true;
            statusLabel.Text = "Ons kon nie nou die kennisgewings laai nie.";
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
                Source = _apiClient.BuildCachedImageSource(notification.ImagePath, "schink_background.jpeg"),
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

    private static void OpenAccountTab()
    {
        if (Shell.Current?.CurrentItem is not TabBar tabs)
        {
            return;
        }

        var accountTab = tabs.Items.FirstOrDefault(item =>
            string.Equals(item.Title, "Rekening", StringComparison.OrdinalIgnoreCase));
        if (accountTab is not null)
        {
            tabs.CurrentItem = accountTab;
        }
    }

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
            var plansUrl = string.IsNullOrWhiteSpace(_sessionState.Current.PlansUrl)
                ? _apiClient.BuildAbsoluteUrl("/opsies")
                : _sessionState.Current.PlansUrl;
            await Browser.OpenAsync(plansUrl, BrowserLaunchMode.External);
        };

        return new Border
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
        section.Children.Add(PageHelpers.BuildSectionTitle(string.IsNullOrWhiteSpace(title) ? "Speellyste" : title));

        section.Children.Add(BuildHorizontalCarousel(
            playlists,
            186,
            playlist => BuildPlaylistCard(playlist)));

        return section;
    }

    private View BuildPlaylistCard(MobilePlaylist playlist)
    {
        var imageSource = _apiClient.BuildCachedImageSource(playlist.ArtworkUrl, "schink_background.jpeg");
        var card = new Border
        {
            WidthRequest = 246,
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
                        StrokeShape = new RoundRectangle { CornerRadius = 16 },
                        HeightRequest = 138,
                        Content = new Grid
                        {
                            Children =
                            {
                                new Image
                                {
                                    Source = imageSource,
                                    HeightRequest = 138,
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
                TextColor = Color.FromArgb("#5F5F5F")
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
                286,
                story => BuildLuisterStoryCarouselCard(playlist, story)));

        return section;
    }

    private View BuildPlaylistShowcaseStory(MobilePlaylist playlist, MobileStorySummary story)
    {
        var cover = new Border
        {
            Stroke = Color.FromArgb("#AA0F766E"),
            StrokeThickness = 3,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HeightRequest = 320,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 12),
                Radius = 26,
                Opacity = 0.22f
            },
            Content = new Grid
            {
                Children =
                {
                    new Image
                    {
                        Source = _apiClient.BuildCachedImageSource(
                            PageHelpers.ResolveStoryCardImageSource(story, _apiClient)),
                        Aspect = Aspect.AspectFill
                    },
                    BuildLockedBadge(story),
                    BuildFavoriteOverlay(story),
                    BuildCoverPlayBadge("▶", 52, 22, 3)
                }
            }
        };
        cover.SizeChanged += (_, _) =>
        {
            if (cover.Width > 0)
            {
                cover.HeightRequest = Math.Min(Math.Max(cover.Width, 248), 360);
            }
        };

        var showcase = new VerticalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(0, 2, 0, 6),
            Children =
            {
                cover,
                new Label
                {
                    Text = string.IsNullOrWhiteSpace(story.Title) ? playlist.Title : story.Title,
                    FontSize = 17,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1B2231"),
                    InputTransparent = true,
                    HorizontalTextAlignment = TextAlignment.Center,
                    MaxLines = 2,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    LineHeight = 1.2
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenPlaylistStoryAsync(story, playlist);
        showcase.GestureRecognizers.Add(tap);
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
            286,
            rankedStory => BuildLuisterStoryCarouselCard(playlist, rankedStory.Story, rankedStory.Rank));
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
            {
                var host = new ContentView();
                host.BindingContextChanged += (_, _) =>
                {
                    if (host.BindingContext is T item)
                    {
                        host.Content = buildItem(item);
                    }
                };
                return host;
            })
        };

        return carousel;
    }

    private View BuildLuisterStoryCarouselCard(MobilePlaylist playlist, MobileStorySummary story, int? rank = null)
    {
        var coverGrid = new Grid
        {
            HeightRequest = 218,
            Children =
            {
                new Image
                {
                    Source = _apiClient.BuildCachedImageSource(
                        PageHelpers.ResolveStoryCardImageSource(story, _apiClient)),
                    Aspect = Aspect.AspectFill,
                    HeightRequest = 218
                },
                BuildLockedBadge(story),
                BuildFavoriteOverlay(story),
                BuildCoverPlayBadge("▶", 38, 17, 2)
            }
        };
        if (rank is not null)
        {
            coverGrid.Children.Add(BuildStoryRankBadge(rank.Value));
        }

        var cover = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HeightRequest = 218,
            Content = coverGrid
        };

        var card = new Border
        {
            WidthRequest = 168,
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            Margin = new Thickness(0, 0, 0, 10),
            Content = new VerticalStackLayout
            {
                Spacing = 9,
                Children =
                {
                    cover,
                    new Label
                    {
                        Text = story.Title,
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
        tap.Tapped += async (_, _) => await OpenPlaylistStoryAsync(story, playlist);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private static View BuildStoryRankBadge(int rank) =>
        new Label
        {
            Text = rank.ToString(CultureInfo.InvariantCulture),
            TextColor = Color.FromArgb("#FFFEF8"),
            FontSize = 46,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(8, 2, 0, 0),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 4),
                Radius = 10,
                Opacity = 0.24f
            }
        };

    private static View BuildLockedBadge(MobileStorySummary story) =>
        new Border
        {
            IsVisible = story.IsLocked,
            BackgroundColor = Color.FromArgb("#D9222222"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            Padding = new Thickness(9, 4),
            Margin = new Thickness(10),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = "Gesluit",
                TextColor = Colors.White,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold
            }
        };

    private View BuildFavoriteOverlay(MobileStorySummary story)
    {
        var isFavoriteRequestInFlight = IsFavoriteRequestInFlight(story);
        var heart = new Label
        {
            Text = story.IsFavorite ? "♥" : "♡",
            TextColor = story.IsFavorite ? Color.FromArgb("#E11D48") : Color.FromArgb("#8A938D"),
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 2),
                Radius = 7,
                Opacity = 0.88f
            },
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        var indicator = new ActivityIndicator
        {
            IsRunning = true,
            Color = story.IsFavorite ? Color.FromArgb("#E11D48") : Color.FromArgb("#8A938D"),
            WidthRequest = 24,
            HeightRequest = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var target = new Grid
        {
            WidthRequest = 44,
            HeightRequest = 44,
            Margin = new Thickness(0, 6, 6, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                isFavoriteRequestInFlight ? indicator : heart
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ToggleFavoriteAsync(story);
        target.GestureRecognizers.Add(tap);
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

    private View? BuildDownloadedSection()
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
                BuildHorizontalCarousel(downloads, 286, BuildDownloadedStoryCard)
            }
        };
    }

    private View BuildDownloadedStoryCard(OfflineStoryDownload download)
    {
        var story = ToMobileStorySummary(download);
        var coverGrid = new Grid
        {
            HeightRequest = 218,
            Children =
            {
                new Image
                {
                    Source = _apiClient.BuildCachedImageSource(
                        PageHelpers.ResolveStoryCardImageSource(story, _apiClient)),
                    Aspect = Aspect.AspectFill,
                    HeightRequest = 218
                },
                BuildCoverPlayBadge("▶", 38, 17, 2)
            }
        };

        var card = new Border
        {
            WidthRequest = 168,
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
                        StrokeShape = new RoundRectangle { CornerRadius = 16 },
                        HeightRequest = 218,
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
        _playlistPlaybackState.Clear();
        await CapturePlayerTransitionBackdropAsync();
        await Shell.Current.GoToAsync(
            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(story.Slug)}&source=luister",
            animate: false,
            parameters: new Dictionary<string, object>
            {
                ["preview"] = story
            });
    }

    private async Task OpenDownloadedStoryAsync(OfflineStoryDownload download)
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
    }

    private void StartImageWarmup()
    {
        _imageWarmupCancellation?.Cancel();
        _imageWarmupCancellation?.Dispose();
        _imageWarmupCancellation = new CancellationTokenSource();
        var token = _imageWarmupCancellation.Token;
        var imageUrls = EnumerateLuisterImageUrls().ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                await _apiClient.CacheImagesAsync(imageUrls, token);
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

    private async Task OpenPlaylistStoryAsync(MobileStorySummary story, MobilePlaylist playlist)
    {
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

        RenderPlaylistContent();
        try
        {
            var isFavorite = await _apiClient.SetFavoriteAsync(story.Slug, story.Source, !story.IsFavorite);
            UpdateFavoriteState(story.Slug, isFavorite);
            RenderPlaylistContent();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Kon nie stoor nie", ex.Message, "Reg so");
        }
        finally
        {
            _favoriteRequestsInFlight.Remove(favoriteKey);
            RenderPlaylistContent();
        }
    }

    private bool IsFavoriteRequestInFlight(MobileStorySummary story) =>
        _favoriteRequestsInFlight.Contains(BuildFavoriteRequestKey(story));

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
