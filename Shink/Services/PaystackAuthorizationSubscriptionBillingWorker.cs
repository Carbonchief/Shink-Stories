namespace Shink.Services;

public sealed class PaystackAuthorizationSubscriptionBillingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PaystackAuthorizationSubscriptionBillingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<PaystackAuthorizationSubscriptionBillingWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ledgerService = scope.ServiceProvider.GetRequiredService<ISubscriptionLedgerService>();
            await ledgerService.ProcessPaystackAuthorizationScheduleAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Paystack authorization subscription billing worker iteration failed.");
        }
    }
}
