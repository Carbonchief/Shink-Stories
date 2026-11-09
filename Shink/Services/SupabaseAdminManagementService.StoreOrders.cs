using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using StoreOrderRow = Shink.Services.SupabaseStoreOrderService.StoreOrderRow;

namespace Shink.Services;

public sealed record AdminStoreOrderSummary(
    Guid OrderId,
    string OrderReference,
    string CustomerName,
    decimal TotalPriceZar,
    string PaymentStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ShippedAt);

public sealed record AdminStoreOrderPage(IReadOnlyList<AdminStoreOrderSummary> Items, int TotalCount);

public sealed partial class SupabaseAdminManagementService
{
    private const string StoreOrderSummaryColumns =
        "order_id,order_reference,customer_name,total_price_zar,payment_status,created_at,shipped_at";
    private const string StoreOrderDetailColumns = StoreOrderSummaryColumns +
        ",product_slug,product_name,quantity,unit_price_zar,items,delivery_fee_waived,customer_email,customer_phone" +
        ",delivery_address_line_1,delivery_address_line_2,delivery_suburb,delivery_city,delivery_postal_code" +
        ",notes,provider,currency,status_reason,provider_transaction_id,paid_at,updated_at";

    public async Task<AdminStoreOrderPage> GetStoreOrdersPageAsync(
        string? adminEmail, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken, throwOnLookupFailure: true);
        if (context is null)
        {
            throw new UnauthorizedAccessException();
        }

        var limit = Math.Clamp(pageSize, 1, 100);
        var offset = (long)Math.Max(0, pageIndex) * limit;
        var uri = new Uri(context.BaseUri,
            $"rest/v1/store_orders?select={StoreOrderSummaryColumns}&order=created_at.desc,order_id.desc&limit={limit}&offset={offset}");
        using var request = CreateRequest(HttpMethod.Get, uri, context.ApiKey);
        request.Headers.TryAddWithoutValidation("Prefer", "count=exact");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            response.EnsureSuccessStatusCode();
        }

        // PostgREST uses an unqualified range, such as 0-24/35 or */0.
        var contentRange = response.Content.Headers.NonValidated.TryGetValues("Content-Range", out var values)
            ? values.FirstOrDefault()
            : null;
        var slash = contentRange?.LastIndexOf('/') ?? -1;
        if (slash < 0 || !int.TryParse(contentRange.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var total))
        {
            throw new InvalidOperationException("Supabase did not return the store order count.");
        }

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return new AdminStoreOrderPage([], total);
        }

        var rows = await response.Content.ReadFromJsonAsync<StoreOrderRow[]>(JsonOptions, cancellationToken) ?? [];
        return new AdminStoreOrderPage(rows.Select(row => new AdminStoreOrderSummary(
            row.OrderId, row.OrderReference ?? string.Empty, row.CustomerName ?? string.Empty, row.TotalPriceZar,
            row.PaymentStatus ?? "pending", row.CreatedAt, row.ShippedAt)).ToArray(), total);
    }

    public async Task<StoreOrderRecord?> GetStoreOrderAsync(
        string? adminEmail, Guid orderId, CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            throw new UnauthorizedAccessException();
        }

        var uri = new Uri(context.BaseUri, $"rest/v1/store_orders?select={StoreOrderDetailColumns}&order_id=eq.{orderId:D}&limit=1");
        var rows = await FetchRowsAsync<StoreOrderRow>(uri, context.ApiKey, cancellationToken, throwOnFailure: true);
        return rows.Count > 0 ? SupabaseStoreOrderService.MapRecord(rows[0]) : null;
    }

    public async Task<DateTimeOffset?> MarkStoreOrderShippedAsync(
        string? adminEmail, Guid orderId, CancellationToken cancellationToken = default)
    {
        var context = await TryCreateAdminOperationContextAsync(adminEmail, cancellationToken);
        if (context is null)
        {
            throw new UnauthorizedAccessException();
        }

        var orderUri = $"rest/v1/store_orders?select=shipped_at&order_id=eq.{orderId:D}";
        var pendingShipmentUri = new Uri(context.BaseUri, orderUri + "&payment_status=eq.paid&shipped_at=is.null");
        using var request = CreateJsonRequest(HttpMethod.Patch, pendingShipmentUri, context.ApiKey,
            new { shipped_at = DateTimeOffset.UtcNow }, "return=representation");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<ShipmentRow[]>(JsonOptions, cancellationToken);
        if (updated?.FirstOrDefault()?.ShippedAt is { } shippedAt)
        {
            return shippedAt;
        }

        // A retry or a second admin can find that the shipment was already recorded.
        var current = await FetchRowsAsync<ShipmentRow>(new Uri(context.BaseUri, orderUri), context.ApiKey,
            cancellationToken, throwOnFailure: true);
        return current.FirstOrDefault()?.ShippedAt;
    }

    private sealed record ShipmentRow([property: JsonPropertyName("shipped_at")] DateTimeOffset? ShippedAt);
}
