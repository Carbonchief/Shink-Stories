using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class SearchEngineSourceTests
{
    [TestMethod]
    public void DocumentDeclaresAfrikaansAsItsPageLanguage()
    {
        var app = File.ReadAllText(GetRepoPath("Shink", "Components", "App.razor"));

        StringAssert.Contains(app, "<html lang=\"af\">");
        Assert.IsFalse(app.Contains("<html lang=\"en\">", StringComparison.Ordinal));
    }

    [TestMethod]
    public void HomepageReservesMobileCtaSpaceAndDefersBelowFoldArtwork()
    {
        var home = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Home.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Home.razor.css"));

        StringAssert.Contains(home, "<meta name=\"description\"");
        StringAssert.Contains(home, "<span class=\"hero-cta-placeholder\"");
        StringAssert.Contains(home, "Kom Luister Saam by Schink Stories\" width=\"800\" height=\"600\" loading=\"lazy\"");
        StringAssert.Contains(css, ".hero-cta-placeholder");
        StringAssert.Contains(css, "min-height: calc(2 * 46px + 0.55rem);");
    }

    [TestMethod]
    public void HighImpressionPublicPagesExposeStandardDescriptions()
    {
        var about = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "MeerOorOns.razor"));
        var options = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Opsies.razor"));

        StringAssert.Contains(about, "<meta name=\"description\"");
        StringAssert.Contains(options, "<meta name=\"description\"");
    }

    [TestMethod]
    public void SitemapContainsPublicContentAndExcludesInternalSearch()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var sitemapStart = program.IndexOf("app.MapGet(\"/sitemap.xml\"", StringComparison.Ordinal);
        var sitemapEnd = program.IndexOf("var legacyFreeStorySlugs", sitemapStart, StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, sitemapStart);
        Assert.IsGreaterThan(sitemapStart, sitemapEnd);
        var sitemap = program[sitemapStart..sitemapEnd];

        StringAssert.Contains(sitemap, "\"/blog\"");
        StringAssert.Contains(sitemap, "\"/gratis\"");
        StringAssert.Contains(sitemap, "paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();");
        Assert.IsFalse(sitemap.Contains("\"/soek\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AuthStartAndBlogFeedDeclareResponseLevelNoindex()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        var authStart = program.IndexOf("app.MapGet(\"/api/auth/google/start\"", StringComparison.Ordinal);
        var feedStart = program.IndexOf("app.MapGet(\"/blog/rss.xml\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, authStart);
        Assert.IsGreaterThanOrEqualTo(0, feedStart);

        var robotsHeader = "httpContext.Response.Headers[\"X-Robots-Tag\"] = \"noindex, nofollow, noarchive, nosnippet\";";
        StringAssert.Contains(program[authStart..(authStart + 500)], robotsHeader);
        StringAssert.Contains(program[feedStart..(feedStart + 400)], robotsHeader);
    }

    [TestMethod]
    public void FilteredBlogUrlsAreCanonicalizedAndExcludedFromIndex()
    {
        var blog = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Blog.razor"));

        StringAssert.Contains(blog, "<meta name=\"robots\" content=\"noindex, follow, noarchive, nosnippet\" />");
        StringAssert.Contains(blog, "private string CanonicalUrl => NavigationManager.ToAbsoluteUri(\"/blog\").ToString();");
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
