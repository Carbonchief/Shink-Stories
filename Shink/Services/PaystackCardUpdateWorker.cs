namespace Shink.Services;

public sealed class PaystackCardUpdateWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PaystackCardUpdateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISubscriptionLedgerService>()
                    .ProcessPendingPaystackCardUpdatesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Paystack card-update worker failed; durable jobs remain available for retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
