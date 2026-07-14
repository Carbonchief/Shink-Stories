using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class StoryTypeMusicSourceTests
{
    [TestMethod]
    public void StoryTypeIsPersistedThroughCatalogAndAdminServices()
    {
        var contract = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var catalogModel = File.ReadAllText(GetRepoPath("Shink", "Components", "Content", "StoryCatalog.cs"));
        var catalogService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoryCatalogService.cs"));
        var adminService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260624_story_type_music.sql"));

        StringAssert.Contains(migration, "add column if not exists story_type text not null default 'story'");
        StringAssert.Contains(migration, "stories_story_type_check check (story_type in ('story', 'music'))");
        StringAssert.Contains(catalogModel, "string StoryType = \"story\"");
        StringAssert.Contains(contract, "string StoryType");
        StringAssert.Contains(catalogService, "audio_content_type,story_type,access_level");
        StringAssert.Contains(catalogService, "StoryType: NormalizeStoryType(row.StoryType)");
        StringAssert.Contains(catalogService, "[JsonPropertyName(\"story_type\")]");
        StringAssert.Contains(adminService, "[\"story_type\"] = normalizedStoryType");
        StringAssert.Contains(adminService, "NormalizeStoryType(request.StoryType, allowDefault: false)");
        StringAssert.Contains(adminService, "Storie tipe moet 'story', 'music' of 'video' wees.");
    }

    [TestMethod]
    public void AdminStoryEditorSupportsStoryTypeSelection()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));

        StringAssert.Contains(markup, "Label='@T(\"Storie tipe\", \"Story type\")'");
        StringAssert.Contains(markup, "@bind-Value=\"NewStoryEditor.StoryType\"");
        StringAssert.Contains(markup, "@bind-Value=\"StoryEditor.StoryType\"");
        StringAssert.Contains(markup, "<MudSelectItem Value=\"@(\"story\")\">@T(\"Storie\", \"Story\")</MudSelectItem>");
        StringAssert.Contains(markup, "<MudSelectItem Value=\"@(\"music\")\">@T(\"Musiek\", \"Music\")</MudSelectItem>");
        StringAssert.Contains(markup, "StoryType: NewStoryEditor.StoryType");
        StringAssert.Contains(markup, "StoryType: StoryEditor.StoryType");
        StringAssert.Contains(markup, "StoryType = NormalizeDirtyText(editor.StoryType)");
    }

    [TestMethod]
    public void LuisterMusicStoriesUseSquareCardVariant()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Luister.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Luister.razor.css"));

        StringAssert.Contains(markup, "BuildStoryCarouselItemClass(story, isWeeklyPopularPlaylist)");
        StringAssert.Contains(markup, "classes.Add(\"is-music\")");
        StringAssert.Contains(markup, "string.Equals(story?.StoryType, \"music\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(markup, "\"luister-playlist-showcase is-music\"");
        StringAssert.Contains(css, ".story-carousel-item.is-music .story-carousel-cover");
        StringAssert.Contains(css, ".story-carousel-item.is-music .story-carousel-image");
        StringAssert.Contains(css, ".luister-playlist-showcase.is-music .luister-playlist-showcase-cover");
        StringAssert.Contains(css, "aspect-ratio: 1 / 1;");
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
