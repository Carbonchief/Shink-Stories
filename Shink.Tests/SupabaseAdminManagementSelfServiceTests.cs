using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class SupabaseAdminManagementSelfServiceTests
{
    [TestMethod]
    public async Task CancelSubscriberPaidSubscriptionAsync_DisablesPaystackAndKeepsAccessUntilNextRenewal()
    {
        var subscriberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subscriptionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var nextRenewalAt = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{ "email": "admin@example.com" }]""");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "email": "ouer@example.com",
                        "first_name": "Ouer",
                        "last_name": "Toets",
                        "display_name": "Ouer Toets",
                        "mobile_number": null,
                        "profile_image_url": null,
                        "created_at": "2026-04-01T10:00:00Z",
                        "updated_at": "2026-04-02T10:00:00Z",
                        "disabled_at": null,
                        "disabled_by_admin_email": null,
                        "disabled_reason": null
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "{{subscriptionId}}",
                        "subscriber_id": "{{subscriberId}}",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "status": "active",
                        "subscribed_at": "2026-04-29T12:00:00Z",
                        "next_renewal_at": "{{nextRenewalAt:O}}",
                        "cancelled_at": null,
                        "provider_payment_id": "SUB_admin",
                        "provider_email_token": null,
                        "provider_transaction_id": null
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/subscription/SUB_admin")
            {
                return JsonResponse(
                    """
                    {
                      "status": true,
                      "data": {
                        "subscription_code": "SUB_admin",
                        "email_token": "email-token-admin"
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
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriber_admin_audit")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.CancelSubscriberPaidSubscriptionAsync(
            "admin@example.com",
            new AdminSubscriberPaidSubscriptionCancelRequest(
                subscriberId,
                subscriptionId,
                "Parent asked to stop renewal."));

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        StringAssert.Contains(handler.PaystackDisablePayload!, "\"code\":\"SUB_admin\"");
        StringAssert.Contains(handler.PaystackDisablePayload!, "\"token\":\"email-token-admin\"");

        var patchPayload = JsonSerializer.Deserialize<JsonElement>(handler.SubscriptionPatchPayload!);
        Assert.AreEqual("active", patchPayload.GetProperty("status").GetString());
        Assert.AreEqual(nextRenewalAt, patchPayload.GetProperty("cancelled_at").GetDateTimeOffset());
        Assert.AreEqual("email-token-admin", patchPayload.GetProperty("provider_email_token").GetString());
        StringAssert.Contains(handler.AuditPayload!, "subscription.cancelled_by_admin");
    }

    [TestMethod]
    public async Task CancelSubscriberPaidSubscriptionAsync_CancelsPayFastTokenAndKeepsAccessUntilNextRenewal()
    {
        var subscriberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subscriptionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var nextRenewalAt = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{ "email": "admin@example.com" }]""");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "email": "ouer@example.com",
                        "first_name": "Ouer",
                        "last_name": "Toets",
                        "display_name": "Ouer Toets",
                        "mobile_number": null,
                        "profile_image_url": null,
                        "created_at": "2026-04-01T10:00:00Z",
                        "updated_at": "2026-04-02T10:00:00Z",
                        "disabled_at": null,
                        "disabled_by_admin_email": null,
                        "disabled_reason": null
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscriptions"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscription_id": "{{subscriptionId}}",
                        "subscriber_id": "{{subscriberId}}",
                        "tier_code": "all_stories_monthly",
                        "provider": "payfast",
                        "source_system": "shink_app",
                        "status": "active",
                        "subscribed_at": "2026-04-29T12:00:00Z",
                        "next_renewal_at": "{{nextRenewalAt:O}}",
                        "cancelled_at": null,
                        "provider_payment_id": "all-stories-monthly-20260429120000",
                        "provider_token": "payfast-token-admin",
                        "provider_email_token": null,
                        "provider_transaction_id": "pf-payment-456"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri?.AbsolutePath == "/subscriptions/payfast-token-admin/cancel")
            {
                return JsonResponse("""{ "status": "success", "data": { "response": "Subscription cancelled" } }""");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriber_admin_audit")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var result = await service.CancelSubscriberPaidSubscriptionAsync(
            "admin@example.com",
            new AdminSubscriberPaidSubscriptionCancelRequest(
                subscriberId,
                subscriptionId,
                "Parent asked to stop renewal."));

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsNotNull(handler.PayFastCancelRequest);

        var patchPayload = JsonSerializer.Deserialize<JsonElement>(handler.SubscriptionPatchPayload!);
        Assert.AreEqual("active", patchPayload.GetProperty("status").GetString());
        Assert.AreEqual(nextRenewalAt, patchPayload.GetProperty("cancelled_at").GetDateTimeOffset());
        Assert.IsFalse(patchPayload.TryGetProperty("provider_email_token", out _));
        StringAssert.Contains(handler.AuditPayload!, "subscription.cancelled_by_admin");
    }

    [TestMethod]
    public async Task CreateResourceDocumentAsync_CreatesPublishedResourceNotification()
    {
        var resourceDocumentId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var resourceTypeId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{ "email": "admin@example.com" }]""");
            }

            if (IsSupabaseGet(request, "/rest/v1/resource_types"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "resource_type_id": "{{resourceTypeId}}",
                        "slug": "aktiwiteite",
                        "name": "Aktiwiteite",
                        "description": null,
                        "sort_order": 10,
                        "is_enabled": true,
                        "updated_at": "2026-04-29T12:00:00Z"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/resource_documents")
            {
                return JsonResponse($$"""[{ "resource_document_id": "{{resourceDocumentId}}" }]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var notificationService = new RecordingUserNotificationService();
        var service = CreateService(handler, notificationService);

        var result = await service.CreateResourceDocumentAsync(
            "admin@example.com",
            new AdminResourceDocumentCreateRequest(
                ResourceTypeId: resourceTypeId,
                Slug: "nuwe-aktiwiteit",
                Title: "Nuwe Aktiwiteit",
                Description: null,
                FileName: "nuwe-aktiwiteit.pdf",
                ContentType: "application/pdf",
                SizeBytes: 12345,
                StorageProvider: "r2",
                StorageBucket: "resources",
                StorageObjectKey: "aktiwiteite/nuwe-aktiwiteit.pdf",
                PreviewImageContentType: "image/png",
                PreviewImageBucket: "resources",
                PreviewImageObjectKey: "aktiwiteite/nuwe-aktiwiteit.png",
                RequiredTierCode: null,
                SortOrder: 10,
                IsEnabled: true));

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsNotNull(notificationService.PublishedResourceDocumentRequest);
        Assert.AreEqual(resourceDocumentId, notificationService.PublishedResourceDocumentRequest.ResourceDocumentId);
        Assert.AreEqual("aktiwiteite", notificationService.PublishedResourceDocumentRequest.ResourceTypeSlug);
        Assert.AreEqual("Aktiwiteite", notificationService.PublishedResourceDocumentRequest.ResourceTypeName);
        Assert.AreEqual("Nuwe Aktiwiteit", notificationService.PublishedResourceDocumentRequest.Title);
        Assert.AreEqual($"/media/resources/{resourceDocumentId:D}/preview", notificationService.PublishedResourceDocumentRequest.PreviewImageUrl);
    }

    private static SupabaseAdminManagementService CreateService(
        RecordingHandler handler,
        IUserNotificationService? userNotificationService = null)
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

        return new SupabaseAdminManagementService(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                SecretKey = "secret-key"
            }),
            new MemoryCache(Options.Create(new MemoryCacheOptions())),
            userNotificationService ?? new NoopUserNotificationService(),
            new NoopWordPressMigrationService(),
            new NoopSupabaseAuthService(),
            new NoopAuthSessionService(),
            new NoopSubscriptionPaymentRecoveryEmailService(),
            paystackService,
            payFastService,
            NullLogger<SupabaseAdminManagementService>.Instance);
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
        public string? AuditPayload { get; private set; }
        public string? PaystackDisablePayload { get; private set; }
        public PayFastCancelRequest? PayFastCancelRequest { get; private set; }
        public string? SubscriptionPatchPayload { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/subscription/disable")
            {
                PaystackDisablePayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }
            else if (request.Method == new HttpMethod("PATCH") &&
                     request.RequestUri?.AbsolutePath == "/rest/v1/subscriptions")
            {
                SubscriptionPatchPayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }
            else if (request.Method == HttpMethod.Post &&
                     request.RequestUri?.AbsolutePath == "/rest/v1/subscriber_admin_audit")
            {
                AuditPayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
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

    private sealed class NoopUserNotificationService : IUserNotificationService
    {
        public Task<UserNotificationPageResult> GetNotificationsAsync(
            string? email,
            int take = 10,
            DateTimeOffset? before = null,
            bool history = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserNotificationPageResult([], 0, false, false));

        public Task<NotificationSyncResult> SyncCharacterUnlockNotificationsAsync(
            string? email,
            string? storySlug = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationSyncResult(0));

        public Task<int> MarkAllNotificationsReadAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<bool> MarkNotificationReadAsync(string? email, Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> ClearNotificationsAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<bool> ClearNotificationAsync(string? email, Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> CreatePublishedBlogNotificationsAsync(PublishedBlogNotificationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> CreatePublishedStoryNotificationsAsync(PublishedStoryNotificationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> CreatePublishedResourceDocumentNotificationsAsync(PublishedResourceDocumentNotificationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class RecordingUserNotificationService : IUserNotificationService
    {
        public PublishedResourceDocumentNotificationRequest? PublishedResourceDocumentRequest { get; private set; }

        public Task<UserNotificationPageResult> GetNotificationsAsync(
            string? email,
            int take = 10,
            DateTimeOffset? before = null,
            bool history = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserNotificationPageResult([], 0, false, false));

        public Task<NotificationSyncResult> SyncCharacterUnlockNotificationsAsync(
            string? email,
            string? storySlug = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationSyncResult(0));

        public Task<int> MarkAllNotificationsReadAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<bool> MarkNotificationReadAsync(string? email, Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> ClearNotificationsAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<bool> ClearNotificationAsync(string? email, Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> CreatePublishedBlogNotificationsAsync(PublishedBlogNotificationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> CreatePublishedStoryNotificationsAsync(PublishedStoryNotificationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> CreatePublishedResourceDocumentNotificationsAsync(PublishedResourceDocumentNotificationRequest request, CancellationToken cancellationToken = default)
        {
            PublishedResourceDocumentRequest = request;
            return Task.FromResult(1);
        }
    }

    private sealed class NoopWordPressMigrationService : IWordPressMigrationService
    {
        public Task<WordPressSyncResult> SyncAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WordPressSyncResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []));

        public Task<bool> SyncImportedUserProfileAndAccessAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<WordPressImportedUser?> GetImportedUserByEmailAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult<WordPressImportedUser?>(null);

        public Task MarkPasswordMigratedAsync(long wordpressUserId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopSupabaseAuthService : ISupabaseAuthService
    {
        public Task<SupabaseSignInResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabaseSignInResult.Failure("Not implemented."));

        public Task<SupabaseSignInResult> SignUpWithPasswordAsync(
            string email,
            string password,
            SignUpProfileData? profileData = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabaseSignInResult.Failure("Not implemented."));

        public Task<SupabasePasswordResetResult> SendPasswordResetEmailAsync(
            string email,
            string redirectTo,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabasePasswordResetResult.Failure("Not implemented."));

        public Task<SupabaseRecoverySessionResult> ExchangeRecoveryTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabaseRecoverySessionResult.Failure("Not implemented."));

        public Task<SupabasePasswordResetResult> UpdatePasswordAsync(
            string accessToken,
            string refreshToken,
            string newPassword,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabasePasswordResetResult.Failure("Not implemented."));

        public Task<SupabaseEmailChangeResult> RequestEmailChangeAsync(
            string currentEmail,
            string currentPassword,
            string newEmail,
            string redirectTo,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabaseEmailChangeResult.Failure("Not implemented."));

        public Task<SupabaseSessionUserResult> ResolveUserSessionAsync(
            string accessToken,
            string refreshToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabaseSessionUserResult.Failure("Not implemented."));

        public Task<SupabaseOAuthStartResult> StartGoogleSignInAsync(
            string redirectTo,
            bool useImplicitFlow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabaseOAuthStartResult.Failure("Not implemented."));

        public Task<SupabaseOAuthExchangeResult> ExchangeGoogleAuthCodeAsync(
            string authCode,
            string codeVerifier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabaseOAuthExchangeResult.Failure("Not implemented."));

        public Task<SupabaseOAuthExchangeResult> ExchangeGoogleImplicitSessionAsync(
            Uri callbackUri,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabaseOAuthExchangeResult.Failure("Not implemented."));
    }

    private sealed class NoopAuthSessionService : IAuthSessionService
    {
        public Task<AuthSessionIssueResult> IssueSessionAsync(
            string? email,
            string? userAgent,
            string? ipAddress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthSessionIssueResult(false, Guid.Empty, 0, 0));

        public Task<AuthSessionValidationState> ValidateSessionAsync(
            string? email,
            Guid sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthSessionValidationState.Unknown);

        public Task RevokeSessionAsync(string? email, Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RevokeAllSessionsAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

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
}
