using System.Globalization;
using System.Net;
using Shink.Mobile.Models;
using Shink.Mobile.Navigation;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class KennisgewingsPage : ContentPage
{
    private static readonly Color PageBackgroundColor = Color.FromArgb("#FFF7E8");
    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly MobileAnalyticsService _analytics;
    private readonly PlaylistPlaybackState _playlistPlaybackState;
    private readonly PlayerTransitionBackdropState _transitionBackdropState;
    private readonly NavigationGate _navigationGate = new();
    private readonly VerticalStackLayout _list;
    private readonly Label _countLabel;
    private readonly Label _statusLabel;
    private readonly Button _clearButton;
    private readonly Button _loadMoreButton;
    private MobileNotificationPage? _notificationPage;
    private CancellationTokenSource? _loadCancellation;
    private bool _isPageActive;
    private bool _isClosing;

    public KennisgewingsPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics,
        PlaylistPlaybackState playlistPlaybackState,
        PlayerTransitionBackdropState transitionBackdropState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _analytics = analytics;
        _playlistPlaybackState = playlistPlaybackState;
        _transitionBackdropState = transitionBackdropState;

        Title = "Kennisgewings";
        BackgroundColor = PageBackgroundColor;
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        var titleLabel = new Label
        {
            Text = "Kennisgewings",
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0B3534"),
            VerticalTextAlignment = TextAlignment.Center
        };
        _countLabel = new Label
        {
            FontSize = 13,
            TextColor = Color.FromArgb("#6B7280")
        };
        _statusLabel = new Label
        {
            Text = "Laai kennisgewings...",
            FontSize = 14,
            TextColor = Color.FromArgb("#6B7280"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        _list = new VerticalStackLayout { Spacing = 10 };
        _clearButton = new Button
        {
            Text = "Maak skoon",
            BackgroundColor = Color.FromArgb("#F4E9D1"),
            TextColor = Color.FromArgb("#0B3534"),
            CornerRadius = 16,
            HeightRequest = 42,
            Padding = new Thickness(14, 0)
        };
        _loadMoreButton = new Button
        {
            Text = "Wys vorige kennisgewings",
            BackgroundColor = Color.FromArgb("#123F3F"),
            TextColor = Colors.White,
            CornerRadius = 16,
            HeightRequest = 48,
            IsVisible = false
        };
        var closeButton = BuildNotificationCloseButton();

        var titleStack = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { titleLabel, _countLabel }
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
                _clearButton
            }
        };
        Grid.SetColumn(titleStack, 1);
        Grid.SetColumn(_clearButton, 2);

        var notificationScrollView = new ScrollView
        {
            Content = _list,
            VerticalOptions = LayoutOptions.Fill
        };
        var pageLayout = new Grid
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
                _statusLabel,
                notificationScrollView,
                _loadMoreButton
            }
        };
        Grid.SetRow(_statusLabel, 1);
        Grid.SetRow(notificationScrollView, 2);
        Grid.SetRow(_loadMoreButton, 3);

        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += async (_, _) => await ClosePageAsync();
        closeButton.GestureRecognizers.Add(closeTap);
        _clearButton.Clicked += async (_, _) => await ClearNotificationsAsync();
        _loadMoreButton.Clicked += async (_, _) => await LoadMoreNotificationsAsync();

        Content = pageLayout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_isPageActive)
        {
            return;
        }

        _isPageActive = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;

        if (!_sessionState.Current.IsSignedIn)
        {
            try
            {
                await DisplayAlertAsync("Kennisgewings", "Teken in om kennisgewings te sien.", "Reg so");
            }
            catch (Exception ex)
            {
                _analytics.TrackException(ex, "mobile_notifications_auth_alert_failed");
            }

            await ClosePageAsync();
            return;
        }

        await LoadAsync(cancellationToken);
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _loadCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var renderedCachedNotifications = false;
        try
        {
            renderedCachedNotifications = await TryRenderCachedNotificationsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // A cached response is optional; the network request below can still succeed.
        }

        if (!IsNotificationPageActive(cancellationToken))
        {
            return;
        }

        if (!renderedCachedNotifications)
        {
            SetNotificationControlsBusy("Laai kennisgewings...");
        }

        await LoadNotificationsAsync(cancellationToken, renderedCachedNotifications);
    }

    private async Task<bool> ClosePageAsync()
    {
        if (_isClosing)
        {
            return false;
        }

        _isClosing = true;
        _loadCancellation?.Cancel();
        try
        {
            await _navigationGate.RunAsync(() => Shell.Current.GoToAsync("..", animate: false));
            return true;
        }
        catch (Exception ex)
        {
            _analytics.TrackException(ex, "mobile_notifications_close_failed");
            _isClosing = false;
            return false;
        }
    }

    private bool IsNotificationPageActive(CancellationToken cancellationToken) =>
        _isPageActive &&
        !_isClosing &&
        !cancellationToken.IsCancellationRequested &&
        ReferenceEquals(Shell.Current.CurrentPage, this);

    private async Task<bool> TryRenderCachedNotificationsAsync(CancellationToken cancellationToken)
    {
        var cachedPage = _notificationPage ?? await _apiClient.GetCachedNotificationsAsync(cancellationToken);
        if (cachedPage is null || !IsNotificationPageActive(cancellationToken))
        {
            return false;
        }

        _notificationPage = cachedPage;
        RenderNotificationPageState(cancellationToken);
        return true;
    }

    private async Task LoadNotificationsAsync(
        CancellationToken cancellationToken,
        bool hasRenderedCachedNotifications = false)
    {
        if (!IsNotificationPageActive(cancellationToken))
        {
            return;
        }

        if (!hasRenderedCachedNotifications)
        {
            SetNotificationControlsBusy("Laai kennisgewings...");
        }

        try
        {
            var loadedPage = await _apiClient.GetNotificationsAsync(cancellationToken: cancellationToken);
            if (!IsNotificationPageActive(cancellationToken))
            {
                return;
            }

            _notificationPage = loadedPage;
            RenderNotificationPageState(cancellationToken);

            if (_notificationPage?.UnreadCount > 0)
            {
                MarkAllNotificationsReadLocally();
                RenderNotificationPageState(cancellationToken);
                _ = _apiClient.SaveNotificationsCacheAsync(_notificationPage);
                _ = TryMarkAllNotificationsReadAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _notificationPage = null;
            if (IsNotificationPageActive(cancellationToken))
            {
                _statusLabel.IsVisible = true;
                _statusLabel.Text = "Teken in om kennisgewings te sien.";
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("status 401", StringComparison.OrdinalIgnoreCase))
        {
            _notificationPage = null;
            if (IsNotificationPageActive(cancellationToken))
            {
                _statusLabel.IsVisible = true;
                _statusLabel.Text = "Teken in om kennisgewings te sien.";
            }
        }
        catch
        {
            if (!hasRenderedCachedNotifications && IsNotificationPageActive(cancellationToken))
            {
                _statusLabel.IsVisible = true;
                _statusLabel.Text = "Ons kon nie nou die kennisgewings laai nie.";
            }
        }
        finally
        {
            if (IsNotificationPageActive(cancellationToken))
            {
                _clearButton.IsEnabled = true;
                _loadMoreButton.IsEnabled = true;
            }
        }
    }

    private async Task LoadMoreNotificationsAsync()
    {
        var cancellationToken = _loadCancellation?.Token ?? default;
        if (!IsNotificationPageActive(cancellationToken))
        {
            return;
        }

        var currentPage = _notificationPage;
        if (currentPage is null)
        {
            return;
        }

        var before = currentPage.Notifications.LastOrDefault()?.CreatedAt;
        if (before is null && !currentPage.HasHistory)
        {
            return;
        }

        SetNotificationControlsBusy("Laai vorige kennisgewings...");
        try
        {
            var loadedPage = await _apiClient.GetNotificationsAsync(
                before: before,
                history: currentPage.HasHistory,
                cancellationToken: cancellationToken);
            if (loadedPage is not null && IsNotificationPageActive(cancellationToken))
            {
                _notificationPage = MergeNotificationPages(currentPage, loadedPage);
                RenderNotificationPageState(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (IsNotificationPageActive(cancellationToken))
            {
                _statusLabel.IsVisible = true;
                _statusLabel.Text = "Ons kon nie die vorige kennisgewings laai nie.";
            }
        }
        finally
        {
            if (IsNotificationPageActive(cancellationToken))
            {
                _clearButton.IsEnabled = true;
                _loadMoreButton.IsEnabled = true;
            }
        }
    }

    private async Task ClearNotificationsAsync()
    {
        var cancellationToken = _loadCancellation?.Token ?? default;
        if (!IsNotificationPageActive(cancellationToken) ||
            _notificationPage?.Notifications.Count > 0 != true)
        {
            return;
        }

        _clearButton.IsEnabled = false;
        try
        {
            await _apiClient.ClearNotificationsAsync(cancellationToken);
            if (!IsNotificationPageActive(cancellationToken) || _notificationPage is null)
            {
                return;
            }

            _notificationPage = _notificationPage with
            {
                Count = 0,
                UnreadCount = 0,
                HasMore = false,
                HasHistory = false,
                Notifications = Array.Empty<MobileNotificationItem>()
            };
            _ = _apiClient.SaveNotificationsCacheAsync(_notificationPage);
            RenderNotificationPageState(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (IsNotificationPageActive(cancellationToken))
            {
                _statusLabel.IsVisible = true;
                _statusLabel.Text = "Ons kon nie die kennisgewings skoonmaak nie.";
            }
        }
        finally
        {
            if (IsNotificationPageActive(cancellationToken))
            {
                _clearButton.IsEnabled = true;
            }
        }
    }

    private void SetNotificationControlsBusy(string message)
    {
        _statusLabel.IsVisible = true;
        _statusLabel.Text = message;
        _clearButton.IsEnabled = false;
        _loadMoreButton.IsEnabled = false;
    }

    private void RenderNotificationPageState(CancellationToken cancellationToken)
    {
        if (!IsNotificationPageActive(cancellationToken))
        {
            return;
        }

        var page = _notificationPage;
        var notifications = page?.Notifications ?? Array.Empty<MobileNotificationItem>();
        _list.Children.Clear();

        _countLabel.Text = page?.UnreadCount > 0
            ? $"{page.UnreadCount} ongelees"
            : "Geen ongelees";
        _clearButton.IsVisible = notifications.Count > 0;
        _loadMoreButton.IsVisible = page is not null && (page.HasMore || page.HasHistory);

        if (notifications.Count == 0)
        {
            _statusLabel.IsVisible = true;
            _statusLabel.Text = "Geen kennisgewings nog nie.";
            return;
        }

        _statusLabel.IsVisible = false;
        foreach (var notification in notifications)
        {
            _list.Children.Add(BuildNotificationItem(notification, cancellationToken));
        }
    }

    private View BuildNotificationItem(MobileNotificationItem notification, CancellationToken cancellationToken)
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
            if (isClearing || !IsNotificationPageActive(cancellationToken))
            {
                return;
            }

            isClearing = true;
            clearItemButton.IsEnabled = false;
            try
            {
                await _apiClient.ClearNotificationAsync(notification.Id, cancellationToken);
                if (!IsNotificationPageActive(cancellationToken))
                {
                    return;
                }

                RemoveNotificationLocally(notification.Id);
                if (_notificationPage is not null)
                {
                    _ = _apiClient.SaveNotificationsCacheAsync(_notificationPage);
                }

                RenderNotificationPageState(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch
            {
                if (IsNotificationPageActive(cancellationToken))
                {
                    _statusLabel.IsVisible = true;
                    _statusLabel.Text = "Ons kon nie dié kennisgewing verwyder nie.";
                }
            }
            finally
            {
                isClearing = false;
                if (IsNotificationPageActive(cancellationToken))
                {
                    clearItemButton.IsEnabled = true;
                }
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
            Content = new ProgressiveCachedImage(
                _apiClient,
                new ProgressiveImageRequest(
                    notification.ImagePath,
                    FallbackFile: "schink_background.jpeg"))
            {
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
            try
            {
                if (!IsNotificationPageActive(cancellationToken))
                {
                    return;
                }

                await OpenNotificationAsync(notification);
                if (IsNotificationPageActive(cancellationToken))
                {
                    RenderNotificationPageState(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _analytics.TrackException(ex, "mobile_notification_tap_failed");
            }
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
        MarkNotificationReadLocally(notification.Id);
        if (_notificationPage is not null)
        {
            _ = _apiClient.SaveNotificationsCacheAsync(_notificationPage);
        }

        // Reading a notification is a local UI action first. Persist the read state
        // independently so a slow/offline mutation can never block its destination.
        var markReadTask = TryMarkNotificationReadAsync(notification.Id);
        var target = await ResolveNotificationTargetAsync(notification);
        try
        {
            switch (target.Kind)
            {
                case MobileNotificationNavigationKind.Story:
                    if (!await ClosePageAsync())
                    {
                        break;
                    }

                    await _navigationGate.RunAsync(async () =>
                    {
                        if (string.IsNullOrWhiteSpace(target.Value))
                        {
                            await Shell.Current.GoToAsync("//Luister", animate: false);
                            return;
                        }

                        _playlistPlaybackState.Clear();
                        await CapturePlayerTransitionBackdropAsync();
                        await Shell.Current.GoToAsync(
                            $"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(target.Value)}&source={Uri.EscapeDataString(target.Source ?? "luister")}",
                            animate: false);
                    });
                    break;
                case MobileNotificationNavigationKind.Character:
                    await _navigationGate.RunAsync(async () =>
                    {
                        await Shell.Current.GoToAsync("//Karakters", animate: false);
                        if (string.IsNullOrWhiteSpace(target.Value) ||
                            Shell.Current.CurrentPage is not KaraktersPage karaktersPage)
                        {
                            return;
                        }

                        await karaktersPage.OpenCharacterFromNotificationAsync(target.Value);
                    });
                    break;
                case MobileNotificationNavigationKind.ResourceWebsite when !string.IsNullOrWhiteSpace(target.Value):
                    await Browser.OpenAsync(_apiClient.BuildAbsoluteUrl(target.Value), BrowserLaunchMode.External);
                    break;
            }
        }
        catch
        {
            if (_isPageActive)
            {
                await DisplayAlertAsync(
                    "Kon nie oopmaak nie",
                    "Dié kennisgewing kon nie nou oopmaak nie. Probeer asseblief weer.",
                    "Reg so");
            }
        }

        _ = markReadTask;
    }

    private async Task<MobileNotificationNavigationTarget> ResolveNotificationTargetAsync(
        MobileNotificationItem notification)
    {
        var target = MobileNotificationNavigation.Resolve(notification.Type, notification.Href);
        if (target.Kind != MobileNotificationNavigationKind.Character ||
            !string.IsNullOrWhiteSpace(target.Value))
        {
            return target;
        }

        // Older character-unlock rows only stored /karakters as their href. Use the
        // character catalog to recover the exact slug from the notification body so
        // those existing notifications still open the right profile.
        try
        {
            var cancellationToken = _loadCancellation?.Token ?? default;
            var characters = await _apiClient.GetCachedCharactersAsync(cancellationToken)
                ?? await _apiClient.GetCharactersAsync(cancellationToken);
            var character = characters?.Characters.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(candidate.DisplayName) &&
                notification.Body.StartsWith(candidate.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase));
            return character is null ? target : target with { Value = character.Slug };
        }
        catch (OperationCanceledException) when (_loadCancellation?.IsCancellationRequested == true)
        {
            return target;
        }
        catch
        {
            return target;
        }
    }

    private async Task CapturePlayerTransitionBackdropAsync()
    {
        try
        {
            await _transitionBackdropState.CaptureAsync();
        }
        catch
        {
            // A transition backdrop is optional and must never block notification routing.
        }
    }

    private async Task TryMarkAllNotificationsReadAsync()
    {
        try
        {
            await _apiClient.MarkAllNotificationsReadAsync();
        }
        catch
        {
            // Local read state and navigation must remain usable while offline.
        }
    }

    private async Task TryMarkNotificationReadAsync(Guid notificationId)
    {
        try
        {
            await _apiClient.MarkNotificationReadAsync(notificationId);
        }
        catch
        {
            // The next refresh can reconcile server state; never block the tap action.
        }
    }

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
