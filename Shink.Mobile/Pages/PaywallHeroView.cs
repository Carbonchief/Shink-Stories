using Microsoft.Maui.Controls.Shapes;

namespace Shink.Mobile.Pages;

internal sealed class PaywallHeroView : Grid
{
    private readonly AbsoluteLayout _artwork;
    private readonly Image _skillie;
    private readonly Image _oortjies;
    private readonly Image _panda;

    public PaywallHeroView(Func<Task> close)
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
        var logo = new Image
        {
            Source = ImageSource.FromStream(_ => FileSystem.OpenAppPackageFileAsync("schink_stories_logo_white_raw.png")),
            Aspect = Aspect.AspectFit, HeightRequest = 58, WidthRequest = 142,
            HorizontalOptions = LayoutOptions.Center, BackgroundColor = Colors.Transparent
        };
        SemanticProperties.SetDescription(logo, "Schink Stories");
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
        var header = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Padding = new Thickness(16, 3, 16, 0), HeightRequest = 64,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }
        };
        header.Add(parent); header.Add(logo, 1); header.Add(closeButton, 2);
        Children.Add(header);

        _skillie = Artwork("paywall_skillie.png");
        _oortjies = Artwork("paywall_oortjies.png");
        _panda = Artwork("paywall_prinses_panda.png");
        var background = Artwork("paywall_forest.png");
        var foreground = Artwork("paywall_book.png");
        _artwork = new AbsoluteLayout
        {
            SafeAreaEdges = SafeAreaEdges.None, IsClippedToBounds = true, HeightRequest = 180,
            Children = { background, _skillie, _oortjies, _panda, foreground },
            AutomationId = "paywall-original-character-artwork"
        };
        AbsoluteLayout.SetLayoutFlags(background, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(background, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(foreground, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(foreground, new Rect(0, 0, 1, 1));
        _artwork.SizeChanged += (_, _) => ResizeArtwork();
        Children.Add(_artwork);
        Grid.SetRow(_artwork, 1);
        SemanticProperties.SetDescription(_artwork, "Skillie, Oortjies en Prinses Panda by 'n oop storieboek.");
    }

    private static Image Artwork(string source) => new()
    {
        Source = source, Aspect = Aspect.AspectFit, InputTransparent = true,
        HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start
    };

    private void ResizeArtwork()
    {
        if (_artwork.Width <= 0) return;
        var scale = _artwork.Width / 514d;
        _artwork.HeightRequest = 241 * scale;
        // Original, unmodified 1080px PNGs. Positions account for their transparent margins.
        Place(_skillie, 171 - 111 - 153 * (150d / 914), 179 - 159 - 12 * (150d / 914), 1080 * (150d / 914), scale);
        Place(_oortjies, 284 - 111 - 17 * (209d / 1033), 172 - 159 - 93 * (209d / 1033), 1080 * (209d / 1033), scale);
        Place(_panda, 443 - 111 - 106 * (159d / 896), 186 - 159 - 18 * (159d / 896), 1080 * (159d / 896), scale);
    }

    private static void Place(View image, double x, double y, double size, double scale) =>
        AbsoluteLayout.SetLayoutBounds(image, new Rect(x * scale, y * scale, size * scale, size * scale));
}
