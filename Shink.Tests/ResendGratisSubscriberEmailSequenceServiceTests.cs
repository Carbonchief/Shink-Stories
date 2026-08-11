using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class ResendGratisSubscriberEmailSequenceServiceTests
{
    [TestMethod]
    public async Task TryStartAsync_CreatesContactAndFiresSequenceEventForNewOptIn()
    {
        var requests = new List<RecordedRequest>();
        var handler = new RecordingHandler(async request =>
        {
            requests.Add(await RecordAsync(request));
            return request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(HttpStatusCode.OK, "{}");
        });
        var service = CreateService(handler);

        var result = await service.TryStartAsync(
            " Ouer@Example.com ",
            " Ouer ",
            " Van ");

        Assert.AreEqual(GratisSubscriberSequenceStartResult.Started, result);
        Assert.HasCount(3, requests);
        Assert.AreEqual("/contacts/ouer%40example.com", requests[0].PathAndQuery);
        Assert.AreEqual(HttpMethod.Post, requests[1].Method);
        Assert.AreEqual("/contacts", requests[1].PathAndQuery);
        StringAssert.Contains(requests[1].Body, "\"unsubscribed\":false");
        StringAssert.Contains(requests[1].Body, "\"schink_access\":\"gratis\"");
        Assert.AreEqual("/events/send", requests[2].PathAndQuery);
        StringAssert.Contains(requests[2].Body, "\"event\":\"schink.gratis_sequence.started\"");
        StringAssert.Contains(requests[2].Body, "\"email\":\"ouer@example.com\"");
        Assert.IsTrue(requests.All(request => request.HasAuthorization));
        Assert.IsTrue(requests.All(request => request.HasUserAgent));
    }

    [TestMethod]
    public async Task TryStartAsync_DoesNotResubscribeOrTriggerAnUnsubscribedContact()
    {
        var requests = new List<RecordedRequest>();
        var handler = new RecordingHandler(async request =>
        {
            requests.Add(await RecordAsync(request));
            return JsonResponse(HttpStatusCode.OK, "{\"unsubscribed\":true}");
        });
        var service = CreateService(handler);

        var result = await service.TryStartAsync("ouer@example.com", "Ouer", null);

        Assert.AreEqual(GratisSubscriberSequenceStartResult.SkippedUnsubscribed, result);
        Assert.HasCount(1, requests);
        Assert.AreEqual(HttpMethod.Get, requests[0].Method);
    }

    [TestMethod]
    public async Task MarkPaidAsync_ChangesExistingContactAccessBeforeTheNextDailyEmail()
    {
        var requests = new List<RecordedRequest>();
        var handler = new RecordingHandler(async request =>
        {
            requests.Add(await RecordAsync(request));
            return request.Method == HttpMethod.Get
                ? JsonResponse(HttpStatusCode.OK, "{\"unsubscribed\":false}")
                : JsonResponse(HttpStatusCode.OK, "{}");
        });
        var service = CreateService(handler);

        var updated = await service.MarkPaidAsync("ouer@example.com");

        Assert.IsTrue(updated);
        Assert.HasCount(2, requests);
        Assert.AreEqual(HttpMethod.Patch, requests[1].Method);
        Assert.AreEqual("/contacts/ouer%40example.com", requests[1].PathAndQuery);
        StringAssert.Contains(requests[1].Body, "\"schink_access\":\"paid\"");
        Assert.IsFalse(requests[1].Body.Contains("unsubscribed", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MarkPaidAsync_DoesNotCreateAContactWhoNeverJoinedTheSeries()
    {
        var requests = new List<RecordedRequest>();
        var handler = new RecordingHandler(async request =>
        {
            requests.Add(await RecordAsync(request));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = CreateService(handler);

        var updated = await service.MarkPaidAsync("ouer@example.com");

        Assert.IsTrue(updated);
        Assert.HasCount(1, requests);
        Assert.AreEqual(HttpMethod.Get, requests[0].Method);
    }

    private static ResendGratisSubscriberEmailSequenceService CreateService(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new ResendOptions { ApiKey = "re_test" }),
            NullLogger<ResendGratisSubscriberEmailSequenceService>.Instance);

    private static async Task<RecordedRequest> RecordAsync(HttpRequestMessage request) =>
        new(
            request.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty,
            request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(),
            request.Headers.Authorization?.Scheme == "Bearer",
            request.Headers.UserAgent.Count > 0);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed record RecordedRequest(
        HttpMethod Method,
        string PathAndQuery,
        string Body,
        bool HasAuthorization,
        bool HasUserAgent);

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }
}
