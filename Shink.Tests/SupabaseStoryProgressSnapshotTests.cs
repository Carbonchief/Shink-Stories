using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class SupabaseStoryProgressSnapshotTests
{
    private const string SubscriberId = "00000000-0000-0000-0000-000000000001";
    private const string Email = "listener@example.com";
    private const string Snapshot = """
        [{"story_slug":"alpha","story_path":"/luister/alpha","metadata":{"source":"luister"},
          "last_listened_at":"2026-10-01T00:00:03Z","total_listened_seconds":106.123,
          "listen_count":2,"completed_listen_count":1,"position_seconds":10.123,
          "duration_seconds":100,"is_completed":false}]
        """;

    [TestMethod]
    public async Task GetUserStoryProgressAsync_ReturnsSnapshotWithoutDownloadingEvents()
    {
        using var handler = new ProgressHandler(_ => JsonResponse(Snapshot));
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(client, cache);

        var result = await service.GetUserStoryProgressAsync(Email);

        Assert.AreEqual(new UserStoryProgressItem("alpha", "/luister/alpha", "luister",
            DateTimeOffset.Parse("2026-10-01T00:00:03Z"), 106.123m, 2, 1, 10.123m, 100m, false),
            Assert.ContainsSingle(result));
        Assert.AreEqual(1, handler.SnapshotRequests);
        Assert.AreEqual(0, handler.HistoryRequests);
        using var payload = JsonDocument.Parse(Assert.ContainsSingle(handler.SnapshotPayloads));
        Assert.AreEqual(SubscriberId, payload.RootElement.GetProperty("p_subscriber_id").GetString());
    }

    [TestMethod]
    public async Task GetUserStoryProgressAsync_DatabaseSnapshotMatchesPreviousCalculation()
    {
        // The SQL fixture emits both contracts. Use fresh PostgreSQL output when
        // supplied; the checked-in synthetic fixture also runs in ordinary CI.
        var path = Environment.GetEnvironmentVariable("SHINK_PROGRESS_PARITY_JSON") ?? GetFixturePath();
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var snapshotJson = fixture.RootElement.GetProperty("snapshot").GetRawText();
        var eventsJson = fixture.RootElement.GetProperty("events").GetRawText();
        using var snapshotHandler = new ProgressHandler(_ => JsonResponse(snapshotJson));
        using var legacyHandler = new ProgressHandler(_ => JsonResponse("{\"code\":\"PGRST202\"}", HttpStatusCode.NotFound))
        {
            HistoryJson = eventsJson
        };
        using var snapshotClient = new HttpClient(snapshotHandler);
        using var legacyClient = new HttpClient(legacyHandler);
        using var snapshotCache = new MemoryCache(new MemoryCacheOptions());
        using var legacyCache = new MemoryCache(new MemoryCacheOptions());

        var expected = await CreateService(legacyClient, legacyCache).GetUserStoryProgressAsync(Email);
        var actual = await CreateService(snapshotClient, snapshotCache).GetUserStoryProgressAsync(Email);

        Assert.IsNotEmpty(expected);
        Assert.HasCount(expected.Count, actual);
        CollectionAssert.AreEqual(expected.OrderBy(x => x.StorySlug).ToArray(), actual.OrderBy(x => x.StorySlug).ToArray());
        Assert.AreEqual(0, snapshotHandler.HistoryRequests);
        Assert.AreEqual(fixture.RootElement.GetProperty("events").GetArrayLength() / 1000 + 1,
            legacyHandler.HistoryRequests);
    }

    [TestMethod]
    public async Task GetUserStoryProgressAsync_DoesNotCacheStaleProgressAfterListen()
    {
        var reads = 0;
        using var handler = new ProgressHandler(_ => JsonResponse(++reads == 1
            ? Snapshot
            : Snapshot.Replace("106.123", "118.123").Replace("10.123", "22.123")));
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(client, cache);

        await service.GetUserStoryProgressAsync(Email);
        var recorded = await service.RecordStoryListenAsync(Email, new StoryListenTrackingRequest(
            "alpha", "/luister/alpha", Guid.NewGuid(), "progress", 12m, 22.123m, 100m, "luister", false));
        var refreshed = Assert.ContainsSingle(await service.GetUserStoryProgressAsync(Email));

        Assert.IsTrue(recorded);
        Assert.AreEqual(118.123m, refreshed.TotalListenedSeconds);
        Assert.AreEqual(22.123m, refreshed.LastPositionSeconds);
        Assert.AreEqual(2, handler.SnapshotRequests);
    }

    [TestMethod]
    public async Task GetUserStoryProgressAsync_ScalarJsonSnapshotIsNotLimitedToOneThousandStories()
    {
        using var row = JsonDocument.Parse(Snapshot);
        var json = "[" + string.Join(",", Enumerable.Range(0, 1001)
            .Select(i => row.RootElement[0].GetRawText().Replace("alpha", $"story-{i}"))) + "]";
        using var handler = new ProgressHandler(_ => JsonResponse(json));
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var result = await CreateService(client, cache).GetUserStoryProgressAsync(Email);

        Assert.HasCount(1001, result);
        Assert.AreEqual("story-1000", result[^1].StorySlug);
        Assert.AreEqual(1, handler.SnapshotRequests);
        Assert.AreEqual(0, handler.HistoryRequests);
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(404)]
    [DataRow(500)]
    [DataRow(503)]
    public async Task GetUserStoryProgressAsync_OrdinaryRpcFailureDoesNotDownloadHistoryOrPoisonNextRead(int status)
    {
        var reads = 0;
        using var handler = new ProgressHandler(_ => ++reads == 1
            ? JsonResponse("{\"code\":\"unexpected_error\"}", (HttpStatusCode)status)
            : JsonResponse(Snapshot));
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(client, cache);

        Assert.IsEmpty(await service.GetUserStoryProgressAsync(Email));
        Assert.HasCount(1, await service.GetUserStoryProgressAsync(Email));
        Assert.AreEqual(0, handler.HistoryRequests);
    }

    [TestMethod]
    public async Task GetUserStoryProgressAsync_KeepsSubscriberRequestsSeparate()
    {
        using var handler = new ProgressHandler(_ => JsonResponse(Snapshot));
        using var client = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(client, cache);

        await service.GetUserStoryProgressAsync(Email);
        await service.GetUserStoryProgressAsync("second@example.com");

        Assert.HasCount(2, handler.SnapshotPayloads);
        Assert.Contains(SubscriberId, handler.SnapshotPayloads[0]);
        Assert.Contains("00000000-0000-0000-0000-000000000002", handler.SnapshotPayloads[1]);
    }

    private static SupabaseStoryTrackingService CreateService(HttpClient client, IMemoryCache cache) => new(
        client, Options.Create(new SupabaseOptions { Url = "https://example.supabase.co/", SecretKey = "test-secret" }),
        cache, NullLogger<SupabaseStoryTrackingService>.Instance);

    private static string GetFixturePath([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "Shink", "Database", "tests", "fixtures", "story_progress_parity.json"));

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ProgressHandler(Func<HttpRequestMessage, HttpResponseMessage> snapshotResponse) : HttpMessageHandler
    {
        public int SnapshotRequests { get; private set; }
        public int HistoryRequests { get; private set; }
        public List<string> SnapshotPayloads { get; } = [];
        public string HistoryJson { get; init; } = "[]";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("/subscribers", StringComparison.Ordinal))
            {
                var subscriber = uri.Query.Contains("second%40", StringComparison.Ordinal)
                    ? "00000000-0000-0000-0000-000000000002" : SubscriberId;
                return JsonResponse($$"""[{"subscriber_id":"{{subscriber}}"}]""");
            }

            if (uri.AbsolutePath.EndsWith("/rpc/get_story_progress_snapshot", StringComparison.Ordinal))
            {
                Assert.AreEqual(HttpMethod.Post, request.Method);
                SnapshotRequests++;
                SnapshotPayloads.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return snapshotResponse(request);
            }

            if (uri.AbsolutePath.EndsWith("/story_listen_events", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get)
                {
                    HistoryRequests++;
                    var parameters = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    var offset = int.Parse(parameters["offset"] ?? "0");
                    using var history = JsonDocument.Parse(HistoryJson);
                    return JsonResponse(JsonSerializer.Serialize(history.RootElement.EnumerateArray().Skip(offset).Take(1000)));
                }
                Assert.Contains("return=minimal", string.Join(",", request.Headers.GetValues("Prefer")));
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            Assert.Fail($"Unexpected request: {request.Method} {uri.AbsolutePath}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
