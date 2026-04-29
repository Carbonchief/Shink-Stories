using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class IntekeningDangerActionStyleTests
{
    [TestMethod]
    public void BillingDangerActions_UseBlandButtonClasses()
    {
        var source = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "IntekeningEnBetaling.razor"));

        Assert.IsFalse(source.Contains("btn-outline-danger btn-lg billing-danger-btn", StringComparison.Ordinal));
        StringAssert.Contains(source, "btn-outline-secondary btn-lg billing-danger-btn");
    }

    [TestMethod]
    public void BillingDangerCss_RemovesRedTreatment()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "IntekeningEnBetaling.razor.css"))
            .ReplaceLineEndings("\n");

        StringAssert.Contains(css, ".billing-danger-card {\n    display: grid;\n    gap: 1rem;\n    background: linear-gradient(180deg, #ffffff 0%, #f8fbfb 100%);\n    border-color: rgba(33, 95, 104, 0.12);");
        StringAssert.Contains(css, ".billing-danger-btn {\n    min-height: 50px;");
        StringAssert.Contains(css, "body.schink-night-mode .billing-danger-card {\n    border-color: rgba(234, 229, 216, 0.14);");
        StringAssert.Contains(css, "body.schink-night-mode .billing-danger-btn {\n    color: #c5d0d4;");
        Assert.IsFalse(css.Contains("rgba(246, 122, 98, 0.42)", StringComparison.Ordinal));
        Assert.IsFalse(css.Contains("rgba(156, 54, 40, 0.55)", StringComparison.Ordinal));
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
