namespace Shink.Tests;

[TestClass]
public sealed class PaystackSubscriptionTokenUniquenessMigrationTests
{
    [TestMethod]
    public void MigrationCancelsDuplicateActivePaystackTokenRowsBeforeAddingUniqueIndex()
    {
        var migration = File.ReadAllText(FindRepositoryFile(
            "Shink",
            "Database",
            "migrations",
            "20260529_paystack_subscription_token_uniqueness.sql"));

        StringAssert.Contains(migration, "row_number() over");
        StringAssert.Contains(migration, "provider_payment_id like 'SUB\\_%' escape '\\'");
        StringAssert.Contains(migration, "ranked.duplicate_rank > 1");
        StringAssert.Contains(migration, "create unique index if not exists uq_subscriptions_active_paystack_token_tier");
        StringAssert.Contains(migration, "cancelled_at is null");
        StringAssert.Contains(migration, "provider_token is not null");
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
