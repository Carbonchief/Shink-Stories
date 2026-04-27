using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseAbandonedCartRecoveryService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> supabaseOptions,
    IOptions<ResendOptions> resendOptions,
    ILogger<SupabaseAbandonedCartRecoveryService> logger) : IAbandonedCartRecoveryService
{
    private const string SubscriptionSourceType = "subscription";
    private const string StoreOrderSourceType = "store_order";
    private static readonly TimeSpan FirstDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan SecondDelay = TimeSpan.FromHours(24);
    private static readonly TimeSpan FinalDelay = TimeSpan.FromDays(7);

    private readonly HttpClient _httpClient = httpClient;
    private readonly SupabaseOptions _supabaseOptions = supabaseOptions.Value;
    private readonly ResendOptions _resendOptions = resendOptions.Value;
    private readonly ILogger<SupabaseAbandonedCartRecoveryService> _logger = logger;

    public async Task StartSequenceAsync(
        AbandonedCartRecoveryStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() || !IsResendConfigured() || !IsValidSourceType(request.SourceType))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.CheckoutReference) ||
            string.IsNullOrWhiteSpace(request.CustomerEmail) ||
            string.IsNullOrWhiteSpace(request.CheckoutUrl))
        {
            return;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        var apiKey = ResolveApiKey();
        var nowUtc = DateTimeOffset.UtcNow;
        var token = CreateOptOutToken();
        var recovery = await CreateRecoveryAsync(baseUri, apiKey, request, token, nowUtc, cancellationToken);
        if (recovery is null)
        {
            return;
        }

        var optOutUrl = BuildOptOutUrl(request.OptOutBaseUrl, recovery.RecoveryId, token);
        var continueUrl = BuildContinueUrl(request.OptOutBaseUrl, recovery.RecoveryId, token);
        var variables = BuildTemplateVariables(request, continueUrl, optOutUrl);
        var emailIds = await ScheduleEmailsAsync(recovery.RecoveryId, request, variables, nowUtc, cancellationToken);
        if (emailIds is null)
        {
            return;
        }

        await StoreScheduledEmailIdsAsync(baseUri, apiKey, recovery.RecoveryId, emailIds, nowUtc, cancellationToken);
    }

    public async Task ResolveByCheckoutReferenceAsync(
        string sourceType,
        string checkoutReference,
        string resolution,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() ||
            !IsResendConfigured() ||
            !IsValidSourceType(sourceType) ||
            string.IsNullOrWhiteSpace(checkoutReference))
        {
            return;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        var apiKey = ResolveApiKey();
        var recoveries = await GetActiveRecoveriesAsync(
            baseUri,
            apiKey,
            $"source_type=eq.{Uri.EscapeDataString(sourceType)}&checkout_reference=eq.{Uri.EscapeDataString(checkoutReference.Trim())}",
            cancellationToken);
        await ResolveRecoveriesAsync(baseUri, apiKey, recoveries, resolution, cancellationToken);
    }

    public async Task ResolveSubscriptionRecoveriesAsync(
        string? customerEmail,
        string? tierCode,
        string resolution,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() ||
            !IsResendConfigured() ||
            string.IsNullOrWhiteSpace(customerEmail) ||
            string.IsNullOrWhiteSpace(tierCode))
        {
            return;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return;
        }

        var apiKey = ResolveApiKey();
        var filter = string.Join(
            "&",
            $"source_type=eq.{SubscriptionSourceType}",
            $"source_key=eq.{Uri.EscapeDataString(tierCode.Trim())}",
            $"customer_email=eq.{Uri.EscapeDataString(customerEmail.Trim().ToLowerInvariant())}");
        var recoveries = await GetActiveRecoveriesAsync(baseUri, apiKey, filter, cancellationToken);
        await ResolveRecoveriesAsync(baseUri, apiKey, recoveries, resolution, cancellationToken);
    }

    public async Task<bool> OptOutAsync(
        string recoveryId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() ||
            !IsResendConfigured() ||
            string.IsNullOrWhiteSpace(recoveryId) ||
            string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (!TryBuildSupabaseBaseUri(out var baseUri))
        {
            return false;
        }

        var apiKey = ResolveApiKey();
        var filter = string.Join(
            "&",
            $"recovery_id=eq.{Uri.EscapeDataString(recoveryId.Trim())}",
            $"opt_out_token=eq.{Uri.EscapeDataString(token.Trim())}");
        var recoveries = await GetActiveRecoveriesAsync(baseUri, apiKey, filter, cancellationToken);
        if (recoveries.Count == 0)
        {
            return false;
        }

        await ResolveRecoveriesAsync(baseUri, apiKey, recoveries, "opted_out", cancellationToken);
        return true;
    }

    public async Task<AbandonedCartRecoveryRecord?> GetActiveRecoveryAsync(
        string recoveryId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupabaseConfigured() ||
            string.IsNullOrWhiteSpace(recoveryId) ||
            string.IsNullOrWhiteSpace(token) ||
            !TryBuildSupabaseBaseUri(out var baseUri))
        {
            return null;
        }

        var apiKey = ResolveApiKey();
        var filter = string.Join(
            "&",
            $"recovery_id=eq.{Uri.EscapeDataString(recoveryId.Trim())}",
            $"opt_out_token=eq.{Uri.EscapeDataString(token.Trim())}");
        var recovery = (await GetActiveRecoveriesAsync(baseUri, apiKey, filter, cancellationToken)).FirstOrDefault();
        if (recovery is null)
        {
            return null;
        }

        return new AbandonedCartRecoveryRecord(
            recovery.RecoveryId,
            recovery.SourceType,
            recovery.SourceKey,
            recovery.CheckoutReference,
            recovery.Provider,
            recovery.CustomerEmail,
            recovery.CustomerName,
            recovery.ItemName,
            recovery.ItemSummary,
            recovery.CartTotalZar);
    }

    private async Task<AbandonedRecoveryRow?> CreateRecoveryAsync(
        Uri baseUri,
        string apiKey,
        AbandonedCartRecoveryStartRequest request,
        string token,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var payload = new[]
        {
            new
            {
                source_type = request.SourceType.Trim(),
                source_key = request.SourceKey.Trim(),
                checkout_reference = request.CheckoutReference.Trim(),
                provider = request.Provider.Trim(),
                customer_email = request.CustomerEmail.Trim().ToLowerInvariant(),
                customer_name = NormalizeOptionalText(request.CustomerName, 160),
                item_name = request.ItemName.Trim(),
                item_summary = request.ItemSummary.Trim(),
                cart_total_zar = request.CartTotalZar,
                checkout_url = request.CheckoutUrl.Trim(),
                opt_out_token = token,
                first_scheduled_at = nowUtc.Add(FirstDelay).UtcDateTime,
                second_scheduled_at = nowUtc.Add(SecondDelay).UtcDateTime,
                final_scheduled_at = nowUtc.Add(FinalDelay).UtcDateTime
            }
        };

        var uri = new Uri(baseUri, "rest/v1/abandoned_cart_recoveries?on_conflict=source_type,checkout_reference&select=*");
        using var createRequest = CreateJsonRequest(HttpMethod.Post, uri, apiKey, payload, "resolution=ignore-duplicates,return=representation");
        using var response = await _httpClient.SendAsync(createRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Abandoned cart recovery create failed. source_type={SourceType} reference={Reference} status={StatusCode} body={Body}",
                request.SourceType,
                request.CheckoutReference,
                (int)response.StatusCode,
                body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<AbandonedRecoveryRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
        return rows.FirstOrDefault();
    }

    private async Task<RecoveryEmailIds?> ScheduleEmailsAsync(
        string recoveryId,
        AbandonedCartRecoveryStartRequest request,
        IReadOnlyDictionary<string, object?> variables,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var templates = _resendOptions.Templates.AbandonedCartRecovery;
        var first = await SendEmailAsync(
            request.CustomerEmail,
            templates.Hour1TemplateId,
            variables,
            nowUtc.Add(FirstDelay),
            $"abandoned-cart/{recoveryId}/hour1",
            cancellationToken);
        var second = await SendEmailAsync(
            request.CustomerEmail,
            templates.Hour24TemplateId,
            variables,
            nowUtc.Add(SecondDelay),
            $"abandoned-cart/{recoveryId}/hour24",
            cancellationToken);
        var final = await SendEmailAsync(
            request.CustomerEmail,
            templates.Day7TemplateId,
            variables,
            nowUtc.Add(FinalDelay),
            $"abandoned-cart/{recoveryId}/day7",
            cancellationToken);

        return new RecoveryEmailIds(first?.Id, second?.Id, final?.Id);
    }

    private async Task<ResendEmailResponse?> SendEmailAsync(
        string recipientEmail,
        string templateId,
        IReadOnlyDictionary<string, object?> variables,
        DateTimeOffset scheduledAtUtc,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new ResendEmailRequest(
                From: _resendOptions.FromEmail,
                To: [recipientEmail],
                Subject: null,
                Html: null,
                Text: null,
                ReplyTo: string.IsNullOrWhiteSpace(_resendOptions.ToEmail) ? null : [_resendOptions.ToEmail],
                ScheduledAt: scheduledAtUtc.UtcDateTime.ToString("O"),
                Template: new ResendTemplateRequest(templateId, variables)))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Resend rejected abandoned cart recovery email. template_id={TemplateId} status={StatusCode} body={Body}",
                templateId,
                (int)response.StatusCode,
                body);
            return null;
        }

        return JsonSerializer.Deserialize<ResendEmailResponse>(body);
    }

    private async Task StoreScheduledEmailIdsAsync(
        Uri baseUri,
        string apiKey,
        string recoveryId,
        RecoveryEmailIds emailIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            first_email_id = emailIds.FirstEmailId,
            second_email_id = emailIds.SecondEmailId,
            final_email_id = emailIds.FinalEmailId,
            updated_at = nowUtc.UtcDateTime
        };
        var uri = new Uri(baseUri, $"rest/v1/abandoned_cart_recoveries?recovery_id=eq.{Uri.EscapeDataString(recoveryId)}");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Abandoned cart recovery email id update failed. recovery_id={RecoveryId} status={StatusCode} body={Body}",
                recoveryId,
                (int)response.StatusCode,
                body);
        }
    }

    private async Task<IReadOnlyList<AbandonedRecoveryRow>> GetActiveRecoveriesAsync(
        Uri baseUri,
        string apiKey,
        string filter,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(baseUri, $"rest/v1/abandoned_cart_recoveries?select=*&resolved_at=is.null&{filter}");
        using var request = CreateRequest(HttpMethod.Get, uri, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Abandoned cart recovery lookup failed. status={StatusCode} body={Body}",
                (int)response.StatusCode,
                body);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<AbandonedRecoveryRow>>(stream, cancellationToken: cancellationToken)
            ?? [];
    }

    private async Task ResolveRecoveriesAsync(
        Uri baseUri,
        string apiKey,
        IReadOnlyList<AbandonedRecoveryRow> recoveries,
        string resolution,
        CancellationToken cancellationToken)
    {
        foreach (var recovery in recoveries)
        {
            await CancelScheduledEmailAsync(recovery.FirstEmailId, cancellationToken);
            await CancelScheduledEmailAsync(recovery.SecondEmailId, cancellationToken);
            await CancelScheduledEmailAsync(recovery.FinalEmailId, cancellationToken);
            await MarkResolvedAsync(baseUri, apiKey, recovery.RecoveryId, resolution, cancellationToken);
        }
    }

    private async Task CancelScheduledEmailAsync(string? emailId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailId))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.resend.com/emails/{Uri.EscapeDataString(emailId)}/cancel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Abandoned cart scheduled email cancel failed. email_id={EmailId} status={StatusCode} body={Body}",
            emailId,
            (int)response.StatusCode,
            body);
    }

    private async Task MarkResolvedAsync(
        Uri baseUri,
        string apiKey,
        string recoveryId,
        string resolution,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            resolved_at = DateTimeOffset.UtcNow.UtcDateTime,
            resolution
        };
        var uri = new Uri(baseUri, $"rest/v1/abandoned_cart_recoveries?recovery_id=eq.{Uri.EscapeDataString(recoveryId)}");
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), uri, apiKey, payload, "return=minimal");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Abandoned cart recovery resolve failed. recovery_id={RecoveryId} status={StatusCode} body={Body}",
                recoveryId,
                (int)response.StatusCode,
                body);
        }
    }

    private bool IsSupabaseConfigured() =>
        !string.IsNullOrWhiteSpace(_supabaseOptions.Url) &&
        !string.IsNullOrWhiteSpace(_supabaseOptions.ServiceRoleKey);

    private bool IsResendConfigured()
    {
        var templates = _resendOptions.Templates.AbandonedCartRecovery;
        return !string.IsNullOrWhiteSpace(_resendOptions.ApiKey) &&
               !string.IsNullOrWhiteSpace(_resendOptions.FromEmail) &&
               !string.IsNullOrWhiteSpace(templates.Hour1TemplateId) &&
               !string.IsNullOrWhiteSpace(templates.Hour24TemplateId) &&
               !string.IsNullOrWhiteSpace(templates.Day7TemplateId);
    }

    private static bool IsValidSourceType(string sourceType) =>
        string.Equals(sourceType, SubscriptionSourceType, StringComparison.Ordinal) ||
        string.Equals(sourceType, StoreOrderSourceType, StringComparison.Ordinal);

    private Dictionary<string, object?> BuildTemplateVariables(
        AbandonedCartRecoveryStartRequest request,
        string continueUrl,
        string optOutUrl) =>
        new(StringComparer.Ordinal)
        {
            ["CUSTOMER_NAME"] = string.IsNullOrWhiteSpace(request.CustomerName) ? "daar" : request.CustomerName.Trim(),
            ["ITEM_NAME"] = request.ItemName,
            ["ITEM_SUMMARY"] = request.ItemSummary,
            ["CART_TOTAL"] = request.CartTotalZar is null
                ? string.Empty
                : $"R {request.CartTotalZar.Value.ToString("0.00", CultureInfo.InvariantCulture)}",
            ["CHECKOUT_URL"] = continueUrl,
            ["OPTOUT_URL"] = optOutUrl,
            ["SUPPORT_EMAIL"] = string.IsNullOrWhiteSpace(_resendOptions.ToEmail)
                ? "vanderwaltluan@gmail.com"
                : _resendOptions.ToEmail
        };

    private static string BuildOptOutUrl(string baseUrl, string recoveryId, string token)
    {
        var fallbackBaseUrl = Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl)
            ? parsedBaseUrl
            : new Uri("https://schink.prioritybit.co.za");
        return new Uri(
            fallbackBaseUrl,
            $"/betaalherinneringe/stop?id={Uri.EscapeDataString(recoveryId)}&token={Uri.EscapeDataString(token)}").ToString();
    }

    private static string BuildContinueUrl(string baseUrl, string recoveryId, string token)
    {
        var fallbackBaseUrl = Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl)
            ? parsedBaseUrl
            : new Uri("https://schink.prioritybit.co.za");
        return new Uri(
            fallbackBaseUrl,
            $"/betaalherinneringe/gaan?id={Uri.EscapeDataString(recoveryId)}&token={Uri.EscapeDataString(token)}").ToString();
    }

    private static string CreateOptOutToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, string apiKey, object payload, string preferHeader)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("Prefer", preferHeader);
        return request;
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri)
    {
        baseUri = default!;
        return Uri.TryCreate(_supabaseOptions.Url, UriKind.Absolute, out baseUri!);
    }

    private string ResolveApiKey() => _supabaseOptions.ServiceRoleKey;

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private sealed class AbandonedRecoveryRow
    {
        [JsonPropertyName("recovery_id")]
        public string RecoveryId { get; set; } = string.Empty;

        [JsonPropertyName("source_type")]
        public string SourceType { get; set; } = string.Empty;

        [JsonPropertyName("source_key")]
        public string SourceKey { get; set; } = string.Empty;

        [JsonPropertyName("checkout_reference")]
        public string CheckoutReference { get; set; } = string.Empty;

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("customer_email")]
        public string CustomerEmail { get; set; } = string.Empty;

        [JsonPropertyName("customer_name")]
        public string? CustomerName { get; set; }

        [JsonPropertyName("item_name")]
        public string ItemName { get; set; } = string.Empty;

        [JsonPropertyName("item_summary")]
        public string ItemSummary { get; set; } = string.Empty;

        [JsonPropertyName("cart_total_zar")]
        public decimal? CartTotalZar { get; set; }

        [JsonPropertyName("first_email_id")]
        public string? FirstEmailId { get; set; }

        [JsonPropertyName("second_email_id")]
        public string? SecondEmailId { get; set; }

        [JsonPropertyName("final_email_id")]
        public string? FinalEmailId { get; set; }
    }

    private sealed record RecoveryEmailIds(
        string? FirstEmailId,
        string? SecondEmailId,
        string? FinalEmailId);

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
        [property: JsonPropertyName("html"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Html,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? ReplyTo,
        [property: JsonPropertyName("scheduled_at")] string ScheduledAt,
        [property: JsonPropertyName("template")] ResendTemplateRequest Template);

    private sealed record ResendTemplateRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, object?> Variables);

    private sealed record ResendEmailResponse(
        [property: JsonPropertyName("id")] string Id);
}
