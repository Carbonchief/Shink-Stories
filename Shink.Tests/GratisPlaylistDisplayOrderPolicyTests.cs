using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using Shink.Mobile.Models;

namespace Shink.Tests;

[TestClass]
public class GratisPlaylistDisplayOrderPolicyTests
{
    [TestMethod]
    public void GratisOnlyUsersSeeGratisPlaylistInSecondPositionWhenItWasLower()
    {
        var playlists = new[]
        {
            CreatePlaylist("bybelstories", "Bybelstories"),
            CreatePlaylist("storie-hoekie", "Storie Hoekie"),
            CreatePlaylist(StoryAccessPolicy.GratisPlaylistSlug, "Gratis stories"),
            CreatePlaylist("slaaptyd", "Slaaptyd")
        };

        var reordered = GratisPlaylistDisplayOrderPolicy.MoveGratisPlaylistNearTop(
            playlists,
            isGratisOnlyUser: true,
            static playlist => playlist.Slug);

        CollectionAssert.AreEqual(
            new[]
            {
                "bybelstories",
                StoryAccessPolicy.GratisPlaylistSlug,
                "storie-hoekie",
                "slaaptyd"
            },
            reordered.Select(playlist => playlist.Slug).ToArray());
    }

    [TestMethod]
    public void GratisOnlyUsersKeepGratisPlaylistFirstWhenAlreadyFirst()
    {
        var playlists = new[]
        {
            CreatePlaylist(StoryAccessPolicy.GratisPlaylistSlug, "Gratis stories"),
            CreatePlaylist("bybelstories", "Bybelstories"),
            CreatePlaylist("slaaptyd", "Slaaptyd")
        };

        var reordered = GratisPlaylistDisplayOrderPolicy.MoveGratisPlaylistNearTop(
            playlists,
            isGratisOnlyUser: true,
            static playlist => playlist.Slug);

        CollectionAssert.AreEqual(
            playlists.Select(playlist => playlist.Slug).ToArray(),
            reordered.Select(playlist => playlist.Slug).ToArray());
    }

    [TestMethod]
    public void PaidUsersKeepAdminConfiguredPlaylistOrder()
    {
        var playlists = new[]
        {
            CreatePlaylist("bybelstories", "Bybelstories"),
            CreatePlaylist("storie-hoekie", "Storie Hoekie"),
            CreatePlaylist(StoryAccessPolicy.GratisPlaylistSlug, "Gratis stories")
        };

        var reordered = GratisPlaylistDisplayOrderPolicy.MoveGratisPlaylistNearTop(
            playlists,
            isGratisOnlyUser: false,
            static playlist => playlist.Slug);

        CollectionAssert.AreEqual(
            playlists.Select(playlist => playlist.Slug).ToArray(),
            reordered.Select(playlist => playlist.Slug).ToArray());
    }

    [TestMethod]
    public void GratisOnlyUsersCanPromoteGratisSectionWithoutDisturbingLeadingSystemBlock()
    {
        var sections = new[]
        {
            new PlaylistSection("speellyste"),
            new PlaylistSection("slaaptyd"),
            new PlaylistSection(StoryAccessPolicy.GratisPlaylistSlug),
            new PlaylistSection("bybelstories")
        };

        var reordered = GratisPlaylistDisplayOrderPolicy.MoveGratisPlaylistNearTop(
            sections,
            isGratisOnlyUser: true,
            static section => section.PlaylistSlug);

        CollectionAssert.AreEqual(
            new[]
            {
                "speellyste",
                StoryAccessPolicy.GratisPlaylistSlug,
                "slaaptyd",
                "bybelstories"
            },
            reordered.Select(section => section.PlaylistSlug).ToArray());
    }

    [TestMethod]
    [DataRow(false, true, 2)]
    [DataRow(true, true, 0)]
    [DataRow(true, false, 1)]
    public void FirstVisitOnlyPromotesTheConfiguredGratisSection(bool gratisOnly, bool firstVisit, int expectedIndex)
    {
        var configured = new[] { "speellyste", "slaaptyd", StoryAccessPolicy.GratisPlaylistSlug, "bybelstories" };
        var sections = GratisPlaylistDisplayOrderPolicy.MoveGratisPlaylistNearTop(
            configured, gratisOnly, static slug => slug, firstVisit);

        Assert.AreEqual(StoryAccessPolicy.GratisPlaylistSlug, sections[expectedIndex]);
        CollectionAssert.AreEqual(new[] { "speellyste", "slaaptyd", "bybelstories" },
            sections.Where(slug => slug != StoryAccessPolicy.GratisPlaylistSlug).ToArray());
        Assert.AreEqual(StoryAccessPolicy.GratisPlaylistSlug, configured[2]);
    }

    [TestMethod]
    public void CachedMobileCataloguePromotesGratisOnlyOnTheVisitThatEarnedIt()
    {
        var gratis = new MobilePlaylist(StoryAccessPolicy.GratisPlaylistSlug, "Gratis", null, "", "", []);
        MobileLuisterSection[] sections =
        [
            new("speellyste", "Speellyste", 0, null, [gratis]),
            new("playlist", "Gratis", 10, gratis, [])
        ];
        var first = Guid.NewGuid();
        var catalogue = new Shink.Mobile.Models.MobileLuisterResponse(false, [], sections, first);

        Assert.AreSame(gratis, catalogue.GetSectionsForVisit(first)[0].Playlist);
        Assert.AreSame(sections, catalogue.GetSectionsForVisit(Guid.NewGuid()));
        Assert.AreSame(sections, (catalogue with { FirstGratisVisitId = null }).GetSectionsForVisit(first));
        Assert.AreSame(sections, (catalogue with { HasPaidSubscription = true }).GetSectionsForVisit(first));
    }

    private static StoryPlaylist CreatePlaylist(string slug, string title) =>
        new(
            Slug: slug,
            Title: title,
            Description: null,
            SortOrder: 0,
            Stories: Array.Empty<StoryItem>());

    private sealed record PlaylistSection(string PlaylistSlug);
}
