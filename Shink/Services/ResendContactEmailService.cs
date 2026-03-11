using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
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
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Resend rejected contact email: {StatusCode} {Body}", (int)response.StatusCode, errorBody);
        throw new InvalidOperationException("Failed to send contact email.");
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("reply_to")] string ReplyTo);
}
