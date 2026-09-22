namespace Shink.Mobile.Pages;

// Owns only presentation. StoryDetailPage and StoryPlaybackSession remain the
// playback authority so entering, leaving, or changing stories never forks audio.
internal sealed class StoryFullscreenPage : ContentPage
{
    private readonly Grid _artwork = new()
    {
        BackgroundColor = Colors.Black,
        SafeAreaEdges = SafeAreaEdges.None
    };
    private readonly Grid _top = new()
    {
        ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
        VerticalOptions = LayoutOptions.Start,
        Margin = new Thickness(12)
    };
    private readonly ContentView _controls = new()
    {
        VerticalOptions = LayoutOptions.End,
        HorizontalOptions = LayoutOptions.Fill,
        MaximumWidthRequest = 700
    };
    private readonly View _closeButton;
    private readonly IDispatcherTimer _idleTimer;
    private readonly Func<Task> _togglePlayback;
    private Window? _subscribedWindow;
    private bool _isVisible;
    private bool _isClosing;
    private bool _isScrubbing;
#if ANDROID
    private AndroidX.Core.View.WindowInsetsControllerCompat? _insetsController;
    private int _previousBarsBehavior;
    private int _previousVisibleBars;
#endif

    public StoryFullscreenPage(View closeButton, Func<Task> togglePlayback)
    {
        _closeButton = closeButton;
        _togglePlayback = togglePlayback;
        AutomationId = "story-fullscreen-page";
        BackgroundColor = Colors.Black;
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);
#if IOS
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetPrefersStatusBarHidden(
            this, Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.StatusBarHiddenMode.True);
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetModalPresentationStyle(
            On<Microsoft.Maui.Controls.PlatformConfiguration.iOS>(),
            Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle.FullScreen);
#endif
        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += async (_, _) => await CloseAsync();
        closeButton.GestureRecognizers.Add(closeTap);
        closeButton.AutomationId = "story-fullscreen-close";
        SemanticProperties.SetDescription(closeButton, "Verlaat volskerm speler");

        var artworkTap = new TapGestureRecognizer();
        artworkTap.Tapped += async (_, _) =>
        {
            RevealControls();
            await _togglePlayback();
        };
        _artwork.GestureRecognizers.Add(artworkTap);
        _artwork.AutomationId = "story-fullscreen-artwork";
        var overlay = new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
            InputTransparent = true,
            CascadeInputTransparent = false,
            Children = { _top, _controls }
        };
        Content = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            IsClippedToBounds = true,
            Children = { _artwork, overlay }
        };

        _idleTimer = Dispatcher.CreateTimer();
        _idleTimer.Interval = TimeSpan.FromMilliseconds(2200);
        _idleTimer.IsRepeating = false;
        _idleTimer.Tick += (_, _) =>
        {
            if (!_isVisible || _isScrubbing)
            {
                return;
            }

            _top.IsVisible = false;
            _controls.IsVisible = false;
        };
    }

    public void SetPlayerContent(View artwork, View controls, View favorite)
    {
        // Replace all callbacks with the current story, including after autoplay.
        _artwork.Children.Clear();
        artwork.InputTransparent = true;
        _artwork.Children.Add(artwork);
        _controls.Content = controls;
        _top.Children.Clear();
        favorite.HorizontalOptions = LayoutOptions.Start;
        favorite.Margin = 0;
        _top.Add(favorite, 0);
        _top.Add(_closeButton, 1);
        ObserveInteractions(controls);
        ObserveInteractions(favorite);
        RevealControls();
    }

    public void RevealControls()
    {
        _idleTimer.Stop();
        _top.IsVisible = true;
        _controls.IsVisible = true;
        if (_isVisible && !_isScrubbing)
        {
            _idleTimer.Start();
        }
    }

    private void ObserveInteractions(IVisualTreeElement element)
    {
        if (element is Button button)
        {
            button.Clicked += (_, _) => RevealControls();
        }
        if (element is View view)
        {
            foreach (var tap in view.GestureRecognizers.OfType<TapGestureRecognizer>())
            {
                tap.Tapped += (_, _) => RevealControls();
            }
        }
        if (element is Slider slider)
        {
            slider.DragStarted += (_, _) =>
            {
                _isScrubbing = true;
                RevealControls();
            };
            slider.DragCompleted += (_, _) =>
            {
                _isScrubbing = false;
                RevealControls();
            };
        }
        foreach (var child in element.GetVisualChildren())
        {
            ObserveInteractions(child);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
        _subscribedWindow = Window;
        if (_subscribedWindow is not null)
        {
            _subscribedWindow.Activated += OnWindowActivated;
            _subscribedWindow.Deactivated += OnWindowDeactivated;
        }
        HideSystemBars();
        RevealControls();
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        _idleTimer.Stop();
        if (_subscribedWindow is not null)
        {
            _subscribedWindow.Activated -= OnWindowActivated;
            _subscribedWindow.Deactivated -= OnWindowDeactivated;
            _subscribedWindow = null;
        }
        RestoreSystemBars();
        base.OnDisappearing();
    }

    private void OnWindowActivated(object? sender, EventArgs args)
    {
        HideSystemBars();
        RevealControls();
    }

    private void OnWindowDeactivated(object? sender, EventArgs args) => _idleTimer.Stop();

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync();
        return true;
    }

    private async Task CloseAsync()
    {
        if (_isClosing || Navigation.ModalStack.LastOrDefault() != this)
        {
            return;
        }
        _isClosing = true;
        try
        {
            await Navigation.PopModalAsync(true);
        }
        finally
        {
            _isClosing = false;
        }
    }

    private void HideSystemBars()
    {
#if ANDROID
        var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
        if (window?.DecorView is not { } decor)
        {
            return;
        }
        if (_insetsController is null)
        {
            _insetsController = AndroidX.Core.View.WindowCompat.GetInsetsController(window, decor);
            if (_insetsController is null)
            {
                return;
            }
            _previousBarsBehavior = _insetsController.SystemBarsBehavior;
            var insets = AndroidX.Core.View.ViewCompat.GetRootWindowInsets(decor);
            var status = AndroidX.Core.View.WindowInsetsCompat.Type.StatusBars();
            var navigation = AndroidX.Core.View.WindowInsetsCompat.Type.NavigationBars();
            _previousVisibleBars = (insets?.IsVisible(status) != false ? status : 0)
                | (insets?.IsVisible(navigation) != false ? navigation : 0);
        }
        _insetsController.SystemBarsBehavior =
            AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        _insetsController.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
#endif
    }

    private void RestoreSystemBars()
    {
#if ANDROID
        if (_insetsController is not null)
        {
            _insetsController.Show(_previousVisibleBars);
            _insetsController.SystemBarsBehavior = _previousBarsBehavior;
            _insetsController = null;
        }
#endif
    }
}
