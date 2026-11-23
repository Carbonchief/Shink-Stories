using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
    [DataRow("story_published", "/media/image?src=https%3A%2F%2Fmedia.prioritybit.co.za%2Fcover.jpg", "https://media.prioritybit.co.za/cover.jpg")]
    [DataRow("character_unlock", "/branding/characters/roomie.png", "https://www.schink.co.za/branding/characters/roomie.png")]
    [DataRow("blog_published", "/branding/blog.jpg", null)]
    [DataRow("resource_document_published", "/branding/resource.jpg", null)]
    [DataRow("story_published", "/branding/schink-placeholder.png", null)]
    [DataRow("story_published", null, null)]
    [DataRow("character_unlock", "https://example.net/portrait.jpg", null)]
    [DataRow("story_published", "/media/image?src=https%3A%2F%2Fexample.net%2Fcover.jpg", null)]
    public async Task SendAsync_SelectsArtworkForBothProviders(string kind, string? imagePath, string? expectedUrl)
    {
        using var appleKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var firebaseKey = RSA.Create(2048);
        foreach (var platform in new[] { "apns", "fcm" })
        {
            var handler = new Handler(async request =>
            {
                if (request.RequestUri!.Host == "oauth2.googleapis.com")
                    return JsonResponse(HttpStatusCode.OK, """{"access_token":"test","expires_in":3600}""");
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var data = platform == "apns" ? payload.RootElement : payload.RootElement.GetProperty("message").GetProperty("data");
                Assert.AreEqual(expectedUrl is not null, data.TryGetProperty("image_url", out var url));
                if (expectedUrl is not null) Assert.AreEqual(expectedUrl, url.GetString());
                if (platform == "apns")
                {
                    Assert.AreEqual(expectedUrl is not null, data.GetProperty("aps").TryGetProperty("mutable-content", out var flag));
                    if (expectedUrl is not null) Assert.AreEqual(1, flag.GetInt32());
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var settings = platform == "apns" ? AppleSettings(appleKey) : FirebaseSettings(firebaseKey);
            var sender = new MobilePushSender(new ClientFactory(handler), Options.Create(settings), cache);
            var result = await sender.SendAsync(Delivery(platform) with { NotificationType = kind, ImagePath = imagePath }, TestContext.CancellationToken);
            Assert.AreEqual("sent", result.Result);
        }
    }

    [TestMethod]
    [DataRow("apns", true)]
    [DataRow("apns", false)]
    [DataRow("fcm", true)]
    [DataRow("fcm", false)]
    public async Task SendAsync_BlogIncludesBodyOnlyWhenItAddsText(string platform, bool repeatTitle)
    {
        using var appleKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var firebaseKey = RSA.Create(2048);
        var title = "Skillie en Haas " + new string('a', 120);
        var delivery = Delivery(platform) with
        {
            NotificationType = "blog_published", Href = "/blog/skillie-en-haas",
            Title = title, Body = repeatTitle ? title : "Lees die artikel oor die vertoning."
        };
        var handler = new Handler(async request =>
        {
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
                return JsonResponse(HttpStatusCode.OK, """{"access_token":"test","expires_in":3600}""");

            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = payload.RootElement;
            var data = platform == "apns" ? root : root.GetProperty("message").GetProperty("data");
            var alert = platform == "apns" ? root.GetProperty("aps").GetProperty("alert") : data;
            Assert.AreEqual(title[..120], alert.GetProperty("title").GetString());
            Assert.AreEqual(!repeatTitle, alert.TryGetProperty("body", out var body));
            if (!repeatTitle) Assert.AreEqual(delivery.Body, body.GetString());
            Assert.AreEqual(delivery.Href, data.GetProperty("href").GetString());
            Assert.AreEqual(delivery.NotificationType, data.GetProperty("notification_type").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var settings = platform == "apns" ? AppleSettings(appleKey) : FirebaseSettings(firebaseKey);
        var sender = new MobilePushSender(new ClientFactory(handler), Options.Create(settings), cache);

        Assert.AreEqual("sent", (await sender.SendAsync(delivery, TestContext.CancellationToken)).Result);
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

    [TestMethod]
    public async Task PublicationWorker_DrainsCommittedBatchesWithPushDisabled()
    {
        var calls = 0;
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var storage = new Handler(async request =>
        {
            Assert.AreEqual("/rest/v1/rpc/process_notification_publication_batch", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.IsEmpty(body.RootElement.EnumerateObject().ToArray());
            if (++calls <= 2) return JsonResponse(HttpStatusCode.OK, "200");
            drained.TrySetResult();
            return JsonResponse(HttpStatusCode.OK, "0");
        });
        using var http = new HttpClient(storage);
        var settings = Options.Create(new SupabaseOptions { Url = "https://storage.invalid", SecretKey = "fixture" });
        using var services = new ServiceCollection().AddSingleton(new SupabaseMobilePushStore(http, settings)).BuildServiceProvider();
        using var worker = new NotificationPublicationWorker(services.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<NotificationPublicationWorker>.Instance);
        await worker.StartAsync(TestContext.CancellationToken);
        try { await drained.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken); }
        finally { await worker.StopAsync(TestContext.CancellationToken); }
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    public async Task Worker_ResumesAfterAcknowledgementOutage_WithoutRepeatingAcceptedPushes()
    {
        // Every HTTP client is backed by an in-process handler. No sockets or real devices.
        var devices = Enumerable.Range(0, 2621).Select(i => Delivery(i % 2 == 0 ? "fcm" : "apns")).ToArray();
        var interrupted = devices[3];
        var lostReply = devices[4];
        var throttled = devices[5];
        var accepted = new ConcurrentDictionary<Guid, int>();
        var acknowledgements = new ConcurrentDictionary<Guid, bool>();
        var ackAttempts = new ConcurrentDictionary<Guid, int>();
        var scheduledRetries = new ConcurrentDictionary<Guid, bool>();
        var throttleRequests = 0;
        var directory = Directory.CreateTempSubdirectory("schink-receipts-");
        try
        {
            using var appleKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var settings = AppleSettings(appleKey);
            settings.FirebaseProjectId = "fixture";
            settings.FirebaseServiceAccountJson = "{}";
            settings.ReceiptDirectory = directory.FullName;
            using var cache = new MemoryCache(new MemoryCacheOptions());
            cache.Set("mobile-push:google-token", "fixture-token");
            using var services = new ServiceCollection().BuildServiceProvider();
            using var provider = new Handler(async request =>
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var id = request.RequestUri!.Host == "fcm.googleapis.com"
                    ? payload.RootElement.GetProperty("message").GetProperty("data").GetProperty("notification_id").GetGuid()
                    : payload.RootElement.GetProperty("notification_id").GetGuid();
                if (id == throttled.NotificationId && Interlocked.Increment(ref throttleRequests) == 1)
                    return JsonResponse(HttpStatusCode.TooManyRequests, "{}");
                accepted.AddOrUpdate(id, 1, (_, count) => count + 1);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var storage = new Handler(async request =>
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var id = payload.RootElement.GetProperty("p_delivery_id").GetGuid();
                switch (request.RequestUri!.Segments.Last())
                {
                    case "can_send_mobile_push_delivery":
                        return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(!acknowledgements.ContainsKey(id)));
                    case "complete_mobile_push_delivery":
                        if (payload.RootElement.GetProperty("p_result").GetString() == "retry") scheduledRetries[id] = true;
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    case "record_mobile_push_acceptance":
                        var attempt = ackAttempts.AddOrUpdate(id, 1, (_, count) => count + 1);
                        if (id == interrupted.DeliveryId && attempt <= 3)
                            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                        acknowledgements[id] = true;
                        if (id == lostReply.DeliveryId && attempt == 1)
                            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    default: throw new InvalidOperationException("Unexpected storage request.");
                }
            });
            using var http = new HttpClient(storage);
            var store = new SupabaseMobilePushStore(http, Options.Create(new SupabaseOptions {
                Url = "https://storage.invalid", SecretKey = "fixture"
            }));
            MobilePushWorker NewWorker() => new(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(settings),
                new MobilePushSender(new ClientFactory(provider), Options.Create(settings), cache), NullLogger<MobilePushWorker>.Instance);

            using (var firstWorker = NewWorker())
            {
                var journal = MobilePushReceiptJournal.Open(settings);
                foreach (var batch in devices.Chunk(20))
                    await firstWorker.ProcessBatchAsync(store, journal, batch, TestContext.CancellationToken);
            }
            Assert.HasCount(devices.Length - 2, acknowledgements);
            Assert.IsTrue(scheduledRetries.ContainsKey(throttled.DeliveryId));
            Assert.IsTrue(MobilePushReceiptJournal.Open(settings).HasReceipt(interrupted));
            Assert.AreEqual(2, ackAttempts[lostReply.DeliveryId]);
            using var restarted = NewWorker();
            var reclaimed = new[] { interrupted with { LeaseId = Guid.NewGuid() }, throttled with { LeaseId = Guid.NewGuid() } };
            await restarted.ProcessBatchAsync(store, MobilePushReceiptJournal.Open(settings), reclaimed, TestContext.CancellationToken);
            CollectionAssert.AreEquivalent(devices.Select(d => d.NotificationId).ToArray(), accepted.Keys.ToArray());
            Assert.IsTrue(accepted.Values.All(count => count == 1), "Every simulated phone must be accepted exactly once.");
            CollectionAssert.AreEquivalent(devices.Select(d => d.DeliveryId).ToArray(), acknowledgements.Keys.ToArray());
            Assert.AreEqual(2, throttleRequests);
            Assert.IsEmpty(directory.GetFiles());
        }
        finally { directory.Delete(recursive: true); }
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
