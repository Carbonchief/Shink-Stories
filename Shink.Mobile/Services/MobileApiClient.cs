using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public sealed class MobileAppSettings
{
    private const string BaseUrlPreferenceKey = "mobile_api_base_url";
    public const string DefaultBaseUrl = "https://www.schink.co.za";

    public string BaseUrl
    {
        get => Preferences.Get(BaseUrlPreferenceKey, DefaultBaseUrl);
        set
        {
            var normalized = NormalizeBaseUrl(value);
            Preferences.Set(BaseUrlPreferenceKey, IsValidMobileBaseUrl(normalized) ? normalized : DefaultBaseUrl);
        }
    }

    public static string NormalizeBaseUrl(string? value)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? DefaultBaseUrl : value.Trim();
        return candidate.TrimEnd('/');
    }

    public static bool IsValidMobileBaseUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl))
        {
            return false;
        }

        if (parsedUrl.Scheme is not "http" and not "https" || !parsedUrl.IsWellFormedOriginalString())
        {
            return false;
        }

        return parsedUrl.Host switch
        {
            "localhost" or "127.0.0.1" => false,
            _ => true
        };
    }
}

public sealed class SessionState
{
    private static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web);

    private const string SensitiveSessionPreferenceKey = "mobile_session_sensitive_v1";
    private const string IsSignedInPreferenceKey = "mobile_session_is_signed_in";
    private const string EmailPreferenceKey = "mobile_session_email";
    private const string DisplayNamePreferenceKey = "mobile_session_display_name";
    private const string ProfileImageUrlPreferenceKey = "mobile_session_profile_image_url";
    private const string FirstNamePreferenceKey = "mobile_session_first_name";
    private const string LastNamePreferenceKey = "mobile_session_last_name";
    private const string MobileNumberPreferenceKey = "mobile_session_mobile_number";
    private const string HasPaidSubscriptionPreferenceKey = "mobile_session_has_paid_subscription";
    private const string FavoriteStorySlugsPreferenceKey = "mobile_session_favorite_story_slugs";
    private const string LoginUrlPreferenceKey = "mobile_session_login_url";
    private const string SignupUrlPreferenceKey = "mobile_session_signup_url";
    private const string PlansUrlPreferenceKey = "mobile_session_plans_url";

    public MobileSession Current { get; private set; } = LoadCachedSession();

    public event Action<MobileSession>? Changed;

    public void Update(MobileSession session)
    {
        Current = session;
        SaveCachedSession(session);
        Changed?.Invoke(session);
    }

    private static MobileSession LoadCachedSession()
    {
        var isSignedIn = Preferences.Get(IsSignedInPreferenceKey, false);
        var sensitiveSession = isSignedIn ? LoadSensitiveCachedSession() : null;
        return new MobileSession(
            IsSignedIn: isSignedIn,
            Email: sensitiveSession?.Email,
            DisplayName: sensitiveSession?.DisplayName,
            ProfileImageUrl: sensitiveSession?.ProfileImageUrl,
            FirstName: sensitiveSession?.FirstName,
            LastName: sensitiveSession?.LastName,
            MobileNumber: sensitiveSession?.MobileNumber,
            HasPaidSubscription: Preferences.Get(HasPaidSubscriptionPreferenceKey, false),
            FavoriteStorySlugs: LoadFavoriteStorySlugs(),
            LoginUrl: Preferences.Get(LoginUrlPreferenceKey, string.Empty),
            SignupUrl: Preferences.Get(SignupUrlPreferenceKey, string.Empty),
            PlansUrl: Preferences.Get(PlansUrlPreferenceKey, string.Empty));
    }

    private static IReadOnlyList<string> LoadFavoriteStorySlugs()
    {
        var serializedSlugs = Preferences.Get(FavoriteStorySlugsPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(serializedSlugs))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(serializedSlugs, SessionJsonOptions) ?? Array.Empty<string>();
        }
        catch
        {
            Preferences.Remove(FavoriteStorySlugsPreferenceKey);
            return Array.Empty<string>();
        }
    }

    private static void SaveCachedSession(MobileSession session)
    {
        Preferences.Set(IsSignedInPreferenceKey, session.IsSignedIn);
        Preferences.Set(LoginUrlPreferenceKey, session.LoginUrl);
        Preferences.Set(SignupUrlPreferenceKey, session.SignupUrl);
        Preferences.Set(PlansUrlPreferenceKey, session.PlansUrl);

        if (!session.IsSignedIn)
        {
            RemoveLegacySensitivePreferences();
            SecureStorage.Default.Remove(SensitiveSessionPreferenceKey);
            Preferences.Remove(HasPaidSubscriptionPreferenceKey);
            Preferences.Remove(FavoriteStorySlugsPreferenceKey);
            return;
        }

        RemoveLegacySensitivePreferences();
        _ = SaveSensitiveCachedSessionAsync(session);
        Preferences.Set(HasPaidSubscriptionPreferenceKey, session.HasPaidSubscription);
        Preferences.Set(
            FavoriteStorySlugsPreferenceKey,
            JsonSerializer.Serialize(session.FavoriteStorySlugs ?? Array.Empty<string>(), SessionJsonOptions));
    }

    private static CachedSensitiveSession? LoadSensitiveCachedSession()
    {
        try
        {
            var serializedSession = SecureStorage.Default.GetAsync(SensitiveSessionPreferenceKey).GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(serializedSession))
            {
                return JsonSerializer.Deserialize<CachedSensitiveSession>(serializedSession, SessionJsonOptions);
            }
        }
        catch
        {
            SecureStorage.Default.Remove(SensitiveSessionPreferenceKey);
        }

        var legacySession = LoadLegacySensitivePreferences();
        return legacySession.HasAnyValue ? legacySession : null;
    }

    private static async Task SaveSensitiveCachedSessionAsync(MobileSession session)
    {
        try
        {
            var sensitiveSession = new CachedSensitiveSession(
                NormalizeSensitiveValue(session.Email),
                NormalizeSensitiveValue(session.DisplayName),
                NormalizeSensitiveValue(session.ProfileImageUrl),
                NormalizeSensitiveValue(session.FirstName),
                NormalizeSensitiveValue(session.LastName),
                NormalizeSensitiveValue(session.MobileNumber));

            await SecureStorage.Default.SetAsync(
                SensitiveSessionPreferenceKey,
                JsonSerializer.Serialize(sensitiveSession, SessionJsonOptions));
        }
        catch
        {
            // Cached profile details are a convenience only; live session refresh remains authoritative.
        }
    }

    private static CachedSensitiveSession LoadLegacySensitivePreferences() =>
        new(
            GetLegacyNullablePreference(EmailPreferenceKey),
            GetLegacyNullablePreference(DisplayNamePreferenceKey),
            GetLegacyNullablePreference(ProfileImageUrlPreferenceKey),
            GetLegacyNullablePreference(FirstNamePreferenceKey),
            GetLegacyNullablePreference(LastNamePreferenceKey),
            GetLegacyNullablePreference(MobileNumberPreferenceKey));

    private static string? GetLegacyNullablePreference(string key)
    {
        var value = Preferences.Get(key, string.Empty);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void RemoveLegacySensitivePreferences()
    {
        Preferences.Remove(EmailPreferenceKey);
        Preferences.Remove(DisplayNamePreferenceKey);
        Preferences.Remove(ProfileImageUrlPreferenceKey);
        Preferences.Remove(FirstNamePreferenceKey);
        Preferences.Remove(LastNamePreferenceKey);
        Preferences.Remove(MobileNumberPreferenceKey);
    }

    private static string? NormalizeSensitiveValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record CachedSensitiveSession(
        string? Email,
        string? DisplayName,
        string? ProfileImageUrl,
        string? FirstName,
        string? LastName,
        string? MobileNumber)
    {
        public bool HasAnyValue =>
            !string.IsNullOrWhiteSpace(Email) ||
            !string.IsNullOrWhiteSpace(DisplayName) ||
            !string.IsNullOrWhiteSpace(ProfileImageUrl) ||
            !string.IsNullOrWhiteSpace(FirstName) ||
            !string.IsNullOrWhiteSpace(LastName) ||
            !string.IsNullOrWhiteSpace(MobileNumber);
    }
}

public sealed record MobileAudioDownloadProgress(long BytesReceived, long? TotalBytes, double? Percent);

public sealed class MobileApiClient
{
#if DEBUG
    public const string GoogleCallbackUrl = "schinkstories://auth/google";
    private const string GoogleStartPath = "/api/mobile/auth/google/start?callback=custom-scheme";
#else
    public const string GoogleCallbackUrl = "https://www.schink.co.za/mobile-auth/google/callback";
    private const string GoogleStartPath = "/api/mobile/auth/google/start";
#endif

    private const string AuthCookieStorageKeyPrefix = "mobile_auth_cookies";
    private const string MobileAppHeaderName = "X-Schink-Mobile-App";
    private const string MobileAppHeaderValue = "1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultLuisterCacheMaxAge = TimeSpan.FromHours(12);
    private static readonly TimeSpan DefaultCharactersCacheMaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan DefaultStoryDetailCacheMaxAge = TimeSpan.FromHours(12);
    private static readonly TimeSpan DefaultNotificationCacheMaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan TransientGetRetryDelay = TimeSpan.FromMilliseconds(300);

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private readonly MobileAppSettings _settings;
    private readonly SessionState _sessionState;
    private readonly MobileAnalyticsService _analytics;
    private readonly SemaphoreSlim _authCookieStorageLock = new(1, 1);
    private readonly SemaphoreSlim _luisterCacheLock = new(1, 1);
    private readonly SemaphoreSlim _charactersCacheLock = new(1, 1);
    private readonly SemaphoreSlim _storyDetailCacheLock = new(1, 1);
    private readonly SemaphoreSlim _notificationCacheLock = new(1, 1);
    private readonly SemaphoreSlim _offlineStoryListenQueueLock = new(1, 1);
    private readonly SemaphoreSlim _offlineStoryListenFlushLock = new(1, 1);
    private MobileLuisterCacheEntry? _memoryLuisterCache;
    private MobileCharactersCacheEntry? _memoryCharactersCache;
    private MobileNotificationCacheEntry? _memoryNotificationCache;
    private readonly Dictionary<string, MobileStoryDetailCacheEntry> _memoryStoryDetailCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _authCookiesLoaded;
    private string? _lastPersistedAuthCookies;

    public event Action<int>? NewNotificationsAvailable;

    public MobileApiClient(
        MobileAppSettings settings,
        SessionState sessionState,
        MobileAnalyticsService analytics)
    {
        _settings = settings;
        _sessionState = sessionState;
        _analytics = analytics;

        _cookieContainer = new CookieContainer();

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public string BaseUrl
    {
        get => _settings.BaseUrl;
        set => _settings.BaseUrl = value;
    }

    public async Task<MobileSession> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetAsync<MobileSession>("/api/mobile/session", cancellationToken)
            ?? new MobileSession(false, null, null, null, null, null, null, false, Array.Empty<string>(), string.Empty, string.Empty, string.Empty);
        _sessionState.Update(session);
        return session;
    }

    public Task<MobileHomeResponse?> GetHomeAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MobileHomeResponse>("/api/mobile/home", cancellationToken);

    public Task<MobileStoryCollectionResponse?> GetGratisAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MobileStoryCollectionResponse>("/api/mobile/gratis", cancellationToken);

    public Task<MobileLuisterResponse?> GetLuisterAsync(CancellationToken cancellationToken = default) =>
        GetAndCacheLuisterAsync(cancellationToken);

    public Task<MobileCharactersResponse?> GetCharactersAsync(CancellationToken cancellationToken = default) =>
        GetAndCacheCharactersAsync(cancellationToken);

    public Task<MobileCharactersResponse?> GetCachedCharactersAsync(CancellationToken cancellationToken = default) =>
        GetCachedCharactersAsync(DefaultCharactersCacheMaxAge, cancellationToken);

    public async Task<MobileCharactersResponse?> GetCachedCharactersAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildCharactersCacheKey();
        if (_memoryCharactersCache is { Response: { } memoryResponse } &&
            string.Equals(_memoryCharactersCache.CacheKey, cacheKey, StringComparison.Ordinal) &&
            DateTimeOffset.UtcNow - _memoryCharactersCache.CachedAtUtc <= maxAge)
        {
            return memoryResponse;
        }

        var cachePath = BuildCharactersCachePath();
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(cachePath);
            var cacheEntry = await JsonSerializer.DeserializeAsync<MobileCharactersCacheEntry>(
                stream,
                JsonOptions,
                cancellationToken);
            if (cacheEntry?.Response is null ||
                !string.Equals(cacheEntry.CacheKey, cacheKey, StringComparison.Ordinal) ||
                DateTimeOffset.UtcNow - cacheEntry.CachedAtUtc > maxAge)
            {
                return null;
            }

            _memoryCharactersCache = cacheEntry;
            return cacheEntry.Response;
        }
        catch
        {
            TryDeleteFile(cachePath);
            return null;
        }
    }

    public async Task WarmCharactersCacheAsync(CancellationToken cancellationToken = default)
    {
        if (await GetCachedCharactersAsync(TimeSpan.FromMinutes(10), cancellationToken) is not null)
        {
            return;
        }

        try
        {
            await GetCharactersAsync(cancellationToken);
        }
        catch
        {
            // Prefetching is best-effort and must not affect the page already on screen.
        }
    }

    public async Task TrackCharacterProfileListenAsync(
        string characterSlug,
        string streamSlug,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await PostAsync<TrackingResponse>(
                $"/api/mobile/karakters/{Uri.EscapeDataString(characterSlug)}/listen",
                new { streamSlug },
                cancellationToken);
            if (result?.NewNotificationsCreated > 0)
            {
                NewNotificationsAvailable?.Invoke(result.NewNotificationsCreated);
            }
        }
        catch
        {
            // Character listen tracking must never interrupt voice playback.
        }
    }

    public Task<MobileLuisterResponse?> GetCachedLuisterAsync(CancellationToken cancellationToken = default) =>
        GetCachedLuisterAsync(DefaultLuisterCacheMaxAge, cancellationToken);

    public async Task<MobileLuisterResponse?> GetCachedLuisterAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        if (_memoryLuisterCache is { Response: { } memoryResponse } &&
            DateTimeOffset.UtcNow - _memoryLuisterCache.CachedAtUtc <= maxAge)
        {
            return memoryResponse;
        }

        var cachePath = BuildLuisterCachePath();
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(cachePath);
            var cacheEntry = await JsonSerializer.DeserializeAsync<MobileLuisterCacheEntry>(
                stream,
                JsonOptions,
                cancellationToken);
            if (cacheEntry?.Response is null ||
                DateTimeOffset.UtcNow - cacheEntry.CachedAtUtc > maxAge)
            {
                return null;
            }

            _memoryLuisterCache = cacheEntry;
            return cacheEntry.Response;
        }
        catch
        {
            TryDeleteFile(cachePath);
            return null;
        }
    }

    public Task<MobileAboutResponse?> GetAboutAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MobileAboutResponse>("/api/mobile/meer-oor-ons", cancellationToken);

    public Task<MobileStoryDetailResponse?> GetStoryAsync(string slug, string source, CancellationToken cancellationToken = default) =>
        GetAndCacheStoryAsync(slug, source, cancellationToken);

    public Task<MobileStoryDetailResponse?> GetCachedStoryAsync(
        string slug,
        string source,
        CancellationToken cancellationToken = default) =>
        GetCachedStoryAsync(slug, source, DefaultStoryDetailCacheMaxAge, cancellationToken);

    public async Task<MobileStoryDetailResponse?> GetCachedStoryAsync(
        string slug,
        string source,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildStoryDetailCacheKey(slug, source);
        if (_memoryStoryDetailCache.TryGetValue(cacheKey, out var memoryEntry) &&
            DateTimeOffset.UtcNow - memoryEntry.CachedAtUtc <= maxAge)
        {
            return memoryEntry.Response;
        }

        var cachePath = BuildStoryDetailCachePath(slug, source);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(cachePath);
            var cacheEntry = await JsonSerializer.DeserializeAsync<MobileStoryDetailCacheEntry>(
                stream,
                JsonOptions,
                cancellationToken);
            if (cacheEntry?.Response is null ||
                DateTimeOffset.UtcNow - cacheEntry.CachedAtUtc > maxAge)
            {
                return null;
            }

            _memoryStoryDetailCache[cacheKey] = cacheEntry;
            return cacheEntry.Response;
        }
        catch
        {
            TryDeleteFile(cachePath);
            _memoryStoryDetailCache.Remove(cacheKey);
            return null;
        }
    }

    public Task<MobileNotificationPage?> GetNotificationsAsync(
        int limit = 10,
        DateTimeOffset? before = null,
        bool history = false,
        CancellationToken cancellationToken = default) =>
        GetAndCacheNotificationsAsync(limit, before, history, cancellationToken);

    public Task<MobileNotificationPage?> GetCachedNotificationsAsync(CancellationToken cancellationToken = default) =>
        GetCachedNotificationsAsync(DefaultNotificationCacheMaxAge, cancellationToken);

    public async Task<MobileNotificationPage?> GetCachedNotificationsAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildNotificationCacheKey();
        if (_memoryNotificationCache is { Response: { } memoryResponse } &&
            string.Equals(_memoryNotificationCache.CacheKey, cacheKey, StringComparison.Ordinal) &&
            DateTimeOffset.UtcNow - _memoryNotificationCache.CachedAtUtc <= maxAge)
        {
            return memoryResponse;
        }

        var cachePath = BuildNotificationCachePath();
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(cachePath);
            var cacheEntry = await JsonSerializer.DeserializeAsync<MobileNotificationCacheEntry>(
                stream,
                JsonOptions,
                cancellationToken);
            if (cacheEntry?.Response is null ||
                !string.Equals(cacheEntry.CacheKey, cacheKey, StringComparison.Ordinal) ||
                DateTimeOffset.UtcNow - cacheEntry.CachedAtUtc > maxAge)
            {
                return null;
            }

            _memoryNotificationCache = cacheEntry;
            return cacheEntry.Response;
        }
        catch
        {
            TryDeleteFile(cachePath);
            if (_memoryNotificationCache?.CacheKey == cacheKey)
            {
                _memoryNotificationCache = null;
            }

            return null;
        }
    }

    public Task SaveNotificationsCacheAsync(
        MobileNotificationPage response,
        CancellationToken cancellationToken = default) =>
        SaveNotificationCacheAsync(response, cancellationToken);

    public Task<MobileNotificationMutationResponse?> MarkAllNotificationsReadAsync(CancellationToken cancellationToken = default) =>
        PostAsync<MobileNotificationMutationResponse>("/api/notifications/read-all", new { }, cancellationToken);

    public Task<MobileNotificationMutationResponse?> MarkNotificationReadAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default) =>
        PostAsync<MobileNotificationMutationResponse>($"/api/notifications/{notificationId:D}/read", new { }, cancellationToken);

    public Task<MobileNotificationMutationResponse?> ClearNotificationsAsync(CancellationToken cancellationToken = default) =>
        PostAsync<MobileNotificationMutationResponse>("/api/notifications/clear", new { }, cancellationToken);

    public Task<MobileNotificationMutationResponse?> ClearNotificationAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default) =>
        PostAsync<MobileNotificationMutationResponse>($"/api/notifications/{notificationId:D}/clear", new { }, cancellationToken);

    public string BuildAbsoluteUrl(string path)
    {
        var normalizedPath = NormalizeIncomingUrl(path);
        if (Uri.TryCreate(normalizedPath, UriKind.Absolute, out var absoluteUri) &&
            IsWebUri(absoluteUri))
        {
            return absoluteUri.ToString();
        }

        normalizedPath = normalizedPath.StartsWith("/", StringComparison.Ordinal) ? normalizedPath : $"/{normalizedPath}";
        return $"{_settings.BaseUrl.TrimEnd('/')}{normalizedPath}";
    }

    public async Task<(bool IsSuccess, string Message)> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var payload = new { email = email.Trim(), password };
        var result = await PostAsync<AuthResponse>("/api/auth/login", payload, cancellationToken);
        await GetSessionAsync(cancellationToken);
        _analytics.TrackEvent("mobile_auth_signed_in", new Dictionary<string, object>
        {
            ["auth_method"] = "email"
        });
        return (true, result?.Message ?? "Welkom terug!");
    }

    public Uri BuildGoogleSignInStartUri() =>
        BuildUri(GoogleStartPath);

    public async Task<(bool IsSuccess, string Message)> CompleteGoogleSignInAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var result = await PostAsync<MobileGoogleAuthCompleteResponse>(
            "/api/mobile/auth/google/complete",
            new { token },
            cancellationToken);

        if (result?.Session is not null)
        {
            _sessionState.Update(result.Session);
        }
        else
        {
            await GetSessionAsync(cancellationToken);
        }

        _analytics.TrackEvent("mobile_auth_signed_in", new Dictionary<string, object>
        {
            ["auth_method"] = "google"
        });
        return (true, result?.Message ?? "Welkom terug!");
    }

    public async Task<(bool IsSuccess, string Message)> SignUpAsync(
        string firstName,
        string lastName,
        string displayName,
        string email,
        string mobileNumber,
        string password,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            firstName = firstName.Trim(),
            lastName = lastName.Trim(),
            displayName = displayName.Trim(),
            email = email.Trim(),
            mobileNumber = mobileNumber.Trim(),
            password
        };

        var result = await PostAsync<AuthResponse>("/api/auth/signup", payload, cancellationToken);
        await GetSessionAsync(cancellationToken);
        _analytics.TrackEvent("mobile_auth_signed_up", new Dictionary<string, object>
        {
            ["auth_method"] = "email"
        });
        return (true, result?.Message ?? "Jou rekening is geskep.");
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await PostAsync<object>("/api/auth/logout", new { }, cancellationToken);
        await ClearPersistedAuthCookiesAsync();
        await GetSessionAsync(cancellationToken);
        _analytics.TrackEvent("mobile_auth_signed_out");
    }

    public async Task<(bool IsSuccess, string Message)> UpdateProfileAsync(
        string firstName,
        string lastName,
        string displayName,
        string mobileNumber,
        CancellationToken cancellationToken = default)
    {
        var result = await PostAsync<MobileProfileUpdateResponse>(
            "/api/mobile/profile",
            new
            {
                firstName = firstName.Trim(),
                lastName = lastName.Trim(),
                displayName = displayName.Trim(),
                mobileNumber = mobileNumber.Trim()
            },
            cancellationToken);

        if (result?.Session is not null)
        {
            _sessionState.Update(result.Session);
        }
        else
        {
            await GetSessionAsync(cancellationToken);
        }

        _analytics.TrackEvent("mobile_profile_updated");
        return (true, result?.Message ?? "Profiel opgedateer.");
    }

    public async Task<bool> SetFavoriteAsync(string slug, string source, bool isFavorite, CancellationToken cancellationToken = default)
    {
        var result = await PostAsync<FavoriteResponse>(
            $"/api/mobile/stories/{Uri.EscapeDataString(slug)}/favorite",
            new { isFavorite, source },
            cancellationToken);
        await GetSessionAsync(cancellationToken);
        _analytics.TrackEvent("mobile_story_favorite_changed", new Dictionary<string, object>
        {
            ["story_slug"] = slug,
            ["story_source"] = source,
            ["is_favorite"] = result?.IsFavorite ?? isFavorite
        });
        return result?.IsFavorite ?? false;
    }

    public async Task TrackStoryViewAsync(string slug, string source, CancellationToken cancellationToken = default)
    {
        try
        {
            await PostAsync<TrackingResponse>(
                $"/api/stories/{Uri.EscapeDataString(slug)}/view",
                new
                {
                    storyPath = BuildMobileStoryPath(slug, source),
                    source,
                    referrerPath = "/mobile"
                },
                cancellationToken);
            _analytics.TrackEvent("mobile_story_viewed", new Dictionary<string, object>
            {
                ["story_slug"] = slug,
                ["story_source"] = source
            });
        }
        catch (Exception ex)
        {
            _analytics.TrackException(ex, "mobile_story_view_tracking_failed", new Dictionary<string, object>
            {
                ["story_slug"] = slug,
                ["story_source"] = source
            });
            // Tracking must not block mobile playback or page rendering.
        }
    }

    public async Task TrackStoryListenAsync(
        string slug,
        string source,
        Guid sessionId,
        string eventType,
        decimal listenedSeconds,
        decimal? positionSeconds,
        decimal? durationSeconds,
        bool isCompleted,
        CancellationToken cancellationToken = default)
    {
        if (listenedSeconds <= 0)
        {
            return;
        }

        try
        {
            var trackingEvent = new QueuedStoryListenEvent(
                slug,
                source,
                sessionId,
                eventType,
                listenedSeconds,
                positionSeconds,
                durationSeconds,
                isCompleted,
                DateTimeOffset.UtcNow);
            await SendStoryListenTrackingAsync(trackingEvent, flushQueuedListensAfterSuccess: true, cancellationToken);
        }
        catch (Exception ex)
        {
            await EnqueueStoryListenAsync(
                new QueuedStoryListenEvent(
                    slug,
                    source,
                    sessionId,
                    eventType,
                    listenedSeconds,
                    positionSeconds,
                    durationSeconds,
                    isCompleted,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            _analytics.TrackException(ex, "mobile_story_listen_tracking_queued", new Dictionary<string, object>
            {
                ["story_slug"] = slug,
                ["story_source"] = source,
                ["listen_event_type"] = eventType,
                ["listened_seconds"] = listenedSeconds,
                ["position_seconds"] = positionSeconds ?? 0,
                ["duration_seconds"] = durationSeconds ?? 0,
                ["is_completed"] = isCompleted
            });
            // Tracking must not block mobile playback or page rendering.
        }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        await EnsureAuthCookiesLoadedAsync(cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            using var response = await SendGetWithTransientRetryAsync(path, cancellationToken);
            await SaveAuthCookiesAsync(cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var result = await ReadJsonResponseAsync<T>(response, path, cancellationToken);
            TrackMobileApiRequest("GET", path, response.StatusCode, startedAt, true);
            _ = FlushQueuedStoryListensAsync();
            return result;
        }
        catch (Exception ex)
        {
            TrackMobileApiRequest("GET", path, null, startedAt, false, ex);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendGetWithTransientRetryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
                AddMobileAppHeaderIfNeeded(request, path);
                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (attempt > 0 || !IsTransientGetStatusCode(response.StatusCode))
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt == 0 && !cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(TransientGetRetryDelay, cancellationToken);
        }
    }

    private static bool IsTransientGetStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private async Task<MobileLuisterResponse?> GetAndCacheLuisterAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync<MobileLuisterResponse>("/api/mobile/luister?compact=true", cancellationToken);
        if (response is not null)
        {
            await SaveLuisterCacheAsync(response, cancellationToken);
        }

        return response;
    }

    private async Task<MobileCharactersResponse?> GetAndCacheCharactersAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync<MobileCharactersResponse>("/api/mobile/karakters", cancellationToken);
        if (response is not null)
        {
            await SaveCharactersCacheAsync(response, cancellationToken);
        }

        return response;
    }

    private async Task<MobileStoryDetailResponse?> GetAndCacheStoryAsync(
        string slug,
        string source,
        CancellationToken cancellationToken)
    {
        var response = await GetAsync<MobileStoryDetailResponse>(
            $"/api/mobile/stories/{Uri.EscapeDataString(slug)}?source={Uri.EscapeDataString(source)}",
            cancellationToken);
        if (response is not null)
        {
            await SaveStoryDetailCacheAsync(slug, source, response, cancellationToken);
        }

        return response;
    }

    private async Task<MobileNotificationPage?> GetAndCacheNotificationsAsync(
        int limit,
        DateTimeOffset? before,
        bool history,
        CancellationToken cancellationToken)
    {
        var response = await GetAsync<MobileNotificationPage>(
            BuildNotificationRequestPath(limit, before, history),
            cancellationToken);
        if (response is not null && before is null && !history)
        {
            await SaveNotificationCacheAsync(response, cancellationToken);
        }

        return response;
    }

    private async Task SaveLuisterCacheAsync(MobileLuisterResponse response, CancellationToken cancellationToken)
    {
        var cacheEntry = new MobileLuisterCacheEntry(DateTimeOffset.UtcNow, response);
        _memoryLuisterCache = cacheEntry;

        var cacheLockAcquired = false;
        try
        {
            await _luisterCacheLock.WaitAsync(cancellationToken);
            cacheLockAcquired = true;
            var cachePath = BuildLuisterCachePath();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
            var temporaryPath = $"{cachePath}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    cacheEntry,
                    JsonOptions,
                    cancellationToken);
            }

            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }

            File.Move(temporaryPath, cachePath);
        }
        catch
        {
            // Luister data caching is an optimization only.
        }
        finally
        {
            if (cacheLockAcquired)
            {
                _luisterCacheLock.Release();
            }
        }
    }

    private async Task SaveCharactersCacheAsync(
        MobileCharactersResponse response,
        CancellationToken cancellationToken)
    {
        var cacheEntry = new MobileCharactersCacheEntry(
            BuildCharactersCacheKey(),
            DateTimeOffset.UtcNow,
            response);
        _memoryCharactersCache = cacheEntry;

        var cacheLockAcquired = false;
        try
        {
            await _charactersCacheLock.WaitAsync(cancellationToken);
            cacheLockAcquired = true;
            var cachePath = BuildCharactersCachePath();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
            var temporaryPath = $"{cachePath}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    cacheEntry,
                    JsonOptions,
                    cancellationToken);
            }

            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }

            File.Move(temporaryPath, cachePath);
        }
        catch
        {
            // Character data caching is an optimization only.
        }
        finally
        {
            if (cacheLockAcquired)
            {
                _charactersCacheLock.Release();
            }
        }
    }

    private async Task SaveStoryDetailCacheAsync(
        string slug,
        string source,
        MobileStoryDetailResponse response,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildStoryDetailCacheKey(slug, source);
        var cacheEntry = new MobileStoryDetailCacheEntry(DateTimeOffset.UtcNow, response);
        _memoryStoryDetailCache[cacheKey] = cacheEntry;

        var cacheLockAcquired = false;
        try
        {
            await _storyDetailCacheLock.WaitAsync(cancellationToken);
            cacheLockAcquired = true;
            var cachePath = BuildStoryDetailCachePath(slug, source);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
            var temporaryPath = $"{cachePath}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    cacheEntry,
                    JsonOptions,
                    cancellationToken);
            }

            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }

            File.Move(temporaryPath, cachePath);
        }
        catch
        {
            // Story detail caching is an optimization only.
        }
        finally
        {
            if (cacheLockAcquired)
            {
                _storyDetailCacheLock.Release();
            }
        }
    }

    private async Task SaveNotificationCacheAsync(
        MobileNotificationPage response,
        CancellationToken cancellationToken)
    {
        var cacheEntry = new MobileNotificationCacheEntry(
            BuildNotificationCacheKey(),
            DateTimeOffset.UtcNow,
            response);
        _memoryNotificationCache = cacheEntry;

        var cacheLockAcquired = false;
        try
        {
            await _notificationCacheLock.WaitAsync(cancellationToken);
            cacheLockAcquired = true;
            var cachePath = BuildNotificationCachePath();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
            var temporaryPath = $"{cachePath}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    cacheEntry,
                    JsonOptions,
                    cancellationToken);
            }

            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
            }

            File.Move(temporaryPath, cachePath);
        }
        catch
        {
            // Notification caching is an optimization only.
        }
        finally
        {
            if (cacheLockAcquired)
            {
                _notificationCacheLock.Release();
            }
        }
    }

    private async Task<T?> PostAsync<T>(
        string path,
        object payload,
        CancellationToken cancellationToken,
        bool flushQueuedListens = true)
    {
        await EnsureAuthCookiesLoadedAsync(cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            AddMobileAppHeaderIfNeeded(request, path);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await SaveAuthCookiesAsync(cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            TrackMobileApiRequest("POST", path, response.StatusCode, startedAt, true);

            if (response.Content.Headers.ContentLength == 0)
            {
                if (flushQueuedListens)
                {
                    _ = FlushQueuedStoryListensAsync();
                }

                return default;
            }

            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            if (flushQueuedListens)
            {
                _ = FlushQueuedStoryListensAsync();
            }

            return result;
        }
        catch (Exception ex)
        {
            TrackMobileApiRequest("POST", path, null, startedAt, false, ex);
            throw;
        }
    }

    private async Task SendStoryListenTrackingAsync(
        QueuedStoryListenEvent trackingEvent,
        bool flushQueuedListensAfterSuccess,
        CancellationToken cancellationToken)
    {
        var result = await PostAsync<TrackingResponse>(
            $"/api/stories/{Uri.EscapeDataString(trackingEvent.Slug)}/listen",
            new
            {
                storyPath = BuildMobileStoryPath(trackingEvent.Slug, trackingEvent.Source),
                source = trackingEvent.Source,
                sessionId = trackingEvent.SessionId.ToString(),
                eventType = trackingEvent.EventType,
                listenedSeconds = trackingEvent.ListenedSeconds,
                positionSeconds = trackingEvent.PositionSeconds,
                durationSeconds = trackingEvent.DurationSeconds,
                isCompleted = trackingEvent.IsCompleted
            },
            cancellationToken,
            flushQueuedListens: false);
        if (result?.NewNotificationsCreated > 0)
        {
            NewNotificationsAvailable?.Invoke(result.NewNotificationsCreated);
        }

        _analytics.TrackEvent("mobile_story_listened", new Dictionary<string, object>
        {
            ["story_slug"] = trackingEvent.Slug,
            ["story_source"] = trackingEvent.Source,
            ["listen_event_type"] = trackingEvent.EventType,
            ["listened_seconds"] = trackingEvent.ListenedSeconds,
            ["position_seconds"] = trackingEvent.PositionSeconds ?? 0,
            ["duration_seconds"] = trackingEvent.DurationSeconds ?? 0,
            ["is_completed"] = trackingEvent.IsCompleted,
            ["new_notifications_created"] = result?.NewNotificationsCreated ?? 0
        });

        if (flushQueuedListensAfterSuccess)
        {
            _ = FlushQueuedStoryListensAsync();
        }
    }

    private async Task EnqueueStoryListenAsync(
        QueuedStoryListenEvent trackingEvent,
        CancellationToken cancellationToken)
    {
        var queueLockAcquired = false;
        try
        {
            await _offlineStoryListenQueueLock.WaitAsync(cancellationToken);
            queueLockAcquired = true;
            var queuedEvents = (await LoadQueuedStoryListensUnsafeAsync(cancellationToken)).ToList();
            queuedEvents.Add(trackingEvent);
            await SaveQueuedStoryListensUnsafeAsync(
                queuedEvents
                    .OrderBy(item => item.QueuedAtUtc)
                    .TakeLast(300)
                    .ToArray(),
                cancellationToken);
            _analytics.TrackEvent("mobile_story_listen_queued", new Dictionary<string, object>
            {
                ["story_slug"] = trackingEvent.Slug,
                ["story_source"] = trackingEvent.Source,
                ["listen_event_type"] = trackingEvent.EventType,
                ["queued_count"] = queuedEvents.Count
            });
        }
        catch
        {
            // Offline tracking is best-effort and must not interrupt playback.
        }
        finally
        {
            if (queueLockAcquired)
            {
                _offlineStoryListenQueueLock.Release();
            }
        }
    }

    public async Task FlushQueuedStoryListensAsync(CancellationToken cancellationToken = default)
    {
        if (!await _offlineStoryListenFlushLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            IReadOnlyList<QueuedStoryListenEvent> queuedEvents;
            await _offlineStoryListenQueueLock.WaitAsync(cancellationToken);
            try
            {
                queuedEvents = await LoadQueuedStoryListensUnsafeAsync(cancellationToken);
            }
            finally
            {
                _offlineStoryListenQueueLock.Release();
            }

            if (queuedEvents.Count == 0)
            {
                return;
            }

            var initialQueuedCount = queuedEvents.Count;
            var remainingEvents = new List<QueuedStoryListenEvent>();
            for (var index = 0; index < queuedEvents.Count; index++)
            {
                var queuedEvent = queuedEvents[index];
                try
                {
                    await SendStoryListenTrackingAsync(
                        queuedEvent,
                        flushQueuedListensAfterSuccess: false,
                        cancellationToken);
                }
                catch
                {
                    remainingEvents.AddRange(queuedEvents.Skip(index));
                    break;
                }
            }

            await _offlineStoryListenQueueLock.WaitAsync(cancellationToken);
            try
            {
                await SaveQueuedStoryListensUnsafeAsync(remainingEvents, cancellationToken);
            }
            finally
            {
                _offlineStoryListenQueueLock.Release();
            }

            _analytics.TrackEvent("mobile_story_listen_queue_flushed", new Dictionary<string, object>
            {
                ["initial_queued_count"] = initialQueuedCount,
                ["flushed_count"] = initialQueuedCount - remainingEvents.Count,
                ["remaining_count"] = remainingEvents.Count
            });
        }
        catch (Exception ex)
        {
            _analytics.TrackException(ex, "mobile_story_listen_queue_flush_failed");
            // Syncing queued tracking should never block app startup or playback.
        }
        finally
        {
            _offlineStoryListenFlushLock.Release();
        }
    }

    private static async Task<IReadOnlyList<QueuedStoryListenEvent>> LoadQueuedStoryListensUnsafeAsync(
        CancellationToken cancellationToken)
    {
        var queuePath = BuildOfflineStoryListenQueuePath();
        if (!File.Exists(queuePath))
        {
            return Array.Empty<QueuedStoryListenEvent>();
        }

        try
        {
            await using var stream = File.OpenRead(queuePath);
            return await JsonSerializer.DeserializeAsync<QueuedStoryListenEvent[]>(stream, JsonOptions, cancellationToken)
                   ?? Array.Empty<QueuedStoryListenEvent>();
        }
        catch
        {
            TryDeleteFile(queuePath);
            return Array.Empty<QueuedStoryListenEvent>();
        }
    }

    private static async Task SaveQueuedStoryListensUnsafeAsync(
        IReadOnlyList<QueuedStoryListenEvent> queuedEvents,
        CancellationToken cancellationToken)
    {
        var queuePath = BuildOfflineStoryListenQueuePath();
        if (queuedEvents.Count == 0)
        {
            TryDeleteFile(queuePath);
            return;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(queuePath)!);
        var temporaryPath = $"{queuePath}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, queuedEvents, JsonOptions, cancellationToken);
        }

        if (File.Exists(queuePath))
        {
            File.Delete(queuePath);
        }

        File.Move(temporaryPath, queuePath);
    }

    private static string BuildNotificationRequestPath(int limit, DateTimeOffset? before, bool history)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 50);
        var queryParts = new List<string>
        {
            $"limit={normalizedLimit.ToString(CultureInfo.InvariantCulture)}"
        };

        if (before is { } beforeValue)
        {
            queryParts.Add($"before={Uri.EscapeDataString(beforeValue.ToString("O", CultureInfo.InvariantCulture))}");
        }

        if (history)
        {
            queryParts.Add("history=true");
        }

        return $"/api/notifications?{string.Join("&", queryParts)}";
    }

    public string BuildImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmedUrl = NormalizeIncomingUrl(url.Trim());
        if (TryExtractImageProxySource(trimmedUrl, out var proxiedImageUrl))
        {
            return proxiedImageUrl;
        }

        if (Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var parsed) && IsWebUri(parsed))
        {
            return parsed.ToString();
        }

        return BuildAbsoluteUrl(trimmedUrl);
    }

    public ImageSource BuildCachedImageSource(string? url, string? fallbackFile = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.IsNullOrWhiteSpace(fallbackFile)
                ? ImageSource.FromFile(string.Empty)
                : ImageSource.FromFile(fallbackFile);
        }

        var normalizedUrl = NormalizeIncomingImageUrl(url);
        if (IsBundledImageName(normalizedUrl))
        {
            return ImageSource.FromFile(normalizedUrl);
        }

        var imageUrl = BuildAbsoluteImageUrl(normalizedUrl);
        if (TryGetCachedImagePath(imageUrl, out var cachedPath))
        {
            return ImageSource.FromFile(cachedPath);
        }

        return Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri) && IsWebUri(imageUri)
            ? new UriImageSource
            {
                Uri = imageUri,
                CachingEnabled = true,
                CacheValidity = TimeSpan.FromDays(30)
            }
            : ImageSource.FromFile(string.IsNullOrWhiteSpace(fallbackFile) ? imageUrl : fallbackFile);
    }

    public async Task CacheImagesAsync(
        IEnumerable<string?> urls,
        CancellationToken cancellationToken = default,
        int maxImages = 80,
        int maxDegreeOfParallelism = 4)
    {
        maxImages = Math.Max(1, maxImages);
        maxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism);

        var imageUrls = urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(NormalizeIncomingImageUrl)
            .Where(url => !IsBundledImageName(url))
            .Select(BuildAbsoluteImageUrl)
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsWebUri(uri))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxImages)
            .ToArray();

        await Parallel.ForEachAsync(
            imageUrls,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (imageUrl, token) =>
            {
                try
                {
                    await CacheImageAsync(imageUrl, token);
                }
                catch
                {
                    // Image cache warmup should never block the Luister page.
                }
            });
    }

    public async Task<string> PrepareAudioPlaybackSourceAsync(
        string? audioUrl,
        string slug,
        string source,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            throw new InvalidOperationException("Geen audio URL is beskikbaar nie.");
        }

        var playableUrl = BuildAbsoluteUrl(audioUrl);
        if (!ShouldDownloadAudioForPlayback(playableUrl))
        {
            return playableUrl;
        }

        return await DownloadAudioForPlaybackAsync(playableUrl, slug, source, cancellationToken);
    }

    public async Task<string> DownloadAudioForPlaybackAsync(
        string audioUrl,
        string slug,
        string source,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthCookiesLoadedAsync(cancellationToken);

        var playableUrl = BuildAbsoluteUrl(audioUrl);
        var cacheDirectory = System.IO.Path.Combine(FileSystem.CacheDirectory, "story-audio");
        Directory.CreateDirectory(cacheDirectory);

        var cacheKey = BuildAudioCacheKey(playableUrl);
        var extension = ResolveAudioExtensionFromUrl(playableUrl);
        var fileName = $"{ToSafeFileSegment(source)}-{ToSafeFileSegment(slug)}-{cacheKey}{extension}";
        var cachePath = System.IO.Path.Combine(cacheDirectory, fileName);
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
        {
            _analytics.TrackEvent("mobile_audio_playback_cache_hit", new Dictionary<string, object>
            {
                ["story_slug"] = slug,
                ["story_source"] = source
            });
            return new Uri(cachePath).AbsoluteUri;
        }

        var temporaryPath = $"{cachePath}.tmp";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(playableUrl, UriKind.Absolute));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await SaveAuthCookiesAsync(cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = File.Create(temporaryPath))
        {
            await sourceStream.CopyToAsync(fileStream, cancellationToken);
        }

        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }

        File.Move(temporaryPath, cachePath);
        _analytics.TrackEvent("mobile_audio_playback_cached", new Dictionary<string, object>
        {
            ["story_slug"] = slug,
            ["story_source"] = source,
            ["file_size_bytes"] = new FileInfo(cachePath).Length
        });
        return new Uri(cachePath).AbsoluteUri;
    }

    public async Task DownloadAudioToFileAsync(
        string audioUrl,
        string destinationPath,
        IProgress<MobileAudioDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthCookiesLoadedAsync(cancellationToken);

        var playableUrl = BuildAbsoluteUrl(audioUrl);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(playableUrl, UriKind.Absolute));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await SaveAuthCookiesAsync(cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var totalBytes = response.Content.Headers.ContentLength;
        long bytesReceived = 0;
        var buffer = new byte[81920];

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(destinationPath);
        while (true)
        {
            var read = await sourceStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            bytesReceived += read;
            progress?.Report(new MobileAudioDownloadProgress(
                bytesReceived,
                totalBytes,
                totalBytes is > 0 ? Math.Clamp(bytesReceived / (double)totalBytes.Value, 0, 1) : null));
        }

        _analytics.TrackEvent("mobile_audio_file_downloaded", new Dictionary<string, object>
        {
            ["bytes_received"] = bytesReceived,
            ["total_bytes"] = totalBytes ?? 0
        });
    }

    private async Task CacheImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        await EnsureAuthCookiesLoadedAsync(cancellationToken);

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri) || !IsWebUri(imageUri))
        {
            return;
        }

        var cachePath = BuildImageCachePath(imageUrl);
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
        {
            return;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
        var temporaryPath = $"{cachePath}.tmp";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, imageUri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await SaveAuthCookiesAsync(cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = File.Create(temporaryPath))
        {
            await sourceStream.CopyToAsync(fileStream, cancellationToken);
        }

        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }

        File.Move(temporaryPath, cachePath);
    }

    private Uri BuildUri(string path) => new($"{_settings.BaseUrl.TrimEnd('/')}{path}", UriKind.Absolute);

    private static async Task<T?> ReadJsonResponseAsync<T>(
        HttpResponseMessage response,
        string path,
        CancellationToken cancellationToken)
    {
        if (IsMobileApiPath(path) && IsHtmlResponse(response))
        {
            throw new InvalidOperationException("Die app se Karakters-data is nog nie op die webbediener beskikbaar nie. Probeer weer nadat die webwerf opgedateer is.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (IsMobileApiPath(path) && LooksLikeHtml(body))
        {
            throw new InvalidOperationException("Die app se Karakters-data is nog nie op die webbediener beskikbaar nie. Probeer weer nadat die webwerf opgedateer is.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Kon nie die app-data lees nie. Probeer asseblief weer.", ex);
        }
    }

    private static void AddMobileAppHeaderIfNeeded(HttpRequestMessage request, string path)
    {
        var requestPath = path;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            requestPath = uri.AbsolutePath;
        }

        if (IsMobileApiPath(requestPath))
        {
            request.Headers.TryAddWithoutValidation(MobileAppHeaderName, MobileAppHeaderValue);
        }
    }

    private static bool IsMobileApiPath(string path)
    {
        var requestPath = path;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            requestPath = uri.AbsolutePath;
        }

        return requestPath.StartsWith("/api/mobile/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtmlResponse(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHtml(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var trimmed = body.TrimStart();
        return trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private void TrackMobileApiRequest(
        string method,
        string path,
        HttpStatusCode? statusCode,
        DateTimeOffset startedAt,
        bool isSuccess,
        Exception? exception = null)
    {
        var properties = new Dictionary<string, object>
        {
            ["method"] = method,
            ["path"] = NormalizeAnalyticsPath(path),
            ["status_code"] = statusCode.HasValue ? (int)statusCode.Value : 0,
            ["duration_ms"] = Math.Max(0, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds),
            ["success"] = isSuccess
        };

        if (exception is not null)
        {
            properties["exception_type"] = exception.GetType().Name;
        }

        _analytics.TrackEvent("mobile_api_request", properties);
    }

    private static string NormalizeAnalyticsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? path[..queryIndex] : path;
    }

    private async Task EnsureAuthCookiesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_authCookiesLoaded)
        {
            return;
        }

        await _authCookieStorageLock.WaitAsync(cancellationToken);
        try
        {
            if (_authCookiesLoaded)
            {
                return;
            }

            var serializedCookies = await SecureStorage.Default.GetAsync(BuildAuthCookieStorageKey());
            if (!string.IsNullOrWhiteSpace(serializedCookies))
            {
                RestoreAuthCookies(serializedCookies);
            }

            _lastPersistedAuthCookies = serializedCookies;

            _authCookiesLoaded = true;
        }
        catch
        {
            _authCookiesLoaded = true;
        }
        finally
        {
            _authCookieStorageLock.Release();
        }
    }

    private async Task SaveAuthCookiesAsync(CancellationToken cancellationToken)
    {
        await _authCookieStorageLock.WaitAsync(cancellationToken);
        try
        {
            var cookies = _cookieContainer
                .GetCookies(GetBaseUri())
                .Cast<Cookie>()
                .Where(cookie => !cookie.Expired && !string.IsNullOrWhiteSpace(cookie.Name))
                .Select(PersistedAuthCookie.FromCookie)
                .ToArray();

            if (cookies.Length == 0)
            {
                if (!string.IsNullOrEmpty(_lastPersistedAuthCookies))
                {
                    SecureStorage.Default.Remove(BuildAuthCookieStorageKey());
                    _lastPersistedAuthCookies = null;
                }
                return;
            }

            var serializedCookies = JsonSerializer.Serialize(cookies, JsonOptions);
            if (string.Equals(serializedCookies, _lastPersistedAuthCookies, StringComparison.Ordinal))
            {
                return;
            }

            await SecureStorage.Default.SetAsync(BuildAuthCookieStorageKey(), serializedCookies);
            _lastPersistedAuthCookies = serializedCookies;
        }
        catch
        {
            // Secure cookie persistence is a convenience for app updates; failed storage must not break API calls.
        }
        finally
        {
            _authCookieStorageLock.Release();
        }
    }

    private async Task ClearPersistedAuthCookiesAsync()
    {
        await _authCookieStorageLock.WaitAsync();
        try
        {
            SecureStorage.Default.Remove(BuildAuthCookieStorageKey());
            _lastPersistedAuthCookies = null;
        }
        catch
        {
        }
        finally
        {
            _authCookieStorageLock.Release();
        }
    }

    private void RestoreAuthCookies(string serializedCookies)
    {
        PersistedAuthCookie[]? cookies;
        try
        {
            cookies = JsonSerializer.Deserialize<PersistedAuthCookie[]>(serializedCookies, JsonOptions);
        }
        catch (JsonException)
        {
            SecureStorage.Default.Remove(BuildAuthCookieStorageKey());
            return;
        }

        if (cookies is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var persistedCookie in cookies)
        {
            if (persistedCookie.ExpiresUtc is { } expiresUtc && expiresUtc <= now)
            {
                continue;
            }

            try
            {
                var cookie = persistedCookie.ToCookie(GetBaseUri());
                _cookieContainer.Add(GetBaseUri(), cookie);
            }
            catch
            {
            }
        }
    }

    private Uri GetBaseUri() => new(_settings.BaseUrl.TrimEnd('/'), UriKind.Absolute);

    private string BuildAuthCookieStorageKey()
    {
        var baseUri = GetBaseUri();
        return $"{AuthCookieStorageKeyPrefix}_{baseUri.Host.ToLowerInvariant()}";
    }

    private static string BuildMobileStoryPath(string slug, string source) =>
        $"/mobile/{Uri.EscapeDataString(source)}/{Uri.EscapeDataString(slug)}";

    private static string NormalizeIncomingUrl(string url)
    {
        var trimmedUrl = url.Trim();
        if (trimmedUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return $"https:{trimmedUrl}";
        }

        if (Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var parsed) &&
            string.Equals(parsed.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            var path = Uri.UnescapeDataString(parsed.AbsolutePath);
            return $"{path}{parsed.Query}{parsed.Fragment}";
        }

        if (trimmedUrl.StartsWith("/media/image%3F", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UnescapeDataString(trimmedUrl);
        }

        return trimmedUrl;
    }

    private static bool TryExtractImageProxySource(string url, out string imageUrl)
    {
        imageUrl = string.Empty;
        string path;
        string query;

        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && IsWebUri(parsed))
        {
            path = parsed.AbsolutePath;
            query = parsed.Query;
        }
        else
        {
            var queryStart = url.IndexOf('?', StringComparison.Ordinal);
            if (queryStart < 0)
            {
                return false;
            }

            path = url[..queryStart];
            query = url[queryStart..];
        }

        if (!string.Equals(path.TrimEnd('/'), "/media/image", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var key = separator >= 0 ? pair[..separator] : pair;
            if (!string.Equals(Uri.UnescapeDataString(key.Replace('+', ' ')), "src", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            var candidate = Uri.UnescapeDataString(value.Replace('+', ' '));
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var sourceUri) && IsWebUri(sourceUri))
            {
                imageUrl = sourceUri.ToString();
                return true;
            }
        }

        return false;
    }

    private string NormalizeIncomingImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        return NormalizeIncomingUrl(url.Trim());
    }

    private string BuildAbsoluteImageUrl(string url) => BuildImageUrl(url);

    private static bool IsWebUri(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool IsBundledImageName(string imageUrl) =>
        !imageUrl.StartsWith("/", StringComparison.Ordinal) &&
        !imageUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
        !Uri.TryCreate(imageUrl, UriKind.Absolute, out _);

    private static bool TryGetCachedImagePath(string imageUrl, out string cachePath)
    {
        cachePath = BuildImageCachePath(imageUrl);
        return File.Exists(cachePath) && new FileInfo(cachePath).Length > 0;
    }

    private static string BuildImageCachePath(string imageUrl)
    {
        var cacheDirectory = System.IO.Path.Combine(FileSystem.CacheDirectory, "story-images");
        var cacheKey = BuildStableCacheKey(imageUrl);
        var extension = ResolveImageExtensionFromUrl(imageUrl);
        return System.IO.Path.Combine(cacheDirectory, $"{cacheKey}{extension}");
    }

    private string BuildLuisterCachePath()
    {
        var cacheDirectory = System.IO.Path.Combine(FileSystem.CacheDirectory, "story-data");
        var cacheKey = BuildStableCacheKey(_settings.BaseUrl);
        return System.IO.Path.Combine(cacheDirectory, $"luister-{cacheKey}.json");
    }

    private string BuildCharactersCachePath()
    {
        var cacheDirectory = System.IO.Path.Combine(FileSystem.CacheDirectory, "story-data");
        return System.IO.Path.Combine(cacheDirectory, $"characters-{BuildStableCacheKey(BuildCharactersCacheKey())}.json");
    }

    private string BuildCharactersCacheKey()
    {
        var accountKey = _sessionState.Current.IsSignedIn
            ? _sessionState.Current.Email?.Trim().ToLowerInvariant() ?? "signed-in"
            : "signed-out";
        return $"{_settings.BaseUrl}|{accountKey}";
    }

    private string BuildStoryDetailCachePath(string slug, string source)
    {
        var cacheDirectory = System.IO.Path.Combine(FileSystem.CacheDirectory, "story-data");
        return System.IO.Path.Combine(cacheDirectory, $"story-{BuildStoryDetailCacheKey(slug, source)}.json");
    }

    private string BuildStoryDetailCacheKey(string slug, string source) =>
        BuildStableCacheKey($"{_settings.BaseUrl}|{source}|{slug}");

    private string BuildNotificationCachePath()
    {
        var cacheDirectory = System.IO.Path.Combine(FileSystem.CacheDirectory, "notification-data");
        return System.IO.Path.Combine(cacheDirectory, $"notifications-{BuildNotificationCacheKey()}.json");
    }

    private string BuildNotificationCacheKey()
    {
        var email = _sessionState.Current.Email?.Trim().ToLowerInvariant();
        return BuildStableCacheKey($"{_settings.BaseUrl}|{email}");
    }

    private static string BuildOfflineStoryListenQueuePath()
    {
        var queueDirectory = System.IO.Path.Combine(FileSystem.AppDataDirectory, "offline-tracking");
        return System.IO.Path.Combine(queueDirectory, "story-listens.json");
    }

    private static bool ShouldDownloadAudioForPlayback(string audioUrl)
    {
        if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri))
        {
            return true;
        }

        return string.Equals(uri.Host, "www.schink.co.za", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.StartsWith("/media/audio/", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAudioCacheKey(string audioUrl)
    {
        var stableUrl = audioUrl;
        if (Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri))
        {
            stableUrl = uri.GetLeftPart(UriPartial.Path);
        }

        return BuildStableCacheKey(stableUrl)[..12];
    }

    private static string BuildStableCacheKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string ResolveImageExtensionFromUrl(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            var extension = System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")
            {
                return extension;
            }
        }

        return ".jpg";
    }

    private static string ResolveAudioExtensionFromUrl(string audioUrl)
    {
        if (Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri))
        {
            var extension = System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".mp3" or ".mpeg" or ".m4a" or ".wav" or ".ogg")
            {
                return extension == ".mpeg" ? ".mp3" : extension;
            }
        }

        return ".mp3";
    }

    private static string ToSafeFileSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_'
                ? char.ToLowerInvariant(character)
                : '-');
        }

        var segment = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(segment) ? "audio" : segment;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
            ? $"Versoek het misluk met status {(int)response.StatusCode}."
            : ExtractErrorMessage(body));
    }

    private static string ExtractErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                var message = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }
        catch (JsonException)
        {
        }

        return body;
    }

    private sealed record FavoriteResponse(bool IsFavorite);
    private sealed record TrackingResponse(bool Tracked, int NewNotificationsCreated = 0);
    private sealed record MobileLuisterCacheEntry(DateTimeOffset CachedAtUtc, MobileLuisterResponse Response);

    private sealed record MobileCharactersCacheEntry(
        string CacheKey,
        DateTimeOffset CachedAtUtc,
        MobileCharactersResponse Response);

    private sealed record MobileStoryDetailCacheEntry(DateTimeOffset CachedAtUtc, MobileStoryDetailResponse Response);

    private sealed record MobileNotificationCacheEntry(
        string CacheKey,
        DateTimeOffset CachedAtUtc,
        MobileNotificationPage Response);

    private sealed record QueuedStoryListenEvent(
        string Slug,
        string Source,
        Guid SessionId,
        string EventType,
        decimal ListenedSeconds,
        decimal? PositionSeconds,
        decimal? DurationSeconds,
        bool IsCompleted,
        DateTimeOffset QueuedAtUtc);
    private sealed record PersistedAuthCookie(
        string Name,
        string Value,
        string Domain,
        string Path,
        bool Secure,
        bool HttpOnly,
        DateTimeOffset? ExpiresUtc)
    {
        public static PersistedAuthCookie FromCookie(Cookie cookie) =>
            new(
                cookie.Name,
                cookie.Value,
                string.IsNullOrWhiteSpace(cookie.Domain) ? string.Empty : cookie.Domain,
                string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                cookie.Secure,
                cookie.HttpOnly,
                cookie.Expires == DateTime.MinValue ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime()));

        public Cookie ToCookie(Uri baseUri)
        {
            var cookie = new Cookie(
                Name,
                Value,
                string.IsNullOrWhiteSpace(Path) ? "/" : Path,
                string.IsNullOrWhiteSpace(Domain) ? baseUri.Host : Domain)
            {
                Secure = Secure,
                HttpOnly = HttpOnly
            };

            if (ExpiresUtc is { } expiresUtc)
            {
                cookie.Expires = expiresUtc.UtcDateTime;
            }

            return cookie;
        }
    }
}
