using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class HeaderSearchSourceTests
{
    [TestMethod]
    public void HeaderSearch_DoesNotDoubleWireFormsAfterEnhancedNavigationReloadsModule()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Layout", "MainLayout.razor.js"));

        StringAssert.Contains(script, "if (!(searchForm instanceof HTMLFormElement))");
        StringAssert.Contains(script, "HEADER_SEARCH_CONTROLLER_PROPERTY");
        StringAssert.Contains(script, "existingController.searchToggle === searchToggle");
        StringAssert.Contains(script, "existingController.searchInput === searchInput");
        StringAssert.Contains(script, "headerSearchState.set(searchForm, existingController);");
        StringAssert.Contains(script, "searchForm[HEADER_SEARCH_CONTROLLER_PROPERTY] = controller;");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
