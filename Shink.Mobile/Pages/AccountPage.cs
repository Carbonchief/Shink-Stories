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
    private readonly BoxView _authPanelTopSpacer;
    private VerticalStackLayout? _authHeroStack;
    private Image? _authLogoImage;
    private Label? _authWelcomeLabel;
    private Border? _authTaglinePill;
    private Label? _authTaglineLabel;
    private Image? _authCharacterImage;
    private Border? _authPanelFrame;
    private View? _authHero;
    private AuthPanelMode _authPanelMode = AuthPanelMode.Landing;
    private double _lastLandingLayoutHeight = -1;
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
        _authPanelTopSpacer = new BoxView
        {
            Color = Colors.Transparent,
            HeightRequest = 0,
            IsVisible = false,
            InputTransparent = true
        };
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

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateAuthPanelTopSpacer();
        if (height > 0 && Math.Abs(_lastLandingLayoutHeight - height) > 1)
        {
            _lastLandingLayoutHeight = height;
            ApplyLandingLayoutMetrics();
            if (_authPanelMode == AuthPanelMode.Landing && _authPanelContent.Children.Count > 0)
            {
                RenderAuthFormContent();
            }
        }
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
        UpdateAuthPanelTopSpacer();
        _signedOutState.Children.Add(_authPanelTopSpacer);
        _signedOutState.Children.Add(_authHero);
        _signedOutState.Children.Add(BuildAuthPanel());
    }

    private View BuildAuthHero()
    {
        var metrics = GetLandingLayoutMetrics();
        _authLogoImage = new Image
        {
            Source = "schink_stories_logo_white.png",
            HeightRequest = metrics.LogoHeight,
            Aspect = Aspect.AspectFit,
            Margin = metrics.LogoMargin
        };
        _authWelcomeLabel = new Label
        {
            Text = "Welkom by\nSchink Stories",
            FontSize = metrics.TitleFontSize,
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
        };
        _authTaglineLabel = new Label
        {
            Text = "Afrikaanse stories vir klein luisteraars",
            TextColor = Color.FromArgb("#0F6868"),
            FontSize = metrics.TaglineFontSize,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center
        };
        _authTaglinePill = new Border
        {
            BackgroundColor = Color.FromArgb("#FFD45A"),
            Stroke = Color.FromArgb("#E8B52F"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            Padding = metrics.TaglinePadding,
            HorizontalOptions = LayoutOptions.Center,
            Content = _authTaglineLabel
        };
        _authCharacterImage = new Image
        {
            Source = "schink_character_lineup.png",
            HeightRequest = metrics.CharacterHeight,
            Aspect = Aspect.AspectFit,
            Margin = metrics.CharacterMargin
        };
        _authHeroStack = new VerticalStackLayout
        {
            Padding = metrics.HeroPadding,
            Spacing = metrics.HeroSpacing,
            Children =
            {
                _authLogoImage,
                _authWelcomeLabel,
                _authTaglinePill,
                _authCharacterImage
            }
        };
        return _authHeroStack;
    }

    private View BuildAuthPanel()
    {
        RenderAuthFormContent();
        var metrics = GetLandingLayoutMetrics();

        _authPanelFrame = new Border
        {
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 44 },
            Margin = metrics.PanelMargin,
            Padding = metrics.PanelPadding,
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    _authPanelContent
                }
            }
        };
        return _authPanelFrame;
    }

    private View BuildModeButton(string text, AuthPanelMode mode, bool isPrimary)
    {
        var metrics = GetLandingLayoutMetrics();
        var isLanding = _authPanelMode == AuthPanelMode.Landing;
        var labelColor = isPrimary ? Colors.White : Color.FromArgb("#243238");
        var icon = new Image
        {
            Source = mode == AuthPanelMode.SignIn ? "auth_icon_user_white_rendered.png" : "auth_icon_pencil_gold_rendered.png",
            WidthRequest = isLanding ? metrics.ModeIconSize : 46,
            HeightRequest = isLanding ? metrics.ModeIconSize : 46,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            InputTransparent = true
        };
        var textLabel = new Label
        {
            Text = text,
            FontSize = isLanding ? metrics.ModeButtonFontSize : 25,
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
            HeightRequest = isLanding ? metrics.ModeButtonHeight : 78,
            Padding = isLanding ? metrics.ModeButtonPadding : new Thickness(18, 12),
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
                    new ColumnDefinition { Width = isLanding ? metrics.ModeIconColumnWidth : 48 },
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
        var metrics = GetLandingLayoutMetrics();
        _authPanelContent.Spacing = _authPanelMode == AuthPanelMode.Landing
            ? metrics.PanelContentSpacing
            : 14;

        if (_authPanelMode == AuthPanelMode.Landing)
        {
            _authPanelContent.Children.Add(BuildModeButton("Meld aan", AuthPanelMode.SignIn, true));
            _authPanelContent.Children.Add(BuildModeButton("Skep rekening", AuthPanelMode.SignUp, false));
            _authPanelContent.Children.Add(new BoxView
            {
                HeightRequest = 1,
                Color = Color.FromArgb("#DDD1B8"),
                Margin = metrics.SeparatorMargin
            });
            _authPanelContent.Children.Add(new Label
            {
                Text = "Jou stories. Jou plek.\nVeilig, privaat en altyd kind-vriendelik.",
                TextColor = Color.FromArgb("#243238"),
                FontSize = metrics.PanelInfoFontSize,
                HorizontalTextAlignment = TextAlignment.Center,
                LineHeight = 1.18
            });
            _authPanelContent.Children.Add(_statusLabel);
            return;
        }

        _authPanelContent.Children.Add(BuildAuthPanelHeader(
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
        UpdateAuthPanelTopSpacer();
        RenderAuthFormContent();
    }

    private void ApplyLandingLayoutMetrics()
    {
        var metrics = GetLandingLayoutMetrics();
        if (_authHeroStack is not null)
        {
            _authHeroStack.Padding = metrics.HeroPadding;
            _authHeroStack.Spacing = metrics.HeroSpacing;
        }

        if (_authLogoImage is not null)
        {
            _authLogoImage.HeightRequest = metrics.LogoHeight;
            _authLogoImage.Margin = metrics.LogoMargin;
        }

        if (_authWelcomeLabel is not null)
        {
            _authWelcomeLabel.FontSize = metrics.TitleFontSize;
        }

        if (_authTaglinePill is not null)
        {
            _authTaglinePill.Padding = metrics.TaglinePadding;
        }

        if (_authTaglineLabel is not null)
        {
            _authTaglineLabel.FontSize = metrics.TaglineFontSize;
        }

        if (_authCharacterImage is not null)
        {
            _authCharacterImage.HeightRequest = metrics.CharacterHeight;
            _authCharacterImage.Margin = metrics.CharacterMargin;
        }

        if (_authPanelFrame is not null && _authPanelMode == AuthPanelMode.Landing)
        {
            _authPanelFrame.Margin = metrics.PanelMargin;
            _authPanelFrame.Padding = metrics.PanelPadding;
        }
    }

    private LandingLayoutMetrics GetLandingLayoutMetrics()
    {
        var height = Height > 0
            ? Height
            : DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        var compact = height < 740;
        var tight = height < 680;

        return new LandingLayoutMetrics(
            HeroPadding: new Thickness(18, tight ? 22 : compact ? 32 : 48, 18, 0),
            HeroSpacing: tight ? 4 : compact ? 6 : 8,
            LogoHeight: Math.Clamp(height * (tight ? 0.18 : 0.2), 112, 182),
            LogoMargin: new Thickness(-12, 0, -12, tight ? -8 : -12),
            TitleFontSize: Math.Clamp(height * 0.04, 25, 35),
            TaglineFontSize: tight ? 12 : 13,
            TaglinePadding: new Thickness(tight ? 14 : 18, tight ? 7 : 10),
            CharacterHeight: Math.Clamp(height * (tight ? 0.12 : 0.17), 76, 158),
            CharacterMargin: new Thickness(-34, 0, -34, tight ? -18 : compact ? -24 : -30),
            PanelMargin: new Thickness(18, 0, 18, tight ? 8 : 14),
            PanelPadding: new Thickness(26, tight ? 18 : compact ? 21 : 24, 26, tight ? 20 : compact ? 24 : 28),
            PanelContentSpacing: tight ? 10 : compact ? 12 : 14,
            ModeButtonHeight: tight ? 64 : compact ? 70 : 78,
            ModeButtonFontSize: tight ? 22 : compact ? 24 : 25,
            ModeButtonPadding: new Thickness(tight ? 14 : 18, tight ? 9 : 12),
            ModeIconSize: tight ? 38 : compact ? 42 : 46,
            ModeIconColumnWidth: tight ? 42 : compact ? 46 : 48,
            SeparatorMargin: new Thickness(16, tight ? 3 : compact ? 5 : 8),
            PanelInfoFontSize: tight ? 13 : compact ? 14 : 15);
    }

    private void UpdateAuthPanelTopSpacer()
    {
        if (_authPanelMode == AuthPanelMode.Landing)
        {
            _authPanelTopSpacer.IsVisible = false;
            _authPanelTopSpacer.HeightRequest = 0;
            return;
        }

        var screenHeight = Height > 0
            ? Height
            : DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        var estimatedPanelHeight = _authPanelMode == AuthPanelMode.SignIn ? 330 : 620;
        var topInset = Math.Max(24, Math.Floor((screenHeight - estimatedPanelHeight) / 2));

        _authPanelTopSpacer.IsVisible = true;
        _authPanelTopSpacer.HeightRequest = topInset;
    }

    private View BuildAuthPanelHeader(string text)
    {
        var backIcon = new Image
        {
            Source = "auth_caret_dark_rendered.png",
            WidthRequest = 10,
            HeightRequest = 16,
            Rotation = 180,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        var backButton = new Border
        {
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            Stroke = Color.FromArgb("#E8DEC8"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            WidthRequest = 38,
            HeightRequest = 38,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = backIcon
        };
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += (_, _) =>
        {
            if (!_isAuthRequestInFlight)
            {
                SetAuthPanelMode(AuthPanelMode.Landing);
            }
        };
        backButton.GestureRecognizers.Add(backTap);

        var heading = new Label
        {
            Text = text,
            FontSize = 25,
            FontAttributes = FontAttributes.Bold,
            FontFamily = "serif",
            TextColor = Color.FromArgb("#243238"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        Grid.SetColumn(backButton, 0);
        Grid.SetColumn(heading, 1);

        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 38 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 38 }
            },
            Children =
            {
                backButton,
                heading
            }
        };
    }

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

    private sealed record LandingLayoutMetrics(
        Thickness HeroPadding,
        double HeroSpacing,
        double LogoHeight,
        Thickness LogoMargin,
        double TitleFontSize,
        double TaglineFontSize,
        Thickness TaglinePadding,
        double CharacterHeight,
        Thickness CharacterMargin,
        Thickness PanelMargin,
        Thickness PanelPadding,
        double PanelContentSpacing,
        double ModeButtonHeight,
        double ModeButtonFontSize,
        Thickness ModeButtonPadding,
        double ModeIconSize,
        double ModeIconColumnWidth,
        Thickness SeparatorMargin,
        double PanelInfoFontSize);

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
