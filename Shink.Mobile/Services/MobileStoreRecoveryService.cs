namespace Shink.Mobile.Services;

// The native stores retain undelivered purchases. Recover them without prompting
// an iOS restore dialog, and finish only after the server durably accepts them.
public sealed class MobileStoreRecoveryService(
    IMobileStoreBillingService billing, MobileApiClient api, SessionState session,
    MobileAppLifecycleService lifecycle, MobileAnalyticsService analytics)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private string? _account;
    private bool _started;

    public void Start()
    {
        if (_started) return;
        _started = true;
        billing.PurchasesUpdated += (_, _) => _ = RecoverAsync();
        lifecycle.Resumed += (_, _) => _ = RecoverAsync();
        lifecycle.Destroying += (_, _) => _lifetime.Cancel();
        session.Changed += value =>
        {
            var account = value.IsSignedIn ? value.Email : null;
            if (string.Equals(_account, account, StringComparison.OrdinalIgnoreCase)) return;
            _account = account;
            _ = RecoverAsync();
        };
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            await RecoverAsync();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(_lifetime.Token)) await RecoverAsync();
        }
        catch (OperationCanceledException) { }
    }

    public async Task RecoverAsync()
    {
        if (lifecycle.IsBackgrounded || !await _gate.WaitAsync(0)) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            // Also attaches Apple's observer at startup, before any paywall is opened.
            var purchases = await MainThread.InvokeOnMainThreadAsync(() => billing.GetPendingPurchasesAsync(timeout.Token));
            var email = session.Current.Email;
            if (!session.Current.IsSignedIn || string.IsNullOrWhiteSpace(email)) return;
            var synced = false;
            foreach (var purchase in purchases.Where(p => p.ProductId is "schink_stories_maandeliks" or "schink_stories_jaarliks"))
            {
                if (!session.Current.IsSignedIn || !string.Equals(email, session.Current.Email, StringComparison.OrdinalIgnoreCase)) break;
                try
                {
                    var result = await api.SyncStorePurchaseAsync(new(purchase.Provider, purchase.ProductId,
                        purchase.ProviderPaymentId, purchase.ProviderTransactionId, purchase.ProviderToken, email), timeout.Token);
                    if (result is not null && !result.IsRetryable)
                        await MainThread.InvokeOnMainThreadAsync(() => billing.FinalizeAsync(purchase, timeout.Token));
                    synced |= result?.IsActive == true;
                }
                catch (OperationCanceledException) { break; }
                catch { /* Preserve this purchase and continue recovering other products. */ }
            }
            if (synced) await api.GetSessionAsync(timeout.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { analytics.TrackException(exception, "mobile_store_recovery_retry"); }
        finally { _gate.Release(); }
    }
}
