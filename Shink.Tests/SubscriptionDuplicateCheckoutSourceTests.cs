using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class SubscriptionDuplicateCheckoutSourceTests
{
    [TestMethod]
    public void PaymentRouteBlocksDuplicateActiveTierBeforeStartingProviderCheckout()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var routeBlock = ExtractBlock(
            program,
            "app.MapGet(\"/betaal/{planSlug}\"",
            ".DisableAntiforgery();",
            startOffset: program.IndexOf("app.MapGet(\"/betaal/{planSlug}\"", StringComparison.Ordinal));

        StringAssert.Contains(routeBlock, "HasActiveSubscriptionForTierAsync(");
        StringAssert.Contains(routeBlock, "betaling\"] = \"reeds-ingeteken\"");

        var activeCheckIndex = routeBlock.IndexOf("HasActiveSubscriptionForTierAsync(", StringComparison.Ordinal);
        var providerResolutionIndex = routeBlock.IndexOf("TryResolvePaymentProvider(", StringComparison.Ordinal);
        Assert.IsTrue(
            activeCheckIndex >= 0 && providerResolutionIndex > activeCheckIndex,
            "The duplicate subscription check must run before any payment provider is selected.");
    }

    [TestMethod]
    public void AbandonedCartSubscriptionRecoveryBlocksDuplicateActiveTierBeforeStartingProviderCheckout()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var routeStart = program.IndexOf("app.MapGet(\"/betaalherinneringe/gaan\"", StringComparison.Ordinal);
        var routeBlock = ExtractBlock(
            program,
            "app.MapGet(\"/betaalherinneringe/gaan\"",
            "app.MapPost(\"/rekening/skuif-na-gratis\"",
            startOffset: routeStart);

        StringAssert.Contains(routeBlock, "ISubscriptionLedgerService subscriptionLedgerService");
        StringAssert.Contains(routeBlock, "HasActiveSubscriptionForTierAsync(");
        StringAssert.Contains(routeBlock, "\"reeds-ingeteken\"");

        var activeCheckIndex = routeBlock.IndexOf("HasActiveSubscriptionForTierAsync(", StringComparison.Ordinal);
        var paystackCheckoutIndex = routeBlock.IndexOf("InitializeCheckoutForEmailAsync(", StringComparison.Ordinal);
        var payFastCheckoutIndex = routeBlock.IndexOf("TryBuildCheckoutForBuyer(", StringComparison.Ordinal);

        Assert.IsTrue(
            activeCheckIndex >= 0 && paystackCheckoutIndex > activeCheckIndex,
            "The duplicate subscription check must run before Paystack checkout is initialized.");
        Assert.IsTrue(
            activeCheckIndex >= 0 && payFastCheckoutIndex > activeCheckIndex,
            "The duplicate subscription check must run before PayFast checkout is initialized.");
    }

    [TestMethod]
    public void SubscriptionRepairRouteDoesNotFallbackToCheckoutWhenRepairIsPending()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var routeBlock = ExtractBlock(
            program,
            "app.MapPost(\"/rekening/herstel-intekening\"",
            ".DisableAntiforgery();",
            startOffset: program.IndexOf("app.MapPost(\"/rekening/herstel-intekening\"", StringComparison.Ordinal));

        StringAssert.Contains(routeBlock, "repairResult.IsPending");
        StringAssert.Contains(routeBlock, "herstel-besig");
        StringAssert.Contains(routeBlock, "IMemoryCache memoryCache");
        StringAssert.Contains(routeBlock, "memoryCache.TryGetValue(repairCacheKey");

        var cacheCheckIndex = routeBlock.IndexOf("memoryCache.TryGetValue(repairCacheKey", StringComparison.Ordinal);
        var repairCallIndex = routeBlock.IndexOf("TryRepairPaidSubscriptionAsync(", StringComparison.Ordinal);
        var pendingCheckIndex = routeBlock.IndexOf("repairResult.IsPending", StringComparison.Ordinal);
        var fallbackPlanIndex = routeBlock.IndexOf("PaymentPlanCatalog.FindBySlug", StringComparison.Ordinal);
        Assert.IsTrue(
            cacheCheckIndex >= 0 && repairCallIndex > cacheCheckIndex,
            "The repair route must block duplicate posts before calling the ledger repair service.");
        Assert.IsTrue(
            pendingCheckIndex >= 0 && fallbackPlanIndex > pendingCheckIndex,
            "Pending Paystack repairs must return before the fresh-checkout fallback is resolved.");
    }

    private static string ExtractBlock(string source, string startMarker, string endMarker, int startOffset = 0)
    {
        var start = source.IndexOf(startMarker, startOffset, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, $"Could not find '{startMarker}'.");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(start, end, $"Could not isolate block ending at '{endMarker}'.");

        return source[start..end];
    }

    private static string GetRepoPath(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new FileNotFoundException("Could not find repo file.", Path.Combine(parts));
    }
}
