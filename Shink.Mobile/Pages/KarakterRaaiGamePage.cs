using Shink.Mobile.Games;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class KarakterRaaiGamePage : ContentPage, IQueryAttributable
{
    private const int DesiredChoiceCount = 4;
    private const int DefaultRoundCount = 10;
    private static readonly TimeSpan AutoAdvanceDelay = TimeSpan.FromSeconds(3);
    private const string PoppinsBoldFontFamily = "PoppinsBold";
    private static readonly string[] PerfectScoreMessages =
    [
        "Geen Karakter kon vir jou wegkruip nie!",
        "Jy ken die Karakters soos ’n ware Schink-ster!",
        "Elke skaduwee reg—jy is ’n Karakter-kampioen!"
    ];
    private readonly MobileApiClient _apiClient;
    private readonly MobileAnalyticsService _analytics;
    private readonly Label _roundLabel;
    private readonly Label _scoreLabel;
    private readonly Label _messageLabel;
    private readonly ProgressiveCachedImage _mysteryImage;
    private readonly ProgressiveCachedImage _revealImage;
    private readonly ContentView _mysteryLayer;
    private readonly ContentView _revealLayer;
    private readonly Border _imageStage;
    private readonly Grid _choicesGrid;
    private readonly Button _actionButton;
    private readonly ImageButton _newGameButton;
    private readonly Grid _stateOverlay;
    private readonly ActivityIndicator _loadingIndicator;
    private readonly Label _stateLabel;
    private readonly Button _retryButton;
    private readonly GameCelebrationOverlay _celebrationOverlay;
    private readonly Dictionary<string, Border> _choiceButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Label> _choiceLabels = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MobileCharacterCard> _availableCharacters = Array.Empty<MobileCharacterCard>();
    private CharacterGuessGame? _game;
    private MobileCharacterCard? _targetCharacter;
    private string? _targetMysteryImageUrl;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _autoAdvanceCancellation;
    private CancellationTokenSource? _nextRoundPreloadCancellation;
    private Task _nextRoundPreloadTask = Task.CompletedTask;
    private bool _hasLoaded;
    private bool _isPageActive;
    private bool _roundAnswered;
    private bool _hintShown;
    private bool _isReturningToConfiguration;
    private int _selectedRoundCount = DefaultRoundCount;

    public KarakterRaaiGamePage(
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics,
        StoryPlaybackSession storyPlaybackSession)
    {
        _apiClient = apiClient;
        _analytics = analytics;
        Title = "Karakter Raai";
        Background = BuildGameBackground();
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);

        _roundLabel = BuildScoreLabel("Rondte 1");
        _scoreLabel = BuildScoreLabel("0/10");
        _scoreLabel.HorizontalTextAlignment = TextAlignment.End;
        _messageLabel = new Label
        {
            Text = "???",
            FontFamily = PoppinsBoldFontFamily,
            FontSize = 52,
            TextColor = Color.FromArgb("#5B7188"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0)
        };

        _revealImage = BuildCharacterImage();
        _mysteryImage = BuildCharacterImage();
        _revealLayer = BuildCharacterLayer(_revealImage, isVisible: false, opacity: 0, scale: 0.94);
        _mysteryLayer = BuildCharacterLayer(_mysteryImage);
        _imageStage = BuildImageStage();
        _choicesGrid = BuildChoicesGrid();

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
            AutomationId = "character-guess-retry",
            IsVisible = false
        };
        SemanticProperties.SetDescription(_newGameButton, "Speel weer");
        _newGameButton.Clicked += async (_, _) =>
        {
            if (!_newGameButton.IsEnabled)
            {
                return;
            }

            SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
            _newGameButton.IsEnabled = false;
            StartNewGame();
            _newGameButton.IsEnabled = true;
            await Task.CompletedTask;
        };

        _actionButton = BuildPrimaryButton("Gee my ’n leidraad");
        _actionButton.Margin = new Thickness(18, 4, 18, 8);
        _actionButton.IsVisible = false;
        _actionButton.Clicked += async (_, _) => await HandleActionAsync();

        _loadingIndicator = new ActivityIndicator
        {
            Color = Color.FromArgb("#F8C854"),
            IsRunning = true,
            WidthRequest = 42,
            HeightRequest = 42
        };
        _stateLabel = new Label
        {
            Text = "Ons kies ’n geheime karakter …",
            FontSize = 17,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(24, 0)
        };
        _retryButton = BuildPrimaryButton("Probeer weer");
        _retryButton.IsVisible = false;
        _retryButton.Clicked += async (_, _) => await LoadCharactersAsync(forceRefresh: true);
        _celebrationOverlay = new GameCelebrationOverlay
        {
            ZIndex = 500
        };
        _stateOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#166476"),
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

        var root = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Background = Brush.Transparent,
            RowDefinitions =
            {
                new RowDefinition(new GridLength(112)),
                new RowDefinition(new GridLength(120)),
                new RowDefinition(new GridLength(50)),
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(190)),
                new RowDefinition(GridLength.Auto)
            }
        };

        var topBar = BuildGameTopBar();
        var heading = BuildHeading();
        var scoreCard = BuildScoreCard();

        root.Children.Add(topBar);
        root.Children.Add(heading);
        root.Children.Add(scoreCard);
        root.Children.Add(_imageStage);
        root.Children.Add(_choicesGrid);
        root.Children.Add(_actionButton);
        root.Children.Add(_stateOverlay);
        root.Children.Add(_celebrationOverlay);
        Grid.SetRow(heading, 1);
        Grid.SetRow(scoreCard, 2);
        Grid.SetRow(_imageStage, 3);
        Grid.SetRow(_choicesGrid, 4);
        Grid.SetRow(_actionButton, 5);
        Grid.SetRow(_stateOverlay, 1);
        Grid.SetRowSpan(_stateOverlay, 5);
        Grid.SetRowSpan(_celebrationOverlay, 6);
        Content = PersistentPlaybackHost.Wrap(root, storyPlaybackSession, edgeToEdge: true);
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        MobileResponsiveLayout.ApplyCenteredContent(_imageStage, Width, 720);
        MobileResponsiveLayout.ApplyCenteredContent(_choicesGrid, Width, 720);
        MobileResponsiveLayout.ApplyCenteredContent(_actionButton, Width, 640);
        _choicesGrid.Margin = new Thickness(Width >= 600 ? 24 : 14, 0, Width >= 600 ? 24 : 14, 12);
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
        var topBar = new Grid
        {
            Padding = new Thickness(18, 20, 18, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                BuildBackButton(),
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
            VerticalOptions = LayoutOptions.Center,
            Content = new GraphicsView
            {
                Drawable = new GuessBackChevronDrawable(Color.FromArgb("#5B7188")),
                WidthRequest = 32,
                HeightRequest = 32,
                InputTransparent = true
            },
            AutomationId = "character-guess-back"
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
        CancelAutoAdvance();
        SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
        try
        {
            await Shell.Current.GoToAsync("..", animate: false);
        }
        finally
        {
            _isReturningToConfiguration = false;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        if (!_hasLoaded)
        {
            await LoadCharactersAsync();
        }
        else if (_roundAnswered && _game?.IsComplete != true)
        {
            PreloadNextRoundImages();
            ScheduleAutoAdvance();
        }
        else if (_game?.IsComplete != true)
        {
            PreloadNextRoundImages();
        }
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _loadCancellation?.Cancel();
        CancelAutoAdvance();
        CancelNextRoundPreload();
        _celebrationOverlay.Hide();
        base.OnDisappearing();
    }

    private static Label BuildHeading() =>
        new()
        {
            Text = "Wie is die\nkarakter?",
            FontFamily = PoppinsBoldFontFamily,
            FontSize = 36,
            TextColor = Color.FromArgb("#5B7188"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.WordWrap,
            AutomationId = "character-guess-heading"
        };

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
            Children =
            {
                _roundLabel,
                _scoreLabel
            }
        };
        Grid.SetColumn(_scoreLabel, 1);

        return new Border
        {
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(38, 0, 38, 4),
            Content = scoreGrid
        };
    }

    private Border BuildImageStage()
    {
        var stageGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(60))
            },
            Children =
            {
                _revealLayer,
                _mysteryLayer,
                _messageLabel
            }
        };
        Grid.SetRow(_messageLabel, 1);

        return new Border
        {
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Content = stageGrid,
            IsVisible = false,
            AutomationId = "character-guess-stage"
        };
    }

    private ProgressiveCachedImage BuildCharacterImage() =>
        new(_apiClient)
        {
            Aspect = Aspect.AspectFit,
            Margin = new Thickness(0),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };

    private static ContentView BuildCharacterLayer(
        ProgressiveCachedImage image,
        bool isVisible = true,
        double opacity = 1,
        double scale = 1) =>
        new()
        {
            Content = image,
            IsVisible = isVisible,
            Opacity = opacity,
            Scale = scale,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };

    private static Grid BuildChoicesGrid() =>
        new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star)
            },
            ColumnSpacing = 4,
            Margin = new Thickness(24, 0, 24, 12),
            IsVisible = false,
            AutomationId = "character-guess-choices"
        };

    private static Label BuildScoreLabel(string text) =>
        new()
        {
            Text = text,
            FontFamily = PoppinsBoldFontFamily,
            FontSize = 30,
            TextColor = Color.FromArgb("#5B7188"),
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center
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
                ? await _apiClient.GetCharactersAsync(cancellationToken)
                : await _apiClient.GetCachedCharactersAsync(cancellationToken);
            var eligibleCharacters = SelectEligibleCharacters(response);
            if (!forceRefresh && eligibleCharacters.Count < 2)
            {
                response = await _apiClient.GetCharactersAsync(cancellationToken);
                eligibleCharacters = SelectEligibleCharacters(response);
            }

            if (cancellationToken.IsCancellationRequested || !_isPageActive)
            {
                return;
            }

            if (response is null)
            {
                ShowErrorState("Kon nie die Karakters laai nie. Probeer asseblief weer.");
                return;
            }

            if (eligibleCharacters.Count < 2)
            {
                ShowErrorState("Sluit minstens twee Karakters oop deur na hul stories te luister.");
                return;
            }

            _availableCharacters = eligibleCharacters;
            _hasLoaded = true;
            StartNewGame();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (_isPageActive)
            {
                ShowErrorState("Kon nie die Karakters laai nie. Probeer asseblief weer.");
            }
        }
    }

    private static IReadOnlyList<MobileCharacterCard> SelectEligibleCharacters(MobileCharactersResponse? response) =>
        response?.Characters
            .Where(static character => character.IsUnlocked)
            .Where(static character => !string.IsNullOrWhiteSpace(character.Slug))
            .Where(static character => !string.IsNullOrWhiteSpace(character.DisplayName))
            .Where(static character => !string.IsNullOrWhiteSpace(character.ImageUrl))
            .Where(static character => !string.IsNullOrWhiteSpace(
                CharacterMysteryImageResolver.Resolve(character.ImageUrl, character.MysteryImageUrl)))
            .DistinctBy(static character => character.Slug, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? Array.Empty<MobileCharacterCard>();

    private void StartNewGame()
    {
        if (_availableCharacters.Count < 2)
        {
            return;
        }

        CancelAutoAdvance();
        CancelNextRoundPreload();
        _celebrationOverlay.Hide();

        _game = new CharacterGuessGame(
            _availableCharacters.Select(static character => character.Slug),
            _selectedRoundCount,
            DesiredChoiceCount);
        _stateOverlay.IsVisible = false;
        _imageStage.IsVisible = true;
        _choicesGrid.IsVisible = true;
        _actionButton.IsVisible = false;
        _newGameButton.IsVisible = true;
        _newGameButton.IsEnabled = true;

        _analytics.TrackEvent("mobile_character_guess_started", new Dictionary<string, object>
        {
            ["available_character_count"] = _availableCharacters.Count,
            ["round_count"] = _selectedRoundCount
        });
        StartNextRound();
    }

    private void StartNextRound()
    {
        var game = _game;
        if (game is null)
        {
            return;
        }

        var round = game.StartNextRound();
        _targetCharacter = FindCharacter(round.TargetKey);
        _targetMysteryImageUrl = _targetCharacter is null
            ? null
            : CharacterMysteryImageResolver.Resolve(
                _targetCharacter.ImageUrl,
                _targetCharacter.MysteryImageUrl);
        if (_targetCharacter is null || string.IsNullOrWhiteSpace(_targetMysteryImageUrl))
        {
            ShowErrorState("Kon nie die geheime Karakter kry nie. Probeer asseblief weer.");
            return;
        }

        _roundAnswered = false;
        _hintShown = false;
        _roundLabel.Text = $"Rondte {round.RoundNumber}";
        _scoreLabel.Text = $"{game.Score}/{game.TotalRounds}";
        _messageLabel.Text = "???";
        _messageLabel.FontSize = 52;
        _messageLabel.TextColor = Color.FromArgb("#5B7188");
        _mysteryLayer.CancelAnimations();
        _mysteryLayer.IsVisible = true;
        _mysteryLayer.Opacity = 1;
        _mysteryLayer.Scale = 1;
        _revealLayer.CancelAnimations();
        _revealLayer.IsVisible = false;
        _revealLayer.Opacity = 0;
        _revealLayer.Scale = 0.94;
        _mysteryImage.SetImage(
            _targetMysteryImageUrl,
            _targetCharacter.MysteryPreviewImageUrl,
            fallbackFile: "schink_placeholder.png");
        _revealImage.SetImage(
            _targetCharacter.ImageUrl,
            _targetCharacter.MatchPreviewImageUrl,
            fallbackFile: "schink_placeholder.png");
        RenderChoices(round);

        _actionButton.IsVisible = false;
        _actionButton.IsEnabled = true;
        _actionButton.Opacity = 1;

        _ = _apiClient.CacheImagesAsync(
            [_targetMysteryImageUrl, _targetCharacter.ImageUrl],
            _loadCancellation?.Token ?? default,
            maxImages: 2,
            maxDegreeOfParallelism: 2);
        PreloadNextRoundImages();
    }

    private void PreloadNextRoundImages()
    {
        CancelNextRoundPreload();
        var nextRound = _game?.PrepareNextRound();
        if (nextRound is null)
        {
            _nextRoundPreloadTask = Task.CompletedTask;
            return;
        }

        var urls = new List<string?>();
        foreach (var choiceKey in nextRound.ChoiceKeys)
        {
            var choice = FindCharacter(choiceKey);
            if (choice is null)
            {
                continue;
            }

            urls.Add(choice.ImageUrl);
            urls.Add(choice.MatchPreviewImageUrl);
        }

        var target = FindCharacter(nextRound.TargetKey);
        if (target is not null)
        {
            urls.Add(CharacterMysteryImageResolver.Resolve(target.ImageUrl, target.MysteryImageUrl));
            urls.Add(target.MysteryPreviewImageUrl);
        }

        var cancellation = new CancellationTokenSource();
        _nextRoundPreloadCancellation = cancellation;
        _nextRoundPreloadTask = PreloadNextRoundImagesAsync(urls, cancellation);
    }

    private async Task PreloadNextRoundImagesAsync(
        IReadOnlyList<string?> urls,
        CancellationTokenSource cancellation)
    {
        try
        {
            await _apiClient.CacheImagesAsync(
                urls,
                cancellation.Token,
                maxImages: 10,
                maxDegreeOfParallelism: 2);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_nextRoundPreloadCancellation, cancellation))
            {
                _nextRoundPreloadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelNextRoundPreload()
    {
        var cancellation = _nextRoundPreloadCancellation;
        _nextRoundPreloadCancellation = null;
        cancellation?.Cancel();
    }

    private void RenderChoices(CharacterGuessRound round)
    {
        _choiceButtons.Clear();
        _choiceLabels.Clear();
        _choicesGrid.Children.Clear();

        for (var index = 0; index < round.ChoiceKeys.Count; index++)
        {
            var character = FindCharacter(round.ChoiceKeys[index]);
            if (character is null)
            {
                continue;
            }

            var choice = BuildChoiceButton(character);
            _choiceButtons[character.Slug] = choice;
            _choicesGrid.Children.Add(choice);
            Grid.SetColumn(choice, index);
            Grid.SetRow(choice, 0);
        }
    }

    private Border BuildChoiceButton(MobileCharacterCard character)
    {
        var image = new ProgressiveCachedImage(
            _apiClient,
            new ProgressiveImageRequest(
                character.ImageUrl,
                character.MatchPreviewImageUrl,
                "schink_placeholder.png"))
        {
            Aspect = Aspect.AspectFit,
            HeightRequest = 138,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };
        var label = new Label
        {
            Text = character.DisplayName,
            FontFamily = PoppinsBoldFontFamily,
            FontSize = 16,
            TextColor = Color.FromArgb("#5B7188"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.WordWrap,
            InputTransparent = true
        };
        _choiceLabels[character.Slug] = label;

        var content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(28))
            },
            Children =
            {
                image,
                label
            }
        };
        Grid.SetRow(label, 1);

        var button = new Border
        {
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Content = content,
            AutomationId = $"character-guess-choice-{character.Slug}"
        };
        SemanticProperties.SetDescription(button, $"Kies {character.DisplayName}");

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await HandleChoiceTappedAsync(character.Slug);
        button.GestureRecognizers.Add(tap);
        return button;
    }

    private async Task HandleChoiceTappedAsync(string characterKey)
    {
        var game = _game;
        var target = _targetCharacter;
        if (game is null || target is null || _roundAnswered)
        {
            return;
        }

        var result = game.Guess(characterKey);
        if (result.Outcome == CharacterGuessOutcome.Ignored)
        {
            return;
        }

        _roundAnswered = true;
        SafeHapticFeedback.TryPerform(
            result.Outcome == CharacterGuessOutcome.Correct
                ? HapticFeedbackType.LongPress
                : HapticFeedbackType.Click);

        foreach (var choice in _choiceButtons.Values)
        {
            choice.InputTransparent = true;
            choice.Opacity = 0.76;
        }

        if (_choiceButtons.TryGetValue(result.CorrectKey, out var correctChoice))
        {
            ApplyChoiceResultStyle(
                correctChoice,
                isCorrect: true,
                label: _choiceLabels.GetValueOrDefault(result.CorrectKey));
        }

        if (result.Outcome == CharacterGuessOutcome.Incorrect &&
            _choiceButtons.TryGetValue(characterKey, out var incorrectChoice))
        {
            ApplyChoiceResultStyle(
                incorrectChoice,
                isCorrect: false,
                label: _choiceLabels.GetValueOrDefault(characterKey));
        }

        await RevealCharacterAsync();
        _scoreLabel.Text = $"{result.Score}/{game.TotalRounds}";
        _messageLabel.FontSize = 18;
        _messageLabel.TextColor = result.Outcome == CharacterGuessOutcome.Correct
            ? Color.FromArgb("#18794E")
            : Color.FromArgb("#B14D32");
        _messageLabel.Text = result.IsComplete
            ? game.HasPerfectScore
                ? $"VOLPUNTE! {result.Score} uit {game.TotalRounds}!"
                : $"Dis {target.DisplayName}! Eindtelling: {result.Score} uit {game.TotalRounds}."
            : result.Outcome == CharacterGuessOutcome.Correct
                ? $"Ja! Dis {target.DisplayName}!"
                : $"Byna! Dis {target.DisplayName}.";

        if (result.IsComplete)
        {
            _actionButton.Text = "Speel weer";
            _actionButton.IsEnabled = true;
            _actionButton.Opacity = 1;
            _actionButton.IsVisible = true;
        }
        else
        {
            _actionButton.IsVisible = false;
            ScheduleAutoAdvance();
        }

        _analytics.TrackEvent("mobile_character_guess_answered", new Dictionary<string, object>
        {
            ["round_number"] = game.RoundNumber,
            ["is_correct"] = result.Outcome == CharacterGuessOutcome.Correct,
            ["used_hint"] = _hintShown,
            ["score"] = result.Score
        });
        if (result.IsComplete)
        {
            _analytics.TrackEvent("mobile_character_guess_completed", new Dictionary<string, object>
            {
                ["score"] = result.Score,
                ["round_count"] = game.TotalRounds,
                ["is_perfect_score"] = game.HasPerfectScore
            });
            if (game.HasPerfectScore)
            {
                _ = _celebrationOverlay.CelebrateAsync(
                    "KARAKTER-KAMPIOEN!",
                    BuildPerfectScoreMessage(game));
            }
        }
    }

    private void ScheduleAutoAdvance()
    {
        if (!_isPageActive || !_roundAnswered || _game?.IsComplete == true)
        {
            return;
        }

        CancelAutoAdvance();
        var cancellation = new CancellationTokenSource();
        _autoAdvanceCancellation = cancellation;
        _ = AdvanceToNextRoundAsync(cancellation);
    }

    private async Task AdvanceToNextRoundAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(AutoAdvanceDelay, cancellation.Token);
            await _nextRoundPreloadTask.WaitAsync(cancellation.Token);
            if (_isPageActive && _roundAnswered && _game?.IsComplete != true)
            {
                StartNextRound();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_autoAdvanceCancellation, cancellation))
            {
                _autoAdvanceCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelAutoAdvance()
    {
        var cancellation = _autoAdvanceCancellation;
        _autoAdvanceCancellation = null;
        cancellation?.Cancel();
    }

    private static void ApplyChoiceResultStyle(Border choice, bool isCorrect, Label? label)
    {
        choice.BackgroundColor = Color.FromArgb(isCorrect ? "#D9F3E4" : "#FDE4DE");
        choice.Stroke = Color.FromArgb(isCorrect ? "#18794E" : "#B14D32");
        choice.StrokeThickness = 2;
        choice.Opacity = 1;
        if (label is not null)
        {
            label.TextColor = Color.FromArgb(isCorrect ? "#11643F" : "#8E3526");
        }
    }

    private async Task RevealCharacterAsync()
    {
        if (_mysteryLayer.Handler is null || _revealLayer.Handler is null)
        {
            _mysteryLayer.IsVisible = false;
            _mysteryLayer.Opacity = 0;
            _revealLayer.IsVisible = true;
            _revealLayer.Opacity = 1;
            _revealLayer.Scale = 1;
            return;
        }

        _revealLayer.IsVisible = true;
        _revealLayer.Opacity = 0;
        _revealLayer.Scale = 0.94;
        await _mysteryLayer.FadeToAsync(0, 170, Easing.CubicIn);
        _mysteryLayer.IsVisible = false;
        await Task.WhenAll(
            _revealLayer.FadeToAsync(1, 250, Easing.CubicOut),
            _revealLayer.ScaleToAsync(1, 250, Easing.CubicOut));
    }

    private async Task HandleActionAsync()
    {
        if (_roundAnswered)
        {
            SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
            if (_game?.IsComplete == true)
            {
                StartNewGame();
            }
            else
            {
                StartNextRound();
            }

            return;
        }

        if (_targetCharacter is null || _hintShown)
        {
            return;
        }

        SafeHapticFeedback.TryPerform(HapticFeedbackType.Click);
        _hintShown = true;
        _messageLabel.FontSize = 18;
        _messageLabel.Text = BuildHintText(_targetCharacter);
        _messageLabel.TextColor = Color.FromArgb("#166476");
        _actionButton.Text = "Leidraad gewys";
        _actionButton.IsEnabled = false;
        _actionButton.Opacity = 0.74;
        _analytics.TrackEvent("mobile_character_guess_hint_used", new Dictionary<string, object>
        {
            ["round_number"] = _game?.RoundNumber ?? 0
        });
        await Task.CompletedTask;
    }

    private static string BuildHintText(MobileCharacterCard character)
    {
        var hints = new[]
        {
            BuildHint("Tipe", character.Species),
            BuildHint("Blyplek", character.Habitat),
            BuildHint("Gunsteling-ding", character.FavoriteThing),
            BuildHint("Eienskap", character.CharacterTrait),
            BuildHint("Sê-ding", character.Catchphrase)
        };
        return hints.FirstOrDefault(static hint => !string.IsNullOrWhiteSpace(hint))
            ?? "Geen leidraad vandag nie—vertrou jou oë!";
    }

    private static string? BuildHint(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{label}: {value.Trim()}";

    private static string BuildPerfectScoreMessage(CharacterGuessGame game)
    {
        var message = PerfectScoreMessages[Random.Shared.Next(PerfectScoreMessages.Length)];
        return $"{game.Score} uit {game.TotalRounds}! {message}";
    }

    private MobileCharacterCard? FindCharacter(string key) =>
        _availableCharacters.FirstOrDefault(character =>
            string.Equals(character.Slug, key, StringComparison.OrdinalIgnoreCase));

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("rounds", out var value))
        {
            return;
        }

        if (int.TryParse(value?.ToString(), out var roundCount) && roundCount > 0)
        {
            _selectedRoundCount = CharacterGuessDifficultyCatalog.FromRoundCount(roundCount).TotalRounds;
            _roundLabel.Text = "Rondte 1";
        }
    }

    private void ShowLoadingState()
    {
        _imageStage.IsVisible = false;
        _choicesGrid.IsVisible = false;
        _actionButton.IsVisible = false;
        _newGameButton.IsVisible = false;
        _stateOverlay.IsVisible = true;
        _loadingIndicator.IsRunning = true;
        _loadingIndicator.IsVisible = true;
        _retryButton.IsVisible = false;
        _stateLabel.Text = "Ons kies ’n geheime karakter …";
    }

    private void ShowErrorState(string message)
    {
        _imageStage.IsVisible = false;
        _choicesGrid.IsVisible = false;
        _actionButton.IsVisible = false;
        _newGameButton.IsVisible = false;
        _stateOverlay.IsVisible = true;
        _loadingIndicator.IsRunning = false;
        _loadingIndicator.IsVisible = false;
        _retryButton.IsVisible = true;
        _stateLabel.Text = message;
    }

    private sealed class GuessBackChevronDrawable(Color color) : IDrawable
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
}
