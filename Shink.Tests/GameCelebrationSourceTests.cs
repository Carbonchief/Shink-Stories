using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class GameCelebrationSourceTests
{
    [TestMethod]
    public void SharedCelebrationExplodesConfettiAndAnimatesTheMessageCard()
    {
        var celebration = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "GameCelebrationOverlay.cs"));

        StringAssert.Contains(celebration, "internal sealed class GameCelebrationOverlay : Grid");
        StringAssert.Contains(celebration, "new List<Border>(ConfettiMotions.Length)");
        StringAssert.Contains(celebration, "AutomationId = \"perfect-score-celebration\"");
        StringAssert.Contains(celebration, "particle.TranslateToAsync(");
        StringAssert.Contains(celebration, "particle.RotateToAsync(");
        StringAssert.Contains(celebration, "particle.FadeToAsync(");
        StringAssert.Contains(celebration, "_messageCard.ScaleToAsync(");
        StringAssert.Contains(celebration, "_messageCard.RotateToAsync(");
        StringAssert.Contains(celebration, "Source = \"oortjies_01.png\"");
        StringAssert.Contains(celebration, "UIKit.UIAccessibility.IsReduceMotionEnabled");
        Assert.IsFalse(celebration.Contains("ScrollView", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PairGameCelebratesEveryCompletionAndKeepsPerfectScoreCopySpecial()
    {
        var matchGame = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "KarakterPareGamePage.cs"));
        var guessGame = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "KarakterRaaiGamePage.cs"));

        StringAssert.Contains(matchGame, "turn.IsComplete && game.IsPerfectScore");
        StringAssert.Contains(matchGame, "\"PERFEKTE PARE!\"");
        StringAssert.Contains(matchGame, "\"BAIE GELUK!\"");
        StringAssert.Contains(matchGame, "await _celebrationOverlay.CelebrateAsync(");
        StringAssert.Contains(matchGame, "await AnimateCompletedBoardRevealAsync();");
        StringAssert.Contains(matchGame, "tile.PrepareForCompletionReveal();");
        StringAssert.Contains(matchGame, "await AnimateFlipAsync(tile, tileView, faceUp: true);");
        StringAssert.Contains(matchGame, "BuildCompletionMessage(game)");
        StringAssert.Contains(matchGame, "PerfectScoreMessages");
        StringAssert.Contains(matchGame, "[\"is_perfect_score\"] = isPerfectScore");

        StringAssert.Contains(guessGame, "if (game.HasPerfectScore)");
        StringAssert.Contains(guessGame, "game.HasPerfectScore");
        StringAssert.Contains(guessGame, "\"KARAKTER-KAMPIOEN!\"");
        StringAssert.Contains(guessGame, "_celebrationOverlay.CelebrateAsync(");
        StringAssert.Contains(guessGame, "PerfectScoreMessages");
        StringAssert.Contains(guessGame, "[\"is_perfect_score\"] = game.HasPerfectScore");
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
