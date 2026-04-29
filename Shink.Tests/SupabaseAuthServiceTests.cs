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

    private static SupabaseAuthService CreateService(HttpClient httpClient) =>
        new(
            httpClient,
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                AnonKey = "anon-key"
            }),
            new EmptyWordPressMigrationService(),
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
            Task.FromResult(new WordPressSyncResult(0, 0, 0, 0, 0, 0, 0, 0, 0, []));

        public Task<bool> SyncImportedUserProfileAndAccessAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<WordPressImportedUser?> GetImportedUserByEmailAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult<WordPressImportedUser?>(null);

        public Task MarkPasswordMigratedAsync(long wordpressUserId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
