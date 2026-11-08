using System.Text.Json;

namespace Shink.Mobile.Services;

public sealed class MobileAuthRequestException(
    string message, int statusCode, string requestId, string reason, string responseBodyKind)
    : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
    public string RequestId { get; } = requestId;
    public string Reason { get; } = reason;
    public string ResponseBodyKind { get; } = responseBodyKind;
}

public static class MobileAuthFailure
{
    public const string RequestIdHeader = "X-Schink-Request-Id";
    private const string FailureReasonHeader = "X-Schink-Auth-Reason";

    public static bool IsSignInPath(string path) => path is
        "/api/auth/login" or "/api/mobile/auth/google/complete" or "/api/mobile/auth/apple/complete";

    public static MobileAuthRequestException Create(HttpResponseMessage response, string body, string clientRequestId)
    {
        var requestId = response.Headers.TryGetValues(RequestIdHeader, out var ids) &&
                        Guid.TryParseExact(ids.FirstOrDefault(), "N", out var serverId)
            ? serverId.ToString("N")
            : clientRequestId;
        var bodyKind = string.IsNullOrWhiteSpace(body) ? "empty" : "other";
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            bodyKind = "json";
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var element) &&
                element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()))
                message = element.GetString();
        }
        catch (JsonException)
        {
            if (body.TrimStart().StartsWith('<')) bodyKind = "html";
        }

        var reason = response.Headers.TryGetValues(FailureReasonHeader, out var reasons)
            ? NormalizeReason(reasons.FirstOrDefault())
            : null;
        reason ??= (int)response.StatusCode switch
        {
            429 => "rate_limited",
            >= 500 => "server_error",
            _ => bodyKind == "empty" ? "empty_response" : "auth_rejected"
        };

        // Proxy HTML/plain text is never a useful sign-in instruction.
        message ??= reason switch
        {
            "invalid_credentials" => "Ongeldige e-pos of wagwoord. Probeer asseblief weer.",
            "email_not_confirmed" => "Bevestig asseblief eers jou e-posadres voordat jy inteken.",
            "rate_limited" => "Te veel aanmeldpogings. Wag asseblief 'n paar minute en probeer weer.",
            "auth_unavailable" or "server_error" => "Ons kan jou nie nou inteken nie. Probeer asseblief oor 'n rukkie weer.",
            _ => "Kon nie nou inteken nie. Probeer asseblief weer. As dit aanhou, kontak ons vir hulp."
        };
        return new(message, (int)response.StatusCode, requestId, reason, bodyKind);
    }

    public static void AddDiagnostics(IDictionary<string, object> properties, Exception exception, string requestId)
    {
        properties["request_id"] = requestId;
        if (exception is MobileAuthRequestException failure)
        {
            properties["request_id"] = failure.RequestId;
            properties["failure_reason"] = failure.Reason;
            properties["response_body_kind"] = failure.ResponseBodyKind;
        }
        else
        {
            properties["failure_reason"] = properties.TryGetValue("status_code", out var status) && status is int code && code != 0
                ? "response_processing_failed"
                : "no_response";
        }
        // Do not include exception messages, response bodies, headers, or payloads.
    }

    private static string? NormalizeReason(string? reason) => reason switch
    {
        "invalid_input" or "invalid_request" or "invalid_credentials" or "email_not_confirmed" or
        "rate_limited" or "auth_unavailable" or "auth_not_configured" or "server_error" or "auth_rejected" => reason,
        _ => null
    };
}
