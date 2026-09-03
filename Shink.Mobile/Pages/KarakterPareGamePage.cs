using System.ComponentModel;
using Shink.Mobile.Games;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class KarakterPareGamePage : ContentPage, IQueryAttributable
{
    private const int MatchedTileAnimationZIndex = 1_000;
    private const int MergedTileAnimationZIndex = 1_001;
    private const double PhoneTileCornerRadius = 12;
    private const double PhoneTileSpacing = 4;
    private const double TabletFaceUpTileCornerRadius = 15;
    private const double TabletTileCornerRadius = 22;
    private const double TabletThreeColumnTileSpacing = 10;
    private const double TabletFourColumnTileSpacing = 12;
    private static bool IsAndroid => DeviceInfo.Current.Platform == DevicePlatform.Android;
    private static bool IsTablet => DeviceInfo.Current.Idiom == DeviceIdiom.Tablet;
    private static readonly MatchDifficultyOption[] DifficultyOptions =
    [
        new(MatchDifficultyLevel.Easy, "Beginner", 3, 4),
        new(MatchDifficultyLevel.Medium, "Kenner", 4, 4),
        new(MatchDifficultyLevel.Hard, "Meester", 4, 6)
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
    private readonly Grid _boardHost;
    private readonly AbsoluteLayout _matchAnimationOverlay;
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
    private readonly ImageButton _newGameButton;
    private readonly GameCelebrationOverlay _celebrationOverlay;
    private IReadOnlyList<MobileCharacterCard> _availableCharacters = Array.Empty<MobileCharacterCard>();
    private CharacterMatchGame? _game;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _matchImagePreloadCancellation;
    private MatchDifficultyOption _selectedDifficulty = DifficultyOptions[0];
    private bool _hasLoaded;
    private bool _isPageActive;
    private bool _startConfiguredGame;
    private bool _isReturningToConfiguration;

    public KarakterPareGamePage(
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics,
        StoryPlaybackSession storyPlaybackSession)
    {
        _apiClient = apiClient;
        _analytics = analytics;
        Title = "Karakter-pare";
        Background = BuildGameBackground();
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);

        _attemptsLabel = BuildScoreLabel("Beurte: 0");
        _pairsLabel = BuildScoreLabel("0/0");
        _pairsLabel.HorizontalTextAlignment = TextAlignment.End;
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
        _matchAnimationOverlay = new AbsoluteLayout
        {
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            ZIndex = MatchedTileAnimationZIndex
        };

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

        _newGameButton = new ImageButton
        {
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 22,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 9,
            Source = new FontImageSource
            {
                Glyph = "\uf2f1",
                FontFamily = "FontAwesomeSolid",
                Color = Color.FromArgb("#5B7188"),
                Size = 25
            },
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            AutomationId = "character-match-retry"
        };
        SemanticProperties.SetDescription(_newGameButton, "Speel weer");
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
            Background = Brush.Transparent,
            RowDefinitions =
            {
                new RowDefinition(new GridLength(64)),
                new RowDefinition(new GridLength(66)),
                new RowDefinition(GridLength.Star)
            }
        };

        var topBar = BuildGameTopBar();
        _scoreCard = BuildScoreCard();
        _scoreCard.IsVisible = false;
        _boardHost = new Grid
        {
            Children =
            {
                _board,
                _matchAnimationOverlay,
                _stateOverlay
            }
        };
        _boardHost.SizeChanged += (_, _) => ApplyBoardGeometry(_selectedDifficulty);

        root.Children.Add(topBar);
        root.Children.Add(_scoreCard);
        root.Children.Add(_boardHost);
        root.Children.Add(_setupView);
        root.Children.Add(_celebrationOverlay);
        Grid.SetRow(_scoreCard, 1);
        Grid.SetRow(_boardHost, 2);
        Grid.SetRow(_setupView, 1);
        Grid.SetRowSpan(_setupView, 2);
        Grid.SetRowSpan(_celebrationOverlay, 3);
        Content = PersistentPlaybackHost.Wrap(root, storyPlaybackSession, edgeToEdge: true);
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        ApplyResponsiveLayout();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("difficulty", out var rawDifficulty))
        {
            return;
        }

        var level = rawDifficulty?.ToString();
        var difficulty = DifficultyOptions.FirstOrDefault(option =>
            option.Level.ToString().Equals(level, StringComparison.OrdinalIgnoreCase));
        if (difficulty is null)
        {
            return;
        }

        _selectedDifficulty = difficulty;
        _setupView.IsVisible = false;
        _startConfiguredGame = true;
        UpdateDifficultyChoiceStyles();
        if (_isPageActive)
        {
            _startConfiguredGame = false;
            _ = HandleStartGameAsync();
        }
    }

    private void ApplyResponsiveLayout()
    {
        // Keep the score labels readable while the board uses the largest
        // square tiles that fit both the available width and height.
        var scoreFontSize = Math.Clamp(Width * 0.06, 30, 44);
        _attemptsLabel.FontSize = scoreFontSize;
        _pairsLabel.FontSize = scoreFontSize;
        _scoreCard.Margin = new Thickness(Width >= 600 ? 36 : 18, 0, Width >= 600 ? 36 : 18, 8);
        ApplyBoardGeometry(_selectedDifficulty);
    }

    private static LinearGradientBrush BuildGameBackground() =>
        new(
            new GradientStopCollection
            {
                new(Color.FromArgb("#C9EAE4"), 0),
                new(Color.FromArgb("#E6F0D2"), 0.52f),
                new(Color.FromArgb("#FFF0B8"), 1)
            },
            new Point(0, 0),
            new Point(0, 1));

    private Grid BuildGameTopBar()
    {
        var backButton = BuildBackButton();
        var topBar = new Grid
        {
            Padding = new Thickness(18, 4, 18, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                backButton,
                _newGameButton
            }
        };
        Grid.SetColumn(_newGameButton, 1);
        return topBar;
    }

    private Border BuildBackButton()
    {
        var backButton = new Border
        {
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            WidthRequest = 54,
            HeightRequest = 54,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Content = new GraphicsView
            {
                Drawable = new GameBackChevronDrawable(Color.FromArgb("#5B7188")),
                WidthRequest = 32,
                HeightRequest = 32,
                InputTransparent = true
            },
            AutomationId = "character-match-back"
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ReturnToConfigurationAsync();
        backButton.GestureRecognizers.Add(tap);
        return backButton;
    }

    private async Task ReturnToConfigurationAsync()
    {
        if (_isReturningToConfiguration)
        {
            return;
        }

        _isReturningToConfiguration = true;
        _newGameButton.IsEnabled = false;
        _loadCancellation?.Cancel();
        SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
        try
        {
            var parameters = new ShellNavigationQueryParameters
            {
                ["difficulty"] = _selectedDifficulty.Level.ToString().ToLowerInvariant()
            };
            await Shell.Current.GoToAsync("..", parameters);
        }
        finally
        {
            _isReturningToConfiguration = false;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        if (_startConfiguredGame)
        {
            _startConfiguredGame = false;
            _ = HandleStartGameAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _loadCancellation?.Cancel();
        _matchImagePreloadCancellation?.Cancel();
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
            ColumnSpacing = 10,
            RowSpacing = 0
        };
        scoreGrid.Children.Add(_attemptsLabel);
        scoreGrid.Children.Add(_pairsLabel);
        Grid.SetColumn(_pairsLabel, 1);

        return new Border
        {
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(36, 0, 36, 8),
            Content = scoreGrid
        };
    }

    private static Label BuildScoreLabel(string text) =>
        new()
        {
            Text = text,
            FontSize = 40,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#5B7188"),
            HorizontalTextAlignment = TextAlignment.Start
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
            ColumnSpacing = PhoneTileSpacing,
            RowSpacing = PhoneTileSpacing,
            Margin = new Thickness(12, 0),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
            AutomationId = "character-match-board"
        };

    private View BuildTileView(CharacterMatchTile tile)
    {
        var faceUpCornerRadius = IsTablet ? TabletFaceUpTileCornerRadius : PhoneTileCornerRadius;
        var tileCornerRadius = IsTablet ? TabletTileCornerRadius : PhoneTileCornerRadius;
        var characterImage = new ProgressiveCachedImage(_apiClient)
        {
            Aspect = Aspect.AspectFit,
            Margin = new Thickness(2, 1),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        characterImage.SetBinding(
            ProgressiveCachedImage.RequestProperty,
            static (CharacterMatchTile tile) => tile.ImageRequest);

        var front = new Border
        {
            BackgroundColor = Color.FromArgb("#FFF9F0"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = faceUpCornerRadius },
            Padding = new Thickness(5, 3, 5, 2),
            Content = characterImage
        };
        front.SetBinding(
            IsVisibleProperty,
            static (CharacterMatchTile tile) => tile.IsFaceUp);

        var back = new Grid
        {
            Children =
            {
                new Image
                {
                    Source = "karakter_pare_card_back.png",
                    Aspect = Aspect.AspectFill,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
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
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Color.FromArgb("#27313A"),
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = tileCornerRadius },
            Padding = 0,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 3),
                Radius = 9,
                Opacity = 0.14f
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
                .Where(IsUsableMatchCharacter)
                .DistinctBy(character => character.Slug, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (_availableCharacters.Count < _selectedDifficulty.PairCount)
            {
                ShowErrorState(
                    $"Ons benodig minstens {_selectedDifficulty.PairCount} Karakters met gewone prente vir {_selectedDifficulty.DisplayName.ToLowerInvariant()}.");
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
        _matchAnimationOverlay.Children.Clear();

        var pairCount = difficulty.PairCount;
        var cancellationToken = _loadCancellation is { IsCancellationRequested: false } activeLoad
            ? activeLoad.Token
            : CancellationToken.None;
        var selectedCharacters = Shuffle(_availableCharacters).Take(pairCount).ToArray();
        _matchImagePreloadCancellation?.Cancel();
        using var preloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _matchImagePreloadCancellation = preloadCancellation;
        try
        {
            await PreloadMatchDisplayImagesAsync(selectedCharacters, preloadCancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_matchImagePreloadCancellation, preloadCancellation))
            {
                _matchImagePreloadCancellation = null;
            }
        }

        if (cancellationToken.IsCancellationRequested || !_isPageActive)
        {
            return;
        }

        if (animateBoard)
        {
            await AnimateBoardOutAsync();
        }

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
        _attemptsLabel.Text = "Beurte: 0";
        _pairsLabel.Text = $"0/{_game.PairCount}";
        _messageLabel.Text = "Kies jou eerste kaartjie.";
        _messageLabel.TextColor = Color.FromArgb("#166476");
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
            selectedCharacters.Select(character => GetMatchImageUrl(character)!),
            cancellationToken,
            maxImages: difficulty.PairCount,
            maxDegreeOfParallelism: 2);
    }

    private async Task PreloadMatchDisplayImagesAsync(
        IReadOnlyList<MobileCharacterCard> selectedCharacters,
        CancellationToken cancellationToken)
    {
        using var preloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        preloadCancellation.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await _apiClient.CacheImagesAsync(
                selectedCharacters.Select(GetMatchDisplayImageUrl),
                preloadCancellation.Token,
                maxImages: selectedCharacters.Count,
                maxDegreeOfParallelism: 2);
        }
        catch (OperationCanceledException)
        {
            // A slow or unavailable connection must not prevent the game from starting.
        }
    }

    private CharacterMatchTile CreateTile(MobileCharacterCard character) =>
        new(
            Guid.NewGuid(),
            character.Slug,
            character.DisplayName,
            new ProgressiveImageRequest(
                GetMatchImageUrl(character),
                character.MatchPreviewImageUrl,
                PageHelpers.StoryPlaceholderFile));

    private static bool IsUsableMatchCharacter(MobileCharacterCard character) =>
        !string.IsNullOrWhiteSpace(GetMatchImageUrl(character)) &&
        !IsMysteryImageUrl(GetMatchImageUrl(character)!);

    private static string? GetMatchImageUrl(MobileCharacterCard character) =>
        string.IsNullOrWhiteSpace(character.MatchImageUrl)
            ? character.ImageUrl
            : character.MatchImageUrl;

    private static string? GetMatchDisplayImageUrl(MobileCharacterCard character) =>
        string.IsNullOrWhiteSpace(character.MatchPreviewImageUrl)
            ? GetMatchImageUrl(character)
            : character.MatchPreviewImageUrl;

    private static bool IsMysteryImageUrl(string imageUrl)
    {
        var normalizedUrl = imageUrl.Trim();
        var queryIndex = normalizedUrl.IndexOf('?');
        var fragmentIndex = normalizedUrl.IndexOf('#');
        var suffixIndex = queryIndex < 0
            ? fragmentIndex
            : fragmentIndex < 0
                ? queryIndex
                : Math.Min(queryIndex, fragmentIndex);
        var path = suffixIndex < 0 ? normalizedUrl : normalizedUrl[..suffixIndex];

        return path.Contains("mystery", StringComparison.OrdinalIgnoreCase);
    }

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

        var spacing = ResolveTileSpacing(difficulty);
        _board.ColumnSpacing = spacing;
        _board.RowSpacing = spacing;
        var horizontalMargin = Width >= 600 ? 36 : 18;
        _board.Margin = new Thickness(horizontalMargin, 0, horizontalMargin, 0);
        ApplyBoardGeometry(difficulty);
    }

    private static double ResolveTileSpacing(MatchDifficultyOption difficulty)
    {
        if (!IsTablet)
        {
            return PhoneTileSpacing;
        }

        return difficulty.Columns == 3
            ? TabletThreeColumnTileSpacing
            : TabletFourColumnTileSpacing;
    }

    private void ApplyBoardGeometry(MatchDifficultyOption difficulty)
    {
        var hostWidth = _boardHost.Width > 0 ? _boardHost.Width : Width;
        var hostHeight = _boardHost.Height;
        if (hostWidth <= 0 || hostHeight <= 0)
        {
            return;
        }

        var horizontalMargin = Width >= 600 ? 36d : 18d;
        var horizontalSpacing = Math.Max(0, difficulty.Columns - 1) * _board.ColumnSpacing;
        var verticalSpacing = Math.Max(0, difficulty.Rows - 1) * _board.RowSpacing;
        var widthLimitedTileSize = (hostWidth - horizontalMargin * 2 - horizontalSpacing) / difficulty.Columns;
        var heightLimitedTileSize = (hostHeight - verticalSpacing) / difficulty.Rows;
        var tileSize = Math.Floor(Math.Min(widthLimitedTileSize, heightLimitedTileSize));
        if (tileSize <= 0)
        {
            return;
        }

        _board.WidthRequest = tileSize * difficulty.Columns + horizontalSpacing;
        _board.HeightRequest = tileSize * difficulty.Rows + verticalSpacing;
        _board.HorizontalOptions = LayoutOptions.Center;
        _board.VerticalOptions = LayoutOptions.Center;
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
            // Android can leave concurrent sibling ScaleTo animations at their
            // initial value. Keep cards full-sized and in their final grid position
            // there, while iOS keeps the existing spring-and-slide motion.
            tileView.Scale = IsAndroid ? 1 : 0.72;
            tileView.TranslationY = IsAndroid ? 0 : 22;
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

        if (IsAndroid)
        {
            await tileView.FadeToAsync(1, 180, Easing.CubicOut);
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
            tileView.Scale = IsAndroid ? 1 : 0.78;
            tileView.ScaleX = 1;
            tileView.TranslationX = 0;
            tileView.TranslationY = IsAndroid ? 0 : 16;
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

        if (IsAndroid)
        {
            await tileView.FadeToAsync(1, 150, Easing.CubicOut);
            ResetTileTransform(tileView);
        }
        else
        {
            await Task.WhenAll(
                tileView.FadeToAsync(1, 150, Easing.CubicOut),
                tileView.ScaleToAsync(1, 240, Easing.SpringOut),
                tileView.TranslateToAsync(0, 0, 200, Easing.CubicOut));
        }

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

        _attemptsLabel.Text = $"Beurte: {game.AttemptCount}";
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
                _pairsLabel.Text = $"{game.MatchedPairCount}/{game.PairCount}";
                _messageLabel.Text = turn.IsComplete
                    ? isPerfectScore
                        ? "VOLPUNTE! Elke paar was reg!"
                        : "Jy het al die pare gekry!"
                    : $"Mooi so! {tile.DisplayName} is ’n paar.";
                _messageLabel.TextColor = Color.FromArgb("#18794E");
                SafeHapticFeedback.TryPerform(HapticFeedbackType.LongPress);
                if (!ShouldReduceMotion())
                {
                    await Task.Delay(320);
                }

                if (firstTile is not null)
                {
                    await AnimateMatchedPairAsync(firstTile, firstTileView, tile, tileView);
                }

                if (turn.IsComplete)
                {
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

    private async Task AnimateMatchedPairAsync(
        CharacterMatchTile firstTile,
        VisualElement? firstTileView,
        CharacterMatchTile secondTile,
        VisualElement secondTileView)
    {
        var matchedViews = new[] { firstTileView, secondTileView }
            .OfType<VisualElement>()
            .Where(static tileView => tileView.Handler is not null)
            .Distinct()
            .ToArray();

        if (matchedViews.Length < 2 || ShouldReduceMotion() || _board.Width <= 0 || _board.Height <= 0)
        {
            ResetTileTransforms(matchedViews);
            firstTile.MarkMatched();
            secondTile.MarkMatched();
            return;
        }

        var placements = new Dictionary<VisualElement, MatchedTilePlacement>();
        foreach (var tileView in matchedViews)
        {
            placements[tileView] = MoveMatchedTileToAnimationOverlay(tileView);
        }

        // This is a sibling layer above the board, rather than only a higher
        // child ZIndex inside the board grid. That keeps the cards above every
        // other card while their positions cross the grid cells.
        await Task.Yield();

        var cardWidth = matchedViews
            .Select(static tileView => tileView.Width)
            .Where(static width => width > 0)
            .DefaultIfEmpty(84)
            .Min();
        var cardHeight = matchedViews
            .Select(static tileView => tileView.Height)
            .Where(static height => height > 0)
            .DefaultIfEmpty(112)
            .Min();
        var overlayWidth = _matchAnimationOverlay.Width > 0 ? _matchAnimationOverlay.Width : _board.Width;
        var overlayHeight = _matchAnimationOverlay.Height > 0 ? _matchAnimationOverlay.Height : _board.Height;
        var pageCenterX = Width > 0 ? Width / 2 : _boardHost.X + overlayWidth / 2;
        var pageCenterY = Height > 0 ? Height / 2 : _boardHost.Y + overlayHeight / 2;
        var centerX = Math.Clamp(
            pageCenterX - _boardHost.X,
            cardWidth / 2,
            Math.Max(cardWidth / 2, overlayWidth - cardWidth / 2));
        var centerY = Math.Clamp(
            pageCenterY - _boardHost.Y,
            cardHeight / 2,
            Math.Max(cardHeight / 2, overlayHeight - cardHeight / 2));
        var pairSeparation = Math.Max(18, cardWidth * 0.64);
        var animations = new List<Task>();

        if (firstTileView?.Handler is not null)
        {
            animations.Add(AnimateMatchedTileToCenterAsync(
                firstTileView,
                centerX,
                centerY,
                -pairSeparation / 2,
                duration: 420));
        }

        if (secondTileView.Handler is not null)
        {
            animations.Add(AnimateMatchedTileToCenterAsync(
                secondTileView,
                centerX,
                centerY,
                pairSeparation / 2,
                duration: 420));
        }

        await Task.WhenAll(animations);

        // Close the last gap so the matching cards visually become one card.
        await Task.WhenAll(
            firstTileView?.Handler is not null
                ? AnimateMatchedTileToCenterAsync(
                    firstTileView,
                    centerX,
                    centerY,
                    0,
                    duration: 190)
                : Task.CompletedTask,
            secondTileView.Handler is not null
                ? AnimateMatchedTileToCenterAsync(
                    secondTileView,
                    centerX,
                    centerY,
                    0,
                    duration: 190)
                : Task.CompletedTask);

        var mergedTileView = secondTileView.Handler is not null
            ? secondTileView
            : firstTileView!;
        var coveredTileView = ReferenceEquals(mergedTileView, firstTileView)
            ? secondTileView
            : firstTileView;
        coveredTileView!.Opacity = 0;
        mergedTileView.ZIndex = MergedTileAnimationZIndex;

        await Task.Delay(45);
        await AnimateMergedTilePopAsync(mergedTileView);
        await WiggleMatchedTileAsync(mergedTileView, 1);
        await AnimateMatchedTileAwayAsync(mergedTileView);

        foreach (var (tileView, placement) in placements)
        {
            RestoreMatchedTileFromAnimationOverlay(tileView, placement);
        }

        firstTile.MarkMatched();
        secondTile.MarkMatched();
    }

    private MatchedTilePlacement MoveMatchedTileToAnimationOverlay(VisualElement tileView)
    {
        var placement = new MatchedTilePlacement(
            Grid.GetColumn(tileView),
            Grid.GetRow(tileView),
            tileView.WidthRequest,
            tileView.HeightRequest);
        var width = tileView.Width > 0 ? tileView.Width : 84;
        var height = tileView.Height > 0 ? tileView.Height : 112;
        var x = _board.X + tileView.X + tileView.TranslationX;
        var y = _board.Y + tileView.Y + tileView.TranslationY;

        _board.Children.Remove(tileView);
        tileView.WidthRequest = width;
        tileView.HeightRequest = height;
        if (tileView is View view)
        {
            view.HorizontalOptions = LayoutOptions.Start;
            view.VerticalOptions = LayoutOptions.Start;
        }
        tileView.Opacity = 1;
        tileView.Scale = 1;
        tileView.ScaleX = 1;
        tileView.ScaleY = 1;
        tileView.Rotation = 0;
        tileView.TranslationX = 0;
        tileView.TranslationY = 0;
        tileView.ZIndex = MatchedTileAnimationZIndex;
        AbsoluteLayout.SetLayoutBounds(tileView, new Rect(x, y, width, height));
        _matchAnimationOverlay.Children.Add(tileView);
        return placement;
    }

    private void RestoreMatchedTileFromAnimationOverlay(
        VisualElement tileView,
        MatchedTilePlacement placement)
    {
        _matchAnimationOverlay.Children.Remove(tileView);
        tileView.WidthRequest = placement.WidthRequest;
        tileView.HeightRequest = placement.HeightRequest;
        if (tileView is View view)
        {
            view.HorizontalOptions = LayoutOptions.Fill;
            view.VerticalOptions = LayoutOptions.Fill;
        }
        tileView.Opacity = 0;
        tileView.Scale = 1;
        tileView.ScaleX = 1;
        tileView.ScaleY = 1;
        tileView.Rotation = 0;
        tileView.TranslationX = 0;
        tileView.TranslationY = 0;
        tileView.ZIndex = 0;
        _board.Children.Add(tileView);
        Grid.SetColumn(tileView, placement.Column);
        Grid.SetRow(tileView, placement.Row);
    }

    private async Task AnimateMatchedTileToCenterAsync(
        VisualElement tileView,
        double centerX,
        double centerY,
        double centerOffsetX,
        uint duration)
    {
        var layoutBounds = AbsoluteLayout.GetLayoutBounds(tileView);
        var tileX = layoutBounds.Width > 0 ? layoutBounds.X : tileView.X;
        var tileY = layoutBounds.Height > 0 ? layoutBounds.Y : tileView.Y;
        var tileWidth = layoutBounds.Width > 0 ? layoutBounds.Width : tileView.Width;
        var tileHeight = layoutBounds.Height > 0 ? layoutBounds.Height : tileView.Height;
        // TranslateToAsync expects an absolute translation. Always calculate its
        // target from the tile's fixed overlay bounds so the second merge phase
        // continues toward center instead of jumping relative to phase one.
        var tileCenterX = tileX + tileWidth / 2;
        var tileCenterY = tileY + tileHeight / 2;
        var targetX = centerX + centerOffsetX - tileCenterX;
        var targetY = centerY - tileCenterY;

        await Task.WhenAll(
            tileView.TranslateToAsync(targetX, targetY, duration, Easing.SinInOut),
            tileView.ScaleToAsync(1.03, duration, Easing.SinInOut));
    }

    private static async Task AnimateMergedTilePopAsync(VisualElement tileView)
    {
        await tileView.ScaleToAsync(1.19, 135, Easing.CubicOut);
        await tileView.ScaleToAsync(1.04, 120, Easing.SinInOut);
    }

    private static async Task WiggleMatchedTileAsync(VisualElement tileView, double direction)
    {
        await tileView.RotateToAsync(5 * direction, 75, Easing.SinOut);
        await tileView.RotateToAsync(-4 * direction, 95, Easing.SinInOut);
        await tileView.RotateToAsync(3 * direction, 85, Easing.SinInOut);
        await tileView.RotateToAsync(0, 95, Easing.SinOut);
    }

    private static async Task AnimateMatchedTileAwayAsync(VisualElement tileView)
    {
        await Task.WhenAll(
            tileView.FadeToAsync(0, 220, Easing.CubicIn),
            tileView.ScaleToAsync(0.78, 220, Easing.CubicIn));
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

    private sealed class GameBackChevronDrawable(Color color) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = color;
            canvas.StrokeSize = 5;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var centerX = dirtyRect.Width * 0.55f;
            var centerY = dirtyRect.Height * 0.5f;
            var halfWidth = dirtyRect.Width * 0.24f;
            var halfHeight = dirtyRect.Height * 0.22f;
            canvas.DrawLine(centerX + halfWidth, centerY - halfHeight, centerX - halfWidth, centerY);
            canvas.DrawLine(centerX - halfWidth, centerY, centerX + halfWidth, centerY + halfHeight);
        }
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

    private sealed record MatchedTilePlacement(
        int Column,
        int Row,
        double WidthRequest,
        double HeightRequest);

    internal sealed class CharacterMatchTile(
        Guid id,
        string pairKey,
        string displayName,
        ProgressiveImageRequest imageRequest) : INotifyPropertyChanged
    {
        private bool _isFaceUp;
        private bool _isMatched;
        private bool _showAfterCompletion;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Id { get; } = id;

        public string PairKey { get; } = pairKey;

        public string DisplayName { get; } = displayName;

        public ProgressiveImageRequest ImageRequest { get; } = imageRequest;

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
