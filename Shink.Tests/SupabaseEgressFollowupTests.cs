using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SupabaseEgressFollowupTests
{
    private static readonly IOptions<SupabaseOptions> Settings = Options.Create(new SupabaseOptions
    {
        Url = "https://example.supabase.co", PublishableKey = "test-public", SecretKey = "test-server"
    });
    private const string Subscriber = "11111111-1111-1111-1111-111111111111";
    private const string Character = "22222222-2222-2222-2222-222222222222";
    private const string Story = "33333333-3333-3333-3333-333333333333";

    [TestMethod]
    public async Task PublicContent_SurvivesTwoDays_ThenRefreshesAcrossInstancesOnPublish()
    {
        var clock = new TestClock();
        var versions = new Versions();
        using var cache1 = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        using var cache2 = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        using var origin = new RoutingHandler(_ => Json($"[{{\"title\":\"revision-{versions.Revision}\"}}]"));
        using var first = CachedClient(cache1, versions, origin);
        using var second = CachedClient(cache2, versions, origin);
        var url = "https://example.supabase.co/rest/v1/stories?status=eq.published";
        Assert.Contains("revision-1", await first.GetStringAsync(url));
        Assert.Contains("revision-1", await second.GetStringAsync(url));
        clock.UtcNow += TimeSpan.FromDays(2);
        await first.GetStringAsync(url);
        await second.GetStringAsync(url);
        Assert.AreEqual(2, origin.Calls, "Payloads remain cached beyond the former two-minute lifetime.");
        versions.Revision = 2;
        Assert.Contains("revision-2", await first.GetStringAsync(url));
        Assert.Contains("revision-2", await second.GetStringAsync(url));
        Assert.AreEqual(4, origin.Calls, "Both instances must refresh immediately after publishing.");
        clock.UtcNow += TimeSpan.FromDays(8);
        await first.GetStringAsync(url);
        Assert.AreEqual(5, origin.Calls);
    }

    [TestMethod]
    public async Task PublishDuringFetch_CannotPopulateNewRevisionWithOldContent()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var versions = new Versions();
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var origin = new AsyncHandler(async (_, token) =>
        {
            var revision = versions.Revision;
            began.TrySetResult();
            await release.Task.WaitAsync(token);
            return Json($"[{{\"title\":\"revision-{revision}\"}}]");
        });
        using var client = CachedClient(cache, versions, origin);
        var url = "https://example.supabase.co/rest/v1/stories?status=eq.published";
        var old = client.GetStringAsync(url);
        await began.Task;
        versions.Revision = 2;
        var current = client.GetStringAsync(url);
        release.SetResult();
        Assert.Contains("revision-1", await old);
        Assert.Contains("revision-2", await current);
        Assert.Contains("revision-2", await client.GetStringAsync(url));
        Assert.AreEqual(2, origin.Calls);
    }

    [TestMethod]
    [DataRow(500, "[]")]
    [DataRow(200, "invalid json")]
    [DataRow(200, "[]")]
    [DataRow(200, "{\"unexpected\":true}")]
    public async Task FailedOrEmptyResponses_DoNotPoisonLongCache(int status, string body)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var origin = new RoutingHandler(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
        using var client = CachedClient(cache, new Versions(), origin);
        for (var index = 0; index < 2; index++)
        {
            using var response = await client.GetAsync("https://example.supabase.co/rest/v1/stories?status=eq.published");
        }
        Assert.AreEqual(2, origin.Calls);
    }

    [TestMethod]
    public async Task UnavailableRevision_BypassesWarmPayloadAndObjectCaches()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var versions = new Versions();
        using var origin = StoryOrigin(versions);
        using var client = CachedClient(cache, versions, origin);
        var service = new SupabaseStoryCatalogService(client, Settings, cache,
            NullLogger<SupabaseStoryCatalogService>.Instance, versions);
        Assert.AreEqual("revision-1", (await service.GetLuisterStoriesAsync()).Single().Title);
        versions.Revision = null;
        Assert.AreEqual("revision-", (await service.GetLuisterStoriesAsync()).Single().Title);
        Assert.AreEqual(2, origin.Count("get_story_catalogue_listing"));
    }

    [TestMethod]
    [DataRow("subscriber_notifications")]
    [DataRow("story_listen_events")]
    [DataRow("subscriptions")]
    [DataRow("story_favorites")]
    [DataRow("stories?status=eq.draft")]
    [DataRow("blog_posts?is_published=eq.true&published_at=lte.2026-10-23T00%3A00%3A00Z")]
    [DataRow("rpc/get_character_profile_listen_snapshot")]
    [DataRow("rpc/get_notification_page_snapshot")]
    public async Task PrivateAndFreshData_NeverEntersPublicCache(string path)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var versions = new Versions();
        using var origin = new RoutingHandler(_ => Json("[{\"value\":1}]"));
        using var client = CachedClient(cache, versions, origin);
        for (var index = 0; index < 2; index++)
        {
            using var request = new HttpRequestMessage(path.StartsWith("rpc/") ? HttpMethod.Post : HttpMethod.Get,
                "https://example.supabase.co/rest/v1/" + path);
            using var response = await client.SendAsync(request);
        }
        Assert.AreEqual(2, origin.Calls);
        Assert.AreEqual(0, versions.Calls);
    }

    [TestMethod]
    public async Task CancelledWaiter_DoesNotAbortTheSharedPublicPayloadFetch()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var versions = new Versions();
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var origin = new AsyncHandler(async (_, token) =>
        {
            began.TrySetResult();
            await release.Task.WaitAsync(token);
            return Json("[{\"title\":\"Story\"}]");
        });
        using var client = CachedClient(cache, versions, origin);
        var url = "https://example.supabase.co/rest/v1/stories?status=eq.published";
        var first = client.GetStringAsync(url);
        await began.Task;
        using var cancellation = new CancellationTokenSource();
        var second = client.GetStringAsync(url, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await second);
        release.SetResult();
        await first;
        await client.GetStringAsync(url);
        Assert.AreEqual(1, origin.Calls);
    }

    [TestMethod]
    public async Task RevisionClient_ReadsOnlyCounter_AndRejectsUnavailableRevisions()
    {
        var status = HttpStatusCode.OK;
        using var origin = new RoutingHandler(request =>
        {
            Assert.AreEqual("public_content_cache_revisions", request.RequestUri!.Segments[^1]);
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            Assert.AreEqual("revision", query["select"]);
            Assert.AreEqual("eq.stories", query["cache_key"]);
            Assert.AreEqual("test-public", request.Headers.GetValues("apikey").Single());
            return Json("[{\"revision\":7}]", status);
        });
        var service = new SupabasePublicContentVersions(new ClientFactory(origin), Settings,
            NullLogger<SupabasePublicContentVersions>.Instance);
        Assert.AreEqual(7L, await service.GetRevisionAsync("stories"));
        status = HttpStatusCode.NotFound;
        Assert.IsNull(await service.GetRevisionAsync("stories"));
    }

    [TestMethod]
    public async Task CachedContent_IsSeparatedByCredentialsAndRepresentationHeaders()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var origin = new RoutingHandler(_ => Json("[{\"value\":1}]"));
        using var client = CachedClient(cache, new Versions(), origin);
        foreach (var key in new[] { "first", "second", "first" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.supabase.co/rest/v1/stories?status=eq.published");
            request.Headers.TryAddWithoutValidation("apikey", key);
            using var response = await client.SendAsync(request);
        }
        Assert.AreEqual(2, origin.Calls);
        using var withCount = new HttpRequestMessage(HttpMethod.Get, "https://example.supabase.co/rest/v1/stories?status=eq.published");
        withCount.Headers.TryAddWithoutValidation("apikey", "first");
        withCount.Headers.TryAddWithoutValidation("Prefer", "count=exact");
        using var countedResponse = await client.SendAsync(withCount);
        Assert.AreEqual(3, origin.Calls);
    }

    [TestMethod]
    public async Task Catalogue_LoadsDetailsForOnlySelectedStory_AndInvalidatesWarmObjects()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var versions = new Versions();
        using var origin = StoryOrigin(versions);
        using var client = CachedClient(cache, versions, origin);
        var service = new SupabaseStoryCatalogService(client, Settings, cache,
            NullLogger<SupabaseStoryCatalogService>.Instance, versions);
        var list = await service.GetLuisterStoriesAsync();
        Assert.AreEqual("Searchable description", list.Single().Description);
        Assert.AreEqual("Panda", list.Single().Characters!.Single());
        Assert.AreEqual(0, origin.Count("stories"));
        var detail = await service.FindLuisterBySlugAsync("STORY");
        Assert.IsNotNull(detail);
        Assert.AreEqual("Full lesson", detail.Lessons!.Single());
        Assert.AreEqual("Full question", detail.ConversationQuestions!.Single());
        Assert.AreEqual(1, detail.TestQuestions!.Count);
        Assert.AreEqual(1, origin.Count("stories"));
        await service.FindLuisterBySlugAsync("story");
        Assert.AreEqual(1, origin.Count("stories"), "A selected story's details also use the revision cache.");
        versions.Revision = 2;
        Assert.AreEqual("revision-2", (await service.GetLuisterStoriesAsync()).Single().Title);
        Assert.AreEqual(2, origin.Count("get_story_catalogue_listing"));
    }

    [TestMethod]
    public async Task CharacterSummaries_AreFreshAfterAListen_WithoutHistoryDownloads()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var plays = 1866;
        using var origin = new RoutingHandler(request => request.RequestUri!.Segments[^1] switch
        {
            "subscribers" => Json($"[{{\"subscriber_id\":\"{Subscriber}\"}}]"),
            "get_character_profile_listen_snapshot" => Json($"[{{\"character_id\":\"{Character}\",\"character_slug\":\"panda\",\"listen_count\":{plays},\"last_listened_at\":\"2026-10-23T00:00:00Z\"}}]"),
            "character_audio_plays" when request.Method == HttpMethod.Post => Record(),
            _ => throw new AssertFailedException("Unexpected raw-history request.")
        });
        HttpResponseMessage Record() { plays++; return Json("[]", HttpStatusCode.Created); }
        using var client = new HttpClient(origin);
        var service = new SupabaseCharacterService(client, Settings, cache, NullLogger<SupabaseCharacterService>.Instance);
        Assert.AreEqual(1866, (await service.GetUserProfileListenStatsAsync("listener@example.com")).Single().ListenCount);
        Assert.IsTrue(await service.RecordProfileListenAsync("listener@example.com",
            new CharacterProfileListenTrackingRequest(Guid.Parse(Character), "panda", "panda-profile", "test")));
        Assert.AreEqual(1867, (await service.GetUserProfileListenStatsAsync("listener@example.com")).Single().ListenCount);
    }

    [TestMethod]
    public async Task Notifications_BadgeOmitsBodies_PagePreservesReadHistoryAndFreshCounts()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var unread = 4;
        using var origin = new RoutingHandler(request =>
        {
            if (request.RequestUri!.Segments[^1] == "subscribers") return Json($"[{{\"subscriber_id\":\"{Subscriber}\",\"disabled_at\":null}}]");
            Assert.AreEqual("get_notification_page_snapshot", request.RequestUri.Segments[^1]);
            Assert.AreEqual(HttpMethod.Post, request.Method);
            var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var items = payload.RootElement.GetProperty("p_include_items").GetBoolean();
            return Json(JsonSerializer.Serialize(new
            {
                unread_count = unread, has_more = items, has_history = items,
                notifications = items ? new[] { new { notification_id = Guid.Parse(Story), notification_type = "story_published",
                    title = "New story", body = "Message body", href = "/luister/story", created_at = "2026-10-23T00:00:00Z", read_at = (string?)null } } : []
            }));
        });
        using var client = new HttpClient(origin);
        var service = new SupabaseUserNotificationService(client, Settings, cache, new NoCharacters(), null!, null!,
            NullLogger<SupabaseUserNotificationService>.Instance);
        var badge = await service.GetNotificationSummaryAsync("listener@example.com");
        Assert.AreEqual(4, badge.UnreadCount);
        Assert.AreEqual(0, badge.Notifications.Count);
        unread = 3;
        var page = await service.GetNotificationsAsync("listener@example.com");
        Assert.AreEqual(3, page.UnreadCount);
        Assert.AreEqual("Message body", page.Notifications.Single().Body);
        Assert.IsTrue(page.HasMore);
        Assert.IsTrue(page.HasHistory);
        Assert.IsFalse(page.Notifications.Single().IsRead);
        Assert.AreEqual(2, origin.Count("get_notification_page_snapshot"));
        Assert.AreEqual(0, origin.Count("subscriber_notifications"));
    }

    private static RoutingHandler StoryOrigin(Versions versions) => new(request =>
    {
        var path = request.RequestUri!.Segments[^1];
        if (path is "story_playlists" or "story_playlist_items") return Json("[]");
        if (path == "get_story_catalogue_listing") return Json("""
            [{"story_id":"{{Story}}","slug":"story","title":"revision-{{versions.Revision}}","status":"published",
            "access_level":"subscriber","audio_object_key":"protected/story.mp3","duration_seconds":240,"is_summary":true,
            "description":"Searchable description","metadata":{"story_details":{"characters":["Panda"]}}}]
            """.Replace("{{Story}}", Story).Replace("{{versions.Revision}}", versions.Revision?.ToString() ?? ""));
        Assert.AreEqual("stories", path);
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        Assert.AreEqual("eq.story", query["slug"]);
        Assert.AreEqual("1", query["limit"]);
        Assert.Contains("test_questions", query["select"]!);
        return Json("""
            [{"story_id":"{{Story}}","slug":"story","title":"Full story","status":"published","access_level":"subscriber",
            "audio_object_key":"protected/story.mp3","duration_seconds":240,"metadata":{"story_details":{"lessons":["Full lesson"],
            "conversation_questions":["Full question"]}},"test_questions":[{"question":"Question","option_a":"A","option_b":"B","correct_option":"A"}]}]
            """.Replace("{{Story}}", Story).Replace("{{versions.Revision}}", versions.Revision?.ToString() ?? ""));
    });

    private static HttpClient CachedClient(IMemoryCache cache, Versions versions, HttpMessageHandler origin) =>
        new(new SupabasePublicContentCacheHandler(cache, versions, Settings) { InnerHandler = origin }, disposeHandler: false);
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class TestClock : ISystemClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private sealed class Versions : IPublicContentVersions
    {
        public long? Revision { get; set; } = 1;
        public int Calls { get; private set; }
        public Task<long?> GetRevisionAsync(string category, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Revision); }
    }
    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        private readonly Dictionary<string, int> counts = new();
        public int Calls => counts.Values.Sum();
        public int Count(string path) => counts.GetValueOrDefault(path);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri!.Segments[^1]; counts[key] = Count(key) + 1;
            return Task.FromResult(response(request));
        }
    }
    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return response(request, cancellationToken); }
    }
    private sealed class NoCharacters : ICharacterCatalogService
    {
        public Task<IReadOnlyList<StoryCharacterItem>> GetPublishedCharactersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoryCharacterItem>>([]);
        public Task<CharacterAudioClipItem?> FindPublishedAudioClipByStreamSlugAsync(string? streamSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult<CharacterAudioClipItem?>(null);
    }
}
