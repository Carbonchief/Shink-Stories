using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed partial class SupabaseDataApiGrantMigrationTests
{
    [TestMethod]
    public void ExplicitDataApiGrantsCoverEveryPublicTableCreatedByMigrations()
    {
        var migrationsDirectory = GetRepoPath("Shink", "Database", "migrations");
        var grantSql = string.Join(
            Environment.NewLine,
            Directory
                .EnumerateFiles(migrationsDirectory, "*.sql")
                .Select(File.ReadAllText)
                .SelectMany(sql => GrantStatementRegex().Matches(sql))
                .Select(match => match.Value));
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
            StringAssert.Contains(grantSql, $"public.{tableName}");
        }

        StringAssert.Contains(grantSql, "to service_role;");
        StringAssert.Contains(grantSql, "grant usage, select on all sequences in schema public to service_role;");
    }

    [TestMethod]
    public void ExplicitDataApiGrantMigrationKeepsAnonAccessReadOnly()
    {
        var migrationsDirectory = GetRepoPath("Shink", "Database", "migrations");
        var grantSql = string.Join(
            Environment.NewLine,
            Directory
                .EnumerateFiles(migrationsDirectory, "*.sql")
                .Select(File.ReadAllText)
                .SelectMany(sql => GrantStatementRegex().Matches(sql))
                .Select(match => match.Value));

        Assert.IsFalse(
            Regex.IsMatch(grantSql, @"grant\s+[^;]*(insert|update|delete)[^;]*\bto\s+anon\b", RegexOptions.IgnoreCase),
            "Anon should not receive write privileges through the Data API grant migration.");
        StringAssert.Contains(grantSql, "to anon, authenticated;");
    }

    [GeneratedRegex(@"create\s+table\s+(?:if\s+not\s+exists\s+)?public\.(?<name>[a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex PublicTableCreateRegex();

    [GeneratedRegex(@"\bgrant\b[\s\S]*?;", RegexOptions.IgnoreCase)]
    private static partial Regex GrantStatementRegex();

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
