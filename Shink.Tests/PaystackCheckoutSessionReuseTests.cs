using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Components.Content;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public sealed class PaystackCheckoutSessionReuseTests
{
    [TestMethod]
    public async Task InitializeCheckoutForEmailAsync_ReusesActivePendingSessionWithoutCreatingPaystackTransaction()
    {
        var paystackInitializeCalls = 0;
        var supabaseInsertCalls = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/paystack_checkout_sessions")
            {
                var query = request.RequestUri.Query;
                StringAssert.Contains(query, "customer_email=eq.ouer%40example.com");
                StringAssert.Contains(query, "tier_code=eq.all_stories_monthly");
                StringAssert.Contains(query, "status=eq.pending");
                StringAssert.Contains(query, "expires_at=gt.");

                return JsonResponse(
                    """
                    [
                      {
                        "reference": "schink-stories-maandeliks-existing",
                        "authorization_url": "https://checkout.paystack.com/existing-session",
                        "expires_at": "2099-01-01T00:00:00Z"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/initialize")
            {
                paystackInitializeCalls++;
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/paystack_checkout_sessions")
            {
                supabaseInsertCalls++;
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(new HttpClient(handler));

        var result = await service.InitializeCheckoutForEmailAsync(
            PaymentPlanCatalog.FindBySlug("schink-stories-maandeliks")!,
            "Ouer@Example.com",
            CreateHttpContext(),
            returnUrl: "/luister");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("https://checkout.paystack.com/existing-session", result.AuthorizationUrl);
        Assert.AreEqual("schink-stories-maandeliks-existing", result.Reference);
        Assert.AreEqual(0, paystackInitializeCalls);
        Assert.AreEqual(0, supabaseInsertCalls);
    }

    [TestMethod]
    public async Task InitializeCheckoutForEmailAsync_StoresNewPendingSessionWhenNoReusableSessionExists()
    {
        var paystackInitializeCalls = 0;
        var supabaseInsertCalls = 0;
        var insertedReference = string.Empty;
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/paystack_checkout_sessions")
            {
                return JsonResponse("[]");
            }

            if (request.Method.Method == "PATCH" &&
                request.RequestUri?.AbsolutePath == "/rest/v1/paystack_checkout_sessions")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/initialize")
            {
                paystackInitializeCalls++;
                var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result).RootElement;
                insertedReference = payload.GetProperty("reference").GetString() ?? string.Empty;

                return JsonResponse(
                    $$"""
                    {
                      "status": true,
                      "data": {
                        "authorization_url": "https://checkout.paystack.com/new-session",
                        "reference": "{{insertedReference}}"
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/paystack_checkout_sessions")
            {
                supabaseInsertCalls++;
                var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result).RootElement;
                Assert.AreEqual("paystack", payload.GetProperty("provider").GetString());
                Assert.AreEqual("subscription", payload.GetProperty("checkout_kind").GetString());
                Assert.AreEqual("ouer@example.com", payload.GetProperty("customer_email").GetString());
                Assert.AreEqual("all_stories_monthly", payload.GetProperty("tier_code").GetString());
                Assert.AreEqual(insertedReference, payload.GetProperty("reference").GetString());
                Assert.AreEqual("https://checkout.paystack.com/new-session", payload.GetProperty("authorization_url").GetString());
                Assert.AreEqual("pending", payload.GetProperty("status").GetString());
                Assert.IsTrue(payload.TryGetProperty("expires_at", out _));

                return JsonResponse("[]");
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(new HttpClient(handler));

        var result = await service.InitializeCheckoutForEmailAsync(
            PaymentPlanCatalog.FindBySlug("schink-stories-maandeliks")!,
            "Ouer@Example.com",
            CreateHttpContext(),
            returnUrl: "/luister");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("https://checkout.paystack.com/new-session", result.AuthorizationUrl);
        Assert.AreEqual(insertedReference, result.Reference);
        Assert.AreEqual(1, paystackInitializeCalls);
        Assert.AreEqual(1, supabaseInsertCalls);
    }

    [TestMethod]
    public void PaystackCheckoutSessionMigration_CreatesRlsProtectedSessionTable()
    {
        var migration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260430_paystack_checkout_sessions.sql"));

        StringAssert.Contains(migration, "create table if not exists public.paystack_checkout_sessions");
        StringAssert.Contains(migration, "alter table public.paystack_checkout_sessions enable row level security");
        StringAssert.Contains(migration, "paystack_checkout_sessions_status_check");
        StringAssert.Contains(migration, "uq_paystack_checkout_sessions_pending_subscription");
    }

    private static PaystackCheckoutService CreateService(HttpClient httpClient) =>
        new(
            httpClient,
            Options.Create(new PaystackOptions
            {
                SecretKey = "paystack-secret",
                InitializeUrl = "https://api.paystack.co/transaction/initialize",
                VerifyUrl = "https://api.paystack.co/transaction/verify",
                PublicBaseUrl = "https://schink.example.com",
                PlanCodes = new Dictionary<string, string>
                {
                    ["all_stories_monthly"] = "PLN_monthly"
                }
            }),
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co/",
                ServiceRoleKey = "service-role-key"
            }));

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("schink.example.com");
        return context;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string GetRepoPath(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new FileNotFoundException("Could not find repo file.", Path.Combine(parts));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
