using System.Globalization;
using System.Reflection;
using PostHog;
using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public sealed record MobileAnalyticsSettings(string? ProjectApiKey, string? HostUrl)
{
    public const string DefaultHostUrl = "https://eu.i.posthog.com";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectApiKey) &&
        !string.IsNullOrWhiteSpace(HostUrl);

    public static MobileAnalyticsSettings FromEnvironment() =>
        new(
            ResolveValue("POSTHOG_PROJECT_API_KEY", "POSTHOG_API_KEY") ?? ResolveAssemblyMetadata("PostHogProjectApiKey"),
            ResolveValue("POSTHOG_HOST_URL", "POSTHOG_HOST") ?? ResolveAssemblyMetadata("PostHogHostUrl") ?? DefaultHostUrl);

    private static string? ResolveValue(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? ResolveAssemblyMetadata(string key)
    {
        var value = typeof(MobileAnalyticsSettings).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}

public sealed class MobileAnalyticsService
{
    private const string AnonymousDistinctIdPreferenceKey = "mobile_analytics_anonymous_distinct_id";
    private readonly IPostHogClient _postHog;
    private readonly MobileAnalyticsSettings _settings;
    private readonly SessionState _sessionState;
    private readonly string _anonymousDistinctId;
    private string? _lastScreenName;
    private readonly object _identityGate = new();
    private string? _lastIdentifiedEmail;

    public MobileAnalyticsService(
        IPostHogClient postHog,
        MobileAnalyticsSettings settings,
        SessionState sessionState)
    {
        _postHog = postHog;
        _settings = settings;
        _sessionState = sessionState;
        _anonymousDistinctId = GetOrCreateAnonymousDistinctId();
        _sessionState.Changed += _ => IdentifyCurrentSession();
    }

    public bool IsConfigured => _settings.IsConfigured;

    public void TrackAppOpened() =>
        TrackEvent("mobile_app_opened");

    public void TrackLifecycle(string lifecycleEvent) =>
        TrackEvent(
            "mobile_app_lifecycle",
            new Dictionary<string, object>
            {
                ["lifecycle_event"] = lifecycleEvent
            });

    public void TrackScreenView(string screenName, IReadOnlyDictionary<string, object>? properties = null)
    {
        if (!_settings.IsConfigured || string.IsNullOrWhiteSpace(screenName))
        {
            return;
        }

        screenName = screenName.Trim();
        var previousScreen = Interlocked.Exchange(ref _lastScreenName, screenName);
        TryCapture(() =>
        {
            var session = _sessionState.Current;
            var identity = MobileAnalyticsIdentity.FromSession(session, _anonymousDistinctId);
            var eventProperties = BuildProperties("$screen", session, identity, properties);
            eventProperties["screen_name"] = screenName;
            if (!string.IsNullOrWhiteSpace(previousScreen))
                eventProperties["previous_screen_name"] = previousScreen;
            return _postHog.CaptureScreenView(identity.DistinctId, screenName, eventProperties);
        });
    }

    public void TrackEvent(string eventName, IReadOnlyDictionary<string, object>? properties = null)
    {
        if (!_settings.IsConfigured || string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        TryCapture(() =>
        {
            var session = _sessionState.Current;
            var identity = MobileAnalyticsIdentity.FromSession(session, _anonymousDistinctId);
            return _postHog.Capture(identity.DistinctId, eventName, BuildProperties(eventName, session, identity, properties));
        });
    }

    public void TrackRecoverableFailure(Exception exception, string context,
        IReadOnlyDictionary<string, object>? properties = null)
    {
        var reason = MobileTransientFailure.Reason(exception);
        if (reason is null)
        {
            TrackException(exception, context, properties);
            return;
        }

        var eventProperties = properties?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<string, object>();
        eventProperties["failure_reason"] = reason;
        eventProperties["exception_type"] = exception.GetType().Name;
        eventProperties["outcome"] = "deferred";
        TrackEvent(context, eventProperties);
    }

    public bool TrackException(Exception exception, string context, IReadOnlyDictionary<string, object>? properties = null)
        => TrackExceptionCore(exception, context, properties);

    private bool TrackExceptionCore(Exception exception, string context,
        IReadOnlyDictionary<string, object>? properties, MobileAnalyticsIdentity? originalIdentity = null)
    {
        if (!_settings.IsConfigured)
        {
            return false;
        }

        return TryCapture(() =>
        {
            var session = _sessionState.Current;
            var identity = originalIdentity ?? MobileAnalyticsIdentity.FromSession(session, _anonymousDistinctId);
            var eventProperties = BuildProperties("$exception", session, identity, properties, isException: true);
            if (originalIdentity is not null)
            {
                // A replay may run after a different account signs in.
                eventProperties["is_signed_in"] = identity.Email is not null;
                eventProperties.Remove("has_paid_subscription");
            }
            eventProperties["context"] = context;
            eventProperties["exception_type"] = exception.GetType().Name;
            eventProperties["exception_details"] = MobileCrashDiagnostics.Sanitize(exception.ToString(), 24_000);
            var nativeStack = MobileCrashDiagnostics.NativeStackTrace(exception);
            if (!string.IsNullOrWhiteSpace(nativeStack)) eventProperties["native_stack_trace"] = nativeStack;
            return _postHog.CaptureException(exception, identity.DistinctId, eventProperties);
        });
    }

    public async Task<bool> TrackExceptionAndFlushAsync(
        Exception exception,
        string context,
        IReadOnlyDictionary<string, object>? properties = null,
        TimeSpan? timeout = null)
    {
        if (!TrackException(exception, context, properties))
        {
            return false;
        }

        return await FlushAsync(timeout).ConfigureAwait(false);
    }

    public void IdentifyCurrentSession()
    {
        if (!_settings.IsConfigured) return;
        lock (_identityGate)
        {
            var identity = CurrentIdentity;
            if (identity.Email is null)
            {
                _lastIdentifiedEmail = null;
                return;
            }
            if (identity.Email == _lastIdentifiedEmail) return;
            // The .NET SDK has explicit distinct IDs rather than a stateful
            // frontend identify/reset API. Set this account's person properties
            // without aliasing the shared installation to it.
            if (TryCapture(() =>
            {
                var properties = new Dictionary<string, object>();
                identity.ApplyTo(properties);
                return _postHog.Capture(identity.DistinctId, "$set", properties);
            }))
                _lastIdentifiedEmail = identity.Email;
        }
    }

    internal MobileAnalyticsIdentity CurrentIdentity =>
        MobileAnalyticsIdentity.FromSession(_sessionState.Current, _anonymousDistinctId);

    internal MobileAnalyticsIdentity AnonymousIdentity => new(_anonymousDistinctId, null);

    internal async Task<bool> TrackOriginalExceptionAndFlushAsync(Exception exception, string context,
        IReadOnlyDictionary<string, object> properties, TimeSpan timeout, MobileAnalyticsIdentity identity)
    {
        if (!TrackExceptionCore(exception, context, properties, identity)) return false;
        return await FlushAsync(timeout).ConfigureAwait(false);
    }

    public void Flush() =>
        _ = FlushAsync();

    public async Task<bool> FlushAsync(TimeSpan? timeout = null)
    {
        if (!_settings.IsConfigured)
        {
            return false;
        }

        try
        {
            var flushTask = _postHog.FlushAsync();
            if (timeout is { } flushTimeout)
            {
                await flushTask.WaitAsync(flushTimeout).ConfigureAwait(false);
            }
            else
            {
                await flushTask.ConfigureAwait(false);
            }

            return true;
        }
        catch
        {
            // Analytics flush is best-effort.
            return false;
        }
    }

    private Dictionary<string, object> BuildProperties(string eventName,
        MobileSession session, MobileAnalyticsIdentity identity,
        IReadOnlyDictionary<string, object>? properties = null, bool isException = false)
    {
        var result = MobileAnalyticsSchema.DeviceProperties(
            DeviceInfo.Platform.ToString(), DeviceInfo.Manufacturer, DeviceInfo.Model,
            DeviceInfo.Idiom.ToString(), DeviceInfo.DeviceType == DeviceType.Virtual,
            DeviceInfo.VersionString, AppInfo.VersionString, AppInfo.BuildString);
        result["app"] = "schink_stories_mobile";
        result["network_access"] = Connectivity.Current.NetworkAccess.ToString();
        result["is_signed_in"] = session.IsSignedIn;
        result["has_paid_subscription"] = session.HasPaidSubscription;
        result["anonymous_distinct_id"] = _anonymousDistinctId;
#if DEBUG
        result["build_configuration"] = "debug";
#else
        result["build_configuration"] = "release";
#endif

        var lastScreenName = Volatile.Read(ref _lastScreenName);
        if (!string.IsNullOrWhiteSpace(lastScreenName))
        {
            result["last_screen_name"] = lastScreenName;
            result["screen_name"] = lastScreenName;
        }

        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                if (!string.IsNullOrWhiteSpace(key) && value is not null)
                {
                    result[key] = NormalizePropertyValue(value);
                }
            }
        }

        identity.ApplyTo(result);
        MobileAnalyticsSchema.EnrichEvent(result, eventName, lastScreenName, isException);
        return result;
    }

    private static object NormalizePropertyValue(object value) =>
        value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue,
            double doubleValue when double.IsFinite(doubleValue) => doubleValue,
            float floatValue when float.IsFinite(floatValue) => floatValue,
            TimeSpan timeSpan => timeSpan.TotalSeconds,
            _ => value
        };

    private static string GetOrCreateAnonymousDistinctId()
    {
        var distinctId = Preferences.Get(AnonymousDistinctIdPreferenceKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(distinctId))
        {
            return distinctId;
        }

        distinctId = $"mobile-anon-{Guid.NewGuid():N}";
        Preferences.Set(AnonymousDistinctIdPreferenceKey, distinctId);
        return distinctId;
    }

    private bool TryCapture(Func<bool> capture)
    {
        try
        {
            return capture();
        }
        catch
        {
            // Analytics must never block app behavior.
            return false;
        }
    }
}
