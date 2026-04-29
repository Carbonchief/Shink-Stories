using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class GratisToLuisterRoutingTests
{
    [TestMethod]
    public void LuisterTreatsFreeAccessLevelStoriesAsFree()
    {
        var freeStory = new StoryItem(
            Slug: "gratis-storie",
            Title: "Gratis Storie",
            Description: "Gratis",
            ImageFileName: "cover.jpg",
            AudioFileName: "audio.mp3",
            AccessLevel: "free");

        var paidStory = freeStory with
        {
            Slug = "betaalde-storie",
            AccessLevel = "subscriber"
        };

        Assert.AreEqual(StoryAccessRequirement.Free, StoryAccessPolicy.ResolveRequirement("luister", freeStory));
        Assert.AreNotEqual(StoryAccessRequirement.Free, StoryAccessPolicy.ResolveRequirement("luister", paidStory));
    }

    [TestMethod]
    public void GratisRoutesRedirectToLuisterRoutes()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "app.MapGet(\"/gratis\"");
        StringAssert.Contains(program, "Results.Redirect(\"/luister\"");
        StringAssert.Contains(program, "app.MapGet(\"/gratis/{slug}\"");
        StringAssert.Contains(program, "$\"/luister/{Uri.EscapeDataString(slug.Trim())}\"");
    }

    [TestMethod]
    public void HomeGratisCtaSendsSignedOutUsersToSignupThenLuister()
    {
        var home = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Home.razor"));

        StringAssert.Contains(home, "private const string GratisSignupHref = \"/teken-op?returnUrl=%2Fluister\";");
        StringAssert.Contains(home, "<a class=\"cta cta-primary\" href=\"@GratisSignupHref\">Luister 3 Stories Gratis</a>");
    }

    [TestMethod]
    public void LuisterStoryRequiresSubscriptionForFreeStories()
    {
        var luisterStory = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));

        StringAssert.Contains(luisterStory, "if (requirement == StoryAccessRequirement.Free)");
        StringAssert.Contains(luisterStory, "return await HasAnyActiveStorySubscriptionAsync(email);");
    }

    [TestMethod]
    public void SignedAudioEndpointChecksCurrentSubscriptionBeforeServingStoryAudio()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "ISubscriptionLedgerService subscriptionLedgerService");
        StringAssert.Contains(program, "var requirement = StoryAccessPolicy.ResolveRequirement(\"luister\", story);");
        StringAssert.Contains(program, "var hasStoryAccess = await HasRequiredStoryAccessAsync(");
        StringAssert.Contains(program, "return httpContext.User.Identity?.IsAuthenticated == true");
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
