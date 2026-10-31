namespace Shink.Services;

public sealed class AdminOpsAlertDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AdminOpsAlertDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISubscriptionNotificationEmailService>()
                    .ProcessPendingAdminOpsAlertsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Admin ops alert dispatcher failed; stored alerts remain available for retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
