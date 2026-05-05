using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class WordPressMigrationSourceTests
{
    [TestMethod]
    public void CurrentPayFastEntitlementsCarryGatewaySubscriptionIdAsProviderToken()
    {
        var sourcePath = FindRepositoryFile("Shink", "Services", "WordPressMigrationService.cs");
        var source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "ProviderToken: string.Equals(provider, \"payfast\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(source, "? providerTransactionId");
    }

    [TestMethod]
    public void FutureDiscountCodeWindowsAreTreatedAsCurrentEntitlements()
    {
        var sourcePath = FindRepositoryFile("Shink", "Services", "WordPressMigrationService.cs");
        var source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "LoadDiscountCodeAccessKeysAsync");
        StringAssert.Contains(source, "HasFutureDiscountCodeAccess(period, discountCodeAccessKeys, now)");
        StringAssert.Contains(source, "period.CodeId.HasValue");
        StringAssert.Contains(source, "period.EndDate.Value <= now");
    }

    [TestMethod]
    public void NativePaystackSubscriptionsSuppressDuplicateImportedEntitlements()
    {
        var sourcePath = FindRepositoryFile("Shink", "Services", "WordPressMigrationService.cs");
        var source = File.ReadAllText(sourcePath);

        StringAssert.Contains(source, "FilterNativeSubscriptionDuplicatesAsync");
        StringAssert.Contains(source, "source_system=eq.shink_app&status=eq.active");
        StringAssert.Contains(source, "provider_transaction_id");
        StringAssert.Contains(source, "provider_email_token");
        StringAssert.Contains(source, "BuildSubscriptionMatchKeys");
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Could not find repository file: {Path.Combine(pathParts)}");
        return string.Empty;
    }
}
