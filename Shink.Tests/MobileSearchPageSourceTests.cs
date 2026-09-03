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
        StringAssert.Contains(source, "Source = \"soek_stories_title.png\"");
        Assert.IsTrue(File.Exists(GetRepoPath("Shink.Mobile", "Resources", "Images", "soek_stories_title.png")));
        Assert.DoesNotContain("Text = \"Storie soek...\"", source, StringComparison.Ordinal);
        StringAssert.Contains(source, "Text = \"Tik die naam van die storie wat jy wil luister\"");
        StringAssert.Contains(source, "HeightRequest = 28,");
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
    public void SearchCompactsAfterTypingAndKeepsTheLiveEntryStable()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SearchPage.cs"));

        StringAssert.Contains(source, "_searchEntry.TextChanged += OnSearchTextChanged;");
        StringAssert.Contains(source, "var shouldCompact = !string.IsNullOrWhiteSpace(_searchEntry.Text);");
        StringAssert.Contains(source, "_searchHero.IsVisible = !shouldCompact;");
        StringAssert.Contains(source, "Dispatcher.Dispatch(UpdateStickySearchFieldPosition);");
        StringAssert.Contains(source, "Content = _searchField");
        StringAssert.Contains(source, "MobileResponsiveLayout.ApplyCenteredContent(_searchFieldOverlay, width, 720);");
        Assert.DoesNotContain("_searchEntry =", source[source.IndexOf("private void UpdateSearchPresentation", StringComparison.Ordinal)..]);
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
    public void SearchResultCardsHideTheLockedBadgeAndWrapAtWordBoundaries()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SearchPage.cs"));

        Assert.DoesNotContain("Sien opsies", source, StringComparison.Ordinal);
        StringAssert.Contains(source, "if (!story.IsLocked)");
        StringAssert.Contains(source, "Text = \"Luister\"");
        Assert.AreEqual(2, source.Split("LineBreakMode = LineBreakMode.WordWrap", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("LineBreakMode = LineBreakMode.TailTruncation", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void SearchEntryRemainsStableAndThePageHasNoPullToRefresh()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SearchPage.cs"));

        StringAssert.Contains(source, "Header = _searchHeader,");
        StringAssert.Contains(source, "Children = { _resultsView }");
        Assert.DoesNotContain("RefreshView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("forceRefresh", source, StringComparison.Ordinal);
        StringAssert.Contains(source, "_searchFieldPlaceholder");
        StringAssert.Contains(source, "_resultsView.Scrolled += OnResultsScrolled;");
        StringAssert.Contains(source, "_searchFieldOverlay.TranslationY = fieldTop - SearchFieldTopInset;");
        Assert.DoesNotContain("RestoreSearchFocusAfterResultsUpdate", source);
        Assert.DoesNotContain("_searchEntry.CursorPosition =", source);
    }

    [TestMethod]
    public void SearchFieldWaitsForTheMeasuredHeroBeforeItsFirstReveal()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SearchPage.cs"));

        StringAssert.Contains(source, "_searchHero.SizeChanged += (_, _) => ScheduleStickySearchFieldPositionUpdate();");
        StringAssert.Contains(source, "Loaded += (_, _) => ScheduleStickySearchFieldPositionUpdate();");
        StringAssert.Contains(source, "if (_searchHero.IsVisible && _searchHero.Height <= 0)");
        StringAssert.Contains(source, "? _searchHero.Height");
        StringAssert.Contains(source, "_searchFieldOverlay.Opacity = 0;");
        StringAssert.Contains(source, "_searchFieldOverlay.Opacity = 1;");
        Assert.DoesNotContain("_searchHeader.Y + _searchFieldPlaceholder.Y", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void SearchEntryUsesPlatformNativeVerticalCentering()
    {
        var source = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SearchPage.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(source, "AutomationId = \"story-search-input\"");
        StringAssert.Contains(source, "VerticalTextAlignment = TextAlignment.Center");
        StringAssert.Contains(mauiProgram, "handler.VirtualView is Entry { AutomationId: \"story-search-input\" }");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.VerticalAlignment = UIKit.UIControlContentVerticalAlignment.Center;");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.Gravity = Android.Views.GravityFlags.CenterVertical |");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.SetIncludeFontPadding(false);");
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
