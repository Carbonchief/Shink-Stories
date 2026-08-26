using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileTabletOrientationSourceTests
{
    [TestMethod]
    public void TabletOrientationIsNotLeftPortraitLockedAfterFullscreen()
    {
        var orientationService = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Services",
            "OrientationService.cs"));

        StringAssert.Contains(orientationService, "UIUserInterfaceIdiom.Pad");
        StringAssert.Contains(orientationService, "UIInterfaceOrientationMask.All");
        StringAssert.Contains(orientationService, "DeviceIdiom.Tablet");
        StringAssert.Contains(orientationService, "ScreenOrientation.Unspecified");
    }

    [TestMethod]
    public void IosManifestTargetsBothIPhoneAndIPad()
    {
        var infoPlist = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Platforms",
            "iOS",
            "Info.plist"));

        StringAssert.Contains(infoPlist, "<key>UIDeviceFamily</key>");
        StringAssert.Contains(infoPlist, "<integer>1</integer>");
        StringAssert.Contains(infoPlist, "<integer>2</integer>");
    }

    [TestMethod]
    public void HomeAndGratisRebuildTheirFiniteCollectionsAfterAResize()
    {
        var homePage = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "HomePage.cs"));
        var gratisPage = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "GratisPage.cs"));

        StringAssert.Contains(homePage, "HandleResponsiveSizeChanged");
        StringAssert.Contains(homePage, "RenderHome(_homeResponse)");
        StringAssert.Contains(gratisPage, "HandleResponsiveSizeChanged");
        StringAssert.Contains(gratisPage, "RenderStories(_response)");
    }

    [TestMethod]
    public void PlaylistDescriptionsStayCenteredBelowTheirTitles()
    {
        var luisterPage = File.ReadAllText(GetRepoPath(
            "Shink.Mobile",
            "Pages",
            "LuisterPage.cs"));
        var descriptionStart = luisterPage.IndexOf(
            "Text = playlist.Description,",
            StringComparison.Ordinal);

        Assert.IsTrue(descriptionStart >= 0);
        var descriptionEnd = luisterPage.IndexOf(
            "});",
            descriptionStart,
            StringComparison.Ordinal);
        var descriptionBlock = luisterPage.Substring(
            descriptionStart,
            descriptionEnd - descriptionStart);

        StringAssert.Contains(descriptionBlock, "HorizontalOptions = LayoutOptions.Fill");
        StringAssert.Contains(descriptionBlock, "HorizontalTextAlignment = TextAlignment.Center");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)}.");
    }
}
