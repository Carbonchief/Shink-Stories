using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shink.Services;

public sealed partial class ResendSubscriptionNotificationEmailService
{
    public async Task<bool> QueueAdminOpsAlertAsync(AdminOpsAlertEmailRequest request, CancellationToken cancellationToken = default)
    {
        var alert = BuildAdminOpsAlert(request);
        if (alert is null || !TryGetAlertStorageUri(out var baseUri)) return false;
        var payload = BuildAdminOpsRequest(alert.Value.TemplateId, alert.Value.Variables);
        try
        {
            using var storageRequest = CreateAlertStorageRequest(HttpMethod.Post,
                new Uri(baseUri, "rest/v1/admin_ops_alert_deliveries?on_conflict=alert_key"));
            storageRequest.Headers.Add("Prefer", "resolution=ignore-duplicates,return=minimal");
            storageRequest.Content = JsonContent.Create(new { alert_key = alert.Value.Key, request_payload = payload });
            using var response = await _httpClient.SendAsync(storageRequest, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Admin ops alert could not be durably queued.");
            return false;
        }
    }

    public async Task ProcessPendingAdminOpsAlertsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || !TryGetAlertStorageUri(out var baseUri)) return;
        for (var index = 0; index < 5 && !cancellationToken.IsCancellationRequested; index++)
        {
            try
            {
                using var request = CreateAlertStorageRequest(HttpMethod.Post, new Uri(baseUri, "rest/v1/rpc/claim_pending_admin_ops_alert"));
                request.Content = JsonContent.Create(new { });
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var claims = await response.Content.ReadFromJsonAsync<List<AdminOpsDeliveryClaim>>(cancellationToken);
                var claim = claims?.SingleOrDefault();
                if (claim is null) return;
                await SendClaimedAdminOpsAlertAsync(baseUri, claim, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(exception, "Pending admin ops alert dispatch failed; its durable record remains retryable.");
                return;
            }
        }
    }

    private ResendTemplateEmailRequest BuildAdminOpsRequest(string templateId, IReadOnlyDictionary<string, object?> variables) =>
        new(_options.FromEmail, [_options.ToEmail], null, null, null, [_options.ToEmail],
            new ResendTemplateRequest(templateId, variables));

    private bool TryGetAlertStorageUri(out Uri baseUri)
    {
        if (Uri.TryCreate(_supabaseOptions.Url.TrimEnd('/') + "/", UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(_supabaseOptions.SecretKey))
        {
            baseUri = uri;
            return true;
        }
        _logger.LogWarning("Admin ops alert skipped: durable delivery storage is not configured.");
        baseUri = null!;
        return false;
    }

    private async Task SendAdminOpsAlertOnceAsync(
        string alertKey,
        string templateId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken)
    {
        if (!TryGetAlertStorageUri(out var baseUri)) return;
        var payload = BuildAdminOpsRequest(templateId, variables);
        try
        {
            using var claimRequest = CreateAlertStorageRequest(HttpMethod.Post, new Uri(baseUri, "rest/v1/rpc/claim_admin_ops_alert"));
            claimRequest.Content = JsonContent.Create(new { p_alert_key = alertKey, p_request_payload = payload });
            using var claimResponse = await _httpClient.SendAsync(claimRequest, cancellationToken);
            if (!claimResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Admin ops alert could not claim durable delivery. Status={StatusCode}", (int)claimResponse.StatusCode);
                return;
            }

            var claims = await claimResponse.Content.ReadFromJsonAsync<List<AdminOpsDeliveryClaim>>(cancellationToken);
            var claim = claims?.SingleOrDefault();
            if (claim is null)
            {
                // Already sent, currently leased, or past the safe retry window.
                return;
            }

            await SendClaimedAdminOpsAlertAsync(baseUri, claim, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Admin ops alert delivery failed; its durable claim was retained.");
        }
    }

    private async Task SendClaimedAdminOpsAlertAsync(Uri baseUri, AdminOpsDeliveryClaim claim, CancellationToken cancellationToken)
    {
        using var sendRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(claim.RequestPayload)
        };
        sendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        sendRequest.Headers.Add("Idempotency-Key", claim.AlertKey);
        using var sendResponse = await _httpClient.SendAsync(sendRequest, cancellationToken);
        if (!sendResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Resend rejected admin ops alert. Status={StatusCode}", (int)sendResponse.StatusCode);
            return;
        }

        using var responseBody = await JsonDocument.ParseAsync(await sendResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!responseBody.RootElement.TryGetProperty("id", out var idNode) ||
            idNode.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(idNode.GetString()))
        {
            return;
        }
        var emailId = idNode.GetString();

        var completionUri = new Uri(baseUri, "rest/v1/admin_ops_alert_deliveries" +
            $"?alert_key=eq.{Uri.EscapeDataString(claim.AlertKey)}&lease_token=eq.{claim.LeaseToken}&sent_at=is.null");
        using var completionRequest = CreateAlertStorageRequest(HttpMethod.Patch, completionUri);
        completionRequest.Content = JsonContent.Create(new { sent_at = DateTimeOffset.UtcNow, provider_email_id = emailId });
        using var completionResponse = await _httpClient.SendAsync(completionRequest, cancellationToken);
        if (!completionResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Admin ops alert was accepted but its delivery record could not be completed. Status={StatusCode}", (int)completionResponse.StatusCode);
        }
    }

    private HttpRequestMessage CreateAlertStorageRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("apikey", _supabaseOptions.SecretKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseOptions.SecretKey);
        return request;
    }

    private sealed record AdminOpsDeliveryClaim(
        [property: JsonPropertyName("alert_key")] string AlertKey,
        [property: JsonPropertyName("request_payload")] JsonElement RequestPayload,
        [property: JsonPropertyName("lease_token")] Guid LeaseToken);
}
