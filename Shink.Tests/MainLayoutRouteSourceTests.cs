using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class MainLayoutRouteSourceTests
{
    [TestMethod]
    public void MainLayout_BlogRouteIncludesBlogPostSubpaths()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Layout", "MainLayout.razor"));

        StringAssert.Contains(markup, "relativePath.StartsWith(\"blog\", StringComparison.OrdinalIgnoreCase)");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
