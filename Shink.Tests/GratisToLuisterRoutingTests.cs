using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using Shink.Services;
using System.Collections;
using System.Reflection;
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
    public void CatalogGratisStoriesAreMarkedAsFreeForSignedAudioAccess()
    {
        Assert.IsTrue(StoryCatalog.All.Count > 0);

        foreach (var story in StoryCatalog.All)
        {
            Assert.AreEqual("free", story.AccessLevel, $"Gratis story {story.Slug} must stay free.");
            Assert.AreEqual(StoryAccessRequirement.Free, StoryAccessPolicy.ResolveRequirement("luister", story));
        }
    }

    [TestMethod]
    public void LegacyFallbackServesGratisAudioFromR2()
    {
        var fallbackRows = InvokeLegacyFallbackRows();
        var row = fallbackRows.FirstOrDefault(row =>
            string.Equals(ReadStringProperty(row, "Slug"), "suurlemoentjie", StringComparison.OrdinalIgnoreCase));

        Assert.IsNotNull(row, "Legacy fallback catalog should include Suurlemoentjie.");
        Assert.AreEqual("free", ReadStringProperty(row, "AccessLevel"));
        Assert.AreEqual("r2", ReadStringProperty(row, "AudioProvider"));
        Assert.AreEqual("pub-0696529d5a7d426882c0dcb881a7d08d.r2.dev", ReadStringProperty(row, "AudioBucket"));
        Assert.AreEqual("Suurlemoentjie.mpeg", ReadStringProperty(row, "AudioObjectKey"));
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
    public void GratisPaidUserRedirectsAreHandledByMiddlewareWithoutDuplicateEndpoint()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "static bool IsGratisPath(PathString path)");
        StringAssert.Contains(program, "string.Equals(value, \"/gratis\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(program, "static string ResolveGratisRedirectPath(PathString path, QueryString queryString)");
        StringAssert.Contains(program, "? \"/luister\"");
        StringAssert.Contains(program, "ResolveGratisRedirectPath(httpContext.Request.Path, httpContext.Request.QueryString)");
        Assert.IsFalse(
            program.Contains("app.MapGet(\"/gratis/{slug}\"", StringComparison.Ordinal),
            "Do not map a minimal /gratis/{slug} endpoint because it conflicts with the Razor /gratis/{Slug} page.");
    }

    [TestMethod]
    public void HomeGratisCtaSendsSignedOutUsersToSignupThenLuister()
    {
        var home = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Home.razor"));

        StringAssert.Contains(home, "private const string GratisSignupHref = \"/teken-op?returnUrl=%2Fluister\";");
        StringAssert.Contains(home, "<a class=\"cta cta-primary\" href=\"@GratisSignupHref\">Luister 3 Stories Gratis</a>");
    }

    [TestMethod]
    public void LuisterStoryAllowsFreeStoriesWithoutSubscription()
    {
        var luisterStory = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));
        var luister = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Luister.razor"));

        StringAssert.Contains(luisterStory, "if (requirement == StoryAccessRequirement.Free)");
        StringAssert.Contains(luisterStory, "return true;");
        StringAssert.Contains(luister, "StoryAccessRequirement.Free => true");
        Assert.IsFalse(
            luisterStory.Contains("HasAnyActiveStorySubscriptionAsync", StringComparison.Ordinal),
            "Free stories on /luister/{slug} should not require a gratis or paid subscription row.");
    }

    [TestMethod]
    public void SignedAudioEndpointChecksCurrentSubscriptionBeforeServingStoryAudio()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "ISubscriptionLedgerService subscriptionLedgerService");
        StringAssert.Contains(program, "var requirement = StoryAccessPolicy.ResolveRequirement(\"luister\", story);");
        StringAssert.Contains(program, "var hasStoryAccess = await HasRequiredStoryAccessAsync(");
        StringAssert.Contains(program, "if (requirement == StoryAccessRequirement.Free)");
        StringAssert.Contains(program, "return true;");
        StringAssert.Contains(program, "return httpContext.User.Identity?.IsAuthenticated == true");
    }

    [TestMethod]
    public void GratisStoryPageDoesNotRedirectFreeStoriesAwayFromPlayback()
    {
        var gratisStory = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor"));

        StringAssert.Contains(gratisStory, "AudioAccessService.CreateSignedAudioUrl(CurrentStory.Slug)");
        Assert.IsFalse(
            gratisStory.Contains("NavigateTo($\"/opsies?returnUrl={returnUrl}\")", StringComparison.Ordinal),
            "The dedicated gratis player should render the signed free-story audio URL instead of redirecting away.");
        Assert.IsFalse(
            gratisStory.Contains("IsRedirectingToOpsies", StringComparison.Ordinal),
            "The dedicated gratis player should not show the paid-options redirect state.");
    }

    [TestMethod]
    public void SignedAudioEndpointUsesR2SignedReadUrlsForBareHttpsHostConfiguration()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var storageService = File.ReadAllText(GetRepoPath("Shink", "Services", "CloudflareR2StoryMediaStorageService.cs"));

        StringAssert.Contains(program, "storyMediaStorageService.CreateAudioReadUrlAsync(");
        StringAssert.Contains(program, "story.AudioBucket");
        StringAssert.Contains(program, "Results.Redirect(readUri.ToString(), permanent: false, preserveMethod: true)");
        StringAssert.Contains(storageService, "private static bool TryBuildHttpsBaseUri(string? value, out Uri publicBaseUri)");
        StringAssert.Contains(storageService, "candidate = $\"https://{candidate.TrimStart('/')}\";");
        StringAssert.Contains(storageService, "TryBuildPublicReferenceUri(bucket, out var bucketPublicBaseUri)");
        StringAssert.Contains(storageService, "ResolveReadBucketName(bucket)");
        StringAssert.Contains(program, "TryBuildHttpsPublicBaseUri(options.PublicBaseUrl, out var publicBaseUri)");
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

    private static IReadOnlyList<object> InvokeLegacyFallbackRows()
    {
        var method = typeof(SupabaseStoryCatalogService).GetMethod(
            "BuildLegacyFallbackRows",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method, "Could not find the legacy fallback catalog builder.");
        var rows = method.Invoke(null, null);
        Assert.IsNotNull(rows);
        return ((IEnumerable)rows).Cast<object>().ToArray();
    }

    private static string? ReadStringProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.IsNotNull(property, $"Could not find property {propertyName}.");
        return property.GetValue(instance) as string;
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
