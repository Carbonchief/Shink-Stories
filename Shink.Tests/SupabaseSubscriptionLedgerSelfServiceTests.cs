using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    public async Task GetPaidSubscriptionAttentionAsync_FlagsActivePaidSubscriptionWithMissingRenewalDate()
    {
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      { "subscriber_id": "11111111-1111-1111-1111-111111111111", "disabled_at": null }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "subscription_id": "22222222-2222-2222-2222-222222222222",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "provider_payment_id": "SUB_missingrenewal",
                        "provider_token": "AUTH_retry",
                        "next_renewal_at": null,
                        "cancelled_at": null,
                        "status": "active"
                      }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var attention = await service.GetPaidSubscriptionAttentionAsync("ouer@example.com");

        Assert.IsTrue(attention.RequiresAttention);
        Assert.AreEqual("missing_next_payment", attention.Reason);
        Assert.AreEqual("schink-stories-maandeliks", attention.PlanSlug);
        Assert.IsTrue(attention.CanAttemptAutomaticRetry);
    }

    [TestMethod]
    public async Task TransferPaidSubscriptionToGratisAsync_CancelsProblematicPaidRowsAndKeepsFreeAccess()
    {
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "email": "ouer@example.com",
                        "first_name": "Ouer",
                        "last_name": "Een",
                        "display_name": "Ouer Een",
                        "mobile_number": "0820000000",
                        "disabled_at": null
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions") &&
                request.RequestUri?.Query.Contains("status=eq.active", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse(
                    """
                    [
                      {
                        "subscription_id": "22222222-2222-2222-2222-222222222222",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "SUB_transferfree",
                        "provider_email_token": "email-token-123",
                        "next_renewal_at": null,
                        "cancelled_at": null,
                        "status": "active"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/subscription/disable")
            {
                return JsonResponse("""{ "status": true, "message": "Subscription disabled successfully" }""");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscribers")
            {
                return JsonResponse("""[{ "subscriber_id": "11111111-1111-1111-1111-111111111111" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_tiers")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return JsonResponse("""[{ "subscription_id": "33333333-3333-3333-3333-333333333333" }]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.TransferPaidSubscriptionToGratisAsync("ouer@example.com");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(1, result.CancelledPaidSubscriptions);
        Assert.IsNotNull(handler.PaystackDisablePayload);
        Assert.IsTrue(handler.SubscriptionPatchPayloads.Any(payload => payload.Contains("\"status\":\"cancelled\"", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task TransferPaidSubscriptionToGratisAsync_LocallyCancelsBrokenPayFastRowWhenProviderCancelFails()
    {
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "email": "ouer@example.com",
                        "first_name": "Ouer",
                        "last_name": "Een",
                        "display_name": "Ouer Een",
                        "mobile_number": "0820000000",
                        "disabled_at": null
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions") &&
                request.RequestUri?.Query.Contains("status=eq.active", StringComparison.OrdinalIgnoreCase) == true)
            {
                return JsonResponse(
                    """
                    [
                      {
                        "subscription_id": "44444444-4444-4444-4444-444444444444",
                        "tier_code": "all_stories_yearly",
                        "provider": "payfast",
                        "source_system": "shink_app",
                        "provider_payment_id": "payfast-payment",
                        "provider_transaction_id": "3053334",
                        "provider_token": "payfast-token-123",
                        "next_renewal_at": null,
                        "cancelled_at": null,
                        "status": "active"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.AbsolutePath == "/subscriptions/payfast-token-123/cancel")
            {
                return JsonResponse("""{ "status": "failed", "message": "Subscription not found" }""");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscribers")
            {
                return JsonResponse("""[{ "subscriber_id": "11111111-1111-1111-1111-111111111111" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_tiers")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return JsonResponse("""[{ "subscription_id": "33333333-3333-3333-3333-333333333333" }]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.TransferPaidSubscriptionToGratisAsync("ouer@example.com");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(1, result.CancelledPaidSubscriptions);
        Assert.IsNotNull(handler.PayFastCancelRequest);
        Assert.IsTrue(handler.SubscriptionPatchPayloads.Any(payload => payload.Contains("\"status\":\"cancelled\"", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CancelPaidSubscriptionAsync_DisablesPaystackAndKeepsAccessUntilNextRenewal()
    {
        var nextRenewalAt = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      { "subscriber_id": "11111111-1111-1111-1111-111111111111", "disabled_at": null }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "22222222-2222-2222-2222-222222222222",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "provider_payment_id": "SUB_selfservice",
                        "provider_email_token": null,
                        "next_renewal_at": "{{nextRenewalAt:O}}",
                        "cancelled_at": null,
                        "status": "active"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/subscription/SUB_selfservice")
            {
                return JsonResponse(
                    """
                    {
                      "status": true,
                      "data": {
                        "subscription_code": "SUB_selfservice",
                        "email_token": "email-token-123"
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/subscription/disable")
            {
                return JsonResponse("""{ "status": true, "message": "Subscription disabled successfully" }""");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return JsonResponse(
                    """
                    [
                      { "subscription_id": "22222222-2222-2222-2222-222222222222" }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.CancelPaidSubscriptionAsync("ouer@example.com");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(nextRenewalAt, result.AccessEndsAtUtc);
        StringAssert.Contains(handler.PaystackDisablePayload!, "\"code\":\"SUB_selfservice\"");
        StringAssert.Contains(handler.PaystackDisablePayload!, "\"token\":\"email-token-123\"");

        var patchPayload = JsonSerializer.Deserialize<JsonElement>(handler.SubscriptionPatchPayload!);
        Assert.AreEqual("active", patchPayload.GetProperty("status").GetString());
        Assert.AreEqual(nextRenewalAt, patchPayload.GetProperty("cancelled_at").GetDateTimeOffset());
        Assert.AreEqual("email-token-123", patchPayload.GetProperty("provider_email_token").GetString());
    }

    [TestMethod]
    public async Task CancelPaidSubscriptionAsync_SchedulesLegacyRowsWithoutPaystackLookup()
    {
        var legacyRenewalAt = new DateTimeOffset(2026, 5, 27, 12, 40, 0, TimeSpan.Zero);
        var paystackRenewalAt = new DateTimeOffset(2026, 5, 27, 10, 40, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      { "subscriber_id": "11111111-1111-1111-1111-111111111111", "disabled_at": null }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "33333333-3333-3333-3333-333333333333",
                        "tier_code": "story_corner_monthly",
                        "provider": "paystack",
                        "source_system": "wordpress_pmpro",
                        "provider_payment_id": "wp-pmpro-current-2796",
                        "provider_email_token": null,
                        "next_renewal_at": "{{legacyRenewalAt:O}}",
                        "cancelled_at": null,
                        "status": "active"
                      },
                      {
                        "subscription_id": "22222222-2222-2222-2222-222222222222",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "SUB_selfservice",
                        "provider_email_token": "email-token-123",
                        "next_renewal_at": "{{paystackRenewalAt:O}}",
                        "cancelled_at": null,
                        "status": "active"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath.StartsWith("/subscription/wp-pmpro-current", StringComparison.Ordinal) == true)
            {
                Assert.Fail("Legacy imported rows must not be looked up in Paystack.");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/subscription/disable")
            {
                return JsonResponse("""{ "status": true, "message": "Subscription disabled successfully" }""");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return JsonResponse("""[{ "subscription_id": "patched" }]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.CancelPaidSubscriptionAsync("ouer@example.com");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(legacyRenewalAt, result.AccessEndsAtUtc);
        Assert.AreEqual(2, handler.SubscriptionPatchPayloads.Count);
        Assert.IsFalse(handler.PaystackLookups.Any(path => path.Contains("wp-pmpro-current", StringComparison.Ordinal)));
        StringAssert.Contains(handler.PaystackDisablePayload!, "\"code\":\"SUB_selfservice\"");
    }

    [TestMethod]
    public async Task CancelPaidSubscriptionAsync_CancelsPayFastTokenAndKeepsAccessUntilNextRenewal()
    {
        var nextRenewalAt = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      { "subscriber_id": "11111111-1111-1111-1111-111111111111", "disabled_at": null }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "44444444-4444-4444-4444-444444444444",
                        "tier_code": "all_stories_monthly",
                        "provider": "payfast",
                        "source_system": "shink_app",
                        "provider_payment_id": "all-stories-monthly-20260429120000",
                        "provider_transaction_id": "pf-payment-123",
                        "provider_token": "payfast-token-123",
                        "next_renewal_at": "{{nextRenewalAt:O}}",
                        "cancelled_at": null,
                        "status": "active"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.AbsolutePath == "/subscriptions/payfast-token-123/cancel")
            {
                return JsonResponse("""{ "status": "success", "data": { "response": "Subscription cancelled" } }""");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return JsonResponse("""[{ "subscription_id": "44444444-4444-4444-4444-444444444444" }]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.CancelPaidSubscriptionAsync("ouer@example.com");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(nextRenewalAt, result.AccessEndsAtUtc);
        Assert.IsNotNull(handler.PayFastCancelRequest);
        Assert.AreEqual("merchant-id", handler.PayFastCancelRequest!.Headers.First().Key);
        Assert.IsTrue(handler.PayFastCancelRequest.Headers.ContainsKey("signature"));

        var patchPayload = JsonSerializer.Deserialize<JsonElement>(handler.SubscriptionPatchPayload!);
        Assert.AreEqual("active", patchPayload.GetProperty("status").GetString());
        Assert.AreEqual(nextRenewalAt, patchPayload.GetProperty("cancelled_at").GetDateTimeOffset());
    }

    [TestMethod]
    public async Task CloseAccountAsync_DisablesSubscriber()
    {
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      { "subscriber_id": "11111111-1111-1111-1111-111111111111", "disabled_at": null }
                    ]
                    """);
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscribers")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.CloseAccountAsync("ouer@example.com");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        var patchPayload = JsonSerializer.Deserialize<JsonElement>(handler.SubscriberPatchPayload!);
        Assert.AreEqual("self_service", patchPayload.GetProperty("disabled_by_admin_email").GetString());
        Assert.AreEqual("Rekening deur gebruiker gesluit.", patchPayload.GetProperty("disabled_reason").GetString());
        Assert.AreEqual(JsonValueKind.String, patchPayload.GetProperty("disabled_at").ValueKind);
    }

    private static SupabaseSubscriptionLedgerService CreateService(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.supabase.co/")
        };

        var paystackService = new PaystackCheckoutService(
            httpClient,
            Options.Create(new PaystackOptions
            {
                SecretKey = "paystack-secret",
                InitializeUrl = "https://api.paystack.co/transaction/initialize",
                VerifyUrl = "https://api.paystack.co/transaction/verify",
                ChargeAuthorizationUrl = "https://api.paystack.co/transaction/charge_authorization"
            }));

        var payFastService = new PayFastCheckoutService(
            httpClient,
            Options.Create(new PayFastOptions
            {
                MerchantId = "10000100",
                MerchantKey = "merchant-key",
                Passphrase = "passphrase",
                ApiBaseUrl = "https://api.payfast.co.za",
                UseSandboxApi = true
            }));

        return new SupabaseSubscriptionLedgerService(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                ServiceRoleKey = "service-role-key"
            }),
            new NoopSubscriptionPaymentRecoveryEmailService(),
            new NoopSubscriptionNotificationEmailService(),
            paystackService,
            payFastService,
            NullLogger<SupabaseSubscriptionLedgerService>.Instance);
    }

    private static bool IsSupabaseGet(HttpRequestMessage request, string path) =>
        request.Method == HttpMethod.Get &&
        request.RequestUri?.Host == "example.supabase.co" &&
        request.RequestUri.AbsolutePath == path;

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public string? PaystackDisablePayload { get; private set; }
        public string? SubscriptionPatchPayload { get; private set; }
        public List<string> SubscriptionPatchPayloads { get; } = [];
        public List<string> PaystackLookups { get; } = [];
        public PayFastCancelRequest? PayFastCancelRequest { get; private set; }
        public string? SubscriberPatchPayload { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath.StartsWith("/subscription/", StringComparison.Ordinal) == true)
            {
                PaystackLookups.Add(request.RequestUri.AbsolutePath);
            }
            else if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/subscription/disable")
            {
                PaystackDisablePayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }
            else if (request.Method == new HttpMethod("PATCH") &&
                     request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                SubscriptionPatchPayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                if (SubscriptionPatchPayload is not null)
                {
                    SubscriptionPatchPayloads.Add(SubscriptionPatchPayload);
                }
            }
            else if (request.Method == new HttpMethod("PATCH") &&
                     request.RequestUri?.AbsolutePath == "/rest/v1/subscribers")
            {
                SubscriberPatchPayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }
            else if (request.Method == HttpMethod.Put &&
                     request.RequestUri?.AbsolutePath.StartsWith("/subscriptions/", StringComparison.Ordinal) == true)
            {
                PayFastCancelRequest = new PayFastCancelRequest(
                    request.RequestUri.ToString(),
                    request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value)));
            }

            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record PayFastCancelRequest(string Url, Dictionary<string, string> Headers);

    private sealed class NoopSubscriptionPaymentRecoveryEmailService : ISubscriptionPaymentRecoveryEmailService
    {
        public Task<SubscriptionPaymentRecoveryEmailSequence?> ScheduleSequenceAsync(
            SubscriptionPaymentRecoveryEmailRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SubscriptionPaymentRecoveryEmailSequence?>(null);

        public Task<string?> SendImmediateAsync(
            SubscriptionPaymentRecoveryEmailRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task CancelSequenceAsync(
            SubscriptionPaymentRecoveryEmailSequence sequence,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopSubscriptionNotificationEmailService : ISubscriptionNotificationEmailService
    {
        public Task SendSubscriptionConfirmationAsync(
            SubscriptionConfirmationEmailRequest request,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendSubscriptionEndedAsync(
            SubscriptionEndedEmailRequest request,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendAdminOpsAlertAsync(
            AdminOpsAlertEmailRequest request,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
