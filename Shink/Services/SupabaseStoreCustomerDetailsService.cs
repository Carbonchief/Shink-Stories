using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class SupabaseStoreCustomerDetailsService(HttpClient httpClient, IOptions<SupabaseOptions> options,
    ILogger<SupabaseStoreCustomerDetailsService> logger) : IStoreCustomerDetailsService
{
    private const string Columns = "customer_name,customer_email,customer_phone,delivery_address_line_1,delivery_address_line_2,delivery_suburb,delivery_city,delivery_postal_code";

    public async Task<StoreCustomerDetails?> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var email = StoreCustomerDetails.AccountEmail(user);
        if (email is null || !TryBaseUri(out var baseUri)) return null;
        try
        {
            using var request = Request(HttpMethod.Get, new Uri(baseUri,
                $"rest/v1/store_customer_details?select={Columns}&account_email=eq.{Uri.EscapeDataString(email)}&limit=1"));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Saved store details lookup failed. Status={StatusCode}", (int)response.StatusCode);
                return null;
            }
            var rows = await response.Content.ReadFromJsonAsync<StoreCustomerDetails[]>(cancellationToken);
            return rows?.FirstOrDefault();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Saved store details lookup was unavailable. Type={ExceptionType}", exception.GetType().Name);
            return null;
        }
    }

    public async Task<bool> SaveAsync(ClaimsPrincipal user, StoreCustomerDetails details, CancellationToken cancellationToken = default)
    {
        var email = StoreCustomerDetails.AccountEmail(user);
        if (email is null || !TryBaseUri(out var baseUri)) return false;
        try
        {
            // The owner comes only from the authenticated session, never the checkout contact address.
            var payload = JsonSerializer.SerializeToNode(details)!.AsObject();
            payload["account_email"] = email;
            payload["updated_at"] = DateTimeOffset.UtcNow;
            using var request = Request(HttpMethod.Post, new Uri(baseUri, "rest/v1/store_customer_details?on_conflict=account_email"));
            request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");
            request.Content = JsonContent.Create(payload);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Saved store details update failed. Status={StatusCode}", (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Saved store details update was unavailable. Type={ExceptionType}", exception.GetType().Name);
            return false;
        }
    }

    private bool TryBaseUri(out Uri baseUri)
    {
        baseUri = null!;
        return !string.IsNullOrWhiteSpace(options.Value.SecretKey)
            && Uri.TryCreate(options.Value.Url.TrimEnd('/') + "/", UriKind.Absolute, out baseUri!)
            && baseUri.Scheme is "https" or "http";
    }

    private HttpRequestMessage Request(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var key = options.Value.SecretKey.Trim();
        request.Headers.TryAddWithoutValidation("apikey", key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
}
