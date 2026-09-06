using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

// Provider APIs remain the source of truth. Polling recovers missed notifications
// and never revokes access merely because a provider/network request failed.
public sealed class StoreSubscriptionReconciliationService(
    HttpClient httpClient, IOptions<SupabaseOptions> options,
    MobileStoreEntitlementService verifier, ILogger<StoreSubscriptionReconciliationService> logger)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!Uri.TryCreate(config.Url.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            string.IsNullOrWhiteSpace(config.SecretKey)) return;
        string? cursor = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var route = "rest/v1/subscriptions?select=subscription_id,subscriber_id,provider,provider_payment_id,provider_transaction_id,provider_token,tier_code,next_renewal_at,status" +
                "&provider=in.(apple,google_play)&order=subscription_id.asc&limit=100" +
                (cursor is null ? "" : $"&subscription_id=gt.{Uri.EscapeDataString(cursor)}");
            using var request = Create(HttpMethod.Get, route);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<List<StoreRow>>(cancellationToken) ?? [];
            foreach (var row in rows)
            {
                try { await ReconcileRowAsync(row); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) { logger.LogWarning(exception, "Store reconciliation failed for subscription {SubscriptionId}", row.Id); }
            }
            if (rows.Count < 100) return;
            cursor = rows[^1].Id;
        }

        HttpRequestMessage Create(HttpMethod method, string route)
        {
            var request = new HttpRequestMessage(method, new Uri(baseUri, route));
            request.Headers.Add("apikey", config.SecretKey);
            request.Headers.Authorization = new("Bearer", config.SecretKey);
            return request;
        }

        async Task ReconcileRowAsync(StoreRow row)
        {
            var plan = PaymentPlanCatalog.MobileStorePlans.FirstOrDefault(p => p.TierCode == row.Tier);
            if (plan is null) return;
            var check = await verifier.CheckAsync(row.Provider, plan.StoreProductId,
                row.TransactionId ?? row.PaymentId, row.Token, cancellationToken: cancellationToken);
            // Switching monthly/yearly within Apple's group keeps the original
            // transaction lineage. Never borrow another family's subscription.
            if (row.Provider == "apple" && check.Purchase is null)
            {
                foreach (var alternative in PaymentPlanCatalog.MobileStorePlans.Where(p => p.StoreProductId != plan.StoreProductId))
                {
                    var other = await verifier.CheckAsync(row.Provider, alternative.StoreProductId,
                        row.PaymentId, null, cancellationToken: cancellationToken);
                    if (other.Purchase?.ProviderPaymentId == row.PaymentId) { check = other; break; }
                }
            }
            if (check.Purchase is null && !check.IsInactive) return;
            var purchase = check.Purchase;
            if (purchase is not null && purchase.ProviderPaymentId != row.PaymentId) return;
            var tier = purchase is null ? row.Tier : PaymentPlanCatalog.FindMobileStorePlan(purchase.ProductId)!.TierCode;
            // Compare the snapshot: a slower reconciliation cannot overwrite a
            // purchase/restore that updated this record while the API was queried.
            var filter = $"rest/v1/subscriptions?subscription_id=eq.{Uri.EscapeDataString(row.Id)}&subscriber_id=eq.{Uri.EscapeDataString(row.SubscriberId)}&provider=eq.{row.Provider}&status=eq.{Uri.EscapeDataString(row.Status)}" +
                (row.Expires is null ? "&next_renewal_at=is.null" : $"&next_renewal_at=eq.{Uri.EscapeDataString(row.Expires.Value.ToString("O"))}") + "&select=subscription_id";
            using var update = Create(HttpMethod.Patch, filter);
            update.Headers.Add("Prefer", "return=representation");
            update.Content = JsonContent.Create(new {
                status = purchase is null ? "cancelled" : "active",
                tier_code = tier,
                next_renewal_at = purchase?.AccessEndsAtUtc ?? row.Expires,
                cancelled_at = purchase is null ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
                provider_transaction_id = purchase?.ProviderTransactionId ?? row.TransactionId
            });
            using var updated = await httpClient.SendAsync(update, cancellationToken);
            updated.EnsureSuccessStatusCode();
            var changed = await updated.Content.ReadFromJsonAsync<List<StoreRow>>(cancellationToken) ?? [];
            if (changed.Count == 0 || purchase is null) return;
            await verifier.AcknowledgeAsync(purchase, cancellationToken);
            if (!string.IsNullOrWhiteSpace(purchase.LinkedPurchaseToken))
            {
                using var supersede = Create(HttpMethod.Patch,
                    $"rest/v1/subscriptions?provider=eq.google_play&subscriber_id=eq.{Uri.EscapeDataString(row.SubscriberId)}&provider_payment_id=eq.{Uri.EscapeDataString(purchase.LinkedPurchaseToken)}");
                supersede.Content = JsonContent.Create(new {status="cancelled",cancelled_at=DateTimeOffset.UtcNow});
                using var superseded = await httpClient.SendAsync(supersede, cancellationToken);
                superseded.EnsureSuccessStatusCode();
            }
        }
    }

    private sealed record StoreRow(
        [property: JsonPropertyName("subscription_id")] string Id,
        [property: JsonPropertyName("subscriber_id")] string SubscriberId,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("provider_payment_id")] string PaymentId,
        [property: JsonPropertyName("provider_transaction_id")] string? TransactionId,
        [property: JsonPropertyName("provider_token")] string? Token,
        [property: JsonPropertyName("tier_code")] string Tier,
        [property: JsonPropertyName("next_renewal_at")] DateTimeOffset? Expires,
        [property: JsonPropertyName("status")] string Status);
}

public sealed class StoreSubscriptionReconciliationWorker(IServiceScopeFactory scopes,
    ILogger<StoreSubscriptionReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<StoreSubscriptionReconciliationService>().ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Store subscription reconciliation failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
