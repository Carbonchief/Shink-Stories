using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Shink.Services;

public sealed class MobilePushSender(IHttpClientFactory clients, IOptions<MobilePushOptions> options, IMemoryCache cache)
{
    private readonly SemaphoreSlim _credentialGate = new(1, 1);

    public async Task<MobilePushSendResult> SendAsync(MobilePushDelivery delivery, CancellationToken cancellationToken)
    {
        if (!options.Value.Supports(delivery.Platform)) return new("retry", "provider_not_configured", 3600);
        using var client = clients.CreateClient("mobile-push-provider");
        return delivery.Platform == "apns"
            ? await SendAppleAsync(client, delivery, cancellationToken)
            : await SendFirebaseAsync(client, delivery, cancellationToken);
    }

    private async Task<MobilePushSendResult> SendAppleAsync(HttpClient client, MobilePushDelivery delivery, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var host = delivery.Environment == "sandbox" ? "api.sandbox.push.apple.com" : "api.push.apple.com";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/3/device/{delivery.Token}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = JsonContent.Create(new
            {
                aps = new { alert = new { title = Truncate(delivery.Title, 120), body = Truncate(delivery.Body, 500) }, sound = "default" },
                notification_id = delivery.NotificationId.ToString("D"),
                registration_id = delivery.RegistrationId.ToString("D"),
                notification_type = delivery.NotificationType,
                href = delivery.Href ?? string.Empty
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", await GetAppleTokenAsync(cancellationToken));
        request.Headers.Add("apns-topic", settings.AppleBundleId);
        request.Headers.Add("apns-push-type", "alert");
        request.Headers.Add("apns-priority", "10");
        request.Headers.Add("apns-expiration", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("apns-collapse-id", delivery.NotificationId.ToString("D"));
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return new("sent");
        using var error = await ReadErrorAsync(response, cancellationToken);
        var reason = ReadString(error?.RootElement, "reason");
        if (reason is "Unregistered" or "BadDeviceToken" or "DeviceTokenNotForTopic") return new("invalid", reason);
        // Authentication failures are configuration problems, never grounds to delete device tokens.
        if (reason is "ExpiredProviderToken" or "InvalidProviderToken") cache.Remove("mobile-push:apple-token");
        return Failure(response, reason is "PayloadTooLarge" or "BadTopic", "apns_http_" + (int)response.StatusCode);
    }

    private async Task<MobilePushSendResult> SendFirebaseAsync(HttpClient client, MobilePushDelivery delivery, CancellationToken cancellationToken)
    {
        var accessToken = await GetGoogleTokenAsync(client, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(options.Value.FirebaseProjectId)}/messages:send")
        {
            // Data messages are displayed by our service only while the installation
            // still belongs to this account. The notification ID supplies local dedupe.
            Content = JsonContent.Create(new
            {
                message = new
                {
                    token = delivery.Token,
                    data = new Dictionary<string, string>
                    {
                        ["notification_id"] = delivery.NotificationId.ToString("D"),
                        ["registration_id"] = delivery.RegistrationId.ToString("D"),
                        ["notification_type"] = delivery.NotificationType,
                        ["href"] = delivery.Href ?? string.Empty,
                        ["title"] = Truncate(delivery.Title, 120), ["body"] = Truncate(delivery.Body, 500)
                    },
                    android = new { priority = "HIGH", ttl = "3600s" }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return new("sent");
        using var error = await ReadErrorAsync(response, cancellationToken);
        if (error?.RootElement.TryGetProperty("error", out var errorObject) == true
            && errorObject.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            && details.EnumerateArray().Any(detail => ReadString(detail, "errorCode") == "UNREGISTERED"))
            return new("invalid", "fcm_unregistered");
        if (response.StatusCode == HttpStatusCode.Unauthorized) cache.Remove("mobile-push:google-token");
        return Failure(response, response.StatusCode == HttpStatusCode.BadRequest, "fcm_http_" + (int)response.StatusCode);
    }

    private async Task<string> GetAppleTokenAsync(CancellationToken cancellationToken)
    {
        await _credentialGate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<string>("mobile-push:apple-token", out var token)) return token!;
            var settings = options.Value;
            var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = settings.AppleKeyId }));
            var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new { iss = settings.AppleTeamId, iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }));
            var unsigned = header + "." + claims;
            using var key = ECDsa.Create();
            key.ImportFromPem(settings.ApplePrivateKey.Replace("\\n", "\n", StringComparison.Ordinal));
            token = unsigned + "." + Encode(key.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            cache.Set("mobile-push:apple-token", token, TimeSpan.FromMinutes(45));
            return token;
        }
        finally { _credentialGate.Release(); }
    }

    private async Task<string> GetGoogleTokenAsync(HttpClient client, CancellationToken cancellationToken)
    {
        await _credentialGate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<string>("mobile-push:google-token", out var token)) return token!;
            using var credentials = JsonDocument.Parse(options.Value.FirebaseServiceAccountJson);
            var email = credentials.RootElement.GetProperty("client_email").GetString();
            var privateKey = credentials.RootElement.GetProperty("private_key").GetString()!;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = email, scope = "https://www.googleapis.com/auth/firebase.messaging",
                aud = "https://oauth2.googleapis.com/token", iat = now, exp = now + 3600
            }));
            var unsigned = header + "." + claims;
            using var key = RSA.Create();
            key.ImportFromPem(privateKey.Replace("\\n", "\n", StringComparison.Ordinal));
            var assertion = unsigned + "." + Encode(key.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using var response = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = assertion
            }), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            token = result.RootElement.GetProperty("access_token").GetString()!;
            var lifetime = result.RootElement.GetProperty("expires_in").GetInt32();
            cache.Set("mobile-push:google-token", token, TimeSpan.FromSeconds(Math.Max(30, lifetime - 120)));
            return token;
        }
        finally { _credentialGate.Release(); }
    }

    private static MobilePushSendResult Failure(HttpResponseMessage response, bool permanent, string code)
    {
        var delay = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
        return new(permanent ? "failed" : "retry", code, (int)Math.Clamp(delay?.TotalSeconds ?? 60, 60, 3600));
    }

    private static async Task<JsonDocument?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try { return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken); }
        catch (JsonException) { return null; }
    }

    private static string? ReadString(JsonElement? element, string name) =>
        element?.ValueKind == JsonValueKind.Object && element.Value.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Truncate(string? value, int length) => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= length ? value : value[..length];
}
