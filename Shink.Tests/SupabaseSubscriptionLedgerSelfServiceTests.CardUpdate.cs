using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CardVerification_WithoutUnpaidRecovery_DoesNotRenewEvenWhenAuthorizationMatches(bool matchesStoredCard)
    {
        var fixture = new CardUpdateFixture { HasRecovery = false, StoredCardMatches = matchesStoredCard };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsTrue(result.IsCardVerification);
        Assert.AreEqual(CardUpdateFixture.SubscriptionId, result.SubscriptionId);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsEmpty(fixture.Handler.SubscriptionCreatePayloads);
        Assert.IsTrue(fixture.Handler.SubscriptionPatchPayloads.Any(p => p.Contains("AUTH_updated_card", StringComparison.Ordinal)));
        Assert.IsFalse(fixture.Handler.SubscriptionPatchPayloads.Any(p => p.Contains("next_renewal_at", StringComparison.Ordinal)), "R1 must not grant another month.");
        Assert.IsTrue(fixture.Handler.SubscriptionEventPayloads.Any(p => p.Contains("card_verified", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CardVerification_WithUnpaidRecovery_RetriesUpdatedCardAndIgnoresDuplicateAndLaterRefund()
    {
        var fixture = new CardUpdateFixture();
        var service = CreateService(fixture.Handler);
        var first = await service.ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);
        var duplicate = await service.ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);
        fixture.VerificationReversed = true;
        var refund = await service.ProcessQueuedPaystackCardUpdateAsync("""{"event":"refund.processed","data":{"id":902,"transaction_reference":"update-card-transaction","status":"processed","amount":100,"currency":"ZAR"}}""");

        Assert.IsTrue(first.IsSuccess, first.ErrorMessage);
        Assert.IsTrue(duplicate.IsSuccess, duplicate.ErrorMessage);
        Assert.IsTrue(duplicate.IsCardVerification);
        Assert.IsTrue(refund.IsSuccess, refund.ErrorMessage);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
        using var charge = JsonDocument.Parse(fixture.Handler.PaystackChargePayloads.Single());
        Assert.AreEqual(7900, charge.RootElement.GetProperty("amount").GetInt32());
        Assert.AreEqual("AUTH_updated_card", charge.RootElement.GetProperty("authorization_code").GetString(), "The confirmed provider card must be used for the renewal.");
        Assert.AreEqual("card_update", charge.RootElement.GetProperty("metadata").GetProperty("retry_trigger").GetString());
        Assert.IsTrue(DateTimeOffset.Parse(fixture.CompletedRenewal!) == DateTimeOffset.Parse("2099-11-25T19:43:00Z"));
        Assert.IsFalse(fixture.HasRecovery);
    }

    [TestMethod]
    public async Task CardVerification_FullChargeFails_DoesNotRestorePaidAccess()
    {
        var fixture = new CardUpdateFixture { ChargeSucceeds = false };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsTrue(result.IsCardVerification);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsFalse(fixture.Handler.SubscriptionPatchPayloads.Any(p => p.Contains("next_renewal_at", StringComparison.Ordinal)));
        Assert.IsFalse(fixture.Handler.PaymentRecoveryPatchPayloads.Any(p => p.Contains("recovered", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("pending", null)]
    [DataRow("failed", "queued")]
    public async Task CardVerification_WithPendingPayment_StoresCardWithoutStartingAnotherCharge(string recoveryStatus, string? previousPaymentStatus)
    {
        var fixture = new CardUpdateFixture { RecoveryStatus = recoveryStatus, PreviousPaymentStatus = previousPaymentStatus };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsTrue(fixture.Handler.SubscriptionPatchPayloads.Any(p => p.Contains("AUTH_updated_card", StringComparison.Ordinal)));
        Assert.IsFalse(fixture.Handler.SubscriptionPatchPayloads.Any(p => p.Contains("next_renewal_at", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CardVerification_PreviousRetryAlreadyPaid_RecoversWithoutAnotherCharge()
    {
        var fixture = new CardUpdateFixture { PreviousPaymentStatus = "success" };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsFalse(fixture.HasRecovery);
        Assert.IsTrue(DateTimeOffset.Parse(fixture.CompletedRenewal!) == DateTimeOffset.Parse("2099-11-25T19:43:00Z"));
    }

    [TestMethod]
    [DataRow(100, "ZAR", "ouer@example.com")]
    [DataRow(7900, "NGN", "ouer@example.com")]
    [DataRow(7900, "ZAR", "ander@example.com")]
    public async Task CardVerification_VerifiedRecoveryDoesNotMatch_DoesNotGrantAccess(int amount, string currency, string email)
    {
        var fixture = new CardUpdateFixture { VerifiedAmount = amount, VerifiedCurrency = currency, VerifiedPaymentEmail = email };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(fixture.HasRecovery);
        Assert.IsFalse(fixture.Handler.SubscriptionPatchPayloads.Any(p => p.Contains("next_renewal_at", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CardVerification_OlderThanCurrentPaymentFailure_DoesNotCollectNewDebt()
    {
        var fixture = new CardUpdateFixture { VerificationPaidAt = "2026-09-30T10:00:00Z" };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.IsTrue(fixture.HasRecovery);
    }

    [TestMethod]
    [DataRow(false, "ouer@example.com", true)]
    [DataRow(true, "ander@example.com", true)]
    public async Task CardVerification_InvalidAuthorizationOrOwner_NeverChangesCardOrCharges(bool reusable, string verifiedEmail, bool expectFailure)
    {
        var fixture = new CardUpdateFixture { Reusable = reusable, VerifiedEmail = verifiedEmail };
        var result = await CreateService(fixture.Handler).ProcessQueuedPaystackCardUpdateAsync(fixture.Payload);

        Assert.AreEqual(expectFailure, !result.IsSuccess);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    private sealed partial class CardUpdateFixture
    {
        internal const string SubscriptionId = "22222222-2222-2222-2222-222222222222";
        public bool HasRecovery { get; set; } = true;
        public bool SuspendedRecovery { get; set; }
        public string HistoricalAttempts { get; set; } = "[]";
        public string? CompletedRenewal { get; private set; }
        public decimal? BillingAmount { get; set; } = 79m;
        public bool AccessSuspended { get; private set; }
        public bool FailRecoveryResolutionOnce { get; set; }
        public bool FailChargeRequestOnce { get; set; }
        public bool StoredCardMatches { get; set; }
        public bool ChargeSucceeds { get; set; } = true;
        public bool Reusable { get; set; } = true;
        public bool ProviderCardMatches { get; set; } = true;
        public bool ProviderCardReusable { get; set; } = true;
        public string ProviderStatus { get; set; } = "attention";
        public bool SubscriptionUnavailable { get; set; }
        public bool VerificationUnavailable { get; set; }
        public int InvoiceAmount { get; set; } = 7900;
        public bool InvoicePaid { get; set; }
        public bool PreviousPeriod { get; set; }
        public bool Cancelled { get; set; }
        public bool FollowUp { get; set; }
        public string? ScheduledEmailsAt { get; set; } = "2026-10-27T10:00:00Z";
        public string? DueAt { get; set; } = "2026-10-27T10:00:00Z";
        public DateTimeOffset? AttemptedAt { get; set; }
        public bool Paused { get; set; }
        public string? ReservedThrough { get; set; }
        public bool ProviderUnavailable { get; set; }
        public bool AccountClosed { get; set; }
        public bool AccountLookupUnavailable { get; set; }
        public bool ExposeScheduledRetry { get; set; }
        public int ProviderRequestCount { get; private set; }
        public string? PaymentStatus { get; set; }
        public int VerifiedAmount { get; set; } = 7900;
        public string VerifiedCurrency { get; set; } = "ZAR";
        public string VerifiedPaymentEmail { get; set; } = "ouer@example.com";
        public string VerificationPaidAt { get; set; } = "2026-10-31T10:00:00Z";
        private string? _retryReference;
        private string? _verifiedReceipt;
        public bool VerificationReversed { get; set; }
        public string RecoveryStatus { get; set; } = "failed";
        public string? PreviousPaymentStatus { get; set; }
        public string VerifiedEmail { get; set; } = "ouer@example.com";
        public RecordingHandler Handler { get; }
        private readonly Dictionary<string, string> _events = [];
        public string Payload => """{"event":"charge.success","data":{"id":901,"reference":"update-card-transaction","status":"success","amount":100,"currency":"ZAR","customer":{"email":"ouer@example.com"},"authorization":{"authorization_code":"AUTH_updated_card","reusable":true},"metadata":{"transaction_type":"update_card","custom_fields":[{"variable_name":"subscription_code","value":"SUB_cardupdate"}]}}}""";

        public CardUpdateFixture() => Handler = new RecordingHandler(Respond);

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "api.paystack.co")
            {
                ProviderRequestCount++;
                if (ProviderUnavailable) return new(HttpStatusCode.ServiceUnavailable);
            }
            var queueResponse = RespondToQueue(request);
            if (queueResponse is not null) return queueResponse;
            if (IsSupabaseGet(request, "/rest/v1/subscription_payment_pauses")) return JsonResponse(Paused ? """[{"pause_id":"active-pause"}]""" : "[]");
            if (path == "/rest/v1/rpc/claim_payment_recovery_checks")
                return HasRecovery && RecoveryStatus is "pending" or "succeeded" && (CurrentRetryReference is not null && (DueAt is null || RecoveryStatus == "succeeded") || ExposeScheduledRetry && DueAt is not null && DateTimeOffset.Parse(DueAt) <= DateTimeOffset.UtcNow)
                    ? RecoveryResponse() : JsonResponse("[]");
            if (path.StartsWith("/transaction/verify/") && VerificationUnavailable) return new(HttpStatusCode.ServiceUnavailable);
            if (path == "/rest/v1/rpc/complete_customer_payment_recovery")
            {
                using var doc = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var body = doc.RootElement;
                if (FailRecoveryResolutionOnce || FailRecoveryWriteOnce)
                {
                    FailRecoveryResolutionOnce = FailRecoveryWriteOnce = false;
                    return new(HttpStatusCode.ServiceUnavailable);
                }
                if (Cancelled || AccountClosed || (body.GetProperty("p_reference").GetString() != CurrentRetryReference && body.GetProperty("p_reference").GetString() != _verifiedReceipt)) return JsonResponse("false");
                RecoveryStatus = "succeeded";
                CompletedRenewal = body.GetProperty("p_next_payment").GetString();
                HasRecovery = false;
                return JsonResponse("true");
            }
            if (path is "/rest/v1/rpc/reserve_paystack_card_update_attempt" or "/rest/v1/rpc/reserve_customer_payment_recovery")
            {
                using var doc = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var body = doc.RootElement;
                var reference = body.GetProperty("p_attempt_reference").GetString();
                if (!HasRecovery || body.GetProperty("p_expected_status").GetString() != RecoveryStatus ||
                    body.GetProperty("p_expected_reference").GetString() != CurrentRetryReference ||
                    !(RecoveryStatus is "failed" or "skipped" || RecoveryStatus == "pending" &&
                        DueAt is not null && CurrentRetryReference != reference))
                    return JsonResponse("[]");
                DueAt = null;
                FollowUp |= AttemptedAt.HasValue;
                AttemptedAt = DateTimeOffset.UtcNow;
                ReservedThrough = body.GetProperty("p_paid_through").GetString();
                _retryReference = reference;
                RecoveryStatus = "pending";
                return RecoveryResponse();
            }
            if (path == "/rest/v1/rpc/suspend_expired_payment_recovery")
            {
                if (!HasRecovery || RecoveryStatus is "pending" or "succeeded") return JsonResponse("[]");
                AccessSuspended = true;
                HasRecovery = false;
                return RecoveryResponse();
            }
            if (IsSupabaseGet(request, "/rest/v1/subscription_plan_changes")) return JsonResponse("[]");
            if (request.Method == HttpMethod.Post && path == "/rest/v1/subscription_events")
            {
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                if (!request.RequestUri.Query.Contains("on_conflict", StringComparison.Ordinal))
                {
                    if (body.RootElement.GetProperty("event_type").GetString() is "paystack.verified_recovery_invoice" or "paystack.verified_recovery_payment")
                    {
                        var receipt = body.RootElement.GetProperty("payload");
                        if (receipt.GetProperty("previous_reference").GetString() == CurrentRetryReference)
                            _verifiedReceipt = receipt.GetProperty("reference").GetString();
                    }
                    return new(HttpStatusCode.Created);
                }
                var key = body.RootElement.GetProperty("event_dedupe_key").GetString()!;
                if (LedgerClaimAlreadyProcessing && _events.TryAdd(key, "processing")) return JsonResponse("[]");
                if (LedgerClaimAlreadyProcessed && _events.TryAdd(key, "processed")) return JsonResponse("[]");
                return _events.TryAdd(key, "processing")
                    ? JsonResponse("""[{"event_id":"901","processing_status":"processing"}]""")
                    : JsonResponse("[]");
            }
            if (IsSupabaseGet(request, "/rest/v1/subscription_events"))
            {
                if (request.RequestUri.Query.Contains("received_at=gte.", StringComparison.Ordinal)) return JsonResponse(HistoricalAttempts);
                var status = _events.Values.FirstOrDefault() ?? "processing";
                return JsonResponse($$"""[{"event_id":"901","subscription_id":"{{SubscriptionId}}","processing_status":"{{status}}"}]""");
            }
            if (request.Method == HttpMethod.Patch && path == "/rest/v1/subscription_events")
            {
                using var patch = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                foreach (var key in _events.Keys.ToArray()) _events[key] = patch.RootElement.GetProperty("processing_status").GetString()!;
                return new(HttpStatusCode.NoContent);
            }
            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
                return JsonResponse($$"""[{"subscription_id":"{{SubscriptionId}}","subscriber_id":"11111111-1111-1111-1111-111111111111","provider":"paystack","provider_payment_id":"SUB_cardupdate","provider_token":"{{(StoredCardMatches ? "AUTH_updated_card" : "AUTH_old_card")}}","tier_code":"all_stories_monthly","cancelled_at":{{(Cancelled ? "\"2026-11-25T00:00:00Z\"" : "null")}},"status":"{{(SuspendedRecovery && HasRecovery ? "failed" : "active")}}","billing_amount_zar":{{JsonSerializer.Serialize(BillingAmount)}},"billing_period_months":1}]""");
            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                if (AccountLookupUnavailable && request.RequestUri.Query.Contains("disabled_at", StringComparison.Ordinal))
                    return new(HttpStatusCode.ServiceUnavailable);
                return JsonResponse($$"""[{"subscriber_id":"11111111-1111-1111-1111-111111111111","email":"ouer@example.com","disabled_at":{{(AccountClosed ? "\"2026-10-30T10:00:00Z\"" : "null")}}}]""");
            }
            if (IsSupabaseGet(request, "/rest/v1/subscription_payment_recoveries"))
            {
                if (request.RequestUri.Query.Contains("select=immediate_email_id", StringComparison.Ordinal))
                    return JsonResponse("""[{"warning_email_id":"warning-email","suspension_email_id":"suspension-email"}]""");
                if (request.RequestUri.Query.Contains("authorization_retry_status=eq.pending", StringComparison.Ordinal))
                    return ExposeScheduledRetry && HasRecovery && DueAt is not null && RecoveryStatus == "pending"
                        ? RecoveryResponse() : JsonResponse("[]");
                if (request.RequestUri.Query.Contains("grace_ends_at=lte.", StringComparison.Ordinal))
                    return JsonResponse("[]");
                return HasRecovery || request.RequestUri.Query.Contains("order=first_failed_at.desc", StringComparison.Ordinal)
                    ? RecoveryResponse() : JsonResponse("[]");
            }
            if (request.Method == HttpMethod.Patch && path == "/rest/v1/subscription_payment_recoveries")
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var patch = JsonDocument.Parse(body);
                if (request.RequestUri.Query.Contains("authorization_retry_reference=eq.", StringComparison.Ordinal) &&
                    !request.RequestUri.Query.Contains("authorization_retry_reference=eq." + Uri.EscapeDataString(CurrentRetryReference!), StringComparison.Ordinal))
                    return JsonResponse("[]");
                if (body.Contains("recovered", StringComparison.Ordinal))
                {
                    if (FailRecoveryResolutionOnce)
                    {
                        FailRecoveryResolutionOnce = false;
                        return new(HttpStatusCode.ServiceUnavailable);
                    }
                    HasRecovery = false;
                }
                if (patch.RootElement.TryGetProperty("authorization_retry_due_at", out var due)) DueAt = due.GetString();
                if (patch.RootElement.TryGetProperty("authorization_retry_follow_up", out var follow)) FollowUp = follow.GetBoolean();
                if (patch.RootElement.TryGetProperty("resolution", out var resolution) && resolution.GetString() == "cancelled")
                    HasRecovery = false;
                if (patch.RootElement.TryGetProperty("authorization_retry_reference", out var reference)) _retryReference = reference.GetString();
                if (patch.RootElement.TryGetProperty("authorization_retry_status", out var status)) RecoveryStatus = status.GetString()!;
                return request.RequestUri.Query.Contains("select=recovery_id", StringComparison.Ordinal) ? RecoveryResponse() : new(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Patch && path == "/rest/v1/subscriptions")
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                if (FailRecoveryWriteOnce && body.Contains("next_renewal_at", StringComparison.Ordinal))
                {
                    FailRecoveryWriteOnce = false;
                    return new(HttpStatusCode.ServiceUnavailable);
                }
                return new(HttpStatusCode.NoContent);
            }
            if (path.StartsWith("/transaction/verify/update-card-transaction", StringComparison.Ordinal))
                return JsonResponse(JsonSerializer.Serialize(new { status = true, data = new {
                    id = 901, reference = request.RequestUri.Segments.Last(), status = VerificationReversed ? "reversed" : "success", amount = 100, currency = "ZAR", paid_at = VerificationPaidAt,
                    customer = new { email = VerifiedEmail }, authorization = new { authorization_code = "AUTH_updated_card", reusable = Reusable },
                    metadata = new { transaction_type = "update_card", custom_fields = new[] { new { variable_name = "subscription_code", value = "SUB_cardupdate" } } }
                } }));
            if (path == "/transaction/verify/paid-invoice")
                return JsonResponse(JsonSerializer.Serialize(new { status = true, data = new { reference = "paid-invoice",
                    status = "success", amount = VerifiedAmount, currency = VerifiedCurrency, customer = new { email = VerifiedPaymentEmail },
                    paid_at = PreviousPeriod ? "2025-01-01T10:00:00Z" : "2026-10-31T10:00:00Z" } }));
            if (path == "/transaction/verify/previous-retry")
                return JsonResponse(JsonSerializer.Serialize(new { status = true, data = new {
                    id = 904, reference = "previous-retry", status = PreviousPaymentStatus, paid_at = "2026-10-31T10:00:00Z", amount = 7900, currency = "ZAR", customer = new { email = "ouer@example.com" }
                } }));
            if (path == "/subscription/SUB_cardupdate" && SubscriptionUnavailable) return new(HttpStatusCode.ServiceUnavailable);
            if (path == "/subscription/SUB_cardupdate")
                return JsonResponse(JsonSerializer.Serialize(new { status = true, data = new {
                    subscription_code = "SUB_cardupdate", status = ProviderStatus, next_payment_date = "2099-11-25T19:43:00Z",
                    customer = new { email = VerifiedEmail }, plan = new { currency = "ZAR" },
                    authorization = new { authorization_code = ProviderCardMatches ? "AUTH_updated_card" : "AUTH_old_card", reusable = ProviderCardReusable },
                    most_recent_invoice = new { status = InvoicePaid ? "success" : "failed", paid = InvoicePaid, amount = InvoiceAmount,
                        transaction = new { reference = "paid-invoice" }, period_start = PreviousPeriod ? "2025-01-01T00:00:00Z" : "2026-10-25T08:00:00Z",
                        period_end = PreviousPeriod ? "2025-02-01T00:00:00Z" : "2026-11-25T07:59:59Z" }
                } }));
            if (path == "/transaction/charge_authorization" ||
                path.StartsWith("/transaction/verify/card-update-retry-", StringComparison.Ordinal) ||
                path.StartsWith("/transaction/verify/customer-retry-", StringComparison.Ordinal) ||
                path.StartsWith("/transaction/verify/retry-", StringComparison.Ordinal))
            {
                if (path == "/transaction/charge_authorization")
                {
                    if (FailChargeRequestOnce)
                    {
                        FailChargeRequestOnce = false;
                        throw new HttpRequestException("Simulated crash after reserving the recovery, before provider acceptance.");
                    }
                    using var doc = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    var reference = doc.RootElement.GetProperty("reference").GetString()!;
                    if (!ChargeReferences.Add(reference)) return new(HttpStatusCode.BadRequest);
                    if ((PaymentStatus ?? (ChargeSucceeds ? "success" : "failed")) == "success") CollectedPaymentCount++;
                }
                else if (!ChargeReferences.Contains(request.RequestUri.Segments.Last()))
                    return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"status":false,"message":"Transaction reference not found"}""") };
                return JsonResponse(JsonSerializer.Serialize(new { status = true, data = new {
                    id = 903, reference = path.StartsWith("/transaction/verify/", StringComparison.Ordinal) ? request.RequestUri.Segments.Last() : "retry-reference",
                    status = PaymentStatus ?? (ChargeSucceeds ? "success" : "failed"), amount = VerifiedAmount, currency = VerifiedCurrency,
                    customer = new { email = VerifiedPaymentEmail }, paid_at = "2026-10-31T16:36:57Z", gateway_response = ChargeSucceeds ? "Approved" : "Restricted card"
                } }));
            }
            return new(HttpStatusCode.NotFound);
        }

        private string? CurrentRetryReference => _retryReference ?? (PreviousPaymentStatus is null ? null : "previous-retry");

        private HttpResponseMessage RecoveryResponse() => JsonResponse(JsonSerializer.Serialize(new[] { new {
            recovery_id = "33333333-3333-3333-3333-333333333333", subscription_id = SubscriptionId, provider = "paystack", provider_payment_id = "SUB_cardupdate",
            resolution = !HasRecovery ? "recovered" : SuspendedRecovery && _retryReference is null ? "suspended" : null,
            first_failed_at = "2026-10-25T10:00:00Z", grace_ends_at = "2026-10-30T10:00:00Z", emails_scheduled_at = ScheduledEmailsAt,
            authorization_retry_status = RecoveryStatus, authorization_retry_reference = CurrentRetryReference,
            authorization_retry_attempted_at = AttemptedAt, authorization_retry_due_at = DueAt, authorization_retry_paid_through = ReservedThrough, authorization_retry_follow_up = FollowUp
        } }));
    }
}
