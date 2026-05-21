using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminGridStyleSourceTests
{
    [TestMethod]
    public void AdminMudTablesUseReadableRowAndColumnLines()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));
        var settingsCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor.css"));
        var globalCss = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "--admin-grid-line:");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-root");
        StringAssert.Contains(css, ".admin-page ::deep .mud-table-cell:not(:last-child)");
        StringAssert.Contains(css, ".admin-page ::deep tbody .mud-table-row:not(:last-child) > .mud-table-cell");
        StringAssert.Contains(css, ".admin-grid ::deep .mud-table-root");
        StringAssert.Contains(css, "border-collapse: separate;");
        StringAssert.Contains(css, ".admin-grid ::deep .mud-table-cell:not(:last-child)");
        StringAssert.Contains(css, "border-right: 1px solid var(--admin-grid-line);");
        StringAssert.Contains(css, ".admin-grid ::deep tbody .mud-table-row:not(:last-child) > .mud-table-cell");
        StringAssert.Contains(css, "border-bottom: 1px solid var(--admin-grid-line);");
        StringAssert.Contains(css, ".admin-grid ::deep .mud-sm-table .mud-table-cell");
        StringAssert.Contains(settingsCss, ".admin-settings-table ::deep .mud-table-cell:not(:last-child)");
        StringAssert.Contains(settingsCss, ".admin-settings-table ::deep tbody .mud-table-row:not(:last-child) > .mud-table-cell");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-table-cell:not(:last-child)");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog tbody .mud-table-row:not(:last-child) > .mud-table-cell");
    }

    [TestMethod]
    public void AdminGridSwitchesUseClearOnAndOffColors()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));
        var settingsCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor.css"));
        var globalCss = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));

        StringAssert.Contains(css, "--admin-toggle-on:");
        StringAssert.Contains(css, "--admin-toggle-off:");
        StringAssert.Contains(css, ".admin-page ::deep .mud-switch-base:not(.mud-checked)");
        StringAssert.Contains(css, ".admin-page ::deep .mud-switch-base.mud-checked");
        StringAssert.Contains(css, ".admin-page ::deep .mud-switch-base:not(.mud-checked) + .mud-switch-track");
        StringAssert.Contains(css, ".admin-page ::deep .mud-switch-base.mud-checked + .mud-switch-track");
        StringAssert.Contains(css, "color: var(--admin-toggle-off) !important;");
        StringAssert.Contains(css, "color: var(--admin-toggle-on) !important;");
        StringAssert.Contains(settingsCss, ".admin-section ::deep .mud-switch-base:not(.mud-checked)");
        StringAssert.Contains(settingsCss, ".admin-section ::deep .mud-switch-base.mud-checked");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-switch-base:not(.mud-checked)");
        StringAssert.Contains(globalCss, ".mud-dialog.admin-solid-dialog .mud-switch-base.mud-checked");
    }

    [TestMethod]
    public void AdminGridTogglesExposeStateTooltips()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var settingsMarkup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor"));

        StringAssert.Contains(markup, "<MudTooltip Text=\"@BuildPlaylistEnabledToggleTooltip(context)\"");
        StringAssert.Contains(markup, "<MudTooltip Text=\"@BuildPlaylistHomeToggleTooltip(context)\"");
        StringAssert.Contains(markup, "<MudTooltip Text=\"@BuildPlaylistShowcaseToggleTooltip(context)\"");
        StringAssert.Contains(markup, "<MudTooltip Text=\"@BuildPlaylistStoryShowcaseToggleTooltip(context.IsShowcase)\"");
        StringAssert.Contains(markup, "private string BuildPlaylistEnabledToggleTooltip(AdminPlaylistRecord playlist)");
        StringAssert.Contains(markup, "private string BuildPlaylistHomeToggleTooltip(AdminPlaylistRecord playlist)");
        StringAssert.Contains(markup, "private string BuildPlaylistShowcaseToggleTooltip(AdminPlaylistRecord playlist)");
        StringAssert.Contains(markup, "private string BuildPlaylistStoryShowcaseToggleTooltip(bool isShowcase)");
        StringAssert.Contains(markup, "Aktief - klik om af te skakel");
        StringAssert.Contains(markup, "Active - click to turn off");

        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildSignupCodeBypassTooltip()\"");
        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildTierActiveTooltip(context.IsActive)\"");
        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildOneUsePerUserTooltip()\"");
        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildBypassPaymentTooltip()\"");
        StringAssert.Contains(settingsMarkup, "<MudTooltip Text=\"@BuildCodeActiveTooltip()\"");
        StringAssert.Contains(settingsMarkup, "private string BuildSignupCodeBypassTooltip()");
        StringAssert.Contains(settingsMarkup, "private string BuildTierActiveTooltip(bool isActive)");
        StringAssert.Contains(settingsMarkup, "private string BuildCodeActiveTooltip()");
        StringAssert.Contains(settingsMarkup, "Aan - klik om af te skakel");
        StringAssert.Contains(settingsMarkup, "On - click to turn off");
    }

    [TestMethod]
    public void SubscriberSearchRunsWhenEnterIsPressed()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));

        StringAssert.Contains(markup, "@bind-Value=\"SubscriberSearch\"");
        StringAssert.Contains(markup, "OnKeyDown=\"HandleSubscriberSearchKeyDownAsync\"");
        StringAssert.Contains(markup, "private async Task HandleSubscriberSearchKeyDownAsync(KeyboardEventArgs args)");
        StringAssert.Contains(markup, "string.Equals(args.Key, \"Enter\", StringComparison.Ordinal)");
        StringAssert.Contains(markup, "await SearchSubscribersAsync();");
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
