using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class StoryTestQuestionSourceTests
{
    [TestMethod]
    public void LuisterStoryShowsTestButtonOnlyWhenQuestionsExist()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor.css"));

        StringAssert.Contains(markup, "HasStoryTestQuestions(CurrentStory)");
        StringAssert.Contains(markup, "class=\"story-test-open-btn\"");
        StringAssert.Contains(markup, "class=\"story-test-modal\"");
        StringAssert.Contains(markup, "SelectStoryTestOption(index, \"A\")");
        StringAssert.Contains(markup, "SelectStoryTestOption(index, \"B\")");
        StringAssert.Contains(css, ".story-test-modal");
        StringAssert.Contains(css, ".story-test-option.is-correct");
    }

    [TestMethod]
    public void AdminStoryEditorPersistsStoryTestQuestions()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var catalog = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoryCatalogService.cs"));
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260520_story_test_questions.sql"));

        StringAssert.Contains(markup, "admin-story-editor-tabs");
        StringAssert.Contains(markup, "MudTabPanel Text='@T(\"Toets\", \"Test\")'");
        StringAssert.Contains(markup, "admin-story-test-editor admin-story-test-tab");
        StringAssert.Contains(markup, "BuildAdminStoryTestQuestions(StoryEditor.TestQuestions)");
        StringAssert.Contains(markup, "BuildAdminStoryTestQuestions(NewStoryEditor.TestQuestions)");
        Assert.IsLessThan(
            markup.IndexOf("admin-story-test-editor admin-story-test-tab", StringComparison.Ordinal),
            markup.IndexOf("MudTabPanel Text='@T(\"Toets\", \"Test\")'", StringComparison.Ordinal),
            "The story test editor should live inside the Test tab panel.");
        StringAssert.Contains(service, "\"test_questions\"");
        StringAssert.Contains(service, "retrying without test_questions");
        StringAssert.Contains(catalog, "TestQuestions: ReadStoryTestQuestions(row.TestQuestions)");
        StringAssert.Contains(catalog, "FetchPublishedRowsWithSelectAsync");
        StringAssert.Contains(migration, "add column if not exists test_questions jsonb");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            Path.GetDirectoryName(GetSourceFilePath())!,
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
