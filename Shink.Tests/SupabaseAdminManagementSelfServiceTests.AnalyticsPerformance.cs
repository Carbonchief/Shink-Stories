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
    public async Task UsagePanelReadsAndCachesItsDedicatedReport()
    {
        var reads = 0;
        var service = CreateService(new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
                return JsonResponse("""[{"email":"admin@example.com"}]""");

            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("/rest/v1/rpc/get_admin_usage_analytics", request.RequestUri?.AbsolutePath);
            reads++;
            return JsonResponse("""
                {"generated_at":"2026-11-24T00:00:00Z","story_summary":{"total_listen_sessions":42},
                 "character_summary":{},"daily_activity":[],"top_characters":[],"top_completion_stories":[]}
                """);
        }));

        var usage = await service.GetAnalyticsAsync("admin@example.com", panel: AdminActivityPanel.Usage);
        Assert.AreEqual(42, usage.StorySummary.TotalListenSessions);
        Assert.IsEmpty(usage.StoryAnalytics);
        Assert.AreSame(usage, await service.GetAnalyticsAsync("admin@example.com", panel: AdminActivityPanel.Usage));
        Assert.AreEqual(1, reads);
    }

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
        Assert.AreEqual(1, calls["/rest/v1/rpc/get_admin_engagement_periods"]);
        Assert.IsFalse(calls.ContainsKey("/rest/v1/story_listen_events"));

        await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false, forceRefresh: true);
        Assert.AreEqual(3, calls["/rest/v1/rpc/get_wordpress_subscriber_report_snapshot"]);
    }

    [TestMethod]
    public async Task SubscriberReportsDoNotCacheFailuresWhenReadingAllInputs()
    {
        var calls = new Dictionary<string, int>();
        var fail = true;
        var service = CreatePerformanceReportsService(calls, request =>
        {
            if (request.RequestUri!.AbsolutePath != "/rest/v1/rpc/get_admin_report_inputs") return null;
            return fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        });

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        fail = false;
        var recovered = await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false);
        Assert.AreSame(recovered, await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        Assert.AreEqual(2, calls["/rest/v1/rpc/get_admin_report_inputs"]);
        Assert.AreEqual(1, calls["/rest/v1/rpc/get_wordpress_subscriber_report_snapshot"]);
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
        Assert.AreEqual(1, calls["/rest/v1/rpc/get_admin_report_inputs"]);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("invalid JSON")]
    public async Task InvalidReportRowResponsesAreNotCachedAsEmptyData(string invalidResponse)
    {
        var calls = new Dictionary<string, int>();
        var fail = true;
        var service = CreatePerformanceReportsService(calls, request =>
            fail && request.RequestUri!.AbsolutePath == "/rest/v1/rpc/get_admin_report_inputs" ? JsonResponse(invalidResponse) : null);

        await Assert.ThrowsAsync<JsonException>(() =>
            service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        fail = false;
        var recovered = await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false);
        Assert.AreSame(recovered, await service.GetSubscriberReportsAsync("admin@example.com", includeEngagement: false));
        Assert.AreEqual(2, calls["/rest/v1/rpc/get_admin_report_inputs"]);
    }

    private static AdminAnalyticsSnapshot UsageSnapshot(int views) => AdminAnalyticsSnapshot.Empty with
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        StorySummary = AdminAnalyticsSnapshot.Empty.StorySummary with { TotalViews = views }
    };

    [TestMethod]
    public async Task MembershipMonthlySalesMatchesRevenueIncludingPaidOrders()
    {
        var inputs = $$"""
            {"subscribers":[],"subscriptions":[],"tiers":[],"events":[],"recoveries":[],"carts":[],"feedback":[],
             "orders":[{"order_id":"00000000-0000-0000-0000-000000000001","total_price_zar":149,"payment_status":"paid","paid_at":"{{DateTimeOffset.UtcNow:O}}"}]}
            """;
        var sections = new[] { "subscribers", "subscriptions", "events", "tiers", "orders", "carts", "recoveries" };
        var versions = sections.ToDictionary(key => key, _ => "original");
        var calls = new Dictionary<string, int>();
        var paid = true;
        var service = CreatePerformanceReportsService(calls, request =>
        {
            using var args = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var known = args.RootElement.GetProperty("p_known_versions");
            var unchanged = known.ValueKind == JsonValueKind.Object && known.GetProperty("orders").GetString() == versions["orders"];
            using var data = JsonDocument.Parse(inputs);
            var rows = data.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value);
            if (unchanged || !paid) rows["orders"] = JsonSerializer.SerializeToElement(Array.Empty<object>());
            return JsonResponse(JsonSerializer.Serialize(new { versions, inputs = rows }));
        });
        var membership = await service.GetSubscriberReportsAsync("admin@example.com", panel: AdminReportPanel.Membership);
        var revenue = await service.GetSubscriberReportsAsync("admin@example.com", panel: AdminReportPanel.Revenue);
        var month = membership.SalesAndRevenue.Single(row => row.PeriodKey == "this_month");
        Assert.AreEqual(149m, month.RevenueZar);
        Assert.AreEqual(month, revenue.SalesAndRevenue.Single(row => row.PeriodKey == "this_month"));

        paid = false;
        versions["orders"] = "deleted";
        var refreshed = await service.GetSubscriberReportsAsync("admin@example.com", forceRefresh: true, panel: AdminReportPanel.Revenue);
        Assert.AreEqual(0m, refreshed.SalesAndRevenue.Single(row => row.PeriodKey == "this_month").RevenueZar);
        Assert.AreEqual(3, calls["/rest/v1/rpc/get_admin_financial_report_inputs"]);
    }

    [TestMethod]
    public async Task ColdFinancialReportShowsActualOverviewWhileDetailsArePending()
    {
        var full = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var overview = new TaskCompletionSource<AdminFinancialOverview>(TaskCreationOptions.RunContinuationsAsynchronously);
        var now = DateTimeOffset.UtcNow;
        var subscriber = Guid.NewGuid();
        var monthly = new { subscriber_id = subscriber, tier_code = "monthly", provider = "paystack", status = "active",
            next_renewal_at = now.AddDays(10), billing_amount_zar = 79, billing_period_months = 1 };
        var free = new { subscriber_id = subscriber, tier_code = "gratis", provider = "free", status = "active" };
        var excluded = new { subscriber_id = subscriber, tier_code = "monthly", provider = "paystack", status = "active",
            source_system = "admin_override", billing_amount_zar = 300, next_renewal_at = now.AddDays(2) };
        var service = CreateService(new AnalyticsAsyncHandler(async (request, token) =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users")) return JsonResponse("""[{"email":"admin@example.com"}]""");
            if (request.RequestUri!.AbsolutePath.EndsWith("/get_admin_financial_overview", StringComparison.Ordinal))
                return JsonResponse(JsonSerializer.Serialize(new { as_of = now, groups = new object[] {
                    new { quantity = 2, sample = monthly }, new { quantity = 3, sample = free }, new { quantity = 1, sample = excluded } } }));
            Assert.AreEqual("/rest/v1/rpc/get_admin_financial_report_inputs", request.RequestUri.AbsolutePath);
            return await full.Task.WaitAsync(token);
        }));
        var report = service.GetSubscriberReportsAsync("admin@example.com", panel: AdminReportPanel.Revenue,
            onOverviewLoaded: value => { overview.TrySetResult(value); return Task.CompletedTask; });
        var keys = new[] { "subscribers", "subscriptions", "events", "tiers", "orders", "carts", "recoveries" };
        var rows = keys.Append("feedback").ToDictionary(key => key, _ => Array.Empty<object>());
        rows["subscriptions"] = [monthly, monthly, free, free, free, excluded];
        try
        {
            var first = await overview.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(new AdminFinancialOverview(3, 2, 158m), first);
            Assert.IsFalse(report.IsCompleted);
        }
        finally
        {
            full.TrySetResult(JsonResponse(JsonSerializer.Serialize(new { versions = keys.ToDictionary(key => key, _ => "v"), inputs = rows })));
        }
        Assert.AreEqual(158m, (await report).RecurringRevenue.Single(row => row.SegmentKey == "total").MonthlyRecurringRevenueZar);
    }

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

            Assert.AreEqual(HttpMethod.Post, request.Method);
            if (path == "/rest/v1/rpc/get_admin_engagement_periods")
                return JsonResponse(JsonSerializer.Serialize(new { periods = new[] { "today", "this_week", "this_month", "year_to_date", "all_time" }
                    .Select(key => new AdminVisitsViewsLoginsMetric(key, 0, 0, 0)) }));
            if (path == "/rest/v1/rpc/get_admin_financial_report_inputs") return responseOverride!(request)!;
            Assert.AreEqual("/rest/v1/rpc/get_admin_report_inputs", path);
            return responseOverride?.Invoke(request) ?? JsonResponse("""
                {"subscribers":[],"subscriptions":[],"tiers":[],"events":[],"orders":[],"recoveries":[],"carts":[],"feedback":[]}
                """);
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
