namespace Shink.Services;

public sealed class AbandonedCartRecoveryCancellationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AbandonedCartRecoveryCancellationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<AbandonedCartRecoveryCancellationWorker> _logger = logger;

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
            var recoveryService = scope.ServiceProvider.GetRequiredService<IAbandonedCartRecoveryService>();
            await recoveryService.CleanupResolvedScheduledEmailsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Abandoned cart recovery cancellation cleanup failed.");
        }
    }
}
