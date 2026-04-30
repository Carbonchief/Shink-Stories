using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class ResendSubscriptionNotificationEmailService(
    HttpClient httpClient,
    IOptions<ResendOptions> resendOptions,
    ILogger<ResendSubscriptionNotificationEmailService> logger) : ISubscriptionNotificationEmailService
{
    private static readonly CultureInfo AfrikaansCulture = CultureInfo.GetCultureInfo("af-ZA");

    private readonly HttpClient _httpClient = httpClient;
    private readonly ResendOptions _options = resendOptions.Value;
    private readonly ILogger<ResendSubscriptionNotificationEmailService> _logger = logger;

    public async Task SendSubscriptionConfirmationAsync(
        SubscriptionConfirmationEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var templateId = _options.Templates.SubscriptionNotifications.ConfirmationTemplateId;
        if (!IsCustomerTemplateConfigured(templateId, request.Email, "subscription confirmation"))
        {
            return;
        }

        var customerName = ResolveCustomerName(request.FirstName, request.DisplayName);
        var planName = string.IsNullOrWhiteSpace(request.PlanName) ? "Schink Stories" : request.PlanName.Trim();
        var billingLabel = FormatBillingLabel(request.BillingPeriodMonths);
        var paymentProvider = string.IsNullOrWhiteSpace(request.Provider) ? "betaling" : request.Provider.Trim();
        var paymentReference = string.IsNullOrWhiteSpace(request.PaymentReference) ? "nie beskikbaar" : request.PaymentReference.Trim();

        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CUSTOMER_NAME_HTML"] = Html(customerName),
            ["CUSTOMER_NAME_TEXT"] = customerName,
            ["PLAN_NAME_HTML"] = Html(planName),
            ["PLAN_NAME_TEXT"] = planName,
            ["AMOUNT"] = FormatZar(request.AmountZar),
            ["BILLING_LABEL_HTML"] = Html(billingLabel),
            ["BILLING_LABEL_TEXT"] = billingLabel,
            ["NEXT_RENEWAL_DATE"] = FormatDate(request.NextRenewalAtUtc, "sien jou rekeningblad"),
            ["PAYMENT_PROVIDER_HTML"] = Html(paymentProvider),
            ["PAYMENT_PROVIDER_TEXT"] = paymentProvider,
            ["PAYMENT_REFERENCE_HTML"] = Html(paymentReference),
            ["PAYMENT_REFERENCE_TEXT"] = paymentReference,
            ["BILLING_URL"] = ResolveBillingUrl(),
            ["SUPPORT_EMAIL"] = ResolveSupportEmail()
        };

        await SendTemplateEmailAsync(
            to: request.Email,
            templateId,
            variables,
            idempotencyKey: $"subscription-confirmation/{HashKey(request.SubscriptionId)}",
            logContext: "subscription confirmation",
            cancellationToken);
    }

    public async Task SendSubscriptionEndedAsync(
        SubscriptionEndedEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var templateId = _options.Templates.SubscriptionNotifications.EndedTemplateId;
        if (!IsCustomerTemplateConfigured(templateId, request.Email, "subscription ended confirmation"))
        {
            return;
        }

        var customerName = ResolveCustomerName(request.FirstName, request.DisplayName);
        var planName = string.IsNullOrWhiteSpace(request.PlanName) ? "Schink Stories" : request.PlanName.Trim();
        var statusLabel = string.IsNullOrWhiteSpace(request.StatusLabel) ? "opgedateer" : request.StatusLabel.Trim();
        var accessMessage = string.IsNullOrWhiteSpace(request.AccessMessage)
            ? "Jou gratis stories bly beskikbaar."
            : request.AccessMessage.Trim();

        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CUSTOMER_NAME_HTML"] = Html(customerName),
            ["CUSTOMER_NAME_TEXT"] = customerName,
            ["PLAN_NAME_HTML"] = Html(planName),
            ["PLAN_NAME_TEXT"] = planName,
            ["STATUS_LABEL_HTML"] = Html(statusLabel),
            ["STATUS_LABEL_TEXT"] = statusLabel,
            ["ACCESS_MESSAGE_HTML"] = Html(accessMessage),
            ["ACCESS_MESSAGE_TEXT"] = accessMessage,
            ["ENDED_AT"] = FormatDate(request.EndedAtUtc, "vandag"),
            ["BILLING_URL"] = ResolveBillingUrl(),
            ["SUPPORT_EMAIL"] = ResolveSupportEmail()
        };

        await SendTemplateEmailAsync(
            to: request.Email,
            templateId,
            variables,
            idempotencyKey: $"subscription-ended/{HashKey($"{request.SubscriptionId}/{request.IdempotencySuffix}")}",
            logContext: "subscription ended confirmation",
            cancellationToken);
    }

    public async Task SendAdminOpsAlertAsync(
        AdminOpsAlertEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var templateId = _options.Templates.AdminOps.AlertTemplateId;
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.FromEmail) ||
            string.IsNullOrWhiteSpace(_options.ToEmail) ||
            string.IsNullOrWhiteSpace(templateId))
        {
            _logger.LogWarning("Admin ops alert skipped: Resend is not configured.");
            return;
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? "Operasionele waarskuwing" : request.Title.Trim();
        var summary = string.IsNullOrWhiteSpace(request.Summary)
            ? "Nuwe Schink Stories operasionele gebeurtenis."
            : request.Summary.Trim();
        var details = string.IsNullOrWhiteSpace(request.Details) ? "Geen ekstra besonderhede." : request.Details.Trim();
        var eventReference = string.IsNullOrWhiteSpace(request.EventReference) ? "nie beskikbaar" : request.EventReference.Trim();

        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ALERT_TITLE_HTML"] = Html(title),
            ["ALERT_TITLE_TEXT"] = title,
            ["ALERT_SEVERITY"] = string.IsNullOrWhiteSpace(request.Severity) ? "info" : request.Severity.Trim(),
            ["ALERT_SUMMARY_HTML"] = Html(summary),
            ["ALERT_SUMMARY_TEXT"] = summary,
            ["ALERT_DETAILS_HTML"] = HtmlMultiline(details),
            ["ALERT_DETAILS_TEXT"] = details,
            ["EVENT_REFERENCE_HTML"] = Html(eventReference),
            ["EVENT_REFERENCE_TEXT"] = eventReference,
            ["OCCURRED_AT"] = FormatDateTime(request.OccurredAtUtc),
            ["ACTION_URL"] = string.IsNullOrWhiteSpace(request.ActionUrl) ? ResolveAdminUrl() : request.ActionUrl.Trim()
        };

        await SendTemplateEmailAsync(
            to: _options.ToEmail,
            templateId,
            variables,
            idempotencyKey: $"admin-ops-alert/{HashKey(request.AlertKey)}",
            logContext: "admin ops alert",
            cancellationToken);
    }

    private bool IsCustomerTemplateConfigured(string templateId, string recipientEmail, string logContext)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.FromEmail) ||
            string.IsNullOrWhiteSpace(templateId))
        {
            _logger.LogWarning("{LogContext} skipped: Resend template is not configured.", logContext);
            return false;
        }

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning("{LogContext} skipped: recipient email is missing.", logContext);
            return false;
        }

        return true;
    }

    private async Task SendTemplateEmailAsync(
        string to,
        string templateId,
        IReadOnlyDictionary<string, object?> variables,
        string idempotencyKey,
        string logContext,
        CancellationToken cancellationToken)
    {
        var request = new ResendTemplateEmailRequest(
            From: _options.FromEmail,
            To: [to],
            Subject: null,
            Html: null,
            Text: null,
            ReplyTo: string.IsNullOrWhiteSpace(_options.ToEmail) ? null : [_options.ToEmail],
            Template: new ResendTemplateRequest(templateId, variables));

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            _logger.LogWarning(
                "Resend rejected {LogContext}. template_id={TemplateId} status={StatusCode} body={Body}",
                logContext,
                templateId,
                (int)response.StatusCode,
                body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(exception, "Resend {LogContext} failed unexpectedly. template_id={TemplateId}", logContext, templateId);
        }
    }

    private static string ResolveCustomerName(string? firstName, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(firstName))
        {
            return firstName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return "daar";
    }

    private static string FormatZar(decimal? value) =>
        value is decimal amount
            ? $"R {amount.ToString("0.00", CultureInfo.InvariantCulture)}"
            : "sien jou rekeningblad";

    private static string FormatBillingLabel(int billingPeriodMonths) =>
        billingPeriodMonths switch
        {
            1 => "maandeliks",
            12 => "jaarliks",
            > 1 => $"elke {billingPeriodMonths} maande",
            _ => "jou plan"
        };

    private static string FormatDate(DateTimeOffset? value, string fallback) =>
        value is DateTimeOffset date
            ? ToSouthAfricaTime(date).ToString("dd MMMM yyyy", AfrikaansCulture)
            : fallback;

    private static string FormatDateTime(DateTimeOffset value) =>
        ToSouthAfricaTime(value).ToString("dd MMMM yyyy HH:mm", AfrikaansCulture);

    private static DateTimeOffset ToSouthAfricaTime(DateTimeOffset value)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
            return TimeZoneInfo.ConvertTime(value, timeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return value.ToOffset(TimeSpan.FromHours(2));
        }
        catch (InvalidTimeZoneException)
        {
            return value.ToOffset(TimeSpan.FromHours(2));
        }
    }

    private string ResolveSupportEmail() =>
        string.IsNullOrWhiteSpace(_options.ToEmail)
            ? "support@example.com"
            : _options.ToEmail.Trim();

    private string ResolveBillingUrl() =>
        Uri.TryCreate(_options.BillingManageUrl, UriKind.Absolute, out var billingUri)
            ? billingUri.ToString()
            : "https://www.schink.co.za/intekening-en-betaling";

    private string ResolveAdminUrl() =>
        Uri.TryCreate(ResolveBillingUrl(), UriKind.Absolute, out var billingUri)
            ? new Uri(billingUri, "/admin").ToString()
            : "https://www.schink.co.za/admin";

    private static string Html(string value) =>
        HtmlEncoder.Default.Encode(value);

    private static string HtmlMultiline(string value) =>
        HtmlEncoder.Default.Encode(value).Replace("\n", "<br />", StringComparison.Ordinal);

    private static string HashKey(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()[..32];
    }

    private sealed record ResendTemplateEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
        [property: JsonPropertyName("html"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Html,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? ReplyTo,
        [property: JsonPropertyName("template")] ResendTemplateRequest Template);

    private sealed record ResendTemplateRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, object?> Variables);
}
