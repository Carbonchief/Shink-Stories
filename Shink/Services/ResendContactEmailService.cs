using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class ResendContactEmailService(
    HttpClient httpClient,
    IOptions<ResendOptions> resendOptions,
    ILogger<ResendContactEmailService> logger) : IContactEmailService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ResendOptions _options = resendOptions.Value;
    private readonly ILogger<ResendContactEmailService> _logger = logger;

    public async Task SendContactEmailAsync(ContactFormSubmission submission, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.FromEmail) ||
            string.IsNullOrWhiteSpace(_options.ToEmail))
        {
            throw new InvalidOperationException("Resend is not configured.");
        }

        var encodedName = HtmlEncoder.Default.Encode(submission.Name);
        var encodedEmail = HtmlEncoder.Default.Encode(submission.Email);
        var encodedSubject = HtmlEncoder.Default.Encode(submission.Subject);
        var encodedMessage = HtmlEncoder.Default.Encode(submission.Message).Replace("\n", "<br />");

        var request = new ResendEmailRequest(
            From: _options.FromEmail,
            To: [_options.ToEmail],
            Subject: $"Kontakvorm: {submission.Subject}",
            Html: $"""
                   <h2>Nuwe boodskap vanaf Schink kontakvorm</h2>
                   <p><strong>Naam:</strong> {encodedName}</p>
                   <p><strong>E-pos:</strong> {encodedEmail}</p>
                   <p><strong>Onderwerp:</strong> {encodedSubject}</p>
                   <p><strong>Boodskap:</strong><br />{encodedMessage}</p>
                   """,
            Text: $"Naam: {submission.Name}\nE-pos: {submission.Email}\nOnderwerp: {submission.Subject}\n\n{submission.Message}",
            ReplyTo: submission.Email);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Resend rejected contact email: {StatusCode} {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException("Failed to send contact email.");
        }

        await SendContactAutoReplyAsync(submission, cancellationToken);
    }

    private async Task SendContactAutoReplyAsync(ContactFormSubmission submission, CancellationToken cancellationToken)
    {
        var templateId = _options.Templates.Contact.AutoReplyTemplateId;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            _logger.LogWarning("Contact auto-reply skipped: Resend template is not configured.");
            return;
        }

        if (string.IsNullOrWhiteSpace(submission.Email))
        {
            _logger.LogWarning("Contact auto-reply skipped: submission email is missing.");
            return;
        }

        var contactName = string.IsNullOrWhiteSpace(submission.Name) ? "daar" : submission.Name.Trim();
        var contactSubject = string.IsNullOrWhiteSpace(submission.Subject) ? "jou boodskap" : submission.Subject.Trim();
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CONTACT_NAME_HTML"] = HtmlEncoder.Default.Encode(contactName),
            ["CONTACT_NAME_TEXT"] = contactName,
            ["CONTACT_SUBJECT_HTML"] = HtmlEncoder.Default.Encode(contactSubject),
            ["CONTACT_SUBJECT_TEXT"] = contactSubject,
            ["SUPPORT_EMAIL"] = ResolveSupportEmail(),
            ["SITE_URL"] = ResolveSiteUrl()
        };

        var request = new ResendTemplateEmailRequest(
            From: _options.FromEmail,
            To: [submission.Email],
            Subject: null,
            Html: null,
            Text: null,
            ReplyTo: string.IsNullOrWhiteSpace(_options.ToEmail) ? null : [_options.ToEmail],
            Template: new ResendTemplateRequest(templateId, variables));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", BuildContactAutoReplyIdempotencyKey(submission));

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Resend rejected contact auto-reply. template_id={TemplateId} status={StatusCode} body={Body}",
                templateId,
                (int)response.StatusCode,
                errorBody);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Contact auto-reply failed unexpectedly.");
        }
    }

    private string ResolveSupportEmail() =>
        string.IsNullOrWhiteSpace(_options.ToEmail)
            ? "support@example.com"
            : _options.ToEmail.Trim();

    private string ResolveSiteUrl()
    {
        if (Uri.TryCreate(_options.BillingManageUrl, UriKind.Absolute, out var billingUri))
        {
            return $"{billingUri.Scheme}://{billingUri.Authority}";
        }

        return "https://schink.prioritybit.co.za";
    }

    private static string BuildContactAutoReplyIdempotencyKey(ContactFormSubmission submission)
    {
        var normalized = string.Join(
            "\n",
            submission.Email.Trim().ToLowerInvariant(),
            submission.Name.Trim(),
            submission.Subject.Trim(),
            submission.Message.Replace("\r\n", "\n", StringComparison.Ordinal).Trim());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"contact-auto-reply/{hash[..32]}";
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("reply_to")] string ReplyTo);

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
