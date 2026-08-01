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

    public string AppleSharedSecret { get; set; } = string.Empty;
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
    string? ReceiptData);

public sealed record MobileStoreEntitlementResponse(
    bool IsActive,
    string Message,
    string? Provider,
    string? ProductId,
    DateTimeOffset? AccessEndsAtUtc);

public sealed class MobileStoreEntitlementService(
    HttpClient httpClient,
    IOptions<MobileStoreOptions> options,
    ISubscriptionLedgerService subscriptionLedgerService,
    ILogger<MobileStoreEntitlementService> logger)
{
    private const string AppleProductionReceiptUrl = "https://buy.itunes.apple.com/verifyReceipt";
    private const string AppleSandboxReceiptUrl = "https://sandbox.itunes.apple.com/verifyReceipt";
    private const string GoogleTokenUrl = "https://oauth2.googleapis.com/token";
    private const string GooglePublisherScope = "https://www.googleapis.com/auth/androidpublisher";
    private const string GooglePublisherBaseUrl = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications";

    private readonly HttpClient _httpClient = httpClient;
    private readonly MobileStoreOptions _options = options.Value;
    private readonly ISubscriptionLedgerService _subscriptionLedgerService = subscriptionLedgerService;
    private readonly ILogger<MobileStoreEntitlementService> _logger = logger;

    public async Task<MobileStoreEntitlementResponse> VerifyAndRecordAsync(
        string email,
        MobileStorePurchaseRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Failure("Die winkelbetaling kon nie bevestig word nie.");
        }

        var provider = request.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        var productId = request.ProductId?.Trim() ?? string.Empty;
        var plan = PaymentPlanCatalog.FindBySlug(productId) ??
                   PaymentPlanCatalog.FindByStoreProductId(productId);
        if (provider is not ("apple" or "google_play") ||
            plan is null ||
            plan.IsSchoolPlan ||
            plan.IsAdminOnly ||
            !plan.IsSubscription)
        {
            return Failure("Die winkelproduk is nie 'n geldige huishoudelike plan nie.", provider, productId);
        }

        VerifiedStorePurchase? verifiedPurchase = provider switch
        {
            "apple" => await VerifyApplePurchaseAsync(productId, request.ReceiptData, cancellationToken),
            "google_play" => await VerifyGooglePurchaseAsync(productId, request.ProviderToken, cancellationToken),
            _ => null
        };

        if (verifiedPurchase is null)
        {
            return Failure(
                "Die winkelbetaling kon nie bevestig word nie. Jou rekening is nie verander nie.",
                provider,
                productId);
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
                productId);
        }

        return new MobileStoreEntitlementResponse(
            IsActive: true,
            Message: "Jou winkelintekening is bevestig.",
            Provider: verifiedPurchase.Provider,
            ProductId: verifiedPurchase.ProductId,
            AccessEndsAtUtc: verifiedPurchase.AccessEndsAtUtc);
    }

    private async Task<VerifiedStorePurchase?> VerifyApplePurchaseAsync(
        string productId,
        string? receiptData,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(receiptData) ||
            string.IsNullOrWhiteSpace(_options.AppleSharedSecret))
        {
            _logger.LogWarning("Apple store verification is not configured or did not include receipt data.");
            return null;
        }

        var payload = new Dictionary<string, object?>
        {
            ["receipt-data"] = receiptData,
            ["password"] = _options.AppleSharedSecret,
            ["exclude-old-transactions"] = false
        };

        var verification = await SendAppleReceiptAsync(
            AppleProductionReceiptUrl,
            payload,
            cancellationToken);
        if (verification.Status == 21007)
        {
            verification.Document?.Dispose();
            verification = await SendAppleReceiptAsync(
                AppleSandboxReceiptUrl,
                payload,
                cancellationToken);
        }

        if (verification.Status != 0 || verification.Document is null)
        {
            _logger.LogWarning("Apple store receipt verification failed. status={Status}", verification.Status);
            verification.Document?.Dispose();
            return null;
        }

        using (verification.Document)
        {
            var root = verification.Document.RootElement;
            var bundleId = TryReadString(root, "receipt", "bundle_id");
            if (!string.Equals(bundleId, _options.AppleBundleId, StringComparison.Ordinal))
            {
                _logger.LogWarning("Apple store receipt bundle identifier did not match the mobile app.");
                return null;
            }

            var records = new List<AppleReceiptRecord>();
            AddAppleReceiptRecords(records, root, "latest_receipt_info");
            if (records.Count == 0)
            {
                AddAppleReceiptRecords(records, root, "receipt", "in_app");
            }

            var matchingRecord = records
                .Where(record => string.Equals(record.ProductId, productId, StringComparison.Ordinal))
                .OrderByDescending(record => record.ExpiresAtUtc ?? DateTimeOffset.MinValue)
                .FirstOrDefault();
            if (matchingRecord is null ||
                matchingRecord.ExpiresAtUtc is not { } accessEndsAtUtc ||
                accessEndsAtUtc <= DateTimeOffset.UtcNow ||
                matchingRecord.CancelledAtUtc is not null)
            {
                return null;
            }

            var paymentId = matchingRecord.OriginalTransactionId ?? matchingRecord.TransactionId;
            if (string.IsNullOrWhiteSpace(paymentId))
            {
                return null;
            }

            return new VerifiedStorePurchase(
                Provider: "apple",
                ProductId: productId,
                ProviderPaymentId: paymentId,
                ProviderTransactionId: matchingRecord.TransactionId,
                ProviderToken: null,
                SubscribedAtUtc: matchingRecord.PurchasedAtUtc ?? DateTimeOffset.UtcNow,
                AccessEndsAtUtc: accessEndsAtUtc);
        }
    }

    private async Task<AppleReceiptVerification> SendAppleReceiptAsync(
        string endpoint,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var status = TryReadInt32(document.RootElement, "status") ?? -1;
            return new AppleReceiptVerification(status, document);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Apple store receipt verification request failed.");
            return new AppleReceiptVerification(-1, null);
        }
    }

    private async Task<VerifiedStorePurchase?> VerifyGooglePurchaseAsync(
        string productId,
        string? purchaseToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken) ||
            string.IsNullOrWhiteSpace(_options.GoogleServiceAccountJson))
        {
            _logger.LogWarning("Google Play store verification is not configured or did not include a purchase token.");
            return null;
        }

        var accessToken = await GetGoogleAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
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
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var state = TryReadString(root, "subscriptionState");
            if (state is not ("SUBSCRIPTION_STATE_ACTIVE" or
                              "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" or
                              "SUBSCRIPTION_STATE_CANCELED"))
            {
                return null;
            }

            if (!root.TryGetProperty("lineItems", out var lineItems) ||
                lineItems.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var lineItem = lineItems.EnumerateArray()
                .FirstOrDefault(item => string.Equals(
                    TryReadString(item, "productId"),
                    productId,
                    StringComparison.Ordinal));
            if (lineItem.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var accessEndsAtUtc = TryParseDateTimeOffset(TryReadString(lineItem, "expiryTime"));
            if (accessEndsAtUtc is not { } expiry || expiry <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            return new VerifiedStorePurchase(
                Provider: "google_play",
                ProductId: productId,
                ProviderPaymentId: purchaseToken.Trim(),
                ProviderTransactionId: TryReadString(lineItem, "latestSuccessfulOrderId"),
                ProviderToken: purchaseToken.Trim(),
                SubscribedAtUtc: TryParseDateTimeOffset(TryReadString(root, "startTime")) ?? DateTimeOffset.UtcNow,
                AccessEndsAtUtc: expiry);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Google Play subscription verification request failed.");
            return null;
        }
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

    private static void AddAppleReceiptRecords(
        ICollection<AppleReceiptRecord> records,
        JsonElement root,
        params string[] path)
    {
        var node = root;
        foreach (var segment in path)
        {
            if (!node.TryGetProperty(segment, out node))
            {
                return;
            }
        }

        if (node.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in node.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            records.Add(new AppleReceiptRecord(
                ProductId: TryReadString(item, "product_id"),
                OriginalTransactionId: TryReadString(item, "original_transaction_id"),
                TransactionId: TryReadString(item, "transaction_id"),
                PurchasedAtUtc: TryParseAppleMilliseconds(item, "purchase_date_ms"),
                ExpiresAtUtc: TryParseAppleMilliseconds(item, "expires_date_ms"),
                CancelledAtUtc: TryParseAppleMilliseconds(item, "cancellation_date_ms")));
        }
    }

    private static string? TryReadString(JsonElement root, params string[] path)
    {
        var node = root;
        foreach (var segment in path)
        {
            if (!node.TryGetProperty(segment, out node))
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

    private static int? TryReadInt32(JsonElement root, params string[] path) =>
        int.TryParse(TryReadString(root, path), out var value) ? value : null;

    private static DateTimeOffset? TryParseAppleMilliseconds(JsonElement root, string propertyName)
    {
        var raw = TryReadString(root, propertyName);
        return long.TryParse(raw, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;
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
        string? productId = null) =>
        new(
            IsActive: false,
            Message: message,
            Provider: provider,
            ProductId: productId,
            AccessEndsAtUtc: null);

    private sealed record VerifiedStorePurchase(
        string Provider,
        string ProductId,
        string ProviderPaymentId,
        string? ProviderTransactionId,
        string? ProviderToken,
        DateTimeOffset SubscribedAtUtc,
        DateTimeOffset? AccessEndsAtUtc);

    private sealed record AppleReceiptRecord(
        string? ProductId,
        string? OriginalTransactionId,
        string? TransactionId,
        DateTimeOffset? PurchasedAtUtc,
        DateTimeOffset? ExpiresAtUtc,
        DateTimeOffset? CancelledAtUtc);

    private sealed record AppleReceiptVerification(int Status, JsonDocument? Document);
}
