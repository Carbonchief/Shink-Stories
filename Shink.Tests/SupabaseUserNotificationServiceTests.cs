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
public class SupabaseUserNotificationServiceTests
{
    [TestMethod]
    public async Task CreatePublishedStoryNotificationsAsync_InsertsOnlyActiveSubscribers()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("/rest/v1/subscribers", StringComparison.Ordinal) == true)
            {
                var query = request.RequestUri.Query;
                if (query.Contains("disabled_at=is.null", StringComparison.OrdinalIgnoreCase) &&
                    query.Contains("subscriptions.status=eq.active", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(
                        """
                        [
                          {
                            "subscriber_id": "11111111-1111-1111-1111-111111111111",
                            "subscriptions": [
                              { "status": "active", "next_renewal_at": null, "cancelled_at": null }
                            ]
                          }
                        ]
                        """);
                }

                return JsonResponse(
                    """
                    [
                      { "subscriber_id": "11111111-1111-1111-1111-111111111111" },
                      { "subscriber_id": "22222222-2222-2222-2222-222222222222" }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/rest/v1/subscriber_notifications", StringComparison.Ordinal) == true)
            {
                return JsonResponse(
                    """
                    [
                      { "notification_id": "33333333-3333-3333-3333-333333333333" }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.supabase.co/")
        };
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new SupabaseUserNotificationService(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                ServiceRoleKey = "service-role-key"
            }),
            memoryCache,
            null!,
            null!,
            null!,
            NullLogger<SupabaseUserNotificationService>.Instance);

        var created = await service.CreatePublishedStoryNotificationsAsync(
            new PublishedStoryNotificationRequest(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "nuwe-storie",
                "Nuwe Storie",
                "subscriber",
                null,
                null,
                null));

        Assert.AreEqual(1, created);
        var insertPayload = JsonSerializer.Deserialize<JsonElement>(handler.InsertPayload!);
        Assert.AreEqual(1, insertPayload.GetArrayLength());
        Assert.AreEqual(
            "11111111-1111-1111-1111-111111111111",
            insertPayload[0].GetProperty("subscriber_id").GetString());
    }

    [TestMethod]
    public async Task CreatePublishedBlogNotificationsAsync_UsesBlogTitleAsBody()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("/rest/v1/subscribers", StringComparison.Ordinal) == true)
            {
                return JsonResponse(
                    """
                    [
                      {
                        "subscriber_id": "11111111-1111-1111-1111-111111111111",
                        "subscriptions": [
                          { "status": "active", "next_renewal_at": null, "cancelled_at": null }
                        ]
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/rest/v1/subscriber_notifications", StringComparison.Ordinal) == true)
            {
                return JsonResponse(
                    """
                    [
                      { "notification_id": "33333333-3333-3333-3333-333333333333" }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.supabase.co/")
        };
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new SupabaseUserNotificationService(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                ServiceRoleKey = "service-role-key"
            }),
            memoryCache,
            null!,
            null!,
            null!,
            NullLogger<SupabaseUserNotificationService>.Instance);

        var created = await service.CreatePublishedBlogNotificationsAsync(
            new PublishedBlogNotificationRequest(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                "blog-plasing",
                "Blog Titel",
                "Hierdie opsomming moet nie in die kennisgewing wees nie.",
                null));

        Assert.AreEqual(1, created);
        var insertPayload = JsonSerializer.Deserialize<JsonElement>(handler.InsertPayload!);
        Assert.AreEqual("Blog Titel", insertPayload[0].GetProperty("body").GetString());
    }

    [TestMethod]
    public async Task CreatePublishedResourceDocumentNotificationsAsync_InsertsOnlyActiveSubscribers()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("/rest/v1/subscribers", StringComparison.Ordinal) == true)
            {
                var query = request.RequestUri.Query;
                if (query.Contains("disabled_at=is.null", StringComparison.OrdinalIgnoreCase) &&
                    query.Contains("subscriptions.status=eq.active", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(
                        """
                        [
                          {
                            "subscriber_id": "11111111-1111-1111-1111-111111111111",
                            "subscriptions": [
                              { "status": "active", "next_renewal_at": null, "cancelled_at": null }
                            ]
                          }
                        ]
                        """);
                }

                return JsonResponse(
                    """
                    [
                      { "subscriber_id": "11111111-1111-1111-1111-111111111111" },
                      { "subscriber_id": "22222222-2222-2222-2222-222222222222" }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/rest/v1/subscriber_notifications", StringComparison.Ordinal) == true)
            {
                return JsonResponse(
                    """
                    [
                      { "notification_id": "33333333-3333-3333-3333-333333333333" }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.supabase.co/")
        };
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new SupabaseUserNotificationService(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                ServiceRoleKey = "service-role-key"
            }),
            memoryCache,
            null!,
            null!,
            null!,
            NullLogger<SupabaseUserNotificationService>.Instance);

        var created = await service.CreatePublishedResourceDocumentNotificationsAsync(
            new PublishedResourceDocumentNotificationRequest(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                "aktiwiteite",
                "Aktiwiteite",
                "Nuwe Aktiwiteit",
                "/media/resources/66666666-6666-6666-6666-666666666666/preview"));

        Assert.AreEqual(1, created);
        var insertPayload = JsonSerializer.Deserialize<JsonElement>(handler.InsertPayload!);
        Assert.AreEqual(1, insertPayload.GetArrayLength());
        Assert.AreEqual(
            "11111111-1111-1111-1111-111111111111",
            insertPayload[0].GetProperty("subscriber_id").GetString());
        Assert.AreEqual("resource_document_published", insertPayload[0].GetProperty("notification_type").GetString());
        Assert.AreEqual("Nuwe hulpbron beskikbaar", insertPayload[0].GetProperty("title").GetString());
        Assert.AreEqual("Nuwe Aktiwiteit is nou beskikbaar.", insertPayload[0].GetProperty("body").GetString());
        Assert.AreEqual("/resources?tipe=aktiwiteite", insertPayload[0].GetProperty("href").GetString());
        Assert.AreEqual(
            "resource-document-published-66666666666666666666666666666666",
            insertPayload[0].GetProperty("source_key").GetString());
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public string? InsertPayload { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath.EndsWith("/rest/v1/subscriber_notifications", StringComparison.Ordinal) == true)
            {
                InsertPayload = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }

            return Task.FromResult(responseFactory(request));
        }
    }
}
