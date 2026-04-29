using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class MeerOorOnsNightModeCssTests
{
    [TestMethod]
    public void MeerOorOnsCss_DefinesReadableNightModeOverrides()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "body.schink-night-mode .about-page");
        StringAssert.Contains(css, "body.schink-night-mode .about-card");
        StringAssert.Contains(css, "body.schink-night-mode .about-block");
        StringAssert.Contains(css, "body.schink-night-mode .review-card");
        StringAssert.Contains(css, "body.schink-night-mode .site-main .about-card p");
        StringAssert.Contains(css, "body.schink-night-mode .site-main .about-block h2");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .about-card h2");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .review-author");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .review-card .review-author");
        StringAssert.Contains(css, "--about-ink: #f7f1e7");
        StringAssert.Contains(css, "--about-muted: #c6d0ca");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
