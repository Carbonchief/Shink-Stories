using MudBlazor;
using Shink.Services;

namespace Shink.Components.Pages;

public partial class Admin
{
    private const int ShopAnalyticsTabIndex = 8;
    private int _shopAnalyticsDays = 30;
    private int _loadingShopAnalyticsDays;
    private AdminStoreAnalyticsSnapshot? _shopAnalytics;
    private Task? _shopAnalyticsLoadTask;
    private CancellationTokenSource? _shopAnalyticsLoadCancellation;
    private bool _shopAnalyticsLoadFailed;
    private bool IsLoadingShopAnalytics => _shopAnalyticsLoadCancellation is not null;

    private static readonly ChartOptions ShopAnalyticsChartOptions = new()
    {
        ShowLegend = false
    };

    private Task ChangeShopAnalyticsPeriodAsync() => EnsureShopAnalyticsAsync(forceRefresh: false);

    private Task EnsureShopAnalyticsAsync(bool forceRefresh)
    {
        if (_shopAnalyticsLoadTask is { IsCompleted: false } && _loadingShopAnalyticsDays == _shopAnalyticsDays)
        {
            return _shopAnalyticsLoadTask;
        }

        var period = StoreAnalyticsPeriod.EndingToday(_shopAnalyticsDays);
        if (!forceRefresh && _shopAnalytics is { Sales: not null } report &&
            (!report.EngagementConfigured || report.Engagement is not null) &&
            report.From == period.From && report.Through == period.Through)
        {
            return Task.CompletedTask;
        }

        return _shopAnalyticsLoadTask = LoadShopAnalyticsAsync(_shopAnalyticsDays, forceRefresh);
    }

    private async Task LoadShopAnalyticsAsync(int days, bool forceRefresh)
    {
        _shopAnalyticsLoadCancellation?.Cancel();
        using var load = new CancellationTokenSource();
        _shopAnalyticsLoadCancellation = load;
        _loadingShopAnalyticsDays = days;
        _shopAnalytics = null;
        _shopAnalyticsLoadFailed = false;

        try
        {
            var report = await AdminManagementService.GetStoreAnalyticsAsync(CurrentAdminEmail, days, forceRefresh, load.Token);
            if (!load.IsCancellationRequested) _shopAnalytics = report;
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            // The selected period changed or the component was disposed.
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Failed to load admin shop analytics.");
            if (!load.IsCancellationRequested) _shopAnalyticsLoadFailed = true;
        }
        finally
        {
            if (_shopAnalyticsLoadCancellation == load) _shopAnalyticsLoadCancellation = null;
        }
    }

    private string FormatShopAnalyticsPeriod(AdminStoreAnalyticsSnapshot report) =>
        $"{report.From.ToString("d MMM yyyy", GetAdminCulture())} – {report.Through.ToString("d MMM yyyy", GetAdminCulture())}";

    private string FormatShopAnalyticsCount(long? count) => count?.ToString("N0", GetAdminCulture()) ?? "—";

    private string GetCheckoutValidationIssueLabel(string issue) => issue switch
    {
        "name" => T("Naam", "Name"),
        "email" => T("E-posadres", "Email address"),
        "phone" => T("Selfoonnommer", "Mobile number"),
        "address" => T("Afleweringsadres of PUDO-locker", "Delivery address or PUDO locker"),
        "suburb" => T("Voorstad", "Suburb"),
        "city" => T("Stad of dorp", "City or town"),
        "postal_code" => T("Poskode", "Postal code"),
        "notes" => T("Bestelnotas", "Order notes"),
        "terms" => T("Terme en voorwaardes", "Terms and conditions"),
        "cart_changed" => T("Mandjie het verander", "Basket changed"),
        "empty_cart" => T("Leë mandjie", "Empty basket"),
        "products_unavailable" => T("Produkte onbeskikbaar", "Products unavailable"),
        "quantity" => T("Produkhoeveelheid", "Product quantity"),
        "total_changed" => T("Besteltotaal het verander", "Order total changed"),
        _ => T("Ander validasiefout", "Other validation issue")
    };

    private static IEnumerable<DateOnly> ShopAnalyticsChartDays(AdminStoreAnalyticsSnapshot report) =>
        Enumerable.Range(0, report.Through.DayNumber - report.From.DayNumber + 1).Select(report.From.AddDays);

    private string[] ShopAnalyticsChartLabels(AdminStoreAnalyticsSnapshot report)
    {
        var step = (int)Math.Ceiling((report.Through.DayNumber - report.From.DayNumber) / 5d);
        var culture = GetAdminCulture();
        return ShopAnalyticsChartDays(report).Select((day, index) =>
            index % step == 0 || day == report.Through ? day.ToString("d MMM", culture) : string.Empty).ToArray();
    }

    private List<ChartSeries<double>> ShopAnalyticsChartSeries(AdminStoreAnalyticsSnapshot report) =>
    [
        new()
        {
            Name = T("Unieke besoekers", "Unique visitors"),
            Data = ShopAnalyticsChartDays(report).Select(day => (double)(report.Engagement?.DailyVisitors.GetValueOrDefault(day) ?? 0)).ToArray()
        }
    ];
}
