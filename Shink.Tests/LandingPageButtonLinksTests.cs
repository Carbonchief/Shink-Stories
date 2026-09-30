using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class LandingPageButtonLinksTests
{
    [TestMethod]
    public void ExplicitLinkTypeSurvivesContentValidationAndSerialization()
    {
        foreach (var type in new[] { "custom", "subscription" })
        {
            var content = new LandingPageContent { Title = "Test", Blocks = [new LandingPageBlock {
                Type = "button", Text = "Join", LinkType = type, Url = "/opsies?plan=schink-stories-jaarliks"
            }] };
            var normalized = new LandingPageContentValidator().NormalizeAndValidate(content, forPublishing: true);
            var restored = System.Text.Json.JsonSerializer.Deserialize<LandingPageContent>(System.Text.Json.JsonSerializer.Serialize(normalized))!;
            Assert.AreEqual(type, restored.Blocks[0].LinkType);
            Assert.AreEqual(type == "subscription" ? LandingPageButtonLinks.BuildSubscriptionUrl("schink-stories-jaarliks") : content.Blocks[0].Url, restored.Blocks[0].Url);
        }
    }

    [TestMethod]
    public void SubscriptionOptionsBuildRecognizablePlanLinks()
    {
        Assert.AreEqual(3, LandingPageButtonLinks.SubscriptionOptions.Count);
        foreach (var plan in LandingPageButtonLinks.SubscriptionOptions)
        {
            var url = LandingPageButtonLinks.BuildSubscriptionUrl(plan.Slug);
            Assert.AreEqual($"/teken-op?plan={plan.Slug}&returnUrl={Uri.EscapeDataString($"/betaal/{plan.Slug}")}", url);
            Assert.AreEqual(plan.Slug, LandingPageButtonLinks.GetSubscriptionSlug($"/opsies?plan={plan.Slug}"));
            Assert.AreEqual(plan.Slug, LandingPageButtonLinks.GetSubscriptionSlug(url));
        }
    }

    [TestMethod]
    public void CustomUrlsAreNotReinterpretedOrStrippedOfParameters()
    {
        foreach (var url in new[] { "/opsies", "https://example.com", "/opsies?plan=schink-stories-jaarliks&discountCode=TEST", "/opsies?plan=unknown", "" })
            Assert.IsNull(LandingPageButtonLinks.GetSubscriptionSlug(url));
    }

    [TestMethod]
    public void UnknownAndNonPublicOptionsCannotGenerateSubscriptionLinks()
    {
        foreach (var slug in new[] { "unknown", "skool-klein-jaarliks", "skool-premium-jaarliks" })
            Assert.ThrowsExactly<ArgumentException>(() => LandingPageButtonLinks.BuildSubscriptionUrl(slug));
    }
}
