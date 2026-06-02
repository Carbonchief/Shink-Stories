using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminStoryPlaylistAssignmentSourceTests
{
    [TestMethod]
    public void StoryEditorCanAssignStoriesToPlaylists()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));

        StringAssert.Contains(markup, "admin-story-playlist-selector");
        StringAssert.Contains(markup, "ManualPlaylistsForStoryAssignment");
        StringAssert.Contains(markup, "NewStoryEditor.SelectedPlaylistIds");
        StringAssert.Contains(markup, "StoryEditor.SelectedPlaylistIds");
        StringAssert.Contains(markup, "ToggleNewStoryPlaylist");
        StringAssert.Contains(markup, "ToggleStoryPlaylist");
        StringAssert.Contains(markup, "SaveStoryPlaylistAssignmentsAsync(createdStoryId, NewStoryEditor.SelectedPlaylistIds)");
        StringAssert.Contains(markup, "SaveStoryPlaylistAssignmentsAsync(StoryEditor.StoryId, StoryEditor.SelectedPlaylistIds)");
        StringAssert.Contains(markup, "@T(\"Kies die admin-bestuurde playlists waarin hierdie storie moet verskyn.\", \"Choose the admin-managed playlists this story should appear in.\")");
        StringAssert.Contains(css, ".admin-story-playlist-options");
        StringAssert.Contains(css, "grid-template-columns: repeat(2, minmax(0, 1fr));");
    }

    [TestMethod]
    public void PlaylistStorySaveDeletesExistingRowsBeforeEmptySuccess()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseAdminManagementService.cs"));

        var methodIndex = service.IndexOf("public async Task<AdminOperationResult> SavePlaylistStoriesAsync", StringComparison.Ordinal);
        var deleteIndex = service.IndexOf("var deleteUri = new Uri(baseUri, $\"rest/v1/story_playlist_items?playlist_id=eq.{escapedPlaylistId}\");", methodIndex, StringComparison.Ordinal);
        var emptySuccessIndex = service.IndexOf("if (normalizedStories.Length == 0)", methodIndex, StringComparison.Ordinal);

        Assert.AreNotEqual(-1, methodIndex);
        Assert.AreNotEqual(-1, deleteIndex);
        Assert.AreNotEqual(-1, emptySuccessIndex);
        Assert.IsTrue(deleteIndex < emptySuccessIndex);
        StringAssert.Contains(service, "return new AdminOperationResult(true);");
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
