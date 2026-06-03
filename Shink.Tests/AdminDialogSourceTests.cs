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
        StringAssert.Contains(markup, "<MudDialog Visible=\"IsStoryDialogOpen\"\n                       VisibleChanged=\"OnStoryDialogVisibleChanged\"\n                       Class=\"admin-solid-dialog admin-story-dialog\"\n                       ContentClass=\"admin-solid-dialog-content\"\n                       Options=\"StoryEditorDialogOptions\">");
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
        StringAssert.Contains(globalCss, "background-color: transparent !important;");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-dialog-title .mud-icon-button .mud-ripple");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-dialog-title .mud-icon-button .mud-icon-root");
        StringAssert.Contains(globalCss, "fill: currentColor !important;");
    }

    [TestMethod]
    public void SubscriberDialogCloseButtonIsPinnedTopRight()
    {
        var markup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor")));
        var globalCss = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css")));

        StringAssert.Contains(markup, "<MudDialog Visible=\"IsSubscriberDialogOpen\"\n                       VisibleChanged=\"OnSubscriberDialogVisibleChanged\"\n                       Class=\"admin-solid-dialog admin-subscriber-dialog\"\n                       ContentClass=\"admin-solid-dialog-content\"\n                       Options=\"SubscriberEditorDialogOptions\">");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog.admin-subscriber-dialog .mud-dialog-title .mud-icon-button,\n.mud-dialog.admin-solid-dialog.admin-subscriber-dialog .mud-dialog-title button.mud-icon-button");
        StringAssert.Contains(globalCss, "position: absolute !important;");
        StringAssert.Contains(globalCss, "right: 0.45rem;");
    }

    [TestMethod]
    public void SubscriberDialogActionButtonsHaveVisibleButtonChrome()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));

        StringAssert.Contains(markup, "Class=\"admin-icon-btn admin-action-btn\"");
        StringAssert.Contains(markup, "SendSubscriberPasswordResetAsync");
        StringAssert.Contains(markup, "ToggleSubscriberDisabledAsync");
        StringAssert.Contains(markup, "CancelSelectedPaidSubscriptionAsync");

        StringAssert.Contains(css, ".admin-solid-dialog ::deep .admin-action-grid .admin-action-btn");
        StringAssert.Contains(css, "border: 1px solid color-mix(in srgb, var(--admin-border-strong) 82%, #ffffff);");
        StringAssert.Contains(css, "background: color-mix(in srgb, var(--admin-surface-soft) 76%, #ffffff 8%);");
        StringAssert.Contains(css, ".admin-solid-dialog ::deep .admin-action-grid .admin-action-btn.mud-button-outlined-warning");
        StringAssert.Contains(css, ".admin-solid-dialog ::deep .admin-action-grid .admin-action-btn.mud-button-outlined-primary");
        StringAssert.Contains(css, ".admin-solid-dialog ::deep .admin-action-grid .admin-action-btn .admin-btn-text");
    }

    [TestMethod]
    public void StoryEditorDialogSupportsBackdropCloseWithDirtyConfirmation()
    {
        var markup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor")));

        StringAssert.Contains(markup, "<MudDialog Visible=\"IsStoryDialogOpen\"\n                       VisibleChanged=\"OnStoryDialogVisibleChanged\"\n                       Class=\"admin-solid-dialog admin-story-dialog\"\n                       ContentClass=\"admin-solid-dialog-content\"\n                       Options=\"StoryEditorDialogOptions\">");
        StringAssert.Contains(markup, "private static readonly DialogOptions StoryEditorDialogOptions = new()");
        StringAssert.Contains(markup, "BackdropClick = true");
        StringAssert.Contains(markup, "private async Task OnStoryDialogVisibleChanged(bool visible)");
        StringAssert.Contains(markup, "await CloseStoryDialogAsync();");
        StringAssert.Contains(markup, "if (!await ConfirmDiscardDialogChangesAsync(IsStoryEditorDirty()))");
        StringAssert.Contains(markup, "IsStoryDialogOpen = true;");
    }

    [TestMethod]
    public void StoryEditorCloseButtonIsPinnedTopRight()
    {
        var globalCss = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css")));

        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog.admin-story-dialog .mud-dialog-title,\n.mud-dialog.admin-solid-dialog.admin-subscriber-dialog .mud-dialog-title");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog.admin-story-dialog .mud-dialog-title .mud-icon-button,\n.mud-dialog.admin-solid-dialog.admin-story-dialog .mud-dialog-title button.mud-icon-button,\n.mud-dialog.admin-solid-dialog.admin-subscriber-dialog .mud-dialog-title .mud-icon-button,\n.mud-dialog.admin-solid-dialog.admin-subscriber-dialog .mud-dialog-title button.mud-icon-button");
        StringAssert.Contains(globalCss, "top: 0.45rem;");
        StringAssert.Contains(globalCss, "right: 0.45rem;");
    }

    [TestMethod]
    public void SolidAdminDialogsPinCloseButtonTopRightAndKeepIconVisible()
    {
        var globalCss = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css")));

        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-dialog-title {\n    position: relative !important;");
        StringAssert.Contains(globalCss, "padding-right: 3.5rem !important;");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-dialog-title .mud-icon-button,\n.mud-dialog.admin-solid-dialog .mud-dialog-title button.mud-icon-button {\n    position: absolute !important;");
        StringAssert.Contains(globalCss, "top: 0.45rem;");
        StringAssert.Contains(globalCss, "right: 0.45rem;");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-dialog-title button.mud-icon-button svg path");
        StringAssert.Contains(globalCss, "-webkit-text-fill-color: #eaf1f8 !important;");
    }

    [TestMethod]
    public void PlaylistDialogExposesSaveActionsInPersistentFooter()
    {
        var markup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor")));
        var playlistDialogIndex = markup.IndexOf("<MudDialog Visible=\"IsPlaylistDialogOpen\"", StringComparison.Ordinal);
        var playlistActionsIndex = markup.IndexOf("<DialogActions>", playlistDialogIndex, StringComparison.Ordinal);
        var playlistDialogEndIndex = markup.IndexOf("</MudDialog>", playlistActionsIndex, StringComparison.Ordinal);
        var playlistActions = markup[playlistActionsIndex..playlistDialogEndIndex];

        StringAssert.Contains(playlistActions, "OnClick=\"SavePlaylistAsync\"");
        StringAssert.Contains(playlistActions, "OnClick=\"SavePlaylistStoriesAsync\"");
        StringAssert.Contains(playlistActions, "admin-dialog-save-actions");
        StringAssert.Contains(playlistActions, "fa-solid fa-floppy-disk");
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

    [TestMethod]
    public void NoHeaderAdminDialogsExposeFooterSaveButtons()
    {
        var settingsMarkup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor")));
        var charactersMarkup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminCharactersPanel.razor")));

        StringAssert.Contains(settingsMarkup, "<DialogActions>\n                    <div class=\"admin-settings-dialog-actions-row\">");
        StringAssert.Contains(settingsMarkup, "OnClick=\"SaveCodeAsync\"");
        StringAssert.Contains(settingsMarkup, "OnClick=\"CloseCodeDialog\"");

        StringAssert.Contains(charactersMarkup, "<DialogActions>\n                <div class=\"characters-admin-dialog-actions-row\">");
        StringAssert.Contains(charactersMarkup, "@onclick=\"SaveCharacterAsync\"");
        StringAssert.Contains(charactersMarkup, "@onclick=\"CloseCharacterDialogAsync\"");
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
