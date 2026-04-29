using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class HomePerfectPlanNightModeCssTests
{
    [TestMethod]
    public void HomePerfectPlanCss_DefinesReadableNightModeOverrides()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "body.schink-night-mode .perfect-plan-section");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .perfect-plan-card");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .perfect-plan-card h3");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .perfect-plan-list li");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .perfect-plan-option");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .perfect-plan-select");
        StringAssert.Contains(css, "--perfect-plan-night-ink: #f7f1e7");
        StringAssert.Contains(css, "--perfect-plan-night-muted: #c6d0ca");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
