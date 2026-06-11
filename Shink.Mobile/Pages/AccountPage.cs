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
    private readonly VerticalStackLayout _authFormContainer;

    private readonly MauiEntry _loginEmailEntry;
    private readonly MauiEntry _loginPasswordEntry;
    private readonly MauiEntry _signupFirstNameEntry;
    private readonly MauiEntry _signupLastNameEntry;
    private readonly MauiEntry _signupDisplayNameEntry;
    private readonly MauiEntry _signupEmailEntry;
    private readonly MauiEntry _signupMobileEntry;
    private readonly MauiEntry _signupPasswordEntry;
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
        _authFormContainer = new VerticalStackLayout { Spacing = 12 };

        _loginEmailEntry = new MauiEntry { Placeholder = "E-pos", Keyboard = Keyboard.Email };
        _loginPasswordEntry = new MauiEntry { Placeholder = "Wagwoord", IsPassword = true };
        _signupFirstNameEntry = new MauiEntry { Placeholder = "Naam" };
        _signupLastNameEntry = new MauiEntry { Placeholder = "Van" };
        _signupDisplayNameEntry = new MauiEntry { Placeholder = "Vertoonnaam" };
        _signupEmailEntry = new MauiEntry { Placeholder = "E-pos", Keyboard = Keyboard.Email };
        _signupMobileEntry = new MauiEntry { Placeholder = "Selfoon" };
        _signupPasswordEntry = new MauiEntry { Placeholder = "Wagwoord", IsPassword = true };

        ConfigureEntry(_loginEmailEntry);
        ConfigureEntry(_loginPasswordEntry);
        ConfigureEntry(_signupFirstNameEntry);
        ConfigureEntry(_signupLastNameEntry);
        ConfigureEntry(_signupDisplayNameEntry);
        ConfigureEntry(_signupEmailEntry);
        ConfigureEntry(_signupMobileEntry);
        ConfigureEntry(_signupPasswordEntry);
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
        _signedOutState.Children.Add(BuildAuthHero());
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
        _authFormContainer.Children.Clear();
        BuildAuthFormContent();

        return new Border
        {
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 44 },
            Margin = new Thickness(18, 0, 18, 14),
            Padding = new Thickness(26, 24, 26, 28),
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    BuildModeButton("Meld aan", AuthPanelMode.SignIn, true),
                    BuildModeButton("Skep rekening", AuthPanelMode.SignUp, false),
                    new BoxView
                    {
                        HeightRequest = 1,
                        Color = Color.FromArgb("#DDD1B8"),
                        Margin = new Thickness(16, 8)
                    },
                    new Label
                    {
                        Text = "Jou stories. Jou plek.\nVeilig, privaat en altyd kind-vriendelik.",
                        TextColor = Color.FromArgb("#243238"),
                        FontSize = 15,
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineHeight = 1.18
                    },
                    _statusLabel,
                    _authFormContainer
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
            HorizontalOptions = LayoutOptions.Start
        };
        var textLabel = new Label
        {
            Text = text,
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            FontFamily = "serif",
            TextColor = labelColor,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        var caret = new Image
        {
            Source = isPrimary ? "auth_caret_white_rendered.png" : "auth_caret_dark_rendered.png",
            WidthRequest = 18,
            HeightRequest = 28,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
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
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SetAuthPanelMode(mode);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private void BuildAuthFormContent()
    {
        if (_authPanelMode == AuthPanelMode.SignIn)
        {
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

                SetLoginSubmitLoading(loginButton, loginLabel, loginSpinner, isLoading: true);
                try
                {
                    var result = await _apiClient.SignInAsync(_loginEmailEntry.Text ?? string.Empty, _loginPasswordEntry.Text ?? string.Empty);
                    await RefreshSessionAsync(result.Message);
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, isError: true);
                }
                finally
                {
                    SetLoginSubmitLoading(loginButton, loginLabel, loginSpinner, isLoading: false);
                }
            };
            loginButton.GestureRecognizers.Add(loginTap);

            _authFormContainer.Children.Add(BuildField(_loginEmailEntry));
            _authFormContainer.Children.Add(BuildField(_loginPasswordEntry));
            _authFormContainer.Children.Add(loginButton);
            return;
        }

        if (_authPanelMode == AuthPanelMode.SignUp)
        {
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
                        _signupFirstNameEntry.Text ?? string.Empty,
                        _signupLastNameEntry.Text ?? string.Empty,
                        _signupDisplayNameEntry.Text ?? string.Empty,
                        _signupEmailEntry.Text ?? string.Empty,
                        _signupMobileEntry.Text ?? string.Empty,
                        _signupPasswordEntry.Text ?? string.Empty);
                    await RefreshSessionAsync(result.Message);
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, isError: true);
                }
            };

            _authFormContainer.Children.Add(BuildField(_signupFirstNameEntry));
            _authFormContainer.Children.Add(BuildField(_signupLastNameEntry));
            _authFormContainer.Children.Add(BuildField(_signupDisplayNameEntry));
            _authFormContainer.Children.Add(BuildField(_signupEmailEntry));
            _authFormContainer.Children.Add(BuildField(_signupMobileEntry));
            _authFormContainer.Children.Add(BuildField(_signupPasswordEntry));
            _authFormContainer.Children.Add(signupButton);
        }
    }

    private void SetAuthPanelMode(AuthPanelMode mode)
    {
        _authPanelMode = _authPanelMode == mode ? AuthPanelMode.Landing : mode;
        BuildSignedOutState();
    }

    private void SetLoginSubmitLoading(Border button, Label label, ActivityIndicator spinner, bool isLoading)
    {
        _isAuthRequestInFlight = isLoading;
        button.Opacity = isLoading ? 0.82 : 1;
        label.Text = isLoading ? "Meld aan..." : "Meld aan";
        spinner.IsVisible = isLoading;
        spinner.IsRunning = isLoading;
        _loginEmailEntry.IsEnabled = !isLoading;
        _loginPasswordEntry.IsEnabled = !isLoading;
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

            _signedInState.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#FFF7E8"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 34 },
                Margin = new Thickness(18, 96, 18, 28),
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
                            Text = session.Email ?? "Ingeteken",
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
