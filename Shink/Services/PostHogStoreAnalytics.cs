using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Shink.Services;

public sealed class PostHogStoreAnalytics(
    HttpClient httpClient,
    IConfiguration configuration,
    IHostEnvironment environment,
    IMemoryCache cache,
    ILogger<PostHogStoreAnalytics> logger)
{
    private const int ResultLimit = 1000;
    internal bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["PostHog:QueryApiKey"]);

    internal async Task<StoreEngagementReport?> GetAsync(
        StoreAnalyticsPeriod period, bool forceRefresh, CancellationToken cancellationToken)
    {
        var apiKey = configuration["PostHog:QueryApiKey"]?.Trim();
        if (string.IsNullOrEmpty(apiKey)) return null;

        if (!int.TryParse(configuration["PostHog:QueryProjectId"], out var projectId) || projectId <= 0 ||
            !Uri.TryCreate(configuration["PostHog:QueryHostUrl"], UriKind.Absolute, out var host) ||
            (host.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && host.IsLoopback && host.Scheme == Uri.UriSchemeHttp)))
        {
            logger.LogWarning("PostHog Winkel reporting requires a valid query host and project ID.");
            return null;
        }

        var endpoint = new Uri(host, $"/api/projects/{projectId}/query/");
        var environmentName = environment.EnvironmentName.ToLowerInvariant();
        var cacheKey = (typeof(PostHogStoreAnalytics), endpoint, environmentName, period);
        if (!forceRefresh && cache.TryGetValue<StoreEngagementReport>(cacheKey, out var cached)) return cached;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                name = "Winkel admin analytics",
                refresh = forceRefresh ? "force_blocking" : "blocking",
                query = new { kind = "HogQLQuery", query = BuildQuery(period, environmentName) }
            });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var report = await response.Content.ReadFromJsonAsync<QueryResult>(cancellationToken);
            if (report?.Results is not { Length: < ResultLimit } rows)
            {
                throw new JsonException("Expected a complete Winkel analytics result.");
            }

            return cache.Set(cacheKey, ParseReport(rows), TimeSpan.FromMinutes(2));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException ||
                                          (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("PostHog Winkel reporting failed ({ErrorType}, status {StatusCode}).",
                exception.GetType().Name, (exception as HttpRequestException)?.StatusCode);
            return null;
        }
    }

    private static string BuildQuery(StoreAnalyticsPeriod period, string environmentName)
    {
        var from = period.FromUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var to = period.ToExclusiveUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var escapedEnvironment = environmentName.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

        // One scan yields period totals, daily visitors, product engagement and checkout issues.
        // A quick view has always required a product-card click, including before link clicks were tracked.
        return $"""
            SELECT
                bucket.1 AS scope,
                bucket.2 AS label,
                countIf(event = 'winkel_visited') AS visits,
                uniqExactIf(distinct_id, event = 'winkel_visited') AS visitors,
                uniqExactIf(distinct_id, clicked_product) AS clickers,
                countIf(event = 'winkel_product_viewed') AS product_opens,
                uniqExactIf(distinct_id, event = 'winkel_basket_added') AS basket_visitors,
                uniqExactIf(distinct_id, event = 'winkel_checkout_started') AS checkout_visitors,
                countIf(event = 'winkel_checkout_pay_clicked') AS pay_clicks,
                uniqExactIf(distinct_id, event = 'winkel_checkout_pay_clicked') AS pay_clickers,
                countIf(event = 'winkel_checkout_validation_failed') AS validation_failures,
                uniqExactIf(distinct_id, event = 'winkel_checkout_validation_failed') AS blocked_browsers,
                countIf(event = 'winkel_checkout_validation_failed' AND from_server) AS server_validation_failures
            FROM (
                SELECT event, distinct_id,
                    properties.source = 'checkout_server' AS from_server,
                    event = 'winkel_product_clicked' OR
                        (event = 'winkel_product_viewed' AND properties.view_type = 'quick_view') AS clicked_product,
                    arrayJoin([
                        ('total', 'all'),
                        ('day', if(event = 'winkel_visited', toString(toDate(toTimeZone(timestamp, '{StoreAnalyticsPeriod.TimeZoneId}'))), '')),
                        ('product', ifNull(toString(properties.product_slug), '')),
                        ('validation', if(event = 'winkel_checkout_validation_failed',
                            coalesce(nullIf(toString(properties.checkout_issue), ''), 'other'), ''))
                    ]) AS bucket
                FROM events
                WHERE timestamp >= toDateTime('{from}', 'UTC')
                    AND timestamp < toDateTime('{to}', 'UTC')
                    AND event IN ('winkel_visited', 'winkel_product_clicked', 'winkel_product_viewed', 'winkel_basket_added',
                        'winkel_checkout_started', 'winkel_checkout_pay_clicked', 'winkel_checkout_validation_failed')
                    AND properties.environment = '{escapedEnvironment}'
                    AND (properties.source = 'browser' OR (event = 'winkel_checkout_validation_failed' AND from_server))
                    AND startsWith(distinct_id, 'winkel:')
            )
            WHERE bucket.2 != ''
            GROUP BY scope, label
            ORDER BY scope, validation_failures DESC, label
            LIMIT {ResultLimit}
            """;
    }

    private static StoreEngagementReport ParseReport(JsonElement[][] rows)
    {
        var dailyVisitors = new Dictionary<DateOnly, long>();
        var products = new Dictionary<string, StoreProductEngagement>(StringComparer.Ordinal);
        var validationIssues = new List<AdminStoreCheckoutValidationIssue>();
        var totals = new AdminStoreEngagement(0, 0, 0, 0, dailyVisitors, new(0, 0, 0, 0, validationIssues));
        foreach (var row in rows)
        {
            if (row.Length != 13 || row[1].GetString() is not { Length: > 0 } label)
            {
                throw new JsonException("Unexpected Winkel analytics row.");
            }

            switch (row[0].GetString())
            {
                case "total":
                    totals = new(row[2].GetInt64(), row[3].GetInt64(), row[6].GetInt64(), row[7].GetInt64(), dailyVisitors,
                        new(row[8].GetInt64(), row[9].GetInt64(), row[10].GetInt64(), row[11].GetInt64(), validationIssues));
                    break;
                case "day":
                    var day = DateOnly.ParseExact(label, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    dailyVisitors[day] = row[3].GetInt64();
                    break;
                case "product":
                    products[label] = new(row[4].GetInt64(), row[5].GetInt64(), row[6].GetInt64());
                    break;
                case "validation":
                    var serverFailures = row[12].GetInt64();
                    validationIssues.Add(new(label, row[10].GetInt64() - serverFailures, serverFailures));
                    break;
                default:
                    throw new JsonException("Unexpected Winkel analytics group.");
            }
        }

        return new(totals, products);
    }

    private sealed record QueryResult(JsonElement[][]? Results);
}

internal sealed record StoreEngagementReport(
    AdminStoreEngagement Totals, IReadOnlyDictionary<string, StoreProductEngagement> Products);

internal sealed record StoreProductEngagement(long Clickers, long Opens, long BasketVisitors);
