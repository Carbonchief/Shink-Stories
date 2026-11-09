using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

internal sealed class StorePurchaseAnalytics(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    PostHogSettings postHog,
    IHostEnvironment environment,
    ILogger<StorePurchaseAnalytics> logger)
{
    private const string OrdersPath = "rest/v1/store_orders";
    private const string PendingQuery = "?select=order_id,analytics_visitor_id,paid_at,total_price_zar,currency,items&" +
        "payment_status=eq.paid&paid_at=not.is.null&analytics_visitor_id=not.is.null&analytics_reported_at=is.null&" +
        "order=paid_at.asc,order_id.asc&limit=25";

    public async Task DeliverPendingAsync(CancellationToken cancellationToken)
    {
        var options = supabaseOptions.Value;
        if (string.IsNullOrWhiteSpace(options.SecretKey) ||
            !Uri.TryCreate(options.Url, UriKind.Absolute, out var supabaseUri) ||
            postHog.ProjectApiKey?.StartsWith("phc_", StringComparison.Ordinal) != true ||
            !Uri.TryCreate(postHog.HostUrl?.TrimEnd('/') + "/i/v0/e/", UriKind.Absolute, out var captureUri) ||
            (captureUri.Scheme != Uri.UriSchemeHttps && !(captureUri.IsLoopback && captureUri.Scheme == Uri.UriSchemeHttp)))
        {
            return;
        }

        var ordersUri = new Uri(supabaseUri, OrdersPath);
        using var read = OrdersRequest(HttpMethod.Get, new Uri(ordersUri + PendingQuery), options.SecretKey);
        using var response = await httpClient.SendAsync(read, cancellationToken);
        response.EnsureSuccessStatusCode();
        var orders = await response.Content.ReadFromJsonAsync<Purchase[]>(cancellationToken) ?? [];
        foreach (var order in orders)
        {
            try
            {
                var products = order.Items.Where(item => item.ProductSlug != "pudo-locker-delivery").ToArray();
                using var capture = await httpClient.PostAsJsonAsync(captureUri, new
                {
                    api_key = postHog.ProjectApiKey,
                    @event = "winkel_purchase_verified",
                    distinct_id = $"winkel:{order.VisitorId:D}",
                    // Retrying after a failed acknowledgement reuses the same event identity.
                    uuid = order.OrderId,
                    timestamp = order.PaidAt.UtcDateTime,
                    properties = new Dictionary<string, object>
                    {
                        ["source"] = "verified_order",
                        ["environment"] = environment.EnvironmentName.ToLowerInvariant(),
                        ["total_cents"] = decimal.ToInt64(order.TotalPriceZar * 100),
                        ["currency"] = order.Currency,
                        ["item_count"] = products.Sum(item => item.Quantity),
                        ["product_slugs"] = products.Select(item => item.ProductSlug).ToArray(),
                        ["$process_person_profile"] = false
                    }
                }, cancellationToken);
                capture.EnsureSuccessStatusCode();

                var markUri = new Uri(ordersUri + $"?order_id=eq.{order.OrderId:D}&payment_status=eq.paid&analytics_reported_at=is.null");
                using var mark = OrdersRequest(HttpMethod.Patch, markUri, options.SecretKey);
                mark.Content = JsonContent.Create(new { analytics_reported_at = DateTimeOffset.UtcNow });
                using var marked = await httpClient.SendAsync(mark, cancellationToken);
                marked.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning("Winkel purchase analytics delivery failed with status {StatusCode}. It will retry.", exception.StatusCode);
            }
        }
    }

    private static HttpRequestMessage OrdersRequest(HttpMethod method, Uri uri, string key)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("apikey", key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private sealed record Purchase(
        [property: JsonPropertyName("order_id")] Guid OrderId,
        [property: JsonPropertyName("analytics_visitor_id")] Guid VisitorId,
        [property: JsonPropertyName("paid_at")] DateTimeOffset PaidAt,
        [property: JsonPropertyName("total_price_zar")] decimal TotalPriceZar,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("items")] PurchaseItem[] Items);

    private sealed record PurchaseItem(
        [property: JsonPropertyName("product_slug")] string ProductSlug,
        [property: JsonPropertyName("quantity")] int Quantity);
}

internal sealed class StorePurchaseAnalyticsWorker(
    IServiceScopeFactory scopes,
    ILogger<StorePurchaseAnalyticsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<StorePurchaseAnalytics>().DeliverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Winkel purchase analytics polling failed ({ErrorType}). It will retry.", exception.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
