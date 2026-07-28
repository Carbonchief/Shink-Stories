using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class ResendSchoolSeatNotificationEmailService(
    HttpClient httpClient,
    IOptions<ResendOptions> resendOptions,
    IOptions<SiteOptions> siteOptions,
    ILogger<ResendSchoolSeatNotificationEmailService> logger) : ISchoolSeatNotificationEmailService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ResendOptions _resendOptions = resendOptions.Value;
    private readonly SiteOptions _siteOptions = siteOptions.Value;
    private readonly ILogger<ResendSchoolSeatNotificationEmailService> _logger = logger;

    public async Task SendSeatAssignedEmailAsync(
        SchoolSeatAssignmentEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var templateId = _resendOptions.Templates.SchoolSeatNotifications.SeatAssignedTemplateId;
        if (string.IsNullOrWhiteSpace(_resendOptions.ApiKey) ||
            string.IsNullOrWhiteSpace(_resendOptions.FromEmail) ||
            string.IsNullOrWhiteSpace(templateId) ||
            string.IsNullOrWhiteSpace(request.RecipientEmail) ||
            string.IsNullOrWhiteSpace(request.PasswordSetupUrl))
        {
            _logger.LogWarning("School seat assignment email skipped: Resend template is not configured.");
            return;
        }

        var recipientName = string.IsNullOrWhiteSpace(request.RecipientName)
            ? "Juffrou"
            : request.RecipientName.Trim();
        var schoolName = string.IsNullOrWhiteSpace(request.SchoolName)
            ? "jou skool"
            : request.SchoolName.Trim();
        var siteUrl = ResolveSiteUrl();

        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RECIPIENT_NAME_HTML"] = Html(recipientName),
            ["RECIPIENT_NAME_TEXT"] = recipientName,
            ["SCHOOL_NAME_HTML"] = Html(schoolName),
            ["SCHOOL_NAME_TEXT"] = schoolName,
            ["PASSWORD_SETUP_URL_HTML"] = Html(request.PasswordSetupUrl.Trim()),
            ["PASSWORD_SETUP_URL_TEXT"] = request.PasswordSetupUrl.Trim(),
            ["LISTEN_URL_HTML"] = Html(BuildSiteUrl("/luister")),
            ["LISTEN_URL_TEXT"] = BuildSiteUrl("/luister"),
            ["RESOURCES_URL_HTML"] = Html(BuildSiteUrl("/hulpbronne")),
            ["RESOURCES_URL_TEXT"] = BuildSiteUrl("/hulpbronne"),
            ["SUPPORT_EMAIL_HTML"] = Html(ResolveSupportEmail()),
            ["SUPPORT_EMAIL_TEXT"] = ResolveSupportEmail(),
            ["SITE_URL_HTML"] = Html(siteUrl),
            ["SITE_URL_TEXT"] = siteUrl
        };

        var emailRequest = new ResendTemplateEmailRequest(
            From: _resendOptions.FromEmail,
            To: [request.RecipientEmail.Trim()],
            Subject: null,
            Html: null,
            Text: null,
            ReplyTo: string.IsNullOrWhiteSpace(_resendOptions.ToEmail) ? null : [_resendOptions.ToEmail],
            Template: new ResendTemplateRequest(templateId, variables));

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
            {
                Content = JsonContent.Create(emailRequest)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);
            httpRequest.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                $"school-seat-assigned/{request.RecipientEmail.Trim().ToLowerInvariant()}/{ComputeIdempotencySuffix(request)}");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Resend rejected school seat assignment email. template_id={TemplateId} status={StatusCode} body={Body}",
                templateId,
                (int)response.StatusCode,
                body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(
                exception,
                "Resend school seat assignment email failed unexpectedly. template_id={TemplateId}",
                templateId);
        }
    }

    private string ResolveSupportEmail() =>
        string.IsNullOrWhiteSpace(_resendOptions.ToEmail)
            ? "hello@schink.co.za"
            : _resendOptions.ToEmail.Trim();

    private string ResolveSiteUrl()
    {
        if (Uri.TryCreate(_siteOptions.PublicBaseUrl, UriKind.Absolute, out var siteUri))
        {
            return siteUri.ToString().TrimEnd('/');
        }

        return "https://www.schink.co.za";
    }

    private string BuildSiteUrl(string path) =>
        $"{ResolveSiteUrl()}/{path.TrimStart('/')}";

    private static string Html(string value) => HtmlEncoder.Default.Encode(value);

    private static string ComputeIdempotencySuffix(SchoolSeatAssignmentEmailRequest request)
    {
        var input = string.Join(
            "\n",
            request.RecipientEmail.Trim().ToLowerInvariant(),
            request.PasswordSetupUrl.Trim());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input)))[..32].ToLowerInvariant();
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
