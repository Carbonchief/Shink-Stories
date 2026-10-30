using System.Security.Cryptography;
using System.Text;

namespace Shink.Mobile.Services;

public sealed record MobilePushIdentity(Guid InstallationId, string InstallationSecret);
public sealed record MobilePushStatus(bool ApnsEnabled, bool FcmEnabled);
public sealed record MobilePushRegistration(Guid RegistrationId);

public static class MobilePushInstallation
{
    private const string IdentityKey = "mobile_push_identity_v1";
    private const string RevocationKey = "mobile_push_revocation_v1";
    private const string RegistrationKey = "mobile_push_registration_v1";
    private const string AccountKey = "mobile_push_account_v1";
    private const string ActiveAccountKey = "mobile_push_active_account_v1";
    private const string TokenKey = "mobile_push_token_hash_v1";
    private const string RegisteredAtKey = "mobile_push_registered_at_v1";
    private const string DirtyKey = "mobile_push_registration_dirty_v1";
    public static readonly SemaphoreSlim OperationGate = new(1, 1);
    public static bool Enabled
    {
        get => Preferences.Default.Get("mobile_push_enabled_v1", false);
        set => Preferences.Default.Set("mobile_push_enabled_v1", value);
    }
    public static string RegistrationId => Preferences.Default.Get(RegistrationKey, string.Empty);
    public static bool IsRegistered => !string.IsNullOrEmpty(RegistrationId);

    // The caller holds OperationGate while creating or using this capability.
    public static async Task<MobilePushIdentity> GetIdentityAsync()
    {
        var cached = await SecureStorage.Default.GetAsync(IdentityKey);
        if (cached?.Split(':') is [var id, var secret] && Guid.TryParse(id, out var installationId) && secret.Length == 64)
            return new(installationId, secret);
        var identity = new MobilePushIdentity(Guid.NewGuid(), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        await SecureStorage.Default.SetAsync(IdentityKey, identity.InstallationId.ToString("D") + ":" + identity.InstallationSecret);
        return identity;
    }

    public static Task MarkRegistrationAttemptAsync(MobilePushIdentity identity) => SecureStorage.Default.SetAsync(
        RevocationKey, identity.InstallationId.ToString("D") + ":" + identity.InstallationSecret);

    public static async Task<MobilePushIdentity?> GetRevocationIdentityAsync()
    {
        var cached = await SecureStorage.Default.GetAsync(RevocationKey);
        if (cached?.Split(':') is [var id, var secret] && Guid.TryParse(id, out var installationId) && secret.Length == 64)
            return new(installationId, secret);
        return IsRegistered ? await GetIdentityAsync() : null;
    }

    public static void Save(Guid registrationId, string email, string token)
    {
        Preferences.Default.Remove(DirtyKey);
        Preferences.Default.Set(RegistrationKey, registrationId.ToString("D"));
        Preferences.Default.Set(AccountKey, Hash(email.Trim().ToLowerInvariant()));
        Preferences.Default.Set(TokenKey, Hash(token));
        Preferences.Default.Set(RegisteredAtKey, DateTimeOffset.UtcNow.ToString("O"));
    }

    public static bool NeedsRefresh(string email, string token) => Preferences.Default.Get(DirtyKey, false) || !MatchesAccount(email)
        || Preferences.Default.Get(TokenKey, string.Empty) != Hash(token)
        || !DateTimeOffset.TryParse(Preferences.Default.Get(RegisteredAtKey, string.Empty), out var registeredAt)
        || DateTimeOffset.UtcNow - registeredAt > TimeSpan.FromDays(1);

    public static bool MatchesAccount(string? email) => IsRegistered && !string.IsNullOrWhiteSpace(email)
        && Preferences.Default.Get(AccountKey, string.Empty) == Hash(email.Trim().ToLowerInvariant());

    public static void UpdateCurrentAccount(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) Preferences.Default.Remove(ActiveAccountKey);
        else Preferences.Default.Set(ActiveAccountKey, Hash(email.Trim().ToLowerInvariant()));
    }

    public static void ForgetRegistration()
    {
        SecureStorage.Default.Remove(RevocationKey);
        Preferences.Default.Remove(DirtyKey);
        Preferences.Default.Remove(RegistrationKey);
        Preferences.Default.Remove(AccountKey);
        Preferences.Default.Remove(TokenKey);
        Preferences.Default.Remove(RegisteredAtKey);
    }

    // A lost revocation response may mean the server is already disabled. A later
    // opt-in must refresh rather than trust the previous local registration cache.
    public static void MarkRevocationAttempt() => Preferences.Default.Set(DirtyKey, true);

    public static bool CanDisplay(string? registrationId) => Enabled && IsRegistered
        && Preferences.Default.Get("mobile_session_is_signed_in", false)
        && string.Equals(registrationId, RegistrationId, StringComparison.Ordinal)
        && !string.IsNullOrEmpty(Preferences.Default.Get(ActiveAccountKey, string.Empty))
        && Preferences.Default.Get(ActiveAccountKey, string.Empty) == Preferences.Default.Get(AccountKey, string.Empty);

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
