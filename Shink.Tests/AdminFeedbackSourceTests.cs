using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class AdminFeedbackSourceTests
{
    [TestMethod]
    public void AdminFeedbackUsesTopRightSnackbarWithoutInlineBanner()
    {
        var markup = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor")));
        var program = NormalizeLineEndings(File.ReadAllText(GetRepoPath("Shink", "Program.cs")));

        StringAssert.Contains(markup, "<MudSnackbarProvider />");
        StringAssert.Contains(markup, "ShowToast(StatusMessage, Severity.Success);");
        StringAssert.Contains(markup, "ShowToast(ErrorMessage, Severity.Error);");
        StringAssert.Contains(program, "options.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopRight;");

        Assert.IsFalse(markup.Contains("class=\"admin-feedback is-error\"", StringComparison.Ordinal));
        Assert.IsFalse(markup.Contains("class=\"admin-feedback is-success\"", StringComparison.Ordinal));
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

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
