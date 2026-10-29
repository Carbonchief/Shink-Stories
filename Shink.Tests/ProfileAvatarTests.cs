using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;
using Shink.Components.Content;
using Signup = Shink.Components.Pages.Signup;
using Login = Shink.Components.Pages.Login;

namespace Shink.Tests;

[TestClass]
public sealed class ProfileAvatarTests
{
    [TestMethod]
    [DataRow("//elsewhere.example/path")]
    [DataRow("https://elsewhere.example/path")]
    [DataRow("/\\elsewhere.example/path")]
    [DataRow("/%2felsewhere.example/path")]
    [DataRow("/%252felsewhere.example/path")]
    [DataRow("/%5celsewhere.example/path")]
    [DataRow("/luister\r\nLocation: https://elsewhere.example")]
    [DataRow("/api/account/delete")]
    [DataRow("/teken-uit")]
    [DataRow("/profielprentjie?returnUrl=/profielprentjie")]
    public void SafeReturnPath_RejectsExternalAndActionTargets(string target)
    {
        Assert.AreEqual("/luister", ProfileAvatarCatalog.SafeReturnPath(target));
    }

    [TestMethod]
    [DataRow("/luister/storie?playlist=gunstelinge#speel")]
    [DataRow("/betaal/schink-stories-maandeliks?discountCode=HALF&returnUrl=%2Fluister%2Fstorie")]
    public void SelectionPath_PreservesListeningAndCheckoutContinuation(string target)
    {
        var path = ProfileAvatarCatalog.SelectionPath(target);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(path[path.IndexOf('?')..]);
        Assert.AreEqual(target, query["returnUrl"].ToString());
    }

    [TestMethod]
    public void SelectionPath_DropdownIncludesEditModeAndOriginalPage()
    {
        const string originalPage = "/my-stories?tab=gunstelinge&sort=title";
        var path = ProfileAvatarCatalog.SelectionPath(originalPage, isEditing: true);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(path[path.IndexOf('?')..]);
        Assert.AreEqual("edit", query["mode"].ToString());
        Assert.AreEqual(originalPage, query["returnUrl"].ToString());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("panda")]
    public async Task SaveAsync_OnboardingOpensLuisterAndAssignsAnAvatarWhenNoneWasChosen(string avatarId)
    {
        var principal = Listener();
        var auth = new TestAuthentication(principal, new());
        var context = Context(principal, auth, avatarId, "/preview/saved", isEditing: false);
        var store = new AvatarStore();

        var result = (RedirectHttpResult)await ProfileAvatarEndpoints.SaveAsync(context, new TestAntiforgery(), store);

        Assert.AreEqual("/luister", result.Url);
        Assert.IsNotNull(store.Avatar);
        Assert.Contains(store.Avatar, ProfileAvatarCatalog.All);
        if (avatarId == "panda")
        {
            Assert.AreEqual("panda", store.Avatar.Id);
        }
        Assert.AreEqual(store.Avatar.ImageUrl, auth.SignedInPrincipal?.FindFirst("profile_image_url")?.Value);
    }

    [TestMethod]
    public async Task SaveAsync_EditWithoutSelectionDoesNotAssignARandomAvatar()
    {
        var principal = Listener();
        var auth = new TestAuthentication(principal, new());
        var context = Context(principal, auth, "", "/my-stories?tab=gunstelinge");
        var store = new AvatarStore();

        var result = (RedirectHttpResult)await ProfileAvatarEndpoints.SaveAsync(context, new TestAntiforgery(), store);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(result.Url[result.Url.IndexOf('?')..]);

        Assert.IsNull(store.Avatar);
        Assert.AreEqual("selection", query["error"].ToString());
        Assert.AreEqual("edit", query["mode"].ToString());
        Assert.AreEqual("/my-stories?tab=gunstelinge", query["returnUrl"].ToString());
        Assert.IsNull(auth.SignedInPrincipal);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("roomys")]
    public async Task SaveMobileAsync_SavesChosenOrRandomAvatarWithoutReplacingTheSession(string? id)
    {
        var principal = Listener();
        var properties = new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(5) };
        var auth = new TestAuthentication(principal, properties);
        var context = Context(principal, auth, "", "/luister");
        var store = new AvatarStore();

        var result = (Ok<MobileAvatarSelectionResponse>)await ProfileAvatarEndpoints.SaveMobileAsync(context,
            new MobileAvatarSelectionRequest(id, true, "listener@example.com"), store);

        Assert.IsNotNull(store.Avatar);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(store.Avatar.Id, result.Value.AvatarId);
        Assert.AreEqual(store.Avatar.ImageUrl, result.Value.ProfileImageUrl);
        Assert.AreSame(properties, auth.SignedInProperties);
        Assert.AreEqual("existing-session", auth.SignedInPrincipal?.FindFirst("session_id")?.Value);
        Assert.IsTrue(auth.SignedInPrincipal?.IsInRole("Admin"));
        if (id is not null)
            Assert.AreEqual(id, store.Avatar.Id);
    }

    [TestMethod]
    [DataRow(null, false, "listener@example.com", 400)]
    [DataRow("not-an-avatar", true, "listener@example.com", 400)]
    [DataRow("panda", false, "another@example.com", 409)]
    public async Task SaveMobileAsync_RejectsMissingSelectionInvalidIdsAndChangedAccounts(string? id, bool random, string account, int expectedStatus)
    {
        var principal = Listener();
        var auth = new TestAuthentication(principal, new());
        var context = Context(principal, auth, "", "/luister");
        var store = new AvatarStore();

        var result = await ProfileAvatarEndpoints.SaveMobileAsync(context,
            new MobileAvatarSelectionRequest(id, random, account), store);

        Assert.AreEqual(expectedStatus, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.IsNull(store.Avatar);
        Assert.IsNull(auth.SignedInPrincipal);
    }

    [TestMethod]
    [DataRow(false, "/luister/suurlemoentjie")]
    [DataRow(true, "/luister")]
    public void SignupRedirect_VisitsPickerBeforeStoryOrAppliedVoucherAccess(bool codeApplied, string expectedContinuation)
    {
        var signup = new Signup { ReturnUrl = "/luister/suurlemoentjie" };
        var method = typeof(Signup).GetMethod("GetRedirectTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = (string)method.Invoke(signup, [ProfileAvatarCatalog.SelectionPath("/luister"), codeApplied])!;
        Assert.AreEqual(ProfileAvatarCatalog.SelectionPath(expectedContinuation), result);
    }

    [TestMethod]
    public void SignupRedirect_PreservesServersDiscountCheckoutAfterFormReset()
    {
        var signup = new Signup { ReturnUrl = "/luister/suurlemoentjie" };
        typeof(Signup).GetProperty("SelectedPlan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(signup, PaymentPlanCatalog.FindBySlug("schink-stories-maandeliks"));
        const string checkout = "/betaal/schink-stories-maandeliks?provider=paystack&discountCode=HALF";
        var method = typeof(Signup).GetMethod("GetRedirectTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = (string)method.Invoke(signup, [ProfileAvatarCatalog.SelectionPath(checkout), false])!;
        Assert.AreEqual(ProfileAvatarCatalog.SelectionPath(checkout), result);
    }

    [TestMethod]
    public void FirstSignInRedirect_DoesNotSkipPickerWhenAStoryReturnUrlExists()
    {
        var login = new Login { ReturnUrl = "/luister/suurlemoentjie" };
        var method = typeof(Login).GetMethod("GetRedirectTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = (string)method.Invoke(login, [ProfileAvatarCatalog.SelectionPath("/luister")])!;
        Assert.AreEqual(ProfileAvatarCatalog.SelectionPath("/luister/suurlemoentjie"), result);
    }

    [TestMethod]
    public async Task SaveAsync_UsesSignedInAccountAndRefreshesExistingSession()
    {
        var principal = Listener();
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(12),
            AllowRefresh = true
        };
        var auth = new TestAuthentication(principal, properties);
        var context = Context(principal, auth, "panda", "/luister/storie?playlist=gunstelinge");
        var store = new AvatarStore();

        var result = (RedirectHttpResult)await ProfileAvatarEndpoints.SaveAsync(context, new TestAntiforgery(), store);

        Assert.AreEqual("listener@example.com", store.Email);
        Assert.AreEqual("panda", store.Avatar?.Id);
        Assert.AreEqual("/luister/storie?playlist=gunstelinge", result.Url);
        Assert.IsTrue(result.AcceptLocalUrlOnly);
        Assert.AreSame(properties, auth.SignedInProperties);
        Assert.IsNotNull(auth.SignedInPrincipal);
        Assert.AreEqual("existing-session", auth.SignedInPrincipal.FindFirst("session_id")?.Value);
        Assert.IsTrue(auth.SignedInPrincipal.IsInRole("Admin"));
        Assert.AreEqual("/branding/profile-avatars/panda.png", auth.SignedInPrincipal.FindFirst("profile_image_url")?.Value);
        Assert.IsNull(auth.SignedInPrincipal.FindFirst("picture"));
        Assert.AreEqual("https://old.example/avatar.png", principal.FindFirst("picture")?.Value);
    }

    [TestMethod]
    public async Task SaveAsync_RejectsMissingAuthenticationAndInvalidAntiforgery()
    {
        var anonymous = new DefaultHttpContext();
        var store = new AvatarStore();
        Assert.IsInstanceOfType<UnauthorizedHttpResult>(
            await ProfileAvatarEndpoints.SaveAsync(anonymous, new TestAntiforgery(), store));

        var principal = Listener();
        var auth = new TestAuthentication(principal, new());
        var context = Context(principal, auth, "panda", "/luister");
        Assert.IsInstanceOfType<BadRequest<string>>(
            await ProfileAvatarEndpoints.SaveAsync(context, new TestAntiforgery { Reject = true }, store));
        Assert.IsNull(store.Email);
        Assert.IsNull(auth.SignedInPrincipal);
    }

    [TestMethod]
    [DataRow("https://untrusted.example/avatar.png", true, "selection")]
    [DataRow("panda", false, "save")]
    public async Task SaveAsync_FailureKeepsCookieAndReturnsToPicker(string avatarId, bool saves, string error)
    {
        var principal = Listener();
        var auth = new TestAuthentication(principal, new());
        var context = Context(principal, auth, avatarId, "//untrusted.example");

        var result = (RedirectHttpResult)await ProfileAvatarEndpoints.SaveAsync(context, new TestAntiforgery(), new AvatarStore { Saves = saves });

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(result.Url[result.Url.IndexOf('?')..]);
        Assert.AreEqual(error, query["error"].ToString());
        Assert.AreEqual("edit", query["mode"].ToString());
        Assert.AreEqual("/luister", query["returnUrl"].ToString());
        Assert.IsNull(auth.SignedInPrincipal);
    }

    [TestMethod]
    public async Task UpdateAsync_OnlyChangesAvatarFieldsForCurrentSubscriber()
    {
        using var handler = new AvatarHandler("[{\"subscriber_id\":\"one\"}]");
        using var client = new HttpClient(handler);

        var saved = await CreateStore(client).UpdateAsync(" Listener@Example.com ", ProfileAvatarCatalog.Find("panda")!);

        Assert.IsTrue(saved);
        Assert.AreEqual(HttpMethod.Patch, handler.Method);
        Assert.AreEqual("?email=eq.listener%40example.com&select=subscriber_id", handler.Uri?.Query);
        using var json = JsonDocument.Parse(handler.Payload!);
        Assert.AreEqual(3, json.RootElement.EnumerateObject().Count());
        Assert.AreEqual("/branding/profile-avatars/panda.png", json.RootElement.GetProperty("profile_image_url").GetString());
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("profile_image_object_key").ValueKind);
        Assert.AreEqual("image/png", json.RootElement.GetProperty("profile_image_content_type").GetString());
    }

    [TestMethod]
    [DataRow("[]", 200)]
    [DataRow("{}", 200)]
    [DataRow("invalid json", 200)]
    [DataRow("{}", 503)]
    public async Task UpdateAsync_DoesNotReportSuccessForMissingRowsOrFailedWrites(string response, int status)
    {
        using var handler = new AvatarHandler(response, (HttpStatusCode)status);
        using var client = new HttpClient(handler);
        Assert.IsFalse(await CreateStore(client).UpdateAsync("listener@example.com", ProfileAvatarCatalog.All[0]));
    }

    [TestMethod]
    public async Task WordPressSignInSync_PreservesUsersSelectedAvatar()
    {
        using var handler = new WordPressHandler();
        using var client = new HttpClient(handler);
        var service = new WordPressMigrationService(client, Options.Create(new WordPressOptions()),
            Options.Create(new SupabaseOptions { Url = "https://project.example", SecretKey = "test-only-key" }),
            null!, null!, NullLogger<WordPressMigrationService>.Instance);

        await service.SyncImportedUserProfileAndAccessAsync("listener@example.com");

        Assert.IsNotNull(handler.ImportPayload);
        using var json = JsonDocument.Parse(handler.ImportPayload);
        var subscriber = json.RootElement.GetProperty("payload")[0];
        Assert.AreEqual("/branding/profile-avatars/panda.png", subscriber.GetProperty("profile_image_url").GetString());
        Assert.AreEqual("image/png", subscriber.GetProperty("profile_image_content_type").GetString());
        Assert.IsFalse(subscriber.TryGetProperty("profile_image_object_key", out _));
    }

    private static SupabaseSubscriberAvatarService CreateStore(HttpClient client) => new(client,
        Options.Create(new SupabaseOptions { Url = "https://project.example", SecretKey = "test-only-key" }),
        NullLogger<SupabaseSubscriberAvatarService>.Instance);

    private static ClaimsPrincipal Listener() => new(new ClaimsIdentity(
    [
        new(ClaimTypes.Email, "listener@example.com"),
        new(ClaimTypes.Role, "Admin"),
        new("session_id", "existing-session"),
        new("picture", "https://old.example/avatar.png")
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    private static DefaultHttpContext Context(ClaimsPrincipal principal, TestAuthentication auth, string avatarId, string returnUrl, bool isEditing = true)
    {
        var context = new DefaultHttpContext
        {
            User = principal,
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider()
        };
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["avatarId"] = avatarId,
            ["returnUrl"] = returnUrl,
            ["mode"] = isEditing ? "edit" : "onboarding",
            ["email"] = "another-account@example.com"
        });
        return context;
    }

    private sealed class AvatarStore : ISubscriberAvatarService
    {
        public bool Saves { get; init; } = true;
        public string? Email { get; private set; }
        public ProfileAvatar? Avatar { get; private set; }
        public Task<bool> UpdateAsync(string email, ProfileAvatar avatar, CancellationToken cancellationToken = default)
        {
            Email = email;
            Avatar = avatar;
            return Task.FromResult(Saves);
        }
    }

    private sealed class AvatarHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Payload { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class WordPressHandler : HttpMessageHandler
    {
        public string? ImportPayload { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = "[]";
            if (path.EndsWith("/get_wordpress_user_for_auth", StringComparison.Ordinal))
            {
                json = """[{"wp_user_id":1,"normalized_email":"listener@example.com","profile_image_url":"https://legacy.example/avatar.jpg","profile_image_object_key":"legacy/avatar.jpg","profile_image_content_type":"image/jpeg"}]""";
            }
            else if (path == "/rest/v1/subscribers")
            {
                Assert.IsTrue(request.RequestUri.Query.Contains("email=eq.listener%40example.com", StringComparison.Ordinal));
                json = """[{"subscriber_id":"one","email":"listener@example.com","profile_image_url":"/branding/profile-avatars/panda.png","profile_image_content_type":"image/png"}]""";
            }
            else if (path.EndsWith("/import_wordpress_subscribers", StringComparison.Ordinal))
            {
                ImportPayload = await request.Content!.ReadAsStringAsync(cancellationToken);
                // Stop before entitlement reconciliation: only the subscriber import is under test.
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class TestAuthentication(ClaimsPrincipal principal, AuthenticationProperties properties) : IAuthenticationService
    {
        public ClaimsPrincipal? SignedInPrincipal { get; private set; }
        public AuthenticationProperties? SignedInProperties { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme)));
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal user, AuthenticationProperties? authProperties)
        {
            SignedInPrincipal = user;
            SignedInProperties = authProperties;
            return Task.CompletedTask;
        }
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? authProperties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? authProperties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? authProperties) => Task.CompletedTask;
    }

    private sealed class TestAntiforgery : IAntiforgery
    {
        public bool Reject { get; init; }
        public Task ValidateRequestAsync(HttpContext context) => Reject
            ? Task.FromException(new AntiforgeryValidationException("test rejection")) : Task.CompletedTask;
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => throw new NotSupportedException();
        public AntiforgeryTokenSet GetTokens(HttpContext context) => throw new NotSupportedException();
        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(!Reject);
        public void SetCookieTokenAndHeader(HttpContext context) => throw new NotSupportedException();
    }
}
