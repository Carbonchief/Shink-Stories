using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public sealed class MobileAppSettings
{
    private const string BaseUrlPreferenceKey = "mobile_api_base_url";
    private const string DefaultBaseUrl = "https://www.schink.co.za";

    public string BaseUrl
    {
        get => Preferences.Get(BaseUrlPreferenceKey, DefaultBaseUrl);
        set => Preferences.Set(BaseUrlPreferenceKey, NormalizeBaseUrl(value));
    }

    public static string NormalizeBaseUrl(string? value)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? DefaultBaseUrl : value.Trim();
        return candidate.TrimEnd('/');
    }
}

public sealed class SessionState
{
    public MobileSession Current { get; private set; } = new(
        IsSignedIn: false,
        Email: null,
        HasPaidSubscription: false,
        FavoriteStorySlugs: Array.Empty<string>(),
        LoginUrl: string.Empty,
        SignupUrl: string.Empty,
        PlansUrl: string.Empty);

    public event Action<MobileSession>? Changed;

    public void Update(MobileSession session)
    {
        Current = session;
        Changed?.Invoke(session);
    }
}

public sealed class MobileApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MobileAppSettings _settings;
    private readonly SessionState _sessionState;

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
            ?? new MobileSession(false, null, false, Array.Empty<string>(), string.Empty, string.Empty, string.Empty);
        _sessionState.Update(session);
        return session;
    }

    public Task<MobileHomeResponse?> GetHomeAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MobileHomeResponse>("/api/mobile/home", cancellationToken);

    public Task<MobileStoryCollectionResponse?> GetGratisAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MobileStoryCollectionResponse>("/api/mobile/gratis", cancellationToken);

    public Task<MobileLuisterResponse?> GetLuisterAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MobileLuisterResponse>("/api/mobile/luister", cancellationToken);

    public Task<MobileAboutResponse?> GetAboutAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MobileAboutResponse>("/api/mobile/meer-oor-ons", cancellationToken);

    public Task<MobileStoryDetailResponse?> GetStoryAsync(string slug, string source, CancellationToken cancellationToken = default) =>
        GetAsync<MobileStoryDetailResponse>(
            $"/api/mobile/stories/{Uri.EscapeDataString(slug)}?source={Uri.EscapeDataString(source)}",
            cancellationToken);

    public string BuildAbsoluteUrl(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var normalizedPath = path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";
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
            await PostAsync<TrackingResponse>(
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

    private Uri BuildUri(string path) => new($"{_settings.BaseUrl.TrimEnd('/')}{path}", UriKind.Absolute);

    private static string BuildMobileStoryPath(string slug, string source) =>
        $"/mobile/{Uri.EscapeDataString(source)}/{Uri.EscapeDataString(slug)}";

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
            ? $"Versoek het misluk met status {(int)response.StatusCode}."
            : body);
    }

    private sealed record FavoriteResponse(bool IsFavorite);
    private sealed record TrackingResponse(bool Tracked);
}
