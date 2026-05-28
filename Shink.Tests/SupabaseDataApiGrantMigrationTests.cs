using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed partial class SupabaseDataApiGrantMigrationTests
{
    [TestMethod]
    public void ExplicitDataApiGrantMigrationCoversEveryPublicTableCreatedByMigrations()
    {
        var migrationsDirectory = GetRepoPath("Shink", "Database", "migrations");
        var grantMigration = File.ReadAllText(Path.Combine(migrationsDirectory, "20260528_data_api_explicit_table_grants.sql"));
        var tableNames = Directory
            .EnumerateFiles(migrationsDirectory, "*.sql")
            .SelectMany(path => PublicTableCreateRegex().Matches(File.ReadAllText(path)))
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.IsTrue(tableNames.Length > 0, "Expected at least one public table creation in the migration history.");

        foreach (var tableName in tableNames)
        {
            StringAssert.Contains(grantMigration, $"public.{tableName}");
        }

        StringAssert.Contains(grantMigration, "to service_role;");
        StringAssert.Contains(grantMigration, "grant usage, select on all sequences in schema public to service_role;");
    }

    [TestMethod]
    public void ExplicitDataApiGrantMigrationKeepsAnonAccessReadOnly()
    {
        var grantMigration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260528_data_api_explicit_table_grants.sql"));

        Assert.IsFalse(
            Regex.IsMatch(grantMigration, @"grant\s+[^;]*(insert|update|delete)[^;]*\bto\s+anon\b", RegexOptions.IgnoreCase),
            "Anon should not receive write privileges through the Data API grant migration.");
        StringAssert.Contains(grantMigration, "to anon, authenticated;");
    }

    [GeneratedRegex(@"create\s+table\s+(?:if\s+not\s+exists\s+)?public\.(?<name>[a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex PublicTableCreateRegex();

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
