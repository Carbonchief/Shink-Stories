using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminSubscriberManagementMigrationTests
{
    [TestMethod]
    public void Migration_AddsSoftDisableAdminOverrideAndAuditSupport()
    {
        var migrationPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Shink",
            "Database",
            "migrations",
            "20260428_admin_subscriber_management.sql"));
        var sql = File.ReadAllText(migrationPath);

        StringAssert.Contains(sql, "disabled_at");
        StringAssert.Contains(sql, "'admin_override'");
        StringAssert.Contains(sql, "subscriber_admin_audit");
        StringAssert.Contains(sql, "idx_subscriptions_admin_override_active");
    }
}
