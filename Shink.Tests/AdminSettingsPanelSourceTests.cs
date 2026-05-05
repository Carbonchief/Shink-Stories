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
        Assert.IsFalse(markup.Contains("PayFast te omseil", StringComparison.Ordinal));
        StringAssert.Contains(css, "--admin-settings-surface: #172631");
        StringAssert.Contains(css, "--admin-settings-text: #eaf1f8");
        StringAssert.Contains(css, ".admin-settings-card");
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
