using Foundation;

namespace Shink.Mobile.Platforms.iOS;

internal static class IosHttpHandler
{
    public static NSUrlSessionHandler Create()
    {
        // The disk-backed URLSession cache and cookie stores can hold SQLite locks during suspension.
        using var configuration = NSUrlSessionConfiguration.EphemeralSessionConfiguration;
        configuration.URLCredentialStorage = null;
        // Match the default .NET native handler so the HttpClient timeout takes precedence.
        configuration.TimeoutIntervalForRequest = TimeSpan.FromHours(24).TotalSeconds;
        configuration.TimeoutIntervalForResource = TimeSpan.FromHours(24).TotalSeconds;
        return new NSUrlSessionHandler(configuration);
    }
}
