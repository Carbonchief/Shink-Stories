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

    public MobileAnalyticsService(
        IPostHogClient postHog,
        MobileAnalyticsSettings settings,
        SessionState sessionState)
    {
        _postHog = postHog;
        _settings = settings;
        _sessionState = sessionState;
        _anonymousDistinctId = GetOrCreateAnonymousDistinctId();
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
        Volatile.Write(ref _lastScreenName, screenName);
        var distinctId = ResolveDistinctId();
        var eventProperties = BuildProperties(properties);
        eventProperties["screen_name"] = screenName;

        TryCapture(() => _postHog.CaptureScreenView(distinctId, screenName, eventProperties));
    }

    public void TrackEvent(string eventName, IReadOnlyDictionary<string, object>? properties = null)
    {
        if (!_settings.IsConfigured || string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        TryCapture(() => _postHog.Capture(ResolveDistinctId(), eventName, BuildProperties(properties)));
    }

    public bool TrackException(Exception exception, string context, IReadOnlyDictionary<string, object>? properties = null)
    {
        if (!_settings.IsConfigured)
        {
            return false;
        }

        return TryCapture(() =>
        {
            var eventProperties = BuildProperties(properties);
            eventProperties["context"] = context;
            eventProperties["exception_type"] = exception.GetType().Name;
            return _postHog.CaptureException(exception, ResolveDistinctId(), eventProperties);
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
        // Analytics intentionally stays anonymous. Do not attach email addresses
        // or other account identifiers to the PostHog person profile.
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

    private Dictionary<string, object> BuildProperties(IReadOnlyDictionary<string, object>? properties = null)
    {
        var result = new Dictionary<string, object>
        {
            ["app"] = "schink_stories_mobile",
            ["platform"] = DeviceInfo.Platform.ToString(),
            ["device_model"] = DeviceInfo.Model,
            ["device_manufacturer"] = DeviceInfo.Manufacturer,
            ["os_version"] = DeviceInfo.VersionString,
            ["app_version"] = AppInfo.VersionString,
            ["app_build"] = AppInfo.BuildString,
            ["network_access"] = Connectivity.Current.NetworkAccess.ToString(),
            ["is_signed_in"] = _sessionState.Current.IsSignedIn,
            ["has_paid_subscription"] = _sessionState.Current.HasPaidSubscription,
            ["anonymous_distinct_id"] = _anonymousDistinctId
        };

        var lastScreenName = Volatile.Read(ref _lastScreenName);
        if (!string.IsNullOrWhiteSpace(lastScreenName))
        {
            result["last_screen_name"] = lastScreenName;
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

        return result;
    }

    private string ResolveDistinctId() => _anonymousDistinctId;

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
