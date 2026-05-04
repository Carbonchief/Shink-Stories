using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class BlogPostCssTests
{
    [TestMethod]
    public void BlogPostDetailCss_ContainsRenderedContentInsideShell()
    {
        var cssPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(GetSourceFilePath())!,
            "..",
            "Shink",
            "Components",
            "Pages",
            "BlogPost.razor.css"));
        var css = File.ReadAllText(cssPath);

        StringAssert.Contains(css, ".blog-post-content-shell");
        StringAssert.Contains(css, ".blog-post-content");
        StringAssert.Contains(css, ".blog-post-body");
        StringAssert.Contains(css, "min-width: 0");
        StringAssert.Contains(css, "overflow-wrap: break-word");
        StringAssert.Contains(css, "word-break: normal");
        Assert.IsFalse(css.Contains(".blog-post-body {\r\n    color: #33443a;\r\n    font-size: 1.05rem;\r\n    line-height: 1.9;\r\n    min-width: 0;\r\n    max-width: 100%;\r\n    overflow-wrap: anywhere;", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BlogPostDetailCss_OnlyUsesAggressiveWrappingForOverflowProneContent()
    {
        var cssPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(GetSourceFilePath())!,
            "..",
            "Shink",
            "Components",
            "Pages",
            "BlogPost.razor.css"));
        var css = File.ReadAllText(cssPath);

        StringAssert.Contains(css, ".blog-post-body ::deep table,");
        StringAssert.Contains(css, ".blog-post-body ::deep pre,");
        StringAssert.Contains(css, ".blog-post-body ::deep code");
        StringAssert.Contains(css, "overflow-wrap: anywhere");
    }

    [TestMethod]
    public void BlogPostDetailCss_UsesBlogAndResourcesPanelBackgroundsInLightMode()
    {
        var cssPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(GetSourceFilePath())!,
            "..",
            "Shink",
            "Components",
            "Pages",
            "BlogPost.razor.css"));
        var css = File.ReadAllText(cssPath);

        StringAssert.Contains(css, "--blog-post-ink: #233428");
        StringAssert.Contains(css, ".blog-post-hero,");
        StringAssert.Contains(css, ".blog-post-featured,");
        StringAssert.Contains(css, ".blog-post-content-shell,");
        StringAssert.Contains(css, ".blog-post-related-card,");
        StringAssert.Contains(css, "radial-gradient(circle at top right, rgba(255, 214, 179, 0.34), transparent 34%)");
        StringAssert.Contains(css, "linear-gradient(180deg, rgba(255, 252, 247, 0.98) 0%, rgba(255, 248, 237, 0.96) 100%)");
        StringAssert.Contains(css, "border: 1px solid var(--blog-post-line)");
        Assert.IsFalse(css.Contains("background: rgba(255, 255, 255, 0.86);", StringComparison.Ordinal));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
