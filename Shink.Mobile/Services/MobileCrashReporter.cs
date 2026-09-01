using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
#if ANDROID
using Android.Runtime;
#endif

namespace Shink.Mobile.Services;

public sealed class MobileCrashReporter
{
    private const string PendingCrashFilePrefix = "posthog-pending-crash-";
    private const string PendingCrashFileExtension = ".json";
    private const int MaxPendingCrashReportsPerLaunch = 10;
    private const int MaxStoredMessageLength = 2_048;
    private const int MaxStoredStackTraceLength = 24_000;
    private static readonly TimeSpan FatalFlushTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReplayFlushTimeout = TimeSpan.FromSeconds(10);
    private static readonly Regex SensitiveValuePattern = new(
        @"(?i)((?:bearer\s+)|(?:(?:access_token|refresh_token|token|authorization|password|secret|signature|sig|code)\s*[=:]\s*))[^&\s,;]+",
        RegexOptions.CultureInvariant);

    private readonly MobileAnalyticsService _analytics;
    private int _isStarted;
    private int _isHandlingFatalException;

    public MobileCrashReporter(MobileAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    public void Start()
    {
        if (!_analytics.IsConfigured || Interlocked.Exchange(ref _isStarted, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
#if ANDROID
        AndroidEnvironment.UnhandledExceptionRaiser += OnAndroidUnhandledException;
#endif
        _ = ReplayPendingCrashesAsync();
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        var exception = eventArgs.ExceptionObject as Exception ??
            new InvalidOperationException(
                $"Unhandled object of type {eventArgs.ExceptionObject?.GetType().FullName ?? "unknown"}.");

        HandleFatalException(exception, "app_domain_unhandled_exception", eventArgs.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        _analytics.TrackException(
            eventArgs.Exception,
            "unobserved_task_exception",
            new Dictionary<string, object>
            {
                ["global_exception_handler"] = true,
                ["is_terminating"] = false,
                ["managed_thread_id"] = Environment.CurrentManagedThreadId
            });
        _analytics.Flush();
    }

#if ANDROID
    private void OnAndroidUnhandledException(object? sender, RaiseThrowableEventArgs eventArgs) =>
        HandleFatalException(eventArgs.Exception, "android_unhandled_exception", isTerminating: true);
#endif

    private void HandleFatalException(Exception exception, string origin, bool isTerminating)
    {
        if (Interlocked.Exchange(ref _isHandlingFatalException, 1) == 1)
        {
            return;
        }

        var pendingCrashPath = PersistPendingCrash(exception, origin, isTerminating);
        var delivered = false;

        try
        {
            delivered = _analytics.TrackExceptionAndFlushAsync(
                    exception,
                    origin,
                    new Dictionary<string, object>
                    {
                        ["global_exception_handler"] = true,
                        ["is_terminating"] = isTerminating,
                        ["managed_thread_id"] = Environment.CurrentManagedThreadId
                    },
                    FatalFlushTimeout)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // The persisted report is retried after the next launch.
        }

        if (delivered && pendingCrashPath is not null)
        {
            TryDelete(pendingCrashPath);
        }
    }

    private async Task ReplayPendingCrashesAsync()
    {
        string[] pendingCrashPaths;
        try
        {
            pendingCrashPaths = Directory
                .EnumerateFiles(
                    FileSystem.AppDataDirectory,
                    $"{PendingCrashFilePrefix}*{PendingCrashFileExtension}",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(MaxPendingCrashReportsPerLaunch)
                .ToArray();
        }
        catch
        {
            return;
        }

        foreach (var pendingCrashPath in pendingCrashPaths)
        {
            PendingMobileCrash? pendingCrash;
            try
            {
                var json = await File.ReadAllTextAsync(pendingCrashPath).ConfigureAwait(false);
                pendingCrash = JsonSerializer.Deserialize(
                    json,
                    MobileCrashJsonContext.Default.PendingMobileCrash);
            }
            catch
            {
                TryQuarantine(pendingCrashPath);
                continue;
            }

            if (pendingCrash is null)
            {
                TryQuarantine(pendingCrashPath);
                continue;
            }

            var exception = CreateReplayException(pendingCrash);
            var delivered = await _analytics.TrackExceptionAndFlushAsync(
                    exception,
                    "previous_session_managed_crash",
                    new Dictionary<string, object>
                    {
                        ["recovered_after_restart"] = true,
                        ["original_exception_type"] = pendingCrash.ExceptionType,
                        ["original_crash_origin"] = pendingCrash.Origin,
                        ["original_is_terminating"] = pendingCrash.IsTerminating,
                        ["original_captured_at_utc"] = pendingCrash.CapturedAtUtc,
                        ["original_app_version"] = pendingCrash.AppVersion,
                        ["original_app_build"] = pendingCrash.AppBuild,
                        ["original_platform"] = pendingCrash.Platform,
                        ["original_os_version"] = pendingCrash.OsVersion
                    },
                    ReplayFlushTimeout)
                .ConfigureAwait(false);

            if (!delivered)
            {
                return;
            }

            TryDelete(pendingCrashPath);
        }
    }

    private static string? PersistPendingCrash(Exception exception, string origin, bool isTerminating)
    {
        try
        {
            var pendingCrash = new PendingMobileCrash(
                exception.GetType().FullName ?? exception.GetType().Name,
                SanitizeForStorage(exception.Message, MaxStoredMessageLength),
                SanitizeForStorage(exception.StackTrace, MaxStoredStackTraceLength),
                origin,
                isTerminating,
                DateTimeOffset.UtcNow,
                ReadSafely(() => AppInfo.VersionString),
                ReadSafely(() => AppInfo.BuildString),
                ReadSafely(() => DeviceInfo.Platform.ToString()),
                ReadSafely(() => DeviceInfo.VersionString));

            var appDataDirectory = FileSystem.AppDataDirectory;
            Directory.CreateDirectory(appDataDirectory);
            var fileName = $"{PendingCrashFilePrefix}{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{PendingCrashFileExtension}";
            var pendingCrashPath = System.IO.Path.Combine(appDataDirectory, fileName);
            var temporaryPath = $"{pendingCrashPath}.tmp";
            var json = JsonSerializer.Serialize(
                pendingCrash,
                MobileCrashJsonContext.Default.PendingMobileCrash);

            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, pendingCrashPath);
            return pendingCrashPath;
        }
        catch
        {
            return null;
        }
    }

    private static Exception CreateReplayException(PendingMobileCrash pendingCrash)
    {
        var exception = new PreviousSessionCrashException(
            $"{pendingCrash.ExceptionType}: {pendingCrash.Message}");

        if (!string.IsNullOrWhiteSpace(pendingCrash.StackTrace))
        {
            try
            {
                ExceptionDispatchInfo.SetRemoteStackTrace(exception, pendingCrash.StackTrace);
            }
            catch
            {
                // The original stack is also represented by the crash metadata.
            }
        }

        return exception;
    }

    private static string SanitizeForStorage(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = SensitiveValuePattern.Replace(value, "$1[redacted]");
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }

    private static string ReadSafely(Func<string> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return "unknown";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A duplicate retry is preferable to losing the only local report.
        }
    }

    private static void TryQuarantine(string path)
    {
        try
        {
            File.Move(path, $"{path}.invalid", overwrite: true);
        }
        catch
        {
            // Leave the unreadable report in place when it cannot be quarantined.
        }
    }

    private sealed class PreviousSessionCrashException : Exception
    {
        public PreviousSessionCrashException(string message)
            : base(message)
        {
        }
    }
}

internal sealed record PendingMobileCrash(
    string ExceptionType,
    string Message,
    string StackTrace,
    string Origin,
    bool IsTerminating,
    DateTimeOffset CapturedAtUtc,
    string AppVersion,
    string AppBuild,
    string Platform,
    string OsVersion);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PendingMobileCrash))]
internal sealed partial class MobileCrashJsonContext : JsonSerializerContext
{
}
