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
    public void LuisterStoryAllowsSignedOutUsersToOpenFreeStories()
    {
        var luisterStory = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));

        StringAssert.Contains(luisterStory, "var canAccessCurrentStory = CurrentStory is not null && await HasAccessToStoryAsync(authState.User, CurrentStory);");
        StringAssert.Contains(luisterStory, "if (authState.User.Identity?.IsAuthenticated != true && !canAccessCurrentStory)");
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
