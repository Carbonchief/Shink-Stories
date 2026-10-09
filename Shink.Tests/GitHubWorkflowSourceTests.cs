using System.Diagnostics;
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

    [TestMethod]
    public void SupabaseMigrationCheckMatchesBothVersionAndNameInNativeHistory()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "master_schink.yml"));

        StringAssert.Contains(workflow, "supabase_migrations.shink_action_migrations where file_name = '$migration_name_sql'");
        StringAssert.Contains(workflow, "or exists(select 1 from supabase_migrations.schema_migrations");
        StringAssert.Contains(workflow, "where version = '$migration_version_sql' and name = '$migration_slug_sql'");
    }

    [TestMethod]
    [DataRow("true", 0, 0, 0)]
    [DataRow("false", 0, 0, 1)]
    [DataRow("unexpected", 0, 1, 0)]
    [DataRow("", 0, 1, 0)]
    [DataRow("false", 1, 1, 0)]
    public async Task SupabaseMigrationStepSkipsAppliedFilesAndStopsOnLookupFailures(
        string historyResult, int historyExitCode, int expectedExitCode, int expectedApplyCount)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The publish job runs Bash on Ubuntu; this test requires Bash.");
        }

        var workflowPath = FindRepositoryFile(".github", "workflows", "master_schink.yml");
        var workflow = File.ReadAllText(workflowPath).Replace("\r\n", "\n");
        var start = workflow.IndexOf("      - name: Run Supabase migrations\n", StringComparison.Ordinal);
        var scriptStart = workflow.IndexOf("        run: |\n", start, StringComparison.Ordinal) + "        run: |\n".Length;
        var scriptEnd = workflow.IndexOf("\n      - name:", scriptStart, StringComparison.Ordinal);
        var script = string.Join('\n', workflow[scriptStart..scriptEnd].Split('\n')
            .Select(line => line.StartsWith("          ", StringComparison.Ordinal) ? line[10..] : line))
            .Replace("${{ env.SUPABASE_WORKDIR }}", "Shink/Database");
        var directory = Directory.CreateTempSubdirectory("shink-migration-test-");
        try
        {
            var scriptPath = Path.Combine(directory.FullName, "run.sh");
            var appliedPath = Path.Combine(directory.FullName, "applied.txt");
            var queryPath = Path.Combine(directory.FullName, "query.sql");
            var recordedPath = Path.Combine(directory.FullName, "recorded.txt");
            // Replace the CLI with a shell function so the actual workflow loop runs
            // without any database access or credentials.
            await File.WriteAllTextAsync(scriptPath, """
                supabase() {
                  local sql="${@: -1}"
                  if [[ "$*" == *"--output csv"* ]]; then
                    printf '%s\n' "$sql" > "$QUERY_PATH"
                    printf 'already_applied\r\n%s\r\n' "$HISTORY_RESULT"
                    return "$HISTORY_EXIT_CODE"
                  fi
                  if [[ "$*" == *"--file"* ]]; then
                    printf '%s\n' "$sql" >> "$APPLIED_PATH"
                  elif [[ "$sql" == insert* ]]; then
                    printf '%s\n' "$sql" >> "$RECORDED_PATH"
                  fi
                }

                """ + "\n" + script);
            var startInfo = new ProcessStartInfo("bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.Environment.Clear();
            startInfo.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
            startInfo.Environment["GITHUB_WORKSPACE"] = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(workflowPath)!, "..", ".."));
            startInfo.Environment["GITHUB_SHA"] = "test-sha";
            startInfo.Environment["MIGRATION_FILES"] = "Shink/Database/migrations/20261007053828_subscriber_playlists.sql";
            startInfo.Environment["HISTORY_RESULT"] = historyResult;
            startInfo.Environment["HISTORY_EXIT_CODE"] = historyExitCode.ToString();
            startInfo.Environment["QUERY_PATH"] = queryPath;
            startInfo.Environment["APPLIED_PATH"] = appliedPath;
            startInfo.Environment["RECORDED_PATH"] = recordedPath;
            using var process = Process.Start(startInfo)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            Assert.AreEqual(expectedExitCode, process.ExitCode, await stdoutTask + await stderrTask);
            Assert.AreEqual(expectedApplyCount, File.Exists(appliedPath) ? File.ReadAllLines(appliedPath).Length : 0);
            Assert.AreEqual(expectedApplyCount, File.Exists(recordedPath) ? File.ReadAllLines(recordedPath).Length : 0);
            var query = await File.ReadAllTextAsync(queryPath);
            StringAssert.Contains(query, "file_name = '20261007053828_subscriber_playlists.sql'");
            StringAssert.Contains(query, "version = '20261007053828' and name = 'subscriber_playlists'");
            if (historyResult == "true" && historyExitCode == 0)
            {
                StringAssert.Contains(await stdoutTask, "Skipping previously applied migration");
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
