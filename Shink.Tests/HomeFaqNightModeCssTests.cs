using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class HomeFaqNightModeCssTests
{
    [TestMethod]
    public void HomeFaqCss_DefinesReadableNightModeOverrides()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "body.schink-night-mode .home-faq");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .home-faq-heading h2");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .home-faq-kicker");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .home-faq-item");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .home-faq-item summary");
        StringAssert.Contains(css, "body.schink-night-mode .site-shell .site-main .home-faq-item p");
        StringAssert.Contains(css, "--home-faq-night-ink: #f7f1e7");
        StringAssert.Contains(css, "--home-faq-night-muted: #c6d0ca");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
