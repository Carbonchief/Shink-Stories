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
