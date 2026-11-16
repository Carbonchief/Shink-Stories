namespace Shink.Mobile.Pages;

internal sealed class PaywallHeroView : Grid
{
    private const double ArtworkPixelWidth = 589;
    private const double ArtworkPixelHeight = 512;
    // Keep the bottom 330 source rows, including all three characters.
    private const double VisibleArtworkPixelHeight = 330;
    private readonly Grid _header;
    private readonly Image _logo;

    public PaywallHeroView(Func<Task> close, Color pageBackground)
    {
        BackgroundColor = Color.FromArgb("#00474E");
        SafeAreaEdges = SafeAreaEdges.None;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var lockLabel = new Label
        {
            Text = "\uf023", FontFamily = "FontAwesomeSolid", FontSize = 16,
            TextColor = Colors.White, VerticalTextAlignment = TextAlignment.Center
        };
        var parentLabel = new Label { Text = "Vir ouers", FontSize = 12, TextColor = Colors.White, VerticalTextAlignment = TextAlignment.Center };
        var parent = new HorizontalStackLayout { Spacing = 7, VerticalOptions = LayoutOptions.Center, Children = { lockLabel, parentLabel } };
        _logo = new Image
        {
            Source = ImageSource.FromStream(_ => FileSystem.OpenAppPackageFileAsync("schink_stories_logo_white_raw.png")),
            Aspect = Aspect.AspectFit, HeightRequest = 58, WidthRequest = 142,
            HorizontalOptions = LayoutOptions.Center, BackgroundColor = Colors.Transparent
        };
        SemanticProperties.SetDescription(_logo, "Schink Stories");
        var closeButton = new Button
        {
            Text = "\uf00d", FontFamily = "FontAwesomeSolid", FontSize = 22,
            WidthRequest = 44, HeightRequest = 44, CornerRadius = 22,
            BorderColor = Color.FromArgb("#649296"), BorderWidth = 1,
            BackgroundColor = Color.FromArgb("#154F54"), TextColor = Colors.White,
            Padding = 0, AutomationId = "paywall-close", HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(closeButton, "Sluit intekeningopsies");
        closeButton.Clicked += async (_, _) => await close();
        _header = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Padding = new Thickness(16, 3, 16, 0), HeightRequest = 64,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }
        };
        _header.Add(parent);
        _header.Add(_logo, 1);
        _header.Add(closeButton, 2);
        Children.Add(_header);

        var illustration = new Image
        {
            Source = "paywall_valley.png", Aspect = Aspect.AspectFit, InputTransparent = true
        };
        var artwork = new AbsoluteLayout
        {
            SafeAreaEdges = SafeAreaEdges.None, BackgroundColor = pageBackground,
            HeightRequest = 225, IsClippedToBounds = true,
            Children = { illustration },
            AutomationId = "paywall-original-character-artwork"
        };
        artwork.SizeChanged += (_, _) =>
        {
            if (artwork.Width <= 0) return;

            var scale = artwork.Width / ArtworkPixelWidth;
            var visibleHeight = VisibleArtworkPixelHeight * scale;
            var fullHeight = ArtworkPixelHeight * scale;
            artwork.HeightRequest = visibleHeight;
            AbsoluteLayout.SetLayoutBounds(illustration,
                new Rect(0, visibleHeight - fullHeight, artwork.Width, fullHeight));
        };
        Children.Add(artwork);
        Grid.SetRow(artwork, 1);
        SemanticProperties.SetDescription(artwork, "Prinses Panda, Oortjies en Skillie in 'n sonnige vallei.");
    }

    public void SetCompactHeader(bool compact)
    {
        _header.HeightRequest = compact ? 50 : 64;
        _logo.HeightRequest = compact ? 44 : 58;
    }
}
