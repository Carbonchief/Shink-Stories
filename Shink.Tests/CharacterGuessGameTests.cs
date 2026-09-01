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
        var game = new CharacterGuessGame(CharacterKeys, totalRounds: 20, desiredChoiceCount: 4, new Random(17));
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
