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

        StringAssert.Contains(signup, "As jou kode geldig is, slaan ons PayStack vir nou oor en aktiveer jou toegang direk.");
        Assert.IsFalse(signup.Contains("slaan ons PayFast vir nou oor", StringComparison.Ordinal));
        StringAssert.Contains(css, ".signup-discount-code-field");
        StringAssert.Contains(css, "margin-top: 1rem;");
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
