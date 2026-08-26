using Shink.Mobile.Navigation;

namespace Shink.Tests;

[TestClass]
public sealed class MobileNotificationNavigationTests
{
    [TestMethod]
    public void StoryPublished_OpensStoryInsideApp()
    {
        var target = MobileNotificationNavigation.Resolve(
            "story_published",
            "/luister/die-ware-wenner");

        Assert.AreEqual(MobileNotificationNavigationKind.Story, target.Kind);
        Assert.AreEqual("die-ware-wenner", target.Value);
        Assert.AreEqual("luister", target.Source);
    }

    [TestMethod]
    public void StoryPublished_WithoutHref_FallsBackToInAppStoryList()
    {
        var target = MobileNotificationNavigation.Resolve("story_published", null);

        Assert.AreEqual(MobileNotificationNavigationKind.Story, target.Kind);
        Assert.IsNull(target.Value);
    }

    [TestMethod]
    public void CharacterUnlock_OpensUnlockedCharacterInsideApp()
    {
        var target = MobileNotificationNavigation.Resolve(
            "character_unlock",
            "https://schinkstories.com/karakters?karakter=knibbels%20junior");

        Assert.AreEqual(MobileNotificationNavigationKind.Character, target.Kind);
        Assert.AreEqual("knibbels junior", target.Value);
    }

    [TestMethod]
    public void CharacterUnlock_WithoutHref_StillOpensCharacterGallery()
    {
        var target = MobileNotificationNavigation.Resolve("character_unlock", null);

        Assert.AreEqual(MobileNotificationNavigationKind.Character, target.Kind);
        Assert.IsNull(target.Value);
    }

    [TestMethod]
    public void ResourcePublished_IsTheOnlyWebsiteNavigation()
    {
        var resource = MobileNotificationNavigation.Resolve(
            "resource_document_published",
            "/resources?tipe=aktiwiteite");
        var blog = MobileNotificationNavigation.Resolve("blog_published", "/blog/nuwe-blog");
        var unknown = MobileNotificationNavigation.Resolve("other", "https://example.com");

        Assert.AreEqual(MobileNotificationNavigationKind.ResourceWebsite, resource.Kind);
        Assert.AreEqual("/resources?tipe=aktiwiteite", resource.Value);
        Assert.AreEqual(MobileNotificationNavigationKind.None, blog.Kind);
        Assert.AreEqual(MobileNotificationNavigationKind.None, unknown.Kind);
    }
}
