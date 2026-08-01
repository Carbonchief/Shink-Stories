using System.ComponentModel;
using Shink.Mobile.Games;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class KarakterPareGamePage : ContentPage
{
    private static readonly MatchDifficultyOption[] DifficultyOptions =
    [
        new(MatchDifficultyLevel.Easy, "Maklik", 3, 4),
        new(MatchDifficultyLevel.Medium, "Gemiddeld", 4, 4),
        new(MatchDifficultyLevel.Hard, "Moeilik", 4, 5)
    ];
    private static readonly string[] PerfectScoreMessages =
    [
        "Elke paar op die eerste probeerslag—jou geheue is superskerp!",
        "Geen kaartjie kon jou flous nie. Dis ’n perfekte spel!",
        "Jy het soos ’n ware Karakter-kampioen gespeel!"
    ];
    private readonly MobileApiClient _apiClient;
    private readonly MobileAnalyticsService _analytics;
    private readonly List<CharacterMatchTile> _tiles = [];
    private readonly Dictionary<Guid, VisualElement> _tileViews = [];
    private readonly Dictionary<MatchDifficultyLevel, DifficultyChoiceView> _difficultyChoices = [];
    private readonly Grid _board;
    private readonly Grid _setupView;
    private readonly Grid _stateOverlay;
    private readonly Border _scoreCard;
    private readonly ActivityIndicator _loadingIndicator;
    private readonly Label _stateLabel;
    private readonly Button _retryButton;
    private readonly Label _attemptsLabel;
    private readonly Label _pairsLabel;
    private readonly Label _messageLabel;
    private readonly Button _startGameButton;
    private readonly Button _newGameButton;
    private readonly GameCelebrationOverlay _celebrationOverlay;
    private IReadOnlyList<MobileCharacterCard> _availableCharacters = Array.Empty<MobileCharacterCard>();
    private CharacterMatchGame? _game;
    private CancellationTokenSource? _loadCancellation;
    private MatchDifficultyOption _selectedDifficulty = DifficultyOptions[0];
    private bool _hasLoaded;
    private bool _isPageActive;

    public KarakterPareGamePage(
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics)
    {
        _apiClient = apiClient;
        _analytics = analytics;
        Title = "Karakter-pare";
        BackgroundColor = Color.FromArgb("#46969E");
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);

        _attemptsLabel = BuildScoreLabel("Draaie: 0");
        _pairsLabel = BuildScoreLabel("Pare: 0 / 0");
        _messageLabel = new Label
        {
            Text = "Kies jou eerste kaartjie.",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#166476"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        _board = BuildBoard();

        _loadingIndicator = new ActivityIndicator
        {
            Color = Color.FromArgb("#F8C854"),
            IsRunning = true,
            WidthRequest = 42,
            HeightRequest = 42
        };
        _stateLabel = new Label
        {
            Text = "Ons meng die karakterkaartjies …",
            FontSize = 17,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(24, 0)
        };
        _retryButton = BuildPrimaryButton("Probeer weer");
        _retryButton.IsVisible = false;
        _retryButton.Clicked += async (_, _) => await LoadCharactersAsync(forceRefresh: true);
        _stateOverlay = new Grid
        {
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 14,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        _loadingIndicator,
                        _stateLabel,
                        _retryButton
                    }
                }
            }
        };

        _startGameButton = BuildPrimaryButton("Begin speel");
        _startGameButton.AutomationId = "character-match-start";
        _startGameButton.Clicked += async (_, _) => await HandleStartGameAsync();
        _setupView = BuildSetupView();
        UpdateDifficultyChoiceStyles();

        _newGameButton = BuildPrimaryButton("Meng en speel weer");
        _newGameButton.ImageSource = "replay_icon.svg";
        _newGameButton.ContentLayout = new Button.ButtonContentLayout(
            Button.ButtonContentLayout.ImagePosition.Left,
            9);
        _newGameButton.Margin = new Thickness(20, 4, 20, 8);
        _newGameButton.IsVisible = false;
        _newGameButton.Clicked += async (_, _) =>
        {
            SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
            await StartNewGameAsync(animateBoard: true);
        };
        _celebrationOverlay = new GameCelebrationOverlay
        {
            ZIndex = 500
        };

        var root = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb("#166476"), 0),
                    new(Color.FromArgb("#46969E"), 0.48f),
                    new(Color.FromArgb("#68B6B5"), 1)
                },
                new Point(0, 0),
                new Point(0, 1)),
            RowDefinitions =
            {
                new RowDefinition(new GridLength(64)),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        var topBar = new Grid
        {
            Padding = new Thickness(10, 8, 10, 0),
            Children =
            {
                MobileTopBar.Build(this, _apiClient, sessionState.Current, leftAction: "back")
            }
        };
        var heading = BuildHeading();
        _scoreCard = BuildScoreCard();
        _scoreCard.IsVisible = false;
        var boardHost = new Grid
        {
            Children =
            {
                _board,
                _stateOverlay
            }
        };

        root.Children.Add(topBar);
        root.Children.Add(heading);
        root.Children.Add(_scoreCard);
        root.Children.Add(boardHost);
        root.Children.Add(_newGameButton);
        root.Children.Add(_setupView);
        root.Children.Add(_celebrationOverlay);
        Grid.SetRow(heading, 1);
        Grid.SetRow(_scoreCard, 2);
        Grid.SetRow(boardHost, 3);
        Grid.SetRow(_newGameButton, 4);
        Grid.SetRow(_setupView, 2);
        Grid.SetRowSpan(_setupView, 3);
        Grid.SetRowSpan(_celebrationOverlay, 5);
        Content = root;
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        MobileResponsiveLayout.ApplyCenteredContent(_board, Width, 760);
        MobileResponsiveLayout.ApplyCenteredContent(_newGameButton, Width, 440);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _loadCancellation?.Cancel();
        _celebrationOverlay.Hide();
        base.OnDisappearing();
    }

    private static VerticalStackLayout BuildHeading() =>
        new()
        {
            Padding = new Thickness(18, 0, 18, 6),
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = "KARAKTER-PARE",
                    FontSize = 26,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    CharacterSpacing = 1.2
                },
                new Label
                {
                    Text = "Draai twee kaartjies om. Kry twee van dieselfde karakter en laat die paar verdwyn!",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#F4FFFE"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };

    private Grid BuildSetupView()
    {
        var choices = new VerticalStackLayout
        {
            Spacing = 9
        };
        foreach (var difficulty in DifficultyOptions)
        {
            choices.Children.Add(BuildDifficultyChoice(difficulty));
        }

        var setupCard = new Border
        {
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            Stroke = Color.FromArgb("#F3DEB4"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            Padding = new Thickness(18, 15, 18, 18),
            Margin = new Thickness(14, 6, 14, 12),
            MaximumWidthRequest = 440,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 7),
                Radius = 18,
                Opacity = 0.18f
            },
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Image
                    {
                        Source = "oortjies_01.png",
                        HeightRequest = 64,
                        Aspect = Aspect.AspectFit,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = "KIES JOU UITDAGING",
                        FontSize = 23,
                        FontAttributes = FontAttributes.Bold,
                        CharacterSpacing = 0.8,
                        TextColor = Color.FromArgb("#166476"),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = "Hoe groot moet jou kaartrooster wees?",
                        FontSize = 14,
                        TextColor = Color.FromArgb("#4B5960"),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    choices,
                    _startGameButton
                }
            }
        };

        return new Grid
        {
            ZIndex = 100,
            BackgroundColor = Color.FromArgb("#2A166476"),
            AutomationId = "character-match-setup",
            Children =
            {
                setupCard
            }
        };
    }

    private Border BuildDifficultyChoice(MatchDifficultyOption difficulty)
    {
        var title = new Label
        {
            Text = difficulty.DisplayName.ToUpperInvariant(),
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 0.5,
            TextColor = Color.FromArgb("#27313A"),
            VerticalTextAlignment = TextAlignment.Center
        };
        var details = new Label
        {
            Text = $"{difficulty.Columns} × {difficulty.Rows} rooster · {difficulty.PairCount} pare",
            FontSize = 13,
            TextColor = Color.FromArgb("#52616A")
        };
        var checkmark = new Label
        {
            Text = "✓",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#166476"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            WidthRequest = 30
        };
        var text = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                title,
                details
            }
        };
        var content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                text,
                checkmark
            }
        };
        Grid.SetColumn(checkmark, 1);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            Stroke = Color.FromArgb("#DFCFAE"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            Padding = new Thickness(14, 9),
            Content = content,
            AutomationId = $"character-match-difficulty-{difficulty.Level.ToString().ToLowerInvariant()}"
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectDifficulty(difficulty);
        card.GestureRecognizers.Add(tap);
        _difficultyChoices[difficulty.Level] = new DifficultyChoiceView(card, title, details, checkmark);
        return card;
    }

    private void SelectDifficulty(MatchDifficultyOption difficulty)
    {
        SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
        _selectedDifficulty = difficulty;
        UpdateDifficultyChoiceStyles();
    }

    private void UpdateDifficultyChoiceStyles()
    {
        foreach (var difficulty in DifficultyOptions)
        {
            var choice = _difficultyChoices[difficulty.Level];
            var isSelected = difficulty.Level == _selectedDifficulty.Level;
            choice.Card.BackgroundColor = Color.FromArgb(isSelected ? "#FFF0BE" : "#FFFDF7");
            choice.Card.Stroke = Color.FromArgb(isSelected ? "#E8AE27" : "#DFCFAE");
            choice.Card.StrokeThickness = isSelected ? 2 : 1;
            choice.Title.TextColor = Color.FromArgb(isSelected ? "#166476" : "#27313A");
            choice.Details.TextColor = Color.FromArgb(isSelected ? "#35585D" : "#52616A");
            choice.Checkmark.IsVisible = isSelected;
        }
    }

    private async Task HandleStartGameAsync()
    {
        if (!_isPageActive || !_startGameButton.IsEnabled)
        {
            return;
        }

        SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
        _startGameButton.IsEnabled = false;
        _setupView.IsVisible = false;
        try
        {
            if (_hasLoaded)
            {
                await StartNewGameAsync(animateBoard: true);
            }
            else
            {
                await LoadCharactersAsync();
            }
        }
        finally
        {
            _startGameButton.IsEnabled = true;
        }
    }

    private Border BuildScoreCard()
    {
        var scoreGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            RowSpacing = 2
        };
        scoreGrid.Children.Add(_attemptsLabel);
        scoreGrid.Children.Add(_pairsLabel);
        scoreGrid.Children.Add(_messageLabel);
        Grid.SetColumn(_pairsLabel, 1);
        Grid.SetRow(_messageLabel, 1);
        Grid.SetColumnSpan(_messageLabel, 2);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            Stroke = Color.FromArgb("#F3DEB4"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 17 },
            Padding = new Thickness(12, 6),
            Margin = new Thickness(12, 0, 12, 6),
            Content = scoreGrid
        };
    }

    private static Label BuildScoreLabel(string text) =>
        new()
        {
            Text = text,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#27313A"),
            HorizontalTextAlignment = TextAlignment.Center
        };

    private static Button BuildPrimaryButton(string text) =>
        new()
        {
            Text = text,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#27313A"),
            BackgroundColor = Color.FromArgb("#F8C854"),
            CornerRadius = 16,
            HeightRequest = 46,
            Padding = new Thickness(22, 0),
            HorizontalOptions = LayoutOptions.Center
        };

    private static Grid BuildBoard() =>
        new()
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Background = Brush.Transparent,
            ColumnSpacing = 8,
            RowSpacing = 8,
            Margin = new Thickness(12, 0),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false,
            AutomationId = "character-match-board"
        };

    private View BuildTileView(CharacterMatchTile tile)
    {
        var characterImage = new Image
        {
            Aspect = Aspect.AspectFit,
            Margin = new Thickness(2, 1),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        characterImage.SetBinding(
            Image.SourceProperty,
            static (CharacterMatchTile tile) => tile.ImageSource);

        var characterName = new Label
        {
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#27313A"),
            HorizontalTextAlignment = TextAlignment.Center,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        characterName.SetBinding(
            Label.TextProperty,
            static (CharacterMatchTile tile) => tile.DisplayName);

        var frontContent = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 1,
            Children =
            {
                characterImage,
                characterName
            }
        };
        Grid.SetRow(characterName, 1);

        var front = new Border
        {
            BackgroundColor = Color.FromArgb("#FFF9F0"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            Padding = new Thickness(5, 3, 5, 2),
            Content = frontContent
        };
        front.SetBinding(
            IsVisibleProperty,
            static (CharacterMatchTile tile) => tile.IsFaceUp);

        var back = new Grid
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb("#27313A"), 0),
                    new(Color.FromArgb("#166476"), 1)
                },
                new Point(0, 0),
                new Point(1, 1)),
            Children =
            {
                new Image
                {
                    Source = "oortjies_01.png",
                    HeightRequest = 42,
                    Opacity = 0.94,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "?",
                    FontSize = 23,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#F8C854"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.End,
                    Margin = new Thickness(0, 0, 0, 4)
                }
            }
        };
        back.SetBinding(
            IsVisibleProperty,
            static (CharacterMatchTile tile) => tile.IsFaceDown);

        var faces = new Grid
        {
            Children =
            {
                back,
                front
            }
        };
        var card = new Border
        {
            MinimumHeightRequest = 64,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Color.FromArgb("#27313A"),
            Stroke = Color.FromArgb("#55FFFFFF"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = 0,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 3),
                Radius = 7,
                Opacity = 0.18f
            },
            Content = faces,
            AutomationId = "character-match-tile"
        };
        card.SetBinding(
            OpacityProperty,
            static (CharacterMatchTile tile) => tile.TileOpacity);
        card.SetBinding(
            InputTransparentProperty,
            static (CharacterMatchTile tile) => tile.IsMatched);
        card.BindingContext = tile;
        _tileViews[tile.Id] = card;

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (card.BindingContext is CharacterMatchTile tile)
            {
                await HandleTileTappedAsync(tile, card);
            }
        };
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private async Task LoadCharactersAsync(bool forceRefresh = false)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;

        ShowLoadingState();
        try
        {
            var response = forceRefresh
                ? null
                : await _apiClient.GetCachedCharactersAsync(cancellationToken);
            response ??= await _apiClient.GetCharactersAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return;
            }

            if (response is null)
            {
                ShowErrorState("Kon nie die karakterkaartjies laai nie. Probeer asseblief weer.");
                return;
            }

            _availableCharacters = response.Characters
                .Where(character => !string.IsNullOrWhiteSpace(character.Slug))
                .Where(character => !string.IsNullOrWhiteSpace(character.ImageUrl))
                .DistinctBy(character => character.Slug, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (_availableCharacters.Count < _selectedDifficulty.PairCount)
            {
                ShowErrorState(
                    $"Ons benodig minstens {_selectedDifficulty.PairCount} verskillende Karakters vir {_selectedDifficulty.DisplayName.ToLowerInvariant()}.");
                return;
            }

            _hasLoaded = true;
            await StartNewGameAsync(animateBoard: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (_isPageActive)
            {
                ShowErrorState("Kon nie die karakterkaartjies laai nie. Probeer asseblief weer.");
            }
        }
    }

    private async Task StartNewGameAsync(bool animateBoard)
    {
        var difficulty = _selectedDifficulty;
        if (_availableCharacters.Count < difficulty.PairCount)
        {
            return;
        }

        _celebrationOverlay.Hide();
        _game = null;
        _newGameButton.IsEnabled = false;
        _board.InputTransparent = true;
        if (animateBoard)
        {
            await AnimateBoardOutAsync();
        }

        var pairCount = difficulty.PairCount;
        var selectedCharacters = Shuffle(_availableCharacters).Take(pairCount).ToArray();
        var tiles = selectedCharacters
            .SelectMany(character => new[]
            {
                CreateTile(character),
                CreateTile(character)
            })
            .ToList();
        ShuffleInPlace(tiles);

        _tileViews.Clear();
        _tiles.Clear();
        foreach (var tile in tiles)
        {
            _tiles.Add(tile);
        }
        RenderBoard();

        _game = new CharacterMatchGame(
            tiles.Select(tile => new CharacterMatchCard(tile.Id, tile.PairKey)));
        _attemptsLabel.Text = "Draaie: 0";
        _pairsLabel.Text = $"Pare: 0 / {_game.PairCount}";
        _messageLabel.Text = "Kies jou eerste kaartjie.";
        _messageLabel.TextColor = Color.FromArgb("#166476");
        _newGameButton.Text = "Meng en speel weer";
        _newGameButton.IsVisible = true;
        _stateOverlay.IsVisible = false;
        _scoreCard.IsVisible = true;
        _board.Opacity = 1;
        _board.Scale = 1;
        _board.IsVisible = true;

        if (animateBoard)
        {
            await AnimateBoardBuildAsync();
        }

        _board.InputTransparent = false;
        _newGameButton.IsEnabled = true;

        _analytics.TrackEvent("mobile_character_match_started", new Dictionary<string, object>
        {
            ["pair_count"] = _game.PairCount,
            ["difficulty"] = difficulty.AnalyticsName,
            ["columns"] = difficulty.Columns,
            ["rows"] = difficulty.Rows
        });

        _ = _apiClient.CacheImagesAsync(
            selectedCharacters.Select(character => character.ImageUrl),
            _loadCancellation?.Token ?? default,
            maxImages: difficulty.PairCount,
            maxDegreeOfParallelism: 2);
    }

    private CharacterMatchTile CreateTile(MobileCharacterCard character) =>
        new(
            Guid.NewGuid(),
            character.Slug,
            character.DisplayName,
            _apiClient.BuildCachedImageSource(character.ImageUrl, "schink_character_lineup.png"));

    private void RenderBoard()
    {
        var difficulty = _selectedDifficulty;
        ConfigureBoardLayout(difficulty);
        _board.Children.Clear();
        for (var index = 0; index < _tiles.Count; index++)
        {
            var tileView = BuildTileView(_tiles[index]);
            _board.Children.Add(tileView);
            Grid.SetColumn(tileView, index % difficulty.Columns);
            Grid.SetRow(tileView, index / difficulty.Columns);
        }
    }

    private void ConfigureBoardLayout(MatchDifficultyOption difficulty)
    {
        _board.ColumnDefinitions.Clear();
        for (var column = 0; column < difficulty.Columns; column++)
        {
            _board.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        _board.RowDefinitions.Clear();
        for (var row = 0; row < difficulty.Rows; row++)
        {
            _board.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }

        var spacing = difficulty.Columns == 3 ? 8 : 6;
        _board.ColumnSpacing = spacing;
        _board.RowSpacing = spacing;
        _board.Margin = difficulty.Columns == 3
            ? new Thickness(12, 0)
            : new Thickness(9, 0);
    }

    private async Task AnimateBoardOutAsync()
    {
        if (ShouldReduceMotion() || _board.Handler is null || !_board.IsVisible)
        {
            return;
        }

        var visibleTiles = _tiles
            .Select(tile => FindTileView(tile.Id))
            .OfType<VisualElement>()
            .Where(static tileView => tileView.Handler is not null && tileView.Opacity > 0.05)
            .ToArray();
        var tileAnimations = visibleTiles
            .Select((tileView, index) => AnimateTileOutAsync(tileView, index))
            .ToArray();

        await Task.WhenAll(
            _board.FadeToAsync(0, 180, Easing.CubicIn),
            _board.ScaleToAsync(0.94, 180, Easing.CubicIn),
            Task.WhenAll(tileAnimations));
    }

    private async Task AnimateBoardBuildAsync()
    {
        var tileViews = _tiles
            .Select(tile => FindTileView(tile.Id))
            .OfType<VisualElement>()
            .ToArray();
        if (ShouldReduceMotion())
        {
            ResetTileTransforms(tileViews);
            return;
        }

        foreach (var tileView in tileViews)
        {
            tileView.Opacity = 0;
            tileView.Scale = 0.72;
            tileView.TranslationY = 22;
        }

        await Task.Yield();
        await Task.WhenAll(tileViews.Select(AnimateTileIntoBoardAsync));
    }

    private static async Task AnimateTileOutAsync(VisualElement tileView, int index)
    {
        await Task.Delay(index * 14);
        if (tileView.Handler is null)
        {
            return;
        }

        await Task.WhenAll(
            tileView.FadeToAsync(0, 120, Easing.CubicIn),
            tileView.ScaleToAsync(0.76, 150, Easing.CubicIn),
            tileView.TranslateToAsync(0, -12, 150, Easing.CubicIn));
    }

    private async Task AnimateTileIntoBoardAsync(VisualElement tileView, int index)
    {
        await Task.Delay(index * 38);
        if (!_isPageActive || tileView.Handler is null)
        {
            ResetTileTransform(tileView);
            return;
        }

        await Task.WhenAll(
            tileView.FadeToAsync(1, 180, Easing.CubicOut),
            tileView.ScaleToAsync(1, 320, Easing.SpringOut),
            tileView.TranslateToAsync(0, 0, 240, Easing.CubicOut));
    }

    private async Task AnimateCompletedBoardRevealAsync()
    {
        var completedTiles = _tiles
            .Select(tile => (Tile: tile, View: FindTileView(tile.Id)))
            .Where(static entry => entry.View is not null)
            .Select(static entry => (Tile: entry.Tile, View: entry.View!))
            .ToArray();

        foreach (var (tile, tileView) in completedTiles)
        {
            tileView.CancelAnimations();
            tile.PrepareForCompletionReveal();
            tileView.Opacity = 0;
            tileView.Scale = 0.78;
            tileView.ScaleX = 1;
            tileView.TranslationY = 16;
        }

        if (ShouldReduceMotion())
        {
            foreach (var (tile, tileView) in completedTiles)
            {
                tile.SetFaceUp(faceUp: true);
                ResetTileTransform(tileView);
            }

            return;
        }

        await Task.Yield();
        await Task.WhenAll(completedTiles.Select(
            (entry, index) => AnimateCompletedTileRevealAsync(entry.Tile, entry.View, index)));
    }

    private async Task AnimateCompletedTileRevealAsync(
        CharacterMatchTile tile,
        VisualElement tileView,
        int index)
    {
        await Task.Delay(index * 38);
        if (!_isPageActive || tileView.Handler is null)
        {
            tile.SetFaceUp(faceUp: true);
            ResetTileTransform(tileView);
            return;
        }

        await Task.WhenAll(
            tileView.FadeToAsync(1, 150, Easing.CubicOut),
            tileView.ScaleToAsync(1, 240, Easing.SpringOut),
            tileView.TranslateToAsync(0, 0, 200, Easing.CubicOut));
        await AnimateFlipAsync(tile, tileView, faceUp: true);
    }

    private static void ResetTileTransforms(IEnumerable<VisualElement> tileViews)
    {
        foreach (var tileView in tileViews)
        {
            ResetTileTransform(tileView);
        }
    }

    private static void ResetTileTransform(VisualElement tileView)
    {
        tileView.Opacity = 1;
        tileView.Scale = 1;
        tileView.TranslationX = 0;
        tileView.TranslationY = 0;
    }

    private async Task HandleTileTappedAsync(CharacterMatchTile tile, VisualElement tileView)
    {
        var game = _game;
        if (game is null)
        {
            return;
        }

        var turn = game.Reveal(tile.Id);
        if (turn.Outcome == CharacterMatchOutcome.Ignored)
        {
            return;
        }

        SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
        _newGameButton.IsEnabled = false;
        await AnimateFlipAsync(tile, tileView, faceUp: true);
        if (!ReferenceEquals(_game, game))
        {
            return;
        }

        if (turn.Outcome == CharacterMatchOutcome.FirstCard)
        {
            if (!game.IsResolving)
            {
                _messageLabel.Text = "Nou vir die maat!";
            }

            _newGameButton.IsEnabled = true;
            return;
        }

        _attemptsLabel.Text = $"Draaie: {game.AttemptCount}";
        try
        {
            var firstTile = turn.FirstCardId is { } firstCardId
                ? FindTile(firstCardId)
                : null;
            var firstTileView = firstTile is null
                ? null
                : FindTileView(firstTile.Id);

            if (turn.Outcome == CharacterMatchOutcome.Match)
            {
                var isPerfectScore = turn.IsComplete && game.IsPerfectScore;
                _pairsLabel.Text = $"Pare: {game.MatchedPairCount} / {game.PairCount}";
                _messageLabel.Text = turn.IsComplete
                    ? isPerfectScore
                        ? "VOLPUNTE! Elke paar was reg!"
                        : "Jy het al die pare gekry!"
                    : $"Mooi so! {tile.DisplayName} is ’n paar.";
                _messageLabel.TextColor = Color.FromArgb("#18794E");
                SafeHapticFeedback.TryPerform(HapticFeedbackType.LongPress);
                await Task.Delay(260);

                if (firstTile is not null)
                {
                    await AnimateMatchedPairAsync(firstTile, firstTileView, tile, tileView);
                }

                if (turn.IsComplete)
                {
                    _newGameButton.Text = "Speel weer";
                    _analytics.TrackEvent("mobile_character_match_completed", new Dictionary<string, object>
                    {
                        ["attempt_count"] = game.AttemptCount,
                        ["pair_count"] = game.PairCount,
                        ["difficulty"] = _selectedDifficulty.AnalyticsName,
                        ["is_perfect_score"] = isPerfectScore
                    });
                    await _celebrationOverlay.CelebrateAsync(
                        isPerfectScore ? "PERFEKTE PARE!" : "BAIE GELUK!",
                        BuildCompletionMessage(game));
                    if (_isPageActive && ReferenceEquals(_game, game))
                    {
                        await AnimateCompletedBoardRevealAsync();
                    }
                }
            }
            else
            {
                _messageLabel.Text = "Byna! Dié twee pas nie. Probeer weer.";
                _messageLabel.TextColor = Color.FromArgb("#B14D32");
                await Task.Delay(780);
                if (firstTile is not null)
                {
                    await Task.WhenAll(
                        AnimateFlipAsync(firstTile, firstTileView, faceUp: false),
                        AnimateFlipAsync(tile, tileView, faceUp: false));
                }
            }
        }
        finally
        {
            game.CompleteTurn();
            _newGameButton.IsEnabled = true;
        }
    }

    private static async Task AnimateFlipAsync(
        CharacterMatchTile tile,
        VisualElement? tileView,
        bool faceUp)
    {
        if (tileView is null || tileView.Handler is null)
        {
            tile.SetFaceUp(faceUp);
            return;
        }

        await tileView.ScaleXToAsync(0.06, 85, Easing.CubicIn);
        tile.SetFaceUp(faceUp);
        await tileView.ScaleXToAsync(1, 105, Easing.CubicOut);
    }

    private static async Task AnimateMatchedPairAsync(
        CharacterMatchTile firstTile,
        VisualElement? firstTileView,
        CharacterMatchTile secondTile,
        VisualElement secondTileView)
    {
        var animations = new List<Task>();
        if (firstTileView?.Handler is not null)
        {
            animations.Add(firstTileView.FadeToAsync(0, 220, Easing.CubicIn));
            animations.Add(firstTileView.ScaleToAsync(0.82, 220, Easing.CubicIn));
        }

        if (secondTileView.Handler is not null)
        {
            animations.Add(secondTileView.FadeToAsync(0, 220, Easing.CubicIn));
            animations.Add(secondTileView.ScaleToAsync(0.82, 220, Easing.CubicIn));
        }

        if (animations.Count > 0)
        {
            await Task.WhenAll(animations);
        }

        firstTile.MarkMatched();
        secondTile.MarkMatched();
    }

    private CharacterMatchTile? FindTile(Guid tileId) =>
        _tiles.FirstOrDefault(tile => tile.Id == tileId);

    private VisualElement? FindTileView(Guid tileId) =>
        _tileViews.GetValueOrDefault(tileId);

    private void ShowLoadingState()
    {
        _setupView.IsVisible = false;
        _scoreCard.IsVisible = false;
        _board.IsVisible = false;
        _newGameButton.IsVisible = false;
        _stateOverlay.IsVisible = true;
        _loadingIndicator.IsRunning = true;
        _loadingIndicator.IsVisible = true;
        _retryButton.IsVisible = false;
        _stateLabel.Text = "Ons meng die karakterkaartjies …";
    }

    private void ShowErrorState(string message)
    {
        _setupView.IsVisible = false;
        _scoreCard.IsVisible = false;
        _board.IsVisible = false;
        _newGameButton.IsVisible = false;
        _stateOverlay.IsVisible = true;
        _loadingIndicator.IsRunning = false;
        _loadingIndicator.IsVisible = false;
        _retryButton.IsVisible = true;
        _stateLabel.Text = message;
    }

    private static IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T> items)
    {
        var shuffled = items.ToList();
        ShuffleInPlace(shuffled);
        return shuffled;
    }

    private static void ShuffleInPlace<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }

    private static string BuildCompletionMessage(CharacterMatchGame game)
    {
        if (game.IsPerfectScore)
        {
            var perfectMessage = PerfectScoreMessages[Random.Shared.Next(PerfectScoreMessages.Length)];
            return $"{game.PairCount} pare in net {game.AttemptCount} draaie! {perfectMessage}";
        }

        var encouragement = game.AttemptCount <= game.PairCount + 3
            ? "Byna perfek—jou geheue is vlymskerp!"
            : "Jy het elke Karakter se maat gevind. Jou geheue is sommer skerp!";
        return $"{game.PairCount} pare in {game.AttemptCount} draaie! {encouragement}";
    }

    private static bool ShouldReduceMotion()
    {
#if IOS || MACCATALYST
        return UIKit.UIAccessibility.IsReduceMotionEnabled;
#else
        return false;
#endif
    }

    private enum MatchDifficultyLevel
    {
        Easy,
        Medium,
        Hard
    }

    private sealed record MatchDifficultyOption(
        MatchDifficultyLevel Level,
        string DisplayName,
        int Columns,
        int Rows)
    {
        public int PairCount => Columns * Rows / 2;

        public string AnalyticsName => Level.ToString().ToLowerInvariant();
    }

    private sealed record DifficultyChoiceView(
        Border Card,
        Label Title,
        Label Details,
        Label Checkmark);

    internal sealed class CharacterMatchTile(
        Guid id,
        string pairKey,
        string displayName,
        ImageSource imageSource) : INotifyPropertyChanged
    {
        private bool _isFaceUp;
        private bool _isMatched;
        private bool _showAfterCompletion;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Id { get; } = id;

        public string PairKey { get; } = pairKey;

        public string DisplayName { get; } = displayName;

        public ImageSource ImageSource { get; } = imageSource;

        public bool IsFaceUp => _isFaceUp;

        public bool IsFaceDown => !_isFaceUp;

        public bool IsMatched => _isMatched;

        public double TileOpacity => _isMatched && !_showAfterCompletion ? 0 : 1;

        public void SetFaceUp(bool faceUp)
        {
            if (_isFaceUp == faceUp)
            {
                return;
            }

            _isFaceUp = faceUp;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFaceUp)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFaceDown)));
        }

        public void MarkMatched()
        {
            if (_isMatched)
            {
                return;
            }

            _isMatched = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMatched)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileOpacity)));
        }

        public void PrepareForCompletionReveal()
        {
            _showAfterCompletion = true;
            _isFaceUp = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFaceUp)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFaceDown)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileOpacity)));
        }
    }
}
