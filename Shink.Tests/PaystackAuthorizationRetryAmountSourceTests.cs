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
