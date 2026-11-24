using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace Shink.Services;

public sealed partial class SupabaseAdminManagementService
{
    private static readonly IReadOnlyDictionary<AdminActivityPanel, SemaphoreSlim> ActivityLoadGates =
        Enum.GetValues<AdminActivityPanel>().ToDictionary(panel => panel, _ => new SemaphoreSlim(1, 1));
    private static readonly IReadOnlyDictionary<AdminReportPanel, SemaphoreSlim> ReportLoadGates =
        Enum.GetValues<AdminReportPanel>().ToDictionary(panel => panel, _ => new SemaphoreSlim(1, 1));
    private static readonly TimeSpan AdminSnapshotCacheDuration = TimeSpan.FromSeconds(30);

    private static readonly SemaphoreSlim FinancialInputsGate = new(1, 1);
    private sealed record FinancialInputsResponse([property: JsonRequired] Dictionary<string, string> Versions,
        [property: JsonRequired] ReportInputs Inputs);

    // The two tabs overlap on almost every input row. Revalidate each section
    // in one database snapshot, including on manual refresh and after midnight.
    private async Task<ReportInputs> ReadFinancialReportInputsAsync(Uri baseUri, string apiKey,
        Func<AdminFinancialOverview, Task>? onOverviewLoaded, CancellationToken token)
    {
        var key = $"admin:financial-inputs:{baseUri.AbsoluteUri}";
        var full = ReadValidatedFinancialInputsAsync(baseUri, apiKey, key, token);
        if (onOverviewLoaded is null || full.IsCompleted || _memoryCache.TryGetValue(key, out _)) return await full;

        async Task PublishOverviewAsync()
        {
            using var pending = CancellationTokenSource.CreateLinkedTokenSource(token);
            var overview = ReadFinancialOverviewAsync(baseUri, apiKey, pending.Token);
            if (await Task.WhenAny(full, overview) == full)
                await pending.CancelAsync();
            try
            {
                if (await overview is { } value && !full.IsCompleted)
                    await onOverviewLoaded(value);
            }
            catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        }
        await Task.WhenAll(full, PublishOverviewAsync());
        return await full;
    }

    private async Task<ReportInputs> ReadValidatedFinancialInputsAsync(Uri baseUri, string apiKey, string key,
        CancellationToken token)
    {
        await FinancialInputsGate.WaitAsync(token);
        try
        {
            _memoryCache.TryGetValue<FinancialInputsResponse>(key, out var previous);
            var result = await InvokeRpcAsync<FinancialInputsResponse>(baseUri, apiKey, "get_admin_financial_report_inputs",
                new { p_known_versions = previous?.Versions }, token, throwOnFailure: true);
            if (result?.Inputs is null || result.Versions is null)
                throw new JsonException("Missing financial report inputs.");

            T[] Resolve<T>(string section, T[]? rows, T[]? oldRows)
            {
                if (rows is null || !result.Versions.TryGetValue(section, out var version) || string.IsNullOrEmpty(version))
                    throw new JsonException($"Missing financial report section {section}.");
                return oldRows is not null && previous?.Versions.GetValueOrDefault(section) == version ? oldRows : rows;
            }

            var delta = result.Inputs;
            var old = previous?.Inputs;
            var inputs = new ReportInputs(
                Resolve("subscribers", delta.Subscribers, old?.Subscribers),
                Resolve("subscriptions", delta.Subscriptions, old?.Subscriptions),
                Resolve("events", delta.Events, old?.Events),
                Resolve("tiers", delta.Tiers, old?.Tiers),
                Resolve("recoveries", delta.Recoveries, old?.Recoveries),
                Resolve("carts", delta.Carts, old?.Carts),
                Resolve("orders", delta.Orders, old?.Orders), []);
            token.ThrowIfCancellationRequested();
            _memoryCache.Set(key, result with { Inputs = inputs }, TimeSpan.FromHours(12));
            return inputs;
        }
        finally
        {
            FinancialInputsGate.Release();
        }
    }

    private sealed record OverviewGroup(int Quantity, SubscriptionRow Sample);
    private sealed record OverviewInputs([property: JsonPropertyName("as_of")] DateTimeOffset AsOf, OverviewGroup[] Groups);

    private async Task<AdminFinancialOverview?> ReadFinancialOverviewAsync(Uri baseUri, string apiKey, CancellationToken token)
    {
        var input = await InvokeRpcAsync<OverviewInputs>(baseUri, apiKey, "get_admin_financial_overview", new { }, token);
        if (input?.Groups is null || input.AsOf == default) return null;

        int free = 0, paid = 0;
        var monthlyRevenue = 0m;
        foreach (var group in input.Groups)
        {
            if (group.Sample is null || group.Quantity <= 0) return null;
            if (IsCurrentSystemTierDistributionSubscription(group.Sample, input.AsOf))
            {
                if (string.Equals(group.Sample.TierCode!.Trim(), "gratis", StringComparison.OrdinalIgnoreCase))
                    free += group.Quantity;
                else
                    paid += group.Quantity;
            }
            if (IsRecurringRevenueEligible(group.Sample, input.AsOf))
            {
                monthlyRevenue += group.Quantity * MonthlyBillingAmount(group.Sample.BillingAmountZar!.Value,
                    ResolveSubscriptionBillingPeriodMonths(group.Sample));
            }
        }
        return new(free, paid, decimal.Round(monthlyRevenue, 2, MidpointRounding.AwayFromZero));
    }

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
