using Shink.Mobile.Services;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;

namespace Shink.Mobile.Pages;

public sealed class AccountPage : ContentPage
{
    private enum AuthPanelMode
    {
        Landing,
        SignIn,
        SignUp
    }

    private readonly MobileApiClient _apiClient;
    private readonly SessionState _sessionState;
    private readonly Label _statusLabel;
    private readonly VerticalStackLayout _signedInState;
    private readonly VerticalStackLayout _signedOutState;
    private readonly VerticalStackLayout _authPanelContent;
    private View? _authHero;
    private AuthPanelMode _authPanelMode = AuthPanelMode.Landing;
    private bool _hasLoadedSession;
    private bool _isAuthRequestInFlight;

    public AccountPage(MobileApiClient apiClient, SessionState sessionState)
    {
        _apiClient = apiClient;
        _sessionState = sessionState;
        Title = "Rekening";
        BackgroundColor = Color.FromArgb("#0D6F73");
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);

        _statusLabel = new Label
        {
            TextColor = Color.FromArgb("#5F5F5F"),
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            IsVisible = false
        };
        var hasCachedSession = _sessionState.Current.IsSignedIn;
        _signedInState = new VerticalStackLayout { Spacing = 0, IsVisible = hasCachedSession };
        _signedOutState = new VerticalStackLayout { Spacing = 0, IsVisible = !hasCachedSession };
        _authPanelContent = new VerticalStackLayout { Spacing = 14 };
        ApplySessionState();

        Content = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Children =
            {
                new Image
                {
                    Source = "schink_background.jpeg",
                    Aspect = Aspect.AspectFill,
                    Opacity = 0.92,
                    InputTransparent = true
                },
                new BoxView
                {
                    Color = Color.FromArgb("#0B4E52"),
                    Opacity = 0.24,
                    InputTransparent = true
                },
                new MauiScrollView
                {
                    SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.SoftInput),
                    Content = new VerticalStackLayout
                    {
                        Spacing = 0,
                        Children =
                        {
                            _signedInState,
                            _signedOutState
                        }
                    }
                }
            }
        };

        _sessionState.Changed += _ => MainThread.BeginInvokeOnMainThread(ApplySessionState);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_hasLoadedSession)
        {
            return;
        }

        _hasLoadedSession = true;
        await RefreshSessionAsync();
    }

    private void BuildSignedOutState()
    {
        _signedOutState.Children.Clear();
        _authHero = BuildAuthHero();
        _authHero.IsVisible = _authPanelMode == AuthPanelMode.Landing;
        _signedOutState.Children.Add(_authHero);
        _signedOutState.Children.Add(BuildAuthPanel());
    }

    private View BuildAuthHero()
    {
        return new VerticalStackLayout
        {
            Padding = new Thickness(18, 54, 18, 0),
            Spacing = 8,
            Children =
            {
                new Image
                {
                    Source = "schink_stories_logo_white.png",
                    HeightRequest = 192,
                    Aspect = Aspect.AspectFit,
                    Margin = new Thickness(-12, 0, -12, -12)
                },
                new Label
                {
                    Text = "Welkom by\nSchink Stories",
                    FontSize = 35,
                    FontAttributes = FontAttributes.Bold,
                    FontFamily = "serif",
                    LineHeight = 0.9,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    Shadow = new Shadow
                    {
                        Brush = Brush.Black,
                        Offset = new Point(0, 2),
                        Radius = 6,
                        Opacity = 0.28f
                    }
                },
                new Border
                {
                    BackgroundColor = Color.FromArgb("#FFD45A"),
                    Stroke = Color.FromArgb("#E8B52F"),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 999 },
                    Padding = new Thickness(18, 10),
                    HorizontalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = "Afrikaanse stories vir klein luisteraars",
                        TextColor = Color.FromArgb("#0F6868"),
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                },
                new Image
                {
                    Source = "schink_character_lineup.png",
                    HeightRequest = 176,
                    Aspect = Aspect.AspectFit,
                    Margin = new Thickness(-34, 0, -34, -30)
                }
            }
        };
    }

    private View BuildAuthPanel()
    {
        RenderAuthFormContent();

        return new Border
        {
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 44 },
            Margin = new Thickness(18, 0, 18, 14),
            Padding = new Thickness(26, 24, 26, 28),
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    _authPanelContent
                }
            }
        };
    }

    private View BuildModeButton(string text, AuthPanelMode mode, bool isPrimary)
    {
        var labelColor = isPrimary ? Colors.White : Color.FromArgb("#243238");
        var icon = new Image
        {
            Source = mode == AuthPanelMode.SignIn ? "auth_icon_user_white_rendered.png" : "auth_icon_pencil_gold_rendered.png",
            WidthRequest = 46,
            HeightRequest = 46,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            InputTransparent = true
        };
        var textLabel = new Label
        {
            Text = text,
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            FontFamily = "serif",
            TextColor = labelColor,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        var caret = new Image
        {
            Source = isPrimary ? "auth_caret_white_rendered.png" : "auth_caret_dark_rendered.png",
            WidthRequest = 18,
            HeightRequest = 28,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            InputTransparent = true
        };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(textLabel, 1);
        Grid.SetColumn(caret, 2);

        var border = new Border
        {
            BackgroundColor = isPrimary ? Color.FromArgb("#146D69") : Color.FromArgb("#FFFCF5"),
            Stroke = isPrimary ? Color.FromArgb("#146D69") : Color.FromArgb("#E8B52F"),
            StrokeThickness = isPrimary ? 0 : 2,
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            HeightRequest = 78,
            Padding = new Thickness(18, 12),
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 8),
                Radius = 16,
                Opacity = isPrimary ? 0.16f : 0.08f
            },
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = 48 },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = 28 }
                },
                Children =
                {
                    icon,
                    textLabel,
                    caret
                },
                InputTransparent = true
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SetAuthPanelMode(mode);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private void RenderAuthFormContent()
    {
        _authPanelContent.Children.Clear();

        if (_authPanelMode == AuthPanelMode.Landing)
        {
            _authPanelContent.Children.Add(BuildModeButton("Meld aan", AuthPanelMode.SignIn, true));
            _authPanelContent.Children.Add(BuildModeButton("Skep rekening", AuthPanelMode.SignUp, false));
            _authPanelContent.Children.Add(new BoxView
            {
                HeightRequest = 1,
                Color = Color.FromArgb("#DDD1B8"),
                Margin = new Thickness(16, 8)
            });
            _authPanelContent.Children.Add(new Label
            {
                Text = "Jou stories. Jou plek.\nVeilig, privaat en altyd kind-vriendelik.",
                TextColor = Color.FromArgb("#243238"),
                FontSize = 15,
                HorizontalTextAlignment = TextAlignment.Center,
                LineHeight = 1.18
            });
            _authPanelContent.Children.Add(_statusLabel);
            return;
        }

        _authPanelContent.Children.Add(BuildAuthPanelHeading(
            _authPanelMode == AuthPanelMode.SignIn ? "Meld aan" : "Skep rekening"));
        _authPanelContent.Children.Add(_statusLabel);

        if (_authPanelMode == AuthPanelMode.SignIn)
        {
            var loginEmailEntry = CreateEntry("E-pos", Keyboard.Email);
            var loginPasswordEntry = CreateEntry("Wagwoord", isPassword: true);
            var loginLabel = new Label
            {
                Text = "Meld aan",
                TextColor = Colors.White,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            var loginSpinner = new ActivityIndicator
            {
                Color = Colors.White,
                WidthRequest = 22,
                HeightRequest = 22,
                IsRunning = false,
                IsVisible = false,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            var loginButton = new Border
            {
                BackgroundColor = Color.FromArgb("#146D69"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 24 },
                HeightRequest = 54,
                Content = new Grid
                {
                    Children =
                    {
                        loginLabel,
                        loginSpinner
                    }
                }
            };
            var loginTap = new TapGestureRecognizer();
            loginTap.Tapped += async (_, _) =>
            {
                if (_isAuthRequestInFlight)
                {
                    return;
                }

                SetLoginSubmitLoading(loginButton, loginLabel, loginSpinner, loginEmailEntry, loginPasswordEntry, isLoading: true);
                try
                {
                    var result = await _apiClient.SignInAsync(loginEmailEntry.Text ?? string.Empty, loginPasswordEntry.Text ?? string.Empty);
                    await RefreshSessionAsync(result.Message);
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, isError: true);
                }
                finally
                {
                    SetLoginSubmitLoading(loginButton, loginLabel, loginSpinner, loginEmailEntry, loginPasswordEntry, isLoading: false);
                }
            };
            loginButton.GestureRecognizers.Add(loginTap);

            _authPanelContent.Children.Add(BuildField(loginEmailEntry));
            _authPanelContent.Children.Add(BuildField(loginPasswordEntry));
            _authPanelContent.Children.Add(loginButton);
            _authPanelContent.Children.Add(BuildModeSwitchLink(
                "Nog nie 'n rekening nie?",
                "Skep rekening",
                AuthPanelMode.SignUp));
            return;
        }

        if (_authPanelMode == AuthPanelMode.SignUp)
        {
            var signupFirstNameEntry = CreateEntry("Naam");
            var signupLastNameEntry = CreateEntry("Van");
            var signupDisplayNameEntry = CreateEntry("Vertoonnaam");
            var signupEmailEntry = CreateEntry("E-pos", Keyboard.Email);
            var signupMobileEntry = CreateEntry("Selfoon");
            var signupPasswordEntry = CreateEntry("Wagwoord", isPassword: true);
            var signupButton = new Button
            {
                Text = "Skep rekening",
                BackgroundColor = Color.FromArgb("#E8B52F"),
                TextColor = Color.FromArgb("#243238"),
                CornerRadius = 24,
                FontAttributes = FontAttributes.Bold,
                HeightRequest = 54
            };
            signupButton.Clicked += async (_, _) =>
            {
                try
                {
                    var result = await _apiClient.SignUpAsync(
                        signupFirstNameEntry.Text ?? string.Empty,
                        signupLastNameEntry.Text ?? string.Empty,
                        signupDisplayNameEntry.Text ?? string.Empty,
                        signupEmailEntry.Text ?? string.Empty,
                        signupMobileEntry.Text ?? string.Empty,
                        signupPasswordEntry.Text ?? string.Empty);
                    await RefreshSessionAsync(result.Message);
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, isError: true);
                }
            };

            _authPanelContent.Children.Add(BuildField(signupFirstNameEntry));
            _authPanelContent.Children.Add(BuildField(signupLastNameEntry));
            _authPanelContent.Children.Add(BuildField(signupDisplayNameEntry));
            _authPanelContent.Children.Add(BuildField(signupEmailEntry));
            _authPanelContent.Children.Add(BuildField(signupMobileEntry));
            _authPanelContent.Children.Add(BuildField(signupPasswordEntry));
            _authPanelContent.Children.Add(signupButton);
            _authPanelContent.Children.Add(BuildModeSwitchLink(
                "Het jy reeds 'n rekening?",
                "Meld aan",
                AuthPanelMode.SignIn));
        }
    }

    private void SetAuthPanelMode(AuthPanelMode mode)
    {
        _authPanelMode = mode;
        if (_authHero is not null)
        {
            _authHero.IsVisible = _authPanelMode == AuthPanelMode.Landing;
        }

        SetStatus(null);
        RenderAuthFormContent();
    }

    private static View BuildAuthPanelHeading(string text) =>
        new Label
        {
            Text = text,
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            FontFamily = "serif",
            TextColor = Color.FromArgb("#243238"),
            HorizontalTextAlignment = TextAlignment.Center
        };

    private View BuildModeSwitchLink(string prompt, string action, AuthPanelMode mode)
    {
        var label = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Color.FromArgb("#5F5F5F"),
            FontSize = 14,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = $"{prompt} " },
                    new Span
                    {
                        Text = action,
                        TextColor = Color.FromArgb("#146D69"),
                        FontAttributes = FontAttributes.Bold
                    }
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SetAuthPanelMode(mode);
        label.GestureRecognizers.Add(tap);
        return label;
    }

    private void SetLoginSubmitLoading(
        Border button,
        Label label,
        ActivityIndicator spinner,
        MauiEntry emailEntry,
        MauiEntry passwordEntry,
        bool isLoading)
    {
        _isAuthRequestInFlight = isLoading;
        button.Opacity = isLoading ? 0.82 : 1;
        label.Text = isLoading ? "Meld aan..." : "Meld aan";
        spinner.IsVisible = isLoading;
        spinner.IsRunning = isLoading;
        emailEntry.IsEnabled = !isLoading;
        passwordEntry.IsEnabled = !isLoading;
    }

    private static MauiEntry CreateEntry(string placeholder, Keyboard? keyboard = null, bool isPassword = false)
    {
        var entry = new MauiEntry
        {
            Placeholder = placeholder,
            Keyboard = keyboard ?? Keyboard.Default,
            IsPassword = isPassword
        };
        ConfigureEntry(entry);
        return entry;
    }

    private static void ConfigureEntry(MauiEntry entry)
    {
        entry.BackgroundColor = Colors.Transparent;
        entry.TextColor = Color.FromArgb("#243238");
        entry.PlaceholderColor = Color.FromArgb("#7C817C");
        entry.HeightRequest = 48;
    }

    private static View BuildField(MauiEntry entry) =>
        new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E8DEC8"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(14, 0),
            Content = entry
        };

    private async Task RefreshSessionAsync(string? message = null)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _apiClient.GetSessionAsync(timeout.Token);
            ApplySessionState();
            SetStatus(message ?? (_sessionState.Current.IsSignedIn ? "Jy is ingeteken." : null));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void SetStatus(string? message, bool isError = false)
    {
        _statusLabel.Text = message ?? string.Empty;
        _statusLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
        _statusLabel.TextColor = isError ? Color.FromArgb("#B42318") : Color.FromArgb("#5F5F5F");
    }

    private void ApplySessionState()
    {
        var session = _sessionState.Current;

        _signedInState.Children.Clear();
        if (session.IsSignedIn)
        {
            _signedInState.IsVisible = true;
            _signedOutState.IsVisible = false;

            var logoutButton = new Button
            {
                Text = "Teken uit",
                BackgroundColor = Color.FromArgb("#222222"),
                TextColor = Colors.White
            };
            logoutButton.Clicked += async (_, _) =>
            {
                await _apiClient.SignOutAsync();
                await RefreshSessionAsync("Jy is nou afgeteken.");
            };

            _signedInState.Children.Add(MobileTopBar.Build(
                this,
                _apiClient,
                session,
                new Thickness(18, 54, 18, 0)));
            _signedInState.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#FFF7E8"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 34 },
                Margin = new Thickness(18, 18, 18, 28),
                Padding = new Thickness(24),
                Content = new VerticalStackLayout
                {
                    Spacing = 16,
                    Children =
                    {
                        new Image
                        {
                            Source = "schink_logo_text.png",
                            HeightRequest = 58,
                            Aspect = Aspect.AspectFit,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Welkom terug",
                            FontSize = 32,
                            FontAttributes = FontAttributes.Bold,
                            FontFamily = "serif",
                            TextColor = Color.FromArgb("#243238"),
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = session.DisplayName ?? session.Email ?? "Ingeteken",
                            FontSize = 16,
                            TextColor = Color.FromArgb("#5F5F5F"),
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = session.HasPaidSubscription
                                ? "Jou betaalde luistertoegang is aktief."
                                : "Jy het tans gratis toegang.",
                            TextColor = Color.FromArgb("#5F5F5F"),
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        logoutButton
                    }
                }
            });
        }
        else
        {
            if (_signedOutState.Children.Count == 0)
            {
                BuildSignedOutState();
            }

            _signedInState.IsVisible = false;
            _signedOutState.IsVisible = true;
        }
    }
}
