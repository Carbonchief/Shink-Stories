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

    [TestMethod]
    public void NightModeToggle_ClosesContainingNavigationMenu()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Layout", "MainLayout.razor.js"));
        var handlerStart = script.IndexOf("function startNightModeDelegates()", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, handlerStart, "Could not find the night mode delegate.");

        var handlerEnd = script.IndexOf("nightModeDelegatesStarted = true;", handlerStart, StringComparison.Ordinal);

        Assert.IsTrue(handlerEnd > handlerStart, "Could not find the end of the night mode delegate.");

        var handlerScript = script[handlerStart..handlerEnd];

        Assert.Contains("setNightModeEnabled(shouldEnable, { persist: true });", handlerScript);
        Assert.Contains("const controlsContainer = toggle.closest(\".nav-controls, .guest-controls\");", handlerScript);
        Assert.Contains("closeNavMenuInContainer(controlsContainer);", handlerScript);
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
