using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class ResourcesNightModeCssTests
{
    [TestMethod]
    public void ResourcesCss_DefinesReadableNightModePanelsAndCards()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "body.schink-night-mode .resources-page-shell");
        StringAssert.Contains(css, "body.schink-night-mode .resources-panel");
        StringAssert.Contains(css, "body.schink-night-mode .resources-card");
        StringAssert.Contains(css, "body.schink-night-mode .resources-card-body h3");
        StringAssert.Contains(css, "--resources-ink: #f7f1e7");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
