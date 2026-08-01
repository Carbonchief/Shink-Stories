using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;

namespace Shink.Tests;

[TestClass]
public class MobilePlansSourceTests
{
    [TestMethod]
    public void MobilePlanCatalogContainsOnlyHouseholdOptions()
    {
        var householdPlans = PaymentPlanCatalog.All
            .Where(plan => !plan.IsSchoolPlan && !plan.IsAdminOnly)
            .ToArray();

        Assert.AreEqual(3, householdPlans.Length);
        Assert.IsFalse(householdPlans.Any(plan => plan.Slug.StartsWith("skool-", StringComparison.OrdinalIgnoreCase)));

        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        StringAssert.Contains(program, "app.MapGet(\"/api/mobile/plans\"");
        StringAssert.Contains(program, ".Where(plan => !plan.IsSchoolPlan && !plan.IsAdminOnly)");
        StringAssert.Contains(program, "sealed record MobilePlansResponse");
    }

    [TestMethod]
    public void MobileLockedStoriesAndSignupOpenNativePlansPage()
    {
        var luister = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var account = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));
        var shell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var plans = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PlansPage.cs"));

        StringAssert.Contains(luister, "if (story.IsLocked)");
        StringAssert.Contains(luister, "await OpenPlansAsync(BuildStoryReturnPath(story));");
        StringAssert.Contains(account, "await Shell.Current.GoToAsync(nameof(PlansPage), animate: true);");
        StringAssert.Contains(shell, "Routing.RegisterRoute(nameof(PlansPage), typeof(PlansPage));");
        StringAssert.Contains(plans, "StartsWith(\"skool-\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(plans, "mobile_paywall_viewed");
    }

    [TestMethod]
    public void MobileSubscriptionsUseNativeStoresAndVerifiedEntitlements()
    {
        var plans = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PlansPage.cs"));
        var billing = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileStoreBillingService.cs"));
        var api = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var entitlement = File.ReadAllText(GetRepoPath("Shink", "Services", "MobileStoreEntitlementService.cs"));
        var ledger = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260731_mobile_store_subscriptions.sql"));

        Assert.IsFalse(plans.Contains("Browser.OpenAsync", StringComparison.Ordinal));
        Assert.IsFalse(plans.Contains("CheckoutUrl", StringComparison.Ordinal));
        Assert.IsFalse(plans.Contains("SignupUrl", StringComparison.Ordinal));
        StringAssert.Contains(plans, "_storeBilling.PurchaseAsync");
        StringAssert.Contains(plans, "SyncStorePurchaseAsync");
        StringAssert.Contains(plans, "FinalizePurchaseAsync");
        StringAssert.Contains(billing, "CrossInAppBilling.Current");
        StringAssert.Contains(billing, "ItemType.Subscription");
        StringAssert.Contains(api, "/api/mobile/store/entitlement");
        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/store/entitlement\"");
        StringAssert.Contains(program, "MobileStoreEntitlementService");
        StringAssert.Contains(entitlement, "buy.itunes.apple.com/verifyReceipt");
        StringAssert.Contains(entitlement, "androidpublisher.googleapis.com/androidpublisher/v3/applications");
        StringAssert.Contains(entitlement, "RecordVerifiedStoreSubscriptionAsync");
        StringAssert.Contains(ledger, "Rejected store subscription ownership transfer");
        StringAssert.Contains(migration, "'apple'");
        StringAssert.Contains(migration, "'google_play'");
        StringAssert.Contains(migration, "'app_store'");
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
