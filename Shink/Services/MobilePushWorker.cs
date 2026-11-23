using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class MobilePushWorker(
    IServiceScopeFactory scopes, IOptions<MobilePushOptions> options,
    MobilePushSender sender, ILogger<MobilePushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var platforms = new[] { "apns", "fcm" }.Where(options.Value.Supports).ToArray();
        if (platforms.Length == 0) return;
        MobilePushReceiptJournal? receipts = null;
        var nextPrune = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                receipts ??= MobilePushReceiptJournal.Open(options.Value);
                if (DateTimeOffset.UtcNow >= nextPrune)
                {
                    receipts.Prune();
                    nextPrune = DateTimeOffset.UtcNow.AddHours(12);
                }
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<SupabaseMobilePushStore>();
                var deliveries = await store.ClaimAsync(platforms, stoppingToken);
                await ProcessBatchAsync(store, receipts, deliveries, stoppingToken);
                if (deliveries.Count == 20) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                logger.LogWarning("Mobile push queue processing failed. Check push storage and the receipt directory.");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    internal Task ProcessBatchAsync(SupabaseMobilePushStore store, MobilePushReceiptJournal receipts,
        IReadOnlyList<MobilePushDelivery> deliveries, CancellationToken stoppingToken) =>
        Parallel.ForEachAsync(deliveries, new ParallelOptions
        {
            CancellationToken = stoppingToken, MaxDegreeOfParallelism = 4
        }, async (delivery, token) =>
        {
            try
            {
                if (receipts.HasReceipt(delivery))
                {
                    await AcknowledgeAcceptanceAsync(store, receipts, delivery, token);
                    return;
                }
                // A false result can also mean an expired lease. The database
                // cancels revoked deliveries and reclaims eligible expired leases.
                if (!await store.CanSendAsync(delivery, token)) return;

                MobilePushSendResult result;
                try { result = await sender.SendAsync(delivery, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { result = new("retry", "provider_request_failed", 300); }

                if (result.Result != "sent")
                {
                    await store.CompleteAsync(delivery, result, token);
                    return;
                }
                // Record even during shutdown. After a restart a new lease needs
                // only to acknowledge this acceptance, never to call the provider.
                try { receipts.Record(delivery); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogError("Could not journal an accepted push. Attempting the database acknowledgement immediately.");
                }
                await AcknowledgeAcceptanceAsync(store, receipts, delivery, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // This claim remains recoverable independently of its siblings.
                logger.LogWarning("Mobile push delivery {DeliveryId} could not be finalized. It will be reclaimed.", delivery.DeliveryId);
            }
        });

    private async Task AcknowledgeAcceptanceAsync(SupabaseMobilePushStore store, MobilePushReceiptJournal receipts,
        MobilePushDelivery delivery, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await store.RecordAcceptanceAsync(delivery, cancellationToken);
                break;
            }
            catch (Exception ex) when (attempt < 3 && !cancellationToken.IsCancellationRequested
                && ex is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
        try { receipts.Remove(delivery); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Push acceptance is recorded. The receipt for {DeliveryId} remains for cleanup.", delivery.DeliveryId);
        }
    }
}
