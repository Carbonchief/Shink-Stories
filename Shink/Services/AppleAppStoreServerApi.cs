using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Shink.Services;

internal sealed record AppleVerifiedSubscription(
    string ProductId,
    string OriginalTransactionId,
    string TransactionId,
    DateTimeOffset OriginalPurchaseDateUtc,
    DateTimeOffset ExpiresAtUtc,
    string Environment);

internal sealed record AppleTransactionPayload(
    string? BundleId,
    string? ProductId,
    string? OriginalTransactionId,
    string? TransactionId,
    long? PurchaseDate,
    long? OriginalPurchaseDate,
    long? ExpiresDate,
    long? RevocationDate,
    bool IsUpgraded,
    long? SignedDate,
    string? Environment);

internal sealed record AppleRenewalPayload(
    string? OriginalTransactionId,
    string? AutoRenewProductId,
    long? GracePeriodExpiresDate,
    long? RenewalDate,
    long? SignedDate,
    string? Environment);

internal sealed class AppleAppStoreServerApi
{
    internal const string ProductionBaseUrl = "https://api.storekit.apple.com";
    internal const string SandboxBaseUrl = "https://api.storekit-sandbox.apple.com";
    private const int TransactionIdNotFoundError = 4040010;

    private readonly HttpClient _httpClient;
    private readonly MobileStoreOptions _options;
    private readonly ILogger _logger;
    private readonly AppleSignedTransactionVerifier _signedTransactionVerifier;

    internal AppleAppStoreServerApi(
        HttpClient httpClient,
        MobileStoreOptions options,
        ILogger logger,
        AppleSignedTransactionVerifier? signedTransactionVerifier = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _signedTransactionVerifier = signedTransactionVerifier ?? new AppleSignedTransactionVerifier();
    }

    internal async Task<AppleVerifiedSubscription?> VerifySubscriptionAsync(
        string productId,
        string? transactionId,
        CancellationToken cancellationToken = default)
    {
        var normalizedTransactionId = transactionId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTransactionId) ||
            normalizedTransactionId.Length > 128 ||
            string.IsNullOrWhiteSpace(_options.AppleIssuerId) ||
            string.IsNullOrWhiteSpace(_options.AppleKeyId) ||
            string.IsNullOrWhiteSpace(_options.ApplePrivateKey) ||
            string.IsNullOrWhiteSpace(_options.AppleBundleId))
        {
            _logger.LogWarning("Apple App Store Server API verification is not configured or did not include a transaction identifier.");
            return null;
        }

        string bearerToken;
        try
        {
            bearerToken = CreateBearerToken(_options, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or JsonException)
        {
            _logger.LogWarning(exception, "Apple App Store Server API authorization token generation failed.");
            return null;
        }

        var production = await GetSubscriptionStatusAsync(
            ProductionBaseUrl,
            "Production",
            normalizedTransactionId,
            productId,
            bearerToken,
            cancellationToken);
        if (production.Subscription is not null)
        {
            return production.Subscription;
        }

        if (production.StatusCode != HttpStatusCode.NotFound ||
            production.ErrorCode != TransactionIdNotFoundError)
        {
            return null;
        }

        var sandbox = await GetSubscriptionStatusAsync(
            SandboxBaseUrl,
            "Sandbox",
            normalizedTransactionId,
            productId,
            bearerToken,
            cancellationToken);
        return sandbox.Subscription;
    }

    internal static string CreateBearerToken(MobileStoreOptions options, DateTimeOffset nowUtc)
    {
        var issuedAt = nowUtc.ToUnixTimeSeconds();
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "ES256",
            kid = options.AppleKeyId.Trim(),
            typ = "JWT"
        }));
        var claims = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = options.AppleIssuerId.Trim(),
            iat = issuedAt,
            exp = issuedAt + 300,
            aud = "appstoreconnect-v1",
            bid = options.AppleBundleId.Trim()
        }));
        var unsignedToken = $"{header}.{claims}";

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(NormalizePrivateKey(options.ApplePrivateKey));
        if (ecdsa.KeySize != 256)
        {
            throw new CryptographicException("The Apple In-App Purchase key must use the P-256 curve.");
        }

        var signature = ecdsa.SignData(
            Encoding.ASCII.GetBytes(unsignedToken),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }

    private async Task<AppleSubscriptionStatusResult> GetSubscriptionStatusAsync(
        string baseUrl,
        string expectedEnvironment,
        string transactionId,
        string productId,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{baseUrl}/inApps/v1/subscriptions/{Uri.EscapeDataString(transactionId)}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorCode = TryReadErrorCode(body);
                _logger.LogWarning(
                    "Apple subscription status request failed. environment={Environment} status={Status} error_code={ErrorCode}",
                    expectedEnvironment,
                    (int)response.StatusCode,
                    errorCode);
                return new AppleSubscriptionStatusResult(null, response.StatusCode, errorCode);
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var responseBundleId = TryReadString(root, "bundleId");
            var responseEnvironment = TryReadString(root, "environment");
            if (!string.Equals(responseBundleId, _options.AppleBundleId, StringComparison.Ordinal) ||
                !string.Equals(responseEnvironment, expectedEnvironment, StringComparison.Ordinal))
            {
                _logger.LogWarning("Apple subscription status response did not match the configured app or environment.");
                return new AppleSubscriptionStatusResult(null, response.StatusCode, null);
            }

            var subscription = FindActiveSubscription(root, productId, expectedEnvironment);
            return new AppleSubscriptionStatusResult(subscription, response.StatusCode, null);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or CryptographicException)
        {
            _logger.LogWarning(
                exception,
                "Apple subscription status verification failed. environment={Environment}",
                expectedEnvironment);
            return new AppleSubscriptionStatusResult(null, null, null);
        }
    }

    private AppleVerifiedSubscription? FindActiveSubscription(
        JsonElement root,
        string productId,
        string expectedEnvironment)
    {
        if (!root.TryGetProperty("data", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var candidates = new List<AppleVerifiedSubscription>();
        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("lastTransactions", out var transactions) ||
                transactions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var transaction in transactions.EnumerateArray())
            {
                var status = TryReadInt32(transaction, "status");
                if (status is not (1 or 4))
                {
                    continue;
                }

                var signedTransaction = TryReadString(transaction, "signedTransactionInfo");
                if (string.IsNullOrWhiteSpace(signedTransaction))
                {
                    continue;
                }

                var payload = _signedTransactionVerifier.VerifyAndDecode(
                    signedTransaction,
                    _options.AppleBundleId,
                    expectedEnvironment);
                if (payload is null ||
                    !string.Equals(payload.ProductId, productId, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(payload.OriginalTransactionId) ||
                    string.IsNullOrWhiteSpace(payload.TransactionId) ||
                    payload.ExpiresDate is not { } expiresMilliseconds ||
                    payload.RevocationDate is not null ||
                    payload.IsUpgraded)
                {
                    continue;
                }

                var effectiveExpiresMilliseconds = expiresMilliseconds;
                if (status == 4)
                {
                    var signedRenewal = TryReadString(transaction, "signedRenewalInfo");
                    var renewal = string.IsNullOrWhiteSpace(signedRenewal)
                        ? null
                        : _signedTransactionVerifier.VerifyAndDecodeRenewal(
                            signedRenewal,
                            expectedEnvironment);
                    if (renewal is null ||
                        !string.Equals(
                            renewal.OriginalTransactionId,
                            payload.OriginalTransactionId,
                            StringComparison.Ordinal) ||
                        !string.Equals(renewal.AutoRenewProductId, productId, StringComparison.Ordinal) ||
                        renewal.GracePeriodExpiresDate is not { } gracePeriodExpiresMilliseconds)
                    {
                        continue;
                    }

                    effectiveExpiresMilliseconds = Math.Max(
                        effectiveExpiresMilliseconds,
                        gracePeriodExpiresMilliseconds);
                }

                var expiresAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(effectiveExpiresMilliseconds);
                if (expiresAtUtc <= nowUtc)
                {
                    continue;
                }

                var originalPurchaseMilliseconds = payload.OriginalPurchaseDate ?? payload.PurchaseDate;
                var originalPurchaseDateUtc = originalPurchaseMilliseconds is { } purchaseMilliseconds
                    ? DateTimeOffset.FromUnixTimeMilliseconds(purchaseMilliseconds)
                    : nowUtc;
                candidates.Add(new AppleVerifiedSubscription(
                    productId,
                    payload.OriginalTransactionId,
                    payload.TransactionId,
                    originalPurchaseDateUtc,
                    expiresAtUtc,
                    expectedEnvironment));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.ExpiresAtUtc)
            .FirstOrDefault();
    }

    private static int? TryReadErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return TryReadInt32(document.RootElement, "errorCode");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizePrivateKey(string privateKey) =>
        privateKey.Trim().Replace("\\n", "\n", StringComparison.Ordinal);

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? TryReadInt32(JsonElement root, string propertyName) =>
        int.TryParse(TryReadString(root, propertyName), out var value) ? value : null;

    internal static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record AppleSubscriptionStatusResult(
        AppleVerifiedSubscription? Subscription,
        HttpStatusCode? StatusCode,
        int? ErrorCode);
}

internal sealed class AppleSignedTransactionVerifier
{
    private const string AppleReceiptSigningLeafOid = "1.2.840.113635.100.6.11.1";
    private const string AppleApplicationIntegrationIntermediateOid = "1.2.840.113635.100.6.2.1";
    private static readonly string[] AppleRootSha256Fingerprints =
    [
        "B0B1730ECBC7FF4505142C49F1295E6EDA6BCAED7E2C68C5BE91B5A11001F024",
        "C2B9B042DD57830E7D117DAC55AC8AE19407D38E41D88F3215BC3A890444A050",
        "63343ABFB89A6A03EBB57E9B3F5FA7BE7C4F5C756F3017B3A8C488C3653E9179"
    ];

    private readonly HashSet<string> _trustedRootFingerprints;

    internal AppleSignedTransactionVerifier(IEnumerable<string>? trustedRootFingerprints = null)
    {
        _trustedRootFingerprints = new HashSet<string>(
            trustedRootFingerprints ?? AppleRootSha256Fingerprints,
            StringComparer.OrdinalIgnoreCase);
    }

    internal AppleTransactionPayload? VerifyAndDecode(
        string signedTransaction,
        string expectedBundleId,
        string expectedEnvironment)
    {
        try
        {
            var verifiedPayload = VerifyAndExtractPayload(signedTransaction);
            if (verifiedPayload is null)
            {
                return null;
            }

            using var payloadDocument = JsonDocument.Parse(verifiedPayload);
            var payload = payloadDocument.RootElement;
            var decoded = new AppleTransactionPayload(
                BundleId: TryReadString(payload, "bundleId"),
                ProductId: TryReadString(payload, "productId"),
                OriginalTransactionId: TryReadString(payload, "originalTransactionId"),
                TransactionId: TryReadString(payload, "transactionId"),
                PurchaseDate: TryReadInt64(payload, "purchaseDate"),
                OriginalPurchaseDate: TryReadInt64(payload, "originalPurchaseDate"),
                ExpiresDate: TryReadInt64(payload, "expiresDate"),
                RevocationDate: TryReadInt64(payload, "revocationDate"),
                IsUpgraded: TryReadBoolean(payload, "isUpgraded"),
                SignedDate: TryReadInt64(payload, "signedDate"),
                Environment: TryReadString(payload, "environment"));
            return string.Equals(decoded.BundleId, expectedBundleId, StringComparison.Ordinal) &&
                   string.Equals(decoded.Environment, expectedEnvironment, StringComparison.Ordinal)
                ? decoded
                : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    internal AppleRenewalPayload? VerifyAndDecodeRenewal(
        string signedRenewalInfo,
        string expectedEnvironment)
    {
        try
        {
            var verifiedPayload = VerifyAndExtractPayload(signedRenewalInfo);
            if (verifiedPayload is null)
            {
                return null;
            }

            using var payloadDocument = JsonDocument.Parse(verifiedPayload);
            var payload = payloadDocument.RootElement;
            var decoded = new AppleRenewalPayload(
                OriginalTransactionId: TryReadString(payload, "originalTransactionId"),
                AutoRenewProductId: TryReadString(payload, "autoRenewProductId"),
                GracePeriodExpiresDate: TryReadInt64(payload, "gracePeriodExpiresDate"),
                RenewalDate: TryReadInt64(payload, "renewalDate"),
                SignedDate: TryReadInt64(payload, "signedDate"),
                Environment: TryReadString(payload, "environment"));
            return string.Equals(decoded.Environment, expectedEnvironment, StringComparison.Ordinal)
                ? decoded
                : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private byte[]? VerifyAndExtractPayload(string signedData)
    {
        var segments = signedData.Split('.');
        if (segments.Length != 3)
        {
            return null;
        }

        using var headerDocument = JsonDocument.Parse(Base64UrlDecode(segments[0]));
        var header = headerDocument.RootElement;
        if (!string.Equals(TryReadString(header, "alg"), "ES256", StringComparison.Ordinal) ||
            !header.TryGetProperty("x5c", out var certificateElements) ||
            certificateElements.ValueKind != JsonValueKind.Array ||
            certificateElements.GetArrayLength() != 3)
        {
            return null;
        }

        var certificateBytes = certificateElements
            .EnumerateArray()
            .Select(element => Convert.FromBase64String(element.GetString() ?? string.Empty))
            .ToArray();
        using var leaf = X509CertificateLoader.LoadCertificate(certificateBytes[0]);
        using var intermediate = X509CertificateLoader.LoadCertificate(certificateBytes[1]);
        using var root = X509CertificateLoader.LoadCertificate(certificateBytes[2]);
        if (!HasExtension(leaf, AppleReceiptSigningLeafOid) ||
            !HasExtension(intermediate, AppleApplicationIntegrationIntermediateOid) ||
            !_trustedRootFingerprints.Contains(Convert.ToHexString(SHA256.HashData(root.RawData))) ||
            !BuildCertificateChain(leaf, intermediate, root))
        {
            return null;
        }

        using var publicKey = leaf.GetECDsaPublicKey();
        if (publicKey is null || publicKey.KeySize != 256)
        {
            return null;
        }

        var signature = Base64UrlDecode(segments[2]);
        var signingInput = Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}");
        return publicKey.VerifyData(
            signingInput,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            ? Base64UrlDecode(segments[1])
            : null;
    }

    private static bool BuildCertificateChain(
        X509Certificate2 leaf,
        X509Certificate2 intermediate,
        X509Certificate2 root)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.ExtraStore.Add(intermediate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        if (!chain.Build(leaf) || chain.ChainElements.Count != 3)
        {
            return false;
        }

        var chainRoot = chain.ChainElements[^1].Certificate;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(chainRoot.RawData),
            SHA256.HashData(root.RawData));
    }

    private static bool HasExtension(X509Certificate2 certificate, string oid) =>
        certificate.Extensions.Cast<X509Extension>()
            .Any(extension => string.Equals(extension.Oid?.Value, oid, StringComparison.Ordinal));

    private static string? TryReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? TryReadInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }

    private static bool TryReadBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True;

    internal static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            _ => normalized
        };
        return Convert.FromBase64String(normalized);
    }
}
