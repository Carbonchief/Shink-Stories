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
        var encodedProductName = HtmlEncoder.Default.Encode(order.ProductName);
        var encodedCustomerName = HtmlEncoder.Default.Encode(order.CustomerName);
        var encodedEmail = HtmlEncoder.Default.Encode(order.CustomerEmail);
        var encodedPhone = HtmlEncoder.Default.Encode(order.CustomerPhone);
        var encodedAddressLine1 = HtmlEncoder.Default.Encode(order.DeliveryAddressLine1);
        var encodedAddressLine2 = HtmlEncoder.Default.Encode(order.DeliveryAddressLine2 ?? string.Empty);
        var encodedSuburb = HtmlEncoder.Default.Encode(order.DeliverySuburb ?? string.Empty);
        var encodedCity = HtmlEncoder.Default.Encode(order.DeliveryCity);
        var encodedPostalCode = HtmlEncoder.Default.Encode(order.DeliveryPostalCode);
        var encodedNotes = HtmlEncoder.Default.Encode(order.Notes ?? string.Empty).Replace("\n", "<br />");

        var subject = $"Store bestelling betaal: {order.ProductName} x{order.Quantity} ({order.OrderReference})";
        var html = $$"""
            <h2>Nuwe winkel bestelling is betaal</h2>
            <p><strong>Verwysing:</strong> {{encodedReference}}</p>
            <p><strong>Produk:</strong> {{encodedProductName}}</p>
            <p><strong>Hoeveelheid:</strong> {{order.Quantity}}</p>
            <p><strong>Totaal:</strong> R {{order.TotalPriceZar:0.00}}</p>
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
            Produk: {{order.ProductName}}
            Hoeveelheid: {{order.Quantity}}
            Totaal: R {{order.TotalPriceZar:0.00}}

            Naam: {{order.CustomerName}}
            E-pos: {{order.CustomerEmail}}
            Selfoon: {{order.CustomerPhone}}
            Adres: {{order.DeliveryAddressLine1}}
            {{(string.IsNullOrWhiteSpace(order.DeliveryAddressLine2) ? string.Empty : $"Adres lyn 2: {order.DeliveryAddressLine2}\n")}}{{(string.IsNullOrWhiteSpace(order.DeliverySuburb) ? string.Empty : $"Voorstad: {order.DeliverySuburb}\n")}}Stad / Dorp: {{order.DeliveryCity}}
            Poskode: {{order.DeliveryPostalCode}}
            {{(string.IsNullOrWhiteSpace(order.Notes) ? string.Empty : $"\nNotas:\n{order.Notes}")}}
            """;

        var request = new ResendEmailRequest(
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

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("reply_to")] string ReplyTo);
}
