using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    public async Task CardUpdateWebhook_QueuesBeforeAcknowledgement_WithoutCallingUnavailableProvider()
    {
        var fixture = new CardUpdateFixture { ProviderUnavailable = true };
        var result = await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsTrue(result.IsDeferred);
        Assert.IsTrue(result.IsCardVerification);
        Assert.HasCount(1, fixture.Jobs);
        Assert.AreEqual(0, fixture.ProviderRequestCount);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task CardUpdateWebhook_DuplicateAndNewServiceInstance_PreserveOneDurableJob()
    {
        var fixture = new CardUpdateFixture { HasRecovery = false };
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();

        Assert.HasCount(1, fixture.Jobs);
        Assert.AreEqual("completed", fixture.Jobs.Single().Value.Status);
        Assert.AreEqual("card_update_applied", fixture.Jobs.Single().Value.ResultCode);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task CardUpdateWebhook_StorageFailure_IsNotAcknowledgedAndDoesNotCallProvider()
    {
        var fixture = new CardUpdateFixture { QueueStorageUnavailable = true };
        var result = await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsEmpty(fixture.Jobs);
        Assert.AreEqual(0, fixture.ProviderRequestCount);
    }

    [TestMethod]
    public async Task CardUpdateWorker_UnattachedCard_RemainsPendingAndAlertsUntilProviderConfirmsBinding()
    {
        var fixture = new CardUpdateFixture { ProviderCardMatches = false, JobCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20) };
        var alerts = new CardUpdateAlerts();
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler, notificationEmailService: alerts).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("pending", fixture.Jobs.Single().Value.Status);
        Assert.AreEqual("provider_card_update", fixture.Jobs.Single().Value.ResultCode);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads, "Do not replace even the local card until Paystack's subscription binding matches.");
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
        Assert.HasCount(1, alerts.Requests);
        StringAssert.Contains(alerts.Requests[0].Title, "incomplete");

        fixture.ProviderCardMatches = true;
        fixture.MakeJobsDue();
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("completed", fixture.Jobs.Single().Value.Status);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
        Assert.IsFalse(fixture.HasRecovery);
    }

    [TestMethod]
    public async Task CardUpdateWorker_VerificationAndRefund_UseOneIncidentAlertKey()
    {
        var fixture = new CardUpdateFixture {
            ProviderCardMatches = false, VerificationReversed = true,
            JobCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
        var alerts = new CardUpdateAlerts();
        var service = CreateService(fixture.Handler, notificationEmailService: alerts);
        await service.RecordPaystackEventAsync(fixture.Payload);
        await service.RecordPaystackEventAsync("""{"event":"refund.processed","data":{"id":902,"reference":"refund-own-reference","transaction_reference":"update-card-transaction","status":"processed","amount":100,"currency":"ZAR"}}""");
        await service.ProcessPendingPaystackCardUpdatesAsync();

        Assert.HasCount(2, alerts.Requests);
        Assert.HasCount(1, alerts.Requests.Select(r => r.AlertKey).Distinct());
        Assert.IsTrue(alerts.Requests.All(r => r.EventReference == "update-card-transaction"));
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task CardUpdateWorker_ProviderOutage_RetainsJobForRetryAfterRestart()
    {
        var fixture = new CardUpdateFixture { ProviderUnavailable = true, HasRecovery = false };
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();
        Assert.AreEqual("pending", fixture.Jobs.Single().Value.Status);

        fixture.ProviderUnavailable = false;
        fixture.MakeJobsDue();
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();
        Assert.AreEqual("completed", fixture.Jobs.Single().Value.Status);
    }

    [TestMethod]
    public async Task CardUpdateWorker_AbandonedLedgerClaim_IsReclaimedUnderTheQueueLease()
    {
        var fixture = new CardUpdateFixture { LedgerClaimAlreadyProcessing = true, HasRecovery = false };
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("completed", fixture.Jobs.Single().Value.Status);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task CardUpdateWorker_LegacyProcessedEvent_StillChecksTheActualProviderCard()
    {
        var fixture = new CardUpdateFixture { LedgerClaimAlreadyProcessed = true, ProviderCardMatches = false };
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("provider_card_update", fixture.Jobs.Single().Value.ResultCode);
        Assert.AreEqual("pending", fixture.Jobs.Single().Value.Status);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads);
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads);
    }

    [TestMethod]
    public async Task CardUpdateWorker_FailureAfterCollection_ReconcilesSameReferenceWithoutCollectingTwice()
    {
        var fixture = new CardUpdateFixture { FailRecoveryWriteOnce = true };
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();
        Assert.AreEqual("pending", fixture.Jobs.Single().Value.Status);
        Assert.AreEqual(1, fixture.CollectedPaymentCount);

        fixture.MakeJobsDue();
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();
        Assert.AreEqual("completed", fixture.Jobs.Single().Value.Status);
        Assert.AreEqual(1, fixture.CollectedPaymentCount);
        Assert.HasCount(1, fixture.ChargeReferences);
        Assert.IsFalse(fixture.HasRecovery);
    }

    [TestMethod]
    public async Task CardUpdateWorker_QueuedPayment_IsVerifiedLaterWithoutAnotherCharge()
    {
        var fixture = new CardUpdateFixture { PaymentStatus = "queued" };
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();
        Assert.AreEqual("payment_confirmation", fixture.Jobs.Single().Value.ResultCode);
        Assert.AreEqual("pending", fixture.Jobs.Single().Value.Status);
        Assert.IsFalse(fixture.Handler.SubscriptionPatchPayloads.Any(p => p.Contains("next_renewal_at", StringComparison.Ordinal)));

        fixture.PaymentStatus = "success";
        fixture.MakeJobsDue();
        await CreateService(fixture.Handler).ProcessPendingPaystackCardUpdatesAsync();
        Assert.AreEqual("completed", fixture.Jobs.Single().Value.Status);
        Assert.HasCount(1, fixture.Handler.PaystackChargePayloads);
        Assert.IsFalse(fixture.HasRecovery);
    }

    [TestMethod]
    public async Task CardUpdateWorker_ExpiredJob_NeedsAttentionWithoutStartingACharge()
    {
        var fixture = new CardUpdateFixture { JobCreatedAt = DateTimeOffset.UtcNow.AddHours(-25) };
        var alerts = new CardUpdateAlerts();
        await CreateService(fixture.Handler).RecordPaystackEventAsync(fixture.Payload);
        await CreateService(fixture.Handler, notificationEmailService: alerts).ProcessPendingPaystackCardUpdatesAsync();

        Assert.AreEqual("needs_attention", fixture.Jobs.Single().Value.Status);
        Assert.AreEqual(0, fixture.ProviderRequestCount);
        Assert.HasCount(1, alerts.Requests);
    }

    private sealed class CardUpdateAlerts : ISubscriptionNotificationEmailService
    {
        public List<AdminOpsAlertEmailRequest> Requests { get; } = [];
        public bool FailQueueOnce { get; set; }
        public int QueueAttempts { get; private set; }
        public Task SendSubscriptionConfirmationAsync(SubscriptionConfirmationEmailRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendSubscriptionEndedAsync(SubscriptionEndedEmailRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendAdminOpsAlertAsync(AdminOpsAlertEmailRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
        public Task<bool> QueueAdminOpsAlertAsync(AdminOpsAlertEmailRequest request, CancellationToken cancellationToken = default)
        {
            QueueAttempts++;
            if (FailQueueOnce)
            {
                FailQueueOnce = false;
                return Task.FromResult(false);
            }
            Requests.Add(request);
            return Task.FromResult(true);
        }
    }

    private sealed partial class CardUpdateFixture
    {
        public Dictionary<string, TestCardUpdateJob> Jobs { get; } = [];
        public bool QueueStorageUnavailable { get; set; }
        public bool LedgerClaimAlreadyProcessing { get; set; }
        public bool LedgerClaimAlreadyProcessed { get; set; }
        public bool FailRecoveryWriteOnce { get; set; }
        public DateTimeOffset JobCreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public HashSet<string> ChargeReferences { get; } = [];
        public int CollectedPaymentCount { get; set; }

        public void MakeJobsDue()
        {
            foreach (var job in Jobs.Values)
            {
                job.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(-1);
                job.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
            }
        }

        private HttpResponseMessage? RespondToQueue(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/rest/v1/paystack_card_update_jobs" && request.Method == HttpMethod.Post)
            {
                if (QueueStorageUnavailable) return new(HttpStatusCode.ServiceUnavailable);
                using var doc = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var key = doc.RootElement.GetProperty("job_key").GetString()!;
                Jobs.TryAdd(key, new TestCardUpdateJob { Key = key, Payload = doc.RootElement.GetProperty("payload").Clone(), CreatedAt = JobCreatedAt });
                return new(HttpStatusCode.Created);
            }
            if (path == "/rest/v1/rpc/claim_paystack_card_update_job")
            {
                var job = Jobs.Values.FirstOrDefault(j => j.Status == "pending" && j.AvailableAt <= DateTimeOffset.UtcNow ||
                    j.Status == "processing" && j.LockedUntil <= DateTimeOffset.UtcNow);
                if (job is null) return JsonResponse("[]");
                job.Status = "processing";
                job.Attempts++;
                job.Lease = Guid.NewGuid();
                job.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
                return JsonResponse(JsonSerializer.Serialize(new[] { new {
                    job_id = job.Id, job_key = job.Key, payload = job.Payload, created_at = job.CreatedAt, attempts = job.Attempts,
                    lease_token = job.Lease, result_code = job.ResultCode, subscription_id = job.SubscriptionId
                } }));
            }
            if (path == "/rest/v1/paystack_card_update_jobs" && request.Method == HttpMethod.Patch)
            {
                var job = Jobs.Values.Single(j => request.RequestUri.Query.Contains(j.Id.ToString(), StringComparison.Ordinal));
                StringAssert.Contains(request.RequestUri.Query, job.Lease.ToString());
                StringAssert.Contains(request.RequestUri.Query, "status=eq.processing");
                using var doc = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                job.Status = doc.RootElement.GetProperty("status").GetString()!;
                job.ResultCode = doc.RootElement.GetProperty("result_code").GetString();
                if (doc.RootElement.TryGetProperty("subscription_id", out var subscription)) job.SubscriptionId = subscription.GetString();
                job.AvailableAt = doc.RootElement.GetProperty("available_at").GetDateTimeOffset();
                return JsonResponse(JsonSerializer.Serialize(new[] { new { job_id = job.Id } }));
            }
            return null;
        }
    }

    private sealed class TestCardUpdateJob
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Key { get; init; } = "";
        public JsonElement Payload { get; init; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
        public Guid Lease { get; set; }
        public DateTimeOffset LockedUntil { get; set; }
        public string Status { get; set; } = "pending";
        public string? ResultCode { get; set; }
        public string? SubscriptionId { get; set; }
        public int Attempts { get; set; }
    }
}
