using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class SignupSourceTests
{
    [TestMethod]
    public void SignupDiscountCodeValidationStaysInsideEditForm()
    {
        var signup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Signup.razor"));

        var editFormStart = signup.IndexOf("<EditForm FormName=\"signup-form\"", StringComparison.Ordinal);
        var discountField = signup.IndexOf("signup-discount-code-field", StringComparison.Ordinal);
        var editFormEnd = signup.IndexOf("</EditForm>", StringComparison.Ordinal);

        Assert.IsTrue(editFormStart >= 0);
        Assert.IsTrue(discountField > editFormStart);
        Assert.IsTrue(editFormEnd > discountField);
        StringAssert.Contains(signup, "For=\"@(() => SignUpForm.DiscountCode)\"");
    }

    [TestMethod]
    public void SignupDiscountCodeHelpUsesPaystackCopyAndHasSpacing()
    {
        var signup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Signup.razor"));
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Signup.razor.css"));

        StringAssert.Contains(signup, "As jou kode geldig is, wys ons die afslag voor jy met Paystack betaal.");
        StringAssert.Contains(signup, "placeholder=\"Voer promosiekode in\"");
        Assert.IsFalse(signup.Contains("placeholder=\"Voer jou kode in\"", StringComparison.Ordinal));
        Assert.IsFalse(signup.Contains("slaan ons PayFast vir nou oor", StringComparison.Ordinal));
        StringAssert.Contains(css, ".signup-discount-code-field");
        StringAssert.Contains(css, ".signup-discount-preview");
        StringAssert.Contains(css, "margin-top: 1rem;");
    }

    [TestMethod]
    public void SignupDiscountPreviewRevalidatesCodeAndShowsPriceChange()
    {
        var signup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Signup.razor"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var js = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "js", "auth-browser.js"));

        StringAssert.Contains(program, "/api/auth/signup/discount-preview");
        StringAssert.Contains(signup, "PreviewDiscountCodeAsync");
        StringAssert.Contains(signup, "await PreviewDiscountCodeAsync()");
        StringAssert.Contains(signup, "DiscountPreview");
        StringAssert.Contains(signup, "@GetSelectedPlanPriceDisplay()");
        StringAssert.Contains(signup, "Gewone prys");
        StringAssert.Contains(signup, "Afslag");
        StringAssert.Contains(signup, "Jou prys");
        StringAssert.Contains(signup, "RemoveDiscountCode");
        StringAssert.Contains(js, "postJson");
    }

    [TestMethod]
    public void OpsiesDiscountCodeTravelsThroughSignupAndCheckout()
    {
        var opsies = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Opsies.razor"));
        var signup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Signup.razor"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(opsies, "id=\"opsies-discount-code\"");
        StringAssert.Contains(opsies, "[SupplyParameterFromQuery(Name = \"discountCode\")]");
        StringAssert.Contains(opsies, "checkoutQuery[\"discountCode\"] = normalizedDiscountCode;");
        StringAssert.Contains(opsies, "signUpQuery[\"discountCode\"] = normalizedDiscountCode;");

        StringAssert.Contains(signup, "[SupplyParameterFromQuery(Name = \"discountCode\")]");
        StringAssert.Contains(signup, "SignUpForm.DiscountCode = DiscountCode.Trim();");
        StringAssert.Contains(signup, "query[\"discountCode\"] = SignUpForm.DiscountCode.Trim();");

        StringAssert.Contains(program, "PreviewSignupDiscountCodeAsync(");
        StringAssert.Contains(program, "ApplySignupDiscountCodeAsync(");
        StringAssert.Contains(program, "BuildSubscriptionPaymentRedirectPath(\"kode-toegepas\"");
        StringAssert.Contains(program, "BuildSubscriptionPaymentRedirectPath(\"kode-betaalplan\"");
    }

    [TestMethod]
    public void SignupMembershipSelectorSwitchesPlanListBySchoolSource()
    {
        var signup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Signup.razor"));

        StringAssert.Contains(signup, "[SupplyParameterFromQuery(Name = \"source\")]");
        StringAssert.Contains(signup, "IsSchoolOptionsSignupFlow ? PaymentPlanCatalog.SchoolPlans : HouseholdPlans");
        StringAssert.Contains(signup, "IsSchoolOptionsSignupFlow = IsSchoolOptionsSource(Source) || SelectedPlan?.IsSchoolPlan == true;");
        StringAssert.Contains(signup, "PaymentPlanCatalog.All.Where(plan => !plan.IsSchoolPlan).ToArray()");
        StringAssert.Contains(signup, "@foreach (var plan in AvailablePlans)");
        Assert.IsFalse(
            signup.Contains("private IReadOnlyList<PaymentPlan> AvailablePlans => PaymentPlanCatalog.All;", StringComparison.Ordinal),
            "The signup plan selector should not render every catalog plan because that mixes household and school options.");
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
