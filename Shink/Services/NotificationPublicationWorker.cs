using Microsoft.Extensions.Options;

namespace Shink.Services;

// Web inboxes are published even when mobile provider sending is disabled.
public sealed class NotificationPublicationWorker(
    IServiceScopeFactory scopes, IOptions<SupabaseOptions> options,
    ILogger<NotificationPublicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Url) || string.IsNullOrWhiteSpace(options.Value.SecretKey)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<SupabaseMobilePushStore>();
                if (await store.ProcessPublicationBatchAsync(stoppingToken) > 0) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notification publication batch failed. The worker will resume from its last commit.");
            }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
