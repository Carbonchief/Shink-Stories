using Shink.Mobile.Games;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Mobile.Pages;

public sealed class KarakterRaaiGamePage : ContentPage
{
    private const int DesiredChoiceCount = 4;
    private const int TotalRoundCount = 10;
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
    private readonly Image _mysteryImage;
    private readonly Image _revealImage;
    private readonly Border _imageStage;
    private readonly Grid _choicesGrid;
    private readonly Button _actionButton;
    private readonly Grid _stateOverlay;
    private readonly ActivityIndicator _loadingIndicator;
    private readonly Label _stateLabel;
    private readonly Button _retryButton;
    private readonly GameCelebrationOverlay _celebrationOverlay;
    private readonly Dictionary<string, Border> _choiceButtons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MobileCharacterCard> _availableCharacters = Array.Empty<MobileCharacterCard>();
    private CharacterGuessGame? _game;
    private MobileCharacterCard? _targetCharacter;
    private string? _targetMysteryImageUrl;
    private CancellationTokenSource? _loadCancellation;
    private bool _hasLoaded;
    private bool _isPageActive;
    private bool _roundAnswered;
    private bool _hintShown;

    public KarakterRaaiGamePage(
        MobileApiClient apiClient,
        SessionState sessionState,
        MobileAnalyticsService analytics)
    {
        _apiClient = apiClient;
        _analytics = analytics;
        Title = "Wie is dié Karakter?";
        BackgroundColor = Color.FromArgb("#166476");
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container);
        Shell.SetNavBarIsVisible(this, false);

        _roundLabel = BuildScoreLabel("Rondte: 1 / 10");
        _scoreLabel = BuildScoreLabel("Punte: 0");
        _messageLabel = new Label
        {
            Text = "Kyk mooi na die skaduwee!",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#166476"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(8, 3, 8, 0)
        };

        _revealImage = BuildCharacterImage();
        _revealImage.Opacity = 0;
        _revealImage.Scale = 0.94;
        _mysteryImage = BuildCharacterImage();
        _imageStage = BuildImageStage();
        _choicesGrid = BuildChoicesGrid();

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
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb("#166476"), 0),
                    new(Color.FromArgb("#46969E"), 0.52f),
                    new(Color.FromArgb("#68B6B5"), 1)
                },
                new Point(0, 0),
                new Point(0, 1)),
            RowDefinitions =
            {
                new RowDefinition(new GridLength(62)),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(114)),
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
        Content = root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        if (!_hasLoaded)
        {
            await LoadCharactersAsync();
        }
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
            Padding = new Thickness(18, 0, 18, 5),
            Spacing = 1,
            Children =
            {
                new Label
                {
                    Text = "WIE IS DIÉ KARAKTER?",
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    CharacterSpacing = 1
                },
                new Label
                {
                    Text = "Raai wie agter die skaduwee wegkruip.",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#F4FFFE"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.TailTruncation
                }
            }
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
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            Stroke = Color.FromArgb("#F3DEB4"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 17 },
            Padding = new Thickness(12, 7),
            Margin = new Thickness(12, 0, 12, 6),
            Content = scoreGrid
        };
    }

    private Border BuildImageStage()
    {
        var imageBackdrop = new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb("#C9EFED"), 0),
                    new(Color.FromArgb("#F9E8B7"), 1)
                },
                new Point(0, 0),
                new Point(1, 1)),
            Stroke = Color.FromArgb("#E7D1A2"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 32 },
            Margin = new Thickness(12, 4),
            Content = new Grid
            {
                Children =
                {
                    _revealImage,
                    _mysteryImage
                }
            }
        };

        var stageGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(42))
            },
            Children =
            {
                imageBackdrop,
                _messageLabel
            }
        };
        Grid.SetRow(_messageLabel, 1);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#FFFDF7"),
            Stroke = Color.FromArgb("#E7D1A2"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Padding = new Thickness(5),
            Margin = new Thickness(12, 0, 12, 6),
            Content = stageGrid,
            IsVisible = false,
            AutomationId = "character-guess-stage"
        };
    }

    private static Image BuildCharacterImage() =>
        new()
        {
            Aspect = Aspect.AspectFit,
            Margin = new Thickness(8, 3),
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
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            RowSpacing = 8,
            Margin = new Thickness(12, 0),
            IsVisible = false,
            AutomationId = "character-guess-choices"
        };

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

        _celebrationOverlay.Hide();

        _game = new CharacterGuessGame(
            _availableCharacters.Select(static character => character.Slug),
            TotalRoundCount,
            DesiredChoiceCount);
        _stateOverlay.IsVisible = false;
        _imageStage.IsVisible = true;
        _choicesGrid.IsVisible = true;
        _actionButton.IsVisible = true;

        _analytics.TrackEvent("mobile_character_guess_started", new Dictionary<string, object>
        {
            ["available_character_count"] = _availableCharacters.Count,
            ["round_count"] = TotalRoundCount
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
        _roundLabel.Text = $"Rondte: {round.RoundNumber} / {game.TotalRounds}";
        _scoreLabel.Text = $"Punte: {game.Score}";
        _messageLabel.Text = "Kyk mooi na die skaduwee!";
        _messageLabel.TextColor = Color.FromArgb("#166476");
        _mysteryImage.Source = _apiClient.BuildCachedImageSource(
            _targetMysteryImageUrl,
            "schink_character_lineup.png");
        _mysteryImage.Opacity = 1;
        _mysteryImage.Scale = 1;
        _revealImage.Source = _apiClient.BuildCachedImageSource(
            _targetCharacter.ImageUrl,
            "schink_character_lineup.png");
        _revealImage.Opacity = 0;
        _revealImage.Scale = 0.94;
        RenderChoices(round);

        _actionButton.Text = "Gee my ’n leidraad";
        _actionButton.IsEnabled = true;
        _actionButton.Opacity = 1;

        _ = _apiClient.CacheImagesAsync(
            [_targetMysteryImageUrl, _targetCharacter.ImageUrl],
            _loadCancellation?.Token ?? default,
            maxImages: 2,
            maxDegreeOfParallelism: 2);
    }

    private void RenderChoices(CharacterGuessRound round)
    {
        _choiceButtons.Clear();
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
            Grid.SetColumn(choice, index % 2);
            Grid.SetRow(choice, index / 2);
        }
    }

    private Border BuildChoiceButton(MobileCharacterCard character)
    {
        var label = new Label
        {
            Text = character.DisplayName,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#27313A"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.WordWrap,
            InputTransparent = true
        };
        var button = new Border
        {
            BackgroundColor = Color.FromArgb("#FFF7E8"),
            Stroke = Color.FromArgb("#E7D1A2"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(8, 2),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Content = label,
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
            ApplyChoiceResultStyle(correctChoice, isCorrect: true);
        }

        if (result.Outcome == CharacterGuessOutcome.Incorrect &&
            _choiceButtons.TryGetValue(characterKey, out var incorrectChoice))
        {
            ApplyChoiceResultStyle(incorrectChoice, isCorrect: false);
        }

        await RevealCharacterAsync();
        _scoreLabel.Text = $"Punte: {result.Score}";
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

        _actionButton.Text = result.IsComplete ? "Speel weer" : "Volgende Karakter";
        _actionButton.IsEnabled = true;
        _actionButton.Opacity = 1;

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

    private static void ApplyChoiceResultStyle(Border choice, bool isCorrect)
    {
        choice.BackgroundColor = Color.FromArgb(isCorrect ? "#D9F3E4" : "#FDE4DE");
        choice.Stroke = Color.FromArgb(isCorrect ? "#18794E" : "#B14D32");
        choice.StrokeThickness = 2;
        choice.Opacity = 1;
        if (choice.Content is Label label)
        {
            label.TextColor = Color.FromArgb(isCorrect ? "#11643F" : "#8E3526");
        }
    }

    private async Task RevealCharacterAsync()
    {
        if (_mysteryImage.Handler is null || _revealImage.Handler is null)
        {
            _mysteryImage.Opacity = 0;
            _revealImage.Opacity = 1;
            _revealImage.Scale = 1;
            return;
        }

        await _mysteryImage.FadeToAsync(0, 170, Easing.CubicIn);
        await Task.WhenAll(
            _revealImage.FadeToAsync(1, 250, Easing.CubicOut),
            _revealImage.ScaleToAsync(1, 250, Easing.CubicOut));
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

    private void ShowLoadingState()
    {
        _imageStage.IsVisible = false;
        _choicesGrid.IsVisible = false;
        _actionButton.IsVisible = false;
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
        _stateOverlay.IsVisible = true;
        _loadingIndicator.IsRunning = false;
        _loadingIndicator.IsVisible = false;
        _retryButton.IsVisible = true;
        _stateLabel.Text = message;
    }
}
