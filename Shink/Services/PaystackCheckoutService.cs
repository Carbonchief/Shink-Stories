using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed class PaystackCheckoutService(HttpClient httpClient, IOptions<PaystackOptions> options)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly PaystackOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.SecretKey) &&
        Uri.TryCreate(_options.InitializeUrl, UriKind.Absolute, out _);

    public async Task<PaystackCheckoutInitResult> InitializeCheckoutAsync(
        PaymentPlan plan,
        HttpContext httpContext,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Paystack is nog nie volledig opgestel nie.");
        }

        if (!Uri.TryCreate(_options.InitializeUrl, UriKind.Absolute, out var initializeUri))
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Paystack Initialize URL is ongeldig.");
        }

        var email = GetBuyerEmail(httpContext.User);
        if (string.IsNullOrWhiteSpace(email))
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Kon nie 'n e-posadres vir betaling bepaal nie.");
        }

        var planCode = ResolvePlanCode(plan.TierCode);
        if (plan.IsSubscription && string.IsNullOrWhiteSpace(planCode))
        {
            return new PaystackCheckoutInitResult(
                false,
                ErrorMessage: $"Paystack plan code ontbreek vir tier '{plan.TierCode}'.");
        }

        var reference = BuildReference(plan.Slug);
        var callbackQuery = $"betaling=sukses&provider=paystack&plan={Uri.EscapeDataString(plan.Slug)}";
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            callbackQuery = $"{callbackQuery}&returnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        var callbackUrl = BuildAbsoluteUrl(
            httpContext,
            _options.CallbackUrlPath,
            callbackQuery);

        var amountInCents = (long)Math.Round(plan.Amount * 100m, MidpointRounding.AwayFromZero);

        var metadata = new Dictionary<string, object?>
        {
            ["plan_slug"] = plan.Slug,
            ["tier_code"] = plan.TierCode,
            ["billing_period_months"] = plan.BillingPeriodMonths,
            ["is_subscription"] = plan.IsSubscription,
            ["subscription_key"] = reference
        };

        var payload = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["amount"] = amountInCents,
            ["currency"] = "ZAR",
            ["reference"] = reference,
            ["callback_url"] = callbackUrl,
            ["metadata"] = metadata
        };

        if (!string.IsNullOrWhiteSpace(planCode))
        {
            payload["plan"] = planCode;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, initializeUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PaystackCheckoutInitResult(
                false,
                ErrorMessage: $"Paystack checkout kon nie begin nie (HTTP {(int)response.StatusCode}).");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusNode) &&
                         statusNode.ValueKind == JsonValueKind.True;

            var dataNode = root.TryGetProperty("data", out var parsedData) &&
                           parsedData.ValueKind == JsonValueKind.Object
                ? parsedData
                : default;

            var authorizationUrl = TryReadString(dataNode, "authorization_url");
            var parsedReference = TryReadString(dataNode, "reference") ?? reference;

            if (!status || string.IsNullOrWhiteSpace(authorizationUrl))
            {
                var message = TryReadString(root, "message") ?? "Paystack checkout kon nie begin nie.";
                return new PaystackCheckoutInitResult(false, ErrorMessage: message);
            }

            return new PaystackCheckoutInitResult(
                true,
                AuthorizationUrl: authorizationUrl,
                Reference: parsedReference);
        }
        catch (JsonException)
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Paystack checkout antwoord kon nie gelees word nie.");
        }
    }

    public bool IsWebhookSignatureValid(string rawPayload, string? providedSignature)
    {
        if (string.IsNullOrWhiteSpace(rawPayload) ||
            string.IsNullOrWhiteSpace(providedSignature) ||
            string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return false;
        }

        if (!TryParseHex(providedSignature.Trim(), out var providedBytes))
        {
            return false;
        }

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_options.SecretKey));
        var payloadBytes = Encoding.UTF8.GetBytes(rawPayload);
        var computedBytes = hmac.ComputeHash(payloadBytes);
        return CryptographicOperations.FixedTimeEquals(computedBytes, providedBytes);
    }

    private string BuildAbsoluteUrl(HttpContext httpContext, string path, string? queryString)
    {
        var baseUri = ResolveBaseUrl(httpContext);
        var builder = new UriBuilder(new Uri(new Uri(baseUri), path));
        if (!string.IsNullOrWhiteSpace(queryString))
        {
            builder.Query = queryString;
        }

        return builder.Uri.ToString();
    }

    private string ResolveBaseUrl(HttpContext httpContext)
    {
        if (Uri.TryCreate(_options.PublicBaseUrl, UriKind.Absolute, out var configuredBaseUri))
        {
            return configuredBaseUri.ToString();
        }

        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    }

    private static string? GetBuyerEmail(ClaimsPrincipal user)
    {
        var email = user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? string.Empty;

        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    private string? ResolvePlanCode(string tierCode)
    {
        if (_options.PlanCodes.TryGetValue(tierCode, out var directCode) &&
            !string.IsNullOrWhiteSpace(directCode))
        {
            return directCode.Trim();
        }

        foreach (var entry in _options.PlanCodes)
        {
            if (string.Equals(entry.Key, tierCode, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(entry.Value))
            {
                return entry.Value.Trim();
            }
        }

        return null;
    }

    private static string BuildReference(string planSlug) =>
        $"{planSlug}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString(),
            JsonValueKind.Number => node.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool TryParseHex(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length % 2 != 0)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record PaystackCheckoutInitResult(
    bool IsSuccess,
    string? AuthorizationUrl = null,
    string? Reference = null,
    string? ErrorMessage = null);
