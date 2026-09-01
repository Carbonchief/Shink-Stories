using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;

namespace Shink.Tests;

[TestClass]
public class MobilePlansSourceTests
{
    [TestMethod]
    public void MobilePlanCatalogContainsOnlyMonthlyAndYearlyFullAccessOptions()
    {
        var mobilePlans = PaymentPlanCatalog.MobileStorePlans.ToArray();

        Assert.AreEqual(2, mobilePlans.Length);
        CollectionAssert.AreEquivalent(
            new[] { "schink_stories_maandeliks", "schink_stories_jaarliks" },
            mobilePlans.Select(plan => plan.StoreProductId).ToArray());
        Assert.AreEqual(99.00m, mobilePlans.Single(plan => plan.BillingPeriodMonths == 1).Amount);
        Assert.AreEqual(990.00m, mobilePlans.Single(plan => plan.BillingPeriodMonths == 12).Amount);

        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        StringAssert.Contains(program, "app.MapGet(\"/api/mobile/plans\"");
        StringAssert.Contains(program, "PaymentPlanCatalog.MobileStorePlans");
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
        StringAssert.Contains(luister, "await PageHelpers.OpenPlansForStoryAsync(detail.Story);");
        StringAssert.Contains(account, "await Shell.Current.GoToAsync(nameof(PlansPage), animate: true);");
        StringAssert.Contains(shell, "Routing.RegisterRoute(nameof(PlansPage), typeof(PlansPage));");
        StringAssert.Contains(plans, "plan.ProductId is \"schink_stories_maandeliks\" or \"schink_stories_jaarliks\"");
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
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var manifest = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "AndroidManifest.xml"));
        var playManifest = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "AndroidManifest.Play.xml"));
        var storeKit = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "StoreKit", "SchinkStories.storekit"));
        var playScript = File.ReadAllText(GetRepoPath("scripts", "build-mobile-play-aab.sh"));

        Assert.IsFalse(plans.Contains("Browser.OpenAsync", StringComparison.Ordinal));
        Assert.IsFalse(plans.Contains("CheckoutUrl", StringComparison.Ordinal));
        Assert.IsFalse(plans.Contains("SignupUrl", StringComparison.Ordinal));
        StringAssert.Contains(plans, "_storeBilling.PurchaseAsync");
        StringAssert.Contains(plans, "SyncStorePurchaseAsync");
        StringAssert.Contains(plans, "FinalizePurchaseAsync");
        Assert.IsTrue(
            plans.IndexOf("var entitlement = await SyncPurchaseAsync", StringComparison.Ordinal) <
            plans.IndexOf("await FinalizePurchaseAsync(purchaseResult.Purchase);", StringComparison.Ordinal),
            "A Google Play purchase must only be acknowledged after the shared entitlement is recorded.");
        StringAssert.Contains(billing, "CrossInAppBilling.Current");
        StringAssert.Contains(billing, "ItemType.Subscription");
        StringAssert.Contains(billing, "GetAppleProductsAsync");
        StringAssert.Contains(billing, "SKProductsRequest");
        StringAssert.Contains(billing, "GetDebugStoreKitProducts");
        StringAssert.Contains(billing, "PathForResource(\"SchinkStories\", \"storekit\")");
        StringAssert.Contains(billing, "displayPrice");
        StringAssert.Contains(billing, "purchase.TransactionIdentifier ?? purchase.PurchaseToken");
        StringAssert.Contains(billing, "purchase.FinalizationId");
        StringAssert.Contains(api, "/api/mobile/store/entitlement");
        StringAssert.Contains(program, "app.MapPost(\"/api/mobile/store/entitlement\"");
        StringAssert.Contains(program, "MobileStoreEntitlementService");
        var appleApi = File.ReadAllText(GetRepoPath("Shink", "Services", "AppleAppStoreServerApi.cs"));
        StringAssert.Contains(appleApi, "api.storekit.apple.com");
        StringAssert.Contains(appleApi, "api.storekit-sandbox.apple.com");
        StringAssert.Contains(appleApi, "inApps/v1/subscriptions");
        StringAssert.Contains(appleApi, "AppleIssuerId");
        StringAssert.Contains(appleApi, "AppleKeyId");
        StringAssert.Contains(appleApi, "ApplePrivateKey");
        StringAssert.Contains(appleApi, "DSASignatureFormat.IeeeP1363FixedFieldConcatenation");
        StringAssert.Contains(appleApi, "AppleReceiptSigningLeafOid");
        Assert.IsFalse(entitlement.Contains("verifyReceipt", StringComparison.Ordinal));
        Assert.IsFalse(entitlement.Contains("AppleSharedSecret", StringComparison.Ordinal));
        Assert.IsFalse(billing.Contains("ReceiptData", StringComparison.Ordinal));
        StringAssert.Contains(entitlement, "androidpublisher.googleapis.com/androidpublisher/v3/applications");
        StringAssert.Contains(entitlement, "RecordVerifiedStoreSubscriptionAsync");
        StringAssert.Contains(ledger, "Rejected store subscription ownership transfer");
        StringAssert.Contains(migration, "'apple'");
        StringAssert.Contains(migration, "'google_play'");
        StringAssert.Contains(migration, "'app_store'");
        StringAssert.Contains(project, "Plugin.InAppBilling\" Version=\"10.0.0\"");
        StringAssert.Contains(project, "and '$(SchinkGooglePlayBuild)' == 'true'\">23.0</SupportedOSPlatformVersion>");
        StringAssert.Contains(project, "and '$(SchinkGooglePlayBuild)' != 'true'\">21.0</SupportedOSPlatformVersion>");
        StringAssert.Contains(project, "<AndroidMinSdkVersion Condition=\"'$(SchinkGooglePlayBuild)' == 'true'\">23</AndroidMinSdkVersion>");
        StringAssert.Contains(project, "<AndroidMinSdkVersion Condition=\"'$(SchinkGooglePlayBuild)' != 'true'\">21</AndroidMinSdkVersion>");
        StringAssert.Contains(project, "<AndroidTargetSdkVersion Condition=\"'$(SchinkGooglePlayBuild)' == 'true'\">36</AndroidTargetSdkVersion>");
        StringAssert.Contains(project, "<Target Name=\"SelectGooglePlayAndroidManifest\"");
        StringAssert.Contains(project, "<AndroidManifest>Platforms/Android/AndroidManifest.Play.xml</AndroidManifest>");
        StringAssert.Contains(playManifest, "com.android.vending.BILLING");
        StringAssert.Contains(playManifest, "android:minSdkVersion=\"23\"");
        StringAssert.Contains(playManifest, "android:targetSdkVersion=\"36\"");
        StringAssert.Contains(manifest, "tools:overrideLibrary=\"com.android.billingclient,com.google.android.gms.base,com.google.android.gms.common,com.google.android.gms.tasks\"");
        StringAssert.Contains(playManifest, "com.google.android.play.billingclient.version");
        StringAssert.Contains(playManifest, "android:value=\"8.1.0\"");
        StringAssert.Contains(playScript, "-p:SchinkGooglePlayBuild=true");
        StringAssert.Contains(storeKit, "\"displayPrice\" : \"99\"");
        StringAssert.Contains(storeKit, "\"displayPrice\" : \"990\"");
        Assert.IsFalse(storeKit.Contains("storie_hoekie_maandeliks", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobilePlansRemainVisibleWhenStoreProductsHaveNotLoaded()
    {
        var plans = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PlansPage.cs"));

        StringAssert.Contains(plans, "foreach (var plan in plans)");
        StringAssert.Contains(plans, "_storeProducts.TryGetValue(plan.ProductId, out var product)");
        StringAssert.Contains(plans, "$\"R{plan.Amount:0}\"");
        StringAssert.Contains(plans, "Tans nie beskikbaar nie");
        StringAssert.Contains(plans, "Die winkelpryse is tans nie beskikbaar nie.");
    }

    [TestMethod]
    public void MobilePaywallResumesTheLockedStoryAndPostSignInAccessCheck()
    {
        var helpers = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PageHelpers.cs"));
        var plans = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PlansPage.cs"));
        var account = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));
        var story = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var playlistStories = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PlaylistStoriesPage.cs"));
        var playlistDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PlaylistDetailPage.cs"));

        StringAssert.Contains(helpers, "OpenPlansForStoryAsync");
        StringAssert.Contains(helpers, "TryBuildStoryDetailRoute");
        StringAssert.Contains(plans, "$\"../{storyRoute}\"");
        StringAssert.Contains(account, "PageHelpers.TryParseStoryReturnPath(ReturnUrl");
        StringAssert.Contains(account, "detail?.RequiresSubscription == true");
        StringAssert.Contains(story, "detail.RequiresSubscription && _sessionState.Current.IsSignedIn");
        StringAssert.Contains(playlistStories, "PageHelpers.OpenPlansForStoryAsync(story)");
        StringAssert.Contains(playlistDetail, "PageHelpers.OpenPlansForStoryAsync(story)");
        StringAssert.Contains(playlistDetail, "PageHelpers.OpenPlansForStoryAsync(_currentStory)");
    }

    [TestMethod]
    public void MobileAndWebsiteUseTheSameFullAccessEntitlementRows()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var entitlement = File.ReadAllText(GetRepoPath("Shink", "Services", "MobileStoreEntitlementService.cs"));
        var ledger = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        var plans = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PlansPage.cs"));
        var luister = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var downloads = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "OfflineStoryDownloadService.cs"));

        StringAssert.Contains(entitlement, "PaymentPlanCatalog.FindMobileStorePlan(productId)");
        StringAssert.Contains(ledger, "PaymentPlanCatalog.FindMobileStorePlan(normalizedProductId)");
        StringAssert.Contains(ledger, "UpsertSubscriptionAsync(");
        StringAssert.Contains(program, "HasActivePaidSubscriptionAsync(signedInEmail");
        StringAssert.Contains(program, "StoryAccessPolicy.HasAllStoriesAccess(activeTierCodes)");
        StringAssert.Contains(program, "HasFullStoryAccess: hasFullStoryAccess");
        StringAssert.Contains(plans, "_sessionState.Current.HasFullStoryAccess");
        StringAssert.Contains(plans, "Jy hoef nie weer te betaal nie");
        StringAssert.Contains(luister, "previous.HasFullStoryAccess == current.HasFullStoryAccess");
        StringAssert.Contains(downloads, "_sessionState.Current.HasFullStoryAccess");
        StringAssert.Contains(downloads, "session.HasFullStoryAccess");
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
