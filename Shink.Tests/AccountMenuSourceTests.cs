using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class AccountMenuSourceTests
{
    [TestMethod]
    public void AccountDropdown_DoesNotShowUnimplementedHelpCentreLink()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Layout", "MainLayout.razor"));
        var dropdownStart = markup.IndexOf("id=\"header-account-dropdown\"", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, dropdownStart, "Could not find the account dropdown.");

        var dropdownEnd = markup.IndexOf("</section>", dropdownStart, StringComparison.Ordinal);

        Assert.IsGreaterThan(dropdownStart, dropdownEnd, "Could not find the end of the account dropdown.");

        var dropdownMarkup = markup[dropdownStart..dropdownEnd];

        Assert.DoesNotContain("Hulp sentrum", dropdownMarkup);
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
