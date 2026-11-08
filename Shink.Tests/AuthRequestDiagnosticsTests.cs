using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class AuthRequestDiagnosticsTests
{
    [TestMethod]
    public async Task MalformedLogin_CapturesPreHandler400WithCorrelationAndNoCredentials()
    {
        await using var server = await StartServerAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login?token=private-query")
        {
            Content = new StringContent("{\"password\":\"private-password\",", Encoding.UTF8, "application/json")
        };
        var requestId = Guid.NewGuid().ToString("N");
        request.Headers.Add(AuthRequestDiagnosticsMiddleware.RequestIdHeader, requestId);

        using var response = await server.Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(requestId, response.Headers.GetValues(AuthRequestDiagnosticsMiddleware.RequestIdHeader).Single());
        Assert.AreEqual("invalid_request", response.Headers.GetValues(AuthRequestDiagnosticsMiddleware.FailureReasonHeader).Single());
        var entry = await ReadEntryAsync(server.Queue);
        Assert.AreEqual("Warning", entry.Level);
        Assert.AreEqual("AuthenticationRequestFailed", entry.EventName);
        Assert.Contains(requestId, entry.Message);
        Assert.Contains("Reason=invalid_request", entry.Message);
        Assert.AreEqual("/api/auth/login", entry.RequestPath);
        Assert.DoesNotContain("private-", entry.Message);
        Assert.IsNull(entry.ExceptionText);
    }

    [TestMethod]
    [DataRow("/api/auth/login")]
    [DataRow("/api/mobile/auth/google/complete")]
    [DataRow("/api/mobile/auth/apple/complete")]
    public async Task RejectedLogin_PersistsOnlyAllowlistedReasonAndReplacesInvalidClientId(string path)
    {
        await using var server = await StartServerAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("""{"email":"private@example.com","password":"private-password"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(AuthRequestDiagnosticsMiddleware.RequestIdHeader, "private-header");

        using var response = await server.Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var requestId = response.Headers.GetValues(AuthRequestDiagnosticsMiddleware.RequestIdHeader).Single();
        Assert.IsTrue(Guid.TryParseExact(requestId, "N", out _));
        Assert.AreEqual("invalid_credentials", response.Headers.GetValues(AuthRequestDiagnosticsMiddleware.FailureReasonHeader).Single());
        var entry = await ReadEntryAsync(server.Queue);
        Assert.Contains("Reason=invalid_credentials", entry.Message);
        Assert.Contains(requestId, entry.Message);
        Assert.DoesNotContain("private", entry.Message);
        Assert.IsNull(entry.UserEmail);
    }

    [TestMethod]
    public async Task SuccessfulAndUnrelatedRequests_DoNotProduceAuthenticationWarnings()
    {
        await using var server = await StartServerAsync();
        using var success = await server.Client.PostAsync("/api/auth/login", new StringContent(
            """{"email":"success@example.com","password":"any-password"}""", Encoding.UTF8, "application/json"));
        using var unrelated = await server.Client.PostAsync("/unrelated", new StringContent(""));

        Assert.AreEqual(HttpStatusCode.OK, success.StatusCode);
        Assert.IsTrue(success.Headers.Contains(AuthRequestDiagnosticsMiddleware.RequestIdHeader));
        Assert.IsFalse(success.Headers.Contains(AuthRequestDiagnosticsMiddleware.FailureReasonHeader));
        Assert.IsFalse(unrelated.Headers.Contains(AuthRequestDiagnosticsMiddleware.RequestIdHeader));
        Assert.IsFalse(server.Queue.Reader.TryRead(out _));
    }

    [TestMethod]
    [DataRow(400, """{"error_code":"invalid_credentials","message":"private-password"}""", "invalid_credentials")]
    [DataRow(400, """{"error_code":"private-password"}""", "auth_rejected")]
    [DataRow(400, "private-password", "auth_rejected")]
    [DataRow(400, "[]", "auth_rejected")]
    [DataRow(429, "private-password", "rate_limited")]
    [DataRow(503, "private-password", "auth_unavailable")]
    public void ProviderFailureReason_ReturnsOnlySafeCodes(int status, string body, string expected)
    {
        Assert.AreEqual(expected, AuthRequestDiagnosticsMiddleware.ProviderFailureReason((HttpStatusCode)status, body));
    }

    [TestMethod]
    public async Task LoggingProvider_StillIgnoresUnrelatedWarnings()
    {
        await using var server = await StartServerAsync();
        server.App.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Unrelated").LogWarning("private-warning");
        Assert.IsFalse(server.Queue.Reader.TryRead(out _));
    }

    private static async Task<AppErrorLogEntry> ReadEntryAsync(AppErrorLogQueue queue)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await queue.Reader.ReadAsync(timeout.Token);
    }

    private static async Task<TestServer> StartServerAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<AppErrorLogQueue>();
        builder.Services.AddSingleton<ILoggerProvider, SupabaseErrorLoggingProvider>();
        var app = builder.Build();
        app.UseMiddleware<AuthRequestDiagnosticsMiddleware>();
        IResult SignIn(TestLogin request, HttpContext context)
        {
            if (request.Email == "success@example.com") return Results.Ok(new { message = "Welkom terug!" });
            AuthRequestDiagnosticsMiddleware.SetFailureReason(context, "invalid_credentials");
            return Results.BadRequest(new { message = "Ongeldige e-pos of wagwoord." });
        }
        app.MapPost("/api/auth/login", SignIn);
        app.MapPost("/api/mobile/auth/google/complete", SignIn);
        app.MapPost("/api/mobile/auth/apple/complete", SignIn);
        app.MapPost("/unrelated", () => Results.BadRequest());
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new(app, new HttpClient { BaseAddress = new Uri(address) }, app.Services.GetRequiredService<AppErrorLogQueue>());
    }

    private sealed record TestLogin(string? Email, string? Password);
    private sealed record TestServer(WebApplication App, HttpClient Client, AppErrorLogQueue Queue) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
