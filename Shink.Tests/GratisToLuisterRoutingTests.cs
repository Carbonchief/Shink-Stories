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
    public void LuisterTreatsStorieHoekiePlaylistStoriesAsStoryCornerStories()
    {
        var story = new StoryItem(
            Slug: "tiekie-tik-tik-tok",
            Title: "Tiekie Tik Tik Tok",
            Description: "Subscriber story",
            ImageFileName: "cover.jpg",
            AudioFileName: "Schink-_Stories_03_Tiekie_Tik_Tik_Tok.mp3",
            AccessLevel: "subscriber",
            PlaylistSlugs: ["storie-hoekie"]);

        Assert.AreEqual(StoryAccessRequirement.StoryCornerOrAllStories, StoryAccessPolicy.ResolveRequirement("luister", story));
    }

    [TestMethod]
    public void LuisterTreatsNonLimitedPlaylistStoriesAsAllStoriesOnly()
    {
        var story = new StoryItem(
            Slug: "bybel-storie",
            Title: "Bybel Storie",
            Description: "Subscriber story",
            ImageFileName: "cover.jpg",
            AudioFileName: "Schink-_Stories_Bybel.mp3",
            AccessLevel: "subscriber",
            PlaylistSlugs: ["bybelstories"]);

        Assert.AreEqual(StoryAccessRequirement.AllStoriesOnly, StoryAccessPolicy.ResolveRequirement("luister", story));
    }

    [TestMethod]
    public void LuisterUsesPlaylistMembershipOverLegacyStorieHoekieFilenameMarkers()
    {
        var story = new StoryItem(
            Slug: "non-storie-hoekie-story",
            Title: "Non Storie Hoekie Story",
            Description: "Subscriber story",
            ImageFileName: "cover.jpg",
            AudioFileName: "imported/stories/Storie_Hoekie_marker_in_old_filename.mp3",
            AccessLevel: "subscriber",
            PlaylistSlugs: ["bybelstories"]);

        Assert.AreEqual(StoryAccessRequirement.AllStoriesOnly, StoryAccessPolicy.ResolveRequirement("luister", story));
    }

    [TestMethod]
    public void OpsiesAlwaysRendersStorieHoekieCardWithDisabledState()
    {
        var opsies = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Opsies.razor"));

        Assert.IsFalse(opsies.Contains("@if (ShouldShowStoryCornerPlanOption)", StringComparison.Ordinal));
        StringAssert.Contains(opsies, "IsStoryCornerPlanUnavailable");
        StringAssert.Contains(opsies, "EnableStoryCornerPlan");
        StringAssert.Contains(opsies, "Jy is reeds op hierdie plan");
        StringAssert.Contains(opsies, "Hierdie storie is nie deel van Storie Hoekie");
    }

    [TestMethod]
    public void GratisRoutesRedirectToLuisterRoutes()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "static bool IsGratisPath(PathString path)");
        StringAssert.Contains(program, "string.Equals(value, \"/gratis\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(program, "static string ResolveGratisRedirectPath(PathString path, QueryString queryString)");
        StringAssert.Contains(program, "? \"/luister\"");
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
