using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class AppleAppStoreServerApiTests
{
    private const string BundleId = "com.schink.stories.mobile";
    private const string ProductId = "schink_stories_maandeliks";

    [TestMethod]
    public void CreateBearerTokenUsesAppleEs256ClaimsAndP1363Signature()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = CreateOptions(signingKey);
        var nowUtc = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        var token = AppleAppStoreServerApi.CreateBearerToken(options, nowUtc);
        var segments = token.Split('.');

        Assert.HasCount(3, segments);
        using var header = JsonDocument.Parse(AppleSignedTransactionVerifier.Base64UrlDecode(segments[0]));
        Assert.AreEqual("ES256", header.RootElement.GetProperty("alg").GetString());
        Assert.AreEqual("TESTKEY123", header.RootElement.GetProperty("kid").GetString());
        using var claims = JsonDocument.Parse(AppleSignedTransactionVerifier.Base64UrlDecode(segments[1]));
        Assert.AreEqual("test-issuer", claims.RootElement.GetProperty("iss").GetString());
        Assert.AreEqual("appstoreconnect-v1", claims.RootElement.GetProperty("aud").GetString());
        Assert.AreEqual(BundleId, claims.RootElement.GetProperty("bid").GetString());
        Assert.AreEqual(nowUtc.ToUnixTimeSeconds(), claims.RootElement.GetProperty("iat").GetInt64());
        Assert.AreEqual(nowUtc.AddMinutes(5).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());

        var signature = AppleSignedTransactionVerifier.Base64UrlDecode(segments[2]);
        Assert.HasCount(64, signature);
        Assert.IsTrue(signingKey.VerifyData(
            Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"),
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [TestMethod]
    public async Task VerifySubscriptionFallsBackToSandboxAndAcceptsOnlyAppleSignedActiveTransaction()
    {
        using var apiSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var chain = TestCertificateChain.Create();
        var nowUtc = DateTimeOffset.UtcNow;
        var signedTransaction = chain.CreateSignedTransaction(new
        {
            bundleId = BundleId,
            productId = ProductId,
            originalTransactionId = "200000000000001",
            transactionId = "200000000000099",
            purchaseDate = nowUtc.AddMinutes(-10).ToUnixTimeMilliseconds(),
            originalPurchaseDate = nowUtc.AddMonths(-2).ToUnixTimeMilliseconds(),
            expiresDate = nowUtc.AddMonths(1).ToUnixTimeMilliseconds(),
            signedDate = nowUtc.ToUnixTimeMilliseconds(),
            environment = "Sandbox",
            type = "Auto-Renewable Subscription"
        });
        var sandboxBody = JsonSerializer.Serialize(new
        {
            environment = "Sandbox",
            bundleId = BundleId,
            data = new[]
            {
                new
                {
                    subscriptionGroupIdentifier = "test-group",
                    lastTransactions = new[]
                    {
                        new
                        {
                            status = 1,
                            originalTransactionId = "200000000000001",
                            signedTransactionInfo = signedTransaction,
                            signedRenewalInfo = "unused"
                        }
                    }
                }
            }
        });
        var requestedUrls = new List<string>();
        var bearerTokens = new List<string>();
        var handler = new RecordingHandler(request =>
        {
            requestedUrls.Add(request.RequestUri!.ToString());
            bearerTokens.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
            return request.RequestUri.Host == "api.storekit.apple.com"
                ? JsonResponse(HttpStatusCode.NotFound, "{\"errorCode\":4040010}")
                : JsonResponse(HttpStatusCode.OK, sandboxBody);
        });
        using var httpClient = new HttpClient(handler);
        var options = CreateOptions(apiSigningKey);
        var trustedRoot = Convert.ToHexString(SHA256.HashData(chain.Root.RawData));
        var verifier = new AppleSignedTransactionVerifier([trustedRoot]);
        var api = new AppleAppStoreServerApi(
            httpClient,
            options,
            NullLogger<MobileStoreEntitlementService>.Instance,
            verifier);

        var result = await api.VerifySubscriptionAsync(ProductId, "200000000000099");

        Assert.IsNotNull(result);
        Assert.AreEqual(ProductId, result.ProductId);
        Assert.AreEqual("200000000000001", result.OriginalTransactionId);
        Assert.AreEqual("200000000000099", result.TransactionId);
        Assert.AreEqual("Sandbox", result.Environment);
        Assert.HasCount(2, requestedUrls);
        StringAssert.StartsWith(requestedUrls[0], AppleAppStoreServerApi.ProductionBaseUrl);
        StringAssert.StartsWith(requestedUrls[1], AppleAppStoreServerApi.SandboxBaseUrl);
        Assert.IsTrue(bearerTokens.All(token => token.Split('.').Length == 3));
    }

    [TestMethod]
    public async Task VerifySubscriptionUsesSignedGracePeriodExpiry()
    {
        using var apiSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var chain = TestCertificateChain.Create();
        var nowUtc = DateTimeOffset.UtcNow;
        const string originalTransactionId = "200000000000002";
        var signedTransaction = chain.CreateSignedTransaction(new
        {
            bundleId = BundleId,
            productId = ProductId,
            originalTransactionId,
            transactionId = "200000000000100",
            purchaseDate = nowUtc.AddMonths(-1).ToUnixTimeMilliseconds(),
            originalPurchaseDate = nowUtc.AddMonths(-3).ToUnixTimeMilliseconds(),
            expiresDate = nowUtc.AddHours(-1).ToUnixTimeMilliseconds(),
            signedDate = nowUtc.ToUnixTimeMilliseconds(),
            environment = "Production"
        });
        var gracePeriodExpiresAtUtc = nowUtc.AddDays(2);
        var signedRenewal = chain.CreateSignedTransaction(new
        {
            originalTransactionId,
            autoRenewProductId = ProductId,
            gracePeriodExpiresDate = gracePeriodExpiresAtUtc.ToUnixTimeMilliseconds(),
            renewalDate = nowUtc.AddMonths(1).ToUnixTimeMilliseconds(),
            signedDate = nowUtc.ToUnixTimeMilliseconds(),
            environment = "Production"
        });
        var body = JsonSerializer.Serialize(new
        {
            environment = "Production",
            bundleId = BundleId,
            data = new[]
            {
                new
                {
                    lastTransactions = new[]
                    {
                        new
                        {
                            status = 4,
                            originalTransactionId,
                            signedTransactionInfo = signedTransaction,
                            signedRenewalInfo = signedRenewal
                        }
                    }
                }
            }
        });
        using var httpClient = new HttpClient(new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, body)));
        var trustedRoot = Convert.ToHexString(SHA256.HashData(chain.Root.RawData));
        var api = new AppleAppStoreServerApi(
            httpClient,
            CreateOptions(apiSigningKey),
            NullLogger<MobileStoreEntitlementService>.Instance,
            new AppleSignedTransactionVerifier([trustedRoot]));

        var result = await api.VerifySubscriptionAsync(ProductId, "200000000000100");

        Assert.IsNotNull(result);
        Assert.AreEqual(
            gracePeriodExpiresAtUtc.ToUnixTimeMilliseconds(),
            result.ExpiresAtUtc.ToUnixTimeMilliseconds());
    }

    [TestMethod]
    public void SignedTransactionVerifierRejectsPayloadTampering()
    {
        using var chain = TestCertificateChain.Create();
        var trustedRoot = Convert.ToHexString(SHA256.HashData(chain.Root.RawData));
        var verifier = new AppleSignedTransactionVerifier([trustedRoot]);
        var signedTransaction = chain.CreateSignedTransaction(new
        {
            bundleId = BundleId,
            productId = ProductId,
            originalTransactionId = "200000000000001",
            transactionId = "200000000000099",
            expiresDate = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds(),
            environment = "Production"
        });
        var segments = signedTransaction.Split('.');
        var tamperedPayload = AppleAppStoreServerApi.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            bundleId = BundleId,
            productId = "attacker_product",
            originalTransactionId = "200000000000001",
            transactionId = "200000000000099",
            expiresDate = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds(),
            environment = "Production"
        }));

        var result = verifier.VerifyAndDecode(
            $"{segments[0]}.{tamperedPayload}.{segments[2]}",
            BundleId,
            "Production");

        Assert.IsNull(result);
    }

    private static MobileStoreOptions CreateOptions(ECDsa signingKey) =>
        new()
        {
            AppleIssuerId = "test-issuer",
            AppleKeyId = "TESTKEY123",
            ApplePrivateKey = signingKey.ExportPkcs8PrivateKeyPem(),
            AppleBundleId = BundleId
        };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class TestCertificateChain : IDisposable
    {
        private const string LeafOid = "1.2.840.113635.100.6.11.1";
        private const string IntermediateOid = "1.2.840.113635.100.6.2.1";
        private readonly ECDsa _leafKey;
        private readonly X509Certificate2 _intermediate;
        private readonly X509Certificate2 _leaf;

        private TestCertificateChain(
            X509Certificate2 root,
            X509Certificate2 intermediate,
            X509Certificate2 leaf,
            ECDsa leafKey)
        {
            Root = root;
            _intermediate = intermediate;
            _leaf = leaf;
            _leafKey = leafKey;
        }

        internal X509Certificate2 Root { get; }

        internal static TestCertificateChain Create()
        {
            var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            var notAfter = DateTimeOffset.UtcNow.AddDays(30);
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = new CertificateRequest("CN=Schink Test Root", rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));
            rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
            var root = rootRequest.CreateSelfSigned(notBefore, notAfter);

            using var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var intermediateRequest = new CertificateRequest(
                "CN=Schink Test Intermediate",
                intermediateKey,
                HashAlgorithmName.SHA256);
            intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            intermediateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));
            intermediateRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(intermediateRequest.PublicKey, false));
            intermediateRequest.CertificateExtensions.Add(new X509Extension(IntermediateOid, [0x05, 0x00], false));
            using var intermediateWithoutKey = intermediateRequest.Create(
                root,
                notBefore,
                notAfter,
                RandomNumberGenerator.GetBytes(16));
            var intermediate = intermediateWithoutKey.CopyWithPrivateKey(intermediateKey);

            var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var leafRequest = new CertificateRequest("CN=Schink Test Leaf", leafKey, HashAlgorithmName.SHA256);
            leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            leafRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(leafRequest.PublicKey, false));
            leafRequest.CertificateExtensions.Add(new X509Extension(LeafOid, [0x05, 0x00], false));
            var leaf = leafRequest.Create(
                intermediate,
                notBefore,
                notAfter,
                RandomNumberGenerator.GetBytes(16));

            return new TestCertificateChain(root, intermediate, leaf, leafKey);
        }

        internal string CreateSignedTransaction(object payload)
        {
            var header = AppleAppStoreServerApi.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = "ES256",
                x5c = new[]
                {
                    Convert.ToBase64String(_leaf.RawData),
                    Convert.ToBase64String(_intermediate.RawData),
                    Convert.ToBase64String(Root.RawData)
                }
            }));
            var encodedPayload = AppleAppStoreServerApi.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
            var signingInput = $"{header}.{encodedPayload}";
            var signature = _leafKey.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return $"{signingInput}.{AppleAppStoreServerApi.Base64UrlEncode(signature)}";
        }

        public void Dispose()
        {
            _leaf.Dispose();
            _intermediate.Dispose();
            Root.Dispose();
            _leafKey.Dispose();
        }
    }
}
