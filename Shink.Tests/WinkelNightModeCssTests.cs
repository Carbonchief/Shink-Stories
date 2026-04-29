using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class WinkelNightModeCssTests
{
    [TestMethod]
    public void WinkelCss_DefinesReadableNightModeOverrides()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "body.schink-night-mode .winkel-page");
        StringAssert.Contains(css, "body.schink-night-mode .winkel-product");
        StringAssert.Contains(css, "body.schink-night-mode .winkel-order-section");
        StringAssert.Contains(css, "body.schink-night-mode .winkel-field input");
        StringAssert.Contains(css, "body.schink-night-mode .winkel-product-quantity input");
        StringAssert.Contains(css, "body.schink-night-mode .site-main .winkel-product-description");
        StringAssert.Contains(css, "body.schink-night-mode .site-main .winkel-product-price");
        StringAssert.Contains(css, "--winkel-ink: #f7f1e7");
        StringAssert.Contains(css, "--winkel-muted: #c6d0ca");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
