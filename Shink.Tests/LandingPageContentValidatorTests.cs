using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class LandingPageContentValidatorTests
{
    [TestMethod]
    public void DraftValidationAllowsIncompleteContentAndAppliesDefaults()
    {
        var validator = new LandingPageContentValidator();

        var normalized = validator.NormalizeAndValidate(new LandingPageContent
        {
            Title = "  ",
            BackgroundColor = "",
            TextColor = "",
            Blocks = []
        });

        Assert.AreEqual(1, normalized.SchemaVersion);
        Assert.AreEqual(string.Empty, normalized.Title);
        Assert.AreEqual("#ff7133", normalized.BackgroundColor);
        Assert.AreEqual("#ffffff", normalized.TextColor);
        Assert.AreEqual(0, normalized.Blocks.Count);
    }

    [TestMethod]
    public void PublishValidationSanitizesTextHtmlAndRequiresVisibleText()
    {
        var validator = new LandingPageContentValidator();
        var normalized = validator.NormalizeAndValidate(new LandingPageContent
        {
            Title = "Landing page",
            Blocks =
            [
                new LandingPageBlock
                {
                    Type = "text",
                    Html = "<h2 onclick=\"alert(1)\">Welkom</h2><script>alert(2)</script><p><a href=\"javascript:alert(3)\">Skadelik</a><a href=\"/luister\">Luister</a></p>"
                }
            ]
        }, forPublishing: true);

        var html = normalized.Blocks[0].Html;
        StringAssert.Contains(html, "<h2>Welkom</h2>");
        StringAssert.Contains(html, "href=\"/luister\"");
        Assert.IsFalse(html.Contains("onclick", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("<script", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("javascript:", StringComparison.OrdinalIgnoreCase));

        var emptyText = new LandingPageContent
        {
            Title = "Landing page",
            Blocks = [new LandingPageBlock { Type = "text", Html = "<p><br></p>" }]
        };

        Assert.ThrowsExactly<ArgumentException>(() => validator.NormalizeAndValidate(emptyText, forPublishing: true));
    }

    [TestMethod]
    public void ImageUrlsMustBeSiteRelativeOrOnConfiguredR2Host()
    {
        var validator = new LandingPageContentValidator(
            Options.Create(new CloudflareR2Options { PublicBaseUrl = "https://cdn.schink.test/media" }),
            Options.Create(new SiteOptions { PublicBaseUrl = "https://www.schink.test" }));

        var normalized = validator.NormalizeAndValidate(new LandingPageContent
        {
            Blocks =
            [
                new LandingPageBlock
                {
                    Type = "image",
                    ImageUrl = "https://cdn.schink.test/media/landing/cover.webp"
                }
            ]
        });

        Assert.AreEqual("https://cdn.schink.test/media/landing/cover.webp", normalized.Blocks[0].ImageUrl);

        Assert.ThrowsExactly<ArgumentException>(() => validator.NormalizeAndValidate(new LandingPageContent
        {
            Blocks = [new LandingPageBlock { Type = "image", ImageUrl = "https://other.example/cover.webp" }]
        }));
        Assert.ThrowsExactly<ArgumentException>(() => validator.NormalizeAndValidate(new LandingPageContent
        {
            Blocks = [new LandingPageBlock { Type = "image", ImageUrl = "//other.example/cover.webp" }]
        }));
    }

    [TestMethod]
    public void LinkPolicyAcceptsSafePathsAndHttpsAndRejectsUnsafeSchemesAndPaths()
    {
        Assert.IsTrue(LandingPageContentValidator.IsSafeLinkUrl("/meer-oor-ons"));
        Assert.IsTrue(LandingPageContentValidator.IsSafeLinkUrl("#lees-meer"));
        Assert.IsTrue(LandingPageContentValidator.IsSafeLinkUrl("https://example.org/stories"));
        Assert.IsFalse(LandingPageContentValidator.IsSafeLinkUrl("//example.org/stories"));
        Assert.IsFalse(LandingPageContentValidator.IsSafeLinkUrl("/../admin"));
        Assert.IsFalse(LandingPageContentValidator.IsSafeLinkUrl("javascript:alert(1)"));
        Assert.IsFalse(LandingPageContentValidator.IsSafeLinkUrl("https://user:pass@example.org"));
        Assert.IsFalse(LandingPageContentValidator.IsSafeLinkUrl("https://example.org/\r\n"));
        Assert.IsFalse(LandingPageContentValidator.IsSafeLinkUrl("https:\\example.org"));
    }

    [TestMethod]
    public void UnsupportedContentTypesAndColorsAreRejected()
    {
        var validator = new LandingPageContentValidator();

        Assert.ThrowsExactly<ArgumentException>(() => validator.NormalizeAndValidate(new LandingPageContent
        {
            BackgroundColor = "red"
        }));
        Assert.ThrowsExactly<ArgumentException>(() => validator.NormalizeAndValidate(new LandingPageContent
        {
            Blocks = [new LandingPageBlock { Type = "video" }]
        }));
        Assert.ThrowsExactly<ArgumentException>(() => validator.NormalizeAndValidate(new LandingPageContent
        {
            Blocks = [new LandingPageBlock { Type = "text", Alignment = "justify" }]
        }));
    }
}
