using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseStoreOrderService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    ILogger<SupabaseStoreOrderService> logger) : IStoreOrderService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly ILogger<SupabaseStoreOrderService> _logger = logger;

    public async Task<StoreOrderCreateResult> CreatePendingOrderAsync(StoreOrderDraft draft, CancellationToken cancellationToken = default)
    {
        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Store order create skipped: Supabase URL is not configured.");
            return new StoreOrderCreateResult(false, ErrorMessage: "Die winkel is nog nie volledig opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Store order create skipped: Supabase ServiceRoleKey is not configured.");
            return new StoreOrderCreateResult(false, ErrorMessage: "Die winkel is nog nie volledig opgestel nie.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var payload = new[]
        {
            new
            {
                order_reference = draft.OrderReference,
                product_slug = draft.ProductSlug,
                product_name = draft.ProductName,
                quantity = draft.Quantity,
                unit_price_zar = draft.UnitPriceZar,
                total_price_zar = draft.UnitPriceZar * draft.Quantity,
                customer_name = draft.CustomerName,
                customer_email = draft.CustomerEmail,
                customer_phone = draft.CustomerPhone,
                delivery_address_line_1 = draft.DeliveryAddressLine1,
                delivery_address_line_2 = draft.DeliveryAddressLine2,
                delivery_suburb = draft.DeliverySuburb,
                delivery_city = draft.DeliveryCity,
                delivery_postal_code = draft.DeliveryPostalCode,
                notes = draft.Notes,
                payment_status = "pending",
                provider = "paystack",
                currency = "ZAR",
                created_at = nowUtc,
                updated_at = nowUtc
            }
        };

        var uri = new Uri(baseUri, "rest/v1/store_orders");

        try
        {
            using var request = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "return=representation");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Store order create failed. reference={Reference} Status={StatusCode} Body={Body}",
                    draft.OrderReference,
                    (int)response.StatusCode,
                    responseBody);
                return new StoreOrderCreateResult(false, ErrorMessage: "Kon nie jou bestelling nou begin nie.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoreOrderRow>>(stream, cancellationToken: cancellationToken) ?? [];
            var row = rows.FirstOrDefault();
            return row is null
                ? new StoreOrderCreateResult(false, ErrorMessage: "Kon nie jou bestelling nou begin nie.")
                : new StoreOrderCreateResult(true, MapRecord(row));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Store order create failed unexpectedly. reference={Reference}", draft.OrderReference);
            return new StoreOrderCreateResult(false, ErrorMessage: "Kon nie jou bestelling nou begin nie.");
        }
    }

    public async Task<StoreOrderRecord?> GetOrderByReferenceAsync(string? orderReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderReference) ||
            !TryBuildSupabaseBaseUri(out var baseUri))
        {
            return null;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var escapedReference = Uri.EscapeDataString(orderReference.Trim());
        var uri = new Uri(baseUri, $"rest/v1/store_orders?select=*&order_reference=eq.{escapedReference}&limit=1");

        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Store order lookup failed. reference={Reference} Status={StatusCode} Body={Body}",
                    orderReference,
                    (int)response.StatusCode,
                    responseBody);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoreOrderRow>>(stream, cancellationToken: cancellationToken) ?? [];
            var row = rows.FirstOrDefault();
            return row is null ? null : MapRecord(row);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Store order lookup failed unexpectedly. reference={Reference}", orderReference);
            return null;
        }
    }

    public async Task<StoreOrderPaymentUpdateResult> ApplyPaymentUpdateAsync(StoreOrderPaymentUpdate update, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderByReferenceAsync(update.OrderReference, cancellationToken);
        if (order is null)
        {
            return new StoreOrderPaymentUpdateResult(false, ErrorMessage: "Kon nie die bestelling vind nie.");
        }

        var expectedAmountInCents = (int)Math.Round(order.TotalPriceZar * 100m, MidpointRounding.AwayFromZero);
        if (expectedAmountInCents != update.AmountInCents)
        {
            _logger.LogWarning(
                "Store payment amount mismatch. reference={Reference} expected={ExpectedAmount} actual={ActualAmount}",
                update.OrderReference,
                expectedAmountInCents,
                update.AmountInCents);
            return new StoreOrderPaymentUpdateResult(false, Order: order, ErrorMessage: "Die betaalbedrag stem nie ooreen met die bestelling nie.");
        }

        if (!string.Equals(update.Currency, "ZAR", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Store payment currency mismatch. reference={Reference} currency={Currency}",
                update.OrderReference,
                update.Currency);
            return new StoreOrderPaymentUpdateResult(false, Order: order, ErrorMessage: "Die betaalgeldeenheid is ongeldig.");
        }

        var normalizedCustomerEmail = NormalizeOptionalText(update.CustomerEmail, 320)?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedCustomerEmail) &&
            !string.Equals(order.CustomerEmail, normalizedCustomerEmail, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Store payment email mismatch. reference={Reference} expected={ExpectedEmail} actual={ActualEmail}",
                update.OrderReference,
                order.CustomerEmail,
                normalizedCustomerEmail);
            return new StoreOrderPaymentUpdateResult(false, Order: order, ErrorMessage: "Die bestelling e-pos stem nie ooreen met die betaling nie.");
        }

        var normalizedStatus = NormalizePaymentStatus(update.PaymentStatus);
        var wasAlreadyPaid = string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(order.PaymentStatus, normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return new StoreOrderPaymentUpdateResult(true, order, StatusChanged: false, WasAlreadyPaid: wasAlreadyPaid);
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new StoreOrderPaymentUpdateResult(false, Order: order, ErrorMessage: "Die winkel is nog nie volledig opgestel nie.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new StoreOrderPaymentUpdateResult(false, Order: order, ErrorMessage: "Die winkel is nog nie volledig opgestel nie.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object?>
        {
            ["payment_status"] = normalizedStatus,
            ["status_reason"] = NormalizeOptionalText(update.StatusReason, 400),
            ["provider_transaction_id"] = NormalizeOptionalText(update.ProviderTransactionId, 120),
            ["paid_at"] = string.Equals(normalizedStatus, "paid", StringComparison.OrdinalIgnoreCase)
                ? update.PaidAt ?? nowUtc
                : null,
            ["updated_at"] = nowUtc
        };

        var rawPayload = DeserializePayloadObject(update.RawPayload);
        if (string.Equals(update.Source, "webhook", StringComparison.OrdinalIgnoreCase))
        {
            payload["raw_webhook_payload"] = rawPayload;
        }
        else
        {
            payload["raw_verify_response"] = rawPayload;
        }

        var escapedReference = Uri.EscapeDataString(update.OrderReference.Trim());
        var uri = new Uri(baseUri, $"rest/v1/store_orders?order_reference=eq.{escapedReference}");

        try
        {
            using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=representation");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Store payment update failed. reference={Reference} Status={StatusCode} Body={Body}",
                    update.OrderReference,
                    (int)response.StatusCode,
                    responseBody);
                return new StoreOrderPaymentUpdateResult(false, Order: order, ErrorMessage: "Kon nie jou bestelling nou bywerk nie.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<StoreOrderRow>>(stream, cancellationToken: cancellationToken) ?? [];
            var updatedOrder = rows.FirstOrDefault() is { } row ? MapRecord(row) : order;
            return new StoreOrderPaymentUpdateResult(
                true,
                updatedOrder,
                StatusChanged: true,
                WasAlreadyPaid: wasAlreadyPaid);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Store payment update failed unexpectedly. reference={Reference}", update.OrderReference);
            return new StoreOrderPaymentUpdateResult(false, Order: order, ErrorMessage: "Kon nie jou bestelling nou bywerk nie.");
        }
    }

    public async Task<StoreOrderPaymentUpdateResult> RecordPaystackWebhookAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new StoreOrderPaymentUpdateResult(false, ErrorMessage: "Paystack payload is leeg.");
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var eventType = TryReadString(root, "event");
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return new StoreOrderPaymentUpdateResult(false, ErrorMessage: "Paystack payload is ongeldig.");
            }

            var metadata = data.TryGetProperty("metadata", out var metadataNode) && metadataNode.ValueKind == JsonValueKind.Object
                ? metadataNode
                : default;

            var checkoutKind = TryReadString(metadata, "checkout_kind");
            if (!string.Equals(checkoutKind, "store", StringComparison.OrdinalIgnoreCase))
            {
                return new StoreOrderPaymentUpdateResult(false, ErrorMessage: "Paystack payload is nie vir die winkel nie.");
            }

            var orderReference = TryReadString(metadata, "order_reference")
                ?? TryReadString(data, "reference");
            if (string.IsNullOrWhiteSpace(orderReference))
            {
                return new StoreOrderPaymentUpdateResult(false, ErrorMessage: "Paystack payload het nie 'n bestelling verwysing nie.");
            }

            var amountInCents = TryReadInt(data, "amount");
            if (amountInCents <= 0)
            {
                return new StoreOrderPaymentUpdateResult(false, ErrorMessage: "Paystack payload het nie 'n geldige bedrag nie.");
            }

            var update = new StoreOrderPaymentUpdate(
                OrderReference: orderReference,
                PaymentStatus: ResolveWebhookPaymentStatus(eventType, data),
                AmountInCents: amountInCents,
                Currency: TryReadString(data, "currency") ?? "ZAR",
                CustomerEmail: TryReadNestedString(data, "customer", "email"),
                ProviderTransactionId: TryReadString(data, "id") ?? TryReadString(data, "reference"),
                PaidAt: ParseDateTimeOffset(TryReadString(data, "paid_at")),
                StatusReason: TryReadString(data, "gateway_response") ?? TryReadString(data, "message"),
                RawPayload: payloadJson,
                Source: "webhook");

            return await ApplyPaymentUpdateAsync(update, cancellationToken);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Store Paystack webhook parse failed.");
            return new StoreOrderPaymentUpdateResult(false, ErrorMessage: "Paystack payload is nie geldige JSON nie.");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, string apiKey, object payload, string preferHeader)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("Prefer", preferHeader);
        return request;
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = default!;
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        baseUri = parsedUri;
        return true;
    }

    private string ResolveApiKey() => _options.ServiceRoleKey;

    private static StoreOrderRecord MapRecord(StoreOrderRow row) =>
        new(
            OrderId: row.OrderId,
            OrderReference: row.OrderReference?.Trim() ?? string.Empty,
            ProductSlug: row.ProductSlug?.Trim() ?? string.Empty,
            ProductName: row.ProductName?.Trim() ?? string.Empty,
            Quantity: row.Quantity,
            UnitPriceZar: row.UnitPriceZar,
            TotalPriceZar: row.TotalPriceZar,
            CustomerName: row.CustomerName?.Trim() ?? string.Empty,
            CustomerEmail: row.CustomerEmail?.Trim().ToLowerInvariant() ?? string.Empty,
            CustomerPhone: row.CustomerPhone?.Trim() ?? string.Empty,
            DeliveryAddressLine1: row.DeliveryAddressLine1?.Trim() ?? string.Empty,
            DeliveryAddressLine2: NormalizeOptionalText(row.DeliveryAddressLine2, 250),
            DeliverySuburb: NormalizeOptionalText(row.DeliverySuburb, 120),
            DeliveryCity: row.DeliveryCity?.Trim() ?? string.Empty,
            DeliveryPostalCode: row.DeliveryPostalCode?.Trim() ?? string.Empty,
            Notes: NormalizeOptionalText(row.Notes, 2000),
            PaymentStatus: row.PaymentStatus?.Trim().ToLowerInvariant() ?? "pending",
            Provider: row.Provider?.Trim().ToLowerInvariant() ?? "paystack",
            Currency: row.Currency?.Trim().ToUpperInvariant() ?? "ZAR",
            StatusReason: NormalizeOptionalText(row.StatusReason, 400),
            ProviderTransactionId: NormalizeOptionalText(row.ProviderTransactionId, 120),
            PaidAt: row.PaidAt,
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt);

    private static string NormalizePaymentStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "success" => "paid",
            "paid" => "paid",
            "failed" => "failed",
            "cancelled" => "cancelled",
            "canceled" => "cancelled",
            _ => "pending"
        };

    private static string ResolveWebhookPaymentStatus(string? eventType, JsonElement data)
    {
        var explicitStatus = TryReadString(data, "status");
        if (string.Equals(explicitStatus, "success", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "charge.success", StringComparison.OrdinalIgnoreCase))
        {
            return "paid";
        }

        if (string.Equals(eventType, "charge.failed", StringComparison.OrdinalIgnoreCase))
        {
            return "failed";
        }

        return NormalizePaymentStatus(explicitStatus);
    }

    private static object DeserializePayloadObject(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(payloadJson);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>
            {
                ["raw"] = payloadJson
            };
        }
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength
            ? trimmed[..maxLength]
            : trimmed;
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString(),
            JsonValueKind.Number => node.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int TryReadInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var node))
        {
            return 0;
        }

        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (node.ValueKind == JsonValueKind.String &&
            int.TryParse(node.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static string? TryReadNestedString(JsonElement element, string firstProperty, string secondProperty)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(firstProperty, out var nested))
        {
            return null;
        }

        return TryReadString(nested, secondProperty);
    }

    private sealed record StoreOrderRow(
        [property: JsonPropertyName("order_id")] Guid OrderId,
        [property: JsonPropertyName("order_reference")] string? OrderReference,
        [property: JsonPropertyName("product_slug")] string? ProductSlug,
        [property: JsonPropertyName("product_name")] string? ProductName,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("unit_price_zar")] decimal UnitPriceZar,
        [property: JsonPropertyName("total_price_zar")] decimal TotalPriceZar,
        [property: JsonPropertyName("customer_name")] string? CustomerName,
        [property: JsonPropertyName("customer_email")] string? CustomerEmail,
        [property: JsonPropertyName("customer_phone")] string? CustomerPhone,
        [property: JsonPropertyName("delivery_address_line_1")] string? DeliveryAddressLine1,
        [property: JsonPropertyName("delivery_address_line_2")] string? DeliveryAddressLine2,
        [property: JsonPropertyName("delivery_suburb")] string? DeliverySuburb,
        [property: JsonPropertyName("delivery_city")] string? DeliveryCity,
        [property: JsonPropertyName("delivery_postal_code")] string? DeliveryPostalCode,
        [property: JsonPropertyName("notes")] string? Notes,
        [property: JsonPropertyName("payment_status")] string? PaymentStatus,
        [property: JsonPropertyName("provider")] string? Provider,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("status_reason")] string? StatusReason,
        [property: JsonPropertyName("provider_transaction_id")] string? ProviderTransactionId,
        [property: JsonPropertyName("paid_at")] DateTimeOffset? PaidAt,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);
}
