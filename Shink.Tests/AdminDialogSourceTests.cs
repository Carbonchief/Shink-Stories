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

    [TestMethod]
    public void AdminModalLoadingOverlayDoesNotBlockDialogCloseButtons()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));
        var globalCss = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, ".admin-modal-loading {");
        StringAssert.Contains(css, "pointer-events: none;");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-dialog-title .mud-icon-button");
        StringAssert.Contains(globalCss, "pointer-events: auto;");
        StringAssert.Contains(globalCss, "fill: currentColor !important;");
    }

    [TestMethod]
    public void NoHeaderAdminDialogsExposeExplicitCloseButtons()
    {
        var settingsMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor"));
        var charactersMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminCharactersPanel.razor"));

        StringAssert.Contains(settingsMarkup, "NoHeader = true");
        StringAssert.Contains(settingsMarkup, "CloseButton = false");
        StringAssert.Contains(settingsMarkup, "OnClick=\"CloseCodeDialog\"");
        StringAssert.Contains(settingsMarkup, "private void CloseCodeDialog()");

        StringAssert.Contains(charactersMarkup, "NoHeader = true");
        StringAssert.Contains(charactersMarkup, "CloseButton = false");
        StringAssert.Contains(charactersMarkup, "@onclick=\"CloseCharacterDialogAsync\"");
        StringAssert.Contains(charactersMarkup, "private async Task CloseCharacterDialogAsync()");
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
