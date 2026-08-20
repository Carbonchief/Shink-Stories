using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Games;

namespace Shink.Tests;

[TestClass]
public class CharacterMatchGameTests
{
    [TestMethod]
    public void MismatchedCardsStayFaceUpUntilTheTurnCompletes()
    {
        var firstA = Guid.NewGuid();
        var secondA = Guid.NewGuid();
        var firstB = Guid.NewGuid();
        var secondB = Guid.NewGuid();
        var game = CreateGame(firstA, secondA, firstB, secondB);

        var firstTurn = game.Reveal(firstA);
        var mismatch = game.Reveal(firstB);

        Assert.AreEqual(CharacterMatchOutcome.FirstCard, firstTurn.Outcome);
        Assert.AreEqual(CharacterMatchOutcome.Mismatch, mismatch.Outcome);
        Assert.AreEqual(1, game.AttemptCount);
        Assert.IsTrue(game.IsResolving);
        Assert.IsTrue(game.IsFaceUp(firstA));
        Assert.IsTrue(game.IsFaceUp(firstB));
        Assert.AreEqual(CharacterMatchOutcome.Ignored, game.Reveal(secondA).Outcome);

        game.CompleteTurn();

        Assert.IsFalse(game.IsResolving);
        Assert.IsFalse(game.IsFaceUp(firstA));
        Assert.IsFalse(game.IsFaceUp(firstB));
    }

    [TestMethod]
    public void MatchingEveryPairCompletesTheGameAndMatchedCardsCannotFlipAgain()
    {
        var firstA = Guid.NewGuid();
        var secondA = Guid.NewGuid();
        var firstB = Guid.NewGuid();
        var secondB = Guid.NewGuid();
        var game = CreateGame(firstA, secondA, firstB, secondB);

        game.Reveal(firstA);
        var firstMatch = game.Reveal(secondA);
        Assert.AreEqual(CharacterMatchOutcome.Match, firstMatch.Outcome);
        Assert.IsFalse(firstMatch.IsComplete);
        Assert.IsTrue(game.IsMatched(firstA));
        Assert.IsTrue(game.IsMatched(secondA));
        game.CompleteTurn();

        Assert.AreEqual(CharacterMatchOutcome.Ignored, game.Reveal(firstA).Outcome);
        game.Reveal(firstB);
        var finalMatch = game.Reveal(secondB);

        Assert.AreEqual(CharacterMatchOutcome.Match, finalMatch.Outcome);
        Assert.IsTrue(finalMatch.IsComplete);
        Assert.AreEqual(2, game.AttemptCount);
        Assert.AreEqual(2, game.MatchedPairCount);
        Assert.IsTrue(game.IsPerfectScore);
    }

    [TestMethod]
    public void ACompletedGameAfterAnyMismatchIsNotAPerfectScore()
    {
        var firstA = Guid.NewGuid();
        var secondA = Guid.NewGuid();
        var firstB = Guid.NewGuid();
        var secondB = Guid.NewGuid();
        var game = CreateGame(firstA, secondA, firstB, secondB);

        game.Reveal(firstA);
        game.Reveal(firstB);
        game.CompleteTurn();
        game.Reveal(firstA);
        game.Reveal(secondA);
        game.CompleteTurn();
        game.Reveal(firstB);
        var finalMatch = game.Reveal(secondB);

        Assert.IsTrue(finalMatch.IsComplete);
        Assert.AreEqual(3, game.AttemptCount);
        Assert.IsFalse(game.IsPerfectScore);
    }

    [TestMethod]
    public void TappingTheSameCardTwiceDoesNotCountAsAnAttempt()
    {
        var firstA = Guid.NewGuid();
        var secondA = Guid.NewGuid();
        var firstB = Guid.NewGuid();
        var secondB = Guid.NewGuid();
        var game = CreateGame(firstA, secondA, firstB, secondB);

        game.Reveal(firstA);
        var duplicateTap = game.Reveal(firstA);

        Assert.AreEqual(CharacterMatchOutcome.Ignored, duplicateTap.Outcome);
        Assert.AreEqual(0, game.AttemptCount);
        Assert.IsFalse(game.IsResolving);
    }

    [TestMethod]
    public void MobileGameIsRegisteredAndAvailableFromBothMenuImplementations()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterPareGamePage.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));
        var mobileMenuSheet = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileMenuSheet.cs"));
        var appShell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(gamePage, "public sealed class KarakterPareGamePage : ContentPage");
        StringAssert.Contains(gamePage, "private readonly Grid _board;");
        StringAssert.Contains(gamePage, "private static Grid BuildBoard()");
        StringAssert.Contains(gamePage, "Grid.SetColumn(tileView, index % difficulty.Columns);");
        StringAssert.Contains(gamePage, "Grid.SetRow(tileView, index / difficulty.Columns);");
        StringAssert.Contains(gamePage, "ConfigureBoardLayout(difficulty);");
        Assert.IsFalse(gamePage.Contains("CollectionView", StringComparison.Ordinal));
        Assert.IsFalse(gamePage.Contains("ScrollView", StringComparison.Ordinal));
        StringAssert.Contains(gamePage, "turn.Outcome == CharacterMatchOutcome.Match");
        StringAssert.Contains(gamePage, "await Task.Delay(780);");
        StringAssert.Contains(gamePage, "AnimateFlipAsync(firstTile, firstTileView, faceUp: false)");
        StringAssert.Contains(gamePage, "AnimateMatchedPairAsync(firstTile, firstTileView, tile, tileView)");
        StringAssert.Contains(gamePage, "_newGameButton.ImageSource = \"replay_icon.svg\"");
        StringAssert.Contains(gamePage, "StartNewGameAsync(animateBoard: true)");
        StringAssert.Contains(gamePage, "AnimateBoardOutAsync()");
        StringAssert.Contains(gamePage, "AnimateBoardBuildAsync()");
        StringAssert.Contains(gamePage, "AnimateTileIntoBoardAsync");
        StringAssert.Contains(gamePage, "_apiClient.BuildCachedImageSource(GetMatchImageUrl(character)!");
        StringAssert.Contains(gamePage, "Text = \"KARAKTER-PARE\"");
        StringAssert.Contains(luisterPage, "\"Karakter-pare\",");
        StringAssert.Contains(luisterPage, "GoToAsync(nameof(KarakterPareGamePage), animate: true)");
        StringAssert.Contains(mobileTopBar, "\"Karakter-pare\",");
        StringAssert.Contains(mobileTopBar, "GoToAsync(nameof(KarakterPareGamePage), animate: true)");
        StringAssert.Contains(mobileMenuSheet, "Content = new ScrollView");
        StringAssert.Contains(mobileMenuSheet, "VerticalScrollBarVisibility = ScrollBarVisibility.Never");
        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(KarakterPareGamePage), typeof(KarakterPareGamePage));");
        StringAssert.Contains(mauiProgram, "builder.Services.AddTransient<KarakterPareGamePage>();");
        Assert.IsTrue(File.Exists(GetRepoPath(
            "Shink.Mobile",
            "Resources",
            "Images",
            "replay_icon.svg")));
    }

    [TestMethod]
    public void MatchGameSetupOffersTheRequestedDifficultyGridsAndAnimatesTheInitialDeal()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterPareGamePage.cs"));

        StringAssert.Contains(gamePage, "new(MatchDifficultyLevel.Easy, \"Maklik\", 3, 4)");
        StringAssert.Contains(gamePage, "new(MatchDifficultyLevel.Medium, \"Gemiddeld\", 4, 4)");
        StringAssert.Contains(gamePage, "new(MatchDifficultyLevel.Hard, \"Moeilik\", 4, 5)");
        StringAssert.Contains(gamePage, "AutomationId = \"character-match-setup\"");
        StringAssert.Contains(gamePage, "Text = \"KIES JOU UITDAGING\"");
        StringAssert.Contains(gamePage, "Text = $\"{difficulty.Columns} × {difficulty.Rows} rooster · {difficulty.PairCount} pare\"");
        StringAssert.Contains(gamePage, "await StartNewGameAsync(animateBoard: true);");
        Assert.IsFalse(gamePage.Contains("StartNewGameAsync(animateBoard: false)", StringComparison.Ordinal));
        StringAssert.Contains(gamePage, "public int PairCount => Columns * Rows / 2;");
    }

    [TestMethod]
    public void MatchGameBoardUsesTheFullAvailableWidth()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterPareGamePage.cs"));

        StringAssert.Contains(gamePage, "_board.WidthRequest = -1;");
        StringAssert.Contains(gamePage, "_board.HorizontalOptions = LayoutOptions.Fill;");
        Assert.IsFalse(gamePage.Contains("Math.Min(760, Math.Max(320, availableWidth - 48))", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MatchGameUsesRegularNonMysteryCharacterImagesWithoutUnlockRequirement()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterPareGamePage.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));

        StringAssert.Contains(gamePage, ".Where(IsUsableMatchCharacter)");
        StringAssert.Contains(gamePage, "character.MatchImageUrl");
        StringAssert.Contains(gamePage, "GetMatchImageUrl(character)");
        StringAssert.Contains(gamePage, "path.Contains(\"mystery\", StringComparison.OrdinalIgnoreCase)");
        Assert.IsFalse(gamePage.Contains("character.IsUnlocked &&", StringComparison.Ordinal));
        Assert.IsFalse(gamePage.Contains("character.MysteryImageUrl", StringComparison.Ordinal));
        Assert.IsFalse(gamePage.Contains("CharacterMysteryImageResolver", StringComparison.Ordinal));
        StringAssert.Contains(program, "MatchImageUrl: ResolveMobileCharacterImageUrl(httpContext, character.ImagePath, character.UpdatedAt)");
        StringAssert.Contains(models, "string? MatchImageUrl,");
    }

    private static CharacterMatchGame CreateGame(
        Guid firstA,
        Guid secondA,
        Guid firstB,
        Guid secondB) =>
        new(
        [
            new CharacterMatchCard(firstA, "a"),
            new CharacterMatchCard(secondA, "a"),
            new CharacterMatchCard(firstB, "b"),
            new CharacterMatchCard(secondB, "b")
        ]);

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
