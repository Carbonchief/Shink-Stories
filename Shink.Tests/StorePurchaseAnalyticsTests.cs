using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class StorePurchaseAnalyticsTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PaidOrderRetriesWithTheSameEventIdentity(bool captureFails)
    {
        var handler = new OrderAnalyticsHandler(captureFails);
        using var client = new HttpClient(handler);
        var analytics = new StorePurchaseAnalytics(client,
            Options.Create(new SupabaseOptions { Url = "https://store.example.invalid", SecretKey = "fixture-database-key" }),
            new PostHogSettings("phc_fixture", "https://capture.example.invalid"),
            new TestEnvironment(), NullLogger<StorePurchaseAnalytics>.Instance);
        var cancellationToken = TestContext.CancellationToken;

        await analytics.DeliverPendingAsync(cancellationToken);
        Assert.AreEqual(captureFails ? 0 : 1, handler.MarkAttempts);
        Assert.IsFalse(handler.Reported);
        await analytics.DeliverPendingAsync(cancellationToken);
        await analytics.DeliverPendingAsync(cancellationToken);

        Assert.IsTrue(handler.Reported);
        Assert.HasCount(2, handler.Captures);
        Assert.AreEqual(handler.Captures[0], handler.Captures[1]);
        using var payload = JsonDocument.Parse(handler.Captures[0]);
        var root = payload.RootElement;
        Assert.AreEqual("winkel_purchase_verified", root.GetProperty("event").GetString());
        Assert.AreEqual(OrderAnalyticsHandler.OrderId, root.GetProperty("uuid").GetGuid());
        Assert.AreEqual($"winkel:{OrderAnalyticsHandler.VisitorId:D}", root.GetProperty("distinct_id").GetString());
        Assert.AreEqual(DateTimeOffset.Parse("2026-11-09T08:30:00Z"), root.GetProperty("timestamp").GetDateTimeOffset());
        var properties = root.GetProperty("properties");
        Assert.AreEqual("verified_order", properties.GetProperty("source").GetString());
        Assert.AreEqual("production", properties.GetProperty("environment").GetString());
        Assert.IsFalse(properties.GetProperty("$process_person_profile").GetBoolean());
        Assert.AreEqual(44000, properties.GetProperty("total_cents").GetInt32());
        Assert.AreEqual(2, properties.GetProperty("item_count").GetInt32());
        Assert.AreEqual("tiekie", properties.GetProperty("product_slugs")[0].GetString());
        Assert.AreEqual(1, properties.GetProperty("product_slugs").GetArrayLength());
        Assert.DoesNotContain("private@example.invalid", handler.Captures[0]);
        Assert.DoesNotContain("fixture-database-key", handler.Captures[0]);
    }

    public TestContext TestContext { get; set; } = null!;

    private sealed class OrderAnalyticsHandler(bool captureFails) : HttpMessageHandler
    {
        public static readonly Guid OrderId = Guid.Parse("11bd3352-f818-4ca6-801c-74030318f038");
        public static readonly Guid VisitorId = Guid.Parse("5cca1560-1809-463e-b0c9-af9d31c1f5d0");
        public List<string> Captures { get; } = [];
        public bool Reported { get; private set; }
        public int MarkAttempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (request.Method == HttpMethod.Get)
            {
                Assert.AreEqual("/rest/v1/store_orders", uri.AbsolutePath);
                Assert.Contains("payment_status=eq.paid", uri.Query);
                Assert.Contains("paid_at=not.is.null", uri.Query);
                Assert.Contains("analytics_visitor_id=not.is.null", uri.Query);
                Assert.Contains("analytics_reported_at=is.null", uri.Query);
                Assert.DoesNotContain("customer_", uri.Query);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Reported ? "[]" : $$"""
                        [{"order_id":"{{OrderId:D}}","analytics_visitor_id":"{{VisitorId:D}}","paid_at":"2026-11-09T08:30:00Z",
                        "currency":"ZAR","total_price_zar":440.00,"customer_email":"private@example.invalid",
                        "items":[{"product_slug":"tiekie","quantity":2},{"product_slug":"pudo-locker-delivery","quantity":1}]}]
                        """, Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Post)
            {
                Assert.AreEqual("https://capture.example.invalid/i/v0/e/", uri.AbsoluteUri);
                Assert.IsNull(request.Headers.Authorization);
                Captures.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(captureFails && Captures.Count == 1
                    ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            }

            Assert.AreEqual(HttpMethod.Patch, request.Method);
            Assert.AreEqual("store.example.invalid", uri.Host);
            Assert.Contains($"order_id=eq.{OrderId:D}", uri.Query);
            Assert.Contains("analytics_reported_at=is.null", uri.Query);
            using var marker = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.IsTrue(marker.RootElement.GetProperty("analytics_reported_at").TryGetDateTimeOffset(out _));
            MarkAttempts++;
            Reported = captureFails || MarkAttempts > 1;
            return new HttpResponseMessage(Reported ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Shink";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
