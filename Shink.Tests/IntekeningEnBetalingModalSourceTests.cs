using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class IntekeningEnBetalingModalSourceTests
{
    [TestMethod]
    public void CancelSubscriptionModal_RendersAccountActionErrorsInsideModal()
    {
        var pagePath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor");
        var source = File.ReadAllText(pagePath);

        var cancelModal = ExtractBetween(
            source,
            "@if (IsCancelSubscriptionModalOpen)",
            "@if (IsCloseAccountModalOpen)");

        StringAssert.Contains(cancelModal, "AccountActionStatusMessage");
        StringAssert.Contains(cancelModal, "role=\"@(AccountActionSucceeded ? \"status\" : \"alert\")\"");
    }

    [TestMethod]
    public void CloseAccountSection_RendersAfterPlanChoices()
    {
        var pagePath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor");
        var source = File.ReadAllText(pagePath);

        var plansIndex = source.IndexOf("<section class=\"billing-plans\"", StringComparison.Ordinal);
        var closeAccountIndex = source.IndexOf("<section class=\"billing-panel billing-danger-card\"", StringComparison.Ordinal);

        Assert.IsTrue(plansIndex >= 0, "Could not find the plan choices section.");
        Assert.IsTrue(closeAccountIndex >= 0, "Could not find the close account section.");
        Assert.IsTrue(closeAccountIndex > plansIndex, "Close account section should render after plan choices.");
    }

    [TestMethod]
    public void PlanCards_ReserveSavingsBadgeRowForAlignedButtons()
    {
        var pagePath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor");
        var cssPath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor.css");
        var source = File.ReadAllText(pagePath);
        var css = File.ReadAllText(cssPath);

        StringAssert.Contains(source, "billing-save-badge-placeholder");
        StringAssert.Contains(source, "aria-hidden=\"true\"");
        StringAssert.Contains(css, ".billing-save-badge-placeholder");
        StringAssert.Contains(css, "visibility: hidden");
    }

    private static string ExtractBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.IsTrue(startIndex >= 0, $"Could not find start marker: {start}");

        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.IsTrue(endIndex > startIndex, $"Could not find end marker: {end}");

        return source[startIndex..endIndex];
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail($"Could not find repository file: {Path.Combine(pathParts)}");
        return string.Empty;
    }
}
