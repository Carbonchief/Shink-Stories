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
        return new MobileSession(
            IsSignedIn: isSignedIn,
            Email: GetNullablePreference(EmailPreferenceKey),
            DisplayName: GetNullablePreference(DisplayNamePreferenceKey),
            ProfileImageUrl: GetNullablePreference(ProfileImageUrlPreferenceKey),
            FirstName: GetNullablePreference(FirstNamePreferenceKey),
            LastName: GetNullablePreference(LastNamePreferenceKey),
            MobileNumber: GetNullablePreference(MobileNumberPreferenceKey),
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
            Preferences.Remove(EmailPreferenceKey);
            Preferences.Remove(DisplayNamePreferenceKey);
            Preferences.Remove(ProfileImageUrlPreferenceKey);
            Preferences.Remove(FirstNamePreferenceKey);
            Preferences.Remove(LastNamePreferenceKey);
            Preferences.Remove(MobileNumberPreferenceKey);
            Preferences.Remove(HasPaidSubscriptionPreferenceKey);
            Preferences.Remove(FavoriteStorySlugsPreferenceKey);
            return;
        }

        SetNullablePreference(EmailPreferenceKey, session.Email);
        SetNullablePreference(DisplayNamePreferenceKey, session.DisplayName);
        SetNullablePreference(ProfileImageUrlPreferenceKey, session.ProfileImageUrl);
        SetNullablePreference(FirstNamePreferenceKey, session.FirstName);
        SetNullablePreference(LastNamePreferenceKey, session.LastName);
        SetNullablePreference(MobileNumberPreferenceKey, session.MobileNumber);
        Preferences.Set(HasPaidSubscriptionPreferenceKey, session.HasPaidSubscription);
        Preferences.Set(
            FavoriteStorySlugsPreferenceKey,
            JsonSerializer.Serialize(session.FavoriteStorySlugs ?? Array.Empty<string>(), SessionJsonOptions));
    }

    private static string? GetNullablePreference(string key)
    {
        var value = Preferences.Get(key, string.Empty);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void SetNullablePreference(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Preferences.Remove(key);
            return;
        }

        Preferences.Set(key, value);
    }
}

public sealed record MobileAudioDownloadProgress(long BytesReceived, long? TotalBytes, double? Percent);

public sealed class MobileApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultLuisterCacheMaxAge = TimeSpan.FromHours(12);

    private readonly HttpClient _httpClient;
    private readonly MobileAppSettings _settings;
    private readonly SessionState _sessionState;

    public event Action<int>? NewNotificationsAvailable;

    public MobileApiClient(MobileAppSettings settings, SessionState sessionState)
    {
        _settings = settings;
        _sessionState = sessionState;

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
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

    public Task<MobileLuisterResponse?> GetCachedLuisterAsync(CancellationToken cancellationToken = default) =>
        GetCachedLuisterAsync(DefaultLuisterCacheMaxAge, cancellationToken);

    public async Task<MobileLuisterResponse?> GetCachedLuisterAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
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
        GetAsync<MobileStoryDetailResponse>(
            $"/api/mobile/stories/{Uri.EscapeDataString(slug)}?source={Uri.EscapeDataString(source)}",
            cancellationToken);

    public Task<MobileNotificationPage?> GetNotificationsAsync(
        int limit = 10,
        DateTimeOffset? before = null,
        bool history = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<MobileNotificationPage>(BuildNotificationRequestPath(limit, before, history), cancellationToken);

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
        return (true, result?.Message ?? "Jou rekening is geskep.");
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await PostAsync<object>("/api/auth/logout", new { }, cancellationToken);
        await GetSessionAsync(cancellationToken);
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

        return (true, result?.Message ?? "Profiel opgedateer.");
    }

    public async Task<bool> SetFavoriteAsync(string slug, string source, bool isFavorite, CancellationToken cancellationToken = default)
    {
        var result = await PostAsync<FavoriteResponse>(
            $"/api/mobile/stories/{Uri.EscapeDataString(slug)}/favorite",
            new { isFavorite, source },
            cancellationToken);
        await GetSessionAsync(cancellationToken);
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
        }
        catch
        {
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
            var result = await PostAsync<TrackingResponse>(
                $"/api/stories/{Uri.EscapeDataString(slug)}/listen",
                new
                {
                    storyPath = BuildMobileStoryPath(slug, source),
                    source,
                    sessionId = sessionId.ToString(),
                    eventType,
                    listenedSeconds,
                    positionSeconds,
                    durationSeconds,
                    isCompleted
                },
                cancellationToken);
            if (result?.NewNotificationsCreated > 0)
            {
                NewNotificationsAvailable?.Invoke(result.NewNotificationsCreated);
            }
        }
        catch
        {
            // Tracking must not block mobile playback or page rendering.
        }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<MobileLuisterResponse?> GetAndCacheLuisterAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync<MobileLuisterResponse>("/api/mobile/luister?compact=true", cancellationToken);
        if (response is not null)
        {
            await SaveLuisterCacheAsync(response, cancellationToken);
        }

        return response;
    }

    private async Task SaveLuisterCacheAsync(MobileLuisterResponse response, CancellationToken cancellationToken)
    {
        try
        {
            var cachePath = BuildLuisterCachePath();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
            var temporaryPath = $"{cachePath}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new MobileLuisterCacheEntry(DateTimeOffset.UtcNow, response),
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
    }

    private async Task<T?> PostAsync<T>(string path, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        if (response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
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

        var normalizedUrl = NormalizeIncomingUrl(url.Trim());
        if (IsBundledImageName(normalizedUrl))
        {
            return ImageSource.FromFile(normalizedUrl);
        }

        var imageUrl = BuildImageUrl(normalizedUrl);
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

    public async Task CacheImagesAsync(IEnumerable<string?> urls, CancellationToken cancellationToken = default)
    {
        var imageUrls = urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => BuildImageUrl(url!))
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsWebUri(uri))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToArray();

        await Parallel.ForEachAsync(
            imageUrls,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
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
        var playableUrl = BuildAbsoluteUrl(audioUrl);
        var cacheDirectory = System.IO.Path.Combine(FileSystem.CacheDirectory, "story-audio");
        Directory.CreateDirectory(cacheDirectory);

        var cacheKey = BuildAudioCacheKey(playableUrl);
        var extension = ResolveAudioExtensionFromUrl(playableUrl);
        var fileName = $"{ToSafeFileSegment(source)}-{ToSafeFileSegment(slug)}-{cacheKey}{extension}";
        var cachePath = System.IO.Path.Combine(cacheDirectory, fileName);
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
        {
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
        return new Uri(cachePath).AbsoluteUri;
    }

    public async Task DownloadAudioToFileAsync(
        string audioUrl,
        string destinationPath,
        IProgress<MobileAudioDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var playableUrl = BuildAbsoluteUrl(audioUrl);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(playableUrl, UriKind.Absolute));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
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
    }

    private async Task CacheImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
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
}
