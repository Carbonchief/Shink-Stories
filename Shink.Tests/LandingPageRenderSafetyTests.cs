using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Shared;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class LandingPageRenderSafetyTests
{
    [TestMethod]
    public void CssColor_AcceptsHexValuesAndRejectsCssDeclarations()
    {
        Assert.AreEqual("#a3c", LandingPageRenderSafety.CssColor("#a3c", "#ffffff"));
        Assert.AreEqual("#12345678", LandingPageRenderSafety.CssColor("#12345678", "#ffffff"));
        Assert.AreEqual("#ffffff", LandingPageRenderSafety.CssColor("#fff;display:none", "#ffffff"));
        Assert.AreEqual("#ffffff", LandingPageRenderSafety.CssColor("url(javascript:alert(1))", "#ffffff"));
    }

    [TestMethod]
    public void LinkUrl_UsesTheLandingPageValidatorPolicy()
    {
        var safeUrls = new[] { "/stories", "/stories?filter=free", "#more", "?ref=campaign", "https://example.com/page" };
        var unsafeUrls = new[] { "http://example.com", "javascript:alert(1)", "//example.com/page", "/%2e%2e/admin", "https://user:pass@example.com", "/safe\r\n" };

        foreach (var url in safeUrls)
        {
            Assert.AreEqual(url, LandingPageRenderSafety.LinkUrl(url));
            Assert.IsTrue(LandingPageContentValidator.IsSafeLinkUrl(url));
        }

        foreach (var url in unsafeUrls)
        {
            Assert.IsNull(LandingPageRenderSafety.LinkUrl(url), url);
            Assert.IsFalse(LandingPageContentValidator.IsSafeLinkUrl(url), url);
        }
    }

    [TestMethod]
    public void ImageUrl_AllowsHttpsAndSafeRootPathsOnly()
    {
        Assert.AreEqual("/media/campaign.webp", LandingPageRenderSafety.ImageUrl("/media/campaign.webp"));
        Assert.AreEqual("https://media.example.com/campaign.webp", LandingPageRenderSafety.ImageUrl("https://media.example.com/campaign.webp"));
        Assert.IsNull(LandingPageRenderSafety.ImageUrl("http://media.example.com/campaign.webp"));
        Assert.IsNull(LandingPageRenderSafety.ImageUrl("//media.example.com/campaign.webp"));
        Assert.IsNull(LandingPageRenderSafety.ImageUrl("/%2e%2e/private.webp"));
        Assert.IsNull(LandingPageRenderSafety.ImageUrl("/safe.webp\r\n"));
        Assert.IsNull(LandingPageRenderSafety.ImageUrl("data:image/svg+xml,<svg></svg>"));
    }

    [TestMethod]
    public void SanitizeRichTextHtml_KeepsTextFormattingAndRemovesActiveMarkup()
    {
        var html = LandingPageContentValidator.SanitizeRichTextHtml(
            """
            <p onclick="run()">Lees <strong>hier</strong>.</p>
            <a href="javascript:run()">Onveilige skakel</a>
            <img src="https://example.com/tracker.png" onerror="run()">
            <script>run()</script>
            """);

        StringAssert.Contains(html, "<p>Lees <strong>hier</strong>.</p>");
        StringAssert.Contains(html, "Onveilige skakel");
        Assert.IsFalse(html.Contains("onclick", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("javascript:", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("<img", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("<script", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("onerror", StringComparison.OrdinalIgnoreCase));
    }
}
