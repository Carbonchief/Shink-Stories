using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class StoryCatalogBehaviorTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task StoryReads_PreserveFilteringOrderingAndPlaylistMembershipAfterRefresh()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new StoryHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client, cache);

        var stories = await service.GetLuisterStoriesAsync();
        CollectionAssert.AreEqual(new[] { "story-0", "story-1", "story-2" }, stories.Select(story => story.Slug).ToArray());
        CollectionAssert.AreEqual(new[] { "story-0" }, (await service.GetFreeStoriesAsync()).Select(story => story.Slug).ToArray());
        Assert.IsNull(await service.FindFreeBySlugAsync("story-1"));
        Assert.IsNull(await service.FindLuisterBySlugAsync("story-3"), "Soundbites must remain excluded.");
        Assert.IsNull(await service.FindAnyBySlugAsync("story-4"), "Draft stories must remain excluded.");
        Assert.IsNull(await service.FindAnyBySlugAsync("missing"));
        Assert.IsNotNull(await service.FindAnyBySlugAsync("story-3"));
        var first = await service.FindLuisterBySlugAsync("STORY-0");
        Assert.IsNotNull(first);
        Assert.AreEqual("free", first.AccessLevel);
        CollectionAssert.AreEqual(new[] { "playlist-0", "playlist-1" }, first.PlaylistSlugs!.ToArray());
        Assert.AreEqual("r2", first.AudioProvider);
        Assert.AreEqual("audio/story-0.mp3", first.AudioFileName);

        handler.TitlePrefix = "Updated ";
        cache.Remove("stories:catalog:v2");
        var updated = await service.FindAnyBySlugAsync("story-0");
        Assert.AreEqual("Updated Story 0", updated!.Title);
        CollectionAssert.AreEqual(first.PlaylistSlugs.ToArray(), updated.PlaylistSlugs!.ToArray());
        Assert.AreEqual(6, handler.RequestCount);
    }

    [TestMethod]
    public async Task PersonalizedPlaylists_KeepEachUsersFavoritesSeparate()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new StoryHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client, cache);
        var firstUser = await service.GetLuisterPlaylistsAsync("first@example.com");
        var secondUser = await service.GetLuisterPlaylistsAsync("second@example.com");
        var firstFavorites = firstUser.Single(playlist => playlist.SystemKey == "favourites");
        var secondFavorites = secondUser.Single(playlist => playlist.SystemKey == "favourites");
        CollectionAssert.AreEqual(new[] { "story-0" }, firstFavorites.Stories.Select(story => story.Slug).ToArray());
        CollectionAssert.AreEqual(new[] { "story-1" }, secondFavorites.Stories.Select(story => story.Slug).ToArray());
        Assert.IsFalse((await service.GetLuisterPlaylistsAsync()).Any(playlist => playlist.SystemKey == "favourites"));
        var count = handler.RequestCount;
        await service.GetLuisterPlaylistsAsync(" FIRST@example.com ");
        Assert.AreEqual(count, handler.RequestCount);
    }

    [TestMethod]
    public async Task WarmStoryLookup_RecordsAllocationCostWithoutDatabaseRequests()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new StoryHandler { StoryCount = 400 };
        using var client = new HttpClient(handler);
        var service = CreateService(client, cache);
        await service.FindAnyBySlugAsync("story-399");
        var timer = Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++)
        {
            var story = await service.FindAnyBySlugAsync("story-399");
            Assert.IsNotNull(story);
            Assert.HasCount(2, story.PlaylistSlugs!);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        TestContext.WriteLine($"100 warm lookups; 400 stories; 800 playlist links: {allocated:N0} bytes allocated, {timer.Elapsed.TotalMilliseconds:F2} ms.");
        Assert.AreEqual(3, handler.RequestCount);
    }

    private static SupabaseStoryCatalogService CreateService(HttpClient client, IMemoryCache cache) => new(
        client, Options.Create(new SupabaseOptions
        {
            Url = "https://example.supabase.co/", PublishableKey = "test-public", SecretKey = "test-secret"
        }), cache, NullLogger<SupabaseStoryCatalogService>.Instance);

    private sealed class StoryHandler : HttpMessageHandler
    {
        public int StoryCount { get; init; } = 5;
        public string TitlePrefix { get; set; } = "";
        public int RequestCount { get; private set; }
        private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("example.supabase.co", request.RequestUri!.Host);
            RequestCount++;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            object rows = request.RequestUri.Segments[^1] switch
            {
                "stories" => Enumerable.Range(0, StoryCount).Select(index => new
                {
                    story_id = Id(index + 1), slug = $"story-{index}", title = $"{TitlePrefix}Story {index}",
                    status = index == 4 ? "draft" : "published", access_level = index == 0 ? "free" : "subscriber",
                    duration_seconds = index == 3 ? 30 : 240, audio_provider = "r2", audio_bucket = "test-audio",
                    audio_object_key = $"audio/story-{index}.mp3", sort_order = index,
                    published_at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-index)
                }).ToArray(),
                "story_playlists" => Enumerable.Range(0, 4).Select(index => new
                {
                    playlist_id = Id(1000 + index), slug = $"playlist-{index}", title = $"Playlist {index}",
                    is_enabled = index != 2, sort_order = index,
                    playlist_type = index == 3 ? "system" : "custom", system_key = index == 3 ? "favourites" : null
                }).ToArray(),
                "story_playlist_items" => Enumerable.Range(0, StoryCount).SelectMany(index => Enumerable.Range(0, 2)
                    .Select(playlist => new { playlist_id = Id(1000 + playlist), story_id = Id(index + 1), sort_order = index })).ToArray(),
                "subscribers" => new[] { new { subscriber_id = Id(query["email"]!.Contains("first") ? 9001 : 9002) } },
                "story_favorites" => new[] { new { story_slug = query["subscriber_id"] == $"eq.{Id(9001):D}" ? "story-0" : "story-1", created_at = "2026-01-01T00:00:00Z" } },
                "story_favourites" => Array.Empty<object>(),
                _ => throw new AssertFailedException($"Unexpected request: {request.RequestUri.AbsolutePath}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(rows), Encoding.UTF8, "application/json")
            });
        }
    }
}
