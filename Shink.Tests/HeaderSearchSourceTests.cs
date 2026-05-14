using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class HeaderSearchSourceTests
{
    [TestMethod]
    public void HeaderSearch_RewiresFormsWhenEnhancedNavigationLeavesOnlyStaleWiredMarker()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Layout", "MainLayout.razor.js"));

        StringAssert.Contains(script, "if (!(searchForm instanceof HTMLFormElement))");
        StringAssert.Contains(script, "if (searchForm.dataset.searchWired === \"true\" && headerSearchState.has(searchForm))");
        StringAssert.Contains(script, "headerSearchState.set(searchForm, { setSearchState });");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
