using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileResponsiveLayoutSourceTests
{
    [TestMethod]
    public void PhoneLayoutsRestoreAnUnboundedMaximumWidth()
    {
        var responsiveLayout = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "MobileResponsiveLayout.cs"));

        StringAssert.Contains(
            responsiveLayout,
            "view.MaximumWidthRequest = double.PositiveInfinity;");
        Assert.IsFalse(
            responsiveLayout.Contains(
                "view.MaximumWidthRequest = -1;",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void StoryCollectionsUseTabletColumnsAndProportionalArtwork()
    {
        var responsiveLayout = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "MobileResponsiveLayout.cs"));
        var pageHelpers = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "PageHelpers.cs"));

        StringAssert.Contains(responsiveLayout, "ResolveStoryGridColumns");
        StringAssert.Contains(responsiveLayout, "ThreeColumnStoryBreakpoint = 960");
        StringAssert.Contains(responsiveLayout, "ResolveStoryCardArtworkHeight");
        StringAssert.Contains(pageHelpers, "var columns = MobileResponsiveLayout.ResolveStoryGridColumns(width);");
        StringAssert.Contains(pageHelpers, "artworkHeight: artworkHeight");
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
