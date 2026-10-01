using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        var crashReportId = Guid.NewGuid().ToString("N");
        var identity = _analytics.CurrentIdentity;
        var pendingCrashPath = PersistPendingCrash(exception, origin, isTerminating, crashReportId, identity);
        var delivered = false;

        try
        {
            delivered = _analytics.TrackOriginalExceptionAndFlushAsync(
                    exception,
                    origin,
                    new Dictionary<string, object>
                    {
                        ["global_exception_handler"] = true,
                        ["is_terminating"] = isTerminating,
                        ["managed_thread_id"] = Environment.CurrentManagedThreadId,
                        ["crash_report_id"] = crashReportId
                    },
                    FatalFlushTimeout, identity)
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
            // Older reports have no account attribution. Never infer their
            // owner from whichever account happens to be signed in now.
            var identity = string.IsNullOrWhiteSpace(pendingCrash.AnalyticsDistinctId)
                ? _analytics.AnonymousIdentity
                : new MobileAnalyticsIdentity(pendingCrash.AnalyticsDistinctId, pendingCrash.AnalyticsEmail);
            var delivered = await _analytics.TrackOriginalExceptionAndFlushAsync(
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
                        ["original_os_version"] = pendingCrash.OsVersion,
                        ["crash_report_id"] = string.IsNullOrWhiteSpace(pendingCrash.CrashReportId)
                            ? System.IO.Path.GetFileNameWithoutExtension(pendingCrashPath) : pendingCrash.CrashReportId,
                        ["original_stack_trace"] = SanitizeForStorage(pendingCrash.StackTrace, MaxStoredStackTraceLength),
                        ["native_stack_trace"] = SanitizeForStorage(pendingCrash.NativeStackTrace, MaxStoredStackTraceLength)
                    },
                    ReplayFlushTimeout, identity)
                .ConfigureAwait(false);

            if (!delivered)
            {
                return;
            }

            TryDelete(pendingCrashPath);
        }
    }

    private static string? PersistPendingCrash(Exception exception, string origin, bool isTerminating,
        string crashReportId, MobileAnalyticsIdentity identity)
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
                ReadSafely(() => DeviceInfo.VersionString))
            {
                CrashReportId = crashReportId,
                NativeStackTrace = MobileCrashDiagnostics.NativeStackTrace(exception),
                AnalyticsDistinctId = identity.DistinctId,
                AnalyticsEmail = identity.Email
            };

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
        return MobileCrashDiagnostics.Sanitize(value, maximumLength);
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
    string OsVersion)
{
    public string CrashReportId { get; init; } = string.Empty;
    public string NativeStackTrace { get; init; } = string.Empty;
    public string AnalyticsDistinctId { get; init; } = string.Empty;
    public string? AnalyticsEmail { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PendingMobileCrash))]
internal sealed partial class MobileCrashJsonContext : JsonSerializerContext
{
}
