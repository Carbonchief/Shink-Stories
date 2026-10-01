using System.Net;

namespace Shink.Mobile.Services;

internal sealed class StoreRecoveryBackoff
{
    private int _failures;
    private DateTimeOffset _retryAt;

    public bool CanAttempt(DateTimeOffset now) => now >= _retryAt;

    public TimeSpan Failed(DateTimeOffset now)
    {
        _failures = Math.Min(_failures + 1, 6);
        var delay = TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, _failures - 1), 900));
        _retryAt = now + delay;
        return delay;
    }

    public void Succeeded()
    {
        _failures = 0;
        _retryAt = default;
    }
}

internal sealed class MobileStoreUnavailableException()
    : InvalidOperationException("Die winkel kon nie vir herstel verbind word nie.");

internal static class MobileTransientFailure
{
    public static string? Reason(Exception exception) => exception switch
    {
        MobileStoreUnavailableException => "store_unavailable",
        OperationCanceledException => "cancelled_or_timed_out",
        TimeoutException => "timed_out",
        HttpRequestException { StatusCode: null } => "network_unavailable",
        HttpRequestException { StatusCode: { } status } when (int)status >= 500 && (int)status <= 599
            => "server_unavailable",
        WebException { Status: WebExceptionStatus.ConnectFailure or WebExceptionStatus.ConnectionClosed
            or WebExceptionStatus.NameResolutionFailure or WebExceptionStatus.Timeout
            or WebExceptionStatus.ReceiveFailure or WebExceptionStatus.SendFailure
            or WebExceptionStatus.RequestCanceled } => "network_unavailable",
        _ => null
    };
}

internal sealed class AudioPlaybackLoadException(string message) : IOException(message);

internal static class AudioPlaybackRetry
{
    // Renew authorization once. Local/offline files never trigger a network retry.
    public static async Task<string> PlayAsync(string url, Func<string, Task> play,
        Func<Task<string?>> refreshUrl)
    {
        try
        {
            await play(url);
            return url;
        }
        catch (Exception exception) when (
            (exception is AudioPlaybackLoadException or TimeoutException) &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "http" or "https"))
        {
            var refreshedUrl = await refreshUrl();
            if (string.IsNullOrWhiteSpace(refreshedUrl)) throw;
            await play(refreshedUrl);
            return refreshedUrl;
        }
    }
}
