using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileSearchPageSourceTests
{
    [TestMethod]
    public void SearchIconsNavigateToTheDedicatedNativeSearchPage()
    {
        var shell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var bottomBar = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "MobileBottomBar.cs"));
        var luister = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var characters = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "KaraktersPage.cs"));

        StringAssert.Contains(shell, "Routing.RegisterRoute(nameof(SearchPage), typeof(SearchPage));");
        StringAssert.Contains(bottomBar, "OpenRouteAsync(nameof(SearchPage))");
        StringAssert.Contains(luister, "BuildStoriesTopBar(");
        Assert.DoesNotContain("searchAction: OpenStoriesSearchAsync", luister, StringComparison.Ordinal);
        StringAssert.Contains(luister, "Shell.Current.GoToAsync(nameof(SearchPage), animate: false)");
        StringAssert.Contains(characters, "Shell.Current.GoToAsync(nameof(SearchPage), animate: false)");
    }

    [TestMethod]
    public void SearchInitialStateMatchesTheApprovedAfrikaansVisual()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SearchPage.cs"));

        StringAssert.Contains(source, "Source = \"schink_background.jpeg\"");
        StringAssert.Contains(source, "Source = \"knibbels_search.png\"");
        Assert.IsTrue(File.Exists(GetRepoPath("Shink.Mobile", "Resources", "Images", "knibbels_search.png")));
        StringAssert.Contains(source, "Text = \"Storie soek...\"");
        StringAssert.Contains(source, "Text = \"Tik die naam van die storie wat jy wil luister\"");
        StringAssert.Contains(source, "Placeholder = \"Soek stories\"");
        StringAssert.Contains(source, "MobileBottomBar.Build(this, \"search\", FocusSearchAsync)");
        StringAssert.Contains(source, "MobileTopBar.BuildStoriesTopBar(");
        Assert.DoesNotContain("searchAction: FocusSearchAsync", source, StringComparison.Ordinal);
        StringAssert.Contains(source, "var heroSpacer = new BoxView { HeightRequest = 200");
        Assert.DoesNotContain("_searchEntry.Focused +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCompactSearchHeaderAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HeightRequest = 98", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void SearchUsesWebRankingRulesAndSkipsRecycledRowAnimationsOnAndroid()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SearchPage.cs"));

        StringAssert.Contains(source, "SearchDebounceMilliseconds = 220");
        StringAssert.Contains(source, "queryTerms.All(term => normalizedContent.Contains(term");
        StringAssert.Contains(source, "score += 140;");
        StringAssert.Contains(source, "score += 70;");
        StringAssert.Contains(source, "CharUnicodeInfo.GetUnicodeCategory(character)");
        StringAssert.Contains(source, "Task.Delay(Math.Min(result.RevealIndex, 8) * 55)");
        StringAssert.Contains(source, "container.FadeToAsync(1, 260, Easing.CubicOut)");
        StringAssert.Contains(source, "container.TranslateToAsync(0, 0, 320, Easing.CubicOut)");
        StringAssert.Contains(source, "container.ScaleToAsync(1, 330, Easing.CubicOut)");
        StringAssert.Contains(source, "ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem");
        StringAssert.Contains(source, "if (IsAndroid)");
        StringAssert.Contains(source, "container.Opacity = 1;");
        StringAssert.Contains(source, "_ = AnimateResultContainerAsync(container, result);");
        StringAssert.Contains(source, "_visibleResults.ReplaceWith(matches)");
        StringAssert.Contains(source, "SetItem(index, replacement[index])");
        StringAssert.Contains(source, "RemoveAt(Count - 1)");
        StringAssert.Contains(source, "Add(replacement[index])");
        Assert.DoesNotContain("NotifyCollectionChangedAction.Reset", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void SearchEntryRemainsOutsideTheRefreshingResultsCollection()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SearchPage.cs"));

        Assert.DoesNotContain("Header = _searchHeader", source);
        StringAssert.Contains(source, "Children = { _searchHeader, _refreshView }");
        StringAssert.Contains(source, "Grid.SetRow(_refreshView, 1);");
        Assert.DoesNotContain("RestoreSearchFocusAfterResultsUpdate", source);
        Assert.DoesNotContain("_searchEntry.CursorPosition =", source);
        StringAssert.Contains(source, "The live search Entry is a stable sibling");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', segments)}.");
    }
}
