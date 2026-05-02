using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class OpsiesNightModeCssTests
{
    [TestMethod]
    public void OpsiesCss_DefinesReadableNightModeOverrides()
    {
        var appCss = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));
        var opsiesCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Opsies.razor.css"));

        StringAssert.Contains(appCss, "body.schink-night-mode .site-shell.opsies-route.store-route");
        StringAssert.Contains(opsiesCss, "body.schink-night-mode .opsies-page");
        StringAssert.Contains(opsiesCss, "body.schink-night-mode .opsies-hero-point");
        StringAssert.Contains(opsiesCss, "body.schink-night-mode .opsies-hero-copy h1");
        StringAssert.Contains(opsiesCss, "--opsies-ink: #f7f1e7");
        StringAssert.Contains(opsiesCss, "--opsies-muted: #c6d0ca");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
