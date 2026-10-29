using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseSubscriberAvatarService(
    HttpClient httpClient,
    IOptions<SupabaseOptions> options,
    ILogger<SupabaseSubscriberAvatarService> logger) : ISubscriberAvatarService
{
    public async Task<bool> UpdateAsync(string email, ProfileAvatar avatar, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(email) || ProfileAvatarCatalog.Find(avatar.Id) != avatar ||
            !Uri.TryCreate(settings.Url.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            return false;
        }

        var escapedEmail = Uri.EscapeDataString(email.Trim().ToLowerInvariant());
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            new Uri(baseUri, $"rest/v1/subscribers?email=eq.{escapedEmail}&select=subscriber_id"));
        request.Headers.Add("apikey", settings.SecretKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SecretKey);
        request.Headers.Add("Prefer", "return=representation");
        request.Content = JsonContent.Create(new
        {
            profile_image_url = avatar.ImageUrl,
            // A catalogue portrait is a local asset, rather than an uploaded R2 object.
            profile_image_object_key = (string?)null,
            profile_image_content_type = "image/png"
        });

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Subscriber avatar update failed. Status={StatusCode}", (int)response.StatusCode);
                return false;
            }

            using var result = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            return result?.RootElement.ValueKind == JsonValueKind.Array && result.RootElement.GetArrayLength() == 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Subscriber avatar update failed.");
            return false;
        }
    }
}
