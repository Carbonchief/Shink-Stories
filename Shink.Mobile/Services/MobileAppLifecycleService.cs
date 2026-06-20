namespace Shink.Mobile.Services;

public sealed class MobileAppLifecycleService
{
    private const string LastStoppedUtcPreferenceKey = "schink_mobile_last_stopped_utc";
    private const string LastDestroyedUtcPreferenceKey = "schink_mobile_last_destroyed_utc";

    public bool IsBackgrounded { get; private set; }

    public void OnStopped()
    {
        IsBackgrounded = true;
        Preferences.Default.Set(LastStoppedUtcPreferenceKey, DateTimeOffset.UtcNow.ToString("O"));
    }

    public void OnResumed()
    {
        IsBackgrounded = false;
    }

    public void OnDestroying()
    {
        Preferences.Default.Set(LastDestroyedUtcPreferenceKey, DateTimeOffset.UtcNow.ToString("O"));
    }
}
