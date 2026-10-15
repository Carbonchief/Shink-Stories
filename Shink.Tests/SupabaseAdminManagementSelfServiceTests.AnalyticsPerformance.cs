using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseAdminManagementSelfServiceTests
{
    [TestMethod]
    public async Task UsageCacheExpiresAndManualRefreshAlwaysRequestsFreshData()
    {
        var clock = new AnalyticsTestClock();
        using var cache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var reads = 0;
        var adminChecks = 0;
        var service = CreateService(new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                adminChecks++;
                return JsonResponse("""[{"email":"admin@example.com"}]""");
            }

            Assert.AreEqual("/rest/v1/rpc/get_admin_analytics_snapshot", request.RequestUri?.AbsolutePath);
            return JsonResponse(JsonSerializer.Serialize(UsageSnapshot(++reads)));
        }), memoryCache: cache);

        var first = await service.GetAnalyticsAsync("admin@example.com");
        clock.UtcNow = clock.UtcNow.AddSeconds(29);
        Assert.AreSame(first, await service.GetAnalyticsAsync("admin@example.com"));
        Assert.AreEqual(1, reads);

        var refreshed = await service.GetAnalyticsAsync("admin@example.com", forceRefresh: true);
        Assert.AreEqual(2, refreshed.StorySummary.TotalViews);
        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        Assert.AreEqual(3, (await service.GetAnalyticsAsync("admin@example.com")).StorySummary.TotalViews);
        Assert.AreEqual(3, reads);
        Assert.AreEqual(4, adminChecks, "Cached analytics must still recheck admin access.");
    }

    [TestMethod]
    public async Task UsageCacheDoesNotExposeSnapshotsAfterAdminAccessIsRevoked()
    {
        var authorized = true;
        var reads = 0;
        var service = CreateService(new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse(authorized ? """[{"email":"admin@example.com"}]""" : "[]");
            }

            reads++;
            return JsonResponse(JsonSerializer.Serialize(UsageSnapshot(1)));
        }));

        await service.GetAnalyticsAsync("admin@example.com");
        authorized = false;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.GetAnalyticsAsync("admin@example.com"));
        Assert.AreEqual(1, reads);
    }

    [TestMethod]
    public async Task ConcurrentUsageRequestsAcrossServiceInstancesShareOneSnapshotLoad()
    {
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var handler = new AnalyticsAsyncHandler(async (request, token) =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{"email":"admin@example.com"}]""");
            }

            Interlocked.Increment(ref reads);
            entered.TrySetResult();
            return await response.Task.WaitAsync(token);
        });
        var firstService = CreateService(handler, memoryCache: cache);
        var secondService = CreateService(handler, memoryCache: cache);
        var first = firstService.GetAnalyticsAsync("admin@example.com");
        try
        {
            await entered.Task;
            var second = secondService.GetAnalyticsAsync("admin@example.com");
            Assert.IsFalse(second.IsCompleted);
            Assert.AreEqual(1, reads);
            response.SetResult(JsonResponse(JsonSerializer.Serialize(UsageSnapshot(1))));
            Assert.AreSame(await first, await second);
            Assert.AreEqual(1, reads);
        }
        finally
        {
            response.TrySetResult(JsonResponse(JsonSerializer.Serialize(UsageSnapshot(1))));
            await first;
        }
    }

    [TestMethod]
    public async Task CancellingAQueuedUsageRequestDoesNotCancelOrPoisonTheSharedLoad()
    {
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var handler = new AnalyticsAsyncHandler(async (request, token) =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{"email":"admin@example.com"}]""");
            }

            Interlocked.Increment(ref reads);
            entered.TrySetResult();
            return await response.Task.WaitAsync(token);
        });
        var service = CreateService(handler, memoryCache: cache);
        var first = service.GetAnalyticsAsync("admin@example.com");
        try
        {
            await entered.Task;
            var second = service.GetAnalyticsAsync("admin@example.com", cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => second);
            response.SetResult(JsonResponse(JsonSerializer.Serialize(UsageSnapshot(1))));
            Assert.AreSame(await first, await service.GetAnalyticsAsync("admin@example.com"));
            Assert.AreEqual(1, reads);
        }
        finally
        {
            response.TrySetResult(JsonResponse(JsonSerializer.Serialize(UsageSnapshot(1))));
            await first;
        }
    }

    [TestMethod]
    public async Task SubscriberAnalyticsSkipsUnusedEngagementHistoryAndCachesEachReportShapeSeparately()
    {
        var calls = new Dictionary<string, int>();
        var service = CreatePerformanceReportsService(calls);
        var first = await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false);
        Assert.IsEmpty(first.VisitsViewsAndLogins);
        Assert.AreSame(first, await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        Assert.AreEqual(1, calls["/rest/v1/rpc/get_wordpress_subscriber_report_snapshot"]);
        Assert.IsFalse(calls.ContainsKey("/rest/v1/story_listen_events"));
        Assert.IsFalse(calls.ContainsKey("/rest/v1/story_views"));
        Assert.IsFalse(calls.ContainsKey("/rest/v1/auth_sessions"));

        var engagement = await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: true);
        Assert.IsNotEmpty(engagement.VisitsViewsAndLogins);
        Assert.AreEqual(2, calls["/rest/v1/rpc/get_wordpress_subscriber_report_snapshot"]);
        Assert.AreEqual(1, calls["/rest/v1/story_listen_events"]);
        Assert.AreEqual(1, calls["/rest/v1/story_views"]);
        Assert.AreEqual(1, calls["/rest/v1/auth_sessions"]);

        await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false, forceRefresh: true);
        Assert.AreEqual(3, calls["/rest/v1/rpc/get_wordpress_subscriber_report_snapshot"]);
    }

    [TestMethod]
    public async Task SubscriberReportsDoNotCachePartialPagesAfterARequiredRequestFails()
    {
        var calls = new Dictionary<string, int>();
        var fail = true;
        var service = CreatePerformanceReportsService(calls, request =>
        {
            if (!IsSupabaseGet(request, "/rest/v1/subscriptions")) return null;
            if (request.RequestUri!.Query.Contains("offset=0", StringComparison.Ordinal))
            {
                return JsonResponse(JsonSerializer.Serialize(Enumerable.Repeat(new { subscription_id = Guid.Empty }, 1000)));
            }

            return fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : JsonResponse("[]");
        });

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        fail = false;
        var recovered = await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false);
        Assert.AreSame(recovered, await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        Assert.AreEqual(4, calls["/rest/v1/subscriptions"]);
        Assert.AreEqual(2, calls["/rest/v1/rpc/get_wordpress_subscriber_report_snapshot"]);
    }

    [TestMethod]
    public async Task SubscriberReportCacheStillChecksAdminAccess()
    {
        var calls = new Dictionary<string, int>();
        var authorized = true;
        var service = CreatePerformanceReportsService(calls, authorized: () => authorized);
        await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false);
        authorized = false;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        Assert.AreEqual(1, calls["/rest/v1/subscriptions"]);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("invalid JSON")]
    public async Task InvalidReportRowResponsesAreNotCachedAsEmptyData(string invalidResponse)
    {
        var calls = new Dictionary<string, int>();
        var fail = true;
        var service = CreatePerformanceReportsService(calls, request =>
            fail && IsSupabaseGet(request, "/rest/v1/subscribers") ? JsonResponse(invalidResponse) : null);

        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        fail = false;
        var recovered = await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false);
        Assert.AreSame(recovered, await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        Assert.AreEqual(2, calls["/rest/v1/subscribers"]);
    }

    private static AdminAnalyticsSnapshot UsageSnapshot(int views) => AdminAnalyticsSnapshot.Empty with
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        StorySummary = AdminAnalyticsSnapshot.Empty.StorySummary with { TotalViews = views }
    };

    private static SupabaseAdminManagementService CreatePerformanceReportsService(
        Dictionary<string, int> calls,
        Func<HttpRequestMessage, HttpResponseMessage?>? responseOverride = null,
        Func<bool>? authorized = null) => CreateService(new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            calls[path] = calls.GetValueOrDefault(path) + 1;
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse(authorized?.Invoke() != false ? """[{"email":"admin@example.com"}]""" : "[]");
            }

            if (path == "/rest/v1/rpc/get_wordpress_subscriber_report_snapshot")
            {
                return JsonResponse("""{"has_wordpress_data":true,"membership_stats":[],"active_members_per_level":[],"sales_and_revenue":[]}""");
            }

            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.Contains(path, new[] { "/rest/v1/subscribers", "/rest/v1/subscriptions", "/rest/v1/subscription_events", "/rest/v1/store_orders", "/rest/v1/subscription_tiers", "/rest/v1/subscription_payment_recoveries", "/rest/v1/abandoned_cart_recoveries", "/rest/v1/subscription_cancellation_feedback", "/rest/v1/auth_sessions", "/rest/v1/story_views", "/rest/v1/story_listen_events" });
            return responseOverride?.Invoke(request) ?? JsonResponse("[]");
        }));

    private sealed class AnalyticsTestClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class AnalyticsAsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
