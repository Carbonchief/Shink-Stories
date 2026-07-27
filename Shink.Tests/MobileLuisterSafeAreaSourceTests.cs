using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileLuisterSafeAreaSourceTests
{
    [TestMethod]
    public void LuisterScrollsEdgeToEdgeWhileKeepingTheTopBarInsideTheSafeArea()
    {
        var source = File.ReadAllText(FindLuisterPage());

        StringAssert.Contains(
            source,
            "Header = new Grid\n                {\n                    SafeAreaEdges = new SafeAreaEdges(\n                        SafeAreaRegions.None,\n                        SafeAreaRegions.Container,\n                        SafeAreaRegions.None,\n                        SafeAreaRegions.None),");
        StringAssert.Contains(
            source,
            "_content = new VerticalStackLayout\n            {\n                SafeAreaEdges = new SafeAreaEdges(\n                    SafeAreaRegions.None,\n                    SafeAreaRegions.Container,\n                    SafeAreaRegions.None,\n                    SafeAreaRegions.None),");
        StringAssert.Contains(
            source,
            "_scrollView = new ScrollView\n            {\n                SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(
            source,
            "_refreshView = new RefreshView\n        {\n            SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(
            source,
            "_rootLayout = new Grid\n        {\n            SafeAreaEdges = SafeAreaEdges.None,");
        StringAssert.Contains(
            source,
            "_topBarOverlay = new Grid\n        {\n            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),");
    }

    private static string FindLuisterPage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Shink.Mobile", "Pages", "LuisterPage.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate Shink.Mobile/Pages/LuisterPage.cs.");
    }
}
