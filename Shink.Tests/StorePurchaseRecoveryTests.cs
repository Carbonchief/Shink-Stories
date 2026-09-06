using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Services;
using Shink.Components.Content;

namespace Shink.Tests;

[TestClass]
public sealed class StorePurchaseRecoveryTests
{
    [TestMethod]
    public async Task RestoreContinuesPastExpiredReceiptsAndTransientFailures()
    {
        var attempted = new List<string>();
        var summary = await StorePurchaseRecovery.RecoverAsync(new[] {"expired", "outage", "active"}, item =>
        {
            attempted.Add(item);
            if (item == "outage") throw new HttpRequestException();
            return Task.FromResult(new StoreDeliveryOutcome(item == "active", false));
        });
        Assert.AreEqual(1, summary.ActivePurchases);
        Assert.AreEqual(1, summary.RetryPurchases);
        CollectionAssert.AreEqual(new[] {"expired", "outage", "active"}, attempted);
    }

    [TestMethod]
    public async Task FailedFinalizationKeepsActiveAccessAndRequiresRetry()
    {
        var summary = await StorePurchaseRecovery.RecoverAsync(new[] {"active"}, _ =>
            Task.FromResult(new StoreDeliveryOutcome(true, true)));
        Assert.AreEqual(1, summary.ActivePurchases);
        Assert.AreEqual(1, summary.RetryPurchases);
    }

    [TestMethod]
    public void WebsiteTierLimitsArePreservedAndEveryAppPlanGrantsFullAccess()
    {
        Assert.Contains("story_corner_monthly", StoryAccessPolicy.GetAllowedTierCodes(StoryAccessRequirement.StoryCornerOrAllStories));
        Assert.DoesNotContain("story_corner_monthly", StoryAccessPolicy.GetAllowedTierCodes(StoryAccessRequirement.AllStoriesOnly));
        Assert.IsFalse(StoryAccessPolicy.HasAllStoriesAccess(["gratis"]));
        Assert.IsFalse(StoryAccessPolicy.HasAllStoriesAccess(["story_corner_monthly"]));
        foreach (var plan in PaymentPlanCatalog.MobileStorePlans)
            Assert.IsTrue(StoryAccessPolicy.HasAllStoriesAccess([plan.TierCode]), plan.StoreProductId);
        Assert.IsTrue(StoryAccessPolicy.HasAllStoriesAccess(["all_stories_monthly"]));
        Assert.IsTrue(StoryAccessPolicy.HasAllStoriesAccess(["all_stories_yearly"]));
    }
}
