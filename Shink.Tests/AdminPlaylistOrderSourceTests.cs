using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminPlaylistOrderSourceTests
{
    [TestMethod]
    public void PlaylistGridSupportsDraftOrderEditsAndSingleSave()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));
        var gridTools = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "js", "admin-grid-tools.js"));

        StringAssert.Contains(markup, "@T(\"Posisie\", \"Position\")");
        StringAssert.Contains(markup, "admin-playlist-order-move");
        StringAssert.Contains(markup, "MovePlaylistDraft(context.PlaylistId, -1)");
        StringAssert.Contains(markup, "MovePlaylistDraft(context.PlaylistId, 1)");
        StringAssert.Contains(markup, "CanMovePlaylistDraft(context.PlaylistId, -1)");
        StringAssert.Contains(markup, "CanMovePlaylistDraft(context.PlaylistId, 1)");
        StringAssert.Contains(markup, "HasUnsavedPlaylistOrderChanges");
        StringAssert.Contains(markup, "SavePlaylistOrderDraftAsync");
        StringAssert.Contains(markup, "DiscardPlaylistOrderDraft");
        StringAssert.Contains(markup, "BuildPlaylistOrderSaveIds()");
        StringAssert.Contains(markup, "RegisterLocationChangingHandler(OnAdminLocationChanging)");
        StringAssert.Contains(markup, "ConfirmDiscardPlaylistOrderChangesAsync");
        StringAssert.Contains(markup, "setBeforeUnloadGuard");
        Assert.IsFalse(
            markup.Contains("MovePlaylistAsync(context.PlaylistId", StringComparison.Ordinal),
            "The playlist grid should not save order changes one arrow click at a time.");
        Assert.IsFalse(
            markup.Contains("admin-playlist-order-input", StringComparison.Ordinal),
            "Playlist order should use arrow buttons instead of a numeric input.");
        StringAssert.Contains(css, ".admin-playlist-order-toolbar");
        StringAssert.Contains(css, ".admin-playlist-order-move");
        StringAssert.Contains(css, ".admin-playlist-order-dirty");
        StringAssert.Contains(gridTools, "export function setBeforeUnloadGuard");
        StringAssert.Contains(gridTools, "window.addEventListener(\"beforeunload\", beforeUnloadHandler)");
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
