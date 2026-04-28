using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class BlogNightModeCssTests
{
    [TestMethod]
    public void BlogListCss_DefinesReadableNightModePanels()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "body.schink-night-mode .blog-page");
        StringAssert.Contains(css, "body.schink-night-mode .blog-hero");
        StringAssert.Contains(css, "body.schink-night-mode .blog-card");
        StringAssert.Contains(css, "--blog-ink: #f7f1e7");
    }

    [TestMethod]
    public void BlogPostCss_DefinesReadableNightModeContentBubble()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "body.schink-night-mode .blog-post-content-shell");
        StringAssert.Contains(css, "body.schink-night-mode .blog-post-body");
        StringAssert.Contains(css, "body.schink-night-mode .blog-post-hero h1");
        StringAssert.Contains(css, "color: #f7f1e7");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
