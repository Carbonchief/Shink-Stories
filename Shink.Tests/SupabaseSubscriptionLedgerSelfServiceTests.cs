using System.Net;
using System.Security.Cryptography;
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
        var timestamp = handler.PayFastCancelRequest.Headers["timestamp"];
        var expectedSignaturePayload = $"merchant-id=10000100&passphrase=passphrase&timestamp={Uri.EscapeDataString(timestamp)}&version=v1";
        var expectedSignature = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(expectedSignaturePayload))).ToLowerInvariant();
        Assert.AreEqual(expectedSignature, handler.PayFastCancelRequest.Headers["signature"]);

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

    [TestMethod]
    public async Task TryRepairPaidSubscriptionAsync_TreatsPaystackQueuedChargeAsPending()
    {
        var paystackChargeCalls = 0;
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
                        "disabled_at": null
                      }
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
                        "tier_code": "all_stories_yearly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "wp-pmpro-current-2681",
                        "provider_transaction_id": "EFE7D68193",
                        "provider_token": "AUTH_retry",
                        "next_renewal_at": null,
                        "cancelled_at": null,
                        "status": "active"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/charge_authorization")
            {
                paystackChargeCalls++;
                return JsonResponse(
                    """
                    {
                      "status": true,
                      "data": {
                        "id": 6095474321,
                        "status": "queued",
                        "amount": 79000,
                        "currency": "ZAR",
                        "reference": "repair-20260430150911-22222222-2222-2222-2222-222222222222",
                        "paid_at": "2026-04-30T15:09:11.000Z"
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.TryRepairPaidSubscriptionAsync("ouer@example.com");

        Assert.IsFalse(result.IsRecovered);
        Assert.IsTrue(result.IsPending);
        Assert.AreEqual("schink-stories-jaarliks", result.PlanSlug);
        Assert.AreEqual(1, paystackChargeCalls);
        Assert.AreEqual(0, handler.SubscriptionPatchPayloads.Count);
    }

    [TestMethod]
    public async Task TryRepairPaidSubscriptionAsync_UsesStoredLegacyBillingAmountForPaystackRetry()
    {
        long? chargedAmountInCents = null;
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
                        "disabled_at": null
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                StringAssert.Contains(request.RequestUri!.Query, "billing_amount_zar");

                return JsonResponse(
                    """
                    [
                      {
                        "subscription_id": "22222222-2222-2222-2222-222222222222",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "SUB_legacy_monthly",
                        "provider_transaction_id": "TRX_legacy_monthly",
                        "provider_token": "AUTH_retry",
                        "next_renewal_at": null,
                        "cancelled_at": null,
                        "status": "active",
                        "billing_amount_zar": 49.00
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/charge_authorization")
            {
                var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
                chargedAmountInCents = payload.GetProperty("amount").GetInt64();

                return JsonResponse(
                    """
                    {
                      "status": false,
                      "message": "Card declined",
                      "data": {
                        "status": "failed",
                        "reference": "repair-legacy-monthly"
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.TryRepairPaidSubscriptionAsync("ouer@example.com");

        Assert.IsFalse(result.IsRecovered);
        Assert.IsFalse(result.IsPending);
        Assert.AreEqual("schink-stories-maandeliks", result.PlanSlug);
        Assert.AreEqual(4900L, chargedAmountInCents);
    }

    [TestMethod]
    public async Task TryRepairPaidSubscriptionAsync_TreatsDuplicateRepairReferenceAsPending()
    {
        var repairReferences = new List<string>();
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
                        "disabled_at": null
                      }
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
                        "tier_code": "all_stories_yearly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "wp-pmpro-current-2681",
                        "provider_transaction_id": "EFE7D68193",
                        "provider_token": "AUTH_retry",
                        "next_renewal_at": null,
                        "cancelled_at": null,
                        "status": "active"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/charge_authorization")
            {
                var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var document = JsonDocument.Parse(payload);
                var reference = document.RootElement.GetProperty("reference").GetString()!;
                repairReferences.Add(reference);

                return JsonResponse(
                    $$"""
                    {
                      "status": false,
                      "message": "Duplicate transaction reference",
                      "data": {
                        "status": "failed",
                        "reference": "{{reference}}"
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var firstResult = await service.TryRepairPaidSubscriptionAsync("ouer@example.com");
        var secondResult = await service.TryRepairPaidSubscriptionAsync("ouer@example.com");

        Assert.IsTrue(firstResult.IsPending);
        Assert.IsTrue(secondResult.IsPending);
        Assert.AreEqual(2, repairReferences.Count);
        Assert.AreEqual(repairReferences[0], repairReferences[1]);
        Assert.AreEqual(0, handler.SubscriptionPatchPayloads.Count);
    }

    [TestMethod]
    public async Task TryRepairPaidSubscriptionAsync_ReusesRecentQueuedRepairInsteadOfChargingAgain()
    {
        var paystackChargeCalls = 0;
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
                        "disabled_at": null
                      }
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
                        "tier_code": "all_stories_yearly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "wp-pmpro-current-2681",
                        "provider_transaction_id": "EFE7D68193",
                        "provider_token": "AUTH_retry",
                        "next_renewal_at": null,
                        "cancelled_at": null,
                        "status": "active"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "event_status": "queued",
                        "provider_transaction_id": "6095474321",
                        "received_at": "2099-01-01T00:00:00Z",
                        "payload": {
                          "data": {
                            "status": "queued",
                            "reference": "repair-20990101000000-22222222-2222-2222-2222-222222222222",
                            "metadata": {
                              "source": "subscription_authorization_retry"
                            }
                          }
                        }
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/charge_authorization")
            {
                paystackChargeCalls++;
                return JsonResponse("""{ "status": true, "data": { "status": "queued" } }""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.TryRepairPaidSubscriptionAsync("ouer@example.com");

        Assert.IsFalse(result.IsRecovered);
        Assert.IsTrue(result.IsPending);
        Assert.AreEqual("schink-stories-jaarliks", result.PlanSlug);
        Assert.AreEqual(0, paystackChargeCalls);
    }

    [TestMethod]
    public async Task RecordPaystackEventAsync_FailedRecurringPaymentSeedsOriginalAmountForRetry()
    {
        var originalSubscriptionId = "22222222-2222-2222-2222-222222222222";
        var originalProviderPaymentId = "SUB_pg02qbp0h6cb015";
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                return JsonResponse("[]");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "{{originalSubscriptionId}}",
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "{{originalProviderPaymentId}}",
                        "provider_transaction_id": null,
                        "provider_token": "AUTH_retry",
                        "status": "active",
                        "billing_amount_zar": null,
                        "billing_period_months": null,
                        "billing_amount_source": null
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "email": "ouer@example.com",
                        "first_name": "Ouer",
                        "display_name": "Ouer Een"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_payment_recoveries")
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_payment_recoveries")
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "recovery_id": "d1fe972f-4421-460e-a3c2-744cb2c2ef96",
                        "subscription_id": "{{originalSubscriptionId}}",
                        "provider": "paystack",
                        "provider_payment_id": "{{originalProviderPaymentId}}",
                        "first_failed_at": "2026-05-06T20:00:14Z",
                        "grace_ends_at": "2026-05-11T20:07:13Z",
                        "authorization_retry_status": "pending"
                      }
                    ]
                    """);
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.RecordPaystackEventAsync(
            $$"""
            {
              "event": "invoice.payment_failed",
              "data": {
                "id": 6099999999,
                "status": "failed",
                "amount": 5500,
                "customer": {
                  "email": "ouer@example.com"
                },
                "subscription": {
                  "subscription_code": "{{originalProviderPaymentId}}"
                }
              }
            }
            """);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(originalSubscriptionId, result.SubscriptionId);
        Assert.IsTrue(
            handler.SubscriptionPatchPayloads.Any(IsOriginalPaystackFailureAmountPatch),
            "The failed Paystack amount should be stored so the automatic retry uses the original amount.");
    }

    [TestMethod]
    public async Task RecordPaystackEventAsync_AuthorizationRetrySuccessResolvesOriginalRecovery()
    {
        var originalSubscriptionId = "22222222-2222-2222-2222-222222222222";
        var originalProviderPaymentId = "SUB_pg02qbp0h6cb015";
        var retryReference = "retry-20260507200713-d1fe972f-4421-460e-a3c2-744cb2c2ef96";
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                return JsonResponse("[]");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "{{originalSubscriptionId}}",
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "{{originalProviderPaymentId}}",
                        "provider_transaction_id": "{{originalProviderPaymentId}}",
                        "provider_token": "AUTH_retry",
                        "status": "active",
                        "billing_amount_zar": 79.00,
                        "billing_period_months": 1,
                        "billing_amount_source": "paystack_payload"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "email": "ouer@example.com",
                        "first_name": "Ouer",
                        "display_name": "Ouer Een"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_payment_recoveries"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "recovery_id": "d1fe972f-4421-460e-a3c2-744cb2c2ef96",
                        "subscription_id": "{{originalSubscriptionId}}",
                        "provider": "paystack",
                        "provider_payment_id": "{{originalProviderPaymentId}}",
                        "first_failed_at": "2026-05-06T20:00:14Z",
                        "grace_ends_at": "2026-05-11T20:07:13Z",
                        "authorization_retry_status": "failed",
                        "authorization_retry_reference": "{{retryReference}}",
                        "authorization_retry_error": "Transaction will be processed later.",
                        "emails_scheduled_at": "2026-05-07T20:07:20Z",
                        "immediate_email_id": "sent-email",
                        "warning_email_id": "warning-email",
                        "suspension_email_id": "suspension-email"
                      }
                    ]
                    """);
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath is "/rest/v1/subscriptions" or "/rest/v1/subscription_payment_recoveries")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.RecordPaystackEventAsync(
            $$"""
            {
              "event": "charge.success",
              "data": {
                "id": 6122688188,
                "status": "success",
                "reference": "{{retryReference}}",
                "amount": 7900,
                "paid_at": "2026-05-07T20:07:16Z",
                "customer": {
                  "email": "ouer@example.com"
                },
                "metadata": {
                  "source": "subscription_authorization_retry",
                  "subscription_id": "{{originalSubscriptionId}}",
                  "provider_payment_id": "{{originalProviderPaymentId}}",
                  "tier_code": "all_stories_monthly",
                  "billing_period_months": 1
                }
              }
            }
            """);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(originalSubscriptionId, result.SubscriptionId);
        Assert.IsTrue(
            handler.SubscriptionPatchPayloads.Any(payload =>
                payload.Contains("\"status\":\"active\"", StringComparison.Ordinal) &&
                payload.Contains("\"next_renewal_at\":\"2026-06-07T20:07:16Z\"", StringComparison.Ordinal)),
            "The original subscription should be renewed from the retry success payload.");
        Assert.IsTrue(
            handler.PaymentRecoveryPatchPayloads.Any(payload =>
                payload.Contains("\"authorization_retry_status\":\"succeeded\"", StringComparison.Ordinal) &&
                payload.Contains($"\"authorization_retry_reference\":\"{retryReference}\"", StringComparison.Ordinal)),
            "The original recovery row should record the async retry success.");
        Assert.IsTrue(
            handler.PaymentRecoveryPatchPayloads.Any(payload =>
                payload.Contains("\"resolution\":\"recovered\"", StringComparison.Ordinal)),
            "The original recovery row should be resolved as recovered.");
        Assert.IsTrue(
            handler.SubscriptionEventPayloads.Any(payload =>
                payload.Contains($"\"provider_payment_id\":\"{originalProviderPaymentId}\"", StringComparison.Ordinal) &&
                payload.Contains($"\"subscription_id\":\"{originalSubscriptionId}\"", StringComparison.Ordinal)),
            "The success event should be linked to the original subscription, not a retry-reference subscription.");
    }

    [TestMethod]
    public async Task RecordPaystackEventAsync_RecurringChargeSuccessWithAuthorizationTokenRenewsOriginalSubscription()
    {
        var originalSubscriptionId = "22222222-2222-2222-2222-222222222222";
        var originalProviderPaymentId = "SUB_pg02qbp0h6cb015";
        var recurringReference = "03c9fa42f55322c25d97b7c9a03976441c08e7987e467332";
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                return JsonResponse("[]");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "{{originalSubscriptionId}}",
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "{{originalProviderPaymentId}}",
                        "provider_transaction_id": "{{recurringReference}}",
                        "provider_token": "AUTH_retry",
                        "status": "active",
                        "billing_amount_zar": 79.00,
                        "billing_period_months": 1,
                        "billing_amount_source": "paystack_payload"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "email": "ouer@example.com",
                        "first_name": "Ouer",
                        "display_name": "Ouer Een"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_payment_recoveries"))
            {
                return JsonResponse("[]");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.RecordPaystackEventAsync(
            $$"""
            {
              "event": "charge.success",
              "data": {
                "id": 6130238443,
                "status": "success",
                "reference": "{{recurringReference}}",
                "amount": 7900,
                "paid_at": "2026-05-10T05:15:11Z",
                "customer": {
                  "email": "ouer@example.com"
                },
                "authorization": {
                  "authorization_code": "AUTH_retry"
                },
                "plan": {
                  "amount": 7900,
                  "interval": "monthly"
                }
              }
            }
            """);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(originalSubscriptionId, result.SubscriptionId);
        Assert.IsTrue(
            handler.SubscriptionPatchPayloads.Any(payload =>
                payload.Contains("\"status\":\"active\"", StringComparison.Ordinal) &&
                payload.Contains("\"next_renewal_at\":\"2026-06-10T05:15:11Z\"", StringComparison.Ordinal)),
            "Recurring charge.success events should renew the existing subscription matched by Paystack authorization token.");
        Assert.IsTrue(
            handler.SubscriptionEventPayloads.Any(payload =>
                payload.Contains($"\"provider_payment_id\":\"{originalProviderPaymentId}\"", StringComparison.Ordinal) &&
                payload.Contains("\"provider_transaction_id\":\"6130238443\"", StringComparison.Ordinal) &&
                payload.Contains($"\"subscription_id\":\"{originalSubscriptionId}\"", StringComparison.Ordinal)),
            "The event should be captured against the original subscription, not a new payment-reference subscription.");
    }

    [TestMethod]
    public async Task RecordPaystackEventAsync_SubscriptionCreateCanonicalizesInitialChargeSuccessRow()
    {
        var originalSubscriptionId = "22222222-2222-2222-2222-222222222222";
        var subscriptionCode = "SUB_x97u02ht01jysfp";
        var checkoutReference = "storie-hoekie-maandeliks-20260511180929-6ae838fdc804411588d3d9e8b0836d13";
        var canonicalized = false;
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                return JsonResponse("[]");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                var query = request.RequestUri?.Query ?? string.Empty;
                if (query.Contains($"provider_payment_id=eq.{Uri.EscapeDataString(subscriptionCode)}", StringComparison.Ordinal))
                {
                    return JsonResponse(
                        canonicalized
                            ? $$"""
                               [
                                 {
                                   "subscription_id": "{{originalSubscriptionId}}",
                                   "subscriber_id": "11111111-1111-1111-1111-111111111111",
                                   "tier_code": "story_corner_monthly",
                                   "provider": "paystack",
                                   "source_system": "shink_app",
                                   "provider_payment_id": "{{subscriptionCode}}",
                                   "provider_transaction_id": "6135862592",
                                   "provider_token": "AUTH_signup",
                                   "provider_email_token": "dyx4196k38od3k0",
                                   "status": "active",
                                   "billing_amount_zar": 55.00,
                                   "billing_period_months": 1,
                                   "billing_amount_source": "paystack_payload"
                                 }
                               ]
                               """
                            : "[]");
                }

                if (query.Contains("provider_token=eq.AUTH_signup", StringComparison.Ordinal) &&
                    query.Contains("tier_code=eq.story_corner_monthly", StringComparison.Ordinal))
                {
                    return JsonResponse(
                        $$"""
                        [
                          {
                            "subscription_id": "{{originalSubscriptionId}}",
                            "subscriber_id": "11111111-1111-1111-1111-111111111111",
                            "tier_code": "story_corner_monthly",
                            "provider": "paystack",
                            "source_system": "shink_app",
                            "provider_payment_id": "{{checkoutReference}}",
                            "provider_transaction_id": "6135862592",
                            "provider_token": "AUTH_signup",
                            "provider_email_token": null,
                            "status": "active",
                            "billing_amount_zar": 55.00,
                            "billing_period_months": 1,
                            "billing_amount_source": "paystack_payload"
                          }
                        ]
                        """);
                }

                if (query.Contains($"provider_payment_id=eq.{Uri.EscapeDataString(checkoutReference)}", StringComparison.Ordinal))
                {
                    return JsonResponse(
                        $$"""
                        [
                          {
                            "subscription_id": "{{originalSubscriptionId}}",
                            "subscriber_id": "11111111-1111-1111-1111-111111111111",
                            "tier_code": "story_corner_monthly",
                            "provider": "paystack",
                            "source_system": "shink_app",
                            "provider_payment_id": "{{checkoutReference}}",
                            "provider_transaction_id": "6135862592",
                            "provider_token": "AUTH_signup",
                            "provider_email_token": null,
                            "status": "active",
                            "billing_amount_zar": 55.00,
                            "billing_period_months": 1,
                            "billing_amount_source": "paystack_payload"
                          }
                        ]
                        """);
                }

                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscribers")
            {
                return JsonResponse("""[{ "subscriber_id": "11111111-1111-1111-1111-111111111111" }]""");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                var query = request.RequestUri?.Query ?? string.Empty;
                if (query.Contains("select=email", StringComparison.Ordinal))
                {
                    return JsonResponse("""[{ "email": "ouer@example.com" }]""");
                }

                return JsonResponse("""[{ "first_name": "Ouer", "display_name": "Ouer Een" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                if (request.Content?.ReadAsStringAsync().GetAwaiter().GetResult().Contains(checkoutReference, StringComparison.Ordinal) == true)
                {
                    return JsonResponse($$"""[{ "subscription_id": "{{originalSubscriptionId}}" }]""");
                }

                Assert.Fail("subscription.create should canonicalize the initial charge.success row instead of inserting a duplicate subscription.");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                canonicalized = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_payment_recoveries")
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var chargeSuccess = await service.RecordPaystackEventAsync(
            $$"""
            {
              "event": "charge.success",
              "data": {
                "id": 6135862592,
                "status": "success",
                "reference": "{{checkoutReference}}",
                "amount": 5500,
                "paid_at": "2026-05-11T18:10:41Z",
                "customer": {
                  "email": "ouer@example.com"
                },
                "metadata": {
                  "subscription_key": "{{checkoutReference}}",
                  "tier_code": "story_corner_monthly"
                },
                "authorization": {
                  "authorization_code": "AUTH_signup"
                },
                "plan": {
                  "amount": 5500,
                  "interval": "monthly"
                }
              }
            }
            """);

        Assert.IsTrue(chargeSuccess.IsSuccess);
        Assert.AreEqual(originalSubscriptionId, chargeSuccess.SubscriptionId);

        var subscriptionCreate = await service.RecordPaystackEventAsync(
            $$"""
            {
              "event": "subscription.create",
              "data": {
                "id": 1164259,
                "status": "active",
                "email_token": "dyx4196k38od3k0",
                "next_payment_date": "2026-06-11T18:10:00.000Z",
                "customer": {
                  "email": "ouer@example.com"
                },
                "authorization": {
                  "authorization_code": "AUTH_signup"
                },
                "subscription": {
                  "subscription_code": "{{subscriptionCode}}"
                },
                "plan": {
                  "amount": 5500,
                  "interval": "monthly"
                }
              }
            }
            """);

        Assert.IsTrue(subscriptionCreate.IsSuccess);
        Assert.AreEqual(originalSubscriptionId, subscriptionCreate.SubscriptionId);
        Assert.IsTrue(
            handler.SubscriptionPatchPayloads.Any(payload =>
                payload.Contains($"\"provider_payment_id\":\"{subscriptionCode}\"", StringComparison.Ordinal) &&
                payload.Contains("\"provider_email_token\":\"dyx4196k38od3k0\"", StringComparison.Ordinal) &&
                payload.Contains("\"billing_amount_zar\":55", StringComparison.Ordinal)),
            "subscription.create should rewrite the original charge.success row onto the canonical Paystack subscription code.");
        Assert.IsTrue(
            handler.SubscriptionEventPayloads.Any(payload =>
                payload.Contains($"\"provider_payment_id\":\"{subscriptionCode}\"", StringComparison.Ordinal) &&
                payload.Contains($"\"subscription_id\":\"{originalSubscriptionId}\"", StringComparison.Ordinal)),
            "The subscription.create event should be captured against the original subscription row.");
    }

    [TestMethod]
    public async Task RecordPaystackEventAsync_RepairChargeSuccessReusesExistingSubscriptionRow()
    {
        var originalSubscriptionId = "0c6ff3c0-48ff-44fd-bc91-c449acb90ab0";
        var originalProviderPaymentId = "wp-pmpro-current-2681";
        var repairReference = "repair-20260430150912-0c6ff3c0-48ff-44fd-bc91-c449acb90ab0";
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                return JsonResponse("[]");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                var query = request.RequestUri?.Query ?? string.Empty;
                if (query.Contains($"provider_payment_id=eq.{Uri.EscapeDataString(repairReference)}", StringComparison.Ordinal))
                {
                    return JsonResponse("[]");
                }

                if (query.Contains("provider_token=eq.AUTH_retry", StringComparison.Ordinal) &&
                    query.Contains("tier_code=eq.all_stories_yearly", StringComparison.Ordinal))
                {
                    return JsonResponse(
                        $$"""
                        [
                          {
                            "subscription_id": "{{originalSubscriptionId}}",
                            "subscriber_id": "11111111-1111-1111-1111-111111111111",
                            "tier_code": "all_stories_yearly",
                            "provider": "paystack",
                            "source_system": "wordpress_pmpro",
                            "provider_payment_id": "{{originalProviderPaymentId}}",
                            "provider_transaction_id": "EFE7D68193",
                            "provider_token": "AUTH_retry",
                            "provider_email_token": null,
                            "status": "active",
                            "billing_amount_zar": null,
                            "billing_period_months": null,
                            "billing_amount_source": null
                          }
                        ]
                        """);
                }

                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscribers")
            {
                return JsonResponse("""[{ "subscriber_id": "11111111-1111-1111-1111-111111111111" }]""");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                var query = request.RequestUri?.Query ?? string.Empty;
                if (query.Contains("select=email", StringComparison.Ordinal))
                {
                    return JsonResponse("""[{ "email": "ouer@example.com" }]""");
                }

                return JsonResponse("""[{ "first_name": "Ouer", "display_name": "Ouer Een" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                Assert.Fail("repair charge.success should reuse the existing subscription row instead of inserting a repair-reference subscription.");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_payment_recoveries")
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.RecordPaystackEventAsync(
            $$"""
            {
              "event": "charge.success",
              "data": {
                "id": 6095474343,
                "status": "success",
                "reference": "{{repairReference}}",
                "amount": 79000,
                "paid_at": "2026-04-30T15:09:15Z",
                "customer": {
                  "email": "ouer@example.com"
                },
                "authorization": {
                  "authorization_code": "AUTH_retry"
                },
                "metadata": {
                  "plan_slug": "schink-stories-jaarliks",
                  "tier_code": "all_stories_yearly",
                  "billing_period_months": 12
                },
                "plan": {
                  "amount": 79000,
                  "interval": "annually"
                }
              }
            }
            """);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(originalSubscriptionId, result.SubscriptionId);
        Assert.IsTrue(
            handler.SubscriptionPatchPayloads.Any(payload =>
                payload.Contains("\"provider_transaction_id\":\"6095474343\"", StringComparison.Ordinal) &&
                payload.Contains("\"billing_amount_zar\":790", StringComparison.Ordinal) &&
                payload.Contains("\"billing_period_months\":12", StringComparison.Ordinal) &&
                payload.Contains("\"next_renewal_at\":\"2027-04-30T15:09:15Z\"", StringComparison.Ordinal)),
            "repair charge.success should renew the existing subscription row instead of creating a repair-reference row.");
        Assert.IsTrue(
            handler.SubscriptionEventPayloads.Any(payload =>
                payload.Contains($"\"provider_payment_id\":\"{originalProviderPaymentId}\"", StringComparison.Ordinal) &&
                payload.Contains($"\"subscription_id\":\"{originalSubscriptionId}\"", StringComparison.Ordinal)),
            "The repair charge success event should be recorded against the original subscription.");
    }

    [TestMethod]
    public async Task ProcessExpiredPaymentRecoveriesAsync_FailedFirstRetrySchedulesFollowUpBeforeWarningEmail()
    {
        var subscriptionId = "22222222-2222-2222-2222-222222222222";
        var providerPaymentId = "SUB_pg02qbp0h6cb015";
        var recoveryEmailService = new TrackingSubscriptionPaymentRecoveryEmailService();
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscription_payment_recoveries") &&
                request.RequestUri!.Query.Contains("authorization_retry_status=eq.pending", StringComparison.Ordinal))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "recovery_id": "recovery-one",
                        "subscription_id": "{{subscriptionId}}",
                        "provider": "paystack",
                        "provider_payment_id": "{{providerPaymentId}}",
                        "first_failed_at": "2026-05-06T20:00:14Z",
                        "grace_ends_at": "2026-05-11T20:07:13Z",
                        "authorization_retry_status": "pending",
                        "authorization_retry_due_at": "2026-05-07T20:00:14Z"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_payment_recoveries"))
            {
                return JsonResponse("[]");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "{{subscriptionId}}",
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "{{providerPaymentId}}",
                        "provider_transaction_id": null,
                        "provider_token": "AUTH_retry",
                        "status": "active",
                        "billing_amount_zar": 55.00,
                        "billing_period_months": 1,
                        "billing_amount_source": "paystack_payload"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse("""[{ "email": "ouer@example.com", "first_name": "Ouer", "display_name": "Ouer Een" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/charge_authorization")
            {
                return JsonResponse(
                    """
                    {
                      "status": true,
                      "data": {
                        "status": "failed",
                        "reference": "retry-failed",
                        "gateway_response": "Declined"
                      }
                    }
                    """);
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath is "/rest/v1/subscriptions" or "/rest/v1/subscription_payment_recoveries")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler, recoveryEmailService);

        await service.ProcessExpiredPaymentRecoveriesAsync();

        Assert.AreEqual(1, recoveryEmailService.ScheduledRequests.Count);
        Assert.IsTrue(
            handler.PaymentRecoveryPatchPayloads.Any(IsFollowUpAuthorizationRetryPatch),
            "A failed first retry should schedule one more automatic retry before the warning email goes out.");
        Assert.IsTrue(
            handler.PaymentRecoveryPatchPayloads.Any(payload =>
                payload.Contains("\"warning_email_id\":\"warning-email\"", StringComparison.Ordinal) &&
                payload.Contains("\"suspension_email_id\":\"suspension-email\"", StringComparison.Ordinal)),
            "The recovery email sequence should still be scheduled after the first retry fails.");
    }

    [TestMethod]
    public async Task ProcessExpiredPaymentRecoveriesAsync_SuccessfulFollowUpRetryCancelsScheduledRecoveryEmails()
    {
        var subscriptionId = "22222222-2222-2222-2222-222222222222";
        var providerPaymentId = "SUB_pg02qbp0h6cb015";
        var recoveryEmailService = new TrackingSubscriptionPaymentRecoveryEmailService();
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/subscription_payment_recoveries") &&
                request.RequestUri!.Query.Contains("authorization_retry_status=eq.pending", StringComparison.Ordinal))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "recovery_id": "recovery-one",
                        "subscription_id": "{{subscriptionId}}",
                        "provider": "paystack",
                        "provider_payment_id": "{{providerPaymentId}}",
                        "first_failed_at": "2026-05-06T20:00:14Z",
                        "grace_ends_at": "2026-05-11T20:07:13Z",
                        "authorization_retry_status": "pending",
                        "authorization_retry_due_at": "2026-05-08T20:00:14Z",
                        "emails_scheduled_at": "2026-05-07T20:00:14Z",
                        "immediate_email_id": "immediate-email",
                        "warning_email_id": "warning-email",
                        "suspension_email_id": "suspension-email"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_payment_recoveries"))
            {
                return JsonResponse("[]");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "{{subscriptionId}}",
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "provider_payment_id": "{{providerPaymentId}}",
                        "provider_transaction_id": null,
                        "provider_token": "AUTH_retry",
                        "status": "active",
                        "billing_amount_zar": 55.00,
                        "billing_period_months": 1,
                        "billing_amount_source": "paystack_payload"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse("""[{ "email": "ouer@example.com", "first_name": "Ouer", "display_name": "Ouer Een" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/charge_authorization")
            {
                return JsonResponse(
                    """
                    {
                      "status": true,
                      "data": {
                        "status": "success",
                        "reference": "retry-succeeded",
                        "id": 6122688188,
                        "paid_at": "2026-05-08T20:00:14Z"
                      }
                    }
                    """);
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath is "/rest/v1/subscriptions" or "/rest/v1/subscription_payment_recoveries")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler, recoveryEmailService);

        await service.ProcessExpiredPaymentRecoveriesAsync();

        Assert.IsTrue(
            recoveryEmailService.CancelledSequences.Any(sequence =>
                sequence.WarningEmailId == "warning-email" &&
                sequence.SuspensionEmailId == "suspension-email"),
            "A successful follow-up retry should cancel the scheduled warning and suspension emails.");
        Assert.IsTrue(
            handler.PaymentRecoveryPatchPayloads.Any(payload =>
                payload.Contains("\"authorization_retry_status\":\"succeeded\"", StringComparison.Ordinal)),
            "The follow-up retry should mark the recovery retry as succeeded.");
        Assert.IsTrue(
            handler.PaymentRecoveryPatchPayloads.Any(payload =>
                payload.Contains("\"resolution\":\"recovered\"", StringComparison.Ordinal)),
            "The successful follow-up retry should resolve the recovery.");
    }

    private static SupabaseSubscriptionLedgerService CreateService(
        RecordingHandler handler,
        ISubscriptionPaymentRecoveryEmailService? recoveryEmailService = null)
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
                SecretKey = "secret-key"
            }),
            recoveryEmailService ?? new NoopSubscriptionPaymentRecoveryEmailService(),
            new NoopSubscriptionNotificationEmailService(),
            paystackService,
            payFastService,
            NullLogger<SupabaseSubscriptionLedgerService>.Instance);
    }

    private static bool IsSupabaseGet(HttpRequestMessage request, string path) =>
        request.Method == HttpMethod.Get &&
        request.RequestUri?.Host == "example.supabase.co" &&
        request.RequestUri.AbsolutePath == path;

    private static bool IsOriginalPaystackFailureAmountPatch(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return root.TryGetProperty("billing_amount_zar", out var amount) &&
               amount.TryGetDecimal(out var parsedAmount) &&
               parsedAmount == 55.00m &&
               root.TryGetProperty("billing_period_months", out var period) &&
               period.GetInt32() == 1 &&
               root.TryGetProperty("billing_amount_source", out var source) &&
               string.Equals(source.GetString(), "paystack_payload", StringComparison.Ordinal);
    }

    private static bool IsFollowUpAuthorizationRetryPatch(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return root.TryGetProperty("authorization_retry_status", out var status) &&
               string.Equals(status.GetString(), "pending", StringComparison.Ordinal) &&
               root.TryGetProperty("authorization_retry_reference", out var reference) &&
               string.Equals(reference.GetString(), "retry-failed", StringComparison.Ordinal) &&
               root.TryGetProperty("authorization_retry_error", out var error) &&
               string.Equals(error.GetString(), "Declined", StringComparison.Ordinal) &&
               root.TryGetProperty("authorization_retry_due_at", out var dueAt) &&
               dueAt.ValueKind == JsonValueKind.String;
    }

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
        public List<string> PaymentRecoveryPatchPayloads { get; } = [];
        public List<string> SubscriptionEventPayloads { get; } = [];
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
                     request.RequestUri?.AbsolutePath == "/rest/v1/subscription_payment_recoveries")
            {
                var payload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                if (payload is not null)
                {
                    PaymentRecoveryPatchPayloads.Add(payload);
                }
            }
            else if (request.Method == HttpMethod.Post &&
                     request.RequestUri?.AbsolutePath == "/rest/v1/subscription_events")
            {
                var payload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                if (payload is not null)
                {
                    SubscriptionEventPayloads.Add(payload);
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

    private sealed class TrackingSubscriptionPaymentRecoveryEmailService : ISubscriptionPaymentRecoveryEmailService
    {
        public List<SubscriptionPaymentRecoveryEmailRequest> ScheduledRequests { get; } = [];
        public List<SubscriptionPaymentRecoveryEmailSequence> CancelledSequences { get; } = [];

        public Task<SubscriptionPaymentRecoveryEmailSequence?> ScheduleSequenceAsync(
            SubscriptionPaymentRecoveryEmailRequest request,
            CancellationToken cancellationToken = default)
        {
            ScheduledRequests.Add(request);
            return Task.FromResult<SubscriptionPaymentRecoveryEmailSequence?>(
                new("immediate-email", "warning-email", "suspension-email"));
        }

        public Task<string?> SendImmediateAsync(
            SubscriptionPaymentRecoveryEmailRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("immediate-email");

        public Task CancelSequenceAsync(
            SubscriptionPaymentRecoveryEmailSequence sequence,
            CancellationToken cancellationToken = default)
        {
            CancelledSequences.Add(sequence);
            return Task.CompletedTask;
        }
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
