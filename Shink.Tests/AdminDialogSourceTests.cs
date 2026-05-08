using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminDialogSourceTests
{
    [TestMethod]
    public void EditorDialogsUseSolidAdminDialogClass()
    {
        var markup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor")));
        var globalCss = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css")));

        StringAssert.Contains(markup, "<MudDialog Visible=\"IsNewStoryDialogOpen\"\n                       VisibleChanged=\"OnNewStoryDialogVisibleChanged\"\n                       Class=\"admin-solid-dialog\"\n                       ContentClass=\"admin-solid-dialog-content\"\n                       Options=\"EditorDialogOptions\">");
        StringAssert.Contains(markup, "<MudDialog Visible=\"IsStoryDialogOpen\"\n                       VisibleChanged=\"OnStoryDialogVisibleChanged\"\n                       Class=\"admin-solid-dialog\"\n                       ContentClass=\"admin-solid-dialog-content\"\n                       Options=\"EditorDialogOptions\">");
        StringAssert.Contains(markup, "<MudDialog Visible=\"IsPlaylistDialogOpen\"\n                       VisibleChanged=\"OnPlaylistDialogVisibleChanged\"\n                       Class=\"admin-solid-dialog\"\n                       ContentClass=\"admin-solid-dialog-content\"\n                       Options=\"EditorDialogOptions\">");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-input-outlined");
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

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
