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
        StringAssert.Contains(gamePage, "private readonly Image _mysteryImage;");
        StringAssert.Contains(gamePage, "private readonly Image _revealImage;");
        StringAssert.Contains(gamePage, "private readonly ImageButton _newGameButton;");
        StringAssert.Contains(gamePage, "AutomationId = \"character-guess-retry\"");
        StringAssert.Contains(gamePage, "Glyph = \"\\uf2f1\"");
        StringAssert.Contains(gamePage, "FontFamily = \"FontAwesomeSolid\"");
        StringAssert.Contains(gamePage, "Text = \"???\"");
        StringAssert.Contains(gamePage, "Text = \"Rondte 1\"");
        StringAssert.Contains(gamePage, "BuildScoreLabel(\"0/10\")");
        StringAssert.Contains(gamePage, "Grid.SetColumn(choice, index);");
        StringAssert.Contains(gamePage, "Grid.SetRow(choice, 0);");
        StringAssert.Contains(gamePage, "CharacterMysteryImageResolver.Resolve(");
        StringAssert.Contains(gamePage, "await RevealCharacterAsync()");
        StringAssert.Contains(gamePage, "new RowDefinition(GridLength.Star)");
        Assert.IsFalse(gamePage.Contains("ScrollView", StringComparison.Ordinal));
        Assert.IsFalse(gamePage.Contains("CollectionView", StringComparison.Ordinal));
        StringAssert.Contains(models, "string? MysteryImageUrl,");
        StringAssert.Contains(program, "MysteryImageUrl: string.IsNullOrWhiteSpace(character.MysteryImagePath)");
        StringAssert.Contains(configPage, "karakter_raai_logo_cropped.png");
        StringAssert.Contains(configPage, "WidthRequest = 62,");
        StringAssert.Contains(configPage, "HeightRequest = 64,");
        StringAssert.Contains(configPage, "Children = { card, character }");
        Assert.IsFalse(configPage.Contains("ScrollView", StringComparison.Ordinal));
        StringAssert.Contains(configPage, "Margin = new Thickness(14, 44, 0, 0)");
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
