using System.Runtime.CompilerServices;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class AdminAnalyticsSourceTests
{
    [TestMethod]
    public void AnalyticsTabShowsSubscriberAnalyticsAndLoadsSubscriberReports()
    {
        var admin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));

        StringAssert.Contains(admin, "admin-subscriber-analytics-section");
        StringAssert.Contains(admin, "SubscriberReports.MembershipStats");
        StringAssert.Contains(admin, "GetSubscriberMetric(\"today\")");
        StringAssert.Contains(admin, "GetSubscriberMetric(\"this_month\")");
        StringAssert.Contains(admin, "GetSubscriberMetric(\"this_year\")");
        StringAssert.Contains(admin, "Cancellations");
        StringAssert.Contains(admin, "New subscribers today");
        StringAssert.Contains(admin, "Cancelled subscriptions");
        StringAssert.Contains(admin, "GetAnalyticsSubscriberReportsAsync");
        StringAssert.Contains(admin, "admin-analytics-tabs");
        StringAssert.Contains(admin, "@T(\"Inkomste\", \"Revenue\")");
        StringAssert.Contains(admin, "@T(\"Gebruik\", \"Usage\")");
        StringAssert.Contains(admin, "SubscriberAdminView.AllSubscribers");
        StringAssert.Contains(admin, "admin-subscriber-analytics-summary-layout");
        StringAssert.Contains(admin, "admin-subscriber-analytics-cards");
        StringAssert.Contains(admin, "SelectedSubscriberDrilldownPeriod");
        StringAssert.Contains(admin, "FilteredSubscriberMembershipDetails");
        StringAssert.Contains(admin, "IsSubscriberMembershipDetailInSelectedPeriod");
        StringAssert.Contains(admin, "SetSubscriberDrilldownPeriod");
        StringAssert.Contains(admin, "Items=\"FilteredSubscriberMembershipDetails\"");
        StringAssert.Contains(admin, "@T(\"Intekenaar detail\", \"Subscriber detail\")");
        StringAssert.Contains(admin, "id=\"subscriber-detail-period\"");
        StringAssert.Contains(admin, "BuildSubscriberDrilldownPeriodSummary()");
        StringAssert.Contains(admin, "BuildSubscriberDrilldownOptionLabel(metric)");
        StringAssert.Contains(admin, "SelectedTierDistributionPeriod");
        StringAssert.Contains(admin, "CurrentTierDistributionMetrics");
        StringAssert.Contains(admin, "SetTierDistributionPeriod");
        StringAssert.Contains(admin, "ChartSeries=\"ActiveMembersPerLevelChartSeries\"");
        Assert.IsFalse(admin.Contains("admin-subscriber-analytics-table", StringComparison.Ordinal));
        StringAssert.Contains(service, "AdminSubscriberMembershipDetailRecord");
        StringAssert.Contains(service, "MembershipDetails");
        StringAssert.Contains(service, "AdminTierDistributionMetric");
        StringAssert.Contains(service, "PeriodKey");

        var subscriberViewsStart = admin.IndexOf("private IReadOnlyList<SubscriberAdminView> SubscriberViews", StringComparison.Ordinal);
        Assert.AreNotEqual(-1, subscriberViewsStart);
        var subscriberViewsEnd = admin.IndexOf("];", subscriberViewsStart, StringComparison.Ordinal);
        Assert.AreNotEqual(-1, subscriberViewsEnd);
        var subscriberViewsBlock = admin[subscriberViewsStart..(subscriberViewsEnd + 2)];
        Assert.IsFalse(subscriberViewsBlock.Contains("SubscriberAdminView.MembershipStats", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnalyticsTabShowsRevenueAnalytics()
    {
        var admin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));

        StringAssert.Contains(admin, "admin-revenue-analytics-section");
        StringAssert.Contains(admin, "GetSubscriberSalesMetric(\"today\")");
        StringAssert.Contains(admin, "GetSubscriberSalesMetric(\"this_month\")");
        StringAssert.Contains(admin, "GetSubscriberSalesMetric(\"this_year\")");
        StringAssert.Contains(admin, "GetSubscriberSalesMetric(\"all_time\")");
        StringAssert.Contains(admin, "SelectedRevenueDrilldownPeriod");
        StringAssert.Contains(admin, "FilteredRevenueSalesDetails");
        StringAssert.Contains(admin, "IsRevenueDetailInSelectedPeriod");
        StringAssert.Contains(admin, "SetRevenueDrilldownPeriod");
        StringAssert.Contains(admin, "Items=\"FilteredRevenueSalesDetails\"");
        StringAssert.Contains(admin, "@T(\"Verkope detail\", \"Sales detail\")");
        StringAssert.Contains(admin, "RevenueZar");
        StringAssert.Contains(admin, "SalesCount");
        StringAssert.Contains(admin, "RecoveredRevenueZar");
        StringAssert.Contains(service, "AdminSalesRevenueDetailRecord");
        StringAssert.Contains(service, "SalesDetails");
    }

    [TestMethod]
    public void UsageAnalyticsTabShowsStoryDrilldown()
    {
        var admin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260505_admin_story_analytics_drilldown.sql"));

        StringAssert.Contains(admin, "SelectedAnalyticsStorySlug");
        StringAssert.Contains(admin, "SelectedAnalyticsStory");
        StringAssert.Contains(admin, "@T(\"Storie statistieke\", \"Story stats\")");
        StringAssert.Contains(admin, "@T(\"Storie detail\", \"Story detail\")");
        StringAssert.Contains(admin, "StoryStatsSearch");
        StringAssert.Contains(admin, "SelectedStoryStatsQuickDatePeriod");
        StringAssert.Contains(admin, "id=\"story-stats-quick-date\"");
        StringAssert.Contains(admin, "OnStoryStatsQuickDateChanged");
        StringAssert.Contains(admin, "ApplyStoryStatsQuickDatePeriod");
        StringAssert.Contains(admin, "StoryStatsQuickDateLast7");
        StringAssert.Contains(admin, "StoryStatsQuickDateLast30");
        StringAssert.Contains(admin, "StoryStatsFromDate");
        StringAssert.Contains(admin, "StoryStatsToDate");
        StringAssert.Contains(admin, "FilteredAnalyticsStoryDetails");
        StringAssert.Contains(admin, "DoesAnalyticsStoryMatchDateFilters");
        StringAssert.Contains(admin, "Immediate=\"true\"");
        StringAssert.Contains(admin, "Items=\"FilteredAnalyticsStoryDetails\"");
        Assert.IsFalse(admin.Contains("Items=\"Analytics.TopStories\"", StringComparison.Ordinal));
        StringAssert.Contains(admin, "SelectedUsageActivityPeriod");
        StringAssert.Contains(admin, "UsageActivityPeriodToday");
        StringAssert.Contains(admin, "UsageActivityPeriodYesterday");
        StringAssert.Contains(admin, "UsageActivityPeriodLast7");
        StringAssert.Contains(admin, "UsageActivityPeriodLast30");
        StringAssert.Contains(admin, "UsageActivityChartSeries");
        StringAssert.Contains(admin, "UsageActivityChartLabels");
        StringAssert.Contains(admin, "ChartType=\"ChartType.Line\"");
        StringAssert.Contains(admin, "@T(\"Gebruik oor tyd\", \"Usage over time\")");
        StringAssert.Contains(admin, "@T(\"Karakter statistieke\", \"Character stats\")");
        StringAssert.Contains(admin, "CharacterStatsSearch");
        StringAssert.Contains(admin, "FilteredAnalyticsCharacters");
        StringAssert.Contains(admin, "Items=\"FilteredAnalyticsCharacters\"");
        StringAssert.Contains(admin, "admin-character-stats-scroll");
        StringAssert.Contains(admin, "admin-character-stats-table");
        Assert.IsFalse(admin.Contains("RowsPerPage=\"10\"", StringComparison.Ordinal));
        StringAssert.Contains(admin, "Immediate=\"true\"");
        Assert.IsFalse(admin.Contains("@T(\"Top karakters\", \"Top characters\")", StringComparison.Ordinal));
        StringAssert.Contains(admin, "Analytics.StoryAnalytics");
        StringAssert.Contains(admin, "BuildAnalyticsStoryDetails");
        StringAssert.Contains(admin, "(Analytics.TopStories ?? []).Where");
        StringAssert.Contains(admin, "Stories.OrderBy(story => story.Title,");
        StringAssert.Contains(admin, "CreateEmptyAnalyticsStoryDetail");

        StringAssert.Contains(service, "AdminAnalyticsStoryDetailRecord");
        StringAssert.Contains(service, "StoryAnalytics");
        StringAssert.Contains(service, "average_listened_seconds_per_session");
        StringAssert.Contains(service, "last_listen_at");

        StringAssert.Contains(migration, "'story_analytics'");
        StringAssert.Contains(migration, "story_analytics as (");
        StringAssert.Contains(migration, "'unique_listeners'");
        StringAssert.Contains(migration, "interval '29 days'");
        StringAssert.Contains(migration, "character_play_stats as (");
        StringAssert.Contains(migration, "from public.story_characters as c");
        Assert.IsFalse(migration.Contains("limit 5", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ResourceAndBlogAnalyticsExposePerItemBreakdowns()
    {
        var admin = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor"));
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "IAdminManagementService.cs"));
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260506_admin_analytics_item_page_breakdowns.sql"));

        StringAssert.Contains(admin, "@T(\"Afslae per hulpbron\", \"Downloads per resource\")");
        StringAssert.Contains(admin, "Analytics.ResourceDownloadSummary.Items");
        StringAssert.Contains(admin, "@T(\"Besoeke per blog-blad\", \"Visits per blog page\")");
        StringAssert.Contains(admin, "Analytics.BlogVisitSummary.Pages");

        StringAssert.Contains(service, "AdminAnalyticsResourceDownloadItemRecord");
        StringAssert.Contains(service, "AdminAnalyticsBlogVisitPageRecord");
        StringAssert.Contains(service, "[property: JsonPropertyName(\"items\")]");
        StringAssert.Contains(service, "[property: JsonPropertyName(\"pages\")]");

        StringAssert.Contains(migration, "resource_download_items as (");
        StringAssert.Contains(migration, "'{resource_download_summary,items}'");
        StringAssert.Contains(migration, "blog_visit_pages as (");
        StringAssert.Contains(migration, "'{blog_visit_summary,pages}'");
    }

    [TestMethod]
    public void AnalyticsPanelsDoNotStretchSparseSections()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));

        StringAssert.Contains(css, ".admin-analytics-layout");
        StringAssert.Contains(css, "align-items: start;");
        StringAssert.Contains(css, ".admin-analytics-panel");
        StringAssert.Contains(css, "align-content: start;");
        StringAssert.Contains(css, ".admin-analytics-tabs");
        StringAssert.Contains(css, ".admin-usage-summary-grid");
        StringAssert.Contains(css, "align-items: stretch;");
        StringAssert.Contains(css, "margin-bottom: 1rem;");
        StringAssert.Contains(css, ".admin-subscriber-analytics-summary-layout");
        StringAssert.Contains(css, ".admin-subscriber-analytics-cards");
        StringAssert.Contains(css, ".admin-subscriber-kpi-main");
        StringAssert.Contains(css, ".admin-subscriber-kpi-secondary");
        StringAssert.Contains(css, "grid-template-columns: minmax(0, 1fr) minmax(116px, 0.74fr);");
    }

    [TestMethod]
    public void SubscriberAnalyticsCountsCurrentDistinctSubscribersOnly()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var subscriberId = Guid.NewGuid();
        var cancelledSubscriberId = Guid.NewGuid();
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(subscriberId, "shink_app", "active", now, null, provider: "paystack", providerPaymentId: "paystack-new-today"),
            CreateSubscriptionRow(subscriberId, "shink_app", "active", now.AddMinutes(-1), null),
            CreateSubscriptionRow(cancelledSubscriberId, "discount_code", "cancelled", now, now, provider: "paystack", providerPaymentId: "paystack-cancelled-today"),
            CreateSubscriptionRow(Guid.NewGuid(), "wordpress_pmpro", "active", now, now),
            CreateSubscriptionRow(Guid.NewGuid(), "admin_override", "active", now, now),
            CreateSubscriptionRow(Guid.NewGuid(), "shink_app", "failed", now, null));
        var revenueEvents = CreateRevenueEvents(
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "paystack-new-today"),
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "paystack-cancelled-today"));

        var metrics = InvokeBuildMembershipStatsMetrics(rows, revenueEvents);
        var today = metrics.Single(metric => metric.PeriodKey == "today");

        Assert.AreEqual(2, today.Signups);
        Assert.AreEqual(1, today.Cancellations);
    }

    [TestMethod]
    public void SubscriberTrendExcludesImportedGratisBatch()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var validSubscriberId = Guid.NewGuid();
        var importedSubscriberId = Guid.NewGuid();
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(validSubscriberId, "shink_app", "active", now, null, providerPaymentId: "paystack-valid-subscriber", provider: "paystack"),
            CreateSubscriptionRow(validSubscriberId, "shink_app", "active", now.AddMinutes(-2), null),
            CreateSubscriptionRow(importedSubscriberId, "shink_app", "active", now, null, tierCode: "gratis", providerPaymentId: "gratis-20260430"),
            CreateSubscriptionRow(Guid.NewGuid(), "admin_override", "active", now, null, tierCode: "gratis", providerPaymentId: "gratis-user-1"),
            CreateSubscriptionRow(Guid.NewGuid(), "wordpress_pmpro", "active", now, null, tierCode: "gratis", providerPaymentId: "gratis-user-2"));
        var revenueEvents = CreateRevenueEvents(
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "paystack-valid-subscriber"));

        var metrics = InvokeBuildMembershipTrendMetrics(rows, revenueEvents);
        var today = metrics.Single(metric => metric.PeriodType == "day" && metric.PeriodKey == now.Date.ToString("yyyy-MM-dd"));

        Assert.AreEqual(1, today.Signups);
        Assert.AreEqual(0, today.Cancellations);

        var stats = InvokeBuildMembershipStatsMetrics(rows, revenueEvents);
        var todayStats = stats.Single(metric => metric.PeriodKey == "today");

        Assert.AreEqual(1, todayStats.Signups);
        Assert.AreEqual(0, todayStats.Cancellations);
    }

    [TestMethod]
    public void SubscriberAnalyticsExcludesPayfastFromNewSubscriberCounts()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var paystackSubscriberId = Guid.NewGuid();
        var payfastSubscriberId = Guid.NewGuid();
        var payfastNullProviderSubscriberId = Guid.NewGuid();
        var legacySubscriberId = Guid.NewGuid();
        var cancelledPayfastSubscriberId = Guid.NewGuid();
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(paystackSubscriberId, "shink_app", "active", now, null, provider: "paystack", providerPaymentId: "paystack-main-subscribe"),
            CreateSubscriptionRow(payfastSubscriberId, "shink_app", "active", now, null, provider: "payfast"),
            CreateSubscriptionRow(payfastNullProviderSubscriberId, "payfast", "active", now, null),
            CreateSubscriptionRow(legacySubscriberId, "shink_app", "active", now, null, provider: "paystack", providerPaymentId: "paystack-legacy-subscribe"),
            CreateSubscriptionRow(cancelledPayfastSubscriberId, "shink_app", "cancelled", now, now, provider: "payfast"));
        var revenueEvents = CreateRevenueEvents(
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "paystack-main-subscribe"),
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "paystack-legacy-subscribe"));

        var metrics = InvokeBuildMembershipStatsMetrics(rows, revenueEvents);
        var today = metrics.Single(metric => metric.PeriodKey == "today");

        Assert.AreEqual(2, today.Signups);
        Assert.AreEqual(0, today.Cancellations);

        var trend = InvokeBuildMembershipTrendMetrics(rows, revenueEvents);
        var todayTrend = trend.Single(metric => metric.PeriodType == "day" && metric.PeriodKey == now.Date.ToString("yyyy-MM-dd"));

        Assert.AreEqual(2, todayTrend.Signups);
        Assert.AreEqual(0, todayTrend.Cancellations);
    }

    [TestMethod]
    public void SubscriberCancellationCountsUsePaystackDisableWebhookEventsOnly()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var paystackSubscriberId = Guid.NewGuid();
        var secondPaystackSubscriberId = Guid.NewGuid();
        var payfastSubscriberId = Guid.NewGuid();
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(
                paystackSubscriberId,
                "shink_app",
                "active",
                now,
                now,
                provider: "paystack",
                providerPaymentId: "paystack-main-cancel"),
            CreateSubscriptionRow(
                secondPaystackSubscriberId,
                "shink_app",
                "active",
                now,
                now,
                provider: "paystack",
                providerTransactionId: "txn-second-cancel"),
            CreateSubscriptionRow(
                payfastSubscriberId,
                "shink_app",
                "cancelled",
                now,
                now,
                provider: "payfast"));
        var revenueEvents = CreateRevenueEvents(
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "paystack-main-cancel"),
            CreateRevenueEvent(now, 0m, "subscription.create", providerTransactionId: "txn-second-cancel"),
            CreateRevenueEvent(now, 0m, "subscription.disable", providerPaymentId: "paystack-main-cancel"),
            CreateRevenueEvent(now, 0m, "subscription.disable", providerTransactionId: "txn-second-cancel"),
            CreateRevenueEvent(now, 0m, "subscription.disable", providerPaymentId: "paystack-main-cancel"));

        var metrics = InvokeBuildMembershipStatsMetrics(rows, revenueEvents);
        var today = metrics.Single(metric => metric.PeriodKey == "today");

        Assert.AreEqual(2, today.Signups);
        Assert.AreEqual(2, today.Cancellations);

        var trend = InvokeBuildMembershipTrendMetrics(rows, revenueEvents);
        var todayTrend = trend.Single(metric => metric.PeriodType == "day" && metric.PeriodKey == now.Date.ToString("yyyy-MM-dd"));

        Assert.AreEqual(2, todayTrend.Signups);
        Assert.AreEqual(2, todayTrend.Cancellations);
    }

    [TestMethod]
    public void SubscriberAnalyticsCountsOnlyRowsWithPaystackSubscriptionCreateEvents()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var paystackWithCreateId = Guid.NewGuid();
        var paystackWithoutCreateId = Guid.NewGuid();
        var payfastSubscriberId = Guid.NewGuid();
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(
                paystackWithCreateId,
                "shink_app",
                "active",
                now,
                null,
                provider: "paystack",
                providerPaymentId: "sub_created"),
            CreateSubscriptionRow(
                paystackWithoutCreateId,
                "shink_app",
                "active",
                now,
                null,
                provider: "paystack",
                providerPaymentId: "sub_no_create"),
            CreateSubscriptionRow(
                payfastSubscriberId,
                "shink_app",
                "active",
                now,
                null,
                provider: "payfast"));

        var revenueEvents = CreateRevenueEvents(
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "sub_created"));

        var metrics = InvokeBuildMembershipStatsMetrics(rows, revenueEvents);
        var today = metrics.Single(metric => metric.PeriodKey == "today");

        Assert.AreEqual(1, today.Signups);
        Assert.AreEqual(0, today.Cancellations);

        var trend = InvokeBuildMembershipTrendMetrics(rows, revenueEvents);
        var todayTrend = trend.Single(metric => metric.PeriodType == "day" && metric.PeriodKey == now.Date.ToString("yyyy-MM-dd"));

        Assert.AreEqual(1, todayTrend.Signups);
        Assert.AreEqual(0, todayTrend.Cancellations);
    }

    [TestMethod]
    public void SubscriberAnalyticsCountsOnlyRowsWithActivePaystackSubscriptionCreateEvents()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var paystackSubscriberId = Guid.NewGuid();
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(
                paystackSubscriberId,
                "shink_app",
                "active",
                now,
                null,
                provider: "paystack",
                providerPaymentId: "sub_active_create"));

        var revenueEvents = CreateRevenueEvents(
            CreateRevenueEvent(now, 0m, "subscription.create", "sub_active_create", eventStatus: "active"));

        var metrics = InvokeBuildMembershipStatsMetrics(rows, revenueEvents);
        var today = metrics.Single(metric => metric.PeriodKey == "today");

        Assert.AreEqual(1, today.Signups);
    }

    [TestMethod]
    public void SubscriberMembershipDetailsExcludePayfastRows()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var paystackSubscriberId = Guid.NewGuid();
        var payfastSubscriberId = Guid.NewGuid();
        var payfastNullProviderSubscriberId = Guid.NewGuid();
        var paystackNullProviderSubscriberId = Guid.NewGuid();
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(paystackSubscriberId, "shink_app", "active", now, null, provider: "paystack", providerPaymentId: "paystack-list-1"),
            CreateSubscriptionRow(payfastSubscriberId, "shink_app", "active", now, null, provider: "payfast"),
            CreateSubscriptionRow(payfastNullProviderSubscriberId, "payfast", "active", now, null),
            CreateSubscriptionRow(paystackNullProviderSubscriberId, "shink_app", "active", now, null, providerPaymentId: "paystack-list-2"),
            CreateSubscriptionRow(Guid.NewGuid(), "wordpress_pmpro", "active", now, null));

        var subscribers = CreateSubscriberRows(
            CreateSubscriberRow(paystackSubscriberId, "paystack@shink.dev"),
            CreateSubscriberRow(payfastSubscriberId, "payfast@shink.dev"),
            CreateSubscriberRow(payfastNullProviderSubscriberId, "payfast-null@shink.dev"),
            CreateSubscriberRow(paystackNullProviderSubscriberId, "paystack-null@shink.dev"),
            CreateSubscriberRow(Guid.NewGuid(), "wordpress@shink.dev"));
        var revenueEvents = CreateRevenueEvents(
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "paystack-list-1"),
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "paystack-list-2"));

        var details = InvokeBuildSubscriberMembershipDetails(
            subscribers,
            rows,
            CreateEmptyTierDetails(),
            revenueEvents);

        Assert.AreEqual(2, details.Count);
        Assert.IsFalse(details.Any(detail => detail.Provider.Equals("payfast", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void SubscriberMembershipDetailsIncludeOnlyPaystackSubscriptionCreateWebhookRows()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var paystackWithCreateId = Guid.NewGuid();
        var paystackWithoutCreateId = Guid.NewGuid();
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(
                paystackWithCreateId,
                "shink_app",
                "active",
                now,
                null,
                provider: "paystack",
                providerPaymentId: "sub_created"),
            CreateSubscriptionRow(
                paystackWithoutCreateId,
                "shink_app",
                "active",
                now,
                null,
                provider: "paystack",
                providerPaymentId: "sub_no_create"));
        var subscribers = CreateSubscriberRows(
            CreateSubscriberRow(paystackWithCreateId, "paystack@shink.dev"),
            CreateSubscriberRow(paystackWithoutCreateId, "missing-event@shink.dev"));
        var revenueEvents = CreateRevenueEvents(
            CreateRevenueEvent(now, 0m, "subscription.create", providerPaymentId: "sub_created"));

        var details = InvokeBuildSubscriberMembershipDetails(
            subscribers,
            rows,
            CreateEmptyTierDetails(),
            revenueEvents);

        Assert.AreEqual(1, details.Count);
        Assert.AreEqual("paystack@shink.dev", details.Single().Email);
    }

    [TestMethod]
    public void RevenueAnalyticsUsesRecordedLedgerAmountsOnly()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(Guid.NewGuid(), "shink_app", "active", now, null, 123.45m),
            CreateSubscriptionRow(Guid.NewGuid(), "discount_code", "active", now, null, 25m),
            CreateSubscriptionRow(Guid.NewGuid(), "shink_app", "active", now, null, 0m),
            CreateSubscriptionRow(Guid.NewGuid(), "wordpress_pmpro", "active", now, null, 700m),
            CreateSubscriptionRow(Guid.NewGuid(), "admin_override", "active", now, null, 500m),
            CreateSubscriptionRow(Guid.NewGuid(), "shink_app", "failed", now, null, 600m));
        var wordpressSnapshot = CreateWordPressRevenueSnapshot("today", 99, 9999m);
        var revenueEvents = CreateRevenueEvents();

        var metrics = InvokeBuildSalesRevenueMetrics(wordpressSnapshot, rows, revenueEvents);
        var today = metrics.Single(metric => metric.PeriodKey == "today");

        Assert.AreEqual(2, today.SalesCount);
        Assert.AreEqual(148.45m, today.RevenueZar);
    }

    [TestMethod]
    public void RevenueAnalyticsUsesPaystackChargeEventsWhenRecentRowsHaveNoRecordedAmounts()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var olderThanEventCoverage = now.AddDays(-10);
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(Guid.NewGuid(), "shink_app", "active", now, null, null, tierCode: "all_stories_monthly", providerPaymentId: "SUB_live_1", provider: "paystack"),
            CreateSubscriptionRow(Guid.NewGuid(), "shink_app", "active", now, null, null, tierCode: "all_stories_monthly", providerPaymentId: "checkout-live-1", provider: "paystack"),
            CreateSubscriptionRow(Guid.NewGuid(), "shink_app", "active", now.AddMinutes(-30), null, 149m, tierCode: "all_stories_monthly", providerPaymentId: "payfast-live-1", provider: "payfast"),
            CreateSubscriptionRow(Guid.NewGuid(), "shink_app", "active", olderThanEventCoverage, null, 79m, tierCode: "all_stories_monthly", providerPaymentId: "historic-paystack-1", provider: "paystack"));
        var wordpressSnapshot = CreateWordPressRevenueSnapshot("today", 99, 9999m);
        var revenueEvents = CreateRevenueEvents(
            CreateRevenueEvent(now, 7900),
            CreateRevenueEvent(now.AddMinutes(-20), 5500));

        var metrics = InvokeBuildSalesRevenueMetrics(wordpressSnapshot, rows, revenueEvents);
        var today = metrics.Single(metric => metric.PeriodKey == "today");
        var allTime = metrics.Single(metric => metric.PeriodKey == "all_time");

        Assert.AreEqual(3, today.SalesCount);
        Assert.AreEqual(283m, today.RevenueZar);
        Assert.AreEqual(4, allTime.SalesCount);
        Assert.AreEqual(362m, allTime.RevenueZar);
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

    private static IReadOnlyList<AdminMembershipStatsMetric> InvokeBuildMembershipStatsMetrics(
        object rows,
        object? revenueEvents = null)
    {
        var method = typeof(SupabaseAdminManagementService).GetMethod(
            "BuildMembershipStatsMetrics",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        var events = revenueEvents ?? CreateRevenueEvents();
        var paystackCreateIdentifiers = BuildPaystackSubscriptionCreateIdentifiers(events);
        var result = method.Invoke(null, [rows, paystackCreateIdentifiers, events]);
        Assert.IsNotNull(result);
        return ((IEnumerable<AdminMembershipStatsMetric>)result).ToArray();
    }

    private static IReadOnlyList<AdminSubscriberTrendMetric> InvokeBuildMembershipTrendMetrics(
        object rows,
        object? revenueEvents = null)
    {
        var method = typeof(SupabaseAdminManagementService).GetMethod(
            "BuildMembershipTrendMetrics",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        var events = revenueEvents ?? CreateRevenueEvents();
        var paystackCreateIdentifiers = BuildPaystackSubscriptionCreateIdentifiers(events);
        var result = method.Invoke(null, [rows, paystackCreateIdentifiers, events]);
        Assert.IsNotNull(result);
        return ((IEnumerable<AdminSubscriberTrendMetric>)result).ToArray();
    }

    private static IReadOnlyList<AdminSalesRevenueMetric> InvokeBuildSalesRevenueMetrics(object wordpressSnapshot, object rows, object revenueEvents)
    {
        var method = typeof(SupabaseAdminManagementService).GetMethod(
            "BuildSalesRevenueMetrics",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        var tierDetails = CreateEmptyTierDetails();
        var result = method.Invoke(null, [wordpressSnapshot, rows, tierDetails, revenueEvents]);
        Assert.IsNotNull(result);
        return ((IEnumerable<AdminSalesRevenueMetric>)result).ToArray();
    }

    private static IReadOnlyList<AdminSubscriberMembershipDetailRecord> InvokeBuildSubscriberMembershipDetails(
        object subscribers,
        object rows,
        object tierDetails,
        object? revenueEvents = null)
    {
        var method = typeof(SupabaseAdminManagementService).GetMethod(
            "BuildSubscriberMembershipDetails",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        var events = revenueEvents ?? CreateRevenueEvents();
        var paystackCreateIdentifiers = BuildPaystackSubscriptionCreateIdentifiers(events);
        var result = method.Invoke(null, [subscribers, rows, tierDetails, paystackCreateIdentifiers]);
        Assert.IsNotNull(result);
        return ((IEnumerable<AdminSubscriberMembershipDetailRecord>)result).ToArray();
    }

    private static object BuildPaystackSubscriptionCreateIdentifiers(object revenueEvents)
    {
        var method = typeof(SupabaseAdminManagementService).GetMethod(
            "BuildPaystackSubscriptionCreateIdentifiers",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        var result = method.Invoke(null, [revenueEvents]);
        Assert.IsNotNull(result);
        return result;
    }

    private static object CreateSubscriptionRows(params object[] rows)
    {
        var rowType = GetSubscriptionRowType();
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        foreach (var row in rows)
        {
            list.Add(row);
        }

        return list;
    }

    private static object CreateSubscriberRows(params object[] rows)
    {
        var rowType = GetSubscriberRowType();
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        foreach (var row in rows)
        {
            list.Add(row);
        }

        return list;
    }

    private static object CreateSubscriptionRow(
        Guid subscriberId,
        string sourceSystem,
        string status,
        DateTimeOffset? subscribedAt,
        DateTimeOffset? cancelledAt,
        decimal? billingAmountZar = null,
        string? tierCode = null,
        string? providerPaymentId = null,
        string? provider = null,
        string? providerTransactionId = null)
    {
        var rowType = GetSubscriptionRowType();
        var row = Activator.CreateInstance(rowType)!;
        SetProperty(row, "SubscriptionId", Guid.NewGuid());
        SetProperty(row, "SubscriberId", subscriberId);
        SetProperty(row, "SourceSystem", sourceSystem);
        SetProperty(row, "Provider", provider);
        SetProperty(row, "Status", status);
        SetProperty(row, "SubscribedAt", subscribedAt);
        SetProperty(row, "CancelledAt", cancelledAt);
        SetProperty(row, "BillingAmountZar", billingAmountZar);
        SetProperty(row, "TierCode", tierCode);
        SetProperty(row, "ProviderPaymentId", providerPaymentId);
        SetProperty(row, "ProviderTransactionId", providerTransactionId);
        return row;
    }

    private static object CreateSubscriberRow(Guid subscriberId, string email)
    {
        var rowType = GetSubscriberRowType();
        var row = Activator.CreateInstance(rowType)!;
        SetProperty(row, "SubscriberId", subscriberId);
        SetProperty(row, "Email", email);
        SetProperty(row, "FirstName", null);
        SetProperty(row, "LastName", null);
        SetProperty(row, "DisplayName", null);
        SetProperty(row, "MobileNumber", null);
        SetProperty(row, "ProfileImageUrl", null);
        SetProperty(row, "CreatedAt", DateTimeOffset.Now);
        SetProperty(row, "UpdatedAt", DateTimeOffset.Now);
        SetProperty(row, "DisabledAt", null);
        SetProperty(row, "DisabledByAdminEmail", null);
        SetProperty(row, "DisabledReason", null);
        return row;
    }

    private static Type GetSubscriptionRowType()
    {
        var type = typeof(SupabaseAdminManagementService).GetNestedType(
            "SubscriptionRow",
            BindingFlags.NonPublic);

        Assert.IsNotNull(type);
        return type;
    }

    private static Type GetSubscriberRowType()
    {
        var type = typeof(SupabaseAdminManagementService).GetNestedType(
            "SubscriberRow",
            BindingFlags.NonPublic);

        Assert.IsNotNull(type);
        return type;
    }

    private static object CreateRevenueEvents(params object[] rows)
    {
        var rowType = GetRevenueEventRowType();
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        foreach (var row in rows)
        {
            list.Add(row);
        }

        return list;
    }

    private static object CreateRevenueEvent(
        DateTimeOffset receivedAt,
        decimal amountInCents,
        string eventType = "charge.success",
        string? providerPaymentId = null,
        string? providerTransactionId = null,
        string eventStatus = "success")
    {
        var rowType = GetRevenueEventRowType();
        var row = Activator.CreateInstance(rowType)!;
        SetProperty(row, "Provider", "paystack");
        SetProperty(row, "EventType", eventType);
        SetProperty(row, "EventStatus", eventStatus);
        SetProperty(row, "ReceivedAt", receivedAt);
        SetProperty(row, "ProviderPaymentId", providerPaymentId);
        SetProperty(row, "ProviderTransactionId", providerTransactionId);
        SetProperty(
            row,
            "Payload",
            JsonSerializer.Deserialize<JsonElement>($$"""
            {
              "data": {
                "amount": {{amountInCents}},
                "paid_at": "{{receivedAt:O}}"
              }
            }
            """));
        return row;
    }

    private static Type GetRevenueEventRowType()
    {
        var type = typeof(SupabaseAdminManagementService).GetNestedType(
            "RevenueEventRow",
            BindingFlags.NonPublic);

        Assert.IsNotNull(type);
        return type;
    }

    private static object CreateEmptyTierDetails()
    {
        var rowType = typeof(SupabaseAdminManagementService).GetNestedType(
            "SubscriptionTierRow",
            BindingFlags.NonPublic);

        Assert.IsNotNull(rowType);
        return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), rowType))!;
    }

    private static object CreateWordPressRevenueSnapshot(string periodKey, int sales, decimal revenue)
    {
        var snapshotType = typeof(SupabaseAdminManagementService).GetNestedType(
            "WordPressSubscriberReportsRpcSnapshot",
            BindingFlags.NonPublic);
        var metricType = typeof(SupabaseAdminManagementService).GetNestedType(
            "WordPressSalesRevenueRpcMetric",
            BindingFlags.NonPublic);

        Assert.IsNotNull(snapshotType);
        Assert.IsNotNull(metricType);

        var snapshot = Activator.CreateInstance(snapshotType)!;
        var metric = Activator.CreateInstance(metricType)!;
        SetProperty(snapshot, "HasWordPressData", true);
        SetProperty(metric, "PeriodKey", periodKey);
        SetProperty(metric, "Sales", sales);
        SetProperty(metric, "Revenue", revenue);

        var metrics = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(metricType))!;
        metrics.Add(metric);
        SetProperty(snapshot, "SalesAndRevenue", metrics);
        return snapshot;
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(property);
        property.SetValue(target, value);
    }
}
