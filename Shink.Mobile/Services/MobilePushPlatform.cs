using System.Reflection;

namespace Shink.Mobile.Services;

public static partial class MobilePushPlatform
{
    public static event Action? TokenChanged;
    public static event Action<MobilePushOpen>? NotificationOpened;
    private static string? _token;
    private static MobilePushOpen? _pendingOpen;
    private static TaskCompletionSource<string> _tokenSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static bool BuildEnabled => typeof(MobilePushPlatform).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "MobilePushEnabled" && string.Equals(attribute.Value, "true", StringComparison.OrdinalIgnoreCase));
    public static string PushEnvironment => typeof(MobilePushPlatform).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "MobilePushEnvironment")?.Value ?? "production";

    public static void ReceiveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        var changed = _token != token;
        _token = token;
        _tokenSource.TrySetResult(token);
        if (changed) TokenChanged?.Invoke();
    }

    public static void RegistrationFailed() => _tokenSource.TrySetException(new InvalidOperationException("Toestelregistrasie het misluk."));
    public static void OpenNotification(MobilePushOpen notification)
    {
        if (NotificationOpened is { } opened) opened(notification);
        else _pendingOpen = notification;
    }
    public static MobilePushOpen? TakePendingOpen() => Interlocked.Exchange(ref _pendingOpen, null);
    public static partial bool IsSupported();
    public static partial Task<bool> HasPermissionAsync(bool requestPermission);
    public static partial Task<string> GetTokenAsync(CancellationToken cancellationToken);
    public static partial void ClearDeliveredNotifications();
}
