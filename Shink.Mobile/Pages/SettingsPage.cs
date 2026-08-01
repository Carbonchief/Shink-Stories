using Microsoft.Maui.ApplicationModel;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class SettingsPage : ContentPage
{
    private static readonly Color PageBackgroundColor = Color.FromArgb("#FFF7E8");
    private static readonly Color TextColor = Color.FromArgb("#1B2231");
    private static readonly Color MutedTextColor = Color.FromArgb("#69716D");
    private static readonly Color AccentColor = Color.FromArgb("#123F3F");
    private static readonly Color GoldColor = Color.FromArgb("#FFD45A");
    private static readonly Color BorderColor = Color.FromArgb("#E8DDC8");
    private static readonly Color DestructiveColor = Color.FromArgb("#8F3B3B");

    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly IOfflineStoryDownloadService _offlineDownloadService;
    private readonly VerticalStackLayout _content;
    private readonly NavigationGate _navigationGate = new();
    private Label? _downloadSummaryLabel;
    private bool _isPageActive;
    private bool _isSessionStateSubscribed;
    private bool _isRefreshing;

    public SettingsPage(
        MobileApiClient apiClient,
        SessionState sessionState,
        IOfflineStoryDownloadService offlineDownloadService)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        _offlineDownloadService = offlineDownloadService;

        Title = "Instellings";
        BackgroundColor = PageBackgroundColor;
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _content = new VerticalStackLayout
        {
            Padding = new Thickness(20, 18, 20, 34),
            Spacing = 18
        };
        MobileResponsiveLayout.ApplyCenteredContent(_content, Width, 760);
        SizeChanged += (_, _) => MobileResponsiveLayout.ApplyCenteredContent(_content, Width, 760);

        Content = new ScrollView
        {
            BackgroundColor = PageBackgroundColor,
            Content = _content
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        SubscribeToSessionState();
        Render(_sessionState.Current);

        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await _apiClient.GetSessionAsync(timeout.Token);
            await RefreshDownloadSummaryAsync(timeout.Token);
        }
        catch
        {
            // Cached session details keep settings useful when the connection is unavailable.
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        UnsubscribeFromSessionState();
        base.OnDisappearing();
    }

    private void SubscribeToSessionState()
    {
        if (_isSessionStateSubscribed)
        {
            return;
        }

        _sessionState.Changed += OnSessionStateChanged;
        _isSessionStateSubscribed = true;
    }

    private void UnsubscribeFromSessionState()
    {
        if (!_isSessionStateSubscribed)
        {
            return;
        }

        _sessionState.Changed -= OnSessionStateChanged;
        _isSessionStateSubscribed = false;
    }

    private void OnSessionStateChanged(MobileSession session)
    {
        if (!_isPageActive)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isPageActive)
            {
                Render(session);
            }
        });
    }

    private void Render(MobileSession session)
    {
        _content.Children.Clear();
        _downloadSummaryLabel = null;

        _content.Children.Add(MobileTopBar.Build(
            this,
            _apiClient,
            session,
            new Thickness(0, 0, 0, 2),
            "back"));
        _content.Children.Add(BuildAccountSummary(session));

        _content.Children.Add(BuildSection(
            "Rekening",
            BuildSettingsRow(
                "◎",
                "Profiel",
                "Wysig jou naam en kontakbesonderhede.",
                () => OpenPageAsync(nameof(ProfilePage)),
                "settings-profile-row"),
            BuildSettingsRow(
                "▣",
                "Bestuur rekening",
                "Bekyk jou rekening en toegang.",
                () => OpenPageAsync(nameof(AccountPage)),
                "settings-account-row"),
            BuildSettingsRow(
                "✦",
                "Intekening",
                session.HasPaidSubscription ? "Jou betaalde toegang is aktief." : "Jy gebruik tans gratis toegang.",
                () => OpenPageAsync(nameof(PlansPage)),
                "settings-subscription-row")));

        _downloadSummaryLabel = new Label
        {
            Text = "Laai aflaaie...",
            FontSize = 13,
            TextColor = MutedTextColor,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        _content.Children.Add(BuildSection(
            "Vanlyn luister",
            BuildSettingsRow(
                "↓",
                "Afgelaaide stories",
                _downloadSummaryLabel,
                () => OpenPageAsync(nameof(DownloadedPage)),
                "settings-downloads-row")));

        _content.Children.Add(BuildSection(
            "Oor Schink Stories",
            BuildSettingsRow(
                "ⓘ",
                "Meer oor ons",
                "Leer meer oor Schink Stories en ons storietyd.",
                () => OpenWebsiteAsync("/meer-oor-ons"),
                "settings-about-row"),
            BuildSettingsRow(
                "?",
                "Hulp en kontak",
                "Kontak ons wanneer jy hulp nodig het.",
                () => OpenWebsiteAsync("/#kontak-ons"),
                "settings-support-row"),
            BuildSettingsRow(
                "↗",
                "Schink Stories webwerf",
                "Maak die webwerf in jou blaaier oop.",
                () => OpenWebsiteAsync("/"),
                "settings-website-row")));

        _content.Children.Add(BuildSignOutButton());
        _content.Children.Add(new Label
        {
            Text = $"Weergawe {AppInfo.Current.VersionString}",
            FontSize = 12,
            TextColor = MutedTextColor,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, -4, 0, 0)
        });
    }

    private View BuildAccountSummary(MobileSession session)
    {
        var displayName = ResolveDisplayName(session);
        var email = string.IsNullOrWhiteSpace(session.Email)
            ? "Jou rekening"
            : session.Email.Trim();

        var card = new Border
        {
            BackgroundColor = AccentColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = new Thickness(18),
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 14,
                Children =
                {
                    BuildAvatar(session),
                    new VerticalStackLayout
                    {
                        Spacing = 4,
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label
                            {
                                Text = displayName,
                                FontSize = 21,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Colors.White,
                                MaxLines = 1,
                                LineBreakMode = LineBreakMode.TailTruncation
                            },
                            new Label
                            {
                                Text = email,
                                FontSize = 13,
                                TextColor = Color.FromArgb("#DDEDE8"),
                                MaxLines = 1,
                                LineBreakMode = LineBreakMode.TailTruncation
                            },
                            new Label
                            {
                                Text = session.HasPaidSubscription ? "Betaalde toegang is aktief" : "Gratis toegang",
                                FontSize = 13,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = GoldColor
                            }
                        }
                    }
                }
            }
        };

        if (card.Content is Grid summaryGrid && summaryGrid.Children.Count > 1)
        {
            summaryGrid.SetColumn(summaryGrid.Children[1], 1);
        }

        return card;
    }

    private static View BuildAvatar(MobileSession session)
    {
        var initials = BuildInitials(session);
        var avatar = new Border
        {
            BackgroundColor = GoldColor,
            Stroke = Colors.White,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 32 },
            WidthRequest = 64,
            HeightRequest = 64,
            Content = new Label
            {
                Text = initials,
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#0B3534"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        return avatar;
    }

    private static View BuildSection(string title, params View[] rows)
    {
        var rowStack = new VerticalStackLayout
        {
            Spacing = 9
        };

        foreach (var row in rows)
        {
            rowStack.Children.Add(row);
        }

        return new VerticalStackLayout
        {
            Spacing = 9,
            Children =
            {
                new Label
                {
                    Text = title,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = MutedTextColor,
                    Margin = new Thickness(4, 0, 0, 1)
                },
                rowStack
            }
        };
    }

    private static Border BuildSettingsRow(
        string icon,
        string title,
        string subtitle,
        Func<Task> onTap,
        string automationId) =>
        BuildSettingsRow(
            icon,
            title,
            new Label
            {
                Text = subtitle,
                FontSize = 13,
                TextColor = MutedTextColor,
                MaxLines = 2,
                LineBreakMode = LineBreakMode.TailTruncation
            },
            onTap,
            automationId);

    private static Border BuildSettingsRow(
        string icon,
        string title,
        Label subtitle,
        Func<Task> onTap,
        string automationId)
    {
        var iconView = new Border
        {
            BackgroundColor = Color.FromArgb("#F3E6C8"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 21 },
            WidthRequest = 42,
            HeightRequest = 42,
            Content = new Label
            {
                Text = icon,
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = AccentColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                InputTransparent = true
            }
        };

        var titleLabel = new Label
        {
            Text = title,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = TextColor,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation,
            InputTransparent = true
        };
        subtitle.InputTransparent = true;

        var copy = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, subtitle },
            InputTransparent = true
        };

        var arrow = new Label
        {
            Text = "›",
            FontSize = 28,
            TextColor = AccentColor,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            InputTransparent = true
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Children = { iconView, copy, arrow }
        };
        Grid.SetColumn(copy, 1);
        Grid.SetColumn(arrow, 2);

        var row = new Border
        {
            AutomationId = automationId,
            BackgroundColor = Colors.White,
            Stroke = BorderColor,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = new Thickness(13, 10),
            MinimumHeightRequest = 68,
            Content = grid
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await onTap();
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private Border BuildSignOutButton()
    {
        var signOutButton = new Border
        {
            AutomationId = "settings-sign-out-button",
            BackgroundColor = Colors.Transparent,
            Stroke = DestructiveColor,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            HeightRequest = 52,
            Content = new Label
            {
                Text = "Teken uit",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = DestructiveColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                InputTransparent = true
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ConfirmSignOutAsync();
        signOutButton.GestureRecognizers.Add(tap);
        return signOutButton;
    }

    private async Task RefreshDownloadSummaryAsync(CancellationToken cancellationToken)
    {
        var downloads = await _offlineDownloadService.GetDownloadsAsync(cancellationToken);
        if (!_isPageActive)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_downloadSummaryLabel is not null)
            {
                _downloadSummaryLabel.Text = BuildDownloadSummary(downloads.Count);
            }
        });
    }

    private Task OpenPageAsync(string route) =>
        _navigationGate.RunAsync(() => Shell.Current.GoToAsync(route, animate: true));

    private async Task OpenWebsiteAsync(string path)
    {
        await _navigationGate.RunAsync(async () =>
        {
            try
            {
                await Browser.OpenAsync(_apiClient.BuildAbsoluteUrl(path), BrowserLaunchMode.External);
            }
            catch (Exception)
            {
                await DisplayAlertAsync(
                    "Kon nie oopmaak nie",
                    "Die webwerf kon nie nou oopmaak nie. Probeer asseblief weer.",
                    "Reg so");
            }
        });
    }

    private async Task ConfirmSignOutAsync()
    {
        var shouldSignOut = await DisplayAlertAsync(
            "Teken uit",
            "Is jy seker jy wil uitteken?",
            "Teken uit",
            "Bly ingeteken");
        if (!shouldSignOut)
        {
            return;
        }

        await _navigationGate.RunAsync(async () =>
        {
            try
            {
                await _apiClient.SignOutAsync();
                await Shell.Current.GoToAsync("..", animate: false);
            }
            catch (Exception)
            {
                await DisplayAlertAsync(
                    "Kon nie uitteken nie",
                    "Ons kon jou nie nou uitteken nie. Probeer asseblief weer.",
                    "Reg so");
            }
        });
    }

    private static string ResolveDisplayName(MobileSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
        {
            return session.DisplayName.Trim();
        }

        var name = string.Join(
            " ",
            new[] { session.FirstName, session.LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()));

        return string.IsNullOrWhiteSpace(name) ? "Welkom terug" : name;
    }

    private static string BuildInitials(MobileSession session)
    {
        var source = ResolveDisplayName(session);
        var initials = source
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();

        return initials.Length == 0 ? "S" : new string(initials);
    }

    private static string BuildDownloadSummary(int count) => count switch
    {
        0 => "Geen afgelaaide stories nie.",
        1 => "1 afgelaaide storie.",
        _ => $"{count} afgelaaide stories."
    };
}
