using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class SupabaseAbandonedCartRecoveryServiceTests
{
    [TestMethod]
    public async Task StartSequenceAsync_SkipsNonWinkelSchedulingWhenActiveSameSubjectRecoveryExists()
    {
        var resendCalls = 0;
        var createRecoveryCalls = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscribers")
            {
                StringAssert.Contains(request.RequestUri.Query, "email=eq.ouer%40example.com");
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/abandoned_cart_recoveries")
            {
                var query = request.RequestUri.Query;
                StringAssert.Contains(query, "resolved_at=is.null");
                StringAssert.Contains(query, "source_type=eq.subscription");
                Assert.IsFalse(query.Contains("source_key=", StringComparison.Ordinal));
                StringAssert.Contains(query, "customer_email=eq.ouer%40example.com");

                return JsonResponse(
                    """
                    [
                      {
                        "recovery_id": "11111111-1111-1111-1111-111111111111",
                        "source_type": "subscription",
                        "source_key": "all_stories_monthly",
                        "checkout_reference": "older-reference",
                        "provider": "paystack",
                        "customer_email": "ouer@example.com",
                        "item_name": "Alle stories",
                        "item_summary": "Maandeliks"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.Host == "api.resend.com")
            {
                resendCalls++;
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/abandoned_cart_recoveries")
            {
                createRecoveryCalls++;
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(new HttpClient(handler));

        await service.StartSequenceAsync(
            new AbandonedCartRecoveryStartRequest(
                SourceType: "subscription",
                SourceKey: "all_stories_monthly",
                CheckoutReference: "new-reference",
                Provider: "paystack",
                CustomerEmail: "Ouer@Example.com",
                CustomerName: "Ouer",
                ItemName: "Alle stories",
                ItemSummary: "Maandeliks",
                CartTotalZar: 99m,
                CheckoutUrl: "https://checkout.example.com/new-reference",
                OptOutBaseUrl: "https://schink.example.com"));

        Assert.AreEqual(0, resendCalls);
        Assert.AreEqual(0, createRecoveryCalls);
    }

    [TestMethod]
    public async Task StartSequenceAsync_SkipsSubscriptionRecoveryWhenCustomerAlreadyHasRelevantPaidAccess()
    {
        var abandonedRecoveryLookupCalls = 0;
        var resendCalls = 0;
        var createRecoveryCalls = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscribers")
            {
                StringAssert.Contains(request.RequestUri.Query, "email=eq.ouer%40example.com");
                return JsonResponse(
                    """
                    [
                      {
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "disabled_at": null
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                StringAssert.Contains(request.RequestUri.Query, "subscriber_id=eq.11111111-1111-1111-1111-111111111111");
                StringAssert.Contains(request.RequestUri.Query, "status=eq.active");
                return JsonResponse(
                    """
                    [
                      {
                        "status": "active",
                        "tier_code": "all_stories_monthly",
                        "next_renewal_at": "2099-01-01T00:00:00Z",
                        "cancelled_at": null
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/abandoned_cart_recoveries")
            {
                abandonedRecoveryLookupCalls++;
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.Host == "api.resend.com")
            {
                resendCalls++;
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/abandoned_cart_recoveries")
            {
                createRecoveryCalls++;
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(new HttpClient(handler));

        await service.StartSequenceAsync(
            new AbandonedCartRecoveryStartRequest(
                SourceType: "subscription",
                SourceKey: "story_corner_monthly",
                CheckoutReference: "new-reference",
                Provider: "paystack",
                CustomerEmail: "Ouer@Example.com",
                CustomerName: "Ouer",
                ItemName: "Storie Hoekie",
                ItemSummary: "Maandeliks",
                CartTotalZar: 49m,
                CheckoutUrl: "https://checkout.example.com/new-reference",
                OptOutBaseUrl: "https://schink.example.com"));

        Assert.AreEqual(0, abandonedRecoveryLookupCalls);
        Assert.AreEqual(0, resendCalls);
        Assert.AreEqual(0, createRecoveryCalls);
    }

    [TestMethod]
    public async Task StartSequenceAsync_AllowsWinkelSchedulingWhenActiveSimilarRecoveryExists()
    {
        var resendCalls = 0;
        var createRecoveryCalls = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/abandoned_cart_recoveries")
            {
                createRecoveryCalls++;
                return JsonResponse(
                    """
                    [
                      {
                        "recovery_id": "11111111-1111-1111-1111-111111111111",
                        "source_type": "store_order",
                        "source_key": "multi-item-order",
                        "checkout_reference": "order-reference",
                        "provider": "paystack",
                        "customer_email": "ouer@example.com",
                        "item_name": "Winkel bestelling",
                        "item_summary": "1 item"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.Host == "api.resend.com")
            {
                resendCalls++;
                return JsonResponse("""{"id":"scheduled-email-id"}""");
            }

            if (request.Method.Method == "PATCH" &&
                request.RequestUri?.AbsolutePath == "/rest/v1/abandoned_cart_recoveries")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(new HttpClient(handler));

        await service.StartSequenceAsync(
            new AbandonedCartRecoveryStartRequest(
                SourceType: "store_order",
                SourceKey: "multi-item-order",
                CheckoutReference: "order-reference",
                Provider: "paystack",
                CustomerEmail: "Ouer@Example.com",
                CustomerName: "Ouer",
                ItemName: "Winkel bestelling",
                ItemSummary: "1 item",
                CartTotalZar: 150m,
                CheckoutUrl: "https://checkout.example.com/order-reference",
                OptOutBaseUrl: "https://schink.example.com"));

        Assert.AreEqual(1, createRecoveryCalls);
        Assert.AreEqual(3, resendCalls);
    }

    [TestMethod]
    public async Task ResolveSubscriptionRecoveriesAsync_ResolvesCoveredRecoveriesForPurchasedTier()
    {
        var cancelCalls = 0;
        var patchCalls = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/abandoned_cart_recoveries")
            {
                var query = request.RequestUri.Query;
                StringAssert.Contains(query, "source_type=eq.subscription");
                Assert.IsFalse(query.Contains("source_key=", StringComparison.Ordinal));
                StringAssert.Contains(query, "customer_email=eq.ouer%40example.com");

                return JsonResponse(
                    """
                    [
                      {
                        "recovery_id": "11111111-1111-1111-1111-111111111111",
                        "source_type": "subscription",
                        "source_key": "story_corner_monthly",
                        "checkout_reference": "older-reference",
                        "provider": "paystack",
                        "customer_email": "ouer@example.com",
                        "item_name": "Storie Hoekie",
                        "item_summary": "Maandeliks",
                        "first_email_id": "email-1",
                        "second_email_id": "email-2",
                        "final_email_id": "email-3"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.Host == "api.resend.com" &&
                request.RequestUri.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal))
            {
                cancelCalls++;
                return JsonResponse("""{"id":"cancelled-email-id"}""");
            }

            if (request.Method.Method == "PATCH" &&
                request.RequestUri?.AbsolutePath == "/rest/v1/abandoned_cart_recoveries")
            {
                patchCalls++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(new HttpClient(handler));

        await service.ResolveSubscriptionRecoveriesAsync(
            customerEmail: "Ouer@Example.com",
            tierCode: "all_stories_monthly",
            resolution: "paid");

        Assert.AreEqual(3, cancelCalls);
        Assert.AreEqual(1, patchCalls);
    }

    [TestMethod]
    public void ActiveSimilarityGuardMigration_AddsPartialUniqueIndex()
    {
        var migration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260430_abandoned_cart_active_similarity_guard.sql"));

        StringAssert.Contains(migration, "duplicate_suppressed");
        StringAssert.Contains(migration, "uq_abandoned_cart_recoveries_active_customer_source");
        StringAssert.Contains(migration, "on public.abandoned_cart_recoveries(source_type, source_key, customer_email)");
        StringAssert.Contains(migration, "where resolved_at is null");
    }

    [TestMethod]
    public void NonWinkelSubjectGuardMigration_AddsPartialUniqueIndex()
    {
        var migration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260430_abandoned_cart_non_winkel_subject_guard.sql"));

        StringAssert.Contains(migration, "drop index if exists public.uq_abandoned_cart_recoveries_active_customer_source");
        StringAssert.Contains(migration, "uq_abandoned_cart_recoveries_active_non_winkel_subject");
        StringAssert.Contains(migration, "on public.abandoned_cart_recoveries(customer_email, source_type)");
        StringAssert.Contains(migration, "source_type <> 'store_order'");
    }

    private static SupabaseAbandonedCartRecoveryService CreateService(HttpClient httpClient) =>
        new(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                ServiceRoleKey = "service-role-key"
            }),
            Options.Create(new ResendOptions
            {
                ApiKey = "resend-key",
                FromEmail = "no-reply@example.com",
                Templates = new ResendTemplateOptions
                {
                    AbandonedCartRecovery = new AbandonedCartRecoveryTemplateOptions
                    {
                        Hour1TemplateId = "hour-1",
                        Hour24TemplateId = "hour-24",
                        Day7TemplateId = "day-7"
                    }
                }
            }),
            NullLogger<SupabaseAbandonedCartRecoveryService>.Instance);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string GetRepoPath(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new FileNotFoundException("Could not find repo file.", Path.Combine(parts));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
