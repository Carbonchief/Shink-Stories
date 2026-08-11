using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class ResendGratisSubscriberEmailSequenceService(
    HttpClient httpClient,
    IOptions<ResendOptions> options,
    ILogger<ResendGratisSubscriberEmailSequenceService> logger) : IGratisSubscriberEmailSequenceService
{
    internal const string SequenceStartedEventName = "schink.gratis_sequence.started";
    internal const string AccessPropertyName = "schink_access";
    internal const string GratisAccessValue = "gratis";
    internal const string PaidAccessValue = "paid";

    private const string ApiBaseUrl = "https://api.resend.com";
    private readonly HttpClient _httpClient = httpClient;
    private readonly ResendOptions _options = options.Value;
    private readonly ILogger<ResendGratisSubscriberEmailSequenceService> _logger = logger;

    public async Task<GratisSubscriberSequenceStartResult> TryStartAsync(
        string? email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null || !IsConfigured())
        {
            return GratisSubscriberSequenceStartResult.Failed;
        }

        try
        {
            var contact = await GetContactAsync(normalizedEmail, cancellationToken);
            if (contact.IsFailure)
            {
                return GratisSubscriberSequenceStartResult.Failed;
            }

            if (contact.Exists && contact.Unsubscribed)
            {
                return GratisSubscriberSequenceStartResult.SkippedUnsubscribed;
            }

            var contactReady = contact.Exists
                ? await UpdateContactAsync(normalizedEmail, firstName, lastName, GratisAccessValue, cancellationToken)
                : await CreateContactAsync(normalizedEmail, firstName, lastName, cancellationToken);
            if (!contactReady)
            {
                return GratisSubscriberSequenceStartResult.Failed;
            }

            var eventSent = await SendSequenceStartedEventAsync(
                normalizedEmail,
                firstName,
                lastName,
                cancellationToken);
            return eventSent
                ? GratisSubscriberSequenceStartResult.Started
                : GratisSubscriberSequenceStartResult.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            _logger.LogWarning(exception, "Resend gratis subscriber sequence enrollment failed.");
            return GratisSubscriberSequenceStartResult.Failed;
        }
    }

    public async Task<bool> MarkPaidAsync(
        string? email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null || !IsConfigured())
        {
            return false;
        }

        try
        {
            var contact = await GetContactAsync(normalizedEmail, cancellationToken);
            if (contact.IsFailure)
            {
                return false;
            }

            if (!contact.Exists)
            {
                return true;
            }

            return await UpdateContactAsync(
                normalizedEmail,
                firstName: null,
                lastName: null,
                PaidAccessValue,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            _logger.LogWarning(exception, "Resend gratis subscriber paid-status sync failed.");
            return false;
        }
    }

    private async Task<ContactLookup> GetContactAsync(string email, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"{ApiBaseUrl}/contacts/{Uri.EscapeDataString(email)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ContactLookup(false, false);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Resend contact lookup failed with status {StatusCode}.",
                (int)response.StatusCode);
            return new ContactLookup(false, false, IsFailure: true);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var unsubscribed = document.RootElement.TryGetProperty("unsubscribed", out var unsubscribedElement) &&
                           unsubscribedElement.ValueKind == JsonValueKind.True;
        return new ContactLookup(true, unsubscribed);
    }

    private async Task<bool> CreateContactAsync(
        string email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["first_name"] = NormalizeName(firstName),
            ["last_name"] = NormalizeName(lastName),
            ["unsubscribed"] = false,
            ["properties"] = new Dictionary<string, string>
            {
                [AccessPropertyName] = GratisAccessValue
            }
        };

        using var request = CreateJsonRequest(HttpMethod.Post, $"{ApiBaseUrl}/contacts", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var contact = await GetContactAsync(email, cancellationToken);
            return contact.Exists &&
                   !contact.Unsubscribed &&
                   await UpdateContactAsync(email, firstName, lastName, GratisAccessValue, cancellationToken);
        }

        _logger.LogWarning(
            "Resend contact creation failed with status {StatusCode}.",
            (int)response.StatusCode);
        return false;
    }

    private async Task<bool> UpdateContactAsync(
        string email,
        string? firstName,
        string? lastName,
        string access,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["properties"] = new Dictionary<string, string>
            {
                [AccessPropertyName] = access
            }
        };
        if (!string.IsNullOrWhiteSpace(firstName))
        {
            payload["first_name"] = NormalizeName(firstName);
        }

        if (!string.IsNullOrWhiteSpace(lastName))
        {
            payload["last_name"] = NormalizeName(lastName);
        }

        using var request = CreateJsonRequest(
            HttpMethod.Patch,
            $"{ApiBaseUrl}/contacts/{Uri.EscapeDataString(email)}",
            payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning(
            "Resend contact update failed with status {StatusCode}.",
            (int)response.StatusCode);
        return false;
    }

    private async Task<bool> SendSequenceStartedEventAsync(
        string email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            @event = SequenceStartedEventName,
            email,
            payload = new
            {
                first_name = NormalizeName(firstName),
                last_name = NormalizeName(lastName)
            }
        };

        using var request = CreateJsonRequest(HttpMethod.Post, $"{ApiBaseUrl}/events/send", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning(
            "Resend gratis subscriber sequence event failed with status {StatusCode}.",
            (int)response.StatusCode);
        return false;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string requestUri)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Schink-Stories/1.0");
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string requestUri, object payload)
    {
        var request = CreateRequest(method, requestUri);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }

    private bool IsConfigured() => !string.IsNullOrWhiteSpace(_options.ApiKey);

    private static string? NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 254 || !normalized.Contains('@')
            ? null
            : normalized;
    }

    private static string NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        return normalized[..Math.Min(normalized.Length, 80)];
    }

    private sealed record ContactLookup(bool Exists, bool Unsubscribed, bool IsFailure = false);
}
