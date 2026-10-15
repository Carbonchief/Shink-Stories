using Microsoft.Extensions.Caching.Memory;

namespace Shink.Services;

public sealed partial class SupabaseAdminManagementService
{
    private static readonly SemaphoreSlim UsageSnapshotLoadGate = new(1, 1);
    private static readonly SemaphoreSlim SubscriberReportsLoadGate = new(1, 1);
    private static readonly TimeSpan AdminSnapshotCacheDuration = TimeSpan.FromSeconds(30);

    // Callers must verify admin access before reaching this cache, including on hits.
    // Sharing successful snapshots avoids repeating the same work across page reloads.
    private async Task<T> GetAdminSnapshotAsync<T>(
        string key,
        SemaphoreSlim gate,
        bool forceRefresh,
        Func<Task<T>> load,
        CancellationToken cancellationToken) where T : class
    {
        if (!forceRefresh && _memoryCache.TryGetValue<T>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _memoryCache.TryGetValue<T>(key, out cached) && cached is not null)
            {
                return cached;
            }

            var snapshot = await load();
            cancellationToken.ThrowIfCancellationRequested();
            _memoryCache.Set(key, snapshot, AdminSnapshotCacheDuration);
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }
}
