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

    [TestMethod]
    public void VideoStoriesUseVideoUploadInsteadOfAudioUpload()
    {
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260811_story_video_media.sql"));
        var storageInterface = File.ReadAllText(GetRepoPath("Shink", "Services", "IStoryMediaStorageService.cs"));
        var adminService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));
        var adminMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));

        StringAssert.Contains(migration, "add column if not exists video_object_key");
        StringAssert.Contains(migration, "stories_published_requires_media");
        StringAssert.Contains(storageInterface, "UploadVideoAsync");
        StringAssert.Contains(adminService, "[\"video_object_key\"]");
        StringAssert.Contains(adminMarkup, "InputFile id=\"@NewStoryVideoInputId\"");
        StringAssert.Contains(adminMarkup, "OnChange=\"OnNewStoryVideoSelected\"");
        StringAssert.Contains(adminMarkup, "InputFile id=\"@EditStoryVideoInputId\"");
        StringAssert.Contains(adminMarkup, "OnChange=\"OnStoryVideoSelected\"");
        StringAssert.Contains(adminMarkup, "accept=\".mp4,.webm,video/mp4,video/webm\"");
        StringAssert.Contains(adminMarkup, "UploadVideoWithFallbackAsync");

        var newStoryVideoBranchStart = adminMarkup.IndexOf(
            "@if (IsVideoStoryType(NewStoryEditor.StoryType))",
            StringComparison.Ordinal);
        var newStoryVideoBranchEnd = adminMarkup.IndexOf("else", newStoryVideoBranchStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, newStoryVideoBranchStart);
        Assert.IsGreaterThan(newStoryVideoBranchStart, newStoryVideoBranchEnd);
        var newStoryVideoBranch = adminMarkup[newStoryVideoBranchStart..newStoryVideoBranchEnd];
        Assert.DoesNotContain("NewStoryAudioInputId", newStoryVideoBranch, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PublishedVideoStoriesUseProtectedR2PlaybackOnTheStoryPage()
    {
        var catalogModel = File.ReadAllText(GetRepoPath("Shink", "Components", "Content", "StoryCatalog.cs"));
        var catalogService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoryCatalogService.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var storyMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));

        StringAssert.Contains(catalogModel, "string? VideoObjectKey = null");
        StringAssert.Contains(catalogService, "? !string.IsNullOrWhiteSpace(row.VideoObjectKey)");
        StringAssert.Contains(catalogService, "VideoObjectKey: NormalizeOptionalText(row.VideoObjectKey)");
        StringAssert.Contains(program, "app.MapGet(\"/media/video/{slug}\"");
        StringAssert.Contains(program, "videoAccessService.IsTokenValid(slug, token)");
        StringAssert.Contains(program, "CreateVideoReadUrlAsync(");
        StringAssert.Contains(program, ".RequireRateLimiting(\"video-stream\")");
        StringAssert.Contains(storyMarkup, "@if (IsVideoStory(CurrentStory))");
        StringAssert.Contains(storyMarkup, "<video controls");
        StringAssert.Contains(storyMarkup, "src=\"@VideoUrl\"");
        StringAssert.Contains(storyMarkup, "VideoAccessService.CreateSignedVideoUrl(CurrentStory.Slug)");
    }

    [TestMethod]
    public void EditingVideoMediaCleansUpReplacedR2ObjectsAfterDatabaseSuccess()
    {
        var adminMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));

        StringAssert.Contains(adminMarkup, "var originalVideoObjectKey = StoryEditor.VideoObjectKey;");
        StringAssert.Contains(adminMarkup, "storyUpdateSucceeded = true;");
        StringAssert.Contains(adminMarkup, "CleanupReplacedStoryMediaObjectAsync(");
        StringAssert.Contains(adminMarkup, "StoryEditor.VideoObjectKey = null;");
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
