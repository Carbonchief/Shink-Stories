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
    public void MobileSettingsLinksToStoreCompliancePages()
    {
        var settings = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "SettingsPage.cs"));

        StringAssert.Contains(settings, "settings-privacy-row");
        StringAssert.Contains(settings, "OpenWebsiteAsync(\"/privaatheidsbeleid\")");
        StringAssert.Contains(settings, "settings-terms-row");
        StringAssert.Contains(settings, "OpenWebsiteAsync(\"/terme-en-voorwaardes\")");
        StringAssert.Contains(settings, "settings-delete-account-row");
        StringAssert.Contains(settings, "OpenWebsiteAsync(\"/rekening-verwydering\")");
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
