using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public sealed class StoryPlaceholderSourceTests
{
    [TestMethod]
    public void WebAndMobileBundleTheSameSuppliedPlaceholderArtwork()
    {
        var webAsset = GetRepoPath("Shink", "wwwroot", "branding", "schink-placeholder.png");
        var mobileAsset = GetRepoPath("Shink.Mobile", "Resources", "Images", "schink_placeholder.png");

        Assert.IsTrue(File.Exists(webAsset));
        Assert.IsTrue(File.Exists(mobileAsset));
        CollectionAssert.AreEqual(File.ReadAllBytes(webAsset), File.ReadAllBytes(mobileAsset));
    }

    [TestMethod]
    public void StoryCatalogAndVisibleStorySurfacesUseTheCanonicalPlaceholder()
    {
        var catalog = File.ReadAllText(GetRepoPath("Shink", "Components", "Content", "StoryCatalog.cs"));
        var catalogService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseStoryCatalogService.cs"));
        var globalStyles = File.ReadAllText(GetRepoPath("Shink", "wwwroot", "app.css"));
        var storyPages = new[]
        {
            "Home.razor",
            "Luister.razor",
            "LuisterStory.razor",
            "LuisterPlaylist.razor",
            "LuisterPlaylistShowcase.razor",
            "Gratis.razor",
            "GratisStory.razor",
            "MyStories.razor",
            "Blog.razor",
            "BlogPost.razor",
            "Winkel.razor"
        };

        StringAssert.Contains(catalog, "public const string PlaceholderImagePath = \"/branding/schink-placeholder.png\";");
        StringAssert.Contains(catalogService, "StoryItem.PlaceholderImagePath");
        StringAssert.Contains(globalStyles, "img.schink-image-placeholder");
        StringAssert.Contains(globalStyles, "background: #146d69 !important;");

        foreach (var page in storyPages)
        {
            var source = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", page));
            StringAssert.Contains(source, "/branding/schink-placeholder.png", page);
        }
    }

    [TestMethod]
    public void MobileStoryArtworkUsesTheBundledPlaceholderWithItsTealBackground()
    {
        var helpers = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PageHelpers.cs"));
        var progressiveImage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "ProgressiveCachedImage.cs"));

        StringAssert.Contains(helpers, "internal const string StoryPlaceholderFile = \"schink_placeholder.png\";");
        StringAssert.Contains(helpers, "ResolveStoryFallbackFile(fallbackFile)");
        StringAssert.Contains(progressiveImage, "PlaceholderBackgroundColor = Color.FromArgb(\"#146D69\")");
        StringAssert.Contains(progressiveImage, "IsPlaceholderRequest(Request)");
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
