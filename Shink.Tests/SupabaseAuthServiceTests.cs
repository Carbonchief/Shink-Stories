using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class SupabaseAuthServiceTests
{
    [TestMethod]
    public async Task SendPasswordResetEmailAsync_MigratesImportedWordPressUserBeforeSendingRecoveryEmail()
    {
        var requestBodies = new List<string>();
        var requestUris = new List<string>();
        var migrationService = new ImportedWordPressMigrationService(new WordPressImportedUser(
            2451,
            "irmadutoitza@outlook.com",
            "$wp$2y$10$hash",
            "wp_bcrypt",
            "Irma",
            "du Toit",
            "Irma du Toit",
            "0821234567",
            DateTimeOffset.Parse("2023-07-24T17:41:38+00:00"),
            DateTimeOffset.Parse("2026-04-15T12:59:59+00:00"),
            null,
            null,
            null));
        var handler = new RecordingHandler(request =>
        {
            requestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            requestBodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);

            if (request.RequestUri?.AbsolutePath == "/auth/v1/admin/users")
            {
                Assert.AreEqual(HttpMethod.Post, request.Method);
                Assert.AreEqual("secret-key", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"email":"irmadutoitza@outlook.com"}""", Encoding.UTF8, "application/json")
                };
            }

            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://example.supabase.co/auth/v1/recover", request.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, secretKey: "secret-key", wordPressMigrationService: migrationService);

        var result = await service.SendPasswordResetEmailAsync(
            "irmadutoitza@outlook.com",
            "https://www.schink.co.za/herstel-wagwoord");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        CollectionAssert.AreEqual(
            new[]
            {
                "https://example.supabase.co/auth/v1/admin/users",
                "https://example.supabase.co/auth/v1/recover"
            },
            requestUris);
        StringAssert.Contains(requestBodies[0], "\"email\":\"irmadutoitza@outlook.com\"");
        StringAssert.Contains(requestBodies[0], "\"email_confirm\":true");
        StringAssert.Contains(requestBodies[0], "\"firstName\":\"Irma\"");
        StringAssert.Contains(requestBodies[0], "\"lastName\":\"du Toit\"");
        StringAssert.Contains(requestBodies[0], "\"displayName\":\"Irma du Toit\"");
        StringAssert.Contains(requestBodies[1], "\"email\":\"irmadutoitza@outlook.com\"");
        StringAssert.Contains(requestBodies[1], "\"redirect_to\":\"https://www.schink.co.za/herstel-wagwoord\"");
        CollectionAssert.AreEqual(new[] { 2451L }, migrationService.MarkedPasswordMigratedUserIds);
    }

    [TestMethod]
    public async Task ExchangeRecoveryTokenHashAsync_PostsVerifyRequestAndReturnsSession()
    {
        string? requestBody = null;
        var handler = new RecordingHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://example.supabase.co/auth/v1/verify", request.RequestUri?.ToString());
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "access_token": "access-token",
                      "refresh_token": "refresh-token",
                      "user": { "email": "ouer@example.com" }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var result = await service.ExchangeRecoveryTokenHashAsync("token-hash");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual("access-token", result.AccessToken);
        Assert.AreEqual("refresh-token", result.RefreshToken);
        Assert.AreEqual("ouer@example.com", result.UserEmail);
        StringAssert.Contains(requestBody!, "\"type\":\"recovery\"");
        StringAssert.Contains(requestBody!, "\"token_hash\":\"token-hash\"");
    }

    [TestMethod]
    public async Task SignInWithPasswordAsync_TranslatesEmailNotConfirmedMessage()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"msg":"Email not confirmed"}""", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var result = await service.SignInWithPasswordAsync("ouer@example.com", "password123");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            "Jou e-posadres is nog nie bevestig nie. Bevestig asseblief jou e-posadres en probeer weer.",
            result.ErrorMessage);
    }

    [TestMethod]
    public async Task SignUpWithPasswordAsync_WhenServiceRoleConfigured_CreatesConfirmedUser()
    {
        string? requestBody = null;
        var handler = new RecordingHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://example.supabase.co/auth/v1/admin/users", request.RequestUri?.ToString());
            Assert.AreEqual("secret-key", request.Headers.Authorization?.Parameter);
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "email": "ouer@example.com"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, secretKey: "secret-key");

        var result = await service.SignUpWithPasswordAsync(
            "ouer@example.com",
            "password123",
            new SignUpProfileData("Ouer", "Een", "Ouer Een", "0821234567"));

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual("ouer@example.com", result.UserEmail);
        StringAssert.Contains(requestBody!, "\"email_confirm\":true");
        StringAssert.Contains(requestBody!, "\"email\":\"ouer@example.com\"");
        StringAssert.Contains(requestBody!, "\"firstName\":\"Ouer\"");
    }

    [TestMethod]
    public async Task ForceUpdatePasswordByEmailAsync_FindsUserByEmailAndUpdatesPasswordWithServiceRole()
    {
        var requestUris = new List<string>();
        var requestBodies = new List<string>();
        var handler = new RecordingHandler(request =>
        {
            requestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            Assert.AreEqual("secret-key", request.Headers.Authorization?.Parameter);

            if (request.RequestUri?.AbsolutePath == "/auth/v1/admin/users")
            {
                Assert.AreEqual(HttpMethod.Get, request.Method);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "users": [
                            { "id": "11111111-1111-1111-1111-111111111111", "email": "other@example.com" },
                            { "id": "22222222-2222-2222-2222-222222222222", "email": "ouer@example.com" }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            Assert.AreEqual(HttpMethod.Put, request.Method);
            Assert.AreEqual("/auth/v1/admin/users/22222222-2222-2222-2222-222222222222", request.RequestUri?.AbsolutePath);
            requestBodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"email":"ouer@example.com"}""", Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, secretKey: "secret-key");

        var result = await service.ForceUpdatePasswordByEmailAsync("ouer@example.com", "NewPassword123!");

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual("ouer@example.com", result.UserEmail);
        CollectionAssert.AreEqual(
            new[]
            {
                "https://example.supabase.co/auth/v1/admin/users?page=1&per_page=1000",
                "https://example.supabase.co/auth/v1/admin/users/22222222-2222-2222-2222-222222222222"
            },
            requestUris);
        StringAssert.Contains(requestBodies[0], "\"password\":\"NewPassword123!\"");
    }

    private static SupabaseAuthService CreateService(
        HttpClient httpClient,
        string secretKey = "",
        IWordPressMigrationService? wordPressMigrationService = null) =>
        new(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                PublishableKey = "publishable-key",
                SecretKey = secretKey
            }),
            wordPressMigrationService ?? new EmptyWordPressMigrationService(),
            new WordPressPasswordVerifier(),
            NullLogger<SupabaseAuthService>.Instance);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class EmptyWordPressMigrationService : IWordPressMigrationService
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

    private sealed class ImportedWordPressMigrationService(WordPressImportedUser importedUser) : IWordPressMigrationService
    {
        public List<long> MarkedPasswordMigratedUserIds { get; } = [];

        public Task<WordPressSyncResult> SyncAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WordPressSyncResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []));

        public Task<bool> SyncImportedUserProfileAndAccessAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<WordPressImportedUser?> GetImportedUserByEmailAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(email, importedUser.Email, StringComparison.OrdinalIgnoreCase)
                ? importedUser
                : null);

        public Task MarkPasswordMigratedAsync(long wordpressUserId, CancellationToken cancellationToken = default)
        {
            MarkedPasswordMigratedUserIds.Add(wordpressUserId);
            return Task.CompletedTask;
        }
    }
}
