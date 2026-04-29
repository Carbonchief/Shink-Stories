using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class ResendStoreOrderNotificationService(
    HttpClient httpClient,
    IOptions<ResendOptions> resendOptions,
    ILogger<ResendStoreOrderNotificationService> logger) : IStoreOrderNotificationService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ResendOptions _options = resendOptions.Value;
    private readonly ILogger<ResendStoreOrderNotificationService> _logger = logger;

    public async Task SendPaidOrderNotificationAsync(StoreOrderRecord order, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.FromEmail) ||
            string.IsNullOrWhiteSpace(_options.ToEmail))
        {
            _logger.LogWarning("Store order notification skipped: Resend is not configured.");
            return;
        }

        var encodedReference = HtmlEncoder.Default.Encode(order.OrderReference);
        var encodedCustomerName = HtmlEncoder.Default.Encode(order.CustomerName);
        var encodedEmail = HtmlEncoder.Default.Encode(order.CustomerEmail);
        var encodedPhone = HtmlEncoder.Default.Encode(order.CustomerPhone);
        var encodedAddressLine1 = HtmlEncoder.Default.Encode(order.DeliveryAddressLine1);
        var encodedAddressLine2 = HtmlEncoder.Default.Encode(order.DeliveryAddressLine2 ?? string.Empty);
        var encodedSuburb = HtmlEncoder.Default.Encode(order.DeliverySuburb ?? string.Empty);
        var encodedCity = HtmlEncoder.Default.Encode(order.DeliveryCity);
        var encodedPostalCode = HtmlEncoder.Default.Encode(order.DeliveryPostalCode);
        var encodedNotes = HtmlEncoder.Default.Encode(order.Notes ?? string.Empty).Replace("\n", "<br />");
        var encodedItemsHtml = string.Join(
            string.Empty,
            order.Items.Select(item =>
                $"<li><strong>{HtmlEncoder.Default.Encode(item.ProductName)}</strong> x{item.Quantity} - R {item.LineTotalZar:0.00}</li>"));
        var plainTextItems = string.Join(
            "\n",
            order.Items.Select(item => $"- {item.ProductName} x{item.Quantity} - R {item.LineTotalZar:0.00}"));

        var subject = $"Store bestelling betaal: {order.Quantity} items ({order.OrderReference})";
        var html = $$"""
            <h2>Nuwe winkel bestelling is betaal</h2>
            <p><strong>Verwysing:</strong> {{encodedReference}}</p>
            <p><strong>Aantal items:</strong> {{order.Quantity}}</p>
            <p><strong>Totaal:</strong> R {{order.TotalPriceZar:0.00}}</p>
            <p><strong>Bestelling:</strong></p>
            <ul>{{encodedItemsHtml}}</ul>
            <hr />
            <p><strong>Naam:</strong> {{encodedCustomerName}}</p>
            <p><strong>E-pos:</strong> {{encodedEmail}}</p>
            <p><strong>Selfoon:</strong> {{encodedPhone}}</p>
            <p><strong>Adres:</strong><br />{{encodedAddressLine1}}</p>
            {{(string.IsNullOrWhiteSpace(order.DeliveryAddressLine2) ? string.Empty : $"<p><strong>Adres lyn 2:</strong><br />{encodedAddressLine2}</p>")}}
            {{(string.IsNullOrWhiteSpace(order.DeliverySuburb) ? string.Empty : $"<p><strong>Voorstad:</strong> {encodedSuburb}</p>")}}
            <p><strong>Stad / Dorp:</strong> {{encodedCity}}</p>
            <p><strong>Poskode:</strong> {{encodedPostalCode}}</p>
            {{(string.IsNullOrWhiteSpace(order.Notes) ? string.Empty : $"<p><strong>Notas:</strong><br />{encodedNotes}</p>")}}
            """;
        var text = $$"""
            Nuwe winkel bestelling is betaal

            Verwysing: {{order.OrderReference}}
            Aantal items: {{order.Quantity}}
            Totaal: R {{order.TotalPriceZar:0.00}}

            Bestelling:
            {{plainTextItems}}

            Naam: {{order.CustomerName}}
            E-pos: {{order.CustomerEmail}}
            Selfoon: {{order.CustomerPhone}}
            Adres: {{order.DeliveryAddressLine1}}
            {{(string.IsNullOrWhiteSpace(order.DeliveryAddressLine2) ? string.Empty : $"Adres lyn 2: {order.DeliveryAddressLine2}\n")}}{{(string.IsNullOrWhiteSpace(order.DeliverySuburb) ? string.Empty : $"Voorstad: {order.DeliverySuburb}\n")}}Stad / Dorp: {{order.DeliveryCity}}
            Poskode: {{order.DeliveryPostalCode}}
            {{(string.IsNullOrWhiteSpace(order.Notes) ? string.Empty : $"\nNotas:\n{order.Notes}")}}
            """;

        var request = new ResendInternalEmailRequest(
            From: _options.FromEmail,
            To: [_options.ToEmail],
            Subject: subject,
            Html: html,
            Text: text,
            ReplyTo: order.CustomerEmail);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Resend rejected store order notification: {StatusCode} {Body}", (int)response.StatusCode, errorBody);
    }

    public async Task SendCustomerOrderConfirmationAsync(StoreOrderRecord order, CancellationToken cancellationToken = default)
    {
        var templateId = _options.Templates.StoreOrder.CustomerConfirmationTemplateId;
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.FromEmail) ||
            string.IsNullOrWhiteSpace(templateId))
        {
            _logger.LogWarning("Store customer confirmation skipped: Resend template is not configured.");
            return;
        }

        if (string.IsNullOrWhiteSpace(order.CustomerEmail))
        {
            _logger.LogWarning(
                "Store customer confirmation skipped: customer email is missing. reference={Reference}",
                order.OrderReference);
            return;
        }

        var variables = BuildCustomerConfirmationVariables(order);
        var request = new ResendTemplateEmailRequest(
            From: _options.FromEmail,
            To: [order.CustomerEmail],
            Subject: null,
            Html: null,
            Text: null,
            ReplyTo: string.IsNullOrWhiteSpace(_options.ToEmail) ? null : [_options.ToEmail],
            Template: new ResendTemplateRequest(templateId, variables));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            $"store-order-confirmation/{order.OrderReference}");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Resend rejected store customer confirmation. reference={Reference} template_id={TemplateId} status={StatusCode} body={Body}",
            order.OrderReference,
            templateId,
            (int)response.StatusCode,
            errorBody);
    }

    private static Dictionary<string, object?> BuildCustomerConfirmationVariables(StoreOrderRecord order) =>
        new(StringComparer.Ordinal)
        {
            ["CUSTOMER_NAME_HTML"] = HtmlEncoder.Default.Encode(string.IsNullOrWhiteSpace(order.CustomerName) ? "daar" : order.CustomerName),
            ["CUSTOMER_NAME_TEXT"] = string.IsNullOrWhiteSpace(order.CustomerName) ? "daar" : order.CustomerName,
            ["ORDER_REFERENCE"] = order.OrderReference,
            ["ORDER_ITEMS_HTML"] = BuildOrderItemsHtml(order),
            ["ORDER_ITEMS_TEXT"] = BuildOrderItemsText(order),
            ["ORDER_TOTAL"] = FormatZar(order.TotalPriceZar),
            ["DELIVERY_ADDRESS_HTML"] = BuildDeliveryCopyHtml(order),
            ["DELIVERY_ADDRESS_TEXT"] = BuildDeliveryCopyText(order)
        };

    private static string BuildOrderItemsHtml(StoreOrderRecord order)
    {
        IReadOnlyList<StoreOrderItemRecord> items = order.Items.Count > 0
            ? order.Items
            : [new StoreOrderItemRecord(order.ProductSlug, order.ProductName, order.Quantity, order.UnitPriceZar)];

        return string.Join(
            string.Empty,
            items.Select(item =>
            {
                var encodedName = HtmlEncoder.Default.Encode(item.ProductName);
                var lineTotal = FormatZar(item.LineTotalZar);
                return $"""
                    <tr><td style="padding-top:8px; padding-right:0; padding-bottom:8px; padding-left:0; font-family:Arial, Helvetica, sans-serif; font-size:15px; line-height:22px; color:#222222;">{encodedName} x{item.Quantity}</td><td align="right" style="padding-top:8px; padding-right:0; padding-bottom:8px; padding-left:8px; font-family:Arial, Helvetica, sans-serif; font-size:15px; line-height:22px; color:#222222;">{lineTotal}</td></tr>
                    """;
            }));
    }

    private static string BuildOrderItemsText(StoreOrderRecord order)
    {
        IReadOnlyList<StoreOrderItemRecord> items = order.Items.Count > 0
            ? order.Items
            : [new StoreOrderItemRecord(order.ProductSlug, order.ProductName, order.Quantity, order.UnitPriceZar)];

        return string.Join(
            "\n",
            items.Select(item => $"- {item.ProductName} x{item.Quantity} - {FormatZar(item.LineTotalZar)}"));
    }

    private static string BuildDeliveryAddress(StoreOrderRecord order)
    {
        var parts = new[]
            {
                order.DeliveryAddressLine1,
                order.DeliveryAddressLine2,
                order.DeliverySuburb,
                order.DeliveryCity,
                order.DeliveryPostalCode
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim());

        return string.Join(", ", parts);
    }

    private static string BuildDeliveryCopyText(StoreOrderRecord order)
    {
        var address = BuildDeliveryAddress(order);
        var pudoNote = "Ons stuur jou bestelling na 'n PUDO locker so naby as moontlik aan die adres wat jy ingevul het.";

        return string.IsNullOrWhiteSpace(address)
            ? pudoNote
            : $"{address}\n\n{pudoNote}";
    }

    private static string BuildDeliveryCopyHtml(StoreOrderRecord order)
    {
        var address = BuildDeliveryAddress(order);
        var pudoNote = "Ons stuur jou bestelling na 'n PUDO locker so naby as moontlik aan die adres wat jy ingevul het.";
        var encodedNote = HtmlEncoder.Default.Encode(pudoNote);

        return string.IsNullOrWhiteSpace(address)
            ? encodedNote
            : $"{HtmlEncoder.Default.Encode(address)}<br /><br />{encodedNote}";
    }

    private static string FormatZar(decimal value) =>
        $"R {value.ToString("0.00", CultureInfo.InvariantCulture)}";

    private sealed record ResendInternalEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("reply_to")] string ReplyTo);

    private sealed record ResendTemplateEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
        [property: JsonPropertyName("html"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Html,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? ReplyTo,
        [property: JsonPropertyName("template")] ResendTemplateRequest Template);

    private sealed record ResendTemplateRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, object?> Variables);
}
