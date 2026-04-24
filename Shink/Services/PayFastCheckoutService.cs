using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed partial class PayFastCheckoutService(HttpClient httpClient, IOptions<PayFastOptions> options)
{
    private readonly PayFastOptions _options = options.Value;
    private readonly HttpClient _httpClient = httpClient;

    public bool TryBuildCheckout(PaymentPlan plan, HttpContext httpContext, string? returnUrl, out PayFastCheckoutForm checkoutForm, out string errorMessage)
    {
        checkoutForm = default!;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(_options.MerchantId) ||
            string.IsNullOrWhiteSpace(_options.MerchantKey) ||
            string.IsNullOrWhiteSpace(_options.ProcessUrl))
        {
            errorMessage = "PayFast is nog nie volledig opgestel nie.";
            return false;
        }

        if (!Uri.TryCreate(_options.ProcessUrl, UriKind.Absolute, out var processUrl))
        {
            errorMessage = "PayFast Process URL is ongeldig.";
            return false;
        }

        var successQuery = $"betaling=sukses&plan={Uri.EscapeDataString(plan.Slug)}";
        var cancelQuery = $"betaling=gekanselleer&plan={Uri.EscapeDataString(plan.Slug)}";
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            var encodedReturnUrl = Uri.EscapeDataString(returnUrl);
            successQuery = $"{successQuery}&returnUrl={encodedReturnUrl}";
            cancelQuery = $"{cancelQuery}&returnUrl={encodedReturnUrl}";
        }

        var returnPath = BuildAbsoluteUrl(httpContext, _options.ReturnUrlPath, successQuery);
        var cancelPath = BuildAbsoluteUrl(httpContext, _options.CancelUrlPath, cancelQuery);
        var notifyUrl = BuildAbsoluteUrl(httpContext, _options.NotifyUrlPath, null);
        var paymentId = $"{plan.Slug}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var (firstName, lastName, emailAddress) = GetBuyerDetails(httpContext.User);

        var fields = new List<KeyValuePair<string, string>>
        {
            new("merchant_id", _options.MerchantId),
            new("merchant_key", _options.MerchantKey),
            new("return_url", returnPath),
            new("cancel_url", cancelPath),
            new("notify_url", notifyUrl),
            new("name_first", firstName),
            new("name_last", lastName),
            new("email_address", emailAddress),
            new("m_payment_id", paymentId),
            new("amount", plan.Amount.ToString("0.00", CultureInfo.InvariantCulture)),
            new("item_name", plan.ItemName),
            new("item_description", plan.ItemDescription),
            new("custom_str1", plan.Slug),
            new("custom_str2", plan.TierCode)
        };

        if (plan.IsSubscription)
        {
            fields.Add(new("subscription_type", "1"));
            fields.Add(new("recurring_amount", plan.Amount.ToString("0.00", CultureInfo.InvariantCulture)));
            fields.Add(new("frequency", plan.BillingFrequency.ToString(CultureInfo.InvariantCulture)));
            fields.Add(new("cycles", "0"));
            fields.Add(new("subscription_notify_email", "true"));
            fields.Add(new("subscription_notify_webhook", "true"));
            fields.Add(new("subscription_notify_buyer", "true"));
        }

        fields = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .ToList();

        var signature = GenerateSignature(fields, _options.Passphrase);
        fields.Add(new KeyValuePair<string, string>("signature", signature));

        checkoutForm = new PayFastCheckoutForm(processUrl.ToString(), fields, paymentId);
        return true;
    }

    public string BuildAutoSubmitFormHtml(PayFastCheckoutForm checkoutForm, string heading, string? cspNonce = null)
    {
        var builder = new StringBuilder();
        var scriptNonceAttribute = string.IsNullOrWhiteSpace(cspNonce)
            ? string.Empty
            : $" nonce=\"{HtmlEncode(cspNonce)}\"";
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"af\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine("  <title>Schink Stories | Betaal</title>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main style=\"max-width:640px;margin:3rem auto;font-family:Arial,Helvetica,sans-serif;padding:0 1rem;\">");
        builder.AppendLine($"    <h1 style=\"font-size:1.35rem;line-height:1.3;\">{HtmlEncode(heading)}</h1>");
        builder.AppendLine("    <p>Ons neem jou nou na PayFast Sandbox vir veilige betaling.</p>");
        builder.AppendLine($"    <form id=\"payfast-form\" action=\"{HtmlEncode(checkoutForm.ActionUrl)}\" method=\"post\">");

        foreach (var field in checkoutForm.Fields)
        {
            builder.AppendLine(
                $"      <input type=\"hidden\" name=\"{HtmlEncode(field.Key)}\" value=\"{HtmlEncode(field.Value)}\" />");
        }

        builder.AppendLine("      <noscript><button type=\"submit\">Gaan na PayFast</button></noscript>");
        builder.AppendLine("    </form>");
        builder.AppendLine("  </main>");
        builder.AppendLine($"  <script{scriptNonceAttribute}>document.getElementById('payfast-form')?.submit();</script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    public bool IsItnSignatureValid(IFormCollection formCollection)
    {
        var postedSignature = formCollection["signature"].ToString();
        if (string.IsNullOrWhiteSpace(postedSignature))
        {
            return false;
        }

        var fields = BuildItnFields(formCollection);
        var expectedSignature = GenerateSignature(fields, _options.Passphrase, includeEmptyValues: true);
        return string.Equals(postedSignature, expectedSignature, StringComparison.OrdinalIgnoreCase);
    }

    public string BuildItnValidationPayload(IFormCollection formCollection)
    {
        var fields = BuildItnFields(formCollection);
        return BuildSignaturePayload(fields, passphrase: null, includeEmptyValues: true);
    }

    public async Task<bool> ValidateServerConfirmationAsync(string validationPayload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(validationPayload) ||
            !Uri.TryCreate(_options.ValidateUrl, UriKind.Absolute, out var validateUri))
        {
            return false;
        }

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, validateUri)
        {
            Content = new StringContent(validationPayload, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        return string.Equals(body, "VALID", StringComparison.OrdinalIgnoreCase);
    }

    private static List<KeyValuePair<string, string>> BuildItnFields(IFormCollection formCollection) =>
        formCollection
            .Where(item => !string.Equals(item.Key, "signature", StringComparison.OrdinalIgnoreCase))
            .Select(item => new KeyValuePair<string, string>(item.Key, item.Value.ToString()))
            .ToList();

    private static (string FirstName, string LastName, string EmailAddress) GetBuyerDetails(ClaimsPrincipal user)
    {
        var email = user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email))
        {
            return ("Ouer", "Schink", string.Empty);
        }

        var localPart = email.Split('@', 2)[0];
        var normalized = localPart
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();

        var segments = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return ("Ouer", "Schink", email);
        }

        if (segments.Length == 1)
        {
            return (ToTitleCase(segments[0]), "Ouer", email);
        }

        return (ToTitleCase(segments[0]), ToTitleCase(string.Join(' ', segments.Skip(1))), email);
    }

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(value.ToLowerInvariant());
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

    private static string GenerateSignature(IEnumerable<KeyValuePair<string, string>> fields, string? passphrase, bool includeEmptyValues = false)
    {
        var payload = BuildSignaturePayload(fields, passphrase, includeEmptyValues);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = MD5.HashData(payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildSignaturePayload(IEnumerable<KeyValuePair<string, string>> fields, string? passphrase, bool includeEmptyValues = false)
    {
        var parts = new List<string>();
        foreach (var (key, value) in fields)
        {
            if (string.Equals(key, "signature", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!includeEmptyValues && string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            parts.Add($"{key}={PayFastEncode(value ?? string.Empty)}");
        }

        var payload = string.Join('&', parts);
        if (!string.IsNullOrWhiteSpace(passphrase))
        {
            payload = $"{payload}&passphrase={PayFastEncode(passphrase)}";
        }

        return payload;
    }

    private static string PayFastEncode(string value)
    {
        var normalizedValue = value.Trim().Replace("+", " ", StringComparison.Ordinal);
        var encoded = Uri.EscapeDataString(normalizedValue).Replace("%20", "+", StringComparison.Ordinal);
        return PercentEncodingRegex().Replace(encoded, match => match.Value.ToUpperInvariant());
    }

    [GeneratedRegex("%[0-9a-fA-F]{2}", RegexOptions.CultureInvariant)]
    private static partial Regex PercentEncodingRegex();

    private static string HtmlEncode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}

public sealed record PayFastCheckoutForm(
    string ActionUrl,
    IReadOnlyList<KeyValuePair<string, string>> Fields,
    string PaymentId);
