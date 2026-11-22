using System.Net.Http.Json;

namespace Shink.Services;

internal sealed class StoreCheckoutAnalytics(
    HttpClient httpClient,
    PostHogSettings postHog,
    IHostEnvironment environment,
    ILogger<StoreCheckoutAnalytics> logger)
{
    internal void RecordValidationFailure(HttpResponse response, Guid? visitorId, string issue)
    {
        if (visitorId is not Guid visitor || !postHog.TryGetCaptureUri(out var captureUri)) return;

        var timestamp = DateTimeOffset.UtcNow;
        // The declined form's redirect can finish before the analytics request runs.
        response.OnCompleted(async () =>
        {
            try
            {
                using var capture = await httpClient.PostAsJsonAsync(captureUri, new
                {
                    api_key = postHog.ProjectApiKey,
                    @event = "winkel_checkout_validation_failed",
                    distinct_id = $"winkel:{visitor:D}",
                    timestamp,
                    properties = new Dictionary<string, object>
                    {
                        ["source"] = "checkout_server",
                        ["environment"] = environment.EnvironmentName.ToLowerInvariant(),
                        ["page"] = "checkout",
                        ["checkout_issue"] = issue,
                        ["$process_person_profile"] = false
                    }
                });
                capture.EnsureSuccessStatusCode();
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                logger.LogWarning("Winkel checkout validation analytics could not be captured ({ErrorType}).", exception.GetType().Name);
            }
        });
    }
}
