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
        var nextRenewalAt = DateTimeOffset.UtcNow.AddDays(7);
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
    public async Task ResendSubscriberRecoveryEmailAsync_SendsImmediatePaystackRecoveryEmailWithoutSchedulingSequence()
    {
        var subscriberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subscriptionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var recoveryId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var recoveryEmailService = new TrackingSubscriptionPaymentRecoveryEmailService();
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
                        "subscribed_at": "2026-05-01T10:00:00Z",
                        "next_renewal_at": "2026-06-01T10:00:00Z",
                        "cancelled_at": null,
                        "provider_payment_id": "SUB_recovery",
                        "provider_email_token": null,
                        "provider_transaction_id": null
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
                        "recovery_id": "{{recoveryId}}",
                        "subscription_id": "{{subscriptionId}}",
                        "provider": "paystack",
                        "provider_payment_id": "SUB_recovery",
                        "first_failed_at": "2026-05-20T10:00:00Z",
                        "grace_ends_at": "2026-05-24T10:00:00Z",
                        "created_at": "2026-05-20T10:00:00Z",
                        "resolved_at": null,
                        "resolution": null
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/subscription/SUB_recovery/manage/link")
            {
                return JsonResponse(
                    """
                    {
                      "status": true,
                      "data": {
                        "link": "https://paystack.example/manage/SUB_recovery"
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/subscriber_admin_audit")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler, subscriptionPaymentRecoveryEmailService: recoveryEmailService);

        var result = await service.ResendSubscriberRecoveryEmailAsync("admin@example.com", subscriberId);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(0, recoveryEmailService.ScheduledRequests.Count);
        Assert.AreEqual(1, recoveryEmailService.ImmediateRequests.Count);
        Assert.AreEqual("ouer@example.com", recoveryEmailService.ImmediateRequests[0].Email);
        Assert.AreEqual("https://paystack.example/manage/SUB_recovery", recoveryEmailService.ImmediateRequests[0].RecoveryUrl);
        Assert.AreEqual("Werk kaartbesonderhede by", recoveryEmailService.ImmediateRequests[0].RecoveryActionLabel);
        StringAssert.Contains(handler.AuditPayload!, "\"action_key\":\"recovery.email_sent\"");
        StringAssert.Contains(handler.AuditPayload!, recoveryId.ToString("D"));
    }

    [TestMethod]
    public async Task CancelSubscriberPaidSubscriptionAsync_CancelsPayFastTokenAndKeepsAccessUntilNextRenewal()
    {
        var subscriberId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subscriptionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var nextRenewalAt = DateTimeOffset.UtcNow.AddDays(7);
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

    [TestMethod]
    public async Task UpdateResourceDocumentAsync_PatchesMetadataWithoutPublishingNotification()
    {
        var resourceDocumentId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{ "email": "admin@example.com" }]""");
            }

            if (request.Method == new HttpMethod("PATCH") &&
                request.RequestUri?.AbsolutePath == "/rest/v1/resource_documents" &&
                request.RequestUri.Query.Contains($"resource_document_id=eq.{resourceDocumentId:D}", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var notificationService = new RecordingUserNotificationService();
        var service = CreateService(handler, notificationService);

        var result = await service.UpdateResourceDocumentAsync(
            "admin@example.com",
            new AdminResourceDocumentUpdateRequest(
                ResourceDocumentId: resourceDocumentId,
                Slug: "nuwe-naam",
                Title: "Nuwe Naam",
                Description: "Opgedateerde beskrywing",
                RequiredTierCode: null,
                SortOrder: 25,
                IsEnabled: false));

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsNull(notificationService.PublishedResourceDocumentRequest);
        Assert.IsNotNull(handler.ResourceDocumentPatchPayload);

        var patchPayload = JsonSerializer.Deserialize<JsonElement>(handler.ResourceDocumentPatchPayload!);
        Assert.AreEqual("nuwe-naam", patchPayload.GetProperty("slug").GetString());
        Assert.AreEqual("Nuwe Naam", patchPayload.GetProperty("title").GetString());
        Assert.AreEqual("Opgedateerde beskrywing", patchPayload.GetProperty("description").GetString());
        Assert.AreEqual(25, patchPayload.GetProperty("sort_order").GetInt32());
        Assert.IsFalse(patchPayload.GetProperty("is_enabled").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, patchPayload.GetProperty("required_tier_code").ValueKind);
        Assert.IsTrue(patchPayload.TryGetProperty("document_updated_at", out _));
    }

    [TestMethod]
    public async Task CreateStoryAsync_CreatesPublishedStoryNotificationByDefault()
    {
        var storyId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{ "email": "admin@example.com" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/stories")
            {
                return JsonResponse($$"""[{ "story_id": "{{storyId}}" }]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var notificationService = new RecordingUserNotificationService();
        var service = CreateService(handler, notificationService);

        var result = await service.CreateStoryAsync(
            "admin@example.com",
            new AdminStoryCreateRequest(
                Slug: "nuwe-gepubliseerde-storie",
                Title: "Nuwe gepubliseerde storie",
                Summary: "Kort opsomming",
                Description: "Beskrywing",
                YouTubeUrl: null,
                TestQuestions: [],
                CoverImagePath: "/stories/nuwe/cover.webp",
                ThumbnailImagePath: "/stories/nuwe/thumb.webp",
                AudioBucket: "stories",
                AudioObjectKey: "nuwe-gepubliseerde-storie/audio.mp3",
                AudioContentType: "audio/mpeg",
                StoryType: "story",
                AccessLevel: "subscriber",
                Status: "published",
                SortOrder: 10,
                PublishedAt: DateTimeOffset.UtcNow,
                DurationSeconds: 60));

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(storyId, result.EntityId);
        Assert.IsNotNull(notificationService.PublishedStoryRequest);
        Assert.AreEqual(storyId, notificationService.PublishedStoryRequest.StoryId);
        Assert.AreEqual("nuwe-gepubliseerde-storie", notificationService.PublishedStoryRequest.Slug);
        Assert.AreEqual("Nuwe gepubliseerde storie", notificationService.PublishedStoryRequest.Title);
        Assert.AreEqual("subscriber", notificationService.PublishedStoryRequest.AccessLevel);
        Assert.AreEqual("Kort opsomming", notificationService.PublishedStoryRequest.Summary);
        Assert.AreEqual("/stories/nuwe/thumb.webp", notificationService.PublishedStoryRequest.ThumbnailImagePath);
        Assert.AreEqual("/stories/nuwe/cover.webp", notificationService.PublishedStoryRequest.CoverImagePath);
    }

    [TestMethod]
    public async Task CreateStoryAsync_SkipsPublishedStoryNotificationWhenAdminDisablesIt()
    {
        var storyId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{ "email": "admin@example.com" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/stories")
            {
                return JsonResponse($$"""[{ "story_id": "{{storyId}}" }]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var notificationService = new RecordingUserNotificationService();
        var service = CreateService(handler, notificationService);

        var result = await service.CreateStoryAsync(
            "admin@example.com",
            new AdminStoryCreateRequest(
                Slug: "stil-gepubliseerde-storie",
                Title: "Stil gepubliseerde storie",
                Summary: "Kort opsomming",
                Description: "Beskrywing",
                YouTubeUrl: null,
                TestQuestions: [],
                CoverImagePath: "/stories/stil/cover.webp",
                ThumbnailImagePath: "/stories/stil/thumb.webp",
                AudioBucket: "stories",
                AudioObjectKey: "stil-gepubliseerde-storie/audio.mp3",
                AudioContentType: "audio/mpeg",
                StoryType: "story",
                AccessLevel: "subscriber",
                Status: "published",
                SortOrder: 10,
                PublishedAt: DateTimeOffset.UtcNow,
                DurationSeconds: 60,
                SummaryDetails: null,
                SendPublishedNotification: false));

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(storyId, result.EntityId);
        Assert.IsNull(notificationService.PublishedStoryRequest);
    }

    [TestMethod]
    public async Task GetSubscriberReportsAsync_MatchesStoryCornerRecoveryToAllStoriesSubscription()
    {
        var subscriberId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var subscriptionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{ "email": "admin@example.com" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/rpc/get_wordpress_subscriber_report_snapshot")
            {
                return JsonResponse("""{ "has_wordpress_data": true, "membership_stats": [], "active_members_per_level": [], "sales_and_revenue": [] }""");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscriber_id": "{{subscriberId}}",
                        "email": "ouer@example.com",
                        "first_name": "Ouer",
                        "last_name": "Toets",
                        "display_name": "Ouer Toets",
                        "mobile_number": null,
                        "profile_image_url": null,
                        "created_at": "2026-06-14T07:55:37Z",
                        "updated_at": "2026-06-14T07:58:30Z",
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
                        "subscribed_at": "2026-06-14T07:58:18Z",
                        "next_renewal_at": "2026-07-14T07:58:18Z",
                        "cancelled_at": null,
                        "billing_amount_zar": 79.00,
                        "billing_period_months": 1,
                        "provider_payment_id": "SUB_test",
                        "provider_transaction_id": "1200677"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_events") ||
                IsSupabaseGet(request, "/rest/v1/store_orders") ||
                IsSupabaseGet(request, "/rest/v1/subscription_payment_recoveries") ||
                IsSupabaseGet(request, "/rest/v1/auth_sessions") ||
                IsSupabaseGet(request, "/rest/v1/story_views") ||
                IsSupabaseGet(request, "/rest/v1/story_listen_events"))
            {
                return JsonResponse("[]");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_tiers"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "tier_code": "story_corner_monthly",
                        "display_name": "Story Corner Monthly",
                        "price_zar": 55.00,
                        "is_active": true
                      },
                      {
                        "tier_code": "all_stories_monthly",
                        "display_name": "All Stories Monthly",
                        "price_zar": 79.00,
                        "is_active": true
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/abandoned_cart_recoveries"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "recovery_id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
                        "source_type": "subscription",
                        "source_key": "story_corner_monthly",
                        "checkout_reference": "storie-hoekie-maandeliks-test",
                        "provider": "paystack",
                        "customer_email": "ouer@example.com",
                        "customer_name": "Ouer Toets",
                        "item_name": "Storie Hoekie Maandeliks",
                        "cart_total_zar": 55.00,
                        "created_at": "2026-06-14T07:55:44Z",
                        "resolved_at": "2026-06-14T07:58:27Z",
                        "resolution": "paid"
                      }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var snapshot = await service.GetSubscriberReportsAsync("admin@example.com");
        var recoveredRevenue = snapshot.RecoveredRevenueDetails.Single();

        Assert.AreEqual("story_corner_monthly", recoveredRevenue.TierCode);
        Assert.AreEqual("active", recoveredRevenue.SubscriptionStatus);
        Assert.AreEqual("subscriptions.next_renewal_at", recoveredRevenue.NextPaymentSource);
        Assert.AreEqual(new DateTimeOffset(2026, 7, 14, 7, 58, 18, TimeSpan.Zero), recoveredRevenue.NextPaymentAt);
    }

    [TestMethod]
    public async Task GetSubscriberReportsAsync_IncludesCancellationSurveyOverviewReasonsAndResponses()
    {
        var subscriberId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var secondSubscriberId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var handler = new RecordingHandler(request =>
        {
            if (IsSupabaseGet(request, "/rest/v1/admin_users"))
            {
                return JsonResponse("""[{ "email": "admin@example.com" }]""");
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/rpc/get_wordpress_subscriber_report_snapshot")
            {
                return JsonResponse("""{ "has_wordpress_data": true, "membership_stats": [], "active_members_per_level": [], "sales_and_revenue": [] }""");
            }

            if (IsSupabaseGet(request, "/rest/v1/subscribers"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "subscriber_id": "{{subscriberId}}",
                        "email": "ouer1@example.com",
                        "first_name": "Ouer",
                        "last_name": "Een",
                        "display_name": "Ouer Een",
                        "mobile_number": null,
                        "profile_image_url": null,
                        "created_at": "2026-06-18T08:00:00Z",
                        "updated_at": "2026-06-18T08:05:00Z",
                        "disabled_at": null,
                        "disabled_by_admin_email": null,
                        "disabled_reason": null
                      },
                      {
                        "subscriber_id": "{{secondSubscriberId}}",
                        "email": "ouer2@example.com",
                        "first_name": "Ouer",
                        "last_name": "Twee",
                        "display_name": "Ouer Twee",
                        "mobile_number": null,
                        "profile_image_url": null,
                        "created_at": "2026-06-18T08:10:00Z",
                        "updated_at": "2026-06-18T08:12:00Z",
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
                        "subscription_id": "11111111-1111-1111-1111-111111111111",
                        "subscriber_id": "{{subscriberId}}",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "source_system": "shink_app",
                        "status": "active",
                        "subscribed_at": "2026-06-10T08:00:00Z",
                        "next_renewal_at": "2026-07-10T08:00:00Z",
                        "cancelled_at": "2026-07-10T08:00:00Z",
                        "billing_amount_zar": 79.00,
                        "billing_period_months": 1,
                        "provider_payment_id": "SUB_one",
                        "provider_transaction_id": "TRX_one"
                      },
                      {
                        "subscription_id": "22222222-2222-2222-2222-222222222222",
                        "subscriber_id": "{{secondSubscriberId}}",
                        "tier_code": "story_corner_monthly",
                        "provider": "payfast",
                        "source_system": "shink_app",
                        "status": "active",
                        "subscribed_at": "2026-06-11T08:00:00Z",
                        "next_renewal_at": "2026-07-11T08:00:00Z",
                        "cancelled_at": "2026-07-11T08:00:00Z",
                        "billing_amount_zar": 55.00,
                        "billing_period_months": 1,
                        "provider_payment_id": "PF_two",
                        "provider_transaction_id": "TRX_two"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_tiers"))
            {
                return JsonResponse(
                    """
                    [
                      {
                        "tier_code": "all_stories_monthly",
                        "display_name": "All Stories Monthly",
                        "price_zar": 79.00,
                        "is_active": true
                      },
                      {
                        "tier_code": "story_corner_monthly",
                        "display_name": "Story Corner Monthly",
                        "price_zar": 55.00,
                        "is_active": true
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_cancellation_feedback"))
            {
                return JsonResponse(
                    $$"""
                    [
                      {
                        "feedback_id": "33333333-3333-3333-3333-333333333333",
                        "subscriber_id": "{{subscriberId}}",
                        "subscription_id": "11111111-1111-1111-1111-111111111111",
                        "tier_code": "all_stories_monthly",
                        "provider": "paystack",
                        "feedback_status": "submitted",
                        "reason_code": "too_expensive",
                        "note": "Ons gebruik dit minder as voorheen.",
                        "cancelled_subscription_count": 1,
                        "created_at": "2026-06-18T09:00:00Z"
                      },
                      {
                        "feedback_id": "44444444-4444-4444-4444-444444444444",
                        "subscriber_id": "{{secondSubscriberId}}",
                        "subscription_id": "22222222-2222-2222-2222-222222222222",
                        "tier_code": "story_corner_monthly",
                        "provider": "payfast",
                        "feedback_status": "submitted",
                        "reason_code": "too_expensive",
                        "note": null,
                        "cancelled_subscription_count": 1,
                        "created_at": "2026-06-18T08:45:00Z"
                      },
                      {
                        "feedback_id": "55555555-5555-5555-5555-555555555555",
                        "subscriber_id": "{{secondSubscriberId}}",
                        "subscription_id": "22222222-2222-2222-2222-222222222222",
                        "tier_code": "story_corner_monthly",
                        "provider": "payfast",
                        "feedback_status": "skipped",
                        "reason_code": null,
                        "note": null,
                        "cancelled_subscription_count": 1,
                        "created_at": "2026-06-18T08:30:00Z"
                      }
                    ]
                    """);
            }

            if (IsSupabaseGet(request, "/rest/v1/subscription_events") ||
                IsSupabaseGet(request, "/rest/v1/store_orders") ||
                IsSupabaseGet(request, "/rest/v1/subscription_payment_recoveries") ||
                IsSupabaseGet(request, "/rest/v1/abandoned_cart_recoveries") ||
                IsSupabaseGet(request, "/rest/v1/auth_sessions") ||
                IsSupabaseGet(request, "/rest/v1/story_views") ||
                IsSupabaseGet(request, "/rest/v1/story_listen_events"))
            {
                return JsonResponse("[]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);

        var snapshot = await service.GetSubscriberReportsAsync("admin@example.com");

        Assert.AreEqual(3, snapshot.CancellationSurveyOverview.TotalResponses);
        Assert.AreEqual(2, snapshot.CancellationSurveyOverview.SubmittedResponses);
        Assert.AreEqual(1, snapshot.CancellationSurveyOverview.SkippedResponses);
        Assert.AreEqual(66.67m, snapshot.CancellationSurveyOverview.ResponseRatePercent);
        Assert.AreEqual("too_expensive", snapshot.CancellationSurveyOverview.TopReasonCode);
        Assert.AreEqual(2, snapshot.CancellationSurveyOverview.TopReasonCount);

        var topReason = snapshot.CancellationSurveyReasons.Single();
        Assert.AreEqual("too_expensive", topReason.ReasonCode);
        Assert.AreEqual(2, topReason.ResponseCount);
        Assert.AreEqual(100m, topReason.PercentageOfSubmitted);

        var latestResponse = snapshot.CancellationSurveyResponses.First();
        Assert.AreEqual("ouer1@example.com", latestResponse.Email);
        Assert.AreEqual("Ouer Een", latestResponse.DisplayName);
        Assert.AreEqual("All Stories Monthly", latestResponse.TierName);
        Assert.AreEqual("submitted", latestResponse.FeedbackStatus);
        Assert.AreEqual("too_expensive", latestResponse.ReasonCode);
        Assert.AreEqual("Ons gebruik dit minder as voorheen.", latestResponse.Note);
    }

    private static SupabaseAdminManagementService CreateService(
        RecordingHandler handler,
        IUserNotificationService? userNotificationService = null,
        ISubscriptionPaymentRecoveryEmailService? subscriptionPaymentRecoveryEmailService = null)
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
            subscriptionPaymentRecoveryEmailService ?? new NoopSubscriptionPaymentRecoveryEmailService(),
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
        public string? ResourceDocumentPatchPayload { get; private set; }

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
            else if (request.Method == new HttpMethod("PATCH") &&
                     request.RequestUri?.AbsolutePath == "/rest/v1/resource_documents")
            {
                ResourceDocumentPatchPayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
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
        public PublishedStoryNotificationRequest? PublishedStoryRequest { get; private set; }
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

        public Task<int> CreatePublishedStoryNotificationsAsync(PublishedStoryNotificationRequest request, CancellationToken cancellationToken = default)
        {
            PublishedStoryRequest = request;
            return Task.FromResult(1);
        }

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

        public Task<SupabaseSignInResult> CreateConfirmedUserWithPasswordAsync(
            string email,
            string password,
            SignUpProfileData? profileData = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabaseSignInResult.Success(email));

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

        public Task<SupabasePasswordResetResult> ChangePasswordAsync(
            string currentEmail,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabasePasswordResetResult.Failure("Not implemented."));

        public Task<SupabasePasswordResetResult> ForceUpdatePasswordByEmailAsync(
            string email,
            string newPassword,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupabasePasswordResetResult.Success(email));

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

    private sealed class TrackingSubscriptionPaymentRecoveryEmailService : ISubscriptionPaymentRecoveryEmailService
    {
        public List<SubscriptionPaymentRecoveryEmailRequest> ScheduledRequests { get; } = [];
        public List<SubscriptionPaymentRecoveryEmailRequest> ImmediateRequests { get; } = [];

        public Task<SubscriptionPaymentRecoveryEmailSequence?> ScheduleSequenceAsync(
            SubscriptionPaymentRecoveryEmailRequest request,
            CancellationToken cancellationToken = default)
        {
            ScheduledRequests.Add(request);
            return Task.FromResult<SubscriptionPaymentRecoveryEmailSequence?>(
                new SubscriptionPaymentRecoveryEmailSequence("immediate", "warning", "suspension"));
        }

        public Task<string?> SendImmediateAsync(
            SubscriptionPaymentRecoveryEmailRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            ImmediateRequests.Add(request);
            return Task.FromResult<string?>("email-immediate");
        }

        public Task CancelSequenceAsync(
            SubscriptionPaymentRecoveryEmailSequence sequence,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
