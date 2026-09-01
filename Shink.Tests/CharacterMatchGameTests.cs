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
        var configPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterPareConfigPage.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mobileTopBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileTopBar.cs"));
        var mobileMenuSheet = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileMenuSheet.cs"));
        var appShell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(gamePage, "public sealed class KarakterPareGamePage : ContentPage, IQueryAttributable");
        StringAssert.Contains(gamePage, "ApplyQueryAttributes(IDictionary<string, object> query)");
        StringAssert.Contains(configPage, "public sealed class KarakterPareConfigPage : ContentPage");
        StringAssert.Contains(configPage, "karakter_pare_logo_cropped.png");
        StringAssert.Contains(configPage, "karakter_pare_beginner.png");
        StringAssert.Contains(configPage, "karakter_pare_kenner.png");
        StringAssert.Contains(configPage, "karakter_pare_meester.png");
        Assert.DoesNotContain("SPEEL NOU", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_playButton", configPage, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectDifficulty(Options[0]);", configPage, StringComparison.Ordinal);
        StringAssert.Contains(configPage, "protected override void OnAppearing()");
        StringAssert.Contains(configPage, "ClearDifficultySelection();");
        StringAssert.Contains(configPage, "Spacing = -3,");
        StringAssert.Contains(configPage, "LineHeight = 0.9");
        StringAssert.Contains(configPage, "AutomationId = \"karakter-pare-close\"");
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
        StringAssert.Contains(configPage, "[\"difficulty\"] = option.Level");
        StringAssert.Contains(configPage, "var contentScroll = new ScrollView");
        StringAssert.Contains(configPage, "VerticalScrollBarVisibility = ScrollBarVisibility.Never");
        StringAssert.Contains(configPage, "AutomationId = \"karakter-pare-config-scroll\"");
        StringAssert.Contains(configPage, "contentScroll,");
        StringAssert.Contains(configPage, "private const double CompactLayoutHeight = 700;");
        StringAssert.Contains(configPage, "Height > 0 && Height < CompactLayoutHeight");
        StringAssert.Contains(configPage, "new Thickness(0, 68, 0, 8)");
        StringAssert.Contains(configPage, "new Thickness(0, 40, 0, 6)");
        StringAssert.Contains(configPage, "Margin = new Thickness(14, 44, 0, 0)");
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
        StringAssert.Contains(gamePage, "AnimateMatchedTileToCenterAsync");
        StringAssert.Contains(gamePage, "private const int MatchedTileAnimationZIndex = 1_000;");
        StringAssert.Contains(gamePage, "private readonly AbsoluteLayout _matchAnimationOverlay;");
        StringAssert.Contains(gamePage, "_matchAnimationOverlay.Children.Add(tileView);");
        StringAssert.Contains(gamePage, "RestoreMatchedTileFromAnimationOverlay");
        StringAssert.Contains(gamePage, "ZIndex = MatchedTileAnimationZIndex");
        StringAssert.Contains(gamePage, "mergedTileView.ZIndex = MergedTileAnimationZIndex;");
        StringAssert.Contains(gamePage, "AbsoluteLayout.GetLayoutBounds(tileView)");
        StringAssert.Contains(gamePage, "private static async Task AnimateMergedTilePopAsync");
        StringAssert.Contains(gamePage, "coveredTileView!.Opacity = 0;");
        StringAssert.Contains(gamePage, "WiggleMatchedTileAsync");
        StringAssert.Contains(gamePage, "AnimateMatchedTileAwayAsync");
        StringAssert.Contains(gamePage, "private readonly ImageButton _newGameButton;");
        StringAssert.Contains(gamePage, "Glyph = \"\\uf2f1\"");
        StringAssert.Contains(gamePage, "FontFamily = \"FontAwesomeSolid\"");
        StringAssert.Contains(gamePage, "StartNewGameAsync(animateBoard: true)");
        StringAssert.Contains(gamePage, "AnimateBoardOutAsync()");
        StringAssert.Contains(gamePage, "AnimateBoardBuildAsync()");
        StringAssert.Contains(gamePage, "AnimateTileIntoBoardAsync");
        StringAssert.Contains(gamePage, "new ProgressiveImageRequest(");
        StringAssert.Contains(gamePage, "character.MatchPreviewImageUrl,");
        StringAssert.Contains(gamePage, "Text = \"KARAKTER-PARE\"");
        StringAssert.Contains(configPage, "private const string PoppinsFontFamily = \"Poppins\";");
        StringAssert.Contains(configPage, "private const string PoppinsBoldFontFamily = \"PoppinsBold\";");
        StringAssert.Contains(configPage, "FontFamily = PoppinsFontFamily");
        StringAssert.Contains(configPage, "FontFamily = PoppinsBoldFontFamily");
        StringAssert.Contains(luisterPage, "\"Karakter-pare\",");
        StringAssert.Contains(luisterPage, "GoToAsync(nameof(KarakterPareConfigPage), animate: true)");
        StringAssert.Contains(mobileTopBar, "\"Karakter-pare\",");
        StringAssert.Contains(mobileTopBar, "GoToAsync(nameof(KarakterPareConfigPage), animate: true)");
        StringAssert.Contains(mobileMenuSheet, "Content = new ScrollView");
        StringAssert.Contains(mobileMenuSheet, "VerticalScrollBarVisibility = ScrollBarVisibility.Never");
        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(KarakterPareGamePage), typeof(KarakterPareGamePage));");
        StringAssert.Contains(appShell, "Routing.RegisterRoute(nameof(KarakterPareConfigPage), typeof(KarakterPareConfigPage));");
        StringAssert.Contains(mauiProgram, "builder.Services.AddTransient<KarakterPareConfigPage>();");
        StringAssert.Contains(mauiProgram, "builder.Services.AddTransient<KarakterPareGamePage>();");
        StringAssert.Contains(mauiProgram, "fonts.AddFont(\"Poppins-Regular.ttf\", \"Poppins\");");
        StringAssert.Contains(mauiProgram, "fonts.AddFont(\"Poppins-SemiBold.ttf\", \"PoppinsSemiBold\");");
        StringAssert.Contains(mauiProgram, "fonts.AddFont(\"Poppins-Bold.ttf\", \"PoppinsBold\");");
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

        StringAssert.Contains(gamePage, "new(MatchDifficultyLevel.Easy, \"Beginner\", 3, 4)");
        StringAssert.Contains(gamePage, "new(MatchDifficultyLevel.Medium, \"Kenner\", 4, 4)");
        StringAssert.Contains(gamePage, "new(MatchDifficultyLevel.Hard, \"Meester\", 4, 6)");
        StringAssert.Contains(gamePage, "AutomationId = \"character-match-setup\"");
        StringAssert.Contains(gamePage, "Text = \"KIES JOU UITDAGING\"");
        StringAssert.Contains(gamePage, "Text = $\"{difficulty.Columns} × {difficulty.Rows} rooster · {difficulty.PairCount} pare\"");
        StringAssert.Contains(gamePage, "await StartNewGameAsync(animateBoard: true);");
        Assert.IsFalse(gamePage.Contains("StartNewGameAsync(animateBoard: false)", StringComparison.Ordinal));
        StringAssert.Contains(gamePage, "public int PairCount => Columns * Rows / 2;");
    }

    [TestMethod]
    public void MatchGameBoardUsesSquareCardsAndCentersWithinTheAvailableSpace()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterPareGamePage.cs"));

        StringAssert.Contains(gamePage, "private const double PhoneTileCornerRadius = 12;");
        StringAssert.Contains(gamePage, "private const double PhoneTileSpacing = 4;");
        StringAssert.Contains(gamePage, "private const double TabletFaceUpTileCornerRadius = 15;");
        StringAssert.Contains(gamePage, "private const double TabletTileCornerRadius = 22;");
        StringAssert.Contains(gamePage, "private const double TabletThreeColumnTileSpacing = 10;");
        StringAssert.Contains(gamePage, "private const double TabletFourColumnTileSpacing = 12;");
        StringAssert.Contains(gamePage, "private static bool IsTablet => DeviceInfo.Current.Idiom == DeviceIdiom.Tablet;");
        StringAssert.Contains(gamePage, "var faceUpCornerRadius = IsTablet ? TabletFaceUpTileCornerRadius : PhoneTileCornerRadius;");
        StringAssert.Contains(gamePage, "var tileCornerRadius = IsTablet ? TabletTileCornerRadius : PhoneTileCornerRadius;");
        StringAssert.Contains(gamePage, "if (!IsTablet)");
        StringAssert.Contains(gamePage, "return PhoneTileSpacing;");
        StringAssert.Contains(gamePage, "? TabletThreeColumnTileSpacing");
        StringAssert.Contains(gamePage, ": TabletFourColumnTileSpacing;");
        StringAssert.Contains(gamePage, "_boardHost.SizeChanged += (_, _) => ApplyBoardGeometry(_selectedDifficulty);");
        StringAssert.Contains(gamePage, "var widthLimitedTileSize =");
        StringAssert.Contains(gamePage, "var heightLimitedTileSize =");
        StringAssert.Contains(gamePage, "var tileSize = Math.Floor(Math.Min(widthLimitedTileSize, heightLimitedTileSize));");
        StringAssert.Contains(gamePage, "_board.WidthRequest = tileSize * difficulty.Columns + horizontalSpacing;");
        StringAssert.Contains(gamePage, "_board.HeightRequest = tileSize * difficulty.Rows + verticalSpacing;");
        StringAssert.Contains(gamePage, "_board.HorizontalOptions = LayoutOptions.Center;");
        StringAssert.Contains(gamePage, "_board.VerticalOptions = LayoutOptions.Center;");
        Assert.IsFalse(gamePage.Contains("MinimumHeightRequest = 64", StringComparison.Ordinal));
        Assert.IsFalse(gamePage.Contains("Math.Min(760, Math.Max(320, availableWidth - 48))", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MatchGameFaceUpCardsShowTheCharacterImageWithoutANameLabel()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterPareGamePage.cs"));

        StringAssert.Contains(gamePage, "Content = characterImage");
        Assert.DoesNotContain("var characterName = new Label", gamePage, StringComparison.Ordinal);
        Assert.DoesNotContain("static (CharacterMatchTile tile) => tile.DisplayName", gamePage, StringComparison.Ordinal);
    }

    [TestMethod]
    public void AndroidMatchGameDoesNotLeaveCardsAtTheDealAnimationScale()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterPareGamePage.cs"));

        StringAssert.Contains(
            gamePage,
            "private static bool IsAndroid => DeviceInfo.Current.Platform == DevicePlatform.Android;");
        StringAssert.Contains(gamePage, "tileView.Scale = IsAndroid ? 1 : 0.72;");
        StringAssert.Contains(gamePage, "tileView.Scale = IsAndroid ? 1 : 0.78;");
        StringAssert.Contains(gamePage, "tileView.TranslationY = IsAndroid ? 0 : 22;");
        StringAssert.Contains(gamePage, "tileView.TranslationY = IsAndroid ? 0 : 16;");
        StringAssert.Contains(
            gamePage,
            "if (IsAndroid)\n        {\n            await tileView.FadeToAsync(1, 180, Easing.CubicOut);\n            ResetTileTransform(tileView);");
    }

    [TestMethod]
    public void MatchedCardsMergeAtThePhoneCenterBeforePoppingAndDisappearing()
    {
        var gamePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KarakterPareGamePage.cs"));

        StringAssert.Contains(gamePage, "private readonly Grid _boardHost;");
        StringAssert.Contains(gamePage, "var pageCenterX = Width > 0 ? Width / 2");
        StringAssert.Contains(gamePage, "var pageCenterY = Height > 0 ? Height / 2");
        StringAssert.Contains(gamePage, "pageCenterY - _boardHost.Y");
        StringAssert.Contains(gamePage, "var pairSeparation = Math.Max(18, cardWidth * 0.64);");
        StringAssert.Contains(gamePage, "var tileCenterX = tileX + tileWidth / 2;");
        StringAssert.Contains(gamePage, "var tileCenterY = tileY + tileHeight / 2;");
        Assert.DoesNotContain(
            "tileX + tileWidth / 2 + tileView.TranslationX",
            gamePage,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "tileY + tileHeight / 2 + tileView.TranslationY",
            gamePage,
            StringComparison.Ordinal);

        var approachIndex = gamePage.IndexOf("await Task.WhenAll(animations);", StringComparison.Ordinal);
        var mergeIndex = gamePage.IndexOf("// Close the last gap", approachIndex, StringComparison.Ordinal);
        var becomeOneIndex = gamePage.IndexOf("coveredTileView!.Opacity = 0;", mergeIndex, StringComparison.Ordinal);
        var popIndex = gamePage.IndexOf("await AnimateMergedTilePopAsync", becomeOneIndex, StringComparison.Ordinal);
        var wiggleIndex = gamePage.IndexOf("await WiggleMatchedTileAsync", popIndex, StringComparison.Ordinal);
        var disappearIndex = gamePage.IndexOf("await AnimateMatchedTileAwayAsync", wiggleIndex, StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, approachIndex);
        Assert.IsGreaterThan(approachIndex, mergeIndex);
        Assert.IsGreaterThan(mergeIndex, becomeOneIndex);
        Assert.IsGreaterThan(becomeOneIndex, popIndex);
        Assert.IsGreaterThan(popIndex, wiggleIndex);
        Assert.IsGreaterThan(wiggleIndex, disappearIndex);
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
