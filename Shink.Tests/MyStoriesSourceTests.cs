using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class MyStoriesSourceTests
{
    [TestMethod]
    public void MyStoriesPageShowsProfileStatsBlockForSignedInUsers()
    {
        var page = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "MyStories.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "MyStories.razor.css"));

        StringAssert.Contains(page, "@inject ISubscriptionLedgerService SubscriptionLedgerService");
        StringAssert.Contains(page, "my-stories-stats-block");
        StringAssert.Contains(page, "my-stories-profile-avatar");
        StringAssert.Contains(page, "Stories geluister");
        StringAssert.Contains(page, "Klaar geluister");
        StringAssert.Contains(page, "Gunstelinge");
        StringAssert.Contains(page, "Totale luistertyd");
        StringAssert.Contains(page, "SubscriptionLedgerService.GetSubscriberProfileAsync(email)");
        StringAssert.Contains(page, "BuildUserStats(progressTask.Result, FavoriteStories.Count)");
        StringAssert.Contains(page, "my-stories-carousel stories-carousel");
        StringAssert.Contains(page, "initializeHomeStoryLoading");
        StringAssert.Contains(css, ".my-stories-stats-block");
        StringAssert.Contains(css, "grid-template-columns: minmax(250px, 0.76fr) minmax(0, 1.24fr);");
        StringAssert.Contains(css, ".my-stories-stats-grid");
        StringAssert.Contains(css, "grid-auto-columns: calc((100% - (var(--my-stories-carousel-gap) * 5)) / 6);");
        StringAssert.Contains(css, ".my-stories-carousel.is-dragging");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
