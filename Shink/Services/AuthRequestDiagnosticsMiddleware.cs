using System.Net;
using System.Text.Json;

namespace Shink.Services;

public sealed class AuthRequestDiagnosticsMiddleware(RequestDelegate next, ILogger<AuthRequestDiagnosticsMiddleware> logger)
{
    public const string RequestIdHeader = "X-Schink-Request-Id";
    public const string FailureReasonHeader = "X-Schink-Auth-Reason";
    private static readonly object FailureReasonKey = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (!HttpMethods.IsPost(context.Request.Method) || !IsSignInPath(path))
        {
            await next(context);
            return;
        }

        // Accept only an opaque GUID, never an arbitrary client-supplied log value.
        var suppliedId = context.Request.Headers[RequestIdHeader].ToString();
        var requestId = Guid.TryParseExact(suppliedId, "N", out var parsedId)
            ? parsedId.ToString("N")
            : Guid.NewGuid().ToString("N");
        context.Response.Headers[RequestIdHeader] = requestId;
        context.Response.OnStarting(() =>
        {
            if (context.Response.StatusCode >= 400)
                context.Response.Headers[FailureReasonHeader] = GetFailureReason(context, context.Response.StatusCode);
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            SetFailureReason(context, "server_error");
            LogFailure(context, requestId, path!, StatusCodes.Status500InternalServerError);
            throw;
        }

        if (context.Response.StatusCode >= 400)
            LogFailure(context, requestId, path!, context.Response.StatusCode);
    }

    public static void SetFailureReason(HttpContext context, string? reason) =>
        context.Items[FailureReasonKey] = NormalizeReason(reason);

    public static string ProviderFailureReason(HttpStatusCode status, string body)
    {
        if ((int)status >= 500) return "auth_unavailable";
        if (status == HttpStatusCode.TooManyRequests) return "rate_limited";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error_code", out var code) &&
                code.ValueKind == JsonValueKind.String)
                return NormalizeReason(code.GetString());
        }
        catch (JsonException) { }
        return "auth_rejected";
    }

    private static bool IsSignInPath(string? path) => path is
        "/api/auth/login" or "/api/mobile/auth/google/complete" or "/api/mobile/auth/apple/complete";

    private static string NormalizeReason(string? reason) => reason switch
    {
        "invalid_input" or "invalid_credentials" or "email_not_confirmed" or
        "rate_limited" or "auth_unavailable" or "auth_not_configured" or
        "server_error" => reason,
        "over_request_rate_limit" or "over_email_send_rate_limit" => "rate_limited",
        _ => "auth_rejected"
    };

    private static string GetFailureReason(HttpContext context, int status) =>
        context.Items.TryGetValue(FailureReasonKey, out var reason) && reason is string value
            ? value
            : status switch
            {
                400 => "invalid_request",
                429 => "rate_limited",
                >= 500 => "server_error",
                _ => "auth_rejected"
            };

    private void LogFailure(HttpContext context, string requestId, string path, int status) =>
        logger.LogWarning(new EventId(4001, "AuthenticationRequestFailed"),
            "Authentication request failed: RequestId={RequestId} Reason={Reason} StatusCode={StatusCode} Endpoint={Endpoint}",
            requestId, GetFailureReason(context, status), status, path);
}
