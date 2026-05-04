using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminBlogPanelSourceTests
{
    [TestMethod]
    public void BlogPreviewKeepsListMarkersInsideThePreviewCard()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminBlogPanel.razor.css"));

        StringAssert.Contains(css, ".blog-admin-preview-body :global(ul)");
        StringAssert.Contains(css, ".blog-admin-preview-body :global(ol)");
        StringAssert.Contains(css, "list-style-position: inside;");
        StringAssert.Contains(css, ".blog-admin-preview-body :global(li)");
        StringAssert.Contains(css, "padding-inline-start: 0.3rem;");
        StringAssert.Contains(css, "overflow-wrap: anywhere;");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            Path.GetDirectoryName(GetSourceFilePath())!,
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
