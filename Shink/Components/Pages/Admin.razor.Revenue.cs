using Shink.Services;

namespace Shink.Components.Pages;

public partial class Admin
{
    private RevenueDetails? _revenueDetails;
    private (AdminSubscriberReportsSnapshot Report, string Period, DateTime? From, DateTime? To, DateTime Day) _revenueKey;

    private sealed record RevenueDetails(
        IReadOnlyList<AdminSalesRevenueDetailRecord> Sales,
        IReadOnlyList<AdminRecoveredRevenueDetailRecord> Recoveries,
        decimal RecoveredAmount,
        AdminSalesRevenueMetric CustomTotal,
        IReadOnlyDictionary<string, AdminSalesRevenueMetric> Providers,
        DateTime ValidUntil)
    {
        public AdminSalesRevenueMetric ForProvider(string provider) =>
            Providers.GetValueOrDefault(provider) ?? new(provider, 0, 0m);
    }

    private RevenueDetails GetRevenueDetails()
    {
        var now = DateTime.Now;
        var report = SubscriberReports;
        var key = (report, SelectedRevenueDrilldownPeriod, RevenueCustomFromDate, RevenueCustomToDate, now.Date);
        if (_revenueDetails is not null && _revenueKey == key && now < _revenueDetails.ValidUntil)
        {
            return _revenueDetails;
        }

        var selected = GetRevenuePeriodBounds(SelectedRevenueDrilldownPeriod, now);
        var custom = GetRevenuePeriodBounds(RevenuePeriodCustom, now);
        var validUntil = DateTime.MaxValue;

        bool InSelectedPeriod(DateTime local)
        {
            // Current periods end at the instant of rendering. Revisit future-dated rows as they become eligible.
            if (selected.EndExclusive == now && local >= now && local < validUntil)
            {
                validUntil = local;
            }

            return InRange(local, selected);
        }

        var sales = new List<AdminSalesRevenueDetailRecord>();
        var customCount = 0;
        var customAmount = 0m;
        foreach (var sale in report.SalesDetails)
        {
            var local = sale.SoldAt.LocalDateTime;
            if (InRange(local, custom))
            {
                customCount++;
                customAmount += sale.RevenueZar;
            }

            if (InSelectedPeriod(local))
            {
                sales.Add(sale);
            }
        }

        var providers = sales.GroupBy(sale => sale.Provider ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => new AdminSalesRevenueMetric(group.Key, group.Count(), RoundRevenue(group.Sum(sale => sale.RevenueZar))),
                StringComparer.OrdinalIgnoreCase);
        var recoveries = report.RecoveredRevenueDetails
            .Where(row => InSelectedPeriod(row.RecoveredAt.LocalDateTime))
            .OrderByDescending(row => row.RecoveredAt)
            .ToArray();

        _revenueKey = key;
        return _revenueDetails = new(
            sales.OrderByDescending(row => row.SoldAt).ToArray(), recoveries, recoveries.Sum(row => row.RevenueZar),
            new(RevenuePeriodCustom, customCount, RoundRevenue(customAmount)), providers, validUntil);
    }

    private static bool InRange(DateTime value, (DateTime? StartInclusive, DateTime? EndExclusive) bounds) =>
        (bounds.StartInclusive is null || value >= bounds.StartInclusive) &&
        (bounds.EndExclusive is null || value < bounds.EndExclusive);

    private static decimal RoundRevenue(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
