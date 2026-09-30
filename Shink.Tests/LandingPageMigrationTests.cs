using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class LandingPageMigrationTests
{
    [TestMethod]
    public void LandingPageTableKeepsDraftAndPublishedSnapshotsSeparateAndRestrictsAccess()
    {
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260929_landing_pages.sql"));

        StringAssert.Contains(migration, "create table if not exists public.landing_pages");
        StringAssert.Contains(migration, "draft_content jsonb not null");
        StringAssert.Contains(migration, "published_content jsonb");
        StringAssert.Contains(migration, "alter table public.landing_pages enable row level security");
        StringAssert.Contains(migration, "to service_role");
        StringAssert.Contains(migration, "landing_page_slug_immutable");
        StringAssert.Contains(migration, "landing_page_revision_must_increment_once");

        var revokePosition = migration.IndexOf(
            "revoke all on table public.landing_pages from public, anon, authenticated, service_role",
            StringComparison.OrdinalIgnoreCase);
        var grantPosition = migration.IndexOf(
            "grant select, insert, update on table public.landing_pages to service_role",
            StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(revokePosition >= 0 && grantPosition > revokePosition,
            "The migration must remove any prior broad service_role table grants before granting minimum access.");

        Assert.IsFalse(Regex.IsMatch(migration, @"grant\s+[^;]*\bdelete\b[^;]*landing_pages", RegexOptions.IgnoreCase));
        Assert.IsFalse(Regex.IsMatch(migration, @"for\s+delete\s+to\s+service_role", RegexOptions.IgnoreCase));
        Assert.IsFalse(Regex.IsMatch(migration, @"\b(drop|delete\s+from|truncate)\b", RegexOptions.IgnoreCase),
            "The migration must not delete data or schema objects.");
    }

    [TestMethod]
    public void LandingPageDataApiPolicyDoesNotGrantPublicOrAuthenticatedAccess()
    {
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260929_landing_pages.sql"));

        StringAssert.Contains(migration, "landing_pages_service_role_select");
        StringAssert.Contains(migration, "landing_pages_service_role_insert");
        StringAssert.Contains(migration, "landing_pages_service_role_update");
        Assert.IsFalse(Regex.IsMatch(migration, @"create\s+policy[^;]*\bto\s+(?:anon|authenticated)\b", RegexOptions.IgnoreCase));
        StringAssert.Contains(migration, "from public, anon, authenticated, service_role");
    }

    private static string GetRepoPath(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Could not find repository path: {Path.Combine(pathParts)}");
        return string.Empty;
    }
}
