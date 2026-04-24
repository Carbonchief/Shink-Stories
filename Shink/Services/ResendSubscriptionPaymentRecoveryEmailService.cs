using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class ResendSubscriptionPaymentRecoveryEmailService(
    HttpClient httpClient,
    IOptions<ResendOptions> resendOptions,
    IOptions<PayFastOptions> payFastOptions,
    IOptions<PaystackOptions> paystackOptions,
    ILogger<ResendSubscriptionPaymentRecoveryEmailService> logger) : ISubscriptionPaymentRecoveryEmailService
{
    private static readonly TimeSpan WarningOffset = TimeSpan.FromDays(2);
    private static readonly TimeSpan SuspensionOffset = TimeSpan.FromDays(4);

    private readonly HttpClient _httpClient = httpClient;
    private readonly ResendOptions _resendOptions = resendOptions.Value;
    private readonly PayFastOptions _payFastOptions = payFastOptions.Value;
    private readonly PaystackOptions _paystackOptions = paystackOptions.Value;
    private readonly ILogger<ResendSubscriptionPaymentRecoveryEmailService> _logger = logger;

    public async Task<SubscriptionPaymentRecoveryEmailSequence?> ScheduleSequenceAsync(
        SubscriptionPaymentRecoveryEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured() || string.IsNullOrWhiteSpace(request.Email))
        {
            _logger.LogWarning(
                "Subscription payment recovery emails skipped. configured={IsConfigured} subscription_id={SubscriptionId}",
                IsConfigured(),
                request.SubscriptionId);
            return null;
        }

        var manageUrl = ResolveManageUrl();
        var displayName = ResolveGreetingName(request);

        var immediate = await SendEmailAsync(
            request.Email,
            BuildImmediateEmail(displayName, manageUrl),
            $"{request.RecoveryId}:day1",
            cancellationToken);

        var warning = await SendEmailAsync(
            request.Email,
            BuildWarningEmail(displayName, manageUrl, request.FirstFailedAtUtc),
            $"{request.RecoveryId}:day3",
            cancellationToken);

        var suspension = await SendEmailAsync(
            request.Email,
            BuildSuspensionEmail(displayName, manageUrl, request.FirstFailedAtUtc),
            $"{request.RecoveryId}:day5",
            cancellationToken);

        return new SubscriptionPaymentRecoveryEmailSequence(
            immediate?.Id,
            warning?.Id,
            suspension?.Id);
    }

    public async Task CancelSequenceAsync(
        SubscriptionPaymentRecoveryEmailSequence sequence,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            return;
        }

        await CancelScheduledEmailAsync(sequence.WarningEmailId, cancellationToken);
        await CancelScheduledEmailAsync(sequence.SuspensionEmailId, cancellationToken);
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_resendOptions.ApiKey) &&
        !string.IsNullOrWhiteSpace(_resendOptions.FromEmail) &&
        !string.IsNullOrWhiteSpace(_resendOptions.Templates.SubscriptionPaymentRecovery.Day1TemplateId) &&
        !string.IsNullOrWhiteSpace(_resendOptions.Templates.SubscriptionPaymentRecovery.Day3TemplateId) &&
        !string.IsNullOrWhiteSpace(_resendOptions.Templates.SubscriptionPaymentRecovery.Day5TemplateId);

    private string ResolveGreetingName(SubscriptionPaymentRecoveryEmailRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FirstName))
        {
            return request.FirstName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return request.DisplayName.Trim();
        }

        return "daar";
    }

    private string? ResolveManageUrl()
    {
        if (Uri.TryCreate(_resendOptions.BillingManageUrl, UriKind.Absolute, out var configuredBillingUrl))
        {
            return configuredBillingUrl.ToString();
        }

        if (Uri.TryCreate(_paystackOptions.PublicBaseUrl, UriKind.Absolute, out var paystackBase))
        {
            return new Uri(paystackBase, "/intekening-en-betaling").ToString();
        }

        if (Uri.TryCreate(_payFastOptions.PublicBaseUrl, UriKind.Absolute, out var payFastBase))
        {
            return new Uri(payFastBase, "/intekening-en-betaling").ToString();
        }

        return null;
    }

    private RecoveryEmailDefinition BuildImmediateEmail(
        string customerName,
        string? manageUrl)
        => new(
            TemplateId: _resendOptions.Templates.SubscriptionPaymentRecovery.Day1TemplateId,
            Variables: BuildTemplateVariables(customerName, manageUrl),
            ScheduledAtUtc: null);

    private RecoveryEmailDefinition BuildWarningEmail(
        string customerName,
        string? manageUrl,
        DateTimeOffset firstFailedAtUtc)
        => new(
            TemplateId: _resendOptions.Templates.SubscriptionPaymentRecovery.Day3TemplateId,
            Variables: BuildTemplateVariables(customerName, manageUrl),
            ScheduledAtUtc: firstFailedAtUtc.Add(WarningOffset));

    private RecoveryEmailDefinition BuildSuspensionEmail(
        string customerName,
        string? manageUrl,
        DateTimeOffset firstFailedAtUtc)
        => new(
            TemplateId: _resendOptions.Templates.SubscriptionPaymentRecovery.Day5TemplateId,
            Variables: BuildTemplateVariables(customerName, manageUrl),
            ScheduledAtUtc: firstFailedAtUtc.Add(SuspensionOffset));

    private static Dictionary<string, object?> BuildTemplateVariables(string customerName, string? manageUrl) =>
        new(StringComparer.Ordinal)
        {
            ["CUSTOMER_NAME"] = string.IsNullOrWhiteSpace(customerName) ? "daar" : customerName,
            ["BILLING_URL"] = string.IsNullOrWhiteSpace(manageUrl) ? "https://example.com/intekening-en-betaling" : manageUrl
        };

    private async Task<ResendEmailResponse?> SendEmailAsync(
        string recipientEmail,
        RecoveryEmailDefinition definition,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
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
                    ScheduledAt: definition.ScheduledAtUtc?.UtcDateTime.ToString("O"),
                    Template: new ResendTemplateRequest(
                        definition.TemplateId,
                        definition.Variables)))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Resend rejected subscription recovery email. template_id={TemplateId} status={StatusCode} body={Body}",
                    definition.TemplateId,
                    (int)response.StatusCode,
                    body);
                return null;
            }

            return System.Text.Json.JsonSerializer.Deserialize<ResendEmailResponse>(body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(exception, "Resend subscription recovery email failed. template_id={TemplateId}", definition.TemplateId);
            return null;
        }
    }

    private async Task CancelScheduledEmailAsync(string? emailId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailId))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.resend.com/emails/{Uri.EscapeDataString(emailId)}/cancel");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Resend scheduled email cancel failed. email_id={EmailId} status={StatusCode} body={Body}",
                emailId,
                (int)response.StatusCode,
                body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Resend scheduled email cancel failed unexpectedly. email_id={EmailId}", emailId);
        }
    }

    private sealed record RecoveryEmailDefinition(
        string TemplateId,
        IReadOnlyDictionary<string, object?> Variables,
        DateTimeOffset? ScheduledAtUtc);

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
        [property: JsonPropertyName("html"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Html,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? ReplyTo,
        [property: JsonPropertyName("scheduled_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ScheduledAt,
        [property: JsonPropertyName("template")] ResendTemplateRequest Template);

    private sealed record ResendTemplateRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, object?> Variables);

    private sealed record ResendEmailResponse(
        [property: JsonPropertyName("id")] string Id);
}
