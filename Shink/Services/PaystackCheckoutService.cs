using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed class PaystackCheckoutService(
    HttpClient httpClient,
    IOptions<PaystackOptions> options,
    IOptions<SupabaseOptions>? supabaseOptions = null)
{
    private static readonly TimeSpan CheckoutSessionTtl = TimeSpan.FromMinutes(60);
    private readonly HttpClient _httpClient = httpClient;
    private readonly PaystackOptions _options = options.Value;
    private readonly SupabaseOptions? _supabaseOptions = supabaseOptions?.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.SecretKey) &&
        Uri.TryCreate(_options.InitializeUrl, UriKind.Absolute, out _) &&
        Uri.TryCreate(_options.VerifyUrl, UriKind.Absolute, out _);

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

        var callbackQuery = $"betaling=sukses&provider=paystack&plan={Uri.EscapeDataString(plan.Slug)}";
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            callbackQuery = $"{callbackQuery}&returnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        var callbackUrl = BuildAbsoluteUrl(httpContext, _options.CallbackUrlPath, callbackQuery);
        var amountInCents = (long)Math.Round(plan.Amount * 100m, MidpointRounding.AwayFromZero);
        var reusableSession = await TryGetReusableCheckoutSessionAsync(plan, email, amountInCents, callbackUrl, cancellationToken);
        if (reusableSession is not null)
        {
            return reusableSession;
        }

        var reference = BuildReference(plan.Slug);
        var metadata = new Dictionary<string, object?>
        {
            ["plan_slug"] = plan.Slug,
            ["tier_code"] = plan.TierCode,
            ["billing_period_months"] = plan.BillingPeriodMonths,
            ["is_subscription"] = plan.IsSubscription,
            ["subscription_key"] = reference
        };

        return await InitializeTransactionAsync(
            email,
            amountInCents,
            reference,
            callbackUrl,
            metadata,
            planCode,
            cancellationToken);
    }

    public async Task<PaystackCheckoutInitResult> InitializeCheckoutForEmailAsync(
        PaymentPlan plan,
        string email,
        HttpContext httpContext,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Paystack is nog nie volledig opgestel nie.");
        }

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

        var callbackQuery = $"betaling=sukses&provider=paystack&plan={Uri.EscapeDataString(plan.Slug)}";
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            callbackQuery = $"{callbackQuery}&returnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        var callbackUrl = BuildAbsoluteUrl(httpContext, _options.CallbackUrlPath, callbackQuery);
        var amountInCents = (long)Math.Round(plan.Amount * 100m, MidpointRounding.AwayFromZero);
        var reusableSession = await TryGetReusableCheckoutSessionAsync(plan, email, amountInCents, callbackUrl, cancellationToken);
        if (reusableSession is not null)
        {
            return reusableSession;
        }

        var reference = BuildReference(plan.Slug);
        var metadata = new Dictionary<string, object?>
        {
            ["plan_slug"] = plan.Slug,
            ["tier_code"] = plan.TierCode,
            ["billing_period_months"] = plan.BillingPeriodMonths,
            ["is_subscription"] = plan.IsSubscription,
            ["subscription_key"] = reference
        };

        return await InitializeTransactionAsync(
            email,
            amountInCents,
            reference,
            callbackUrl,
            metadata,
            planCode,
            cancellationToken);
    }

    public async Task<PaystackCheckoutInitResult> InitializeCheckoutForEmailAsync(
        PaymentPlan plan,
        string email,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(_options.PublicBaseUrl, UriKind.Absolute, out var publicBaseUri))
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Paystack publieke basis-URL is nog nie opgestel nie.");
        }

        if (!IsConfigured)
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Paystack is nog nie volledig opgestel nie.");
        }

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

        var callbackQuery = $"betaling=sukses&provider=paystack&plan={Uri.EscapeDataString(plan.Slug)}";
        var callbackUrl = BuildAbsoluteUrl(publicBaseUri.ToString(), _options.CallbackUrlPath, callbackQuery);
        var amountInCents = (long)Math.Round(plan.Amount * 100m, MidpointRounding.AwayFromZero);
        var reusableSession = await TryGetReusableCheckoutSessionAsync(plan, email, amountInCents, callbackUrl, cancellationToken);
        if (reusableSession is not null)
        {
            return reusableSession;
        }

        var reference = BuildReference(plan.Slug);
        var metadata = new Dictionary<string, object?>
        {
            ["plan_slug"] = plan.Slug,
            ["tier_code"] = plan.TierCode,
            ["billing_period_months"] = plan.BillingPeriodMonths,
            ["is_subscription"] = plan.IsSubscription,
            ["subscription_key"] = reference,
            ["source"] = "admin_subscription_recovery"
        };

        return await InitializeTransactionAsync(
            email,
            amountInCents,
            reference,
            callbackUrl,
            metadata,
            planCode,
            cancellationToken);
    }

    public async Task MarkCheckoutSessionStatusAsync(
        string? reference,
        string status,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference) ||
            string.IsNullOrWhiteSpace(status) ||
            !TryBuildSupabaseBaseUri(out var baseUri, out var apiKey))
        {
            return;
        }

        var normalizedStatus = status.Trim().ToLowerInvariant();
        if (normalizedStatus is not "pending" and not "paid" and not "expired" and not "cancelled" and not "failed")
        {
            return;
        }

        var uri = new Uri(
            baseUri,
            $"rest/v1/paystack_checkout_sessions?provider=eq.paystack&reference=eq.{Uri.EscapeDataString(reference.Trim())}");
        using var request = CreateSupabaseRequest(HttpMethod.Patch, uri, apiKey);
        request.Content = JsonContent.Create(new
        {
            status = normalizedStatus,
            updated_at = DateTimeOffset.UtcNow.UtcDateTime
        });
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

        try
        {
            using var _ = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
        }
    }

    public string? BuildCheckoutPageUrl(PaymentPlan plan)
    {
        if (!Uri.TryCreate(_options.PublicBaseUrl, UriKind.Absolute, out var publicBaseUri))
        {
            return null;
        }

        return new Uri(
            publicBaseUri,
            $"/betaal/{Uri.EscapeDataString(plan.Slug)}?provider=paystack").ToString();
    }

    public async Task<PaystackSubscriptionManageLinkResult> GenerateSubscriptionUpdateLinkAsync(
        string subscriptionCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return new PaystackSubscriptionManageLinkResult(false, ErrorMessage: "Paystack is nog nie volledig opgestel nie.");
        }

        if (string.IsNullOrWhiteSpace(subscriptionCode))
        {
            return new PaystackSubscriptionManageLinkResult(false, ErrorMessage: "Paystack intekeningkode ontbreek.");
        }

        var uri = new Uri(
            $"https://api.paystack.co/subscription/{Uri.EscapeDataString(subscriptionCode.Trim())}/manage/link");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PaystackSubscriptionManageLinkResult(
                false,
                ErrorMessage: $"Paystack kaart-opdateringskakel kon nie geskep word nie (HTTP {(int)response.StatusCode}).");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusNode) &&
                         statusNode.ValueKind == JsonValueKind.True;
            var link = root.TryGetProperty("data", out var dataNode)
                ? TryReadString(dataNode, "link")
                : null;

            if (!status || string.IsNullOrWhiteSpace(link))
            {
                var message = TryReadString(root, "message") ?? "Paystack kaart-opdateringskakel kon nie geskep word nie.";
                return new PaystackSubscriptionManageLinkResult(false, ErrorMessage: message);
            }

            return new PaystackSubscriptionManageLinkResult(true, Link: link);
        }
        catch (JsonException)
        {
            return new PaystackSubscriptionManageLinkResult(false, ErrorMessage: "Paystack kaart-opdateringsantwoord kon nie gelees word nie.");
        }
    }

    public async Task<PaystackSubscriptionDisableResult> DisableSubscriptionAsync(
        string subscriptionCode,
        string? emailToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return new PaystackSubscriptionDisableResult(false, ErrorMessage: "Paystack is nog nie volledig opgestel nie.");
        }

        if (string.IsNullOrWhiteSpace(subscriptionCode))
        {
            return new PaystackSubscriptionDisableResult(false, ErrorMessage: "Paystack intekeningkode ontbreek.");
        }

        var normalizedCode = subscriptionCode.Trim();
        var normalizedToken = emailToken?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            var tokenLookup = await FetchSubscriptionEmailTokenAsync(normalizedCode, cancellationToken);
            if (!tokenLookup.IsSuccess)
            {
                return new PaystackSubscriptionDisableResult(false, ErrorMessage: tokenLookup.ErrorMessage);
            }

            normalizedToken = tokenLookup.EmailToken;
        }

        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return new PaystackSubscriptionDisableResult(false, ErrorMessage: "Paystack kansellasie-token ontbreek.");
        }

        var payload = new
        {
            code = normalizedCode,
            token = normalizedToken
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.paystack.co/subscription/disable")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PaystackSubscriptionDisableResult(
                false,
                EmailToken: normalizedToken,
                ErrorMessage: $"Paystack kon nie die intekening kanselleer nie (HTTP {(int)response.StatusCode}).",
                RawPayload: body);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusNode) &&
                         statusNode.ValueKind == JsonValueKind.True;
            if (!status)
            {
                return new PaystackSubscriptionDisableResult(
                    false,
                    EmailToken: normalizedToken,
                    ErrorMessage: TryReadString(root, "message") ?? "Paystack kon nie die intekening kanselleer nie.",
                    RawPayload: body);
            }

            return new PaystackSubscriptionDisableResult(true, EmailToken: normalizedToken, RawPayload: body);
        }
        catch (JsonException)
        {
            return new PaystackSubscriptionDisableResult(
                false,
                EmailToken: normalizedToken,
                ErrorMessage: "Paystack kansellasie-antwoord kon nie gelees word nie.",
                RawPayload: body);
        }
    }

    public async Task<PaystackAuthorizationChargeResult> ChargeAuthorizationAsync(
        PaymentPlan plan,
        string email,
        string authorizationCode,
        string reference,
        string? subscriptionId = null,
        string? providerPaymentId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey) ||
            !Uri.TryCreate(_options.ChargeAuthorizationUrl, UriKind.Absolute, out var chargeUri))
        {
            return new PaystackAuthorizationChargeResult(false, ErrorMessage: "Paystack charge authorization is nog nie volledig opgestel nie.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return new PaystackAuthorizationChargeResult(false, ErrorMessage: "Kon nie 'n e-posadres vir betaling bepaal nie.");
        }

        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            return new PaystackAuthorizationChargeResult(false, ErrorMessage: "Paystack authorization code ontbreek.");
        }

        var metadata = new Dictionary<string, object?>
        {
            ["source"] = "subscription_authorization_retry",
            ["plan_slug"] = plan.Slug,
            ["tier_code"] = plan.TierCode,
            ["billing_period_months"] = plan.BillingPeriodMonths,
            ["subscription_id"] = subscriptionId,
            ["provider_payment_id"] = providerPaymentId
        };

        var payload = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["amount"] = (long)Math.Round(plan.Amount * 100m, MidpointRounding.AwayFromZero),
            ["authorization_code"] = authorizationCode.Trim(),
            ["reference"] = reference,
            ["currency"] = "ZAR",
            ["metadata"] = metadata,
            ["queue"] = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, chargeUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PaystackAuthorizationChargeResult(
                false,
                Reference: reference,
                ErrorMessage: $"Paystack outomatiese herlaai kon nie begin nie (HTTP {(int)response.StatusCode}).",
                RawPayload: body);
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
            var transactionStatus = TryReadString(dataNode, "status");
            var parsedReference = TryReadString(dataNode, "reference") ?? reference;
            var transactionId = TryReadString(dataNode, "id") ?? parsedReference;

            if (status && IsSuccessfulPaystackStatus(transactionStatus))
            {
                return new PaystackAuthorizationChargeResult(
                    true,
                    Reference: parsedReference,
                    TransactionStatus: transactionStatus,
                    ProviderTransactionId: transactionId,
                    PaidAt: ParseDateTimeOffset(TryReadString(dataNode, "paid_at") ?? TryReadString(dataNode, "transaction_date")),
                    RawPayload: body);
            }

            var message = TryReadString(dataNode, "gateway_response") ??
                          TryReadString(dataNode, "message") ??
                          TryReadString(root, "message") ??
                          "Paystack outomatiese herlaai was nie suksesvol nie.";
            return new PaystackAuthorizationChargeResult(
                false,
                Reference: parsedReference,
                TransactionStatus: transactionStatus,
                ProviderTransactionId: transactionId,
                ErrorMessage: message,
                RawPayload: body);
        }
        catch (JsonException)
        {
            return new PaystackAuthorizationChargeResult(
                false,
                Reference: reference,
                ErrorMessage: "Paystack outomatiese herlaai antwoord kon nie gelees word nie.",
                RawPayload: body);
        }
    }

    public async Task<PaystackCheckoutInitResult> InitializeStoreCheckoutAsync(
        StorePaystackCheckoutRequest checkout,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Paystack is nog nie volledig opgestel nie.");
        }

        if (string.IsNullOrWhiteSpace(checkout.CustomerEmail))
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "E-posadres is verpligtend vir betaling.");
        }

        if (checkout.AmountInCents <= 0)
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Bedrag moet groter as nul wees.");
        }

        var callbackUrl = BuildAbsoluteUrl(httpContext, checkout.CallbackPath, null);
        var cancelUrl = BuildAbsoluteUrl(httpContext, checkout.CancelPath, null);
        var metadata = new Dictionary<string, object?>
        {
            ["checkout_kind"] = "store",
            ["order_reference"] = checkout.OrderReference,
            ["product_slug"] = checkout.ProductSlug,
            ["product_name"] = checkout.ProductName,
            ["quantity"] = checkout.Quantity,
            ["item_summary"] = checkout.ItemSummary,
            ["customer_name"] = checkout.CustomerName,
            ["customer_phone"] = checkout.CustomerPhone,
            ["cancel_action"] = cancelUrl,
            ["custom_fields"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["display_name"] = "Items",
                    ["variable_name"] = "item_summary",
                    ["value"] = checkout.ItemSummary
                },
                new Dictionary<string, object?>
                {
                    ["display_name"] = "Total Items",
                    ["variable_name"] = "quantity",
                    ["value"] = checkout.Quantity.ToString()
                },
                new Dictionary<string, object?>
                {
                    ["display_name"] = "Order Reference",
                    ["variable_name"] = "order_reference",
                    ["value"] = checkout.OrderReference
                }
            }
        };

        return await InitializeTransactionAsync(
            checkout.CustomerEmail,
            checkout.AmountInCents,
            checkout.OrderReference,
            callbackUrl,
            metadata,
            planCode: null,
            cancellationToken);
    }

    public async Task<PaystackVerifyResult> VerifyTransactionAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new PaystackVerifyResult(false, ErrorMessage: "Paystack is nog nie volledig opgestel nie.");
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            return new PaystackVerifyResult(false, ErrorMessage: "Transaksie verwysing ontbreek.");
        }

        if (!Uri.TryCreate(_options.VerifyUrl, UriKind.Absolute, out _))
        {
            return new PaystackVerifyResult(false, ErrorMessage: "Paystack Verify URL is ongeldig.");
        }

        var verifyUri = new Uri($"{_options.VerifyUrl.TrimEnd('/')}/{Uri.EscapeDataString(reference.Trim())}");

        using var request = new HttpRequestMessage(HttpMethod.Get, verifyUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PaystackVerifyResult(
                false,
                ErrorMessage: $"Paystack betaling kon nie bevestig word nie (HTTP {(int)response.StatusCode}).",
                RawPayload: body);
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

            if (!status || dataNode.ValueKind != JsonValueKind.Object)
            {
                var message = TryReadString(root, "message") ?? "Paystack betaling kon nie bevestig word nie.";
                return new PaystackVerifyResult(false, ErrorMessage: message, RawPayload: body);
            }

            return new PaystackVerifyResult(
                true,
                Reference: TryReadString(dataNode, "reference") ?? reference,
                TransactionStatus: TryReadString(dataNode, "status"),
                AmountInCents: TryReadInt64(dataNode, "amount"),
                Currency: TryReadString(dataNode, "currency"),
                CustomerEmail: TryReadNestedString(dataNode, "customer", "email"),
                ProviderTransactionId: TryReadString(dataNode, "id") ?? TryReadString(dataNode, "reference"),
                AuthorizationCode: TryReadNestedString(dataNode, "authorization", "authorization_code")
                    ?? TryReadString(dataNode, "authorization_code"),
                SubscriptionCode: TryReadNestedString(dataNode, "subscription", "subscription_code")
                    ?? TryReadString(dataNode, "subscription_code"),
                EmailToken: TryReadNestedString(dataNode, "subscription", "email_token")
                    ?? TryReadString(dataNode, "email_token"),
                CustomerCode: TryReadNestedString(dataNode, "customer", "customer_code")
                    ?? TryReadString(dataNode, "customer_code"),
                PaidAt: ParseDateTimeOffset(TryReadString(dataNode, "paid_at")),
                GatewayResponse: TryReadString(dataNode, "gateway_response") ?? TryReadString(dataNode, "message"),
                RawPayload: body);
        }
        catch (JsonException)
        {
            return new PaystackVerifyResult(false, ErrorMessage: "Paystack verify antwoord kon nie gelees word nie.", RawPayload: body);
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

    private async Task<PaystackCheckoutInitResult> InitializeTransactionAsync(
        string email,
        long amountInCents,
        string reference,
        string callbackUrl,
        Dictionary<string, object?> metadata,
        string? planCode,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_options.InitializeUrl, UriKind.Absolute, out var initializeUri))
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Paystack Initialize URL is ongeldig.");
        }

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

            var result = new PaystackCheckoutInitResult(
                true,
                AuthorizationUrl: authorizationUrl,
                Reference: parsedReference);
            await StoreCheckoutSessionAsync(
                email,
                amountInCents,
                parsedReference,
                authorizationUrl,
                callbackUrl,
                metadata,
                cancellationToken);
            return result;
        }
        catch (JsonException)
        {
            return new PaystackCheckoutInitResult(false, ErrorMessage: "Paystack checkout antwoord kon nie gelees word nie.");
        }
    }

    private async Task<PaystackCheckoutInitResult?> TryGetReusableCheckoutSessionAsync(
        PaymentPlan plan,
        string email,
        long amountInCents,
        string callbackUrl,
        CancellationToken cancellationToken)
    {
        if (!plan.IsSubscription ||
            !TryBuildSupabaseBaseUri(out var baseUri, out var apiKey))
        {
            return null;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var filter = string.Join(
            "&",
            "select=reference,authorization_url,expires_at",
            "provider=eq.paystack",
            "checkout_kind=eq.subscription",
            "status=eq.pending",
            $"customer_email=eq.{Uri.EscapeDataString(NormalizeEmail(email))}",
            $"tier_code=eq.{Uri.EscapeDataString(plan.TierCode)}",
            $"amount_in_cents=eq.{amountInCents}",
            "currency=eq.ZAR",
            $"callback_url=eq.{Uri.EscapeDataString(callbackUrl)}",
            $"expires_at=gt.{Uri.EscapeDataString(nowUtc.UtcDateTime.ToString("O"))}",
            "order=created_at.desc",
            "limit=1");
        var uri = new Uri(baseUri, $"rest/v1/paystack_checkout_sessions?{filter}");

        try
        {
            using var request = CreateSupabaseRequest(HttpMethod.Get, uri, apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var sessions = await JsonSerializer.DeserializeAsync<List<PaystackCheckoutSessionRow>>(stream, cancellationToken: cancellationToken)
                ?? [];
            var session = sessions.FirstOrDefault();
            if (session is null ||
                string.IsNullOrWhiteSpace(session.Reference) ||
                string.IsNullOrWhiteSpace(session.AuthorizationUrl) ||
                session.ExpiresAt <= nowUtc)
            {
                return null;
            }

            return new PaystackCheckoutInitResult(
                true,
                AuthorizationUrl: session.AuthorizationUrl,
                Reference: session.Reference);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private async Task StoreCheckoutSessionAsync(
        string email,
        long amountInCents,
        string reference,
        string authorizationUrl,
        string callbackUrl,
        Dictionary<string, object?> metadata,
        CancellationToken cancellationToken)
    {
        if (!TryReadMetadataString(metadata, "plan_slug", out var planSlug) ||
            !TryReadMetadataString(metadata, "tier_code", out var tierCode) ||
            !TryBuildSupabaseBaseUri(out var baseUri, out var apiKey))
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        await ExpireStaleCheckoutSessionsAsync(
            baseUri,
            apiKey,
            email,
            tierCode,
            amountInCents,
            callbackUrl,
            nowUtc,
            cancellationToken);

        var uri = new Uri(baseUri, "rest/v1/paystack_checkout_sessions");
        using var request = CreateSupabaseRequest(HttpMethod.Post, uri, apiKey);
        request.Content = JsonContent.Create(new
        {
            provider = "paystack",
            checkout_kind = "subscription",
            customer_email = NormalizeEmail(email),
            plan_slug = planSlug,
            tier_code = tierCode,
            amount_in_cents = amountInCents,
            currency = "ZAR",
            callback_url = callbackUrl,
            reference,
            authorization_url = authorizationUrl,
            status = "pending",
            expires_at = nowUtc.Add(CheckoutSessionTtl).UtcDateTime,
            metadata
        });
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

        try
        {
            using var _ = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
        }
    }

    private async Task ExpireStaleCheckoutSessionsAsync(
        Uri baseUri,
        string apiKey,
        string email,
        string tierCode,
        long amountInCents,
        string callbackUrl,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var filter = string.Join(
            "&",
            "provider=eq.paystack",
            "checkout_kind=eq.subscription",
            "status=eq.pending",
            $"customer_email=eq.{Uri.EscapeDataString(NormalizeEmail(email))}",
            $"tier_code=eq.{Uri.EscapeDataString(tierCode)}",
            $"amount_in_cents=eq.{amountInCents}",
            "currency=eq.ZAR",
            $"callback_url=eq.{Uri.EscapeDataString(callbackUrl)}",
            $"expires_at=lt.{Uri.EscapeDataString(nowUtc.UtcDateTime.ToString("O"))}");
        var uri = new Uri(baseUri, $"rest/v1/paystack_checkout_sessions?{filter}");
        using var request = CreateSupabaseRequest(HttpMethod.Patch, uri, apiKey);
        request.Content = JsonContent.Create(new
        {
            status = "expired",
            updated_at = nowUtc.UtcDateTime
        });
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

        try
        {
            using var _ = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
        }
    }

    private bool TryBuildSupabaseBaseUri(out Uri baseUri, out string apiKey)
    {
        baseUri = default!;
        apiKey = _supabaseOptions?.ServiceRoleKey ?? string.Empty;
        return _supabaseOptions is not null &&
               !string.IsNullOrWhiteSpace(apiKey) &&
               Uri.TryCreate(_supabaseOptions.Url, UriKind.Absolute, out baseUri!);
    }

    private static HttpRequestMessage CreateSupabaseRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static bool TryReadMetadataString(Dictionary<string, object?> values, string key, out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(key, out var rawValue) ||
            rawValue is not string rawText ||
            string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        value = rawText.Trim();
        return true;
    }

    private string BuildAbsoluteUrl(HttpContext httpContext, string path, string? queryString)
    {
        var baseUri = ResolveBaseUrl(httpContext);
        return BuildAbsoluteUrl(baseUri, path, queryString);
    }

    private static string BuildAbsoluteUrl(string baseUri, string path, string? queryString)
    {
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

    private static string? TryReadNestedString(JsonElement element, string firstProperty, string secondProperty)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(firstProperty, out var nested))
        {
            return null;
        }

        return TryReadString(nested, secondProperty);
    }

    private static long TryReadInt64(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var node))
        {
            return 0;
        }

        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var intValue))
        {
            return intValue;
        }

        if (node.ValueKind == JsonValueKind.String &&
            long.TryParse(node.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;

    public async Task<PaystackSubscriptionLookupResult> GetSubscriptionAsync(
        string subscriptionCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return new PaystackSubscriptionLookupResult(false, ErrorMessage: "Paystack is nog nie volledig opgestel nie.");
        }

        if (string.IsNullOrWhiteSpace(subscriptionCode))
        {
            return new PaystackSubscriptionLookupResult(false, ErrorMessage: "Paystack intekeningkode ontbreek.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.paystack.co/subscription/{Uri.EscapeDataString(subscriptionCode.Trim())}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PaystackSubscriptionLookupResult(
                false,
                ErrorMessage: $"Paystack intekening kon nie gelees word nie (HTTP {(int)response.StatusCode}).",
                RawPayload: body);
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
            if (!status || dataNode.ValueKind != JsonValueKind.Object)
            {
                return new PaystackSubscriptionLookupResult(
                    false,
                    ErrorMessage: TryReadString(root, "message") ?? "Paystack intekening kon nie gelees word nie.",
                    RawPayload: body);
            }

            return new PaystackSubscriptionLookupResult(
                true,
                SubscriptionCode: TryReadString(dataNode, "subscription_code") ?? subscriptionCode.Trim(),
                Status: TryReadString(dataNode, "status"),
                NextPaymentDate: ParseDateTimeOffset(TryReadString(dataNode, "next_payment_date")),
                AuthorizationCode: TryReadNestedString(dataNode, "authorization", "authorization_code")
                    ?? TryReadString(dataNode, "authorization_code"),
                EmailToken: TryReadString(dataNode, "email_token"),
                CustomerCode: TryReadNestedString(dataNode, "customer", "customer_code")
                    ?? TryReadString(dataNode, "customer_code"),
                RawPayload: body);
        }
        catch (JsonException)
        {
            return new PaystackSubscriptionLookupResult(
                false,
                ErrorMessage: "Paystack intekeningantwoord kon nie gelees word nie.",
                RawPayload: body);
        }
    }

    private async Task<PaystackSubscriptionDisableResult> FetchSubscriptionEmailTokenAsync(
        string subscriptionCode,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.paystack.co/subscription/{Uri.EscapeDataString(subscriptionCode)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PaystackSubscriptionDisableResult(
                false,
                ErrorMessage: $"Paystack intekening-token kon nie gelees word nie (HTTP {(int)response.StatusCode}).",
                RawPayload: body);
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
            var token = TryReadString(dataNode, "email_token");
            if (!status || string.IsNullOrWhiteSpace(token))
            {
                return new PaystackSubscriptionDisableResult(
                    false,
                    ErrorMessage: TryReadString(root, "message") ?? "Paystack intekening-token ontbreek.",
                    RawPayload: body);
            }

            return new PaystackSubscriptionDisableResult(true, EmailToken: token.Trim(), RawPayload: body);
        }
        catch (JsonException)
        {
            return new PaystackSubscriptionDisableResult(
                false,
                ErrorMessage: "Paystack intekening-token antwoord kon nie gelees word nie.",
                RawPayload: body);
        }
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

    private static bool IsSuccessfulPaystackStatus(string? status) =>
        string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "successful", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase);

    private sealed class PaystackCheckoutSessionRow
    {
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("authorization_url")]
        public string? AuthorizationUrl { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }
    }
}

public sealed record PaystackCheckoutInitResult(
    bool IsSuccess,
    string? AuthorizationUrl = null,
    string? Reference = null,
    string? ErrorMessage = null);

public sealed record PaystackSubscriptionManageLinkResult(
    bool IsSuccess,
    string? Link = null,
    string? ErrorMessage = null);

public sealed record PaystackSubscriptionDisableResult(
    bool IsSuccess,
    string? EmailToken = null,
    string? ErrorMessage = null,
    string? RawPayload = null);

public sealed record PaystackAuthorizationChargeResult(
    bool IsSuccess,
    string? Reference = null,
    string? TransactionStatus = null,
    string? ProviderTransactionId = null,
    DateTimeOffset? PaidAt = null,
    string? ErrorMessage = null,
    string? RawPayload = null);

public sealed record StorePaystackCheckoutRequest(
    string OrderReference,
    string ProductSlug,
    string ProductName,
    int Quantity,
    string ItemSummary,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    long AmountInCents,
    string CallbackPath,
    string CancelPath);

public sealed record PaystackVerifyResult(
    bool IsSuccess,
    string? Reference = null,
    string? TransactionStatus = null,
    long AmountInCents = 0,
    string? Currency = null,
    string? CustomerEmail = null,
    string? ProviderTransactionId = null,
    string? AuthorizationCode = null,
    string? SubscriptionCode = null,
    string? EmailToken = null,
    string? CustomerCode = null,
    DateTimeOffset? PaidAt = null,
    string? GatewayResponse = null,
    string? ErrorMessage = null,
    string? RawPayload = null);

public sealed record PaystackSubscriptionLookupResult(
    bool IsSuccess,
    string? SubscriptionCode = null,
    string? Status = null,
    DateTimeOffset? NextPaymentDate = null,
    string? AuthorizationCode = null,
    string? EmailToken = null,
    string? CustomerCode = null,
    string? ErrorMessage = null,
    string? RawPayload = null);
