using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    [DataRow(true, 30)]
    [DataRow(true, 0)]
    [DataRow(false, 0)]
    public async Task PaystackPaymentConfirmation_RestoresAccessSilentlyButConfirmsNewPayments(
        bool isRestore, int paymentAgeDays)
    {
        var fixture = new RestoredPaymentFixture();
        var paidAt = DateTimeOffset.UtcNow.AddDays(-paymentAgeDays).AddMinutes(-1);
        var payload = RestoredPaymentPayload(paidAt);
        var service = CreateService(fixture.Handler, notificationEmailService: fixture.Notifications);

        var result = isRestore
            ? await service.RecordRecoveredPaystackEventAsync(payload)
            : await service.RecordPaystackEventAsync(payload);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(RestoredPaymentFixture.SubscriptionId, result.SubscriptionId);
        Assert.IsTrue(fixture.Processed, "Restores must still finish the durable payment event.");
        Assert.HasCount(1, fixture.Handler.SubscriptionCreatePayloads);
        using var saved = JsonDocument.Parse(fixture.Handler.SubscriptionCreatePayloads.Single());
        Assert.AreEqual("active", saved.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(paidAt, saved.RootElement.GetProperty("subscribed_at").GetDateTimeOffset());
        Assert.AreEqual(paidAt.AddMonths(12), saved.RootElement.GetProperty("next_renewal_at").GetDateTimeOffset());
        Assert.IsEmpty(fixture.Handler.PaystackChargePayloads, "Restoring a verified payment must not charge again.");

        // A delayed callback/webhook after a restore uses the normal entry point.
        // It must share the completed event, including across service instances.
        var repeated = await CreateService(fixture.Handler, notificationEmailService: fixture.Notifications)
            .RecordPaystackEventAsync(payload);
        Assert.IsTrue(repeated.IsSuccess, repeated.ErrorMessage);
        Assert.AreEqual(result.SubscriptionId, repeated.SubscriptionId);
        Assert.HasCount(1, fixture.Handler.SubscriptionCreatePayloads);
        Assert.HasCount(isRestore ? 0 : 1, fixture.Notifications.Confirmations);
        Assert.HasCount(isRestore ? 0 : 1, fixture.Notifications.Alerts);
        if (!isRestore)
        {
            Assert.AreEqual("Paid subscription confirmed", fixture.Notifications.Alerts.Single().Title);
            Assert.AreEqual("ouer@example.com", fixture.Notifications.Confirmations.Single().Email);
        }
    }

    [TestMethod]
    public async Task PaystackPaymentRestore_PersistenceFailureStillReportsOperationalError()
    {
        var fixture = new RestoredPaymentFixture { FailSubscriptionWrite = true };
        var result = await CreateService(fixture.Handler, notificationEmailService: fixture.Notifications)
            .RecordRecoveredPaystackEventAsync(RestoredPaymentPayload(DateTimeOffset.UtcNow.AddDays(-30)));

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(fixture.Processed);
        Assert.IsEmpty(fixture.Notifications.Confirmations);
        Assert.HasCount(1, fixture.Notifications.Alerts);
        Assert.AreEqual("Paystack subscription persist failed", fixture.Notifications.Alerts.Single().Title);
    }

    private static string RestoredPaymentPayload(DateTimeOffset paidAt) => JsonSerializer.Serialize(new
    {
        @event = "charge.success",
        data = new
        {
            id = 6228020470,
            status = "success",
            reference = "schink-stories-jaarliks-existing-payment",
            amount = 79000,
            paid_at = paidAt,
            customer = new { email = "ouer@example.com" },
            metadata = new
            {
                plan_slug = "schink-stories-jaarliks",
                tier_code = "all_stories_yearly",
                billing_period_months = 12
            }
        }
    });

    private sealed class RestoredPaymentFixture
    {
        public const string SubscriptionId = "22222222-2222-2222-2222-222222222222";
        public RecordingHandler Handler { get; }
        public RestoredPaymentNotifications Notifications { get; } = new();
        public bool FailSubscriptionWrite { get; init; }
        public bool Processed { get; private set; }
        private string? _dedupeKey;

        public RestoredPaymentFixture()
        {
            Handler = new RecordingHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/rest/v1/subscription_events")
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                        var key = body.RootElement.GetProperty("event_dedupe_key").GetString();
                        if (_dedupeKey is not null)
                        {
                            Assert.AreEqual(_dedupeKey, key);
                            return JsonResponse("[]");
                        }
                        _dedupeKey = key;
                        return JsonResponse("""[{"event_id":"payment-event","processing_status":"processing"}]""");
                    }
                    if (request.Method == HttpMethod.Get)
                    {
                        Assert.IsNotNull(_dedupeKey);
                        StringAssert.Contains(request.RequestUri.Query, $"event_dedupe_key=eq.{Uri.EscapeDataString(_dedupeKey)}");
                        return JsonResponse(JsonSerializer.Serialize(new[] { new
                        {
                            event_id = "payment-event",
                            subscription_id = SubscriptionId,
                            processing_status = Processed ? "processed" : "processing"
                        } }));
                    }
                    if (request.Method == HttpMethod.Patch)
                    {
                        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                        Processed = body.RootElement.GetProperty("processing_status").GetString() == "processed";
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    }
                }
                if (request.Method == HttpMethod.Get) return JsonResponse("[]");
                if (path == "/rest/v1/subscribers" && request.Method == HttpMethod.Post)
                    return JsonResponse("""[{"subscriber_id":"11111111-1111-1111-1111-111111111111"}]""");
                if (path == "/rest/v1/subscriptions" && request.Method == HttpMethod.Post)
                    return FailSubscriptionWrite
                        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        : JsonResponse($$"""[{"subscription_id":"{{SubscriptionId}}"}]""");
                if (request.Method == HttpMethod.Patch || path == "/rest/v1/payment_webhook_failures")
                    return new HttpResponseMessage(HttpStatusCode.NoContent);

                Assert.Fail($"Unexpected request: {request.Method} {request.RequestUri}");
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
        }
    }

    private sealed class RestoredPaymentNotifications : ISubscriptionNotificationEmailService
    {
        public List<SubscriptionConfirmationEmailRequest> Confirmations { get; } = [];
        public List<AdminOpsAlertEmailRequest> Alerts { get; } = [];

        public Task SendSubscriptionConfirmationAsync(SubscriptionConfirmationEmailRequest request, CancellationToken cancellationToken = default)
        {
            Confirmations.Add(request);
            return Task.CompletedTask;
        }

        public Task SendAdminOpsAlertAsync(AdminOpsAlertEmailRequest request, CancellationToken cancellationToken = default)
        {
            Alerts.Add(request);
            return Task.CompletedTask;
        }

        public Task SendSubscriptionEndedAsync(SubscriptionEndedEmailRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
