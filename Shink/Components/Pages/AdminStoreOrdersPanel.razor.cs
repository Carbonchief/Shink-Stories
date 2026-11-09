using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shink.Services;

namespace Shink.Components.Pages;

public partial class AdminStoreOrdersPanel : IAsyncDisposable
{
    private static readonly DialogOptions OrderDialogOptions = new()
    {
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        BackdropClick = false,
        BackgroundClass = "admin-solid-dialog-background"
    };

    private readonly CancellationTokenSource _lifetime = new();
    private MudTable<AdminStoreOrderSummary>? _ordersTable;
    private TableData<AdminStoreOrderSummary> _loadedOrders = new() { Items = [] };
    private (int Page, int PageSize) _loadedOrderWindow;
    private int _rowsPerPage = 25;
    private StoreOrderRecord? _selectedOrder;
    private string? _selectedReference;
    private string? _detailError;
    private bool _listLoadFailed;
    private bool _orderOpen;
    private bool _loadingOrder;
    private bool _shipping;

    [Parameter, EditorRequired] public string? AdminEmail { get; set; }
    [Parameter, EditorRequired] public string LanguageCode { get; set; } = "af";
    [Inject] private IAdminManagementService AdminManagementService { get; set; } = default!;
    [Inject] private ILogger<AdminStoreOrdersPanel> Logger { get; set; } = default!;

    private async Task<TableData<AdminStoreOrderSummary>> LoadOrdersAsync(TableState state, CancellationToken cancellationToken)
    {
        var requestedWindow = (state.Page, state.PageSize);
        try
        {
            var page = await AdminManagementService.GetStoreOrdersPageAsync(AdminEmail, state.Page, state.PageSize, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _loadedOrders = new TableData<AdminStoreOrderSummary> { Items = page.Items, TotalItems = page.TotalCount };
            _loadedOrderWindow = requestedWindow;
            _listLoadFailed = false;

            var lastPage = Math.Max(0, (page.TotalCount - 1) / state.PageSize);
            if (state.Page > lastPage && _ordersTable is { } table)
            {
                // The page can disappear after orders are removed. Changing it starts a new load.
                table.NavigateTo(lastPage);
            }
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // MudTable applies cancelled results too. Preserve its current rows and count.
                return _loadedOrders;
            }

            Logger.LogWarning(exception, "Could not load Winkel orders.");
            _listLoadFailed = true;
            if (exception is UnauthorizedAccessException or HttpRequestException
                { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
            {
                _loadedOrders = new TableData<AdminStoreOrderSummary> { Items = [] };
            }
            else if (_loadedOrderWindow != requestedWindow)
            {
                // Keep navigation available without showing another page's orders.
                _loadedOrders = new TableData<AdminStoreOrderSummary> { Items = [], TotalItems = _loadedOrders.TotalItems };
            }
        }

        return _loadedOrders;
    }

    private Task RefreshOrdersAsync() => _ordersTable?.ReloadServerData() ?? Task.CompletedTask;

    private bool AtFirstOrderPage => _ordersTable is null || _ordersTable.CurrentPage == 0;

    private bool AtLastOrderPage => _ordersTable is null ||
        ((long)_ordersTable.CurrentPage + 1) * _ordersTable.RowsPerPage >= _loadedOrders.TotalItems;

    private Task OpenOrderFromRowAsync(TableRowClickEventArgs<AdminStoreOrderSummary> args) =>
        args.Item is { } order ? OpenOrderAsync(order) : Task.CompletedTask;

    private async Task OpenOrderAsync(AdminStoreOrderSummary row)
    {
        if (_orderOpen || _loadingOrder || _shipping)
        {
            return;
        }

        _selectedReference = row.OrderReference;
        _selectedOrder = null;
        _detailError = null;
        _loadingOrder = true;
        _orderOpen = true;
        try
        {
            _selectedOrder = await AdminManagementService.GetStoreOrderAsync(AdminEmail, row.OrderId, _lifetime.Token);
            if (_selectedOrder is null)
            {
                _detailError = T("Die bestelling kon nie gevind word nie.", "The order could not be found.");
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Could not open a Winkel order.");
            _detailError = T("Kon nie die bestelling laai nie.", "Could not load the order.");
        }
        finally
        {
            _loadingOrder = false;
        }
    }

    private async Task MarkShippedAsync()
    {
        if (_shipping || _selectedOrder is not { PaymentStatus: "paid", ShippedAt: null } order)
        {
            return;
        }

        _detailError = null;
        _shipping = true;
        try
        {
            var shippedAt = await AdminManagementService.MarkStoreOrderShippedAsync(AdminEmail, order.OrderId, _lifetime.Token);
            if (shippedAt is null)
            {
                _selectedOrder = await AdminManagementService.GetStoreOrderAsync(AdminEmail, order.OrderId, _lifetime.Token);
                _detailError = T("Die bestelling is nie meer beskikbaar vir versending nie.", "The order is no longer available to ship.");
            }
            else
            {
                _selectedOrder = order with { ShippedAt = shippedAt };
                _loadedOrders.Items = (_loadedOrders.Items ?? [])
                    .Select(row => row.OrderId == order.OrderId ? row with { ShippedAt = shippedAt } : row)
                    .ToArray();
            }

            await RefreshOrdersAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Could not record shipment for a Winkel order.");
            _detailError = T("Kon nie die versending stoor nie. Probeer weer.", "Could not save shipment. Try again.");
        }
        finally
        {
            _shipping = false;
        }
    }

    private void CloseOrder() => _orderOpen = false;

    private string T(string af, string en) => LanguageCode == "en" ? en : af;

    private string FormatDate(DateTimeOffset? value) => value?.ToLocalTime().ToString("d MMM yyyy HH:mm",
        CultureInfo.GetCultureInfo(T("af-ZA", "en-ZA"))) ?? "–";

    private string PaymentLabel(string status) => status switch
    {
        "paid" => T("Betaal", "Paid"),
        "failed" => T("Misluk", "Failed"),
        "cancelled" => T("Gekanselleer", "Cancelled"),
        _ => T("Hangende", "Pending")
    };

    private string ShippingLabel(DateTimeOffset? shippedAt, string paymentStatus) => shippedAt.HasValue
        ? T("Versend", "Shipped")
        : paymentStatus == "paid" ? T("Versending nie aangeteken nie", "Shipment not recorded") : "–";

    private static IEnumerable<string?> DeliveryLines(StoreOrderRecord order) =>
        new[] { order.DeliveryAddressLine1, order.DeliveryAddressLine2, order.DeliverySuburb, order.DeliveryCity, order.DeliveryPostalCode }
            .Where(line => !string.IsNullOrWhiteSpace(line));

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _lifetime.Dispose();
    }
}
