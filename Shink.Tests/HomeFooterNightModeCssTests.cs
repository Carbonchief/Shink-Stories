using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class HomeFooterNightModeCssTests
{
    [TestMethod]
    public void HomeFooterCss_BlendsNightModeMainIntoFooter()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "body.schink-night-mode .site-shell.home-route .contact-section");
        StringAssert.Contains(css, "--home-footer-night-bg: #222222");
        StringAssert.Contains(css, "linear-gradient(180deg, transparent 0%, transparent calc(100% - 2.5rem), var(--home-footer-night-bg) calc(100% - 2.5rem), var(--home-footer-night-bg) 100%)");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
