namespace Shink.Mobile.Services;

public sealed class MobileAppLifecycleService
{
    private const string LastStoppedUtcPreferenceKey = "schink_mobile_last_stopped_utc";
    private const string LastDestroyedUtcPreferenceKey = "schink_mobile_last_destroyed_utc";
    private readonly MobileApiClient _apiClient;
    private int _isResumeSyncRunning;

    public MobileAppLifecycleService(MobileApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public bool IsBackgrounded { get; private set; }

    public event EventHandler? Stopping;

    public event EventHandler? Resumed;

    public event EventHandler? Destroying;

    public void OnStopped()
    {
        IsBackgrounded = true;
        Preferences.Default.Set(LastStoppedUtcPreferenceKey, DateTimeOffset.UtcNow.ToString("O"));
        Stopping?.Invoke(this, EventArgs.Empty);
    }

    public void OnResumed()
    {
        IsBackgrounded = false;
        Resumed?.Invoke(this, EventArgs.Empty);
        _ = RefreshLiveStateAfterResumeAsync();
    }

    public void OnDestroying()
    {
        Preferences.Default.Set(LastDestroyedUtcPreferenceKey, DateTimeOffset.UtcNow.ToString("O"));
        Destroying?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshLiveStateAfterResumeAsync()
    {
        if (Interlocked.Exchange(ref _isResumeSyncRunning, 1) == 1)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _apiClient.GetSessionAsync(timeout.Token);
            await _apiClient.FlushQueuedStoryListensAsync(timeout.Token);
        }
        catch
        {
            // Resume refresh is opportunistic; pages still handle their own visible refresh paths.
        }
        finally
        {
            Interlocked.Exchange(ref _isResumeSyncRunning, 0);
        }
    }
}
