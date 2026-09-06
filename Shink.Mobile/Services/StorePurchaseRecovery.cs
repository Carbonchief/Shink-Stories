namespace Shink.Mobile.Services;

public sealed record StoreDeliveryOutcome(bool IsActive, bool NeedsRetry);
public sealed record StoreRecoverySummary(int ActivePurchases, int RetryPurchases);

public static class StorePurchaseRecovery
{
    public static async Task<StoreRecoverySummary> RecoverAsync<T>(IEnumerable<T> purchases,
        Func<T, Task<StoreDeliveryOutcome>> deliver, CancellationToken cancellationToken = default)
    {
        var active = 0;
        var retry = 0;
        foreach (var purchase in purchases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await deliver(purchase);
                if (result.IsActive) active++;
                if (result.NeedsRetry) retry++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { retry++; }
        }
        return new(active, retry);
    }
}
