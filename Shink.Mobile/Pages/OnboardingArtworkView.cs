namespace Shink.Mobile.Pages;

internal sealed class OnboardingArtworkView : Grid
{
    private const string TransitionName = "onboarding-artwork-transition";
    private const string SwipeHint = "Vee links of regs, of gebruik die navigasieknoppies.";

    private Image _current = new() { Aspect = Aspect.AspectFit };
    private Image _incoming = new() { Aspect = Aspect.AspectFit, IsVisible = false };
    private (string File, string Description)? _requested;

    public OnboardingArtworkView(string automationId)
    {
        AutomationId = automationId;
        SafeAreaEdges = SafeAreaEdges.None;
        Children.Add(_current);
        Children.Add(_incoming);
        AutomationProperties.SetIsInAccessibleTree(_current, true);
        AutomationProperties.SetIsInAccessibleTree(_incoming, false);
        _current.PropertyChanged += OnImageLoadingChanged;
        _incoming.PropertyChanged += OnImageLoadingChanged;
    }

    public void Show(string file, string description, bool animate)
    {
        _requested = (file, description);
        if (!animate || !IsLoaded || CharacterAnimations.ReduceMotion)
        {
            FinishTransition();
            return;
        }

        LoadNextImage();
    }

    public void FinishTransition()
    {
        this.AbortAnimation(TransitionName);
        _incoming.IsVisible = false;
        _incoming.Source = null;

        if (_requested is not { } slide)
            return;

        _requested = null;
        SetImage(_current, slide.File, slide.Description);
    }

    private void LoadNextImage()
    {
        if (_incoming.IsVisible || _requested is not { } slide)
            return;

        if (_current.Source is FileImageSource current && current.File == slide.File)
        {
            _requested = null;
            return;
        }

        _incoming.Opacity = 0;
        _incoming.ZIndex = 1;
        _incoming.IsVisible = true;
        SetImage(_incoming, slide.File, slide.Description);
        Dispatcher.Dispatch(FadeWhenReady);
    }

    private void OnImageLoadingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (ReferenceEquals(sender, _incoming) && args.PropertyName == Image.IsLoadingProperty.PropertyName &&
            !_incoming.IsLoading)
        {
            Dispatcher.Dispatch(FadeWhenReady);
        }
    }

    private void FadeWhenReady()
    {
        if (!_incoming.IsVisible || _incoming.IsLoading || this.AnimationIsRunning(TransitionName))
            return;

        if (CharacterAnimations.ReduceMotion)
        {
            FinishTransition();
            return;
        }

        // Keep the current slide fully opaque below the fade to avoid flashing the backdrop.
        new Animation(value => _incoming.Opacity = value).Commit(this, TransitionName, length: 280,
            easing: Easing.SinInOut, finished: (_, cancelled) =>
            {
                if (cancelled)
                    return;

                AutomationProperties.SetIsInAccessibleTree(_current, false);
                _current.IsVisible = false;
                _current.Source = null;
                (_current, _incoming) = (_incoming, _current);
                _current.ZIndex = 0;
                AutomationProperties.SetIsInAccessibleTree(_current, true);

                // During rapid input keep only the most recently requested destination.
                LoadNextImage();
            });
    }

    private static void SetImage(Image image, string file, string description)
    {
        if (image.Source is not FileImageSource current || current.File != file)
            image.Source = file;

        SemanticProperties.SetDescription(image, description);
        SemanticProperties.SetHint(image, SwipeHint);
    }
}
