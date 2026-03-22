using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseAuthSessionService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IOptions<AuthSessionOptions> authSessionOptions,
    ISubscriptionLedgerService subscriptionLedgerService,
    ILogger<SupabaseAuthSessionService> logger) : IAuthSessionService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _supabaseOptions = supabaseOptions.Value;
    private readonly AuthSessionOptions _authSessionOptions = authSessionOptions.Value;
    private readonly ISubscriptionLedgerService _subscriptionLedgerService = subscriptionLedgerService;
    private readonly ILogger<SupabaseAuthSessionService> _logger = logger;

    public async Task<AuthSessionIssueResult> IssueSessionAsync(
        string? email,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new AuthSessionIssueResult(
                IsSuccess: false,
                SessionId: Guid.Empty,
                MaxConcurrentSessions: ResolveDefaultMaxConcurrentSessions(),
                SessionLifetimeDays: ResolveSessionLifetimeDays(),
                ErrorMessage: "Jou sessie kon nie begin nie: e-posadres ontbreek.");
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return new AuthSessionIssueResult(
                IsSuccess: false,
                SessionId: Guid.Empty,
                MaxConcurrentSessions: ResolveDefaultMaxConcurrentSessions(),
                SessionLifetimeDays: ResolveSessionLifetimeDays(),
                ErrorMessage: "Sessiebeheer is nog nie opgestel nie. Stel asseblief Supabase URL op.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AuthSessionIssueResult(
                IsSuccess: false,
                SessionId: Guid.Empty,
                MaxConcurrentSessions: ResolveDefaultMaxConcurrentSessions(),
                SessionLifetimeDays: ResolveSessionLifetimeDays(),
                ErrorMessage: "Sessiebeheer is nog nie opgestel nie. Stel asseblief Supabase ServiceRoleKey op.");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var nowUtc = DateTimeOffset.UtcNow;
        var maxConcurrentSessions = await ResolveConcurrentSessionLimitAsync(normalizedEmail, cancellationToken);
        var sessionLifetimeDays = ResolveSessionLifetimeDays();
        var sessionId = Guid.NewGuid();
        var expiresAtUtc = nowUtc.AddDays(sessionLifetimeDays);

        try
        {
            var createSessionUri = new Uri(baseUri, "rest/v1/auth_sessions");
            var createPayload = new[]
            {
                new
                {
                    session_id = sessionId,
                    email = normalizedEmail,
                    expires_at = expiresAtUtc.UtcDateTime,
                    user_agent = NormalizeOptionalText(userAgent, 512),
                    ip_address = NormalizeOptionalText(ipAddress, 64)
                }
            };

            using var createSessionRequest = CreateRequest(HttpMethod.Post, createSessionUri, apiKey);
            createSessionRequest.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            createSessionRequest.Content = JsonContent.Create(createPayload);

            using var createSessionResponse = await _httpClient.SendAsync(createSessionRequest, cancellationToken);
            if (!createSessionResponse.IsSuccessStatusCode)
            {
                var responseBody = await createSessionResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase auth session insert failed. Status={StatusCode} Body={Body}",
                    (int)createSessionResponse.StatusCode,
                    responseBody);
                return new AuthSessionIssueResult(
                    IsSuccess: false,
                    SessionId: Guid.Empty,
                    MaxConcurrentSessions: maxConcurrentSessions,
                    SessionLifetimeDays: sessionLifetimeDays,
                    ErrorMessage: "Kon nie nou jou sessie begin nie. Probeer asseblief weer.");
            }

            var activeSessionIds = await GetActiveSessionIdsAsync(baseUri, apiKey, normalizedEmail, nowUtc, cancellationToken);
            if (activeSessionIds is null)
            {
                await RevokeSessionAsync(normalizedEmail, sessionId, cancellationToken);
                return new AuthSessionIssueResult(
                    IsSuccess: false,
                    SessionId: Guid.Empty,
                    MaxConcurrentSessions: maxConcurrentSessions,
                    SessionLifetimeDays: sessionLifetimeDays,
                    ErrorMessage: "Kon nie nou jou sessie verifieer nie. Probeer asseblief weer.");
            }

            if (activeSessionIds.Count > maxConcurrentSessions)
            {
                var revokeCount = activeSessionIds.Count - maxConcurrentSessions;
                var sessionsToRevoke = activeSessionIds
                    .Where(activeSessionId => activeSessionId != sessionId)
                    .Take(revokeCount)
                    .ToArray();

                if (sessionsToRevoke.Length > 0)
                {
                    var revoked = await TryRevokeSessionsAsync(
                        baseUri,
                        apiKey,
                        normalizedEmail,
                        sessionsToRevoke,
                        reason: "concurrent_session_limit",
                        cancellationToken);

                    if (!revoked)
                    {
                        await RevokeSessionAsync(normalizedEmail, sessionId, cancellationToken);
                        return new AuthSessionIssueResult(
                            IsSuccess: false,
                            SessionId: Guid.Empty,
                            MaxConcurrentSessions: maxConcurrentSessions,
                            SessionLifetimeDays: sessionLifetimeDays,
                            ErrorMessage: "Kon nie nou die maksimum aantal toestelle afdwing nie. Probeer asseblief weer.");
                    }
                }
            }

            return new AuthSessionIssueResult(
                IsSuccess: true,
                SessionId: sessionId,
                MaxConcurrentSessions: maxConcurrentSessions,
                SessionLifetimeDays: sessionLifetimeDays);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase auth session issue failed unexpectedly.");
            await RevokeSessionAsync(normalizedEmail, sessionId, cancellationToken);
            return new AuthSessionIssueResult(
                IsSuccess: false,
                SessionId: Guid.Empty,
                MaxConcurrentSessions: maxConcurrentSessions,
                SessionLifetimeDays: sessionLifetimeDays,
                ErrorMessage: "Kon nie nou jou sessie begin nie. Probeer asseblief weer.");
        }
    }

    public async Task<AuthSessionValidationState> ValidateSessionAsync(
        string? email,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || sessionId == Guid.Empty)
        {
            return AuthSessionValidationState.Inactive;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            _logger.LogWarning("Auth session validation skipped: Supabase URL is not configured.");
            return AuthSessionValidationState.Unknown;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Auth session validation skipped: ServiceRoleKey is not configured.");
            return AuthSessionValidationState.Unknown;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var escapedSessionId = Uri.EscapeDataString(sessionId.ToString("D"));
        var escapedNowUtc = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        var validationUri = new Uri(
            baseUri,
            $"rest/v1/auth_sessions?select=session_id&email=eq.{escapedEmail}&session_id=eq.{escapedSessionId}&revoked_at=is.null&expires_at=gt.{escapedNowUtc}&limit=1");

        try
        {
            using var validationRequest = CreateRequest(HttpMethod.Get, validationUri, apiKey);
            using var validationResponse = await _httpClient.SendAsync(validationRequest, cancellationToken);
            if (!validationResponse.IsSuccessStatusCode)
            {
                var responseBody = await validationResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase auth session validation failed. Status={StatusCode} Body={Body}",
                    (int)validationResponse.StatusCode,
                    responseBody);
                return AuthSessionValidationState.Unknown;
            }

            await using var validationStream = await validationResponse.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<AuthSessionRow>>(validationStream, cancellationToken: cancellationToken)
                ?? [];

            return rows.Count > 0 ? AuthSessionValidationState.Active : AuthSessionValidationState.Inactive;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase auth session validation failed unexpectedly.");
            return AuthSessionValidationState.Unknown;
        }
    }

    public async Task RevokeSessionAsync(
        string? email,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || sessionId == Guid.Empty)
        {
            return;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        await TryRevokeSessionsAsync(
            baseUri,
            apiKey,
            normalizedEmail,
            [sessionId],
            reason: "logout",
            cancellationToken);
    }

    private async Task<int> ResolveConcurrentSessionLimitAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var maxConcurrentSessions = ResolveDefaultMaxConcurrentSessions();
        IReadOnlyList<string> activeTierCodes;

        try
        {
            activeTierCodes = await _subscriptionLedgerService.GetActiveTierCodesAsync(normalizedEmail, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Tier lookup failed during auth session issuance; falling back to default session limit.");
            return maxConcurrentSessions;
        }

        if (activeTierCodes.Count == 0)
        {
            return maxConcurrentSessions;
        }

        var tierOverrides = BuildTierSessionLimitMap(_authSessionOptions.TierSessionLimits);
        foreach (var tierCode in activeTierCodes)
        {
            var normalizedTierCode = tierCode?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTierCode))
            {
                continue;
            }

            if (!tierOverrides.TryGetValue(normalizedTierCode, out var overrideLimit))
            {
                continue;
            }

            maxConcurrentSessions = Math.Max(maxConcurrentSessions, NormalizeSessionLimit(overrideLimit));
        }

        return maxConcurrentSessions;
    }

    private static Dictionary<string, int> BuildTierSessionLimitMap(Dictionary<string, int>? rawOverrides)
    {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (rawOverrides is null || rawOverrides.Count == 0)
        {
            return normalized;
        }

        foreach (var (tierCode, limit) in rawOverrides)
        {
            if (string.IsNullOrWhiteSpace(tierCode))
            {
                continue;
            }

            normalized[tierCode.Trim()] = NormalizeSessionLimit(limit);
        }

        return normalized;
    }

    private int ResolveDefaultMaxConcurrentSessions()
    {
        return NormalizeSessionLimit(_authSessionOptions.DefaultMaxConcurrentSessions);
    }

    private int ResolveSessionLifetimeDays()
    {
        return Math.Clamp(_authSessionOptions.SessionLifetimeDays, 1, 90);
    }

    private static int NormalizeSessionLimit(int limit)
    {
        return Math.Clamp(limit, 1, 12);
    }

    private async Task<IReadOnlyList<Guid>?> GetActiveSessionIdsAsync(
        Uri baseUri,
        string apiKey,
        string normalizedEmail,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var escapedNowUtc = Uri.EscapeDataString(nowUtc.ToString("O"));
        var sessionsUri = new Uri(
            baseUri,
            $"rest/v1/auth_sessions?select=session_id&email=eq.{escapedEmail}&revoked_at=is.null&expires_at=gt.{escapedNowUtc}&order=created_at.asc&limit=100");

        try
        {
            using var sessionsRequest = CreateRequest(HttpMethod.Get, sessionsUri, apiKey);
            using var sessionsResponse = await _httpClient.SendAsync(sessionsRequest, cancellationToken);
            if (!sessionsResponse.IsSuccessStatusCode)
            {
                var responseBody = await sessionsResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase auth active sessions lookup failed. Status={StatusCode} Body={Body}",
                    (int)sessionsResponse.StatusCode,
                    responseBody);
                return null;
            }

            await using var sessionsStream = await sessionsResponse.Content.ReadAsStreamAsync(cancellationToken);
            var rows = await JsonSerializer.DeserializeAsync<List<AuthSessionRow>>(sessionsStream, cancellationToken: cancellationToken)
                ?? [];

            return rows
                .Select(row => row.SessionId)
                .Where(sessionId => sessionId != Guid.Empty)
                .ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase auth active sessions lookup failed unexpectedly.");
            return null;
        }
    }

    private async Task<bool> TryRevokeSessionsAsync(
        Uri baseUri,
        string apiKey,
        string normalizedEmail,
        IReadOnlyList<Guid> sessionIds,
        string reason,
        CancellationToken cancellationToken)
    {
        if (sessionIds.Count == 0)
        {
            return true;
        }

        var escapedEmail = Uri.EscapeDataString(normalizedEmail);
        var sessionIdFilter = string.Join(",", sessionIds
            .Where(sessionId => sessionId != Guid.Empty)
            .Select(sessionId => sessionId.ToString("D")));
        if (string.IsNullOrWhiteSpace(sessionIdFilter))
        {
            return true;
        }

        var revokeUri = new Uri(
            baseUri,
            $"rest/v1/auth_sessions?email=eq.{escapedEmail}&revoked_at=is.null&session_id=in.({sessionIdFilter})");

        try
        {
            var revokePayload = new
            {
                revoked_at = DateTimeOffset.UtcNow.UtcDateTime,
                revoked_reason = reason
            };

            using var revokeRequest = CreateRequest(HttpMethod.Patch, revokeUri, apiKey);
            revokeRequest.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            revokeRequest.Content = JsonContent.Create(revokePayload);

            using var revokeResponse = await _httpClient.SendAsync(revokeRequest, cancellationToken);
            if (revokeResponse.IsSuccessStatusCode)
            {
                return true;
            }

            var responseBody = await revokeResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Supabase auth session revoke failed. Status={StatusCode} Body={Body}",
                (int)revokeResponse.StatusCode,
                responseBody);
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Supabase auth session revoke failed unexpectedly.");
            return false;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = default!;
        if (string.IsNullOrWhiteSpace(_supabaseOptions.Url))
        {
            return false;
        }

        if (!Uri.TryCreate(_supabaseOptions.Url, UriKind.Absolute, out var configuredBaseUri))
        {
            _logger.LogWarning("Supabase URL is invalid: {SupabaseUrl}", _supabaseOptions.Url);
            return false;
        }

        baseUri = configuredBaseUri;
        return true;
    }

    private string? ResolveApiKey()
    {
        var apiKey = _supabaseOptions.ServiceRoleKey?.Trim();
        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private sealed class AuthSessionRow
    {
        [JsonPropertyName("session_id")]
        public Guid SessionId { get; set; }
    }
}
