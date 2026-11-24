using System.Globalization;
using System.Text.Json;
using Shink.Components.Content;
using StoreOrderRow = Shink.Services.SupabaseStoreOrderService.StoreOrderRow;

namespace Shink.Services;

public sealed record AdminStoreAnalyticsSnapshot(
    DateOnly From, DateOnly Through, bool EngagementConfigured, AdminStoreEngagement? Engagement,
    AdminStoreSales? Sales, IReadOnlyList<AdminStoreProductAnalytics> Products);

public sealed record AdminStoreEngagement(
    long Visits, long Visitors, long BasketVisitors, long CheckoutVisitors, IReadOnlyDictionary<DateOnly, long> DailyVisitors,
    AdminStoreCheckoutAnalytics Checkout);

public sealed record AdminStoreCheckoutAnalytics(
    long PayClicks, long PayClickers, long ValidationFailures, long BlockedBrowsers,
    IReadOnlyList<AdminStoreCheckoutValidationIssue> Issues);

public sealed record AdminStoreCheckoutValidationIssue(string Issue, long BrowserFailures, long ServerFailures);

public sealed record AdminStoreSales(long PaidOrders, decimal GrossRevenueZar);

public sealed record AdminStoreProductAnalytics(
    string Slug, string Name, long Clickers, long Opens, long BasketVisitors, long UnitsSold);

internal readonly record struct StoreAnalyticsPeriod(DateOnly From, DateOnly Through)
{
    internal const string TimeZoneId = "Africa/Johannesburg";
    private static readonly TimeZoneInfo ShopTimeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public static StoreAnalyticsPeriod EndingToday(int days)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ShopTimeZone).DateTime);
        return new(today.AddDays(1 - days), today);
    }

    public DateTime FromUtc => TimeZoneInfo.ConvertTimeToUtc(From.ToDateTime(TimeOnly.MinValue), ShopTimeZone);
    public DateTime ToExclusiveUtc => TimeZoneInfo.ConvertTimeToUtc(Through.AddDays(1).ToDateTime(TimeOnly.MinValue), ShopTimeZone);
}

public sealed partial class SupabaseAdminManagementService
{
    public async Task<AdminStoreAnalyticsSnapshot> GetStoreAnalyticsAsync(
        string? adminEmail, int days, bool forceRefresh = false, CancellationToken cancellationToken = default,
        Func<AdminStoreAnalyticsSnapshot, Task>? onSalesLoaded = null)
    {
        if (days is not (7 or 30 or 90)) throw new ArgumentOutOfRangeException(nameof(days));

        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken, throwOnLookupFailure: true)
            ?? throw new UnauthorizedAccessException();
        var period = StoreAnalyticsPeriod.EndingToday(days);
        var engagementTask = postHogStoreAnalytics.GetAsync(period, forceRefresh, cancellationToken);
        var salesTask = FetchStoreSalesAnalyticsAsync(context, period, cancellationToken);
        var catalogTask = storeProductCatalog.GetAllProductsAsync(cancellationToken);
        async Task PublishSalesAsync()
        {
            await Task.WhenAll(salesTask, catalogTask);
            if (onSalesLoaded is not null && !engagementTask.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await onSalesLoaded(BuildStoreAnalytics(period, null, await salesTask, await catalogTask));
            }
        }

        await Task.WhenAll(engagementTask, PublishSalesAsync());
        return BuildStoreAnalytics(period, await engagementTask, await salesTask, await catalogTask);
    }

    private AdminStoreAnalyticsSnapshot BuildStoreAnalytics(StoreAnalyticsPeriod period, StoreEngagementReport? engagement,
        StoreSalesReport? sales, IReadOnlyList<StoreProduct> catalog)
    {
        var catalogNames = catalog.ToDictionary(product => product.Slug, product => product.Name, StringComparer.Ordinal);
        var products = catalog.Where(product => product.IsEnabled).Select(product => product.Slug)
            .Union(engagement?.Products.Keys ?? [], StringComparer.Ordinal)
            .Union(sales?.Products.Keys ?? [], StringComparer.Ordinal)
            .Select(slug =>
            {
                var interactions = engagement?.Products.GetValueOrDefault(slug);
                var productSales = sales?.Products.GetValueOrDefault(slug);
                return new AdminStoreProductAnalytics(slug,
                    catalogNames.GetValueOrDefault(slug) ?? productSales?.Name ?? slug,
                    interactions?.Clickers ?? 0L, interactions?.Opens ?? 0L,
                    interactions?.BasketVisitors ?? 0L, productSales?.Units ?? 0L);
            })
            .OrderByDescending(product => product.Clickers)
            .ThenByDescending(product => product.Opens)
            .ThenByDescending(product => product.UnitsSold)
            .ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(period.From, period.Through, postHogStoreAnalytics.IsConfigured, engagement?.Totals, sales?.Totals, products);
    }

    private async Task<StoreSalesReport?> FetchStoreSalesAnalyticsAsync(
        AdminOperationContext context, StoreAnalyticsPeriod period, CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var from = Uri.EscapeDataString(period.FromUtc.ToString("O", CultureInfo.InvariantCulture));
        var to = Uri.EscapeDataString(period.ToExclusiveUtc.ToString("O", CultureInfo.InvariantCulture));
        var path = "rest/v1/store_orders?select=order_id,product_slug,product_name,quantity,unit_price_zar,total_price_zar,items" +
            "&payment_status=eq.paid" +
            $"&or=(and(paid_at.gte.{from},paid_at.lt.{to}),and(paid_at.is.null,created_at.gte.{from},created_at.lt.{to}))" +
            $"&order=order_id.asc&limit={pageSize}";
        var products = new Dictionary<string, StoreProductSales>(StringComparer.Ordinal);
        long paidOrders = 0;
        decimal grossRevenue = 0;
        var pagePath = path;

        try
        {
            while (true)
            {
                var rows = await FetchRowsAsync<StoreOrderRow>(new Uri(context.BaseUri, pagePath), context.ApiKey,
                    cancellationToken, throwOnFailure: true);
                foreach (var row in rows)
                {
                    paidOrders++;
                    grossRevenue += row.TotalPriceZar;
                    foreach (var item in SupabaseStoreOrderService.MapItems(row))
                    {
                        if (item.ProductSlug == "pudo-locker-delivery") continue;

                        var units = products.GetValueOrDefault(item.ProductSlug)?.Units ?? 0L;
                        products[item.ProductSlug] = new(item.ProductName, units + item.Quantity);
                    }
                }

                if (rows.Count < pageSize) break;
                pagePath = path + $"&order_id=gt.{rows[^1].OrderId:D}";
            }

            return new(new(paidOrders, grossRevenue), products);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
                                          (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(exception, "Failed to load Winkel sales analytics.");
            return null;
        }
    }

    private sealed record StoreSalesReport(AdminStoreSales Totals, IReadOnlyDictionary<string, StoreProductSales> Products);
    private sealed record StoreProductSales(string Name, long Units);
}
