using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
            return signupResult;
        }

        return SupabaseSignInResult.Success(signupResult.UserEmail ?? email);
    }

    public async Task<SupabaseOAuthStartResult> StartGoogleSignInAsync(
        string redirectTo,
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
                    FlowType = Constants.OAuthFlowType.PKCE
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
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Supabase Google OAuth code exchange failed.");
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

    private static string? ReadUserEmail(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
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

        return message;
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
    private sealed record SupabasePasswordSignUpMetadata(
        string? FirstName,
        string? LastName,
        string? DisplayName,
        string? FullName,
        string? MobileNumber);
}
