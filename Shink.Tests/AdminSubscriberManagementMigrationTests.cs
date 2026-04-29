using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminSubscriberManagementMigrationTests
{
    [TestMethod]
    public void Migration_AddsSoftDisableAdminOverrideAndAuditSupport()
    {
        var migrationPath = FindRepositoryFile(
            "Shink",
            "Database",
            "migrations",
            "20260428_admin_subscriber_management.sql");
        var sql = File.ReadAllText(migrationPath);

        StringAssert.Contains(sql, "disabled_at");
        StringAssert.Contains(sql, "'admin_override'");
        StringAssert.Contains(sql, "subscriber_admin_audit");
        StringAssert.Contains(sql, "idx_subscriptions_admin_override_active");
    }

    [TestMethod]
    public void Migration_AddsPaystackEmailTokenForSelfServiceCancellation()
    {
        var migrationPath = FindRepositoryFile(
            "Shink",
            "Database",
            "migrations",
            "20260429_self_service_subscription_cancellation.sql");
        var sql = File.ReadAllText(migrationPath);

        StringAssert.Contains(sql, "provider_email_token");
        StringAssert.Contains(sql, "public.subscriptions");
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
