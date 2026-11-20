namespace Shink.Mobile.Pages;

internal sealed class TabletOnboardingView : AbsoluteLayout
{
    private const double ArtworkWidth = 2048;
    private const double ArtworkHeight = 2732;
    private const double ContentBottom = 2200;
    private const double LogoTop = 100;

    // The four JPEGs share this backdrop. Extend its colors beside the artwork in landscape.
    private static readonly (double Y, Color Color)[] BackdropColors =
    [
        (0, Color.FromArgb("#3EC3CC")),
        (160, Color.FromArgb("#3EC3CC")),
        (400, Color.FromArgb("#86CCD0")),
        (700, Color.FromArgb("#B5D1CC")),
        (1200, Color.FromArgb("#E7E9E4")),
        (1540, Color.FromArgb("#FCF8F3"))
    ];

    private readonly Image _artwork = new()
    {
        AutomationId = "onboarding-tablet-artwork",
        Aspect = Aspect.AspectFit
    };
    private readonly LinearGradientBrush _backdrop = new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1)
    };
    private double _safeTop;

    public TabletOnboardingView()
    {
        AutomationId = "onboarding-tablet-composition";
        SafeAreaEdges = SafeAreaEdges.None;
        IsClippedToBounds = true;

        foreach (var (y, color) in BackdropColors)
            _backdrop.GradientStops.Add(new GradientStop(color, (float)(y / ContentBottom)));
        Background = _backdrop;

        AutomationProperties.SetIsInAccessibleTree(_artwork, true);
        SemanticProperties.SetHint(_artwork, "Vee links of regs, of gebruik die navigasieknoppies.");
        Children.Add(_artwork);
        SizeChanged += (_, _) => ArrangeArtwork();
    }

    public void ShowSlide(int index, string description)
    {
        _artwork.Source = $"schink_onboarding_tablet_{index + 1:00}.jpg";
        SemanticProperties.SetDescription(_artwork, description);
    }

    public void SetSafeTop(double safeTop)
    {
        if (_safeTop == safeTop)
            return;

        _safeTop = safeTop;
        ArrangeArtwork();
    }

    private void ArrangeArtwork()
    {
        var logoClearance = _safeTop + 6;
        if (Width <= 0 || Height <= logoClearance)
            return;

        // Fit all visible content above the footer and leave room for the logo in short windows.
        var scale = Math.Min(Width / ArtworkWidth, Height / ContentBottom);
        scale = Math.Min(scale, (Height - logoClearance) / (ContentBottom - LogoTop));
        var width = ArtworkWidth * scale;
        var top = Math.Max(0, logoClearance - LogoTop * scale);

        SetLayoutBounds((BindableObject)_artwork, new Rect((Width - width) / 2, top, width, ArtworkHeight * scale));
        for (var index = 0; index < BackdropColors.Length; index++)
        {
            var y = BackdropColors[index].Y;
            _backdrop.GradientStops[index].Offset = (float)((top + y * scale) / Height);
        }
    }
}
