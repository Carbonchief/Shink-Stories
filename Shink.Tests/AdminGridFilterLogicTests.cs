using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Pages;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class AdminGridFilterLogicTests
{
    [TestMethod]
    public void FilterStories_AppliesSearchAndColumnFiltersTogether()
    {
        var stories = new[]
        {
            new AdminStoryRecord(
                Guid.NewGuid(),
                "sleepy-bear",
                "Sleepy Bear",
                null,
                null,
                null,
                null,
                null,
                "r2",
                null,
                null,
                null,
                "subscriber",
                "published",
                1,
                null,
                null,
                null),
            new AdminStoryRecord(
                Guid.NewGuid(),
                "forest-bear",
                "Forest Bear",
                null,
                null,
                null,
                null,
                null,
                "r2",
                null,
                null,
                null,
                "free",
                "draft",
                2,
                null,
                null,
                null)
        };

        var filters = new AdminStoryColumnFilters
        {
            TitleText = "sleepy",
            Status = "published",
            AccessLevel = "subscriber"
        };

        var result = AdminGridFilterLogic.FilterStories(stories, "bear", filters);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Sleepy Bear", result[0].Title);
    }

    [TestMethod]
    public void FilterPlaylists_AppliesBooleanAndTextFilters()
    {
        var playlists = new[]
        {
            new AdminPlaylistRecord(
                Guid.NewGuid(),
                "free-bedtime",
                "Free Bedtime",
                false,
                null,
                null,
                null,
                null,
                null,
                1,
                null,
                true,
                true,
                false,
                false,
                null,
                Array.Empty<AdminPlaylistStoryItem>()),
            new AdminPlaylistRecord(
                Guid.NewGuid(),
                "subscriber-bedtime",
                "Subscriber Bedtime",
                false,
                null,
                null,
                null,
                null,
                null,
                2,
                null,
                false,
                false,
                false,
                true,
                null,
                Array.Empty<AdminPlaylistStoryItem>())
        };

        var filters = new AdminPlaylistColumnFilters
        {
            TitleText = "free",
            IsEnabled = AdminGridFilterOptionValues.True,
            ShowOnHome = AdminGridFilterOptionValues.True,
            ShowShowcase = AdminGridFilterOptionValues.False
        };

        var result = AdminGridFilterLogic.FilterPlaylists(playlists, "bedtime", filters);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Free Bedtime", result[0].Title);
    }

    [TestMethod]
    public void FilterPlaylists_ReturnsOriginalCollection_WhenNoFiltersAreActive()
    {
        var playlists = new[]
        {
            new AdminPlaylistRecord(
                Guid.NewGuid(),
                "one",
                "One",
                false,
                null,
                null,
                null,
                null,
                null,
                1,
                null,
                true,
                false,
                false,
                false,
                null,
                Array.Empty<AdminPlaylistStoryItem>()),
            new AdminPlaylistRecord(
                Guid.NewGuid(),
                "two",
                "Two",
                false,
                null,
                null,
                null,
                null,
                null,
                2,
                null,
                false,
                true,
                false,
                true,
                null,
                Array.Empty<AdminPlaylistStoryItem>())
        };

        var result = AdminGridFilterLogic.FilterPlaylists(
            playlists,
            string.Empty,
            new AdminPlaylistColumnFilters());

        Assert.AreEqual(2, result.Count);
    }
}
