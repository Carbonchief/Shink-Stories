using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class SupabaseLandingPageServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid AdminId = Guid.Parse("4d609b71-2f5c-4a61-b59c-6c2567d2ba4f");

    [TestMethod]
    public async Task SaveDraftUsesAuthenticatedPrincipalAndServiceRoleKey()
    {
        var content = new LandingPageContent
        {
            Title = "My draft",
            Blocks = [new LandingPageBlock { Type = "text", Text = "Draft text" }]
        };
        var pageId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler(
            JsonResponse($"[{{\"admin_user_id\":\"{AdminId:D}\"}}]"),
            JsonResponse(AdminRow(pageId, "my-page", content, null, false, false, 1)));
        var service = CreateService(handler, authenticatedEmail: "Admin@Example.com");

        var result = await service.SaveDraftAsync(new LandingPageSaveRequest(null, "My-Page", content, null));

        Assert.IsTrue(result.Success);
        Assert.AreEqual(pageId, result.Page?.PageId);
        Assert.AreEqual("my-page", result.Page?.Slug);
        Assert.AreEqual("My draft", result.Page?.Draft.Title);
        Assert.IsTrue(result.Page?.HasUnpublishedChanges);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreEqual("admin@example.com", Uri.UnescapeDataString(handler.Requests[0].Uri.Query).Split("email=eq.")[1].Split('&')[0]);
        Assert.AreEqual("server-role-secret", handler.Requests[0].ApiKey);
        Assert.AreEqual("Bearer server-role-secret", handler.Requests[0].Authorization);

        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        using var body = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.AreEqual("My draft", body.RootElement.GetProperty("draft_content").GetProperty("title").GetString());
        Assert.AreEqual(JsonValueKind.Null, body.RootElement.GetProperty("published_content").ValueKind);
        Assert.IsFalse(body.RootElement.GetProperty("is_published").GetBoolean());
    }

    [TestMethod]
    public async Task SaveDraftRejectsUnauthenticatedCallerBeforeMakingRequests()
    {
        var handler = new RecordingHttpMessageHandler();
        var service = CreateService(handler, authenticatedEmail: null);

        var result = await service.SaveDraftAsync(new LandingPageSaveRequest(null, "draft", new LandingPageContent(), null));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(LandingPageErrorCodes.AdminRequired, result.ErrorCode);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task EnsureAdminRejectsAuthenticatedMemberWithoutChangingIdentitySource()
    {
        var handler = new RecordingHttpMessageHandler(JsonResponse("[]"));
        var service = CreateService(handler, authenticatedEmail: "member@example.com");

        var isAdmin = await service.EnsureAdminAsync();

        Assert.IsFalse(isAdmin);
        Assert.AreEqual(1, handler.Requests.Count);
        StringAssert.Contains(handler.Requests[0].Uri.Query, "email=eq.member%40example.com");
        StringAssert.Contains(handler.Requests[0].Uri.Query, "is_enabled=eq.true");
    }

    [TestMethod]
    public async Task SaveDraftRejectsStaleRevisionWithoutSendingPatch()
    {
        var pageId = Guid.NewGuid();
        var existingContent = new LandingPageContent { Title = "Current draft" };
        var handler = new RecordingHttpMessageHandler(
            JsonResponse($"[{{\"admin_user_id\":\"{AdminId:D}\"}}]"),
            JsonResponse(AdminRow(pageId, "current-page", existingContent, null, false, false, 8)));
        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new LandingPageSaveRequest(
            pageId,
            "current-page",
            new LandingPageContent { Title = "Older editor state" },
            ExpectedRevision: 7));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(LandingPageErrorCodes.RevisionConflict, result.ErrorCode);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[1].Method);
    }

    [TestMethod]
    public async Task DraftUpdateDoesNotSendOrChangePublishedSnapshot()
    {
        var pageId = Guid.NewGuid();
        var draft = new LandingPageContent { Title = "Current draft" };
        var published = new LandingPageContent { Title = "Live snapshot" };
        var updatedDraft = new LandingPageContent { Title = "Changed draft" };
        var handler = new RecordingHttpMessageHandler(
            JsonResponse($"[{{\"admin_user_id\":\"{AdminId:D}\"}}]"),
            JsonResponse(AdminRow(pageId, "locked-page", draft, published, true, true, 4)),
            JsonResponse(AdminRow(pageId, "locked-page", updatedDraft, published, true, true, 5)));
        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new LandingPageSaveRequest(
            pageId,
            "locked-page",
            updatedDraft,
            ExpectedRevision: 4));

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Page?.IsPublished);
        Assert.IsTrue(result.Page?.HasUnpublishedChanges);
        Assert.AreEqual("Changed draft", result.Page?.Draft.Title);
        Assert.AreEqual(3, handler.Requests.Count);
        using var body = JsonDocument.Parse(handler.Requests[2].Body);
        Assert.AreEqual("Changed draft", body.RootElement.GetProperty("draft_content").GetProperty("title").GetString());
        Assert.IsFalse(body.RootElement.TryGetProperty("published_content", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("is_published", out _));
        StringAssert.Contains(handler.Requests[2].Uri.Query, "revision=eq.4");
    }

    [TestMethod]
    public async Task DraftUpdateDetectsRevisionRaceAfterInitialFetch()
    {
        var pageId = Guid.NewGuid();
        var initialDraft = new LandingPageContent { Title = "Initial draft" };
        var concurrentlySavedDraft = new LandingPageContent { Title = "Other editor saved" };
        var handler = new RecordingHttpMessageHandler(
            JsonResponse($"[{{\"admin_user_id\":\"{AdminId:D}\"}}]"),
            JsonResponse(AdminRow(pageId, "race-page", initialDraft, null, false, false, 1)),
            JsonResponse("[]"),
            JsonResponse(AdminRow(pageId, "race-page", concurrentlySavedDraft, null, false, false, 2)));
        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new LandingPageSaveRequest(
            pageId,
            "race-page",
            new LandingPageContent { Title = "This editor's draft" },
            ExpectedRevision: 1));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(LandingPageErrorCodes.RevisionConflict, result.ErrorCode);
        Assert.AreEqual(4, handler.Requests.Count);
        Assert.AreEqual(new HttpMethod("PATCH"), handler.Requests[2].Method);
        StringAssert.Contains(handler.Requests[2].Uri.Query, "revision=eq.1");
        Assert.AreEqual(HttpMethod.Get, handler.Requests[3].Method);
    }

    [TestMethod]
    public async Task PublishCopiesTheRevisionMatchedDraftInOneConditionalUpdate()
    {
        var pageId = Guid.NewGuid();
        var draft = new LandingPageContent
        {
            Title = "Latest draft",
            Blocks = [new LandingPageBlock { Type = "text", Text = "Visible copy" }]
        };
        var priorPublished = new LandingPageContent { Title = "Previous snapshot" };
        var handler = new RecordingHttpMessageHandler(
            JsonResponse($"[{{\"admin_user_id\":\"{AdminId:D}\"}}]"),
            JsonResponse(AdminRow(pageId, "launch-page", draft, priorPublished, true, true, 3)),
            JsonResponse(AdminRow(pageId, "launch-page", draft, draft, true, true, 4)));
        var service = CreateService(handler);

        var result = await service.PublishAsync(pageId, expectedRevision: 3);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(4L, result.Page?.Revision);
        Assert.IsTrue(result.Page?.IsPublished);
        Assert.IsTrue(result.Page?.HasEverPublished);
        Assert.IsFalse(result.Page?.HasUnpublishedChanges);
        Assert.AreEqual(3, handler.Requests.Count);
        var patch = handler.Requests[2];
        Assert.AreEqual(new HttpMethod("PATCH"), patch.Method);
        StringAssert.Contains(patch.Uri.Query, "revision=eq.3");
        using var body = JsonDocument.Parse(patch.Body);
        Assert.AreEqual("Latest draft", body.RootElement.GetProperty("published_content").GetProperty("title").GetString());
        Assert.IsTrue(body.RootElement.GetProperty("is_published").GetBoolean());
        Assert.IsTrue(body.RootElement.GetProperty("has_ever_published").GetBoolean());
        Assert.AreEqual(4L, body.RootElement.GetProperty("revision").GetInt64());
    }

    [TestMethod]
    public async Task UnpublishRetainsPublishedSnapshotAndPublishHistory()
    {
        var pageId = Guid.NewGuid();
        var snapshot = new LandingPageContent
        {
            Title = "Live copy",
            Blocks = [new LandingPageBlock { Type = "text", Text = "Still stored" }]
        };
        var handler = new RecordingHttpMessageHandler(
            JsonResponse($"[{{\"admin_user_id\":\"{AdminId:D}\"}}]"),
            JsonResponse(AdminRow(pageId, "live-page", snapshot, snapshot, true, true, 2)),
            JsonResponse(AdminRow(pageId, "live-page", snapshot, snapshot, false, true, 3)));
        var service = CreateService(handler);

        var result = await service.UnpublishAsync(pageId, expectedRevision: 2);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.Page?.IsPublished);
        Assert.IsTrue(result.Page?.HasEverPublished);
        Assert.IsFalse(result.Page?.HasUnpublishedChanges);
        Assert.AreEqual(3L, result.Page?.Revision);
        var patch = handler.Requests[2];
        Assert.AreEqual(new HttpMethod("PATCH"), patch.Method);
        StringAssert.Contains(patch.Uri.Query, "revision=eq.2");
        using var body = JsonDocument.Parse(patch.Body);
        Assert.IsFalse(body.RootElement.GetProperty("is_published").GetBoolean());
        Assert.AreEqual(3L, body.RootElement.GetProperty("revision").GetInt64());
        Assert.IsFalse(body.RootElement.TryGetProperty("published_content", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("has_ever_published", out _));
    }

    [TestMethod]
    public async Task DeleteArchivesPageWithoutDestroyingContent()
    {
        var pageId = Guid.NewGuid();
        var snapshot = new LandingPageContent
        {
            Title = "Live copy",
            Blocks = [new LandingPageBlock { Type = "text", Text = "Still stored" }]
        };
        var handler = new RecordingHttpMessageHandler(
            JsonResponse($"[{{\"admin_user_id\":\"{AdminId:D}\"}}]"),
            JsonResponse(AdminRow(pageId, "live-page", snapshot, snapshot, true, true, 2)),
            JsonResponse(AdminRow(pageId, "live-page", snapshot, snapshot, false, true, 3)));
        var service = CreateService(handler);

        var result = await service.DeleteAsync(pageId, expectedRevision: 2);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.Page?.IsPublished);
        Assert.IsTrue(result.Page?.HasEverPublished);
        Assert.IsFalse(result.Page?.HasUnpublishedChanges);
        Assert.AreEqual(3L, result.Page?.Revision);
        var patch = handler.Requests[2];
        Assert.AreEqual(new HttpMethod("PATCH"), patch.Method);
        StringAssert.Contains(patch.Uri.Query, "revision=eq.2");
        using var body = JsonDocument.Parse(patch.Body);
        Assert.IsFalse(body.RootElement.GetProperty("is_published").GetBoolean());
        Assert.AreEqual(3L, body.RootElement.GetProperty("revision").GetInt64());
        Assert.IsTrue(body.RootElement.TryGetProperty("deleted_at", out _));
        StringAssert.Contains(patch.Uri.Query, "deleted_at=is.null");
        Assert.IsFalse(body.RootElement.TryGetProperty("draft_content", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("published_content", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("has_ever_published", out _));
    }

    [TestMethod]
    public async Task DeleteRejectsUnauthenticatedCallerWithoutRequests()
    {
        var handler = new RecordingHttpMessageHandler();
        var result = await CreateService(handler, authenticatedEmail: null).DeleteAsync(Guid.NewGuid(), 1);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LandingPageErrorCodes.AdminRequired, result.ErrorCode);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task DeleteRejectsStaleRevisionWithoutSendingPatch()
    {
        var pageId = Guid.NewGuid();
        var content = new LandingPageContent { Title = "Current draft" };
        var handler = new RecordingHttpMessageHandler(
            JsonResponse($"[{{\"admin_user_id\":\"{AdminId:D}\"}}]"),
            JsonResponse(AdminRow(pageId, "current-page", content, null, false, false, 8)));
        var result = await CreateService(handler).DeleteAsync(pageId, 7);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LandingPageErrorCodes.RevisionConflict, result.ErrorCode);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task DeleteReportsConcurrentUpdateInsteadOfRemovingEditorPage()
    {
        var pageId = Guid.NewGuid();
        var content = new LandingPageContent { Title = "Draft" };
        var handler = new RecordingHttpMessageHandler(
            JsonResponse($"[{{\"admin_user_id\":\"{AdminId:D}\"}}]"),
            JsonResponse(AdminRow(pageId, "draft", content, null, false, false, 2)),
            JsonResponse("[]"),
            JsonResponse(AdminRow(pageId, "draft", content, null, false, false, 3)));
        var result = await CreateService(handler).DeleteAsync(pageId, 2);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(LandingPageErrorCodes.RevisionConflict, result.ErrorCode);
    }

    [TestMethod]
    public async Task PublishedCatalogFiltersAtTheDataApiAndDoesNotCacheOrSelectDrafts()
    {
        var pageId = Guid.NewGuid();
        var published = new LandingPageContent
        {
            Title = "Public page",
            Blocks = [new LandingPageBlock { Type = "text", Text = "Public copy" }]
        };
        var row = JsonSerializer.Serialize(new
        {
            page_id = pageId,
            slug = "public-page",
            published_content = published
        }, JsonOptions);
        var handler = new RecordingHttpMessageHandler(JsonResponse($"[{row}]"), JsonResponse($"[{row}]"));
        var service = CreateService(handler, authenticatedEmail: null);

        var first = await service.FindPublishedBySlugAsync("PUBLIC-PAGE");
        var second = await service.FindPublishedBySlugAsync("public-page");

        Assert.AreEqual("Public page", first?.Content.Title);
        Assert.AreEqual("Public page", second?.Content.Title);
        Assert.AreEqual(2, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            StringAssert.Contains(request.Uri.Query, "is_published=eq.true");
            StringAssert.Contains(request.Uri.Query, "deleted_at=is.null");
            StringAssert.Contains(request.Uri.Query, "page_id,slug,published_content");
            Assert.IsFalse(request.Uri.Query.Contains("draft_content", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("server-role-secret", request.ApiKey);
        }
    }

    [TestMethod]
    public async Task PublishedCatalogReportsSchemaFailureInsteadOfReturningNotFound()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent("{\"code\":\"PGRST205\",\"message\":\"Could not find table landing_pages\"}")
            });
        var service = CreateService(handler, authenticatedEmail: null);

        var exception = await Assert.ThrowsExactlyAsync<LandingPageServiceException>(
            () => service.FindPublishedBySlugAsync("page"));

        Assert.AreEqual(LandingPageErrorCodes.SchemaNotReady, exception.ErrorCode);
    }

    private static SupabaseLandingPageService CreateService(
        HttpMessageHandler handler,
        string? authenticatedEmail = "admin@example.com") =>
        new(
            new HttpClient(handler),
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co",
                PublishableKey = "public-key-must-not-be-used",
                SecretKey = "server-role-secret"
            }),
            new TestAuthenticationStateProvider(authenticatedEmail),
            new LandingPageContentValidator(),
            NullLogger<SupabaseLandingPageService>.Instance);

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = JsonContent(body) };

    private static StringContent JsonContent(string body) =>
        new(body, Encoding.UTF8, "application/json");

    private static string AdminRow(
        Guid pageId,
        string slug,
        LandingPageContent draft,
        LandingPageContent? published,
        bool isPublished,
        bool hasEverPublished,
        long revision) =>
        $"[{JsonSerializer.Serialize(new
        {
            page_id = pageId,
            slug,
            draft_content = draft,
            published_content = published,
            is_published = isPublished,
            has_ever_published = hasEverPublished,
            revision,
            updated_at = DateTimeOffset.Parse("2026-09-29T10:00:00Z")
        }, JsonOptions)}]";

    private sealed class TestAuthenticationStateProvider(string? email) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = string.IsNullOrWhiteSpace(email)
                ? new ClaimsIdentity()
                : new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private sealed class RecordingHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var apiKey = request.Headers.TryGetValues("apikey", out var values)
                ? values.SingleOrDefault()
                : null;
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                apiKey,
                request.Headers.Authorization?.ToString(),
                body));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("The test did not provide an HTTP response for every request.");
            }

            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? ApiKey,
        string? Authorization,
        string Body);
}
