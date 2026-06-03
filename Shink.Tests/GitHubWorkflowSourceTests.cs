using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class GitHubWorkflowSourceTests
{
    [TestMethod]
    public void SupabaseMigrationStepUsesAbsoluteSqlFilePath()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "master_schink.yml"));

        StringAssert.Contains(workflow, "migration_path=\"$GITHUB_WORKSPACE/$migration_file\"");
        StringAssert.Contains(workflow, "if [ ! -f \"$migration_path\" ]; then");
        StringAssert.Contains(workflow, "supabase --workdir \"${{ env.SUPABASE_WORKDIR }}\" db query --linked --file \"$migration_path\"");
        Assert.IsFalse(
            workflow.Contains("--file \"$migration_file\"", StringComparison.Ordinal),
            "The Supabase CLI resolves --file relative to --workdir, so repo-relative migration paths fail.");
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
