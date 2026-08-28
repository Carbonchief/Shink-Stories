using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

internal static class MobileBottomBar
{
    public static View Build(Page hostPage, string selectedDestination, Func<Task>? searchAction = null)
    {
        var navigationGate = new NavigationGate();
        var items = new[]
        {
            (Destination: "listen", Label: "Stories", AndroidIcon: MobileAndroidIcon.Home, Action: (Func<Task>)(() => OpenRouteAsync("//Luister"))),
            (Destination: "search", Label: "Soek", AndroidIcon: MobileAndroidIcon.Search, Action: searchAction ?? (Func<Task>)(() => OpenRouteAsync(nameof(SearchPage)))),
            (Destination: "downloads", Label: "Afgelaai", AndroidIcon: MobileAndroidIcon.Download, Action: (Func<Task>)(() => OpenRouteAsync(nameof(DownloadedPage)))),
            (Destination: "characters", Label: "Karakters", AndroidIcon: MobileAndroidIcon.Profile, Action: (Func<Task>)(() => OpenRouteAsync("//Karakters")))
        };

        var itemGrid = new Grid
        {
            ColumnSpacing = 0,
            VerticalOptions = LayoutOptions.End,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var isSelected = item.Destination == selectedDestination;
            var itemView = BuildBottomTabItem(item.Label, item.AndroidIcon, isSelected);
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
                await navigationGate.RunAsync(item.Action);
            };
            itemView.GestureRecognizers.Add(tap);
            Grid.SetColumn(itemView, index);
            itemGrid.Children.Add(itemView);
        }

        var bar = new Border
        {
            BackgroundColor = MobileAndroidChromePalette.BarBackground,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 0 },
            Padding = new Thickness(10, 2, 10, 4),
            Margin = Thickness.Zero,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            Content = itemGrid
        };
        bar.HeightRequest = 114;

        var staticBackdrop = new BoxView
        {
            Color = MobileAndroidChromePalette.BarBackdropTint,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };

        var host = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            BackgroundColor = Colors.Transparent,
            AutomationId = "mobile-bottom-navigation",
            Children = { staticBackdrop, bar }
        };

        return host;
    }

    private static Border BuildBottomTabItem(string label, MobileAndroidIcon icon, bool isSelected)
    {
        var color = isSelected
            ? MobileAndroidChromePalette.PrimaryIcon
            : MobileAndroidChromePalette.SecondaryIcon;
        return new Border
        {
            BackgroundColor = isSelected ? MobileAndroidChromePalette.SelectedBackground : Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(2, 4),
            Margin = new Thickness(1, 0),
            AutomationId = $"mobile-bottom-{label.ToLowerInvariant()}",
            Content = new VerticalStackLayout
            {
                Spacing = 5,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new GraphicsView
                    {
                        Drawable = new MobileAndroidIconDrawable(icon, color),
                        WidthRequest = 32,
                        HeightRequest = 32,
                        HorizontalOptions = LayoutOptions.Center,
                        InputTransparent = true
                    },
                    new Label
                    {
                        Text = label,
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = color,
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode = LineBreakMode.TailTruncation,
                        InputTransparent = true
                    }
                }
            }
        };
    }

    private static Task OpenRouteAsync(string route) =>
        Shell.Current.GoToAsync(route, animate: false);
}
