namespace Shink.Mobile.Pages;

// Reflow the supplied phone designs without regenerating their illustrations.
// Regions are in the original 738 x 1600 JPEG coordinate space.
internal sealed class TabletOnboardingView : AbsoluteLayout
{
    private static readonly Color Cream = Color.FromArgb("#FBF6F0");
    private static readonly Color Ink = Color.FromArgb("#304158");
    private static readonly Rect[] IllustrationRegions =
    [
        new(0, 210, 738, 470),
        new(0, 578, 738, 766),
        new(0, 530, 738, 814),
        new(0, 298, 738, 451)
    ];

    private readonly ArtworkRegion _logo = new(new Rect(62, 68, 868, 325), softenEdges: false, new Size(983, 428));
    private readonly ArtworkRegion _illustration = new(IllustrationRegions[0], softenEdges: true);
    private readonly Label _heading = new()
    {
        AutomationId = "onboarding-tablet-heading",
        FontFamily = "SchinkOnboardingRounded",
        TextColor = Ink,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly Label _introduction = CreateCopyLabel();
    private readonly VerticalStackLayout _features = new() { Spacing = 5, SafeAreaEdges = SafeAreaEdges.None };
    private readonly Label _closing = CreateCopyLabel();
    private readonly VerticalStackLayout _copy;
    private readonly ScrollView _copyViewport;
    private int _slideIndex;
    private double _safeTop;
    private bool _arranging;

    public TabletOnboardingView()
    {
        AutomationId = "onboarding-tablet-composition";
        SafeAreaEdges = SafeAreaEdges.None;
        IsClippedToBounds = true;
        Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb("#36B7C1"), 0),
                new GradientStop(Color.FromArgb("#36B7C1"), 0.10f),
                new GradientStop(Cream, 0.22f),
                new GradientStop(Cream, 1)
            }
        };
        SemanticProperties.SetHeadingLevel(_heading, SemanticHeadingLevel.Level1);
        _logo.SetArtwork("schink_stories_logo_white.png", new Rect(62, 68, 868, 325));
        _copy = new VerticalStackLayout
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Spacing = 14,
            Children = { _heading, _introduction, _features, _closing }
        };
        // Normal text fits without scrolling. Keep an accessible overflow path
        // when Dynamic Type makes the copy taller than a small tablet window.
        _copyViewport = new ScrollView
        {
            AutomationId = "onboarding-tablet-copy",
            SafeAreaEdges = SafeAreaEdges.None,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _copy
        };
        Children.Add(_logo);
        Children.Add(_illustration);
        Children.Add(_copyViewport);
        SizeChanged += (_, _) => ArrangeComposition();
        Loaded += (_, _) => ArrangeComposition();
        ShowSlide(0);
    }

    public void SetSafeTop(double safeTop)
    {
        _safeTop = safeTop;
        ArrangeComposition();
    }

    public void ShowSlide(int index)
    {
        _slideIndex = index;
        _illustration.SetArtwork($"schink_onboarding_{index + 1:00}.jpg", IllustrationRegions[index]);
        _features.Children.Clear();
        _closing.IsVisible = index == 3;
        _features.IsVisible = index == 0;
        _introduction.FormattedText = null;
        _introduction.Text = string.Empty;
        switch (index)
        {
            case 0:
                _heading.Text = "Welkom by\nSchink Stories";
                _introduction.Text = "Kom geniet opbouende Afrikaanse stories wat kinders laat luister, lag en leer!";
                foreach (var (emphasis, detail) in new[]
                {
                    ("120+", " Afrikaanse oudiostories"), ("Waardes", " en lewenslesse"), ("Geen", " donkertemas"),
                    ("Geen", " advertensies"), ("Nuwe stories", " elke maand")
                })
                {
                    var row = new Grid
                    {
                        SafeAreaEdges = SafeAreaEdges.None,
                        ColumnSpacing = 12,
                        ColumnDefinitions = { new ColumnDefinition(12), new ColumnDefinition(GridLength.Star) }
                    };
                    var bullet = new Border
                    {
                        WidthRequest = 7,
                        HeightRequest = 7,
                        BackgroundColor = Color.FromArgb("#FFBD0A"),
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
                        VerticalOptions = LayoutOptions.Center,
                        InputTransparent = true
                    };
                    AutomationProperties.SetIsInAccessibleTree(bullet, false);
                    row.Add(bullet);
                    var label = CreateCopyLabel();
                    label.FormattedText = Copy((emphasis, true), (detail, false));
                    row.Add(label, 1, 0);
                    _features.Children.Add(row);
                }
                break;
            case 1:
                _heading.Text = "Stories waar\njy ook al is!";
                _introduction.FormattedText = Copy(
                    ("Laai julle gunsteling stories af en ", false), ("luister sonder internet.", true),
                    ("\nBy die huis, op die langpad of waar julle ook al gaan.", false));
                break;
            case 2:
                _heading.Text = "Ontmoet die\nkarakters";
                _introduction.FormattedText = Copy(
                    ("Daar is soveel stories om te ontdek en karakters om te leer ken!\n", false),
                    ("Luister, speel en ontdek.", true));
                break;
            case 3:
                _heading.Text = "Dis storietyd!";
                _introduction.Text = "Kom lag, kom luister\nen kom leer!";
                _closing.FormattedText = Copy(("Begin ", false), ("gratis", true),
                    (" met stories,\nof kry volle toegang\ntot al ons stories.", false));
                break;
        }
        ArrangeComposition();
        _ = _copyViewport.ScrollToAsync(0, 0, false);
    }

    private void ArrangeComposition()
    {
        if (_arranging || Width <= 0 || Height <= 0)
            return;

        _arranging = true;
        try
        {
            var landscape = Width >= 900 && Width > Height * 1.12;
            var logoHeight = Height < 720 ? 72d : 84d;
            var logoTop = _safeTop + 16;
            AbsoluteLayout.SetLayoutBounds((BindableObject)_logo, new Rect((Width - logoHeight * 868 / 325) / 2, logoTop,
                logoHeight * 868 / 325, logoHeight));
            var bodyTop = logoTop + logoHeight + 64;
            if (Background is LinearGradientBrush backdrop)
            {
                backdrop.GradientStops[1].Offset = (float)Math.Clamp((logoTop + logoHeight - 8) / Height, 0, 1);
                backdrop.GradientStops[2].Offset = (float)Math.Clamp(bodyTop / Height, 0, 1);
            }
            var bodyHeight = Math.Max(1, Height - bodyTop - 16);
            var bodyWidth = Math.Min(1120, Width - (landscape ? 64 : 48));
            var bodyLeft = (Width - bodyWidth) / 2;
            var textWidth = landscape ? Math.Min(470, bodyWidth * 0.43) : Math.Min(600, bodyWidth);
            var textSize = Math.Clamp(textWidth / 24, 19, 23);
            _heading.FontSize = landscape ? Math.Clamp(textWidth / 10.8, 32, 42) : Math.Clamp(textWidth / 14, 34, 42);
            _heading.HorizontalTextAlignment = landscape ? TextAlignment.Start : TextAlignment.Center;
            _introduction.HorizontalTextAlignment = _heading.HorizontalTextAlignment;
            _closing.HorizontalTextAlignment = _heading.HorizontalTextAlignment;
            _introduction.FontSize = textSize;
            _closing.FontSize = textSize;
            foreach (var row in _features.Children.OfType<Grid>())
                foreach (var label in row.Children.OfType<Label>())
                    label.FontSize = Math.Min(textSize, 21);
            _features.HorizontalOptions = landscape ? LayoutOptions.Fill : LayoutOptions.Center;
            _features.WidthRequest = Math.Min(textWidth, 430);
            var desiredCopyHeight = ((Microsoft.Maui.IView)_copy).Measure(textWidth, double.PositiveInfinity).Height;

            if (landscape)
            {
                var artWidth = bodyWidth - textWidth - 28;
                AbsoluteLayout.SetLayoutBounds((BindableObject)_illustration, new Rect(bodyLeft, bodyTop, artWidth, bodyHeight));
                var copyHeight = Math.Min(bodyHeight, desiredCopyHeight);
                AbsoluteLayout.SetLayoutBounds((BindableObject)_copyViewport, new Rect(bodyLeft + artWidth + 28,
                    bodyTop + (bodyHeight - copyHeight) / 2, textWidth, copyHeight));
            }
            else
            {
                var copyHeight = Math.Min(desiredCopyHeight, bodyHeight * 0.60);
                var gap = 18d;
                var artHeight = Math.Min(bodyHeight - copyHeight - gap, _slideIndex is 0 or 3 ? 440 : 530);
                artHeight = Math.Max(1, artHeight);
                var totalHeight = artHeight + gap + copyHeight;
                var top = bodyTop + Math.Max(0, (bodyHeight - totalHeight) / 2);
                var copyFirst = _slideIndex is 1 or 2;
                AbsoluteLayout.SetLayoutBounds((BindableObject)_illustration, new Rect(bodyLeft, copyFirst ? top + copyHeight + gap : top,
                    bodyWidth, artHeight));
                AbsoluteLayout.SetLayoutBounds((BindableObject)_copyViewport, new Rect((Width - textWidth) / 2,
                    copyFirst ? top : top + artHeight + gap, textWidth, copyHeight));
            }
        }
        finally
        {
            _arranging = false;
        }
    }

    private static Label CreateCopyLabel() => new()
    {
        FontFamily = "Poppins",
        TextColor = Ink,
        LineBreakMode = LineBreakMode.WordWrap
    };

    private static FormattedString Copy(params (string Text, bool Bold)[] pieces)
    {
        var text = new FormattedString();
        foreach (var piece in pieces)
            text.Spans.Add(new Span { Text = piece.Text, FontFamily = piece.Bold ? "PoppinsSemiBold" : "Poppins" });
        return text;
    }

    private sealed class ArtworkRegion : AbsoluteLayout
    {
        private readonly Image _image = new() { Aspect = Aspect.AspectFit, InputTransparent = true };
        private readonly bool _softenEdges;
        private readonly Size _sourceSize;
        private readonly BoxView[] _edgeFades;
        private Rect _region;

        public ArtworkRegion(Rect region, bool softenEdges, Size? sourceSize = null)
        {
            _region = region;
            _softenEdges = softenEdges;
            _sourceSize = sourceSize ?? new Size(738, 1600);
            SafeAreaEdges = SafeAreaEdges.None;
            IsClippedToBounds = true;
            InputTransparent = true;
            AutomationProperties.SetIsInAccessibleTree(_image, false);
            Children.Add(_image);
            _edgeFades = softenEdges
                ? [Fade(new Point(0, 0), new Point(1, 0)), Fade(new Point(1, 0), new Point(0, 0)),
                   Fade(new Point(0, 0), new Point(0, 1)), Fade(new Point(0, 1), new Point(0, 0))]
                : [];
            foreach (var fade in _edgeFades)
                Children.Add(fade);
            SizeChanged += (_, _) => ResizeRegion();
        }

        public void SetArtwork(string source, Rect region)
        {
            _image.Source = source;
            _region = region;
            ResizeRegion();
        }

        private void ResizeRegion()
        {
            if (Width <= 0 || Height <= 0)
                return;

            var scale = Math.Min(Width / _region.Width, Height / _region.Height);
            var width = _region.Width * scale;
            var height = _region.Height * scale;
            var left = (Width - width) / 2;
            var top = (Height - height) / 2;
            // Clip the original file at runtime, keeping every source pixel and
            // all in-image phone previews/character details in their original form.
            _image.WidthRequest = _sourceSize.Width * scale;
            _image.HeightRequest = _sourceSize.Height * scale;
            AbsoluteLayout.SetLayoutBounds((BindableObject)_image, new Rect(left - _region.X * scale, top - _region.Y * scale,
                _sourceSize.Width * scale, _sourceSize.Height * scale));
            Clip = new Microsoft.Maui.Controls.Shapes.RectangleGeometry(new Rect(left, top, width, height));
            if (!_softenEdges)
                return;

            var sideFade = Math.Min(26, width * 0.035);
            var verticalFade = Math.Min(16, height * 0.03);
            AbsoluteLayout.SetLayoutBounds((BindableObject)_edgeFades[0], new Rect(left, top, sideFade, height));
            AbsoluteLayout.SetLayoutBounds((BindableObject)_edgeFades[1], new Rect(left + width - sideFade, top, sideFade, height));
            AbsoluteLayout.SetLayoutBounds((BindableObject)_edgeFades[2], new Rect(left, top, width, verticalFade));
            AbsoluteLayout.SetLayoutBounds((BindableObject)_edgeFades[3], new Rect(left, top + height - verticalFade, width, verticalFade));
        }

        private static BoxView Fade(Point start, Point end) => new()
        {
            InputTransparent = true,
            Background = new LinearGradientBrush
            {
                StartPoint = start,
                EndPoint = end,
                GradientStops = { new GradientStop(Cream, 0), new GradientStop(Cream.WithAlpha(0), 1) }
            }
        };
    }
}
