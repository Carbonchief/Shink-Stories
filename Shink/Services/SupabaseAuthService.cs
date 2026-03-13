using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

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

    public async Task<SupabaseSignInResult> SignUpWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return SupabaseSignInResult.Failure("Vul asseblief jou e-pos en wagwoord in.");
        }

        if (!TryBuildSignupEndpoint(out var signupEndpoint))
        {
            return SupabaseSignInResult.Failure("Supabase is nog nie opgestel nie. Stel asseblief die Supabase URL en anon key op.");
        }

        var signupResult = await ExecuteAuthRequestAsync(
            signupEndpoint,
            new SupabasePasswordSignUpRequest(email, password),
            failureFallbackMessage: "Kon nie nou registreer nie. Probeer asseblief weer.",
            requestActionName: "sign-up",
            cancellationToken);

        if (!signupResult.IsSuccess)
        {
            return signupResult;
        }

        return SupabaseSignInResult.Success(signupResult.UserEmail ?? email);
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

    private sealed record SupabasePasswordSignInRequest(string Email, string Password);
    private sealed record SupabasePasswordSignUpRequest(string Email, string Password);
}
