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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<SupabaseMobilePushStore>();
                var deliveries = await store.ClaimAsync(platforms, stoppingToken);
                await Parallel.ForEachAsync(deliveries, new ParallelOptions
                {
                    CancellationToken = stoppingToken, MaxDegreeOfParallelism = 4
                }, async (delivery, cancellationToken) =>
                {
                    MobilePushSendResult result;
                    try
                    {
                        result = await store.CanSendAsync(delivery, cancellationToken)
                            ? await sender.SendAsync(delivery, cancellationToken)
                            : new("failed", "registration_inactive");
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        // Persist only a safe code: provider errors may include tokens or credentials.
                        result = new("retry", "provider_request_failed", 300);
                    }
                    await store.CompleteAsync(delivery, result, cancellationToken);
                });
                if (deliveries.Count == 20) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                logger.LogWarning("Mobile push queue processing failed; it will retry. Check the push migration and provider configuration.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
