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
        StringAssert.Contains(css, "overflow-wrap: anywhere");
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
