namespace Shink.Mobile.Pages;

internal sealed class TabletOnboardingView : AbsoluteLayout
{
    private const double LogoTop = 100;
    private static readonly (double Width, double Height, double ContentBottom) PortraitArtwork = (2048, 2732, 2200);
    // Leave room below the tallest illustration so navigation never covers it.
    private static readonly (double Width, double Height, double ContentBottom) LandscapeArtwork = (2732, 2048, 1780);

    // Extend the image's edge colors where the artwork is narrower than the window.
    private static readonly (double Y, Color Color)[] PortraitBackdropColors =
    [
        (0, Color.FromArgb("#3EC3CC")),
        (160, Color.FromArgb("#3EC3CC")),
        (400, Color.FromArgb("#86CCD0")),
        (700, Color.FromArgb("#B5D1CC")),
        (1200, Color.FromArgb("#E7E9E4")),
        (1540, Color.FromArgb("#FCF8F3"))
    ];
    private static readonly (double Y, Color Color)[] LandscapeBackdropColors =
    [
        (0, Color.FromArgb("#3EC3CC")),
        (350, Color.FromArgb("#72C8CF")),
        (600, Color.FromArgb("#9BCFCC")),
        (900, Color.FromArgb("#BFD6CC")),
        (1200, Color.FromArgb("#EEEEE4")),
        (1400, Color.FromArgb("#FCF8F3"))
    ];

    private readonly OnboardingArtworkView _artwork = new("onboarding-tablet-artwork");
    private readonly LinearGradientBrush _backdrop = new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1)
    };
    private double _safeTop;
    private int _slideIndex;
    private string _description = string.Empty;
    private bool _isLandscape;

    public TabletOnboardingView(bool isLandscape, double safeTop)
    {
        _isLandscape = isLandscape;
        _safeTop = safeTop;
        AutomationId = "onboarding-tablet-composition";
        SafeAreaEdges = SafeAreaEdges.None;
        IsClippedToBounds = true;

        foreach (var (y, color) in PortraitBackdropColors)
            _backdrop.GradientStops.Add(new GradientStop(color, (float)(y / PortraitArtwork.ContentBottom)));
        Background = _backdrop;

        Children.Add(_artwork);
        SizeChanged += (_, _) => ArrangeArtwork();
    }

    public void ShowSlide(int index, string description, bool animate = false)
    {
        _slideIndex = index;
        _description = description;
        UpdateArtwork(animate);
    }

    public void FinishTransition() => _artwork.FinishTransition();

    public void SetViewport(bool isLandscape, double safeTop)
    {
        if (_isLandscape == isLandscape && _safeTop == safeTop)
            return;

        var orientationChanged = _isLandscape != isLandscape;
        _isLandscape = isLandscape;
        _safeTop = safeTop;
        if (orientationChanged)
            UpdateArtwork(animate: false);
        else
            ArrangeArtwork();
    }

    private void UpdateArtwork(bool animate)
    {
        var image = _isLandscape
            ? $"schink_onboarding_tablet_landscape_{_slideIndex + 1:00}.jpg"
            : $"schink_onboarding_tablet_{_slideIndex + 1:00}.jpg";
        _artwork.Show(image, _description, animate);

        ArrangeArtwork();
    }

    private void ArrangeArtwork()
    {
        var logoClearance = _safeTop + 6;
        if (Width <= 0 || Height <= logoClearance)
            return;

        var (artworkWidth, artworkHeight, contentBottom) = _isLandscape ? LandscapeArtwork : PortraitArtwork;
        // Fit the illustrated content and copy above the footer, clipping only the blank tail.
        var scale = Math.Min(Width / artworkWidth, Height / contentBottom);
        scale = Math.Min(scale, (Height - logoClearance) / (contentBottom - LogoTop));
        var width = artworkWidth * scale;
        var top = Math.Max(0, logoClearance - LogoTop * scale);

        SetLayoutBounds((BindableObject)_artwork, new Rect((Width - width) / 2, top, width, artworkHeight * scale));
        var backdropColors = _isLandscape ? LandscapeBackdropColors : PortraitBackdropColors;
        for (var index = 0; index < backdropColors.Length; index++)
        {
            var (y, color) = backdropColors[index];
            var stop = _backdrop.GradientStops[index];
            stop.Color = color;
            stop.Offset = (float)((top + y * scale) / Height);
        }
    }
}
