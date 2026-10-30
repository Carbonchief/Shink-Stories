using Shink.Mobile.Models;
using Shink.Mobile.Navigation;
using Shink.Mobile.Pages;

namespace Shink.Mobile.Services;

public sealed class MobilePushService(MobileApiClient api, SessionState session, PlaylistPlaybackState playlist)
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private MobilePushOpen? _pendingOpen;
    private bool _started;
    private bool _shellReady;
    private int _refreshRequested;
    public string Status { get; private set; } = "Ontvang nuwe stories en ander kennisgewings op jou selfoon.";
    public event Action? Changed;

    public void Start()
    {
        if (_started) return;
        _started = true;
        MobilePushPlatform.TokenChanged += Refresh;
        MobilePushPlatform.NotificationOpened += notification =>
        {
            _pendingOpen = notification;
            MainThread.BeginInvokeOnMainThread(() => _ = TryOpenAsync());
        };
        _pendingOpen = MobilePushPlatform.TakePendingOpen();
        session.Changed += OnSessionChanged;
        Refresh();
    }

    public void OnShellReady()
    {
        _shellReady = true;
        MainThread.BeginInvokeOnMainThread(() => _ = TryOpenAsync());
    }

    public void OnShellNavigated() => MainThread.BeginInvokeOnMainThread(() => _ = TryOpenAsync());

    public void Refresh()
    {
        Interlocked.Exchange(ref _refreshRequested, 1);
        _ = RefreshSafelyAsync();
    }

    private void OnSessionChanged(MobileSession current)
    {
        if (!current.IsSignedIn)
        {
            _pendingOpen = null;
            MobilePushPlatform.ClearDeliveredNotifications();
        }
        Refresh();
    }

    private async Task RefreshSafelyAsync()
    {
        // Collapse repeated session events; the active pass rechecks current state.
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            do
            {
                Interlocked.Exchange(ref _refreshRequested, 0);
                await RefreshCoreAsync(requestPermission: false);
            } while (Interlocked.Exchange(ref _refreshRequested, 0) == 1);
        }
        catch { SetStatus("Kennisgewings kon nie nou verbind nie. Probeer later weer."); }
        finally
        {
            _refreshGate.Release();
            if (Interlocked.Exchange(ref _refreshRequested, 0) == 1) Refresh();
        }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        // Stop local display immediately, even while revocation is being retried.
        MobilePushInstallation.Enabled = enabled;
        if (!enabled) MobilePushPlatform.ClearDeliveredNotifications();
        await _refreshGate.WaitAsync();
        try { await RefreshCoreAsync(requestPermission: enabled); }
        catch { SetStatus("Die verandering kon nie nou gestoor word nie. Probeer weer wanneer jy aanlyn is."); }
        finally
        {
            _refreshGate.Release();
            if (Interlocked.Exchange(ref _refreshRequested, 0) == 1) Refresh();
        }
    }

    private async Task RefreshCoreAsync(bool requestPermission)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var cancellationToken = timeout.Token;
        if (!session.Current.IsSignedIn || !MobilePushInstallation.Enabled)
        {
            await api.UnregisterPushDeviceAsync(cancellationToken);
            SetStatus("Selfoonkennisgewings is afgeskakel.");
            return;
        }
        var email = session.Current.Email;
        if (string.IsNullOrWhiteSpace(email)) return; // Sensitive cached identity is still loading.
        if (!MobilePushPlatform.IsSupported())
        {
            SetStatus("Selfoonkennisgewings is nog nie op hierdie toestel beskikbaar nie.");
            return;
        }
        var status = await api.GetPushStatusAsync(cancellationToken);
#if IOS
        var providerEnabled = status?.ApnsEnabled == true;
        const string platform = "apns";
#else
        var providerEnabled = status?.FcmEnabled == true;
        const string platform = "fcm";
#endif
        if (!providerEnabled)
        {
            SetStatus("Selfoonkennisgewings is nog nie beskikbaar nie. Probeer later weer.");
            return;
        }
        var granted = await MainThread.InvokeOnMainThreadAsync(() => MobilePushPlatform.HasPermissionAsync(requestPermission));
        if (!granted)
        {
            await api.UnregisterPushDeviceAsync(cancellationToken);
            SetStatus("Laat kennisgewings in jou selfoon se instellings toe.");
            return;
        }
        var token = await MainThread.InvokeOnMainThreadAsync(() => MobilePushPlatform.GetTokenAsync(cancellationToken));
        if (requestPermission || MobilePushInstallation.NeedsRefresh(email, token))
            await api.RegisterPushDeviceAsync(platform, MobilePushPlatform.PushEnvironment, token, email, cancellationToken);
        SetStatus(MobilePushInstallation.Enabled && MobilePushInstallation.MatchesAccount(session.Current.Email)
            ? "Selfoonkennisgewings is aangeskakel." : "Selfoonkennisgewings is afgeskakel.");
        await MainThread.InvokeOnMainThreadAsync(TryOpenAsync);
    }

    private void SetStatus(string value)
    {
        Status = value;
        MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke());
    }

    private async Task TryOpenAsync()
    {
        if (!_shellReady || _pendingOpen is null || !session.Current.IsSignedIn
            || session.Current.RequiresAvatarSelection || string.IsNullOrWhiteSpace(session.Current.Email)
            || Shell.Current?.CurrentPage is null || !await _navigationGate.WaitAsync(0)) return;
        try
        {
            var notification = _pendingOpen;
            _pendingOpen = null;
            if (notification is null || !MobilePushInstallation.CanDisplay(notification.RegistrationId.ToString("D"))
                || !MobilePushInstallation.MatchesAccount(session.Current.Email)) return;
            var target = MobileNotificationNavigation.Resolve(notification.Type, notification.Href);
            switch (target.Kind)
            {
                case MobileNotificationNavigationKind.Story:
                    await Shell.Current.GoToAsync("//Luister", animate: false);
                    if (!string.IsNullOrWhiteSpace(target.Value))
                    {
                        playlist.Clear();
                        await Shell.Current.GoToAsync($"{nameof(StoryDetailPage)}?slug={Uri.EscapeDataString(target.Value)}&source={Uri.EscapeDataString(target.Source ?? "luister")}", animate: false);
                    }
                    break;
                case MobileNotificationNavigationKind.Character:
                    await Shell.Current.GoToAsync("//Karakters", animate: false);
                    if (!string.IsNullOrWhiteSpace(target.Value) && Shell.Current.CurrentPage is KaraktersPage page)
                        await page.OpenCharacterFromNotificationAsync(target.Value);
                    break;
                case MobileNotificationNavigationKind.ResourceWebsite when !string.IsNullOrWhiteSpace(target.Value):
                    var url = api.BuildAbsoluteUrl(target.Value);
                    // Push data can only open this app's website, never arbitrary schemes/hosts.
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                        && Uri.TryCreate(api.BuildAbsoluteUrl("/"), UriKind.Absolute, out var origin) && uri.Host == origin.Host)
                        await Browser.OpenAsync(uri, BrowserLaunchMode.External);
                    break;
                default:
                    await Shell.Current.GoToAsync(nameof(KennisgewingsPage), animate: false);
                    break;
            }
            try { await api.MarkNotificationReadAsync(notification.NotificationId); } catch { /* Inbox can retry later. */ }
        }
        catch { SetStatus("Maak Kennisgewings oop om hierdie kennisgewing te sien."); }
        finally { _navigationGate.Release(); }
    }
}
