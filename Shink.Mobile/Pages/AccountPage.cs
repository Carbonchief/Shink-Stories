using Shink.Mobile.Models;
using Shink.Mobile.Services;
using Microsoft.Maui.Authentication;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;

namespace Shink.Mobile.Pages;

public sealed class AccountPage : ContentPage
{
    private static readonly Thickness SignedInTopBarMargin = new(18, 18, 18, 0);

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
    private ContentView? _authPanelContentHost;
    private readonly BoxView _authPanelTopSpacer;
    private VerticalStackLayout? _authHeroStack;
    private Image? _authLogoImage;
    private Label? _authWelcomeLabel;
    private Label? _authIntroLabel;
    private Label? _authTaglineLabel;
    private Image? _authCharacterImage;
    private Grid? _authPanelFrame;
    private Border? _authPanelBackground;
    private View? _authHero;
    private AuthPanelMode _authPanelMode = AuthPanelMode.Landing;
    private double _lastLandingLayoutHeight = -1;
    private bool _hasLoadedSession;
    private bool _isAuthRequestInFlight;
    private bool _isSessionStateSubscribed;

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
                    Color = Color.FromArgb("#15B5BC"),
                    Opacity = 0.16,
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

    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateAuthPanelTopSpacer();
        if (height > 0 && Math.Abs(_lastLandingLayoutHeight - height) > 1)
        {
            _lastLandingLayoutHeight = height;
            ApplyLandingLayoutMetrics();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SubscribePageEvents();
        if (_hasLoadedSession)
        {
            return;
        }

        _hasLoadedSession = true;
        await RefreshSessionAsync();
    }

    protected override void OnDisappearing()
    {
        UnsubscribePageEvents();
        base.OnDisappearing();
    }

    private void SubscribePageEvents()
    {
        if (_isSessionStateSubscribed)
        {
            return;
        }

        _sessionState.Changed += OnSessionStateChanged;
        _isSessionStateSubscribed = true;
    }

    private void UnsubscribePageEvents()
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
        MainThread.BeginInvokeOnMainThread(ApplySessionState);
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

    private static ImageSource CreatePackageImageSource(string fileName) =>
        ImageSource.FromStream(() => FileSystem.OpenAppPackageFileAsync(fileName).GetAwaiter().GetResult());

    private View BuildAuthHero()
    {
        var metrics = GetLandingLayoutMetrics();
        _authLogoImage = new Image
        {
            Source = CreatePackageImageSource("schink_stories_logo_white_raw.png"),
            BackgroundColor = Colors.Transparent,
            HeightRequest = metrics.LogoHeight,
            Aspect = Aspect.AspectFit,
            Margin = metrics.LogoMargin
        };
        _authWelcomeLabel = new Label
        {
            FontSize = metrics.TitleFontSize,
            FontFamily = "serif",
            LineHeight = 1.02,
            TextColor = Color.FromArgb("#F9FBFA"),
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = metrics.TitleMargin,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span
                    {
                        Text = "\"Rustige, opbouende",
                        FontAttributes = FontAttributes.Bold
                    },
                    new Span
                    {
                        Text = "\nAfrikaanse storietyd.\"",
                        FontAttributes = FontAttributes.Bold | FontAttributes.Italic,
                        FontSize = metrics.TitleSublineFontSize
                    }
                }
            },
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 2),
                Radius = 6,
                Opacity = 0.28f
            }
        };
        _authIntroLabel = new Label
        {
            TextColor = Colors.White,
            FontSize = metrics.IntroFontSize,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = metrics.IntroMargin,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = "Rustige, opbouende " },
                    new Span
                    {
                        Text = "Afrikaanse storietyd",
                        FontAttributes = FontAttributes.Bold
                    },
                    new Span { Text = "." }
                }
            },
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 1),
                Radius = 4,
                Opacity = 0.22f
            }
        };
        _authTaglineLabel = new Label
        {
            Text = "R 79 per maand. Kanselleer enige tyd.",
            TextColor = Color.FromArgb("#FFF7E8"),
            FontSize = metrics.TaglineFontSize,
            FontAttributes = FontAttributes.Bold | FontAttributes.Italic,
            HorizontalTextAlignment = TextAlignment.Center,
            LineHeight = 1.1,
            Margin = metrics.TaglineMargin
        };
        _authCharacterImage = new Image
        {
            Source = "oortjies_02.png",
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
                _authCharacterImage,
                _authLogoImage,
                _authWelcomeLabel
            }
        };
        return _authHeroStack;
    }

    private View BuildAuthPanel()
    {
        var metrics = GetLandingLayoutMetrics();
        _authPanelContentHost = new ContentView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            Padding = metrics.PanelPadding
        };
        RenderAuthFormContent();

        _authPanelBackground = new Border
        {
            BackgroundColor = _authPanelMode == AuthPanelMode.Landing
                ? Colors.Transparent
                : Color.FromArgb("#FFF7E8"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 34 },
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _authPanelFrame = new Grid
        {
            Margin = metrics.PanelMargin,
            Children =
            {
                _authPanelBackground,
                _authPanelContentHost
            }
        };
        return _authPanelFrame;
    }

    private View BuildModeButton(string text, AuthPanelMode mode, bool isPrimary)
    {
        var metrics = GetLandingLayoutMetrics();
        var isLanding = _authPanelMode == AuthPanelMode.Landing;
        if (isLanding)
        {
            var landingLabel = new Label
            {
                Text = text,
                TextColor = Colors.White,
                FontAttributes = FontAttributes.Bold,
                FontFamily = "sans-serif",
                FontSize = metrics.ModeButtonFontSize,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                InputTransparent = true
            };
            var landingButton = new Border
            {
                BackgroundColor = Color.FromArgb("#383A48"),
                Stroke = Color.FromArgb("#30323F"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                HeightRequest = metrics.ModeButtonHeight,
                MinimumHeightRequest = metrics.ModeButtonHeight,
                Padding = metrics.ModeButtonPadding,
                HorizontalOptions = LayoutOptions.Fill,
                Content = landingLabel,
                Shadow = new Shadow
                {
                    Brush = Brush.Black,
                    Offset = new Point(0, 4),
                    Radius = 12,
                    Opacity = 0.18f
                }
            };
            var landingTap = new TapGestureRecognizer();
            landingTap.Tapped += (_, _) => SetAuthPanelMode(mode);
            landingButton.GestureRecognizers.Add(landingTap);
            return landingButton;
        }

        var buttonHeight = isLanding ? metrics.ModeButtonHeight : 78;
        var labelColor = isPrimary ? Colors.White : Color.FromArgb("#243238");
        var icon = new Image
        {
            Source = mode == AuthPanelMode.SignIn
                ? "auth_icon_user_white_rendered.png"
                : "auth_icon_pencil_gold_rendered.png",
            WidthRequest = isLanding ? metrics.ModeIconSize : 42,
            HeightRequest = isLanding ? metrics.ModeIconSize : 42,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        var label = new Label
        {
            Text = text,
            TextColor = labelColor,
            FontAttributes = FontAttributes.Bold,
            FontFamily = "serif",
            FontSize = isLanding ? metrics.ModeButtonFontSize : 25,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            InputTransparent = true
        };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(label, 1);

        var button = new Border
        {
            BackgroundColor = isPrimary ? Color.FromArgb("#146D69") : Color.FromArgb("#FFFCF5"),
            Stroke = isPrimary ? Color.FromArgb("#146D69") : Color.FromArgb("#E8B52F"),
            StrokeThickness = isPrimary ? 0 : 2,
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            HeightRequest = buttonHeight,
            MinimumHeightRequest = buttonHeight,
            Padding = isLanding ? metrics.ModeButtonPadding : new Thickness(18, 12),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = isLanding ? metrics.ModeIconColumnWidth : 48 },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = isLanding ? metrics.ModeIconColumnWidth : 48 }
                },
                Children =
                {
                    icon,
                    label
                },
                InputTransparent = true
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SetAuthPanelMode(mode);
        button.GestureRecognizers.Add(tap);
        return button;
    }

    private void RenderAuthFormContent()
    {
        if (_authPanelContentHost is null)
        {
            return;
        }

        _authPanelContentHost.Content = null;
        var metrics = GetLandingLayoutMetrics();

        if (_authPanelMode == AuthPanelMode.Landing)
        {
            var landingContent = new VerticalStackLayout
            {
                Spacing = metrics.PanelContentSpacing,
                Children =
                {
                    BuildModeButton("Teken In", AuthPanelMode.SignIn, true),
                    BuildModeButton("Kies ’n plan", AuthPanelMode.SignUp, true),
                    _authTaglineLabel ?? new Label
                    {
                        Text = "R 79 per maand. Kanselleer enige tyd.",
                        TextColor = Color.FromArgb("#FFF7E8"),
                        FontSize = metrics.TaglineFontSize,
                        FontAttributes = FontAttributes.Bold | FontAttributes.Italic,
                        HorizontalTextAlignment = TextAlignment.Center,
                        Margin = metrics.TaglineMargin
                    }
                }
            };
            _authPanelContentHost.Content = landingContent;
            return;
        }

        DetachStatusLabel();
        var formContent = new StackLayout
        {
            Spacing = 14,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start
        };

        formContent.Children.Add(BuildAuthPanelHeader(
            _authPanelMode == AuthPanelMode.SignIn ? "Teken in" : "Skep rekening"));
        formContent.Children.Add(_statusLabel);

        if (_authPanelMode == AuthPanelMode.SignIn)
        {
            var loginEmailEntry = CreateEntry("E-pos", Keyboard.Email);
            var loginPasswordEntry = CreateEntry("Wagwoord", isPassword: true);
            var loginLabel = new Label
            {
                Text = "Teken in",
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
            var googleButton = BuildGoogleSignInButton(out var googleLabel, out var googleSpinner);
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
            var googleTap = new TapGestureRecognizer();
            googleTap.Tapped += async (_, _) =>
            {
                if (_isAuthRequestInFlight)
                {
                    return;
                }

                SetGoogleSubmitLoading(googleButton, googleLabel, googleSpinner, loginEmailEntry, loginPasswordEntry, loginButton, isLoading: true);
                try
                {
                    var result = await WebAuthenticator.Default.AuthenticateAsync(
                        _apiClient.BuildGoogleSignInStartUri(),
                        new Uri(MobileApiClient.GoogleCallbackUrl));

                    if (result.Properties.TryGetValue("error", out var errorMessage) &&
                        !string.IsNullOrWhiteSpace(errorMessage))
                    {
                        SetStatus(errorMessage, isError: true);
                        return;
                    }

                    if (!result.Properties.TryGetValue("token", out var token) ||
                        string.IsNullOrWhiteSpace(token))
                    {
                        SetStatus("Google-aanmelding kon nie bevestig word nie. Probeer asseblief weer.", isError: true);
                        return;
                    }

                    var signInResult = await _apiClient.CompleteGoogleSignInAsync(token);
                    await RefreshSessionAsync(signInResult.Message);
                }
                catch (TaskCanceledException)
                {
                    SetStatus(null);
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, isError: true);
                }
                finally
                {
                    SetGoogleSubmitLoading(googleButton, googleLabel, googleSpinner, loginEmailEntry, loginPasswordEntry, loginButton, isLoading: false);
                }
            };
            googleButton.GestureRecognizers.Add(googleTap);

            formContent.Children.Add(googleButton);
            formContent.Children.Add(BuildAuthDivider("of"));
            formContent.Children.Add(BuildField(loginEmailEntry));
            formContent.Children.Add(BuildField(loginPasswordEntry));
            formContent.Children.Add(loginButton);
            formContent.Children.Add(BuildModeSwitchLink(
                "Nog nie 'n rekening nie?",
                "Skep rekening",
                AuthPanelMode.SignUp));
            _authPanelContentHost.Content = formContent;
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

            formContent.Children.Add(BuildField(signupFirstNameEntry));
            formContent.Children.Add(BuildField(signupLastNameEntry));
            formContent.Children.Add(BuildField(signupDisplayNameEntry));
            formContent.Children.Add(BuildField(signupEmailEntry));
            formContent.Children.Add(BuildField(signupMobileEntry));
            formContent.Children.Add(BuildField(signupPasswordEntry));
            formContent.Children.Add(signupButton);
            formContent.Children.Add(BuildModeSwitchLink(
                "Het jy reeds 'n rekening?",
                "Teken in",
                AuthPanelMode.SignIn));
        }

        _authPanelContentHost.Content = formContent;
    }

    private void DetachStatusLabel()
    {
        if (_statusLabel.Parent is Layout parentLayout)
        {
            parentLayout.Remove(_statusLabel);
        }
        else if (_statusLabel.Parent is ContentView parentContent && parentContent.Content == _statusLabel)
        {
            parentContent.Content = null;
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
        UpdateAuthPanelChrome();
        RenderAuthFormContent();
    }

    private void UpdateAuthPanelChrome()
    {
        var metrics = GetLandingLayoutMetrics();
        if (_authPanelFrame is not null)
        {
            _authPanelFrame.Margin = _authPanelMode == AuthPanelMode.Landing
                ? metrics.PanelMargin
                : new Thickness(18, 0, 18, 18);
        }

        if (_authPanelContentHost is not null)
        {
            _authPanelContentHost.Padding = _authPanelMode == AuthPanelMode.Landing
                ? metrics.PanelPadding
                : new Thickness(26, 24, 26, 28);
        }

        if (_authPanelBackground is not null)
        {
            _authPanelBackground.BackgroundColor = _authPanelMode == AuthPanelMode.Landing
                ? Colors.Transparent
                : Color.FromArgb("#FFF7E8");
            _authPanelBackground.StrokeShape = new RoundRectangle
            {
                CornerRadius = _authPanelMode == AuthPanelMode.Landing ? 0 : 34
            };
        }
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
            _authWelcomeLabel.Margin = metrics.TitleMargin;
            if (_authWelcomeLabel.FormattedText?.Spans.Count > 1)
            {
                _authWelcomeLabel.FormattedText.Spans[1].FontSize = metrics.TitleSublineFontSize;
            }
        }

        if (_authIntroLabel is not null)
        {
            _authIntroLabel.FontSize = metrics.IntroFontSize;
            _authIntroLabel.Margin = metrics.IntroMargin;
        }

        if (_authTaglineLabel is not null)
        {
            _authTaglineLabel.FontSize = metrics.TaglineFontSize;
            _authTaglineLabel.Margin = metrics.TaglineMargin;
        }

        if (_authCharacterImage is not null)
        {
            _authCharacterImage.HeightRequest = metrics.CharacterHeight;
            _authCharacterImage.Margin = metrics.CharacterMargin;
        }

        UpdateAuthPanelChrome();
    }

    private LandingLayoutMetrics GetLandingLayoutMetrics()
    {
        var height = Height > 0
            ? Height
            : DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        var compact = height < 740;
        var tight = height < 680;

        return new LandingLayoutMetrics(
            HeroPadding: new Thickness(16, Math.Clamp(height * (tight ? 0.21 : compact ? 0.24 : 0.27), 132, 252), 16, 0),
            HeroSpacing: tight ? -3 : -2,
            LogoHeight: Math.Clamp(height * (tight ? 0.17 : 0.2), 124, 194),
            LogoMargin: new Thickness(-24, tight ? -16 : -18, -24, tight ? 6 : 10),
            TitleFontSize: Math.Clamp(height * (tight ? 0.031 : 0.034), 21, 31),
            TitleSublineFontSize: Math.Clamp(height * (tight ? 0.033 : 0.037), 22, 34),
            TitleMargin: new Thickness(0, tight ? 2 : 6, 0, 0),
            IntroFontSize: Math.Clamp(height * 0.019, 14, 18),
            IntroMargin: new Thickness(0, tight ? 7 : 9, 0, 0),
            TaglineFontSize: Math.Clamp(height * 0.018, 14, 18),
            TaglineMargin: new Thickness(0, tight ? 2 : 4, 0, 0),
            CharacterHeight: Math.Clamp(height * (tight ? 0.085 : 0.105), 64, 112),
            CharacterMargin: new Thickness(0, 0, 0, tight ? -4 : -6),
            PanelMargin: new Thickness(tight ? 50 : 54, tight ? 18 : compact ? 24 : 30, tight ? 50 : 54, tight ? 18 : 28),
            PanelPadding: new Thickness(0),
            PanelContentSpacing: tight ? 9 : 11,
            ModeButtonHeight: tight ? 58 : compact ? 62 : 68,
            ModeButtonFontSize: tight ? 21 : compact ? 23 : 25,
            ModeButtonPadding: new Thickness(tight ? 14 : 18, tight ? 8 : 10),
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
        var backIcon = new GraphicsView
        {
            Drawable = new BackChevronDrawable(),
            WidthRequest = 38,
            HeightRequest = 38,
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
            Content = new Grid
            {
                WidthRequest = 38,
                HeightRequest = 38,
                Children =
                {
                    backIcon
                }
            }
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

    private sealed class BackChevronDrawable : Microsoft.Maui.Graphics.IDrawable
    {
        public void Draw(Microsoft.Maui.Graphics.ICanvas canvas, Microsoft.Maui.Graphics.RectF dirtyRect)
        {
            canvas.StrokeColor = Color.FromArgb("#243238");
            canvas.StrokeSize = 4;
            canvas.StrokeLineCap = Microsoft.Maui.Graphics.LineCap.Round;
            canvas.StrokeLineJoin = Microsoft.Maui.Graphics.LineJoin.Round;

            canvas.DrawLine(21.5f, 12.5f, 15.5f, 19f);
            canvas.DrawLine(15.5f, 19f, 21.5f, 25.5f);
        }
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

    private static Border BuildGoogleSignInButton(out Label label, out ActivityIndicator spinner)
    {
        var googleIcon = new Image
        {
            Source = ImageSource.FromFile("google_logo.png"),
            WidthRequest = 24,
            HeightRequest = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        label = new Label
        {
            Text = "Teken in met Google",
            TextColor = Color.FromArgb("#3C4043"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 18,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            InputTransparent = true
        };
        spinner = new ActivityIndicator
        {
            Color = Color.FromArgb("#3C4043"),
            WidthRequest = 22,
            HeightRequest = 22,
            IsRunning = false,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        Grid.SetColumn(googleIcon, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(spinner, 2);

        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#DADCE0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            HeightRequest = 58,
            Padding = new Thickness(18, 0),
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 2),
                Radius = 5,
                Opacity = 0.18f
            },
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = 42 },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = 42 }
                },
                Children =
                {
                    googleIcon,
                    label,
                    spinner
                },
                InputTransparent = true
            }
        };
    }

    private static View BuildAuthDivider(string text)
    {
        var leftLine = new BoxView
        {
            HeightRequest = 1,
            Color = Color.FromArgb("#E8DEC8"),
            VerticalOptions = LayoutOptions.Center
        };
        var label = new Label
        {
            Text = text,
            TextColor = Color.FromArgb("#7C817C"),
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        var rightLine = new BoxView
        {
            HeightRequest = 1,
            Color = Color.FromArgb("#E8DEC8"),
            VerticalOptions = LayoutOptions.Center
        };

        Grid.SetColumn(leftLine, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(rightLine, 2);

        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 10,
            Children =
            {
                leftLine,
                label,
                rightLine
            }
        };
    }

    private sealed record LandingLayoutMetrics(
        Thickness HeroPadding,
        double HeroSpacing,
        double LogoHeight,
        Thickness LogoMargin,
        double TitleFontSize,
        double TitleSublineFontSize,
        Thickness TitleMargin,
        double IntroFontSize,
        Thickness IntroMargin,
        double TaglineFontSize,
        Thickness TaglineMargin,
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
        label.Text = isLoading ? "Teken in..." : "Teken in";
        spinner.IsVisible = isLoading;
        spinner.IsRunning = isLoading;
        emailEntry.IsEnabled = !isLoading;
        passwordEntry.IsEnabled = !isLoading;
    }

    private void SetGoogleSubmitLoading(
        Border googleButton,
        Label googleLabel,
        ActivityIndicator googleSpinner,
        MauiEntry emailEntry,
        MauiEntry passwordEntry,
        Border loginButton,
        bool isLoading)
    {
        _isAuthRequestInFlight = isLoading;
        googleButton.Opacity = isLoading ? 0.82 : 1;
        googleLabel.Text = isLoading ? "Koppel met Google..." : "Teken in met Google";
        googleSpinner.IsVisible = isLoading;
        googleSpinner.IsRunning = isLoading;
        emailEntry.IsEnabled = !isLoading;
        passwordEntry.IsEnabled = !isLoading;
        loginButton.IsEnabled = !isLoading;
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
                SignedInTopBarMargin,
                "back"));
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
