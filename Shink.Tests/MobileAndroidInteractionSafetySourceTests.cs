using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class MobileAndroidInteractionSafetySourceTests
{
    [TestMethod]
    public void HapticFeedbackIsOptionalAndCannotTerminateMenuOrGameTaps()
    {
        var safeHaptics = ReadMobileSource("Services", "SafeHapticFeedback.cs");
        StringAssert.Contains(safeHaptics, "if (!hapticFeedback.IsSupported)");
        StringAssert.Contains(safeHaptics, "catch");
        StringAssert.Contains(safeHaptics, "return false;");

        foreach (var relativePath in new[]
                 {
                     new[] { "Pages", "MobileMenuSheet.cs" },
                     new[] { "Pages", "KarakterPareGamePage.cs" },
                     new[] { "Pages", "KarakterRaaiGamePage.cs" },
                     new[] { "Pages", "KaraktersPage.cs" },
                     new[] { "Pages", "MobileBottomBar.cs" }
                 })
        {
            var source = ReadMobileSource(relativePath);
            Assert.IsFalse(
                source.Contains("HapticFeedback.Default.Perform", StringComparison.Ordinal),
                $"Unsafe haptic call remains in {string.Join('/', relativePath)}.");
            StringAssert.Contains(source, "SafeHapticFeedback.TryPerform");
        }
    }

    [TestMethod]
    public void BothMenuNavigationPathsContainARecoveryBoundary()
    {
        var topBar = ReadMobileSource("Pages", "MobileTopBar.cs");
        var luister = ReadMobileSource("Pages", "LuisterPage.cs");

        StringAssert.Contains(topBar, "catch (Exception)");
        StringAssert.Contains(topBar, "Kon nie oopmaak nie");
        StringAssert.Contains(luister, "catch (Exception ex)");
        StringAssert.Contains(luister, "mobile_menu_navigation_failed");
        StringAssert.Contains(luister, "Kon nie oopmaak nie");
    }

    private static string ReadMobileSource(params string[] segments)
    {
        var path = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(path))
        {
            var candidate = Path.Combine([path, "Shink.Mobile", .. segments]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            path = Directory.GetParent(path)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException($"Could not find mobile source: {Path.Combine(segments)}");
    }
}
