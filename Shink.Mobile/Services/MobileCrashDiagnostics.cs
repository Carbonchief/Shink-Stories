using System.Text.RegularExpressions;

namespace Shink.Mobile.Services;

internal static class MobileCrashDiagnostics
{
    private static readonly Regex SensitiveValuePattern = new(
        @"(?i)((?:bearer\s+)|(?:(?:access_token|refresh_token|token|authorization|password|secret|signature|sig|code)\s*[=:]\s*))[^&\s,;]+",
        RegexOptions.CultureInvariant);

    public static string Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sanitized = SensitiveValuePattern.Replace(value, "$1[redacted]");
        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength];
    }

    public static string NativeStackTrace(Exception exception)
    {
#if ANDROID
        try
        {
            for (Exception? current = exception, previous = null;
                 current is not null && !ReferenceEquals(current, previous);
                 previous = current, current = current.InnerException)
            {
                if (current is Java.Lang.Throwable nativeException)
                    return Sanitize(Android.Util.Log.GetStackTraceString(nativeException), 24_000);
            }
        }
        catch
        {
            // Native handles may already be unavailable during shutdown.
        }
#endif
        return string.Empty;
    }
}
