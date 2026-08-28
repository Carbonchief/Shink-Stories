namespace Shink.Mobile.Services;

public sealed class MobileAppLifecycleService
{
    private const string LastStoppedUtcPreferenceKey = "schink_mobile_last_stopped_utc";
    private const string LastDestroyedUtcPreferenceKey = "schink_mobile_last_destroyed_utc";
    private readonly MobileApiClient _apiClient;
    private readonly MobileAnalyticsService _analytics;
    private int _isResumeSyncRunning;
#if IOS
    private readonly object _iosBackgroundTaskGate = new();
    private IntPtr _iosImageCacheBackgroundTask = UIKit.UIApplication.BackgroundTaskInvalid;
#endif

    public MobileAppLifecycleService(MobileApiClient apiClient, MobileAnalyticsService analytics)
    {
        _apiClient = apiClient;
        _analytics = analytics;
    }

    public bool IsBackgrounded { get; private set; }

    public event EventHandler? Stopping;

    public event EventHandler? Resumed;

    public event EventHandler? Destroying;

    public void OnStopped()
    {
        IsBackgrounded = true;
        var imageCacheQuiescence = _apiClient.SuspendImageCacheActivity();
#if IOS
        BeginIosImageCacheQuiescence(imageCacheQuiescence);
#endif
        Preferences.Default.Set(LastStoppedUtcPreferenceKey, DateTimeOffset.UtcNow.ToString("O"));
        _analytics.TrackLifecycle("stopped");
        _analytics.Flush();
        Stopping?.Invoke(this, EventArgs.Empty);
    }

    public void OnResumed()
    {
        _apiClient.ResumeImageCacheActivity();
        IsBackgrounded = false;
        _analytics.TrackLifecycle("resumed");
        Resumed?.Invoke(this, EventArgs.Empty);
        _ = RefreshLiveStateAfterResumeAsync();
    }

    public void OnDestroying()
    {
        _apiClient.SuspendImageCacheActivity();
        Preferences.Default.Set(LastDestroyedUtcPreferenceKey, DateTimeOffset.UtcNow.ToString("O"));
        _analytics.TrackLifecycle("destroying");
        _analytics.Flush();
        Destroying?.Invoke(this, EventArgs.Empty);
    }

#if IOS
    private void BeginIosImageCacheQuiescence(Task imageCacheQuiescence)
    {
        if (imageCacheQuiescence.IsCompleted)
        {
            return;
        }

        var application = UIKit.UIApplication.SharedApplication;
        IntPtr backgroundTask = UIKit.UIApplication.BackgroundTaskInvalid;
        backgroundTask = application.BeginBackgroundTask(
            "Schink image cache shutdown",
            () => EndIosImageCacheBackgroundTask(backgroundTask));
        if (backgroundTask == UIKit.UIApplication.BackgroundTaskInvalid)
        {
            return;
        }

        lock (_iosBackgroundTaskGate)
        {
            if (_iosImageCacheBackgroundTask != UIKit.UIApplication.BackgroundTaskInvalid)
            {
                application.EndBackgroundTask(_iosImageCacheBackgroundTask);
            }

            _iosImageCacheBackgroundTask = backgroundTask;
        }

        _ = CompleteIosImageCacheQuiescenceAsync(backgroundTask, imageCacheQuiescence);
    }

    private async Task CompleteIosImageCacheQuiescenceAsync(
        IntPtr backgroundTask,
        Task imageCacheQuiescence)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await imageCacheQuiescence.WaitAsync(timeout.Token);
        }
        catch
        {
            // Expiration also ends the task. Image writes have already received
            // cancellation and dispose their file handles as they unwind.
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(
                () => EndIosImageCacheBackgroundTask(backgroundTask));
        }
    }

    private void EndIosImageCacheBackgroundTask(IntPtr backgroundTask)
    {
        if (backgroundTask == UIKit.UIApplication.BackgroundTaskInvalid)
        {
            return;
        }

        lock (_iosBackgroundTaskGate)
        {
            if (_iosImageCacheBackgroundTask != backgroundTask)
            {
                return;
            }

            _iosImageCacheBackgroundTask = UIKit.UIApplication.BackgroundTaskInvalid;
        }

        UIKit.UIApplication.SharedApplication.EndBackgroundTask(backgroundTask);
    }
#endif

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
            _analytics.TrackEvent("mobile_resume_sync_completed");
        }
        catch (Exception ex)
        {
            _analytics.TrackException(ex, "mobile_resume_sync_failed");
            // Resume refresh is opportunistic; pages still handle their own visible refresh paths.
        }
        finally
        {
            Interlocked.Exchange(ref _isResumeSyncRunning, 0);
        }
    }
}
