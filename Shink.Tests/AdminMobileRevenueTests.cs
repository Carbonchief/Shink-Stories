using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Pages;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class AdminMobileRevenueTests
{
    [TestMethod]
    [DataRow("apple", "iOS · App Store")]
    [DataRow(" APPLE ", "iOS · App Store")]
    [DataRow("google_play", "Android · Google Play")]
    [DataRow("paystack", "Paystack")]
    [DataRow("payfast", "PayFast")]
    public void RevenueProviderLabelsIdentifyMobilePlatforms(string provider, string expected)
    {
        var label = Invoke(new Admin(), "BuildRevenueProviderLabel", provider);
        Assert.AreEqual(expected, label);
    }

    [TestMethod]
    public void MobileProviderSummariesUseTheSelectedDateRangeAndRecordedAmounts()
    {
        var start = new DateTime(2026, 10, 7);
        var admin = new Admin();
        Set(admin, "SubscriberReports", AdminSubscriberReportsSnapshot.Empty with
        {
            SalesDetails = [
                Sale("apple", start, 99m),
                Sale("APPLE", start.AddDays(1), 99m),
                Sale("google_play", start.AddDays(2), 87.50m),
                Sale("apple", start.AddTicks(-1), 990m),
                Sale("google_play", start.AddDays(3), 990m),
                Sale("paystack", start, 790m),
                Sale("payfast", start, 149m)
            ]
        });
        Set(admin, "SelectedRevenueDrilldownPeriod", "custom");
        Set(admin, "RevenueCustomFromDate", start);
        Set(admin, "RevenueCustomToDate", start.AddDays(2));

        var apple = (AdminSalesRevenueMetric)Invoke(admin, "GetSelectedRevenueProviderSalesMetric", "apple")!;
        Assert.AreEqual(2, apple.SalesCount);
        Assert.AreEqual(198m, apple.RevenueZar);
        var android = (AdminSalesRevenueMetric)Invoke(admin, "GetSelectedRevenueProviderSalesMetric", "google_play")!;
        Assert.AreEqual(1, android.SalesCount);
        Assert.AreEqual(87.50m, android.RevenueZar);

        var total = (AdminSalesRevenueMetric)Invoke(admin, "GetCustomRevenueSalesMetric")!;
        Assert.AreEqual(5, total.SalesCount);
        Assert.AreEqual(1224.50m, total.RevenueZar);

        Set(admin, "RevenueCustomFromDate", start.AddDays(10));
        Set(admin, "RevenueCustomToDate", start.AddDays(11));
        var empty = (AdminSalesRevenueMetric)Invoke(admin, "GetSelectedRevenueProviderSalesMetric", "apple")!;
        Assert.AreEqual(0, empty.SalesCount);
        Assert.AreEqual(0m, empty.RevenueZar);
    }

    [TestMethod]
    [DataRow("apple", true)]
    [DataRow("GOOGLE_PLAY", true)]
    [DataRow("paystack", false)]
    [DataRow("payfast", false)]
    public void OnlyMobileRevenueRowsShowThePlanValueLabel(string provider, bool expected)
    {
        var method = typeof(Admin).GetMethod("IsMobileStoreRevenueDetail", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.AreEqual(expected, method.Invoke(null, [Sale(provider, new DateTime(2026, 10, 7), 99m)]));
    }

    private static AdminSalesRevenueDetailRecord Sale(string provider, DateTime soldAt, decimal amount) =>
        new(new DateTimeOffset(soldAt), amount, "parent@example.com", "all_stories_monthly", "Monthly", provider,
            "shink_app", "receipt", "active", "subscriptions.billing_amount_zar");

    private static object? Invoke(Admin admin, string method, params object[] arguments) =>
        typeof(Admin).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(admin, arguments);

    private static void Set(Admin admin, string property, object value) =>
        typeof(Admin).GetProperty(property, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(admin, value);
}
