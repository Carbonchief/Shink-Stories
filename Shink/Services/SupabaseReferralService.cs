using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseReferralService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    ILogger<SupabaseReferralService> logger) : IReferralService
{
    private const int MaxCreateAttempts = 4;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _options = supabaseOptions.Value;
    private readonly ILogger<SupabaseReferralService> _logger = logger;

    public async Task<AdminReferralSnapshot> GetAdminReferralsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(adminEmail, cancellationToken) || !TryBuildBaseUri(out var baseUri))
        {
            return AdminReferralSnapshot.Empty;
        }

        var apiKey = _options.SecretKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AdminReferralSnapshot.Empty;
        }

        var endpoint = new Uri(baseUri, "rest/v1/rpc/admin_referral_codes_summary");
        try
        {
            using var request = CreateJsonRequest(HttpMethod.Post, endpoint, apiKey, new { });
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not load referral summary. Status={StatusCode}", (int)response.StatusCode);
                return AdminReferralSnapshot.Empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var summary = await JsonSerializer.DeserializeAsync<ReferralSummaryResponse>(stream, JsonOptions, cancellationToken);
            return summary is null
                ? AdminReferralSnapshot.Empty
                : new AdminReferralSnapshot(
                    Math.Max(0, summary.TotalReferrals),
                    Math.Max(0, summary.TotalSignups),
                    summary.Items?
                        .Select(item => new AdminReferralCodeRecord(
                            item.Code,
                            item.ReferrerName,
                            item.ReferrerEmail,
                            item.CreatedAt,
                            Math.Max(0, item.SignupCount),
                            item.LastSignupAt))
                        .ToArray() ?? Array.Empty<AdminReferralCodeRecord>());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Could not load referral summary.");
            return AdminReferralSnapshot.Empty;
        }
    }

    public async Task<ReferralOperationResult> CreateReferralAsync(
        string? adminEmail,
        ReferralCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedAdminEmail = NormalizeEmail(adminEmail);
        if (normalizedAdminEmail is null || !await IsAdminAsync(normalizedAdminEmail, cancellationToken))
        {
            return ReferralOperationResult.Failure("Jy het nie toestemming om verwysings te bestuur nie.");
        }

        var referrerName = request.ReferrerName?.Trim();
        if (string.IsNullOrWhiteSpace(referrerName) || referrerName.Length is < 2 or > 120)
        {
            return ReferralOperationResult.Failure("Vul asseblief 'n naam van 2 tot 120 karakters in.");
        }

        var referrerEmail = NormalizeEmail(request.ReferrerEmail);
        if (!string.IsNullOrWhiteSpace(request.ReferrerEmail) && referrerEmail is null)
        {
            return ReferralOperationResult.Failure("Gebruik asseblief 'n geldige e-posadres, of laat dit leeg.");
        }

        if (!TryBuildBaseUri(out var baseUri) || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return ReferralOperationResult.Failure("Verwysings is nie tans beskikbaar nie.");
        }

        var endpoint = new Uri(baseUri, "rest/v1/referral_codes");
        for (var attempt = 0; attempt < MaxCreateAttempts; attempt++)
        {
            var referralCode = ReferralCodeRules.Generate();
            try
            {
                using var requestMessage = CreateJsonRequest(
                    HttpMethod.Post,
                    endpoint,
                    _options.SecretKey,
                    new
                    {
                        code = referralCode,
                        referrer_name = referrerName,
                        referrer_email = referrerEmail,
                        created_by_admin_email = normalizedAdminEmail
                    });
                using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return ReferralOperationResult.Success(referralCode);
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    continue;
                }

                _logger.LogWarning("Could not create referral code. Status={StatusCode}", (int)response.StatusCode);
                return ReferralOperationResult.Failure("Kon nie die verwysing nou skep nie. Probeer asseblief weer.");
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(exception, "Could not create referral code.");
                return ReferralOperationResult.Failure("Kon nie die verwysing nou skep nie. Probeer asseblief weer.");
            }
        }

        return ReferralOperationResult.Failure("Kon nie 'n unieke verwysingskode skep nie. Probeer asseblief weer.");
    }

    private async Task<bool> IsAdminAsync(string? email, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null || !TryBuildBaseUri(out var baseUri) || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return false;
        }

        var endpoint = new Uri(
            baseUri,
            $"rest/v1/admin_users?select=email&email=eq.{Uri.EscapeDataString(normalizedEmail)}&is_enabled=eq.true&limit=1");
        try
        {
            using var request = CreateRequest(HttpMethod.Get, endpoint, _options.SecretKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var admins = await JsonSerializer.DeserializeAsync<List<AdminEmailRow>>(stream, JsonOptions, cancellationToken) ?? [];
            return admins.Count > 0;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Referral admin authorization failed.");
            return false;
        }
    }

    private bool TryBuildBaseUri(out Uri baseUri)
    {
        baseUri = default!;
        if (string.IsNullOrWhiteSpace(_options.Url) ||
            !Uri.TryCreate(_options.Url, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        baseUri = parsedUri;
        return true;
    }

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= 254 &&
               normalized.Count(character => character == '@') == 1 &&
               normalized.IndexOf('@') > 0 &&
               normalized.LastIndexOf('.') > normalized.IndexOf('@') + 1
            ? normalized
            : null;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri endpoint, string apiKey)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri endpoint, string apiKey, object payload)
    {
        var request = CreateRequest(method, endpoint, apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        return request;
    }

    private sealed record AdminEmailRow(string Email);

    private sealed record ReferralSummaryResponse(
        [property: JsonPropertyName("total_referrals")] int TotalReferrals,
        [property: JsonPropertyName("total_signups")] int TotalSignups,
        [property: JsonPropertyName("items")] List<ReferralSummaryItem>? Items);

    private sealed record ReferralSummaryItem(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("referrer_name")] string ReferrerName,
        [property: JsonPropertyName("referrer_email")] string? ReferrerEmail,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("signup_count")] int SignupCount,
        [property: JsonPropertyName("last_signup_at")] DateTimeOffset? LastSignupAt);
}
