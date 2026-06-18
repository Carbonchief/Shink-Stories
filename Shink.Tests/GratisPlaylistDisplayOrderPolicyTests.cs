using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;

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

    private static StoryPlaylist CreatePlaylist(string slug, string title) =>
        new(
            Slug: slug,
            Title: title,
            Description: null,
            SortOrder: 0,
            Stories: Array.Empty<StoryItem>());

    private sealed record PlaylistSection(string PlaylistSlug);
}
