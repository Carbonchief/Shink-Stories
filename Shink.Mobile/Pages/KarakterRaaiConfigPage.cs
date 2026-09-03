using Shink.Mobile.Games;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class KarakterRaaiConfigPage : ContentPage
{
    private static readonly Color PageTopColor = Color.FromArgb("#4DBA88");
    private static readonly Color PageBottomColor = Color.FromArgb("#2D7187");
    private static readonly Color HeadingColor = Color.FromArgb("#285A68");
    private static readonly Color BodyColor = Color.FromArgb("#3C5E66");
    private static readonly Color SelectedCardColor = Color.FromArgb("#FFF3BE");
    private static readonly Color UnselectedCardColor = Color.FromArgb("#F1F6F4");
    private static readonly Color SelectedStrokeColor = Color.FromArgb("#E4B63F");
    private static readonly Color UnselectedStrokeColor = Color.FromArgb("#E2E1D8");
    private static readonly Color CloseColor = Color.FromArgb("#C93F45");
    private static readonly Color CloseBackgroundColor = Color.FromArgb("#FFF4F2");
    private static readonly Color CloseStrokeColor = Color.FromArgb("#E77B78");
    private const string PoppinsFontFamily = "Poppins";
    private const string PoppinsBoldFontFamily = "PoppinsBold";
    private const double CompactLayoutHeight = 700;

    private readonly IReadOnlyList<CharacterGuessDifficultyOption> _options =
        CharacterGuessDifficultyCatalog.Options;
    private readonly List<Border> _optionCards = [];
    private bool _isStartingGame;
    private bool _isNavigatingBack;

    public KarakterRaaiConfigPage(StoryPlaybackSession storyPlaybackSession)
    {
        Title = "Karakter Raai";
        Background = BuildBackground();
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);

        var heroLogo = new Image
        {
            Source = "karakter_raai_logo_cropped.png",
            Aspect = Aspect.AspectFit,
            WidthRequest = 270,
            HeightRequest = 237,
            Margin = new Thickness(0, 0, 0, 18),
            HorizontalOptions = LayoutOptions.Center,
            AutomationId = "karakter-raai-hero-logo"
        };
        var tagline = BuildTagline();
        var difficultyOptions = BuildDifficultyOptions();
        var content = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(0, 80, 0, 8),
            Children =
            {
                heroLogo,
                tagline,
                difficultyOptions,
            }
        };
        var closeButton = BuildCloseButton();
        content.Children.Add(closeButton);

        var contentScroll = new ScrollView
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            AutomationId = "karakter-raai-config-scroll"
        };
        var root = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Children =
            {
                contentScroll,
                BuildBackButton()
            }
        };
        Content = PersistentPlaybackHost.Wrap(root, storyPlaybackSession);
        SizeChanged += (_, _) => ApplyResponsiveLayout(content, heroLogo, difficultyOptions, closeButton);
        ApplyResponsiveLayout(content, heroLogo, difficultyOptions, closeButton);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ClearDifficultySelection();
    }

    private static LinearGradientBrush BuildBackground() =>
        new(
            new GradientStopCollection
            {
                new(PageTopColor, 0),
                new(Color.FromArgb("#43AD92"), 0.43f),
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
                    new Span { Text = "Raai-raai, ", FontFamily = PoppinsBoldFontFamily },
                    new Span { Text = "wie kruip daar weg?" }
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
                    new Span { Text = "Kies jou antwoord, ", FontFamily = PoppinsBoldFontFamily },
                    new Span { Text = "dalk is jy reg!" }
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
            AutomationId = "karakter-raai-difficulty-options"
        };

        foreach (var option in _options)
        {
            var (container, card) = BuildDifficultyCard(option);
            _optionCards.Add(card);
            options.Children.Add(container);
        }

        return options;
    }

    private (Grid Container, Border Card) BuildDifficultyCard(CharacterGuessDifficultyOption option)
    {
        var title = new Label
        {
            Text = option.DisplayName,
            FontFamily = PoppinsBoldFontFamily,
            FontSize = 21,
            TextColor = HeadingColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineHeight = 0.9
        };
        var rounds = new Label
        {
            Text = $"{option.TotalRounds} Rondtes",
            FontFamily = PoppinsFontFamily,
            FontSize = 14,
            TextColor = BodyColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineHeight = 0.9
        };
#if ANDROID
        ConfigureAndroidDifficultyLabel(title);
        ConfigureAndroidDifficultyLabel(rounds);
#endif
        var text = new VerticalStackLayout
        {
            Spacing = -3,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Children = { title, rounds }
        };
        var grid = new Grid
        {
            Children = { text }
        };

        var card = new Border
        {
            HeightRequest = 58,
            Padding = new Thickness(10, 0, 8, 0),
            BackgroundColor = UnselectedCardColor,
            Stroke = UnselectedStrokeColor,
            StrokeThickness = 2,
            Opacity = 0.98,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = grid,
            AutomationId = $"karakter-raai-difficulty-{option.Difficulty.ToString().ToLowerInvariant()}"
        };
        SemanticProperties.SetDescription(card, $"Speel {option.DisplayName.ToLowerInvariant()}, {option.TotalRounds} rondtes");

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
        tap.Tapped += async (_, _) => await StartDifficultyAsync(option);
        card.GestureRecognizers.Add(tap);
        return (container, card);
    }

#if ANDROID
    private static void ConfigureAndroidDifficultyLabel(Label label)
    {
        label.HandlerChanged += (_, _) =>
        {
            if (label.Handler?.PlatformView is Android.Widget.TextView nativeLabel)
            {
                nativeLabel.SetIncludeFontPadding(false);
            }
        };
    }
#endif

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
            AutomationId = "karakter-raai-back"
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await NavigateBackAsync();
        backButton.GestureRecognizers.Add(tap);
        return backButton;
    }

    private Border BuildCloseButton()
    {
        var closeButton = new Border
        {
            WidthRequest = 48,
            HeightRequest = 48,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = CloseBackgroundColor,
            Stroke = CloseStrokeColor,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Content = new Image
            {
                Source = new FontImageSource
                {
                    Glyph = "\uf00d",
                    FontFamily = "FontAwesomeSolid",
                    Color = CloseColor,
                    Size = 26
                },
                WidthRequest = 26,
                HeightRequest = 26,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            },
            AutomationId = "karakter-raai-close"
        };
        SemanticProperties.SetDescription(closeButton, "Terug");

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await NavigateBackAsync();
        closeButton.GestureRecognizers.Add(tap);
        return closeButton;
    }

    private async Task NavigateBackAsync()
    {
        if (_isNavigatingBack)
        {
            return;
        }

        _isNavigatingBack = true;
        try
        {
            await Shell.Current.GoToAsync("..", animate: false);
        }
        finally
        {
            _isNavigatingBack = false;
        }
    }

    private void ClearDifficultySelection()
    {
        foreach (var card in _optionCards)
        {
            card.BackgroundColor = UnselectedCardColor;
            card.Stroke = UnselectedStrokeColor;
            card.Opacity = 0.98;
        }
    }

    private void SelectDifficulty(CharacterGuessDifficultyOption option)
    {
        for (var index = 0; index < _optionCards.Count; index++)
        {
            var isSelected = _options[index].Difficulty == option.Difficulty;
            var card = _optionCards[index];
            card.BackgroundColor = isSelected ? SelectedCardColor : UnselectedCardColor;
            card.Stroke = isSelected ? SelectedStrokeColor : UnselectedStrokeColor;
            card.Opacity = isSelected ? 1 : 0.98;
        }
    }

    private async Task StartDifficultyAsync(CharacterGuessDifficultyOption option)
    {
        if (_isStartingGame)
        {
            return;
        }

        _isStartingGame = true;
        SelectDifficulty(option);
        try
        {
            var parameters = new ShellNavigationQueryParameters
            {
                ["rounds"] = option.TotalRounds
            };
            await Shell.Current.GoToAsync(nameof(KarakterRaaiGamePage), parameters);
        }
        finally
        {
            _isStartingGame = false;
        }
    }

    private void ApplyResponsiveLayout(
        VerticalStackLayout content,
        Image heroLogo,
        VerticalStackLayout difficultyOptions,
        Border closeButton)
    {
        var useCompactLayout = Height > 0 && Height < CompactLayoutHeight;
        content.Padding = useCompactLayout
            ? new Thickness(0, 48, 0, 6)
            : new Thickness(0, 80, 0, 8);
        heroLogo.WidthRequest = useCompactLayout ? 254 : 270;
        heroLogo.HeightRequest = useCompactLayout ? 215 : 237;
        heroLogo.Margin = useCompactLayout
            ? new Thickness(0, 0, 0, 12)
            : new Thickness(0, 0, 0, 18);
        difficultyOptions.Margin = useCompactLayout
            ? new Thickness(0, 28, 0, 0)
            : new Thickness(0, 47, 0, 0);
        closeButton.Margin = useCompactLayout
            ? new Thickness(0, 10, 0, 0)
            : new Thickness(0, 12, 0, 0);
        MobileResponsiveLayout.ApplyCenteredContent(content, DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density, 640);
    }

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
