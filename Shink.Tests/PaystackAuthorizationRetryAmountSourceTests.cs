using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class PaystackAuthorizationRetryAmountSourceTests
{
    [TestMethod]
    public void BatchRetryUsesStoredBillingAmountForChargeAuthorization()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackAuthorizationRetryBatchService.cs"));

        StringAssert.Contains(service, "billing_amount_zar");
        StringAssert.Contains(service, "candidate.Subscription.BillingAmountZar");
        StringAssert.Contains(service, "SkippedMissingBillingAmountCount");
        StringAssert.Contains(service, "candidate.BillingAmountZar is not > 0m");
        StringAssert.Contains(service, "ChargeAuthorizationAsync(");
    }

    [TestMethod]
    public void BatchRetryRequiresDuePaystackRecoveryRows()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackAuthorizationRetryBatchService.cs"));

        StringAssert.Contains(service, "FetchRetryReadyRecoveryBySubscriptionIdAsync");
        StringAssert.Contains(service, "subscription_payment_recoveries");
        StringAssert.Contains(service, "authorization_retry_status=eq.pending");
        StringAssert.Contains(service, "authorization_retry_due_at=lte");
        StringAssert.Contains(service, "FirstFailedAt");
        StringAssert.Contains(service, "AuthorizationRetryDelay");
        Assert.IsFalse(
            service.Contains("next_renewal_at.lt", StringComparison.Ordinal),
            "Batch retry must not charge from elapsed local renewal dates alone.");
    }

    [TestMethod]
    public void BatchRetrySkipsCandidatesWithCurrentDuplicatePaystackSubscription()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackAuthorizationRetryBatchService.cs"));

        StringAssert.Contains(service, "FetchCurrentPaystackSubscriptionCodesBySubscriberAsync");
        StringAssert.Contains(service, "HasCurrentDuplicatePaystackSubscription");
        StringAssert.Contains(service, "SkippedDuplicateCurrentSubscriptionCount");
        StringAssert.Contains(service, "skippedDuplicateCurrentSubscriptionCount");
        StringAssert.Contains(service, "next_renewal_at.gte");
    }

    [TestMethod]
    public void BatchRetryEventsIncludeDedupeAndProcessingStatus()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackAuthorizationRetryBatchService.cs"));

        StringAssert.Contains(service, "event_dedupe_key");
        StringAssert.Contains(service, "BuildRetryEventDedupeKey");
        StringAssert.Contains(service, "processing_status = \"processed\"");
        StringAssert.Contains(service, "processing_error = (string?)null");
    }

    [TestMethod]
    public void SelfServicePaystackRetryRequiresStoredBillingAmount()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));

        StringAssert.Contains(service, "subscription.BillingAmountZar is > 0m");
        StringAssert.Contains(service, "No stored billing amount is available for this subscription.");
        StringAssert.Contains(service, "subscription.BillingAmountZar!.Value");
        StringAssert.Contains(service, "subscriptionContext.BillingAmountZar!.Value");
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
