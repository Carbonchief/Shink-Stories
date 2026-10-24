namespace Shink.Mobile.Pages;

public sealed class OnboardingPage : ContentPage
{
    private static readonly Color ArtworkTeal = Color.FromArgb("#36B7C1");
    private static readonly Color ArtworkCream = Color.FromArgb("#FBF6F0");
    private static readonly Color ProgressGold = Color.FromArgb("#FFBD0A");
    private static readonly Color ProgressInactive = Color.FromArgb("#D8D2C2");
    private const string ProgressAnimationName = "onboarding-progress";
    private const double ProgressStepWidth = 44;
    private const double ProgressDotSize = 18;
    private const double ProgressMaximumWidth = 24;
    private sealed record Slide(string Image, string Description);

    private static readonly Slide[] Slides =
    [
        new("schink_onboarding_01.jpg",
            "Welkom by Schink Stories. Kom geniet opbouende Afrikaanse stories wat kinders laat luister, lag en leer! " +
            "120+ Afrikaanse oudiostories. Waardes en lewenslesse. Geen donkertemas. Geen advertensies. Nuwe stories elke maand."),
        new("schink_onboarding_02.jpg",
            "Stories waar jy ook al is! Laai julle gunsteling stories af en luister sonder internet. " +
            "By die huis, op die langpad of waar julle ook al gaan."),
        new("schink_onboarding_03.jpg",
            "Ontmoet die karakters. Daar is soveel stories om te ontdek en karakters om te leer ken! Luister, speel en ontdek."),
        new("schink_onboarding_04.jpg",
            "Dis storietyd! Kom lag, kom luister en kom leer! Begin gratis met stories, of kry volle toegang tot al ons stories.")
    ];

    private readonly Image _artwork;
    private readonly AbsoluteLayout _artworkCanvas;
    private readonly BoxView _topSpacer;
    private readonly ScrollView _artworkViewport;
    private readonly Grid _stage;
    private TabletOnboardingView? _tabletComposition;
    private readonly Button[] _progressButtons = new Button[Slides.Length];
    private readonly Border _progressMarker = new()
    {
        AutomationId = "onboarding-progress-marker",
        BackgroundColor = ProgressGold,
        StrokeThickness = 0,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 9 },
        InputTransparent = true
    };
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private readonly Button _skipButton;
    private readonly Button _signInButton;
    private int _slideIndex;
    private double _progressLeft = (ProgressStepWidth - ProgressDotSize) / 2;
    private double _progressRight = (ProgressStepWidth + ProgressDotSize) / 2;
    private bool _hasShownSlide;
    private bool _isCompleting;
#if ANDROID
    private AndroidX.Activity.OnBackPressedCallback? _backCallback;
#endif

    public event EventHandler? Completed;
    public bool OpenSignInOnCompletion { get; private set; }

    public OnboardingPage()
    {
        Title = "Welkom";
        AutomationId = "onboarding-page";
        BackgroundColor = ArtworkCream;
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _artwork = new Image
        {
            AutomationId = "onboarding-artwork",
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };
        var swipeForward = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        swipeForward.Swiped += (_, _) => ShowSlide(_slideIndex + 1);
        var swipeBack = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
        swipeBack.Swiped += (_, _) => MoveBack();
        _artwork.GestureRecognizers.Add(swipeForward);
        _artwork.GestureRecognizers.Add(swipeBack);

        _artworkCanvas = new AbsoluteLayout
        {
            SafeAreaEdges = SafeAreaEdges.None,
            IsClippedToBounds = true,
            Children = { _artwork }
        };
        _topSpacer = new BoxView { Color = ArtworkTeal, HeightRequest = 0 };
        _artworkViewport = new ScrollView
        {
            AutomationId = "onboarding-artwork-scroll",
            SafeAreaEdges = SafeAreaEdges.None,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            BackgroundColor = ArtworkCream,
            Content = new VerticalStackLayout
            {
                SafeAreaEdges = SafeAreaEdges.None,
                Spacing = 0,
                Children = { _topSpacer, _artworkCanvas }
            }
        };
        _artworkViewport.SizeChanged += (_, _) => ResizeArtwork();

        _stage = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Children = { _artworkViewport }
        };
        _stage.SizeChanged += (_, _) => ResizeArtwork();

        _skipButton = CreateButton("Slaan oor", "onboarding-skip");
        _skipButton.FontSize = 13;
        _skipButton.Padding = new Thickness(8, 8);
        _skipButton.TextColor = Colors.White;
        _skipButton.HorizontalOptions = LayoutOptions.End;
        _skipButton.VerticalOptions = LayoutOptions.Start;
        _skipButton.Margin = new Thickness(0, 0, 4, 0);
        _skipButton.Clicked += (_, _) => Complete();

        _backButton = new Button
        {
            AutomationId = "onboarding-back",
            Text = "\uf060",
            FontFamily = "FontAwesomeSolid",
            FontSize = 22,
            TextColor = Colors.White,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            WidthRequest = 48,
            HeightRequest = 48,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(12, 0, 0, 0)
        };
        SemanticProperties.SetDescription(_backButton, "Terug na vorige skerm");
        _backButton.Clicked += (_, _) => MoveBack();
        _nextButton = CreateButton("Volgende", "onboarding-next");
        _nextButton.WidthRequest = 176;
        _nextButton.HorizontalOptions = LayoutOptions.Center;
        _nextButton.BackgroundColor = ProgressGold;
        _nextButton.CornerRadius = 26;
        _nextButton.Clicked += (_, _) =>
        {
            if (_slideIndex == Slides.Length - 1)
                Complete();
            else
                ShowSlide(_slideIndex + 1);
        };

        var controls = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Padding = new Thickness(18, 0),
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = ArtworkCream
        };
        controls.Add(_nextButton);

        _signInButton = CreateButton("Het jy reeds ’n rekening? Teken in", "onboarding-sign-in");
        _signInButton.FontSize = 13;
        _signInButton.TextColor = Color.FromArgb("#146D69");
        _signInButton.MinimumHeightRequest = 44;
        _signInButton.HorizontalOptions = LayoutOptions.Center;
        _signInButton.Margin = new Thickness(12, 0);
        _signInButton.Clicked += (_, _) => Complete(openSignIn: true);

        var footer = new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(
                SafeAreaRegions.Container,
                SafeAreaRegions.None,
                SafeAreaRegions.Container,
                SafeAreaRegions.Container),
            BackgroundColor = ArtworkCream,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        footer.Add(CreateProgressIndicator(), 0, 0);
        footer.Add(controls, 0, 1);
        footer.Add(_signInButton, 0, 2);

        var topControlsOverlay = new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(
                SafeAreaRegions.Container,
                SafeAreaRegions.Container,
                SafeAreaRegions.Container,
                SafeAreaRegions.None),
            InputTransparent = true,
            CascadeInputTransparent = false,
            Children = { _backButton, _skipButton }
        };
        var root = new Grid
        {
            BackgroundColor = ArtworkCream,
            SafeAreaEdges = SafeAreaEdges.None,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        root.Add(_stage, 0, 0);
        root.Add(topControlsOverlay, 0, 0);
        root.Add(footer, 0, 1);
        Content = root;
        ShowSlide(0);
    }

    private Grid CreateProgressIndicator()
    {
        var progress = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            WidthRequest = 176,
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Center,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(new GridLength(44))
            }
        };
        var connector = new BoxView
        {
            Color = ProgressInactive,
            WidthRequest = 132,
            HeightRequest = 5,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        progress.Add(connector, 0, 0);
        Grid.SetColumnSpan(connector, Slides.Length);
        for (var index = 0; index < Slides.Length; index++)
        {
            var slideIndex = index;
            var button = new Button
            {
                AutomationId = $"onboarding-step-{index + 1}",
                Text = "\uf111",
                FontFamily = "FontAwesomeSolid",
                FontSize = 18,
                TextColor = ProgressInactive,
                BackgroundColor = Colors.Transparent,
                BorderWidth = 0,
                Padding = 0
            };
            button.Clicked += (_, _) => ShowSlide(slideIndex);
            _progressButtons[index] = button;
            progress.Add(button, index, 0);
        }
        var motionLayer = new AbsoluteLayout
        {
            SafeAreaEdges = SafeAreaEdges.None,
            InputTransparent = true,
            Children = { _progressMarker }
        };
        AutomationProperties.SetIsInAccessibleTree(_progressMarker, false);
        progress.Add(motionLayer, 0, 0);
        Grid.SetColumnSpan(motionLayer, Slides.Length);
        return progress;
    }

    private void UpdateProgressMarker(bool animate)
    {
        _progressMarker.AbortAnimation(ProgressAnimationName);
        var targetLeft = (ProgressStepWidth - ProgressDotSize) / 2 + _slideIndex * ProgressStepWidth;
        var targetRight = targetLeft + ProgressDotSize;
        if (!animate || !IsLoaded || CharacterAnimations.ReduceMotion)
        {
            SetProgressMarker(targetLeft, targetRight);
            return;
        }

        // Start from the current shape when taps or swipes reverse an animation.
        // The leading edge arrives first, then the trailing edge catches up.
        var fromLeft = _progressLeft;
        var fromRight = _progressRight;
        var movingRight = targetLeft + targetRight > fromLeft + fromRight;
        var animation = new Animation(t =>
        {
            var leading = Easing.CubicOut.Ease(Math.Min(1, t / 0.72));
            var trailing = Easing.CubicInOut.Ease(Math.Clamp((t - 0.18) / 0.82, 0, 1));
            var left = fromLeft + (targetLeft - fromLeft) * (movingRight ? trailing : leading);
            var right = fromRight + (targetRight - fromRight) * (movingRight ? leading : trailing);
            SetProgressMarker(left, right);
        });
        animation.Commit(_progressMarker, ProgressAnimationName, length: 420,
            finished: (_, cancelled) =>
            {
                if (!cancelled)
                    SetProgressMarker(targetLeft, targetRight);
            });
    }

    private void SetProgressMarker(double left, double right)
    {
        var center = (left + right) / 2;
        var width = Math.Clamp(right - left, ProgressDotSize, ProgressMaximumWidth);
        _progressLeft = center - width / 2;
        _progressRight = center + width / 2;
        AbsoluteLayout.SetLayoutBounds(_progressMarker,
            new Rect(_progressLeft, (44 - ProgressDotSize) / 2, width, ProgressDotSize));
    }

    private static Button CreateButton(string text, string automationId) => new()
    {
        Text = text,
        AutomationId = automationId,
        FontFamily = "PoppinsSemiBold",
        FontSize = 16,
        TextColor = Color.FromArgb("#304158"),
        BackgroundColor = Colors.Transparent,
        BorderWidth = 0,
        MinimumHeightRequest = 48,
        Padding = new Thickness(12, 8)
    };

    private TabletOnboardingView CreateTabletComposition()
    {
        // Avoid loading a second set of artwork on phones.
        var composition = new TabletOnboardingView();
        _tabletComposition = composition;
        var forward = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        forward.Swiped += (_, _) => ShowSlide(_slideIndex + 1);
        var back = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
        back.Swiped += (_, _) => MoveBack();
        composition.GestureRecognizers.Add(forward);
        composition.GestureRecognizers.Add(back);
        composition.ShowSlide(_slideIndex);
        _stage.Children.Add(composition);
        return composition;
    }

    private void ResizeArtwork()
    {
        if (_stage.Width <= 0)
            return;

        // Use the actual window width so narrow iPad multitasking windows can
        // still use the approved compact composition. A landscape iPhone keeps
        // its existing layout rather than being mistaken for a tablet.
        var useTabletComposition = DeviceInfo.Idiom != DeviceIdiom.Phone && _stage.Width >= 600;
        _artworkViewport.IsVisible = !useTabletComposition;
        if (useTabletComposition)
        {
            var composition = _tabletComposition ?? CreateTabletComposition();
            composition.IsVisible = true;
#if IOS
            composition.SetSafeTop(
                Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.GetSafeAreaInsets(this).Top);
#endif
            return;
        }
        if (_tabletComposition is not null)
            _tabletComposition.IsVisible = false;

        if (_artworkViewport.Width <= 0)
            return;

        // Scale to the full viewport width. All supplied artwork and copy end
        // above y=1312; hide the blank tail and baked dots, then provide native
        // navigation below. Short screens can scroll without introducing gutters.
        var width = _artworkViewport.Width;
        var scale = width / 738d;
        _artwork.WidthRequest = width;
        _artwork.HeightRequest = 1600 * scale;
        _artworkCanvas.WidthRequest = width;
        _artworkCanvas.HeightRequest = 1312 * scale;
        AbsoluteLayout.SetLayoutBounds(_artwork, new Rect(0, 0, width, 1600 * scale));
#if IOS
        // Extend the teal background above the image only as far as needed to
        // keep the original logo (starting at y=74) clear of the camera cutout.
        var safeTop = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.GetSafeAreaInsets(this).Top;
        _topSpacer.HeightRequest = Math.Max(0, safeTop + 6 - 74 * scale);
#endif
    }

    private void ShowSlide(int index)
    {
        if (_isCompleting || index < 0 || index >= Slides.Length || (_hasShownSlide && index == _slideIndex))
            return;

        var animateProgress = _hasShownSlide;
        _hasShownSlide = true;
        _slideIndex = index;
        _artwork.Source = Slides[index].Image;
        _tabletComposition?.ShowSlide(index);
        SemanticProperties.SetDescription(_artwork, $"Skerm {index + 1} van {Slides.Length}. {Slides[index].Description}");
        SemanticProperties.SetHint(_artwork, "Vee links of regs, of gebruik die navigasieknoppies.");
        _backButton.IsEnabled = index > 0;
        _backButton.IsVisible = index > 0;
        _skipButton.IsVisible = index < Slides.Length - 1;
        _signInButton.IsVisible = index == 0;
        _nextButton.Text = index == Slides.Length - 1 ? "Kom ons begin" : "Volgende";
        for (var step = 0; step < _progressButtons.Length; step++)
        {
            SemanticProperties.SetDescription(_progressButtons[step],
                $"Skerm {step + 1} van {Slides.Length}" + (step == index ? ", huidige skerm" : string.Empty));
        }
        UpdateProgressMarker(animateProgress);
        UpdateSystemBackHandling();
        _ = _artworkViewport.ScrollToAsync(0, 0, false);
    }

    private bool MoveBack()
    {
        if (_slideIndex == 0 || _isCompleting)
            return false;

        ShowSlide(_slideIndex - 1);
        return true;
    }

    private void Complete(bool openSignIn = false)
    {
        if (_isCompleting)
            return;

        _isCompleting = true;
        OpenSignInOnCompletion = openSignIn;
        UpdateProgressMarker(animate: false);
        _nextButton.IsEnabled = false;
        _skipButton.IsEnabled = false;
        _signInButton.IsEnabled = false;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    protected override bool OnBackButtonPressed() => MoveBack() || base.OnBackButtonPressed();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Dispatcher.Dispatch(ResizeArtwork);
        UpdateSystemBackHandling();
    }

    protected override void OnDisappearing()
    {
        UpdateProgressMarker(animate: false);
#if ANDROID
        _backCallback?.Remove();
        _backCallback?.Dispose();
        _backCallback = null;
#endif
        base.OnDisappearing();
    }

    private void UpdateSystemBackHandling()
    {
#if ANDROID
        // Root Shell pages need the native dispatcher, as on AccountPage.
        if (_backCallback is null && Platform.CurrentActivity is AndroidX.Activity.ComponentActivity activity)
        {
            _backCallback = new OnboardingBackCallback(this);
            activity.OnBackPressedDispatcher.AddCallback(activity, _backCallback);
        }
        if (_backCallback is not null)
            _backCallback.Enabled = _slideIndex > 0 && !_isCompleting;
#endif
    }

#if ANDROID
    private sealed class OnboardingBackCallback(OnboardingPage page) : AndroidX.Activity.OnBackPressedCallback(false)
    {
        public override void HandleOnBackPressed() => page.MoveBack();
    }
#endif
}
