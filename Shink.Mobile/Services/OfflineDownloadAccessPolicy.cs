using System.Security.Cryptography;
using System.Text;

namespace Shink.Mobile.Services;

internal static class OfflineDownloadAccessPolicy
{
    public static string? BuildOwnerKey(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)))
            .ToLowerInvariant();
    }

    public static bool IsOwnedByCurrentAccount(
        bool requiresSubscription,
        string? downloadOwnerKey,
        string? currentOwnerKey)
    {
        if (!requiresSubscription)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(downloadOwnerKey) &&
            !string.IsNullOrWhiteSpace(currentOwnerKey) &&
            string.Equals(downloadOwnerKey, currentOwnerKey, StringComparison.Ordinal);
    }

    public static bool IsPlayable(
        bool requiresSubscription,
        string? downloadOwnerKey,
        DateTimeOffset lastAccessVerifiedAt,
        bool isSignedIn,
        bool hasFullStoryAccess,
        string? currentOwnerKey,
        DateTimeOffset now,
        TimeSpan accessRefreshWindow,
        bool hasPaidSubscription = false,
        bool requiresFullStoryAccess = true)
    {
        if (!requiresSubscription)
        {
            return true;
        }

        var hasRequiredAccess = requiresFullStoryAccess
            ? hasFullStoryAccess
            : hasPaidSubscription;
        return isSignedIn &&
            hasRequiredAccess &&
            IsOwnedByCurrentAccount(requiresSubscription, downloadOwnerKey, currentOwnerKey) &&
            now - lastAccessVerifiedAt <= accessRefreshWindow;
    }
}
