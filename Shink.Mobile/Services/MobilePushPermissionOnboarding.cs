namespace Shink.Mobile.Services;

internal sealed class MobilePushPermissionOnboarding
{
    private const string BannerSettingsOfferedKey = "mobile_push_banner_settings_offered_v1";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task OfferAsync(Func<bool> isCurrentAccount, Func<Task<bool>> requestPermission,
        Action refreshRegistration, Func<Task<bool>>? offerBannerSettings)
    {
        await _gate.WaitAsync();
        try
        {
            if (!isCurrentAccount()) return;
            if (!MobilePushInstallation.HasNotificationChoice)
            {
                var allowed = await requestPermission();
                if (!isCurrentAccount() || MobilePushInstallation.HasNotificationChoice) return;
                MobilePushInstallation.Enabled = allowed;
                refreshRegistration();
            }

            if (MobilePushInstallation.Enabled && offerBannerSettings is not null
                && !Preferences.Default.Get(BannerSettingsOfferedKey, false) && await offerBannerSettings())
                Preferences.Default.Set(BannerSettingsOfferedKey, true);
        }
        finally { _gate.Release(); }
    }
}
