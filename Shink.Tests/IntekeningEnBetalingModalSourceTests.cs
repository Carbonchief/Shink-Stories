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
    public void CancelSubscriptionModal_RendersOptionalSurveyControlsAndSkipPath()
    {
        var pagePath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor");
        var source = File.ReadAllText(pagePath);

        var cancelModal = ExtractBetween(
            source,
            "@if (IsCancelSubscriptionModalOpen)",
            "@if (IsCloseAccountModalOpen)");

        StringAssert.Contains(cancelModal, "Hoekom kanselleer jy vandag?");
        StringAssert.Contains(cancelModal, "Dis te duur");
        StringAssert.Contains(cancelModal, "Ons gebruik dit nie genoeg nie");
        StringAssert.Contains(cancelModal, "My kind stel nie meer belang nie");
        StringAssert.Contains(cancelModal, "Ons kon nie die regte stories kry nie");
        StringAssert.Contains(cancelModal, "Tegniese of betalingsprobleme");
        StringAssert.Contains(cancelModal, "Ander rede");
        StringAssert.Contains(cancelModal, "Vertel ons meer");
        StringAssert.Contains(cancelModal, "CancelSubscriptionWithSurveyAsync");
        StringAssert.Contains(cancelModal, "SkipCancelSurveyAsync");
        StringAssert.Contains(cancelModal, "Hou intekening");
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
        StringAssert.Contains(css, "grid-template-rows: auto auto auto auto minmax(0, 1fr) auto");
        StringAssert.Contains(css, "align-self: end");
    }

    [TestMethod]
    public void PlanCards_ExcludeSchoolPlansFromBillingQuickOptions()
    {
        var pagePath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor");
        var source = File.ReadAllText(pagePath);

        StringAssert.Contains(source, "PaymentPlanCatalog.All.Where(plan => !plan.IsSchoolPlan)");
        StringAssert.Contains(source, "@foreach (var plan in BillingPlanChoices)");
        Assert.IsFalse(
            source.Contains("@foreach (var plan in PaymentPlanCatalog.All)", StringComparison.Ordinal),
            "The billing page should not render every catalog plan because that includes school options.");
    }

    [TestMethod]
    public void PlanCards_DisableAllCheckoutButtonsWhileSelectedPlanLoads()
    {
        var pagePath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor");
        var cssPath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor.css");
        var source = File.ReadAllText(pagePath);
        var css = File.ReadAllText(cssPath);

        StringAssert.Contains(source, "PendingPlanSlug");
        StringAssert.Contains(source, "IsPlanCheckoutPending");
        StringAssert.Contains(source, "StartPlanCheckoutAsync(plan)");
        StringAssert.Contains(source, "disabled=\"@IsPlanCheckoutPending\"");
        StringAssert.Contains(source, "billing-plan-button-spinner");
        StringAssert.Contains(source, "NavigationManager.NavigateTo(BuildPlanCheckoutPath(plan), forceLoad: true)");
        StringAssert.Contains(source, "private string BuildPlanCheckoutPath(PaymentPlan plan)");
        StringAssert.Contains(source, "var checkoutPath = $\"/betaal/{Uri.EscapeDataString(plan.Slug)}\";");
        StringAssert.Contains(css, ".billing-plan-select-btn:disabled");
        StringAssert.Contains(css, ".billing-plan-button-spinner");
        Assert.IsFalse(
            source.Contains("href=\"@($\"/betaal/{plan.Slug}\")\"", StringComparison.Ordinal),
            "Plan cards should use a guarded button click so repeated checkout navigation cannot be spammed.");
    }

    [TestMethod]
    public void ActivePaidStatus_CanOpenPaystackCardUpdate()
    {
        var pagePath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor");
        var source = File.ReadAllText(pagePath);

        StringAssert.Contains(source, "Werk kaartbesonderhede by");
        StringAssert.Contains(source, "IsOpeningCardUpdate");
        StringAssert.Contains(source, "OpenPaystackCardUpdateAsync");
        StringAssert.Contains(source, "CreatePaystackCardUpdateLinkAsync(UserEmail)");
    }

    [TestMethod]
    public void BillingDiscountCodes_CanApplyFreeAccessCodesWithoutPaystack()
    {
        var pagePath = FindRepositoryFile("Shink", "Components", "Pages", "IntekeningEnBetaling.razor");
        var programPath = FindRepositoryFile("Shink", "Program.cs");
        var source = File.ReadAllText(pagePath);
        var program = File.ReadAllText(programPath);

        StringAssert.Contains(source, "HasValidFreeAccessCodeForPlan(plan)");
        StringAssert.Contains(source, "Aktiveer met kode");
        StringAssert.Contains(source, "stop ons jou Paystack-betaling tydelik");
        StringAssert.Contains(source, "ApplyBillingDiscountCodeAsync(plan)");
        StringAssert.Contains(source, "\"/api/account/discount-code/apply\"");
        StringAssert.Contains(program, "app.MapPost(\"/api/account/discount-code/apply\"");
        StringAssert.Contains(program, "SubscriptionDiscountKinds.FreeAccess");
        StringAssert.Contains(program, "ApplySignupDiscountCodeAsync");
        StringAssert.Contains(program, "paymentPauseApplied");
        StringAssert.Contains(program, "Teken asseblief in om hierdie kode te gebruik.");
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
