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
        StringAssert.Contains(markup, "SelectStoryTestOption(questionIndex, \"A\")");
        StringAssert.Contains(markup, "SelectStoryTestOption(questionIndex, \"B\")");
        StringAssert.Contains(css, ".story-test-modal");
        StringAssert.Contains(css, ".story-test-option.is-correct");
    }

    [TestMethod]
    public void LuisterStoryTestRequiresAllAnswersBeforeScoring()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor.css"));

        StringAssert.Contains(markup, "IsStoryTestSubmitted");
        StringAssert.Contains(markup, "IsStoryTestReadyToCheck(CurrentStory)");
        StringAssert.Contains(markup, "CheckStoryTestAnswers");
        StringAssert.Contains(markup, "BuildStoryTestScoreText(CurrentStory)");
        StringAssert.Contains(markup, "var questionIndex = index;");
        StringAssert.Contains(markup, "SelectStoryTestOption(questionIndex, \"A\")");
        StringAssert.Contains(markup, "SelectStoryTestOption(questionIndex, \"B\")");
        StringAssert.Contains(markup, "BuildStoryTestOptionClass(question, selectedOption, \"A\", IsStoryTestSubmitted)");
        StringAssert.Contains(markup, "BuildStoryTestOptionClass(question, selectedOption, \"B\", IsStoryTestSubmitted)");
        StringAssert.Contains(markup, "story-test-option is-selected");
        StringAssert.Contains(markup, "return $\"Mooi probeer! Jy het {correctAnswers} uit {story.TestQuestions.Count} reg.\";");
        StringAssert.Contains(css, ".story-test-actions");
        StringAssert.Contains(css, ".story-test-check-btn");
        StringAssert.Contains(css, ".story-test-score");
        StringAssert.Contains(css, ".story-test-option.is-selected");
    }

    [TestMethod]
    public void LuisterStoryTestShowsThirdOptionOnlyWhenConfigured()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));
        var catalogModel = File.ReadAllText(GetRepoPath("Shink", "Components", "Content", "StoryCatalog.cs"));
        var catalogService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoryCatalogService.cs"));

        StringAssert.Contains(catalogModel, "[property: JsonPropertyName(\"option_c\")] string? OptionC = null");
        StringAssert.Contains(catalogService, "var optionC = ReadJsonString(item, \"option_c\");");
        StringAssert.Contains(catalogService, "normalizedCorrectOption is not (\"A\" or \"B\" or \"C\")");
        StringAssert.Contains(markup, "HasStoryTestOptionC(question)");
        StringAssert.Contains(markup, "SelectStoryTestOption(questionIndex, \"C\")");
        StringAssert.Contains(markup, "BuildStoryTestOptionClass(question, selectedOption, \"C\", IsStoryTestSubmitted)");
        StringAssert.Contains(markup, "@question.OptionC");
    }

    [TestMethod]
    public void LuisterStoryTestUsesPositiveYoungAudienceCopy()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));

        StringAssert.Contains(markup, "Vraag @(questionIndex + 1)");
        Assert.IsFalse(markup.Contains("van @CurrentStory.TestQuestions.Count", StringComparison.Ordinal));
        StringAssert.Contains(markup, "Mooi so!");
        StringAssert.Contains(markup, "Goeie poging! Kyk, die regte antwoord is gemerk.");
        StringAssert.Contains(markup, "Mooi probeer! Jy het {correctAnswers} uit {story.TestQuestions.Count} reg.");
        StringAssert.Contains(markup, "Jippie! Jy het alles reg! Fantastiese werk.");
        StringAssert.Contains(markup, "if (correctAnswers == story.TestQuestions.Count)");
        Assert.IsFalse(markup.Contains("Verkeerd", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(markup.Contains("Wrong", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LuisterStoryTestModalLocksBackgroundScroll()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor.css"));
        var appCss = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor.js"));

        StringAssert.Contains(markup, "SetStoryTestModalScrollLockAsync(true)");
        StringAssert.Contains(markup, "SetStoryTestModalScrollLockAsync(false)");
        StringAssert.Contains(script, "export function setStoryTestModalScrollLock(isLocked)");
        StringAssert.Contains(script, "story-test-modal-open");
        StringAssert.Contains(appCss, "body.story-test-modal-open");
        StringAssert.Contains(appCss, "position: fixed;");
        StringAssert.Contains(css, "overscroll-behavior: contain;");
        StringAssert.Contains(css, "-webkit-overflow-scrolling: touch;");
    }

    [TestMethod]
    public void LuisterStoryTestCelebratesPerfectScoresAndAnimatesCharacter()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor.css"));

        StringAssert.Contains(markup, "IsStoryTestPerfectScore(CurrentStory)");
        StringAssert.Contains(markup, "class=\"story-test-confetti\"");
        StringAssert.Contains(markup, "class=\"story-test-confetti-piece\"");
        StringAssert.Contains(css, ".story-test-confetti");
        StringAssert.Contains(css, "@keyframes story-test-confetti-explode");
        StringAssert.Contains(css, "@keyframes story-test-character-wiggle");
        StringAssert.Contains(css, "animation: story-test-character-wiggle");
        StringAssert.Contains(css, "background: transparent;");
        StringAssert.Contains(css, "box-shadow: none;");
        StringAssert.Contains(css, "@media (prefers-reduced-motion: reduce)");
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

    [TestMethod]
    public void AdminStoryEditorSupportsOptionalThirdTestOption()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var serviceContract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));

        StringAssert.Contains(markup, "Label='@T(\"Opsie C (opsioneel)\", \"Option C (optional)\")'");
        StringAssert.Contains(markup, "@bind-Value=\"question.OptionC\"");
        StringAssert.Contains(markup, "<MudSelectItem Value=\"@(\"C\")\">C</MudSelectItem>");
        StringAssert.Contains(markup, "NormalizeDirtyText(question.OptionC)");
        StringAssert.Contains(markup, "NormalizeStoryTestCorrectOption(question.CorrectOption, question.OptionC)");
        StringAssert.Contains(markup, "OptionC = question.OptionC");
        StringAssert.Contains(serviceContract, "[property: JsonPropertyName(\"option_c\")] string? OptionC = null");
        StringAssert.Contains(service, "var normalizedOptionC = NormalizeOptionalText(question.OptionC, 180);");
        StringAssert.Contains(service, "normalizedCorrectOption is not (\"A\" or \"B\" or \"C\")");
        StringAssert.Contains(service, "string.Equals(normalizedCorrectOption, \"C\", StringComparison.Ordinal)");
    }

    [TestMethod]
    public void AdminStoryTestRowsUseReadableCardLayout()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));

        StringAssert.Contains(markup, "class=\"admin-story-test-question-field\"");
        StringAssert.Contains(markup, "class=\"admin-story-test-options-grid\"");
        StringAssert.Contains(markup, "class=\"admin-story-test-row-actions\"");
        StringAssert.Contains(css, ".admin-story-test-row");
        StringAssert.Contains(css, "grid-template-columns: minmax(18rem, 0.95fr) minmax(0, 2.2fr) auto;");
        StringAssert.Contains(css, ".admin-story-test-options-grid");
        StringAssert.Contains(css, "grid-template-columns: repeat(3, minmax(9rem, 1fr)) minmax(7.5rem, 0.58fr);");
        StringAssert.Contains(css, ".admin-story-test-row-actions");
        StringAssert.Contains(css, "@media (max-width: 1180px)");
    }

    [TestMethod]
    public void AdminStoryEditorUpdatesStorySummaryCardDetails()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var serviceContract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));

        StringAssert.Contains(markup, "MudTabPanel Text='@T(\"Storiekaart\", \"Story card\")'");
        StringAssert.Contains(markup, "@bind-Value=\"StoryEditor.Synopsis\"");
        StringAssert.Contains(markup, "@bind-Value=\"StoryEditor.ValuesText\"");
        StringAssert.Contains(markup, "@bind-Value=\"StoryEditor.LessonsText\"");
        StringAssert.Contains(markup, "@bind-Value=\"StoryEditor.ConversationQuestionsText\"");
        StringAssert.Contains(markup, "@bind-Value=\"StoryEditor.CharactersText\"");
        StringAssert.Contains(markup, "BuildStorySummaryDetails(StoryEditor)");
        StringAssert.Contains(markup, "BuildStorySummaryDetails(NewStoryEditor)");
        StringAssert.Contains(serviceContract, "public sealed record AdminStorySummaryDetails");
        StringAssert.Contains(service, "\"metadata\"");
        StringAssert.Contains(service, "\"story_details\"");
        StringAssert.Contains(service, "BuildStoryMetadataPayload");
    }

    [TestMethod]
    public void AdminStoryCardTabUsesReadableFieldLayout()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));

        StringAssert.Contains(markup, "admin-story-card-editor admin-story-card-tab");
        StringAssert.Contains(markup, "admin-story-card-fields");
        StringAssert.Contains(markup, "admin-story-card-field is-synopsis");
        StringAssert.Contains(markup, "admin-story-card-field-label");
        StringAssert.Contains(markup, "Opsomming verskyn op storielyste en soekresultate; hou dit kort.");
        StringAssert.Contains(markup, "Description appears on story detail surfaces when there is room for more context.");
        StringAssert.Contains(markup, "Die storiekaart-sinopsis is vir die leerkaart se inhoud en kan anders wees as die algemene opsomming of beskrywing.");
        StringAssert.Contains(markup, "The story card synopsis is for the learning card content and can differ from the general summary or description.");
        StringAssert.Contains(markup, "Placeholder='@T(\"Kort sinopsis\", \"Short synopsis\")'");
        StringAssert.Contains(markup, "Placeholder='@T(\"Een waarde per lyn\", \"One value per line\")'");
        StringAssert.Contains(css, ".admin-story-card-fields");
        StringAssert.Contains(css, ".admin-story-card-field");
        StringAssert.Contains(css, ".admin-story-card-field-label");
        StringAssert.Contains(css, ".admin-story-card-editor ::deep textarea");
        StringAssert.Contains(css, ".admin-story-card-field.is-synopsis");
    }

    [TestMethod]
    public void AdminStoriesPanelExposesSoftDeleteAction()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var serviceContract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260521_story_soft_delete.sql"));

        StringAssert.Contains(markup, "SoftDeleteStoryAsync(context)");
        StringAssert.Contains(markup, "@T(\"Soft delete storie\", \"Soft delete story\")");
        StringAssert.Contains(markup, "fa-trash-can");
        StringAssert.Contains(serviceContract, "SoftDeleteStoryAsync");
        StringAssert.Contains(service, "public async Task<AdminOperationResult> SoftDeleteStoryAsync");
        StringAssert.Contains(service, "[\"status\"] = \"archived\"");
        StringAssert.Contains(migration, "add column if not exists deleted_at timestamptz");
        StringAssert.Contains(migration, "add column if not exists deleted_by_admin_email text");
    }

    [TestMethod]
    public void NewStoryPublishNotificationCanBeDisabled()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var serviceContract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));

        StringAssert.Contains(markup, "Label='@T(\"Stuur publiseer-kennisgewing\", \"Send publish notification\")'");
        StringAssert.Contains(markup, "@bind-Value=\"NewStoryEditor.SendPublishedNotification\"");
        StringAssert.Contains(markup, "SendPublishedNotification: NewStoryEditor.SendPublishedNotification");
        StringAssert.Contains(markup, "public bool SendPublishedNotification { get; set; } = true;");
        StringAssert.Contains(serviceContract, "bool SendPublishedNotification = true");
        StringAssert.Contains(service, "request.SendPublishedNotification");
        StringAssert.Contains(service, "ShouldCreatePublishedStoryNotifications");
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
