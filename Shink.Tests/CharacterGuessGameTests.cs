using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Games;

namespace Shink.Tests;

[TestClass]
public class CharacterGuessGameTests
{
    private static readonly string[] CharacterKeys =
    [
        "cool-krokodil",
        "prinses-panda",
        "rudie-renoster",
        "wim-wurmpie",
        "sussa-seeumeeu"
    ];

    [TestMethod]
    public void EveryRoundContainsTheTargetAndUniqueChoices()
    {
        var game = new CharacterGuessGame(CharacterKeys, totalRounds: 5, desiredChoiceCount: 4, new Random(42));

        var round = game.StartNextRound();

        Assert.AreEqual(4, round.ChoiceKeys.Count);
        Assert.AreEqual(4, round.ChoiceKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        CollectionAssert.Contains(round.ChoiceKeys.ToArray(), round.TargetKey);
    }

    [TestMethod]
    public void MysteryImageResolverSupportsTheCurrentProductionPayloadShape()
    {
        const string imageUrl = "https://www.schink.co.za/branding/characters/cool-krokodil.png?v=42";

        var resolvedUrl = CharacterMysteryImageResolver.Resolve(imageUrl, mysteryImageUrl: null);

        Assert.AreEqual(
            "https://www.schink.co.za/branding/characters/cool-krokodil-mystery.png?v=42",
            resolvedUrl);
        Assert.AreEqual(
            "https://cdn.example/mystery/custom.png",
            CharacterMysteryImageResolver.Resolve(imageUrl, "https://cdn.example/mystery/custom.png"));
    }

    [TestMethod]
    public void ConsecutiveRoundsNeverRepeatTheSameTarget()
    {
        var game = new CharacterGuessGame(CharacterKeys, totalRounds: 5, desiredChoiceCount: 4, new Random(17));
        string? previousTarget = null;

        for (var roundNumber = 1; roundNumber <= game.TotalRounds; roundNumber++)
        {
            var round = game.StartNextRound();
            Assert.AreEqual(roundNumber, round.RoundNumber);
            Assert.AreNotEqual(previousTarget, round.TargetKey);
            previousTarget = round.TargetKey;
            game.Guess(round.TargetKey);
        }

        Assert.IsTrue(game.HasPerfectScore);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(20)]
    [DataRow(30)]
    public void SelectedDifficultyCompletesEveryRoundWithoutRepeatingTargets(int selectedRounds)
    {
        var difficulty = CharacterGuessDifficultyCatalog.FromRoundCount(selectedRounds);
        var keys = Enumerable.Range(1, 40).Select(index => $"character-{index}").ToArray();

        for (var seed = 0; seed < 20; seed++)
        {
            var game = new CharacterGuessGame(keys, difficulty.TotalRounds, random: new Random(seed));
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CharacterGuessRound? prepared = null;
            Assert.AreEqual(selectedRounds, game.TotalRounds);

            for (var number = 1; number <= selectedRounds; number++)
            {
                var round = game.StartNextRound();
                if (prepared is not null)
                {
                    Assert.AreSame(prepared, round);
                }

                Assert.AreEqual(number, round.RoundNumber);
                Assert.IsTrue(targets.Add(round.TargetKey), $"Repeated target at round {number}, seed {seed}.");
                Assert.AreEqual(game.ChoiceCount, round.ChoiceKeys.Distinct().Count());
                CollectionAssert.Contains(round.ChoiceKeys.ToArray(), round.TargetKey);
                prepared = game.PrepareNextRound();
                Assert.AreSame(prepared, game.PrepareNextRound());
                Assert.AreEqual(number == selectedRounds, prepared is null);

                // Wrong answers must not end the game early either.
                var answer = number % 3 == 0
                    ? round.ChoiceKeys.First(key => key != round.TargetKey)
                    : round.TargetKey;
                var result = game.Guess(answer);
                Assert.AreEqual(number == selectedRounds, result.IsComplete);
                Assert.AreEqual(number == selectedRounds, game.IsComplete);
            }

            Assert.AreEqual(selectedRounds, targets.Count);
            Assert.AreEqual(selectedRounds - selectedRounds / 3, game.Score);
            Assert.IsNull(game.PrepareNextRound());
            Assert.ThrowsExactly<InvalidOperationException>(() => game.StartNextRound());
        }
    }

    [TestMethod]
    public void DuplicateCatalogEntriesCannotCreateRepeatTargets()
    {
        var keys = Enumerable.Range(1, 30).Select(index => $"character-{index}").ToArray();
        var input = keys.Concat(keys.Select(key => $" {key.ToUpperInvariant()} ")).Append(" ");
        var game = new CharacterGuessGame(input, totalRounds: 30, random: new Random(17));
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var number = 1; number <= 30; number++)
        {
            var round = game.StartNextRound();
            Assert.IsTrue(targets.Add(round.TargetKey));
            game.PrepareNextRound();
            game.PrepareNextRound();
            Assert.AreEqual(number == 30, game.Guess(round.TargetKey).IsComplete);
        }

        Assert.IsTrue(game.HasPerfectScore);
        Assert.AreEqual(30, game.Score);
    }

    [TestMethod]
    public void InsufficientUniqueCharactersCannotSilentlyShortenOrRepeatTheGame()
    {
        var keys = Enumerable.Range(1, 20).Select(index => $"character-{index}").ToArray();
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CharacterGuessGame(keys.Concat(keys), totalRounds: 30));
    }

    [TestMethod]
    public void LockedCharactersSupplyFullAnswerArtworkAndThirtyUniqueRounds()
    {
        // MatchImageUrl contains the full artwork even when the profile ImageUrl is a silhouette.
        var characters = Enumerable.Range(1, 30).Select(index => new
        {
            Slug = $"character-{index}",
            DisplayName = $"Karakter {index}",
            IsUnlocked = false,
            ImageUrl = $"https://example.com/character-{index}-mystery.png",
            MatchImageUrl = $"https://example.com/character-{index}.png",
            MysteryImageUrl = $"https://example.com/character-{index}-mystery.png"
        }).ToArray();
        var json = System.Text.Json.JsonSerializer.Serialize(new { Characters = characters });
        var response = System.Text.Json.JsonSerializer.Deserialize<Shink.Mobile.Models.MobileCharactersResponse>(json)!;
        var eligible = CharacterGuessCatalog.SelectEligibleCharacters(response);

        Assert.AreEqual(30, eligible.Count);
        Assert.IsTrue(eligible.All(character => !character.IsUnlocked));
        Assert.IsTrue(eligible.All(character => character.ImageUrl == character.MatchImageUrl));
        Assert.IsTrue(eligible.All(character => character.ImageUrl != character.MysteryImageUrl));
        var game = new CharacterGuessGame(eligible.Select(character => character.Slug), totalRounds: 30);
        var targets = new HashSet<string>();
        while (!game.IsComplete)
        {
            var round = game.StartNextRound();
            Assert.IsTrue(targets.Add(round.TargetKey));
            game.Guess(round.TargetKey);
        }

        Assert.AreEqual(30, game.RoundNumber);
    }

    [TestMethod]
    public void CatalogFiltersMissingArtworkAndDuplicateSlugsWithoutUnlockingProfiles()
    {
        const string json = """
            { "Characters": [
                { "Slug": " alpha ", "DisplayName": "Alpha", "IsUnlocked": false,
                  "ImageUrl": "alpha-mystery.png", "MatchImageUrl": "alpha.png" },
                { "Slug": "ALPHA", "DisplayName": "Alpha", "IsUnlocked": true, "ImageUrl": "alpha.png" },
                { "Slug": "beta", "DisplayName": "Beta", "IsUnlocked": true, "ImageUrl": "beta.png" },
                { "Slug": "gamma", "DisplayName": "Gamma", "IsUnlocked": false, "ImageUrl": "gamma-mystery.png" },
                { "Slug": "delta", "DisplayName": "", "IsUnlocked": true, "ImageUrl": "delta.png" },
                { "Slug": null, "DisplayName": "Missing slug", "MatchImageUrl": "missing.png" }
            ] }
            """;
        var response = System.Text.Json.JsonSerializer.Deserialize<Shink.Mobile.Models.MobileCharactersResponse>(json)!;
        var eligible = CharacterGuessCatalog.SelectEligibleCharacters(response);

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, eligible.Select(character => character.Slug).ToArray());
        Assert.AreEqual("alpha.png", eligible[0].ImageUrl);
        Assert.IsFalse(eligible[0].IsUnlocked);
        Assert.AreEqual("beta.png", eligible[1].ImageUrl);
        Assert.AreEqual("alpha-mystery.png", response.Characters[0].ImageUrl);
        Assert.IsEmpty(CharacterGuessCatalog.SelectEligibleCharacters(null));
    }

    [TestMethod]
    public void PreparedNextRoundIsUsedAfterTheCurrentAnswer()
    {
        var game = new CharacterGuessGame(CharacterKeys, totalRounds: 3, desiredChoiceCount: 4, new Random(29));
        var currentRound = game.StartNextRound();

        var preparedRound = game.PrepareNextRound();

        Assert.IsNotNull(preparedRound);
        Assert.AreEqual(2, preparedRound.RoundNumber);
        Assert.AreNotEqual(currentRound.TargetKey, preparedRound.TargetKey);
        game.Guess(currentRound.TargetKey);
        Assert.AreSame(preparedRound, game.StartNextRound());
    }

    [TestMethod]
    public void FinalRoundDoesNotPrepareAnotherRound()
    {
        var game = new CharacterGuessGame(CharacterKeys, totalRounds: 1, desiredChoiceCount: 4, new Random(31));
        game.StartNextRound();

        Assert.IsNull(game.PrepareNextRound());
    }

    [TestMethod]
    public void CorrectAndIncorrectGuessesUpdateScoreStreakAndCompletion()
    {
        var game = new CharacterGuessGame(CharacterKeys, totalRounds: 2, desiredChoiceCount: 4, new Random(7));
        var firstRound = game.StartNextRound();

        var firstResult = game.Guess(firstRound.TargetKey);

        Assert.AreEqual(CharacterGuessOutcome.Correct, firstResult.Outcome);
        Assert.AreEqual(1, firstResult.Score);
        Assert.AreEqual(1, firstResult.Streak);
        Assert.IsFalse(firstResult.IsComplete);

        var secondRound = game.StartNextRound();
        var incorrectChoice = secondRound.ChoiceKeys.First(key =>
            !string.Equals(key, secondRound.TargetKey, StringComparison.OrdinalIgnoreCase));
        var finalResult = game.Guess(incorrectChoice);

        Assert.AreEqual(CharacterGuessOutcome.Incorrect, finalResult.Outcome);
        Assert.AreEqual(1, finalResult.Score);
        Assert.AreEqual(0, finalResult.Streak);
        Assert.IsTrue(finalResult.IsComplete);
        Assert.IsTrue(game.IsComplete);
        Assert.IsFalse(game.HasPerfectScore);
    }

    [TestMethod]
    public void ASecondAnswerForTheSameRoundIsIgnored()
    {
        var game = new CharacterGuessGame(CharacterKeys, totalRounds: 2, desiredChoiceCount: 4, new Random(3));
        var round = game.StartNextRound();

        game.Guess(round.TargetKey);
        var duplicateResult = game.Guess(round.TargetKey);

        Assert.AreEqual(CharacterGuessOutcome.Ignored, duplicateResult.Outcome);
        Assert.AreEqual(1, duplicateResult.Score);
    }

    [TestMethod]
    public void MobileGuessGameUsesMysteryArtworkAndIsAvailableFromBothMenus()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterRaaiGamePage.cs"));
        var configPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterRaaiConfigPage.cs"));
        var difficultyCatalog = File.ReadAllText(GetRepoPath("Shink.Mobile", "Games", "CharacterGuessDifficulty.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));
        var appShell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(gamePage, "public sealed class KarakterRaaiGamePage : ContentPage");
        StringAssert.Contains(gamePage, "Text = \"Wie is die\\nkarakter?\"");
        StringAssert.Contains(gamePage, "IQueryAttributable");
        StringAssert.Contains(gamePage, "private readonly ProgressiveCachedImage _mysteryImage;");
        StringAssert.Contains(gamePage, "private readonly ProgressiveCachedImage _revealImage;");
        StringAssert.Contains(gamePage, "private readonly ContentView _mysteryLayer;");
        StringAssert.Contains(gamePage, "private readonly ContentView _revealLayer;");
        StringAssert.Contains(gamePage, "private readonly ImageButton _newGameButton;");
        StringAssert.Contains(gamePage, "AutomationId = \"character-guess-retry\"");
        StringAssert.Contains(gamePage, "Glyph = \"\\uf2f1\"");
        StringAssert.Contains(gamePage, "FontFamily = \"FontAwesomeSolid\"");
        StringAssert.Contains(gamePage, "Text = \"???\"");
        StringAssert.Contains(gamePage, "Text = \"Rondte 1\"");
        StringAssert.Contains(gamePage, "BuildScoreLabel(\"0/10\")");
        StringAssert.Contains(gamePage, "new RowDefinition(new GridLength(120))");
        StringAssert.Contains(gamePage, "new RowDefinition(new GridLength(50))");
        StringAssert.Contains(gamePage, "FontSize = 36,");
        StringAssert.Contains(gamePage, "FontSize = 30,");
        StringAssert.Contains(gamePage, "new RowDefinition(new GridLength(60))");
        StringAssert.Contains(gamePage, "Margin = new Thickness(0),");
        StringAssert.Contains(gamePage, "Grid.SetColumn(choice, index);");
        StringAssert.Contains(gamePage, "Grid.SetRow(choice, 0);");
        StringAssert.Contains(gamePage, "CharacterMysteryImageResolver.Resolve(");
        StringAssert.Contains(gamePage, "_targetCharacter.MysteryPreviewImageUrl,");
        StringAssert.Contains(gamePage, "_targetCharacter.MatchPreviewImageUrl,");
        Assert.AreEqual(
            3,
            gamePage.Split("\"schink_placeholder.png\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("schink_character_lineup.png", gamePage, StringComparison.Ordinal);
        StringAssert.Contains(gamePage, "await RevealCharacterAsync()");
        StringAssert.Contains(gamePage, "_revealLayer.IsVisible = false;");
        StringAssert.Contains(gamePage, "_mysteryLayer.FadeToAsync(0");
        StringAssert.Contains(gamePage, "_revealLayer.FadeToAsync(1");
        Assert.DoesNotContain("_revealImage.FadeToAsync", gamePage, StringComparison.Ordinal);
        StringAssert.Contains(gamePage, "new RowDefinition(GridLength.Star)");
        Assert.IsFalse(gamePage.Contains("ScrollView", StringComparison.Ordinal));
        Assert.IsFalse(gamePage.Contains("CollectionView", StringComparison.Ordinal));
        StringAssert.Contains(models, "string? MysteryImageUrl,");
        StringAssert.Contains(program, "MysteryImageUrl: string.IsNullOrWhiteSpace(character.MysteryImagePath)");
        StringAssert.Contains(configPage, "karakter_raai_logo_cropped.png");
        StringAssert.Contains(configPage, "WidthRequest = 62,");
        StringAssert.Contains(configPage, "HeightRequest = 64,");
        StringAssert.Contains(configPage, "Children = { card, character }");
        StringAssert.Contains(configPage, "var contentScroll = new ScrollView");
        StringAssert.Contains(configPage, "VerticalScrollBarVisibility = ScrollBarVisibility.Never");
        StringAssert.Contains(configPage, "AutomationId = \"karakter-raai-config-scroll\"");
        StringAssert.Contains(configPage, "contentScroll,");
        StringAssert.Contains(configPage, "private const double CompactLayoutHeight = 700;");
        StringAssert.Contains(configPage, "Height > 0 && Height < CompactLayoutHeight");
        StringAssert.Contains(configPage, "new Thickness(0, 80, 0, 8)");
        StringAssert.Contains(configPage, "new Thickness(0, 48, 0, 6)");
        StringAssert.Contains(configPage, "Margin = new Thickness(14, 44, 0, 0)");
        Assert.DoesNotContain("SPEEL NOU", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_playButton", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectDifficulty(_options[0]);", configPage, StringComparison.Ordinal);
        StringAssert.Contains(configPage, "protected override void OnAppearing()");
        StringAssert.Contains(configPage, "ClearDifficultySelection();");
        StringAssert.Contains(configPage, "Spacing = -3,");
        StringAssert.Contains(configPage, "LineHeight = 0.9");
        StringAssert.Contains(configPage, "AutomationId = \"karakter-raai-close\"");
        StringAssert.Contains(configPage, "Glyph = \"\\uf00d\"");
        StringAssert.Contains(configPage, "FontFamily = \"FontAwesomeSolid\"");
        StringAssert.Contains(configPage, "Color = CloseColor");
        StringAssert.Contains(configPage, "HorizontalOptions = LayoutOptions.Center");
        StringAssert.Contains(configPage, "VerticalOptions = LayoutOptions.Center");
        Assert.DoesNotContain("TranslationY = -1", configPage, StringComparison.Ordinal);
        StringAssert.Contains(configPage, "private async Task NavigateBackAsync()");
        Assert.AreEqual(
            2,
            configPage.Split("await NavigateBackAsync();", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(configPage, "tap.Tapped += async (_, _) => await StartDifficultyAsync(option);");
        StringAssert.Contains(configPage, "SelectDifficulty(option);");
        StringAssert.Contains(configPage, "[\"rounds\"] = option.TotalRounds");
        StringAssert.Contains(difficultyCatalog, "BEGINNER");
        StringAssert.Contains(difficultyCatalog, "KENNER");
        StringAssert.Contains(difficultyCatalog, "MEESTER");
        StringAssert.Contains(difficultyCatalog, "10, \"karakter_raai_beginner.png\"");
        StringAssert.Contains(difficultyCatalog, "20, \"karakter_raai_kenner.png\"");
        StringAssert.Contains(difficultyCatalog, "30, \"karakter_raai_meester.png\"");
        StringAssert.Contains(luisterPage, "\"Karakter Raai\",");
        StringAssert.Contains(luisterPage, "GoToAsync(nameof(KarakterRaaiConfigPage), animate: true)");
        StringAssert.Contains(mobileTopBar, "\"Karakter Raai\",");
        StringAssert.Contains(mobileTopBar, "GoToAsync(nameof(KarakterRaaiConfigPage), animate: true)");
        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(KarakterRaaiGamePage), typeof(KarakterRaaiGamePage));");
        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(KarakterRaaiConfigPage), typeof(KarakterRaaiConfigPage));");
        StringAssert.Contains(mauiProgram, "builder.Services.AddTransient<KarakterRaaiConfigPage>();");
        StringAssert.Contains(mauiProgram, "builder.Services.AddTransient<KarakterRaaiGamePage>();");
    }

    [TestMethod]
    public void MobileGuessGameAutoAdvancesAfterAnsweredRounds()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterRaaiGamePage.cs"));

        StringAssert.Contains(gamePage, "TimeSpan.FromSeconds(3)");
        StringAssert.Contains(gamePage, "ScheduleAutoAdvance();");
        StringAssert.Contains(gamePage, "await Task.Delay(AutoAdvanceDelay, cancellation.Token);");
        StringAssert.Contains(gamePage, "_game?.PrepareNextRound()");
        StringAssert.Contains(gamePage, "PreloadNextRoundImages();");
        StringAssert.Contains(gamePage, "await _nextRoundPreloadTask.WaitAsync(cancellation.Token);");
        StringAssert.Contains(gamePage, "NextRoundPreloadTimeout = TimeSpan.FromSeconds(8)");
        StringAssert.Contains(gamePage, ".WaitAsync(NextRoundPreloadTimeout, cancellation.Token)");
        StringAssert.Contains(gamePage, "catch (TimeoutException)");
        StringAssert.Contains(gamePage, "maxImages: 10");
        Assert.IsFalse(gamePage.Contains("Volgende Karakter", StringComparison.Ordinal));
    }

    private static string GetRepoPath(params string[] segments)
    {
        var path = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(path))
        {
            var candidate = Path.Combine([path, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            path = Directory.GetParent(path)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(segments)}");
    }
}
