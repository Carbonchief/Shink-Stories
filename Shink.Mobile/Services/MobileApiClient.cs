using System.Net;
using System.Net.Http.Json;
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
    public MobileSession Current { get; private set; } = new(
        IsSignedIn: false,
        Email: null,
        DisplayName: null,
        ProfileImageUrl: null,
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
            ?? new MobileSession(false, null, null, null, false, Array.Empty<string>(), string.Empty, string.Empty, string.Empty);
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

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableUrl)))[..12].ToLowerInvariant();
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
            : body);
    }

    private sealed record FavoriteResponse(bool IsFavorite);
    private sealed record TrackingResponse(bool Tracked);
}
