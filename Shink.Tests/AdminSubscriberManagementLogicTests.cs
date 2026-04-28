using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Pages;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class AdminSubscriberManagementLogicTests
{
    [TestMethod]
    public void BuildSubscriberCsv_EscapesCommasQuotesAndNewlines()
    {
        var subscribers = new[]
        {
            new AdminSubscriberRecord(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "renske@example.com",
                "Ren,ske",
                "Tester",
                "Ren\"ske",
                "0821234567",
                null,
                new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero),
                ["all_stories_monthly"],
                "paystack",
                "shink_app",
                "active",
                new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                null,
                new DateTimeOffset(2026, 4, 3, 10, 0, 0, TimeSpan.Zero),
                "admin@example.com",
                "Needs\nreview")
        };

        var csv = AdminSubscriberManagementLogic.BuildSubscriberCsv(subscribers);

        StringAssert.Contains(csv, "\"Ren,ske\"");
        StringAssert.Contains(csv, "\"Ren\"\"ske\"");
        StringAssert.Contains(csv, "\"Needs\nreview\"");
    }

    [TestMethod]
    public void NormalizeSelectedSubscriberIds_RemovesEmptyAndDuplicateIds()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var result = AdminSubscriberManagementLogic.NormalizeSelectedSubscriberIds(
            [Guid.Empty, first, first, second]);

        CollectionAssert.AreEqual(new[] { first, second }, result.ToArray());
    }

    [TestMethod]
    public void ValidateManualAccessExpiry_RejectsMissingOrPastExpiry()
    {
        var now = new DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);

        Assert.IsFalse(AdminSubscriberManagementLogic.ValidateManualAccessExpiry(null, now).IsValid);
        Assert.IsFalse(AdminSubscriberManagementLogic.ValidateManualAccessExpiry(now.AddMinutes(-1), now).IsValid);
        Assert.IsTrue(AdminSubscriberManagementLogic.ValidateManualAccessExpiry(now.AddDays(1), now).IsValid);
    }
}
