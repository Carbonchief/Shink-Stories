using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class HomeMobileLayoutCssTests
{
    [TestMethod]
    public void HomePromoImageCss_PreservesNaturalAspectRatioOnMobile()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Home.razor.css"));

        StringAssert.Contains(css, ".join-promo-media img");
        StringAssert.Contains(css, "height: auto;");
        StringAssert.Contains(css, "aspect-ratio: 4 / 3;");
        StringAssert.Contains(css, "object-fit: contain;");
        StringAssert.Contains(css, "max-height: min(68svh, 600px);");
    }

    [TestMethod]
    public void ContactFormMarkup_DoesNotMixBootstrapRowWithCustomGrid()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Home.razor"));

        StringAssert.Contains(markup, "<div class=\"contact-fields-grid\">");
        Assert.IsFalse(
            markup.Contains("contact-fields-grid row", StringComparison.Ordinal),
            "The contact form uses a custom CSS grid; Bootstrap row gutters can make mobile textboxes overflow.");
    }

    [TestMethod]
    public void ContactFormCss_ContainsTextboxOverflowOnMobile()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Home.razor.css"));

        StringAssert.Contains(css, ".contact-field {");
        StringAssert.Contains(css, "min-width: 0;");
        StringAssert.Contains(css, ".contact-field ::deep .form-control");
        StringAssert.Contains(css, "max-width: 100%;");
        StringAssert.Contains(css, ".contact-form-shell");
        StringAssert.Contains(css, "width: min(100%, 720px);");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
