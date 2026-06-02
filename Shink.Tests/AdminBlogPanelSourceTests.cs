using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminBlogPanelSourceTests
{
    [TestMethod]
    public void BlogListItemsHideSlugAndKeepDateOnOneLine()
    {
        var component = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminBlogPanel.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminBlogPanel.razor.css"));

        Assert.IsFalse(component.Contains("<span class=\"blog-admin-list-meta\">@post.Slug</span>", StringComparison.Ordinal));
        StringAssert.Contains(component, "<span class=\"blog-admin-list-date\">@FormatAdminDate(post.UpdatedAt)</span>");
        StringAssert.Contains(css, ".blog-admin-list-date {");
        StringAssert.Contains(css, "white-space: nowrap;");
        StringAssert.Contains(css, "flex: 0 0 auto;");
    }

    [TestMethod]
    public void BlogPreviewKeepsListMarkersInsideThePreviewCard()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminBlogPanel.razor.css"));

        StringAssert.Contains(css, ".blog-admin-preview {");
        StringAssert.Contains(css, "overflow: hidden;");
        StringAssert.Contains(css, ".blog-admin-preview-body :global(ul)");
        StringAssert.Contains(css, ".blog-admin-preview-body :global(ol)");
        StringAssert.Contains(css, "list-style-position: inside;");
        StringAssert.Contains(css, ".blog-admin-preview-body ::deep ul");
        StringAssert.Contains(css, ".blog-admin-preview-body ::deep ol");
        StringAssert.Contains(css, "padding-inline-start: 1.35rem;");
        StringAssert.Contains(css, "list-style-position: outside;");
        StringAssert.Contains(css, ".blog-admin-preview-body :global(li)");
        StringAssert.Contains(css, ".blog-admin-preview-body ::deep li");
        StringAssert.Contains(css, "padding-inline-start: 0.3rem;");
        StringAssert.Contains(css, "overflow-wrap: anywhere;");
        StringAssert.Contains(css, "box-sizing: border-box;");
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
