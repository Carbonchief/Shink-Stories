using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySqlConnector;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class WordPressImportDateConverterTests
{
    [TestMethod]
    public void ConvertToNullableDateTime_ReturnsUtcDate_ForMySqlDateTime()
    {
        var value = new MySqlDateTime(2024, 1, 8, 5, 20, 46, 0);

        var result = WordPressImportDateConverter.ConvertToNullableDateTime(value);

        Assert.AreEqual(
            new DateTimeOffset(2024, 1, 8, 5, 20, 46, TimeSpan.Zero),
            result);
    }

    [TestMethod]
    public void ConvertToNullableDateTime_ReturnsNull_ForZeroDate()
    {
        var value = DateTime.MinValue;

        var result = WordPressImportDateConverter.ConvertToNullableDateTime(value);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ResolveSubscribedAt_PrefersEarliestHistoricalDate()
    {
        var userRegistered = new DateTimeOffset(2023, 7, 24, 17, 41, 38, TimeSpan.Zero);
        var currentPeriodStart = new DateTimeOffset(2025, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var originalOrderTimestamp = new DateTimeOffset(2024, 1, 8, 5, 8, 24, TimeSpan.Zero);

        var result = WordPressImportDateConverter.ResolveSubscribedAt(
            userRegistered,
            currentPeriodStart,
            originalOrderTimestamp);

        Assert.AreEqual(originalOrderTimestamp, result);
    }

    [TestMethod]
    public void ResolveSubscribedAt_FallsBackToUserRegistered()
    {
        var userRegistered = new DateTimeOffset(2023, 7, 24, 17, 41, 38, TimeSpan.Zero);

        var result = WordPressImportDateConverter.ResolveSubscribedAt(
            userRegistered,
            null,
            null);

        Assert.AreEqual(userRegistered, result);
    }
}
