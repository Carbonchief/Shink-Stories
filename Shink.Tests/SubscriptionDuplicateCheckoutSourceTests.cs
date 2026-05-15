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
    public void PaymentRouteBlocksPendingPaystackRepairBeforeStartingProviderCheckout()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var routeBlock = ExtractBlock(
            program,
            "app.MapGet(\"/betaal/{planSlug}\"",
            ".DisableAntiforgery();",
            startOffset: program.IndexOf("app.MapGet(\"/betaal/{planSlug}\"", StringComparison.Ordinal));

        StringAssert.Contains(routeBlock, "HasPendingPaystackRepairForTierAsync(");
        StringAssert.Contains(routeBlock, "\"herstel-besig\"");

        var pendingRepairCheckIndex = routeBlock.IndexOf("HasPendingPaystackRepairForTierAsync(", StringComparison.Ordinal);
        var paystackCheckoutIndex = routeBlock.IndexOf("InitializeCheckoutAsync(", StringComparison.Ordinal);
        Assert.IsTrue(
            pendingRepairCheckIndex >= 0 && paystackCheckoutIndex > pendingRepairCheckIndex,
            "A pending Paystack repair charge must block a fresh checkout before Paystack can be initialized again.");
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
    public void AbandonedCartSubscriptionRecoveryBlocksPendingPaystackRepairBeforeStartingProviderCheckout()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var routeStart = program.IndexOf("app.MapGet(\"/betaalherinneringe/gaan\"", StringComparison.Ordinal);
        var routeBlock = ExtractBlock(
            program,
            "app.MapGet(\"/betaalherinneringe/gaan\"",
            "app.MapPost(\"/rekening/skuif-na-gratis\"",
            startOffset: routeStart);

        StringAssert.Contains(routeBlock, "HasPendingPaystackRepairForTierAsync(");
        StringAssert.Contains(routeBlock, "\"herstel-besig\"");

        var pendingRepairCheckIndex = routeBlock.IndexOf("HasPendingPaystackRepairForTierAsync(", StringComparison.Ordinal);
        var paystackCheckoutIndex = routeBlock.IndexOf("InitializeCheckoutForEmailAsync(", StringComparison.Ordinal);
        Assert.IsTrue(
            pendingRepairCheckIndex >= 0 && paystackCheckoutIndex > pendingRepairCheckIndex,
            "A pending Paystack repair charge must block abandoned-cart checkout before Paystack can be initialized again.");
    }

    [TestMethod]
    public void PendingPaystackRepairCheckoutBlockUsesLongerWindowThanRepairIdempotency()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));

        StringAssert.Contains(ledgerService, "PendingAccountRepairCheckoutBlockWindow = TimeSpan.FromHours(1)");
        StringAssert.Contains(ledgerService, "nowUtc.Subtract(PendingAccountRepairCheckoutBlockWindow)");
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

    [TestMethod]
    public void PaystackRetryRoutesPollPaystackBeforeStartingFreshCheckout()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        var paymentRouteBlock = ExtractBlock(
            program,
            "app.MapGet(\"/betaal/{planSlug}\"",
            ".DisableAntiforgery();",
            startOffset: program.IndexOf("app.MapGet(\"/betaal/{planSlug}\"", StringComparison.Ordinal));
        AssertCallOrder(
            paymentRouteBlock,
            "TryRedirectRecoveredPaystackSubscriptionAsync(",
            "InitializeCheckoutAsync(",
            "The payment route must poll Paystack before initializing a fresh Paystack checkout.");

        var abandonedRecoveryRouteBlock = ExtractBlock(
            program,
            "app.MapGet(\"/betaalherinneringe/gaan\"",
            "app.MapPost(\"/rekening/skuif-na-gratis\"",
            startOffset: program.IndexOf("app.MapGet(\"/betaalherinneringe/gaan\"", StringComparison.Ordinal));
        AssertCallOrder(
            abandonedRecoveryRouteBlock,
            "TryRedirectRecoveredPaystackSubscriptionAsync(",
            "InitializeCheckoutForEmailAsync(",
            "The abandoned-cart retry route must poll Paystack before initializing a fresh Paystack checkout.");

        var repairRouteBlock = ExtractBlock(
            program,
            "app.MapPost(\"/rekening/herstel-intekening\"",
            ".DisableAntiforgery();",
            startOffset: program.IndexOf("app.MapPost(\"/rekening/herstel-intekening\"", StringComparison.Ordinal));
        AssertCallOrder(
            repairRouteBlock,
            "TryRedirectRecoveredPaystackSubscriptionAsync(",
            "InitializeCheckoutAsync(",
            "The subscription repair route must poll Paystack before initializing a fresh Paystack checkout.");
    }

    private static void AssertCallOrder(string source, string firstCall, string secondCall, string message)
    {
        var firstIndex = source.IndexOf(firstCall, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(secondCall, StringComparison.Ordinal);
        Assert.IsTrue(
            firstIndex >= 0 && secondIndex > firstIndex,
            message);
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
