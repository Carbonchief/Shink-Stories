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
    private string? _identifiedDistinctId;

    public MobileAnalyticsService(
        IPostHogClient postHog,
        MobileAnalyticsSettings settings,
        SessionState sessionState)
    {
        _postHog = postHog;
        _settings = settings;
        _sessionState = sessionState;
        _anonymousDistinctId = GetOrCreateAnonymousDistinctId();
        _sessionState.Changed += IdentifySession;
    }

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

    public void TrackException(Exception exception, string context, IReadOnlyDictionary<string, object>? properties = null)
    {
        if (!_settings.IsConfigured)
        {
            return;
        }

        var eventProperties = BuildProperties(properties);
        eventProperties["context"] = context;
        eventProperties["exception_type"] = exception.GetType().Name;
        TryCapture(() => _postHog.CaptureException(exception, ResolveDistinctId(), eventProperties));
    }

    public void IdentifyCurrentSession() =>
        IdentifySession(_sessionState.Current);

    public void Flush() =>
        _ = FlushAsync();

    private async Task FlushAsync()
    {
        if (!_settings.IsConfigured)
        {
            return;
        }

        try
        {
            await _postHog.FlushAsync();
        }
        catch
        {
            // Analytics flush is best-effort.
        }
    }

    private void IdentifySession(MobileSession session)
    {
        if (!_settings.IsConfigured || !session.IsSignedIn || string.IsNullOrWhiteSpace(session.Email))
        {
            return;
        }

        var distinctId = NormalizeDistinctId(session.Email);
        if (string.Equals(_identifiedDistinctId, distinctId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _identifiedDistinctId = distinctId;
        _ = IdentifySessionAsync(distinctId, session);
    }

    private async Task IdentifySessionAsync(string distinctId, MobileSession session)
    {
        try
        {
            await _postHog.IdentifyAsync(
                distinctId,
                new Dictionary<string, object>
                {
                    ["email"] = distinctId,
                    ["has_paid_subscription"] = session.HasPaidSubscription,
                    ["is_mobile_user"] = true,
                    ["mobile_platform"] = DeviceInfo.Platform.ToString(),
                    ["mobile_app_version"] = AppInfo.VersionString,
                    ["mobile_app_build"] = AppInfo.BuildString
                },
                new Dictionary<string, object>(),
                CancellationToken.None);
        }
        catch
        {
            // Identity enrichment must not affect sign-in state.
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

    private string ResolveDistinctId()
    {
        var email = _sessionState.Current.Email;
        return _sessionState.Current.IsSignedIn && !string.IsNullOrWhiteSpace(email)
            ? NormalizeDistinctId(email)
            : _anonymousDistinctId;
    }

    private static string NormalizeDistinctId(string value) =>
        value.Trim().ToLowerInvariant();

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

    private void TryCapture(Func<bool> capture)
    {
        try
        {
            _ = capture();
        }
        catch
        {
            // Analytics must never block app behavior.
        }
    }
}
