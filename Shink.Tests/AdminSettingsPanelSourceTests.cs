using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminSettingsPanelSourceTests
{
    [TestMethod]
    public void SettingsPanelUsesReadableDarkAdminStyles()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor.css"));

        StringAssert.Contains(markup, "PayStack te omseil");
        StringAssert.Contains(markup, "BuildSignupCodeBypassStateLabel()");
        StringAssert.Contains(markup, "BuildTierActiveStateLabel(tier.IsActive)");
        StringAssert.Contains(markup, "BuildCodeActiveStateLabel()");
        Assert.IsFalse(markup.Contains("Label=\"@BuildSignupCodeBypassStateLabel()\"", StringComparison.Ordinal));
        Assert.IsFalse(markup.Contains("Label=\"@BuildTierActiveStateLabel(tier.IsActive)\"", StringComparison.Ordinal));
        Assert.IsFalse(markup.Contains("Label=\"@T(\"Aktief\", \"Active\")\"", StringComparison.Ordinal));
        Assert.IsFalse(markup.Contains("PayFast te omseil", StringComparison.Ordinal));
        StringAssert.Contains(css, "--admin-settings-surface: #172631");
        StringAssert.Contains(css, "--admin-settings-text: #eaf1f8");
        StringAssert.Contains(css, ".admin-settings-card");
        StringAssert.Contains(css, ".admin-settings-toggle-row");
        StringAssert.Contains(css, ".admin-settings-table-toggle");
        StringAssert.Contains(css, ".admin-settings-toggle-state");
        StringAssert.Contains(css, ".is-enabled");
        StringAssert.Contains(css, ".is-disabled");
        StringAssert.Contains(css, ".admin-settings-table th");
        StringAssert.Contains(css, "::deep .mud-input-control");
        StringAssert.Contains(css, "::deep .mud-button-root");
    }

    [TestMethod]
    public void SettingsPanelFallsBackToAuthenticatedAdminWhenParentEmailIsMissing()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor"));

        StringAssert.Contains(markup, "@inject AuthenticationStateProvider AuthenticationStateProvider");
        StringAssert.Contains(markup, "ResolveAdminEmailAsync");
        StringAssert.Contains(markup, "AuthenticationStateProvider.GetAuthenticationStateAsync()");
        StringAssert.Contains(markup, "authState.User.FindFirst(ClaimTypes.Email)?.Value");
        StringAssert.Contains(markup, "await LoadAsync(effectiveAdminEmail);");
    }

    [TestMethod]
    public void AdminPagePassesLiveSettingsPanelParameters()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));

        StringAssert.Contains(markup, "<AdminSettingsPanel AdminEmail=\"@CurrentAdminEmail\" LanguageCode=\"@CurrentLanguageCode\" />");
        Assert.IsFalse(markup.Contains("<AdminSettingsPanel AdminEmail=\"CurrentAdminEmail\" LanguageCode=\"CurrentLanguageCode\" />", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SettingsPanelShowsParentCodesInListAndUsesDialogForSubcodes()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor.css"));

        StringAssert.Contains(markup, "ParentDiscountCodes");
        StringAssert.Contains(markup, "@foreach (var code in FilteredParentDiscountCodes)");
        StringAssert.Contains(markup, "OpenCodeDialog(code)");
        StringAssert.Contains(markup, "<MudDialog Visible=\"IsCodeDialogOpen\"");
        StringAssert.Contains(markup, "SelectedParentCodeSubcodes");
        StringAssert.Contains(markup, "StartNewSubcode");
        StringAssert.Contains(markup, "EditSubcode(subcode)");
        StringAssert.Contains(markup, "@T(\"Gebruikers\", \"Users\")");
        StringAssert.Contains(markup, "FormatSubcodeUsers(subcode)");
        StringAssert.Contains(css, ".admin-subcode-summary");
        StringAssert.Contains(css, ".admin-subcode-table");
        StringAssert.Contains(css, ".admin-subcode-users");
        StringAssert.Contains(css, "::deep .admin-settings-dialog");
        StringAssert.Contains(css, "max-height: calc(100dvh - 3rem);");
        StringAssert.Contains(css, "overflow-y: auto;");
        StringAssert.Contains(css, "overscroll-behavior: contain;");
        Assert.IsFalse(markup.Contains("@foreach (var code in FilteredDiscountCodes)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EditParentCodeDialogSectionsAreCollapsible()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor.css"));

        StringAssert.Contains(markup, "<details class=\"admin-code-section-disclosure\" open>");
        StringAssert.Contains(markup, "<details class=\"admin-code-section-disclosure\">");
        StringAssert.Contains(markup, "<summary class=\"admin-code-section-summary\">");
        StringAssert.Contains(markup, "@T(\"Kode besonderhede\", \"Code details\")");
        StringAssert.Contains(markup, "@T(\"Plan-koppeling\", \"Plan mapping\")");
        StringAssert.Contains(markup, "@T(\"Subkode lys\", \"Subcode list\")");
        StringAssert.Contains(markup, "@T(\"Vorige gebruike\", \"Previous uses\")");

        StringAssert.Contains(css, ".admin-code-section-disclosure");
        StringAssert.Contains(css, ".admin-code-section-summary");
        StringAssert.Contains(css, ".admin-code-section-body");
        StringAssert.Contains(css, ".admin-code-section-disclosure[open]");
        StringAssert.Contains(css, ".admin-code-section-disclosure[open] .admin-code-section-caret");
    }

    [TestMethod]
    public void EditParentCodeDialogCloseButtonIsExplicitButton()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor"));

        StringAssert.Contains(markup, "OnClick=\"CloseCodeDialog\"");
        StringAssert.Contains(markup, "ButtonType=\"ButtonType.Button\"");
        StringAssert.Contains(markup, "ClickPropagation=\"false\"");
        StringAssert.Contains(markup, "private void CloseCodeDialog()");
        StringAssert.Contains(markup, "IsCodeDialogOpen = false;");
        StringAssert.Contains(markup, "DiscountCodeEditor = null;");
    }

    [TestMethod]
    public void AdminActionButtonsUseSharedIconButtonPattern()
    {
        var admin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var settings = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor"));
        var store = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminStorePanel.razor"));
        var blog = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminBlogPanel.razor"));
        var adminCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));
        var settingsCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminSettingsPanel.razor.css"));
        var storeCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminStorePanel.razor.css"));
        var blogCss = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "AdminBlogPanel.razor.css"));

        StringAssert.Contains(admin, "Class=\"admin-icon-btn admin-action-btn");
        StringAssert.Contains(admin, "fa-solid fa-floppy-disk");
        StringAssert.Contains(admin, "fa-solid fa-xmark");
        StringAssert.Contains(admin, "fa-solid fa-rotate-right");
        StringAssert.Contains(admin, "fa-solid fa-plus");
        StringAssert.Contains(admin, "fa-solid fa-paper-plane");

        StringAssert.Contains(settings, "Class=\"admin-icon-btn admin-action-btn");
        StringAssert.Contains(settings, "admin-icon-only");
        StringAssert.Contains(settings, "fa-solid fa-floppy-disk");
        StringAssert.Contains(settings, "fa-solid fa-xmark");
        StringAssert.Contains(settings, "fa-solid fa-rotate-right");
        StringAssert.Contains(settings, "fa-solid fa-plus");
        StringAssert.Contains(settings, "fa-solid fa-trash");
        StringAssert.Contains(settings, "aria-label=");
        StringAssert.Contains(settings, "title=");
        Assert.IsFalse(settings.Contains("<span class=\"admin-btn-text\">@T(\"Stoor instellings\", \"Save settings\")</span>", StringComparison.Ordinal));
        Assert.IsFalse(settings.Contains("<span class=\"admin-btn-text\">@T(\"Stoor kode\", \"Save code\")</span>", StringComparison.Ordinal));
        Assert.IsFalse(settings.Contains("<span class=\"admin-btn-text\">@T(\"Maak toe\", \"Close\")</span>", StringComparison.Ordinal));

        StringAssert.Contains(store, "fa-solid fa-floppy-disk");
        StringAssert.Contains(store, "fa-solid fa-xmark");
        StringAssert.Contains(store, "fa-solid fa-trash");
        StringAssert.Contains(store, "admin-icon-only");
        StringAssert.Contains(blog, "fa-solid fa-floppy-disk");
        StringAssert.Contains(blog, "fa-solid fa-paper-plane");
        StringAssert.Contains(blog, "fa-solid fa-trash");
        StringAssert.Contains(blog, "admin-icon-only");

        StringAssert.Contains(adminCss, ".admin-action-btn");
        StringAssert.Contains(settingsCss, ".admin-icon-btn");
        StringAssert.Contains(settingsCss, ".admin-action-btn");
        StringAssert.Contains(settingsCss, ".admin-icon-only");
        StringAssert.Contains(storeCss, "display: inline-flex;");
        StringAssert.Contains(storeCss, ".admin-icon-only");
        StringAssert.Contains(blogCss, "display: inline-flex;");
        StringAssert.Contains(blogCss, ".admin-icon-only");
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
