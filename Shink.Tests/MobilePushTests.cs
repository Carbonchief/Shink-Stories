using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MobilePushTests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public async Task SendAsync_AppleSignsHttp2AlertWithStableNotificationAndAccountBinding()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var settings = AppleSettings(key);
        var delivery = Delivery("apns");
        var handler = new Handler(async request =>
        {
            Assert.AreEqual(new Uri("https://api.push.apple.com/3/device/" + delivery.Token), request.RequestUri);
            Assert.AreEqual(HttpVersion.Version20, request.Version);
            Assert.AreEqual(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
            Assert.AreEqual(settings.AppleBundleId, request.Headers.GetValues("apns-topic").Single());
            Assert.AreEqual(delivery.NotificationId.ToString("D"), request.Headers.GetValues("apns-collapse-id").Single());
            var jwt = request.Headers.Authorization!.Parameter!.Split('.');
            Assert.IsTrue(key.VerifyData(Encoding.UTF8.GetBytes(jwt[0] + "." + jwt[1]), Decode(jwt[2]),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.AreEqual(delivery.Title, payload.RootElement.GetProperty("aps").GetProperty("alert").GetProperty("title").GetString());
            Assert.AreEqual(delivery.Body, payload.RootElement.GetProperty("aps").GetProperty("alert").GetProperty("body").GetString());
            Assert.AreEqual(delivery.RegistrationId.ToString("D"), payload.RootElement.GetProperty("registration_id").GetString());
            Assert.AreEqual(delivery.Href, payload.RootElement.GetProperty("href").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sender = new MobilePushSender(new ClientFactory(handler), Options.Create(settings), cache);

        var result = await sender.SendAsync(delivery, TestContext.CancellationToken);

        Assert.AreEqual("sent", result.Result);
    }

    [TestMethod]
    [DataRow("Unregistered", 410, "invalid")]
    [DataRow("BadDeviceToken", 400, "invalid")]
    [DataRow("InvalidProviderToken", 403, "retry")]
    [DataRow("TooManyRequests", 429, "retry")]
    public async Task SendAsync_AppleDistinguishesInvalidDeviceFromProviderFailures(string reason, int status, string expected)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = new Handler(_ => Task.FromResult(JsonResponse((HttpStatusCode)status, JsonSerializer.Serialize(new { reason }))));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sender = new MobilePushSender(new ClientFactory(handler), Options.Create(AppleSettings(key)), cache);

        var result = await sender.SendAsync(Delivery("apns") with { Environment = "sandbox" }, TestContext.CancellationToken);

        Assert.AreEqual(expected, result.Result);
    }

    [TestMethod]
    public async Task SendAsync_FirebaseUsesScopedOAuthAndAccountBoundDataMessage()
    {
        using var key = RSA.Create(2048);
        var settings = FirebaseSettings(key);
        var delivery = Delivery("fcm");
        var requests = 0;
        var handler = new Handler(async request =>
        {
            requests++;
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                var form = await request.Content!.ReadAsStringAsync();
                var assertion = Uri.UnescapeDataString(form.Split('&').Single(pair => pair.StartsWith("assertion=", StringComparison.Ordinal))[10..]);
                var jwt = assertion.Split('.');
                using var claims = JsonDocument.Parse(Decode(jwt[1]));
                Assert.AreEqual("https://www.googleapis.com/auth/firebase.messaging", claims.RootElement.GetProperty("scope").GetString());
                Assert.IsTrue(key.VerifyData(Encoding.UTF8.GetBytes(jwt[0] + "." + jwt[1]), Decode(jwt[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                return JsonResponse(HttpStatusCode.OK, """{"access_token":"fake-access-token","expires_in":3600}""");
            }
            Assert.AreEqual("https://fcm.googleapis.com/v1/projects/schink-test/messages:send", request.RequestUri.ToString());
            Assert.AreEqual("fake-access-token", request.Headers.Authorization?.Parameter);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var message = payload.RootElement.GetProperty("message");
            Assert.IsFalse(message.TryGetProperty("notification", out _), "App checks account binding before displaying Android pushes.");
            Assert.AreEqual(delivery.Token, message.GetProperty("token").GetString());
            Assert.AreEqual(delivery.RegistrationId.ToString("D"), message.GetProperty("data").GetProperty("registration_id").GetString());
            Assert.AreEqual(delivery.NotificationId.ToString("D"), message.GetProperty("data").GetProperty("notification_id").GetString());
            Assert.AreEqual("HIGH", message.GetProperty("android").GetProperty("priority").GetString());
            return JsonResponse(HttpStatusCode.OK, """{"name":"projects/schink-test/messages/123"}""");
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sender = new MobilePushSender(new ClientFactory(handler), Options.Create(settings), cache);

        Assert.AreEqual("sent", (await sender.SendAsync(delivery, TestContext.CancellationToken)).Result);
        Assert.AreEqual("sent", (await sender.SendAsync(delivery, TestContext.CancellationToken)).Result);
        Assert.AreEqual(3, requests, "OAuth access tokens are cached across deliveries.");
    }

    [TestMethod]
    [DataRow("""{"error":{"details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"UNREGISTERED"}]}}""", "invalid")]
    [DataRow("""{"error":{"status":"NOT_FOUND"}}""", "retry")]
    public async Task SendAsync_FirebaseOnlyUnregistersForExplicitInvalidToken(string body, string expected)
    {
        using var key = RSA.Create(2048);
        var handler = new Handler(request => Task.FromResult(request.RequestUri!.Host == "oauth2.googleapis.com"
            ? JsonResponse(HttpStatusCode.OK, """{"access_token":"fake","expires_in":3600}""")
            : JsonResponse(HttpStatusCode.NotFound, body)));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sender = new MobilePushSender(new ClientFactory(handler), Options.Create(FirebaseSettings(key)), cache);

        Assert.AreEqual(expected, (await sender.SendAsync(Delivery("fcm"), TestContext.CancellationToken)).Result);
    }

    [TestMethod]
    public async Task SendAsync_DisabledDoesNotContactProvider()
    {
        var handler = new Handler(_ => throw new AssertFailedException("Push is disabled."));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sender = new MobilePushSender(new ClientFactory(handler), Options.Create(new MobilePushOptions()), cache);
        Assert.AreEqual("retry", (await sender.SendAsync(Delivery("apns"), TestContext.CancellationToken)).Result);
    }

    [TestMethod]
    public async Task RegisterAsync_HashesInstallationProofAndUsesServerKey()
    {
        var secret = new string('f', 64);
        var registration = Guid.NewGuid();
        var handler = new Handler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var payload = JsonDocument.Parse(body);
            Assert.IsFalse(body.Contains(secret, StringComparison.Ordinal));
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))), payload.RootElement.GetProperty("p_secret_hash").GetString());
            Assert.AreEqual("a@example.test", payload.RootElement.GetProperty("p_email").GetString());
            Assert.AreEqual("server-secret", request.Headers.GetValues("apikey").Single());
            return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(registration));
        });
        using var http = new HttpClient(handler);
        var store = new SupabaseMobilePushStore(http, Options.Create(new SupabaseOptions { Url = "https://example.supabase.co", SecretKey = "server-secret" }));
        var result = await store.RegisterAsync("a@example.test", new(Guid.NewGuid(), secret, "apns", "production", new string('1', 64)), TestContext.CancellationToken);
        Assert.AreEqual(registration, result);
    }

    [TestMethod]
    [DataRow("apns", "production", "not-an-apple-token")]
    [DataRow("fcm", "sandbox", "fake-fcm-token-for-test")]
    [DataRow("other", "production", "fake-fcm-token-for-test")]
    [DataRow("fcm", "production", "short")]
    public void Registration_InvalidPlatformsEnvironmentsAndTokensAreRejected(string platform, string environment, string token)
    {
        Assert.IsFalse(new MobilePushRegistrationRequest(Guid.NewGuid(), new string('a', 64), platform, environment, token).IsValid);
    }

    [TestMethod]
    public void Registration_RequiresInstallationProofAndAcceptsValidProviderTokens()
    {
        var request = new MobilePushRegistrationRequest(Guid.NewGuid(), new string('a', 64), "apns", "sandbox", new string('1', 64));
        Assert.IsTrue(request.IsValid);
        Assert.IsTrue((request with { Platform = "fcm", Environment = "production", Token = "valid-fcm-registration-token" }).IsValid);
        Assert.IsFalse((request with { InstallationSecret = "" }).IsValid);
        Assert.IsFalse((request with { InstallationId = Guid.Empty }).IsValid);
    }

    [TestMethod]
    public void PushTap_RequiresBothNotificationAndRegistrationIdentifiers()
    {
        var id = Guid.NewGuid();
        var registration = Guid.NewGuid();
        Assert.IsNull(Shink.Mobile.Services.MobilePushOpen.FromValues(id.ToString(), null, "story_published", "/luister/een"));
        Assert.IsNull(Shink.Mobile.Services.MobilePushOpen.FromValues("invalid", registration.ToString(), "story_published", "/luister/een"));
        var push = Shink.Mobile.Services.MobilePushOpen.FromValues(id.ToString(), registration.ToString(), "story_published", "/luister/een");
        Assert.IsNotNull(push);
        Assert.AreEqual(id, push.NotificationId);
        Assert.AreEqual(registration, push.RegistrationId);
    }

    private static MobilePushOptions AppleSettings(ECDsa key) => new()
    {
        Enabled = true, AppleTeamId = "TEAM", AppleKeyId = "KEY", ApplePrivateKey = key.ExportPkcs8PrivateKeyPem()
    };
    private static MobilePushOptions FirebaseSettings(RSA key) => new()
    {
        Enabled = true, FirebaseProjectId = "schink-test", FirebaseServiceAccountJson = JsonSerializer.Serialize(new
        {
            client_email = "sender@example.test", private_key = key.ExportPkcs8PrivateKeyPem()
        })
    };
    private static MobilePushDelivery Delivery(string platform) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        platform, "production", platform == "apns" ? new string('1', 64) : "fake-fcm-token", "Nuwe storie beskikbaar", "Die nuwe storie", "story_published", "/luister/nuwe-storie");
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
