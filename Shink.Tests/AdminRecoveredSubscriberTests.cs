using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Pages;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class AdminRecoveredSubscriberTests
{
    [TestMethod]
    public void RecoveryCountsUseSelectedPeriodAndCountEachSubscriberOnce()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var today = new DateTimeOffset(DateTime.Today.AddHours(12));
        var previousMonth = today.AddMonths(-1);
        var admin = CreateAdmin(
            Recovery(first, today), Recovery(first, today.AddHours(1)), Recovery(second, previousMonth));

        Assert.AreEqual(1, Count(admin, "today"));
        Assert.AreEqual(1, Count(admin, "this_month"));
        Assert.AreEqual(2, Count(admin, "all_time"));
        Set(admin, "SelectedSubscriberDrilldownPeriod", "today");
        var details = Get<IReadOnlyList<AdminRecoveredSubscriberDetailRecord>>(admin, "FilteredRecoveredSubscriberDetails");
        Assert.HasCount(1, details);
        Assert.AreEqual(today.AddHours(1), details.Single().RecoveredAt);
    }

    [TestMethod]
    public void FreeAccessFilterExcludesPaidRecoveriesFromCountsDetailsAndChart()
    {
        var now = DateTimeOffset.Now;
        var admin = CreateAdmin(Recovery(Guid.NewGuid(), now));
        Set(admin, "SelectedSubscriberAccessFilter", "free");

        Assert.AreEqual(0, Count(admin, "all_time"));
        Assert.IsEmpty(Get<IReadOnlyList<AdminRecoveredSubscriberDetailRecord>>(admin, "FilteredRecoveredSubscriberDetails"));
        Assert.AreEqual(0, TrendCount(admin, new("day", now.ToString("yyyy-MM-dd"), "Today", 0, 0)));

        Set(admin, "SelectedSubscriberAccessFilter", "paid");
        Assert.AreEqual(1, Count(admin, "all_time"));
    }

    [TestMethod]
    public void RecoveryChartGroupsByDayMonthAndYear()
    {
        var now = DateTimeOffset.Now;
        var subscriber = Guid.NewGuid();
        var admin = CreateAdmin(Recovery(subscriber, now), Recovery(subscriber, now.AddHours(-1)));

        Assert.AreEqual(1, TrendCount(admin, new("day", now.ToString("yyyy-MM-dd"), "Today", 0, 0)));
        Assert.AreEqual(1, TrendCount(admin, new("month", now.ToString("yyyy-MM"), "Month", 0, 0)));
        Assert.AreEqual(1, TrendCount(admin, new("year", now.ToString("yyyy"), "Year", 0, 0)));
        Assert.AreEqual(0, TrendCount(admin, new("month", now.AddMonths(-1).ToString("yyyy-MM"), "Previous", 0, 0)));
    }

    private static AdminRecoveredSubscriberDetailRecord Recovery(Guid subscriber, DateTimeOffset date) =>
        new(subscriber, "test@example.com", "Test", "all_stories_monthly", "Monthly", "paystack", date);

    private static Admin CreateAdmin(params AdminRecoveredSubscriberDetailRecord[] details)
    {
        var admin = new Admin();
        Set(admin, "SubscriberReports", AdminSubscriberReportsSnapshot.Empty with { RecoveredSubscriberDetails = details });
        return admin;
    }

    private static int Count(Admin admin, string period) => (int)typeof(Admin).GetMethod(
        "GetRecoveredSubscriberCount", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(admin, [period])!;

    private static int TrendCount(Admin admin, AdminSubscriberTrendMetric metric) => (int)typeof(Admin).GetMethod(
        "GetRecoveredSubscriberTrendCount", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(admin, [metric])!;

    private static void Set(Admin admin, string name, object value) => typeof(Admin).GetProperty(
        name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(admin, value);

    private static T Get<T>(Admin admin, string name) => (T)typeof(Admin).GetProperty(
        name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(admin)!;
}
