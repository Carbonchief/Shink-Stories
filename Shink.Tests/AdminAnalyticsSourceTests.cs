using System.Runtime.CompilerServices;
using System.Collections;
using System.Reflection;
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

        StringAssert.Contains(admin, "admin-subscriber-analytics-section");
        StringAssert.Contains(admin, "SubscriberReports.MembershipStats");
        StringAssert.Contains(admin, "GetSubscriberMetric(\"today\")");
        StringAssert.Contains(admin, "GetSubscriberMetric(\"this_month\")");
        StringAssert.Contains(admin, "GetSubscriberMetric(\"this_year\")");
        StringAssert.Contains(admin, "Cancellations");
        StringAssert.Contains(admin, "New subscribers today");
        StringAssert.Contains(admin, "Cancelled subscriptions");
        StringAssert.Contains(admin, "GetAnalyticsSubscriberReportsAsync");
        StringAssert.Contains(admin, "SubscriberAdminView.AllSubscribers");

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

        StringAssert.Contains(admin, "admin-revenue-analytics-section");
        StringAssert.Contains(admin, "GetSubscriberSalesMetric(\"today\")");
        StringAssert.Contains(admin, "GetSubscriberSalesMetric(\"this_month\")");
        StringAssert.Contains(admin, "GetSubscriberSalesMetric(\"this_year\")");
        StringAssert.Contains(admin, "GetSubscriberSalesMetric(\"all_time\")");
        StringAssert.Contains(admin, "RevenueZar");
        StringAssert.Contains(admin, "SalesCount");
        StringAssert.Contains(admin, "RecoveredRevenueZar");
    }

    [TestMethod]
    public void AnalyticsPanelsDoNotStretchSparseSections()
    {
        var css = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Admin.razor.css"));

        StringAssert.Contains(css, ".admin-analytics-layout");
        StringAssert.Contains(css, "align-items: start;");
        StringAssert.Contains(css, ".admin-analytics-panel");
        StringAssert.Contains(css, "align-content: start;");
    }

    [TestMethod]
    public void SubscriberAnalyticsCountsCurrentDistinctSubscribersOnly()
    {
        var now = DateTimeOffset.Now.AddMinutes(-5);
        var subscriberId = Guid.NewGuid();
        var cancelledSubscriberId = Guid.NewGuid();
        var rows = CreateSubscriptionRows(
            CreateSubscriptionRow(subscriberId, "shink_app", "active", now, null),
            CreateSubscriptionRow(subscriberId, "shink_app", "active", now.AddMinutes(-1), null),
            CreateSubscriptionRow(cancelledSubscriberId, "discount_code", "cancelled", now, now),
            CreateSubscriptionRow(Guid.NewGuid(), "wordpress_pmpro", "active", now, now),
            CreateSubscriptionRow(Guid.NewGuid(), "admin_override", "active", now, now),
            CreateSubscriptionRow(Guid.NewGuid(), "shink_app", "failed", now, null));

        var metrics = InvokeBuildMembershipStatsMetrics(rows);
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
            CreateSubscriptionRow(validSubscriberId, "shink_app", "active", now, null),
            CreateSubscriptionRow(validSubscriberId, "shink_app", "active", now.AddMinutes(-2), null),
            CreateSubscriptionRow(importedSubscriberId, "shink_app", "active", now, null, tierCode: "gratis", providerPaymentId: "gratis-20260430"),
            CreateSubscriptionRow(Guid.NewGuid(), "admin_override", "active", now, null, tierCode: "gratis", providerPaymentId: "gratis-user-1"),
            CreateSubscriptionRow(Guid.NewGuid(), "wordpress_pmpro", "active", now, null, tierCode: "gratis", providerPaymentId: "gratis-user-2"));

        var metrics = InvokeBuildMembershipTrendMetrics(rows);
        var today = metrics.Single(metric => metric.PeriodType == "day" && metric.PeriodKey == now.Date.ToString("yyyy-MM-dd"));

        Assert.AreEqual(1, today.Signups);
        Assert.AreEqual(0, today.Cancellations);

        var stats = InvokeBuildMembershipStatsMetrics(rows);
        var todayStats = stats.Single(metric => metric.PeriodKey == "today");

        Assert.AreEqual(1, todayStats.Signups);
        Assert.AreEqual(0, todayStats.Cancellations);
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

        var metrics = InvokeBuildSalesRevenueMetrics(wordpressSnapshot, rows);
        var today = metrics.Single(metric => metric.PeriodKey == "today");

        Assert.AreEqual(2, today.SalesCount);
        Assert.AreEqual(148.45m, today.RevenueZar);
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

    private static IReadOnlyList<AdminMembershipStatsMetric> InvokeBuildMembershipStatsMetrics(object rows)
    {
        var method = typeof(SupabaseAdminManagementService).GetMethod(
            "BuildMembershipStatsMetrics",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        var result = method.Invoke(null, [rows]);
        Assert.IsNotNull(result);
        return ((IEnumerable<AdminMembershipStatsMetric>)result).ToArray();
    }

    private static IReadOnlyList<AdminSubscriberTrendMetric> InvokeBuildMembershipTrendMetrics(object rows)
    {
        var method = typeof(SupabaseAdminManagementService).GetMethod(
            "BuildMembershipTrendMetrics",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        var result = method.Invoke(null, [rows]);
        Assert.IsNotNull(result);
        return ((IEnumerable<AdminSubscriberTrendMetric>)result).ToArray();
    }

    private static IReadOnlyList<AdminSalesRevenueMetric> InvokeBuildSalesRevenueMetrics(object wordpressSnapshot, object rows)
    {
        var method = typeof(SupabaseAdminManagementService).GetMethod(
            "BuildSalesRevenueMetrics",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(method);
        var tierDetails = CreateEmptyTierDetails();
        var result = method.Invoke(null, [wordpressSnapshot, rows, tierDetails]);
        Assert.IsNotNull(result);
        return ((IEnumerable<AdminSalesRevenueMetric>)result).ToArray();
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

    private static object CreateSubscriptionRow(
        Guid subscriberId,
        string sourceSystem,
        string status,
        DateTimeOffset? subscribedAt,
        DateTimeOffset? cancelledAt,
        decimal? billingAmountZar = null,
        string? tierCode = null,
        string? providerPaymentId = null)
    {
        var rowType = GetSubscriptionRowType();
        var row = Activator.CreateInstance(rowType)!;
        SetProperty(row, "SubscriptionId", Guid.NewGuid());
        SetProperty(row, "SubscriberId", subscriberId);
        SetProperty(row, "SourceSystem", sourceSystem);
        SetProperty(row, "Status", status);
        SetProperty(row, "SubscribedAt", subscribedAt);
        SetProperty(row, "CancelledAt", cancelledAt);
        SetProperty(row, "BillingAmountZar", billingAmountZar);
        SetProperty(row, "TierCode", tierCode);
        SetProperty(row, "ProviderPaymentId", providerPaymentId);
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
