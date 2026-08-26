namespace Shink.Mobile.Pages;

public sealed class KarakterPareConfigPage : ContentPage, IQueryAttributable
{
    private static readonly Color PageTopColor = Color.FromArgb("#298899");
    private static readonly Color PageBottomColor = Color.FromArgb("#6EC0C8");
    private static readonly Color HeadingColor = Color.FromArgb("#285A68");
    private static readonly Color BodyColor = Color.FromArgb("#3C5E66");
    private static readonly Color GoldColor = Color.FromArgb("#F3CC59");
    private static readonly Color SelectedCardColor = Color.FromArgb("#FFF3BE");
    private static readonly Color UnselectedCardColor = Color.FromArgb("#F1F6F4");
    private static readonly Color SelectedStrokeColor = Color.FromArgb("#E4B63F");
    private static readonly Color UnselectedStrokeColor = Color.FromArgb("#E2E1D8");
    private const string PoppinsFontFamily = "Poppins";
    private const string PoppinsBoldFontFamily = "PoppinsBold";

    private static readonly DifficultyOption[] Options =
    [
        new("easy", "BEGINNER", 6, "karakter_pare_beginner.png"),
        new("medium", "KENNER", 8, "karakter_pare_kenner.png"),
        new("hard", "MEESTER", 12, "karakter_pare_meester.png")
    ];

    private readonly List<Border> _optionCards = [];
    private readonly Button _playButton;
    private DifficultyOption _selectedOption;

    public KarakterPareConfigPage()
    {
        Title = "Karakter Pare";
        Background = BuildBackground();
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);
        _selectedOption = Options[0];

        var content = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(0, 128, 0, 52),
            Children =
            {
                new Image
                {
                    Source = "karakter_pare_logo_cropped.png",
                    Aspect = Aspect.AspectFit,
                    WidthRequest = 282,
                    HeightRequest = 240,
                    Margin = new Thickness(0, 0, 0, 25),
                    HorizontalOptions = LayoutOptions.Center,
                    AutomationId = "karakter-pare-hero-logo"
                },
                BuildTagline(),
                BuildDifficultyOptions(),
            }
        };

        _playButton = new Button
        {
            Text = "SPEEL NOU",
            FontFamily = PoppinsBoldFontFamily,
            FontSize = 16,
            TextColor = HeadingColor,
            BackgroundColor = GoldColor,
            CornerRadius = 20,
            HeightRequest = 48,
            WidthRequest = 172,
            Padding = new Thickness(16, 0),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 12, 0, 0),
            AutomationId = "karakter-pare-play"
        };
        _playButton.Clicked += async (_, _) => await PlaySelectedDifficultyAsync();
        content.Children.Add(_playButton);

        var root = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Children =
            {
                content,
                BuildBackButton()
            }
        };
        Content = root;
        SizeChanged += (_, _) => ApplyResponsiveLayout(content);
        ApplyResponsiveLayout(content);
        SelectDifficulty(_selectedOption);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("difficulty", out var rawDifficulty))
        {
            return;
        }

        var difficulty = Options.FirstOrDefault(option =>
            option.Level.Equals(rawDifficulty?.ToString(), StringComparison.OrdinalIgnoreCase));
        if (difficulty is not null)
        {
            SelectDifficulty(difficulty);
        }
    }

    private static LinearGradientBrush BuildBackground() =>
        new(
            new GradientStopCollection
            {
                new(PageTopColor, 0),
                new(Color.FromArgb("#3A9FAA"), 0.5f),
                new(PageBottomColor, 1)
            },
            new Point(0, 0),
            new Point(0, 1));

    private static VerticalStackLayout BuildTagline()
    {
        var firstLine = new Label
        {
            FontFamily = PoppinsFontFamily,
            FontSize = 16,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            CharacterSpacing = 0.1,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = "Draai die kaartjies ", FontFamily = PoppinsFontFamily },
                    new Span { Text = "paar vir paar,", FontFamily = PoppinsBoldFontFamily }
                }
            }
        };
        var secondLine = new Label
        {
            FontFamily = PoppinsFontFamily,
            FontSize = 16,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            CharacterSpacing = 0.1,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = "laat die karakters ", FontFamily = PoppinsFontFamily },
                    new Span { Text = "pas bymekaar!", FontFamily = PoppinsBoldFontFamily }
                }
            }
        };

        return new VerticalStackLayout
        {
            Spacing = 0,
            Margin = new Thickness(16, 0, 16, 0),
            Children = { firstLine, secondLine }
        };
    }

    private VerticalStackLayout BuildDifficultyOptions()
    {
        var options = new VerticalStackLayout
        {
            Spacing = 11,
            WidthRequest = 270,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 47, 0, 0),
            AutomationId = "karakter-pare-difficulty-options"
        };

        foreach (var option in Options)
        {
            var (container, card) = BuildDifficultyCard(option);
            _optionCards.Add(card);
            options.Children.Add(container);
        }

        return options;
    }

    private (Grid Container, Border Card) BuildDifficultyCard(DifficultyOption option)
    {
        var title = new Label
        {
            Text = option.DisplayName,
            FontFamily = PoppinsBoldFontFamily,
            FontSize = 21,
            TextColor = HeadingColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.End
        };
        var pairs = new Label
        {
            Text = $"{option.PairCount} Pare",
            FontFamily = PoppinsFontFamily,
            FontSize = 14,
            TextColor = BodyColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Start
        };
        var text = new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Children = { title, pairs }
        };
        var cardContent = new Grid
        {
            Children = { text }
        };

        var card = new Border
        {
            HeightRequest = 58,
            Padding = new Thickness(10, 0, 8, 0),
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = cardContent,
            AutomationId = $"karakter-pare-difficulty-{option.Level}"
        };
        SemanticProperties.SetDescription(card, $"Kies {option.DisplayName.ToLowerInvariant()}, {option.PairCount} pare");

        var character = new Image
        {
            Source = option.ImageSource,
            Aspect = Aspect.AspectFit,
            WidthRequest = 62,
            HeightRequest = 64,
            Margin = new Thickness(0, 0, 14, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            ZIndex = 2,
            InputTransparent = true
        };
        var container = new Grid
        {
            HeightRequest = 58,
            Children = { card, character }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectDifficulty(option);
        card.GestureRecognizers.Add(tap);
        return (container, card);
    }

    private Border BuildBackButton()
    {
        var backButton = new Border
        {
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            WidthRequest = 54,
            HeightRequest = 54,
            Margin = new Thickness(14, 44, 0, 0),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            ZIndex = 20,
            Content = new GraphicsView
            {
                Drawable = new BackChevronDrawable(),
                WidthRequest = 38,
                HeightRequest = 38,
                InputTransparent = true
            },
            AutomationId = "karakter-pare-back"
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Shell.Current.GoToAsync("..", animate: false);
        backButton.GestureRecognizers.Add(tap);
        return backButton;
    }

    private void SelectDifficulty(DifficultyOption option)
    {
        _selectedOption = option;
        for (var index = 0; index < _optionCards.Count; index++)
        {
            var isSelected = Options[index].Level == option.Level;
            var card = _optionCards[index];
            card.BackgroundColor = isSelected ? SelectedCardColor : UnselectedCardColor;
            card.Stroke = isSelected ? SelectedStrokeColor : UnselectedStrokeColor;
            card.Opacity = isSelected ? 1 : 0.98;
        }
    }

    private async Task PlaySelectedDifficultyAsync()
    {
        if (!_playButton.IsEnabled)
        {
            return;
        }

        _playButton.IsEnabled = false;
        try
        {
            var parameters = new ShellNavigationQueryParameters
            {
                ["difficulty"] = _selectedOption.Level
            };
            await Shell.Current.GoToAsync(nameof(KarakterPareGamePage), parameters);
        }
        finally
        {
            _playButton.IsEnabled = true;
        }
    }

    private static void ApplyResponsiveLayout(View content)
    {
        MobileResponsiveLayout.ApplyCenteredContent(content, DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density, 640);
    }

    private sealed record DifficultyOption(
        string Level,
        string DisplayName,
        int PairCount,
        string ImageSource);

    private sealed class BackChevronDrawable : Microsoft.Maui.Graphics.IDrawable
    {
        public void Draw(Microsoft.Maui.Graphics.ICanvas canvas, Microsoft.Maui.Graphics.RectF dirtyRect)
        {
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 5;
            canvas.StrokeLineCap = Microsoft.Maui.Graphics.LineCap.Round;
            canvas.StrokeLineJoin = Microsoft.Maui.Graphics.LineJoin.Round;

            var centerX = dirtyRect.Width * 0.48f;
            var centerY = dirtyRect.Height * 0.5f;
            var halfWidth = dirtyRect.Width * 0.22f;
            var halfHeight = dirtyRect.Height * 0.18f;
            canvas.DrawLine(centerX + halfWidth, centerY - halfHeight, centerX - halfWidth, centerY);
            canvas.DrawLine(centerX - halfWidth, centerY, centerX + halfWidth, centerY + halfHeight);
        }
    }
}
