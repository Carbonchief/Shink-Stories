using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shink.Components.Content;

namespace Shink.Services;

public sealed class MobileStoreOptions
{
    public const string SectionName = "MobileStore";

    public string AppleIssuerId { get; set; } = string.Empty;
    public string AppleKeyId { get; set; } = string.Empty;
    public string ApplePrivateKey { get; set; } = string.Empty;
    public string AppleBundleId { get; set; } = "com.schink.stories.mobile";
    public string GooglePackageName { get; set; } = "com.schink.stories.mobile";
    public string GoogleServiceAccountJson { get; set; } = string.Empty;
}

public sealed record MobileStorePurchaseRequest(
    string Provider,
    string ProductId,
    string ProviderPaymentId,
    string? ProviderTransactionId,
    string? ProviderToken,
    string? ExpectedAccountEmail = null);

public sealed record MobileStoreEntitlementResponse(
    bool IsActive,
    string Message,
    string? Provider,
    string? ProductId,
    DateTimeOffset? AccessEndsAtUtc,
    bool IsRetryable = false);

public sealed class MobileStoreEntitlementService(
    HttpClient httpClient,
    IOptions<MobileStoreOptions> options,
    ISubscriptionLedgerService subscriptionLedgerService,
    ILogger<MobileStoreEntitlementService> logger,
    IGratisSubscriberEmailSequenceService? gratisSubscriberEmailSequenceService = null)
{
    private const string GoogleTokenUrl = "https://oauth2.googleapis.com/token";
    private const string GooglePublisherScope = "https://www.googleapis.com/auth/androidpublisher";
    private const string GooglePublisherBaseUrl = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications";

    private readonly HttpClient _httpClient = httpClient;
    private readonly MobileStoreOptions _options = options.Value;
    private readonly ISubscriptionLedgerService _subscriptionLedgerService = subscriptionLedgerService;
    private readonly ILogger<MobileStoreEntitlementService> _logger = logger;
    private readonly IGratisSubscriberEmailSequenceService? _gratisSubscriberEmailSequenceService = gratisSubscriberEmailSequenceService;

    public async Task<MobileStoreEntitlementResponse> VerifyAndRecordAsync(
        string email,
        MobileStorePurchaseRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Failure("Die winkelbetaling kon nie bevestig word nie.");
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedAccountEmail) &&
            !string.Equals(email.Trim(), request.ExpectedAccountEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            return Failure("Die aangemelde rekening het verander. Teken by die aankooprekening in.", isRetryable: true);

        var provider = request.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        var productId = request.ProductId?.Trim() ?? string.Empty;
        var plan = PaymentPlanCatalog.FindMobileStorePlan(productId);
        if (provider is not ("apple" or "google_play") ||
            plan is null ||
            !plan.IsSubscription)
        {
            return Failure("Die winkelproduk is nie 'n geldige huishoudelike plan nie.", provider, productId);
        }

        var check = await CheckAsync(provider, productId,
            request.ProviderTransactionId ?? request.ProviderPaymentId, request.ProviderToken, email, cancellationToken);
        var verifiedPurchase = check.Purchase;
        if (verifiedPurchase is null)
        {
            return Failure("Die winkelbetaling kon nie bevestig word nie. Jou rekening is nie verander nie.",
                provider, productId, isRetryable: !check.IsInactive);
        }

        var persistResult = await _subscriptionLedgerService.RecordVerifiedStoreSubscriptionAsync(
            email,
            verifiedPurchase.Provider,
            verifiedPurchase.ProductId,
            verifiedPurchase.ProviderPaymentId,
            verifiedPurchase.ProviderTransactionId,
            verifiedPurchase.ProviderToken,
            verifiedPurchase.SubscribedAtUtc,
            verifiedPurchase.AccessEndsAtUtc,
            cancellationToken);
        if (!persistResult.IsSuccess)
        {
            return Failure(
                persistResult.ErrorMessage ?? "Die winkelintekening kon nie nou geaktiveer word nie.",
                provider,
                productId, isRetryable: true);
        }

        // Acknowledgment is safe only after durable access. A failed attempt is
        // retried by reconciliation, including if the client never opens again.
        await AcknowledgeAsync(verifiedPurchase, cancellationToken);

        if (_gratisSubscriberEmailSequenceService is not null)
        {
            try { await _gratisSubscriberEmailSequenceService.MarkPaidAsync(email, cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Paid subscriber email-sequence cleanup will need retry; store access is already recorded.");
            }
        }

        return new MobileStoreEntitlementResponse(
            IsActive: true,
            Message: "Jou winkelintekening is bevestig.",
            Provider: verifiedPurchase.Provider,
            ProductId: verifiedPurchase.ProductId,
            AccessEndsAtUtc: verifiedPurchase.AccessEndsAtUtc);
    }

    internal async Task<StorePurchaseCheck> CheckAsync(string provider, string productId,
        string? transactionId, string? token, string? expectedEmail = null, CancellationToken cancellationToken = default)
    {
        if (provider == "google_play") return await VerifyGooglePurchaseAsync(productId, token, expectedEmail, cancellationToken);
        if (provider != "apple") return new(null);
        var api = new AppleAppStoreServerApi(_httpClient, _options, _logger);
        var check = await api.CheckSubscriptionAsync(productId, transactionId, cancellationToken);
        var item = check.Subscription;
        return item is null ? new(null, check.IsInactive) : new(new VerifiedStorePurchase(
            "apple", item.ProductId, item.OriginalTransactionId, item.TransactionId, null,
            item.OriginalPurchaseDateUtc, item.ExpiresAtUtc));
    }

    private async Task<StorePurchaseCheck> VerifyGooglePurchaseAsync(
        string productId,
        string? purchaseToken,
        string? expectedEmail,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken) ||
            string.IsNullOrWhiteSpace(_options.GoogleServiceAccountJson))
        {
            _logger.LogWarning("Google Play store verification is not configured or did not include a purchase token.");
            return new(null);
        }

        var accessToken = await GetGoogleAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new(null);
        }

        var packageName = Uri.EscapeDataString(_options.GooglePackageName.Trim());
        var escapedToken = Uri.EscapeDataString(purchaseToken.Trim());
        var endpoint = $"{GooglePublisherBaseUrl}/{packageName}/purchases/subscriptionsv2/tokens/{escapedToken}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Play subscription verification failed. status={Status}", (int)response.StatusCode);
                return new(null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var accountId = TryReadString(root, "externalAccountIdentifiers", "obfuscatedExternalAccountId");
            if (!string.IsNullOrWhiteSpace(expectedEmail) && !string.IsNullOrWhiteSpace(accountId) &&
                !string.Equals(accountId, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedEmail.Trim().ToLowerInvariant()))).ToLowerInvariant(), StringComparison.Ordinal))
                return new(null);
            var state = TryReadString(root, "subscriptionState");
            if (!root.TryGetProperty("lineItems", out var lineItems) ||
                lineItems.ValueKind != JsonValueKind.Array)
            {
                return new(null);
            }

            var lineItem = lineItems.EnumerateArray()
                .FirstOrDefault(item => string.Equals(
                    TryReadString(item, "productId"),
                    productId,
                    StringComparison.Ordinal));
            if (lineItem.ValueKind != JsonValueKind.Object)
            {
                return new(null);
            }

            if (state is "SUBSCRIPTION_STATE_EXPIRED" or "SUBSCRIPTION_STATE_ON_HOLD" or "SUBSCRIPTION_STATE_PAUSED") return new(null, true);
            if (state is not ("SUBSCRIPTION_STATE_ACTIVE" or "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" or "SUBSCRIPTION_STATE_CANCELED")) return new(null);

            var accessEndsAtUtc = TryParseDateTimeOffset(TryReadString(lineItem, "expiryTime"));
            if (accessEndsAtUtc is not { } expiry) return new(null);
            if (expiry <= DateTimeOffset.UtcNow) return new(null, true);

            return new(new VerifiedStorePurchase(
                Provider: "google_play",
                ProductId: productId,
                ProviderPaymentId: purchaseToken.Trim(),
                ProviderTransactionId: TryReadString(lineItem, "latestSuccessfulOrderId"),
                ProviderToken: purchaseToken.Trim(),
                SubscribedAtUtc: TryParseDateTimeOffset(TryReadString(root, "startTime")) ?? DateTimeOffset.UtcNow,
                AccessEndsAtUtc: expiry,
                NeedsAcknowledgment: TryReadString(root, "acknowledgementState") == "ACKNOWLEDGEMENT_STATE_PENDING",
                LinkedPurchaseToken: TryReadString(root, "linkedPurchaseToken")));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Google Play subscription verification request failed.");
            return new(null);
        }
    }

    internal async Task<bool> AcknowledgeAsync(VerifiedStorePurchase purchase, CancellationToken cancellationToken)
    {
        if (purchase.Provider != "google_play" || !purchase.NeedsAcknowledgment) return true;
        var token = await GetGoogleAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var endpoint = $"{GooglePublisherBaseUrl}/{Uri.EscapeDataString(_options.GooglePackageName)}/purchases/subscriptions/{Uri.EscapeDataString(purchase.ProductId)}/tokens/{Uri.EscapeDataString(purchase.ProviderToken!)}:acknowledge";
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
    }

    private async Task<string?> GetGoogleAccessTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var serviceAccount = JsonDocument.Parse(_options.GoogleServiceAccountJson);
            var root = serviceAccount.RootElement;
            var clientEmail = TryReadString(root, "client_email");
            var privateKey = TryReadString(root, "private_key")?.Replace("\\n", "\n", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(clientEmail) || string.IsNullOrWhiteSpace(privateKey))
            {
                _logger.LogWarning("Google Play service account JSON is missing client email or private key.");
                return null;
            }

            var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = clientEmail,
                scope = GooglePublisherScope,
                aud = GoogleTokenUrl,
                iat = issuedAt,
                exp = issuedAt + 3600
            }));
            var unsignedToken = $"{header}.{claims}";

            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey);
            var signature = rsa.SignData(
                Encoding.UTF8.GetBytes(unsignedToken),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var assertion = $"{unsignedToken}.{Base64UrlEncode(signature)}";

            using var request = new HttpRequestMessage(HttpMethod.Post, GoogleTokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion
                })
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Play access token request failed. status={Status}", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var tokenDocument = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return TryReadString(tokenDocument.RootElement, "access_token");
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Google Play access token generation failed.");
            return null;
        }
    }

    private static string? TryReadString(JsonElement root, params string[] path)
    {
        var node = root;
        foreach (var segment in path)
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(segment, out node))
            {
                return null;
            }
        }

        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString(),
            JsonValueKind.Number => node.GetRawText(),
            _ => null
        };
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static MobileStoreEntitlementResponse Failure(
        string message,
        string? provider = null,
        string? productId = null, bool isRetryable = false) =>
        new(
            IsActive: false,
            Message: message,
            Provider: provider,
            ProductId: productId,
            AccessEndsAtUtc: null, IsRetryable: isRetryable);

    internal sealed record StorePurchaseCheck(VerifiedStorePurchase? Purchase, bool IsInactive = false);

    internal sealed record VerifiedStorePurchase(
        string Provider,
        string ProductId,
        string ProviderPaymentId,
        string? ProviderTransactionId,
        string? ProviderToken,
        DateTimeOffset SubscribedAtUtc,
        DateTimeOffset? AccessEndsAtUtc, bool NeedsAcknowledgment = false, string? LinkedPurchaseToken = null);

}
