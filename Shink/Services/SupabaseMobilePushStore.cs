using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseMobilePushStore(HttpClient httpClient, IOptions<SupabaseOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<Guid> RegisterAsync(string email, MobilePushRegistrationRequest device, CancellationToken cancellationToken)
    {
        if (!device.IsValid) throw new ArgumentException("Invalid push registration.", nameof(device));
        using var response = await RpcAsync("register_mobile_push_device", new
        {
            p_email = email, p_installation_id = device.InstallationId,
            p_secret_hash = HashSecret(device.InstallationSecret), p_platform = device.Platform,
            p_environment = device.Environment, p_token = device.Token
        }, cancellationToken);
        return await response.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    public async Task UnregisterAsync(MobilePushRevocationRequest device, CancellationToken cancellationToken)
    {
        if (!device.IsValid) throw new ArgumentException("Invalid installation proof.", nameof(device));
        using var response = await RpcAsync("unregister_mobile_push_device", new
        {
            p_installation_id = device.InstallationId, p_secret_hash = HashSecret(device.InstallationSecret)
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<MobilePushDelivery>> ClaimAsync(string[] platforms, CancellationToken cancellationToken)
    {
        using var response = await RpcAsync("claim_mobile_push_deliveries", new { p_platforms = platforms, p_take = 20 }, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<MobilePushDelivery>>(JsonOptions, cancellationToken) ?? [];
    }

    public async Task<bool> CanSendAsync(MobilePushDelivery delivery, CancellationToken cancellationToken)
    {
        using var response = await RpcAsync("can_send_mobile_push_delivery", new
        {
            p_delivery_id = delivery.DeliveryId, p_lease_id = delivery.LeaseId
        }, cancellationToken);
        return await response.Content.ReadFromJsonAsync<bool>(cancellationToken);
    }

    public async Task CompleteAsync(MobilePushDelivery delivery, MobilePushSendResult result, CancellationToken cancellationToken)
    {
        using var response = await RpcAsync("complete_mobile_push_delivery", new
        {
            p_delivery_id = delivery.DeliveryId, p_lease_id = delivery.LeaseId,
            p_result = result.Result, p_error = result.Error, p_retry_seconds = result.RetrySeconds
        }, cancellationToken);
    }

    private async Task<HttpResponseMessage> RpcAsync(string function, object payload, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!Uri.TryCreate(settings.Url.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || string.IsNullOrWhiteSpace(settings.SecretKey))
            throw new InvalidOperationException("Supabase push storage is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "rest/v1/rpc/" + function))
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("apikey", settings.SecretKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SecretKey);
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            // Never include provider bodies, installation secrets or device tokens in logs.
            throw new HttpRequestException($"Push storage {function} returned HTTP {(int)status}.", null, status);
        }
        return response;
    }

    private static string HashSecret(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
