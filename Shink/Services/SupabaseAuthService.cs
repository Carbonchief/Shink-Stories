using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Supabase.Gotrue;

namespace Shink.Services;

public sealed class SupabaseAuthService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    ILogger<SupabaseAuthService> logger) : ISupabaseAuthService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly ILogger<SupabaseAuthService> _logger = logger;

    public async Task<SupabaseSignInResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return SupabaseSignInResult.Failure("Vul asseblief jou e-pos en wagwoord in.");
        }

        if (!TryBuildTokenEndpoint(out var tokenEndpoint))
        {
            return SupabaseSignInResult.Failure("Supabase is nog nie opgestel nie. Stel asseblief die Supabase URL en anon key op.");
        }

        return await ExecuteAuthRequestAsync(
            tokenEndpoint,
            new SupabasePasswordSignInRequest(email, password),
            failureFallbackMessage: "Kon nie in teken nie. Kontroleer jou e-pos en wagwoord en probeer weer.",
            requestActionName: "sign-in",
            cancellationToken);
    }

    public async Task<SupabaseSignInResult> SignUpWithPasswordAsync(
        string email,
        string password,
        SignUpProfileData? profileData = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return SupabaseSignInResult.Failure("Vul asseblief jou e-pos en wagwoord in.");
        }

        if (!TryBuildSignupEndpoint(out var signupEndpoint))
        {
            return SupabaseSignInResult.Failure("Supabase is nog nie opgestel nie. Stel asseblief die Supabase URL en anon key op.");
        }

        var normalizedDisplayName = profileData?.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            var firstName = profileData?.FirstName?.Trim();
            var lastName = profileData?.LastName?.Trim();
            normalizedDisplayName = $"{firstName} {lastName}".Trim();
        }

        var signUpMetadata = profileData is null
            ? null
            : new SupabasePasswordSignUpMetadata(
                FirstName: profileData.FirstName?.Trim(),
                LastName: profileData.LastName?.Trim(),
                DisplayName: string.IsNullOrWhiteSpace(normalizedDisplayName) ? null : normalizedDisplayName,
                FullName: string.IsNullOrWhiteSpace(normalizedDisplayName) ? null : normalizedDisplayName,
                MobileNumber: profileData.MobileNumber?.Trim());

        var signupResult = await ExecuteAuthRequestAsync(
            signupEndpoint,
            new SupabasePasswordSignUpRequest(email, password, signUpMetadata),
            failureFallbackMessage: "Kon nie nou registreer nie. Probeer asseblief weer.",
            requestActionName: "sign-up",
            cancellationToken);

        if (!signupResult.IsSuccess)
        {
            if (ShouldFallbackToAdminCreateUser(signupResult.ErrorMessage))
            {
                var adminCreateResult = await TryCreateConfirmedUserWithAdminApiAsync(
                    email,
                    password,
                    signUpMetadata,
                    cancellationToken);

                if (adminCreateResult.IsSuccess)
                {
                    return adminCreateResult;
                }
            }

            return signupResult;
        }

        return SupabaseSignInResult.Success(signupResult.UserEmail ?? email);
    }

    public async Task<SupabasePasswordResetResult> SendPasswordResetEmailAsync(
        string email,
        string redirectTo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return SupabasePasswordResetResult.Failure("Gebruik asseblief 'n geldige e-posadres.");
        }

        if (string.IsNullOrWhiteSpace(redirectTo))
        {
            return SupabasePasswordResetResult.Failure("Kon nie die herstel-skakel voorberei nie. Probeer asseblief weer.");
        }

        if (!TryBuildRecoverEndpoint(out var recoverEndpoint))
        {
            return SupabasePasswordResetResult.Failure("Supabase is nog nie opgestel nie. Stel asseblief die Supabase URL en anon key op.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, recoverEndpoint)
        {
            Content = JsonContent.Create(new SupabasePasswordRecoveryRequest(email, redirectTo))
        };
        request.Headers.TryAddWithoutValidation("apikey", _options.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AnonKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Supabase password recovery request failed.");
            return SupabasePasswordResetResult.Failure("Kon nie nou met Supabase koppel nie. Probeer asseblief weer.");
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return SupabasePasswordResetResult.Success();
            }

            var errorMessage = ReadErrorMessage(responseBody) ?? "Kon nie nou 'n herstel-skakel stuur nie. Probeer asseblief weer.";
            _logger.LogInformation(
                "Supabase password recovery rejected: {StatusCode} {Message}",
                (int)response.StatusCode,
                errorMessage);
            return SupabasePasswordResetResult.Failure(errorMessage);
        }
    }

    public async Task<SupabasePasswordResetResult> UpdatePasswordAsync(
        string accessToken,
        string refreshToken,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return SupabasePasswordResetResult.Failure("Die herstel-skakel is ongeldig of het verval. Vra asseblief 'n nuwe skakel aan.");
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return SupabasePasswordResetResult.Failure("Vul asseblief 'n nuwe wagwoord in.");
        }

        if (!TryBuildUserEndpoint(out var userEndpoint) ||
            !TryBuildRefreshTokenEndpoint(out var refreshTokenEndpoint))
        {
            return SupabasePasswordResetResult.Failure("Supabase is nog nie opgestel nie. Stel asseblief die Supabase URL en anon key op.");
        }

        var updateResult = await TryUpdatePasswordAsync(
            userEndpoint,
            accessToken.Trim(),
            newPassword,
            cancellationToken);
        if (updateResult.IsSuccess)
        {
            return SupabasePasswordResetResult.Success(updateResult.UserEmail);
        }

        if (updateResult.ShouldRefreshSession)
        {
            var refreshedSession = await TryRefreshPasswordResetSessionAsync(
                refreshTokenEndpoint,
                refreshToken.Trim(),
                cancellationToken);

            if (!refreshedSession.IsSuccess)
            {
                return SupabasePasswordResetResult.Failure(
                    refreshedSession.ErrorMessage ?? updateResult.ErrorMessage ?? "Kon nie jou wagwoord nou opdateer nie. Probeer asseblief weer.");
            }

            updateResult = await TryUpdatePasswordAsync(
                userEndpoint,
                refreshedSession.AccessToken!,
                newPassword,
                cancellationToken);
            if (updateResult.IsSuccess)
            {
                return SupabasePasswordResetResult.Success(updateResult.UserEmail ?? refreshedSession.UserEmail);
            }
        }

        return SupabasePasswordResetResult.Failure(
            updateResult.ErrorMessage ?? "Kon nie jou wagwoord nou opdateer nie. Probeer asseblief weer.");
    }

    public async Task<SupabaseOAuthStartResult> StartGoogleSignInAsync(
        string redirectTo,
        bool useImplicitFlow,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(redirectTo))
        {
            return SupabaseOAuthStartResult.Failure("Kon nie Google-aanmelding begin nie. Probeer asseblief weer.");
        }

        var supabaseClient = await CreateSupabaseClientAsync(cancellationToken);
        if (supabaseClient is null)
        {
            return SupabaseOAuthStartResult.Failure("Supabase is nog nie opgestel nie. Stel asseblief die Supabase URL en anon key op.");
        }

        try
        {
            var providerState = await supabaseClient.Auth.SignIn(
                Constants.Provider.Google,
                new SignInOptions
                {
                    RedirectTo = redirectTo,
                    FlowType = useImplicitFlow
                        ? Constants.OAuthFlowType.Implicit
                        : Constants.OAuthFlowType.PKCE
                });

            if (providerState?.Uri is null)
            {
                return SupabaseOAuthStartResult.Failure("Kon nie Google-aanmelding begin nie. Probeer asseblief weer.");
            }

            return SupabaseOAuthStartResult.Success(providerState.Uri, providerState.PKCEVerifier);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Supabase Google OAuth start failed.");
            return SupabaseOAuthStartResult.Failure("Kon nie nou met Google koppel nie. Probeer asseblief weer.");
        }
    }

    public async Task<SupabaseOAuthExchangeResult> ExchangeGoogleAuthCodeAsync(
        string authCode,
        string codeVerifier,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authCode) || string.IsNullOrWhiteSpace(codeVerifier))
        {
            return SupabaseOAuthExchangeResult.Failure("Google-aanmelding kon nie bevestig word nie. Probeer asseblief weer.");
        }

        var supabaseClient = await CreateSupabaseClientAsync(cancellationToken);
        if (supabaseClient is null)
        {
            return SupabaseOAuthExchangeResult.Failure("Supabase is nog nie opgestel nie. Stel asseblief die Supabase URL en anon key op.");
        }

        try
        {
            var session = await supabaseClient.Auth.ExchangeCodeForSession(codeVerifier, authCode);
            return CreateGoogleOAuthExchangeResult(session);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Supabase Google OAuth code exchange failed.");
            return SupabaseOAuthExchangeResult.Failure("Google-aanmelding het misluk. Probeer asseblief weer.");
        }
    }

    public async Task<SupabaseOAuthExchangeResult> ExchangeGoogleImplicitSessionAsync(
        Uri callbackUri,
        CancellationToken cancellationToken = default)
    {
        if (callbackUri is null)
        {
            return SupabaseOAuthExchangeResult.Failure("Google-aanmelding kon nie bevestig word nie. Probeer asseblief weer.");
        }

        var supabaseClient = await CreateSupabaseClientAsync(cancellationToken);
        if (supabaseClient is null)
        {
            return SupabaseOAuthExchangeResult.Failure("Supabase is nog nie opgestel nie. Stel asseblief die Supabase URL en anon key op.");
        }

        try
        {
            var session = await supabaseClient.Auth.GetSessionFromUrl(callbackUri, false);
            return CreateGoogleOAuthExchangeResult(session);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Supabase Google implicit OAuth session parse failed.");
            return SupabaseOAuthExchangeResult.Failure("Google-aanmelding het misluk. Probeer asseblief weer.");
        }
    }

    private async Task<SupabaseSignInResult> ExecuteAuthRequestAsync(
        Uri endpoint,
        object requestPayload,
        string failureFallbackMessage,
        string requestActionName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(requestPayload)
        };
        request.Headers.TryAddWithoutValidation("apikey", _options.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AnonKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Supabase {RequestActionName} request failed.", requestActionName);
            return SupabaseSignInResult.Failure("Kon nie nou met Supabase koppel nie. Probeer asseblief weer.");
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseEmail = ReadUserEmail(responseBody);
                return SupabaseSignInResult.Success(responseEmail);
            }

            var errorMessage = ReadErrorMessage(responseBody) ?? failureFallbackMessage;
            _logger.LogInformation("Supabase {RequestActionName} rejected: {StatusCode} {Message}", requestActionName, (int)response.StatusCode, errorMessage);
            return SupabaseSignInResult.Failure(errorMessage);
        }
    }

    private bool TryBuildTokenEndpoint(out Uri tokenEndpoint)
    {
        tokenEndpoint = default!;
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var supabaseUri))
        {
            _logger.LogWarning("Supabase URL is invalid: {SupabaseUrl}", _options.Url);
            return false;
        }

        tokenEndpoint = new Uri(supabaseUri, "auth/v1/token?grant_type=password");
        return true;
    }

    private bool TryBuildSignupEndpoint(out Uri signupEndpoint)
    {
        signupEndpoint = default!;
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var supabaseUri))
        {
            _logger.LogWarning("Supabase URL is invalid: {SupabaseUrl}", _options.Url);
            return false;
        }

        signupEndpoint = new Uri(supabaseUri, "auth/v1/signup");
        return true;
    }

    private bool TryBuildRecoverEndpoint(out Uri recoverEndpoint)
    {
        recoverEndpoint = default!;
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var supabaseUri))
        {
            _logger.LogWarning("Supabase URL is invalid: {SupabaseUrl}", _options.Url);
            return false;
        }

        recoverEndpoint = new Uri(supabaseUri, "auth/v1/recover");
        return true;
    }

    private bool TryBuildUserEndpoint(out Uri userEndpoint)
    {
        userEndpoint = default!;
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var supabaseUri))
        {
            _logger.LogWarning("Supabase URL is invalid: {SupabaseUrl}", _options.Url);
            return false;
        }

        userEndpoint = new Uri(supabaseUri, "auth/v1/user");
        return true;
    }

    private bool TryBuildRefreshTokenEndpoint(out Uri refreshTokenEndpoint)
    {
        refreshTokenEndpoint = default!;
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var supabaseUri))
        {
            _logger.LogWarning("Supabase URL is invalid: {SupabaseUrl}", _options.Url);
            return false;
        }

        refreshTokenEndpoint = new Uri(supabaseUri, "auth/v1/token?grant_type=refresh_token");
        return true;
    }

    private bool TryBuildAdminUsersEndpoint(out Uri adminUsersEndpoint)
    {
        adminUsersEndpoint = default!;
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.ServiceRoleKey))
        {
            return false;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out var supabaseUri))
        {
            _logger.LogWarning("Supabase URL is invalid: {SupabaseUrl}", _options.Url);
            return false;
        }

        adminUsersEndpoint = new Uri(supabaseUri, "auth/v1/admin/users");
        return true;
    }

    private async Task<SupabaseSignInResult> TryCreateConfirmedUserWithAdminApiAsync(
        string email,
        string password,
        SupabasePasswordSignUpMetadata? metadata,
        CancellationToken cancellationToken)
    {
        if (!TryBuildAdminUsersEndpoint(out var adminUsersEndpoint))
        {
            _logger.LogWarning(
                "Supabase admin signup fallback skipped because ServiceRoleKey or URL is not configured.");
            return SupabaseSignInResult.Failure(
                "Registrasie kon nie voltooi nie omdat bevestigings-e-pos tans nie beskikbaar is nie.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, adminUsersEndpoint)
        {
            Content = JsonContent.Create(new SupabaseAdminCreateUserRequest(
                Email: email,
                Password: password,
                EmailConfirm: true,
                UserMetadata: metadata))
        };
        request.Headers.TryAddWithoutValidation("apikey", _options.ServiceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Supabase admin create user fallback failed.");
            return SupabaseSignInResult.Failure(
                "Registrasie kon nie voltooi nie omdat bevestigings-e-pos tans nie beskikbaar is nie.");
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseEmail = ReadUserEmail(responseBody);
                _logger.LogInformation(
                    "Supabase admin create user fallback succeeded after confirmation email failure for {Email}.",
                    email);
                return SupabaseSignInResult.Success(responseEmail ?? email);
            }

            var errorMessage = ReadErrorMessage(responseBody)
                ?? "Registrasie kon nie voltooi word nie. Probeer asseblief weer.";
            _logger.LogWarning(
                "Supabase admin create user fallback rejected: {StatusCode} {Message}",
                (int)response.StatusCode,
                errorMessage);
            return SupabaseSignInResult.Failure(errorMessage);
        }
    }

    private async Task<PasswordUpdateAttemptResult> TryUpdatePasswordAsync(
        Uri userEndpoint,
        string accessToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, userEndpoint)
        {
            Content = JsonContent.Create(new SupabasePasswordUpdateRequest(newPassword))
        };
        request.Headers.TryAddWithoutValidation("apikey", _options.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Supabase password update request failed.");
            return PasswordUpdateAttemptResult.Failure(
                "Kon nie nou met Supabase koppel nie. Probeer asseblief weer.",
                shouldRefreshSession: false);
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return PasswordUpdateAttemptResult.Success(ReadUserEmail(responseBody));
            }

            var errorMessage = ReadErrorMessage(responseBody) ?? "Kon nie jou wagwoord nou opdateer nie. Probeer asseblief weer.";
            _logger.LogInformation(
                "Supabase password update rejected: {StatusCode} {Message}",
                (int)response.StatusCode,
                errorMessage);
            return PasswordUpdateAttemptResult.Failure(
                errorMessage,
                ShouldRefreshPasswordResetSession(response.StatusCode, errorMessage));
        }
    }

    private async Task<RefreshedSessionResult> TryRefreshPasswordResetSessionAsync(
        Uri refreshTokenEndpoint,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, refreshTokenEndpoint)
        {
            Content = JsonContent.Create(new SupabaseRefreshTokenRequest(refreshToken))
        };
        request.Headers.TryAddWithoutValidation("apikey", _options.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AnonKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Supabase password recovery session refresh failed.");
            return RefreshedSessionResult.Failure("Kon nie nou met Supabase koppel nie. Probeer asseblief weer.");
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = ReadRefreshTokenResponse(responseBody);
                if (!string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
                {
                    return RefreshedSessionResult.Success(
                        tokenResponse.AccessToken!,
                        tokenResponse.RefreshToken ?? refreshToken,
                        tokenResponse.UserEmail);
                }
            }

            var errorMessage = ReadErrorMessage(responseBody) ?? "Die herstel-skakel is ongeldig of het verval. Vra asseblief 'n nuwe skakel aan.";
            _logger.LogInformation(
                "Supabase password recovery session refresh rejected: {StatusCode} {Message}",
                (int)response.StatusCode,
                errorMessage);
            return RefreshedSessionResult.Failure(errorMessage);
        }
    }

    private static string? ReadUserEmail(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("email", out var rootEmailNode) &&
                rootEmailNode.ValueKind == JsonValueKind.String)
            {
                return rootEmailNode.GetString();
            }

            if (doc.RootElement.TryGetProperty("user", out var userNode) &&
                userNode.ValueKind == JsonValueKind.Object &&
                userNode.TryGetProperty("email", out var emailNode) &&
                emailNode.ValueKind == JsonValueKind.String)
            {
                return emailNode.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static RefreshTokenResponse ReadRefreshTokenResponse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return new RefreshTokenResponse(null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var accessToken = root.TryGetProperty("access_token", out var accessTokenNode) &&
                              accessTokenNode.ValueKind == JsonValueKind.String
                ? accessTokenNode.GetString()
                : null;
            var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenNode) &&
                               refreshTokenNode.ValueKind == JsonValueKind.String
                ? refreshTokenNode.GetString()
                : null;

            return new RefreshTokenResponse(accessToken, refreshToken, ReadUserEmail(responseBody));
        }
        catch (JsonException)
        {
            return new RefreshTokenResponse(null, null, null);
        }
    }

    private static string? ReadErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            foreach (var key in new[] { "msg", "error_description", "message", "error" })
            {
                if (root.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.String)
                {
                    var message = node.GetString();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return NormalizeSupabaseMessage(message);
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string NormalizeSupabaseMessage(string message)
    {
        if (message.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase))
        {
            return "Ongeldige e-pos of wagwoord. Probeer asseblief weer.";
        }

        if (message.Contains("User already registered", StringComparison.OrdinalIgnoreCase))
        {
            return "Hierdie e-posadres is reeds geregistreer. Teken asseblief in.";
        }

        if (message.Contains("For security purposes", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
        {
            return "Wag asseblief 'n oomblik voor jy weer 'n herstel-skakel aanvra.";
        }

        if (message.Contains("Error sending confirmation email", StringComparison.OrdinalIgnoreCase))
        {
            return "Kon nie bevestigings-e-pos stuur nie. Probeer asseblief weer.";
        }

        if (message.Contains("invalid or has expired", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("invalid token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("jwt", StringComparison.OrdinalIgnoreCase))
        {
            return "Die herstel-skakel is ongeldig of het verval. Vra asseblief 'n nuwe skakel aan.";
        }

        if (message.Contains("same password", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("different from the old password", StringComparison.OrdinalIgnoreCase))
        {
            return "Gebruik asseblief 'n nuwe wagwoord wat verskil van jou vorige een.";
        }

        return message;
    }

    private static bool ShouldFallbackToAdminCreateUser(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.Contains("bevestigings-e-pos", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldRefreshPasswordResetSession(HttpStatusCode statusCode, string? errorMessage) =>
        statusCode == HttpStatusCode.Unauthorized ||
        (!string.IsNullOrWhiteSpace(errorMessage) &&
         (errorMessage.Contains("verval", StringComparison.OrdinalIgnoreCase) ||
          errorMessage.Contains("jwt", StringComparison.OrdinalIgnoreCase)));

    private static SupabaseOAuthExchangeResult CreateGoogleOAuthExchangeResult(Session? session)
    {
        var email = session?.User?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return SupabaseOAuthExchangeResult.Failure("Kon nie jou Google-profiel lees nie. Probeer asseblief weer.");
        }

        var userMetadata = session?.User?.UserMetadata;
        var firstName = ReadMetadataString(userMetadata, "first_name", "given_name");
        var lastName = ReadMetadataString(userMetadata, "last_name", "family_name");
        var displayName = ReadMetadataString(userMetadata, "display_name", "name", "full_name");

        return SupabaseOAuthExchangeResult.Success(email, firstName, lastName, displayName);
    }

    private async Task<global::Supabase.Client?> CreateSupabaseClientAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return null;
        }

        if (!Uri.TryCreate(_options.Url, UriKind.Absolute, out _))
        {
            _logger.LogWarning("Supabase URL is invalid: {SupabaseUrl}", _options.Url);
            return null;
        }

        var client = new global::Supabase.Client(
            _options.Url,
            _options.AnonKey,
            new global::Supabase.SupabaseOptions
            {
                AutoConnectRealtime = false,
                AutoRefreshToken = false
            });

        await client.InitializeAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return client;
    }

    private static string? ReadMetadataString(
        IDictionary<string, object>? metadata,
        params string[] keys)
    {
        if (metadata is null || keys.Length == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var value) || value is null)
            {
                continue;
            }

            var candidate = value switch
            {
                string stringValue => stringValue,
                JsonElement { ValueKind: JsonValueKind.String } jsonElement => jsonElement.GetString(),
                _ => value.ToString()
            };

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    private sealed record SupabasePasswordSignInRequest(string Email, string Password);
    private sealed record SupabasePasswordSignUpRequest(string Email, string Password, SupabasePasswordSignUpMetadata? Data = null);
    private sealed record SupabasePasswordRecoveryRequest(
        string Email,
        [property: JsonPropertyName("redirect_to")] string RedirectTo);
    private sealed record SupabasePasswordUpdateRequest(string Password);
    private sealed record SupabaseRefreshTokenRequest([property: JsonPropertyName("refresh_token")] string RefreshToken);
    private sealed record SupabaseAdminCreateUserRequest(
        string Email,
        string Password,
        [property: JsonPropertyName("email_confirm")] bool EmailConfirm,
        [property: JsonPropertyName("user_metadata")] SupabasePasswordSignUpMetadata? UserMetadata);
    private sealed record SupabasePasswordSignUpMetadata(
        string? FirstName,
        string? LastName,
        string? DisplayName,
        string? FullName,
        string? MobileNumber);
    private sealed record PasswordUpdateAttemptResult(
        bool IsSuccess,
        string? UserEmail,
        string? ErrorMessage,
        bool ShouldRefreshSession)
    {
        public static PasswordUpdateAttemptResult Success(string? userEmail) =>
            new(true, userEmail, null, false);

        public static PasswordUpdateAttemptResult Failure(string errorMessage, bool shouldRefreshSession) =>
            new(false, null, errorMessage, shouldRefreshSession);
    }

    private sealed record RefreshedSessionResult(
        bool IsSuccess,
        string? AccessToken,
        string? RefreshToken,
        string? UserEmail,
        string? ErrorMessage)
    {
        public static RefreshedSessionResult Success(
            string accessToken,
            string? refreshToken,
            string? userEmail) =>
            new(true, accessToken, refreshToken, userEmail, null);

        public static RefreshedSessionResult Failure(string errorMessage) =>
            new(false, null, null, null, errorMessage);
    }

    private sealed record RefreshTokenResponse(string? AccessToken, string? RefreshToken, string? UserEmail);
}
