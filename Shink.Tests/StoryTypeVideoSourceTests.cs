using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class StoryTypeVideoSourceTests
{
    [TestMethod]
    public void VideoTypeIsPersistedAndSelectableInBothAdminEditors()
    {
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260713_story_type_video.sql"));
        var catalogService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoryCatalogService.cs"));
        var adminService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var adminMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));

        StringAssert.Contains(migration, "check (story_type in ('story', 'music', 'video'))");
        StringAssert.Contains(catalogService, "\"video\" => \"video\"");
        StringAssert.Contains(adminService, "\"video\" => \"video\"");
        Assert.AreEqual(
            2,
            CountOccurrences(adminMarkup, "<MudSelectItem Value=\"@(\"video\")\">@T(\"Video\", \"Video\")</MudSelectItem>"));
    }

    [TestMethod]
    public void VideoTypeMatchesMusicPlaylistExclusions()
    {
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260713_story_type_video.sql"));
        var storyMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));

        StringAssert.Contains(migration, "coalesce(s.story_type, 'story') not in ('music', 'video')");
        StringAssert.Contains(storyMarkup, "LuisterStories.Where(story => !IsExcludedFromRelatedStories(story))");
        StringAssert.Contains(storyMarkup, "story.StoryType.Equals(\"music\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(storyMarkup, "story.StoryType.Equals(\"video\", StringComparison.OrdinalIgnoreCase)");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
