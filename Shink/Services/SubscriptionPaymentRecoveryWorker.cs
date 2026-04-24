namespace Shink.Services;

public sealed class SubscriptionPaymentRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionPaymentRecoveryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<SubscriptionPaymentRecoveryWorker> _logger = logger;

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
            await ledgerService.ProcessExpiredPaymentRecoveriesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Subscription payment recovery worker iteration failed.");
        }
    }
}
