using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;

namespace Shink.Tests;

[TestClass]
public class SchoolOptionsSourceTests
{
    [TestMethod]
    public void PaymentPlanCatalogIncludesSchoolPackagesWithSlots()
    {
        var schoolPlans = PaymentPlanCatalog.SchoolPlans;

        Assert.AreEqual(3, schoolPlans.Count);
        Assert.IsTrue(schoolPlans.Any(plan => plan.Slug == "skool-klein-jaarliks" && plan.Amount == 6250.00m && plan.SchoolSlotLimit == 4));
        Assert.IsTrue(schoolPlans.Any(plan => plan.Slug == "skool-medium-jaarliks" && plan.Amount == 8640.00m && plan.SchoolSlotLimit == 6));
        Assert.IsTrue(schoolPlans.Any(plan => plan.Slug == "skool-groot-jaarliks" && plan.Amount == 11520.00m && plan.SchoolSlotLimit == 8));
        Assert.IsTrue(schoolPlans.All(plan => !plan.IsSubscription));
    }

    [TestMethod]
    public void SchoolPlansGrantAllStoriesAccess()
    {
        var allStoriesTierCodes = StoryAccessPolicy.GetAllowedTierCodes(StoryAccessRequirement.AllStoriesOnly);

        CollectionAssert.Contains(allStoriesTierCodes.ToList(), StoryAccessPolicy.SchoolSmallYearlyTierCode);
        CollectionAssert.Contains(allStoriesTierCodes.ToList(), StoryAccessPolicy.SchoolMediumYearlyTierCode);
        CollectionAssert.Contains(allStoriesTierCodes.ToList(), StoryAccessPolicy.SchoolLargeYearlyTierCode);
    }

    [TestMethod]
    public void OpsiesLinksToSkoolOpsiesBelowCompareSection()
    {
        var opsies = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Opsies.razor"));
        var compareIndex = opsies.IndexOf("opsies-compare", StringComparison.Ordinal);
        var schoolCtaIndex = opsies.IndexOf("opsies-school-cta", StringComparison.Ordinal);
        var faqIndex = opsies.IndexOf("opsies-faq", StringComparison.Ordinal);

        Assert.IsTrue(compareIndex >= 0);
        Assert.IsTrue(schoolCtaIndex > compareIndex);
        Assert.IsTrue(faqIndex > schoolCtaIndex);
        StringAssert.Contains(opsies, "href=\"/skoolopsies\"");
        StringAssert.Contains(opsies, "Sien skoolopsies");
    }

    [TestMethod]
    public void SkoolOpsiesUsesDocumentPackageCopy()
    {
        var skoolOpsies = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolOpsies.razor"));

        StringAssert.Contains(skoolOpsies, "@page \"/skoolopsies\"");
        StringAssert.Contains(skoolOpsies, "'n Gesonde skermtyd-alternatief vir die klaskamer.");
        StringAssert.Contains(skoolOpsies, "Graad RR tot Graad 3");
        StringAssert.Contains(skoolOpsies, "10-15 minute");
        StringAssert.Contains(skoolOpsies, "Elke skool ontvang ook 'n promosiekode vir ouers.");
    }

    [TestMethod]
    public void SkoolAdminProvidesDashboardSlotActions()
    {
        var skoolAdmin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "ISchoolManagementService.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var paystack = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackCheckoutService.cs"));

        StringAssert.Contains(skoolAdmin, "@page \"/skool-admin\"");
        StringAssert.Contains(skoolAdmin, "Nooi onderwyser");
        StringAssert.Contains(skoolAdmin, "Plekke gebruik");
        StringAssert.Contains(skoolAdmin, "Ek gebruik self 'n klaskamerplek");
        StringAssert.Contains(service, "InviteTeacherAsync");
        StringAssert.Contains(service, "UpdateAdminSeatUsageAsync");
        StringAssert.Contains(program, "GetSafeCheckoutReturnUrl(returnUrl, plan)");
        StringAssert.Contains(paystack, "ResolveCallbackPath(returnUrl)");
    }

    [TestMethod]
    public void SkoolAdminStatsUsesStoryTitlesInsteadOfSlugs()
    {
        var skoolAdmin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSchoolManagementService.cs"));
        var contract = File.ReadAllText(GetRepoPath("Shink", "Services", "ISchoolManagementService.cs"));

        StringAssert.Contains(skoolAdmin, "@story.StoryTitle");
        Assert.IsFalse(skoolAdmin.Contains("@story.StorySlug</strong>", StringComparison.Ordinal));
        StringAssert.Contains(service, "FetchStoryTitlesBySlugAsync");
        StringAssert.Contains(service, "?select=slug,title");
        StringAssert.Contains(contract, "string StoryTitle");
    }

    [TestMethod]
    public void SkoolAdminStatsLoadingUsesCenteredSpinner()
    {
        var skoolAdmin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor.css"));

        StringAssert.Contains(skoolAdmin, "school-admin-modal-loading");
        StringAssert.Contains(skoolAdmin, "school-admin-spinner");
        Assert.IsFalse(skoolAdmin.Contains("<p class=\"school-admin-empty\">Laai statistiek...</p>", StringComparison.Ordinal));
        StringAssert.Contains(css, ".school-admin-modal-loading");
        StringAssert.Contains(css, "place-items: center;");
        StringAssert.Contains(css, "@keyframes school-admin-spin");
    }

    [TestMethod]
    public void SkoolAdminStatsHeaderShowsSeatDetails()
    {
        var skoolAdmin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor.css"));

        StringAssert.Contains(skoolAdmin, "school-admin-modal-meta");
        StringAssert.Contains(skoolAdmin, "<dt>Status</dt>");
        StringAssert.Contains(skoolAdmin, "<dt>Rekening</dt>");
        StringAssert.Contains(skoolAdmin, "<dt>Toegang eindig</dt>");
        StringAssert.Contains(css, ".school-admin-modal-meta");
        StringAssert.Contains(css, "align-items: baseline;");
        StringAssert.Contains(css, "repeat(auto-fit, minmax(150px, 1fr))");
    }

    [TestMethod]
    public void SkoolAdminDoesNotRenderTopHero()
    {
        var skoolAdmin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor.css"));

        Assert.IsFalse(skoolAdmin.Contains("school-admin-hero", StringComparison.Ordinal));
        Assert.IsFalse(skoolAdmin.Contains("Hulpbron_blad_01.png", StringComparison.Ordinal));
        Assert.IsFalse(css.Contains("school-admin-hero", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SkoolAdminLongEmailUsesEllipsis()
    {
        var skoolAdmin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "SkoolAdmin.razor.css"));

        StringAssert.Contains(skoolAdmin, "class=\"school-admin-truncate\"");
        StringAssert.Contains(skoolAdmin, "title=\"@CurrentAdminEmail\"");
        StringAssert.Contains(css, ".school-admin-truncate");
        StringAssert.Contains(css, "text-overflow: ellipsis;");
        StringAssert.Contains(css, "white-space: nowrap;");
    }

    [TestMethod]
    public void AccountMenuOnlyShowsSkoolAdminLinkForSchoolPlans()
    {
        var layout = File.ReadAllText(GetRepoPath("Shink", "Components", "Layout", "MainLayout.razor"));

        StringAssert.Contains(layout, "@if (HasSchoolOption)");
        StringAssert.Contains(layout, "href=\"/skool-admin\"");
        StringAssert.Contains(layout, "HasSchoolAdminAccessAsync(email)");
    }

    [TestMethod]
    public void SchoolAdminAccessExcludesTeacherSeatEntitlements()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSchoolManagementService.cs"));

        StringAssert.Contains(service, "HasSchoolAdminAccessAsync");
        StringAssert.Contains(service, "source_system=neq.school_seat");
        StringAssert.Contains(service, "ResolveActiveSchoolPlanAsync(baseUri, apiKey, normalizedAdminEmail");
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
