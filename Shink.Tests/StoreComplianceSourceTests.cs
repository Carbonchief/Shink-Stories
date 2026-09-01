using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class StoreComplianceSourceTests
{
    [TestMethod]
    public void PublicSiteExposesPrivacyTermsAndAccountDeletionRoutes()
    {
        var privacy = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Privaatheidsbeleid.razor"));
        var terms = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "TermeEnVoorwaardes.razor"));
        var deletion = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "RekeningVerwydering.razor"));
        var footer = File.ReadAllText(GetRepoPath("Shink", "Components", "Layout", "MainLayout.razor"));

        StringAssert.Contains(privacy, "@page \"/privaatheidsbeleid\"");
        StringAssert.Contains(privacy, "@page \"/privacy-policy\"");
        StringAssert.Contains(terms, "@page \"/terme-en-voorwaardes\"");
        StringAssert.Contains(terms, "@page \"/terms-of-use\"");
        StringAssert.Contains(deletion, "@page \"/rekening-verwydering\"");
        StringAssert.Contains(deletion, "@page \"/account-deletion\"");
        StringAssert.Contains(footer, "href=\"/privaatheidsbeleid\"");
        StringAssert.Contains(footer, "href=\"/terme-en-voorwaardes\"");
        StringAssert.Contains(footer, "href=\"/rekening-verwydering\"");
    }

    [TestMethod]
    public void MobileSettingsProvidesDirectAuthenticatedAccountDeletion()
    {
        var settings = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SettingsPage.cs"));
        var mobileClient = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var migration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260901_account_personal_data_deletion.sql"));

        StringAssert.Contains(settings, "settings-privacy-row");
        StringAssert.Contains(settings, "OpenWebsiteAsync(\"/privaatheidsbeleid\")");
        StringAssert.Contains(settings, "settings-terms-row");
        StringAssert.Contains(settings, "OpenWebsiteAsync(\"/terme-en-voorwaardes\")");
        StringAssert.Contains(settings, "settings-delete-account-row");
        StringAssert.Contains(settings, "ConfirmDeleteAccountAsync");
        StringAssert.Contains(settings, "Verwyder permanent");
        StringAssert.Contains(settings, "settings-manage-subscription-row");
        StringAssert.Contains(settings, "https://apps.apple.com/account/subscriptions");
        StringAssert.Contains(settings, "rekeningverwydering kanselleer dit nie");
        StringAssert.Contains(mobileClient, "DeleteAccountAsync");
        StringAssert.Contains(mobileClient, "/api/mobile/account/delete");
        StringAssert.Contains(program, "MobileAccountDeletionRequest");
        StringAssert.Contains(program, "supabaseAuthService.DeleteUserAsync");
        StringAssert.Contains(migration, "delete_account_personal_data");
        StringAssert.Contains(migration, "Persoonlike data deur gebruiker verwyder.");
        StringAssert.Contains(migration, "private.wordpress_users");
        StringAssert.Contains(migration, "to service_role;");
        Assert.IsFalse(migration.Contains("Kanselleer asseblief eers jou aktiewe intekening", StringComparison.Ordinal));
    }

    private static string GetRepoPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)}.");
    }
}
