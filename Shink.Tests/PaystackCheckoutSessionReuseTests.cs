using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
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
    public void DiscountedPaystackPersistenceRevalidatesCurrentCodeAndPlanBeforeStoringTerms()
    {
        var source = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        var methodStart = source.IndexOf("private async Task<PaystackDiscountedSubscriptionTerms?> ResolvePaystackDiscountedSubscriptionTermsAsync", StringComparison.Ordinal);
        Assert.AreNotEqual(-1, methodStart);
        var methodEnd = source.IndexOf("private static decimal ResolveNextDiscountedSubscriptionBillingAmount", methodStart, StringComparison.Ordinal);
        Assert.AreNotEqual(-1, methodEnd);
        var method = source[methodStart..methodEnd];

        StringAssert.Contains(method, "string email");
        StringAssert.Contains(method, "ResolveDiscountCodeSelectionAsync(");
        StringAssert.Contains(method, "plan.TierCode");
        StringAssert.Contains(method, "email.Trim().ToLowerInvariant()");
        StringAssert.Contains(method, "SubscriptionDiscountKinds.Percentage");
        StringAssert.Contains(method, "resolution.Mapping.TierCode");
        Assert.IsFalse(method.Contains("TryReadNestedDecimal(data, \"metadata\", \"discount_percent\")", StringComparison.Ordinal));
        Assert.IsFalse(method.Contains("TryReadNestedString(data, \"metadata\", \"discount_duration\")", StringComparison.Ordinal));
    }

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
    public async Task TryRecoverPaidCheckoutSessionAsync_VerifiesPendingSessionBeforeStartingRetry()
    {
        var paystackVerifyCalls = 0;
        var paystackInitializeCalls = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/rest/v1/paystack_checkout_sessions")
            {
                var query = request.RequestUri.Query;
                StringAssert.Contains(query, "customer_email=eq.ouer%40example.com");
                StringAssert.Contains(query, "tier_code=eq.all_stories_monthly");
                StringAssert.Contains(query, "status=eq.pending");
                StringAssert.Contains(query, "order=created_at.desc");

                return JsonResponse(
                    """
                    [
                      {
                        "reference": "schink-stories-maandeliks-existing",
                        "authorization_url": "https://checkout.paystack.com/existing-session",
                        "expires_at": "2026-05-14T12:00:00Z",
                        "created_at": "2026-05-14T11:30:00Z"
                      }
                    ]
                    """);
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == "/transaction/verify/schink-stories-maandeliks-existing")
            {
                paystackVerifyCalls++;
                return JsonResponse(
                    """
                    {
                      "status": true,
                      "data": {
                        "status": "success",
                        "reference": "schink-stories-maandeliks-existing",
                        "amount": 5500,
                        "currency": "ZAR",
                        "customer": { "email": "ouer@example.com" },
                        "metadata": {
                          "plan_slug": "schink-stories-maandeliks",
                          "tier_code": "all_stories_monthly",
                          "is_subscription": "true",
                          "subscription_key": "schink-stories-maandeliks-existing"
                        }
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/initialize")
            {
                paystackInitializeCalls++;
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(new HttpClient(handler));

        var result = await service.TryRecoverPaidCheckoutSessionAsync(
            PaymentPlanCatalog.FindBySlug("schink-stories-maandeliks")!,
            "Ouer@Example.com",
            CancellationToken.None);

        Assert.IsTrue(result.IsRecovered);
        Assert.AreEqual("schink-stories-maandeliks-existing", result.Reference);
        Assert.AreEqual("success", result.TransactionStatus);
        Assert.AreEqual("ouer@example.com", result.CustomerEmail);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.RawPayload));
        Assert.AreEqual(1, paystackVerifyCalls);
        Assert.AreEqual(0, paystackInitializeCalls);
    }

    [TestMethod]
    public async Task ChargeAuthorizationAsync_UsesStoredBillingAmountWhenProvided()
    {
        long? chargedAmountInCents = null;
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/transaction/charge_authorization")
            {
                var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result).RootElement;
                chargedAmountInCents = payload.GetProperty("amount").GetInt64();

                return JsonResponse(
                    """
                    {
                      "status": true,
                      "data": {
                        "status": "queued",
                        "reference": "retry-reference"
                      }
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(new HttpClient(handler));

        var result = await service.ChargeAuthorizationAsync(
            PaymentPlanCatalog.FindBySlug("schink-stories-maandeliks")!,
            "ouer@example.com",
            "AUTH_retry",
            "retry-reference",
            billingAmountZarOverride: 49.00m);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(4900L, chargedAmountInCents);
    }

    [TestMethod]
    public async Task InitializeDiscountedAuthorizationCheckoutAsync_ChargesDiscountedAmountWithoutPlanCode()
    {
        JsonElement? initializePayload = null;
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
                initializePayload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result).RootElement.Clone();

                return JsonResponse(
                    """
                    {
                      "status": true,
                      "data": {
                        "authorization_url": "https://checkout.paystack.com/discounted-session",
                        "reference": "discounted-reference"
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.AbsolutePath == "/rest/v1/paystack_checkout_sessions")
            {
                var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result).RootElement;
                Assert.AreEqual("subscription_discount", payload.GetProperty("metadata").GetProperty("checkout_kind").GetString());
                Assert.AreEqual("TENOFF", payload.GetProperty("metadata").GetProperty("discount_code").GetString());
                return JsonResponse("[]");
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(new HttpClient(handler));
        var discount = new SubscriptionCodeSignupPreviewResult(
            IsValid: true,
            Message: null,
            ResolvedTierCode: "all_stories_monthly",
            BypassesPayment: false,
            DiscountKind: SubscriptionDiscountKinds.Percentage,
            DiscountPercent: 10m,
            DiscountDuration: SubscriptionDiscountDurations.Lifetime,
            DiscountPaymentCount: null,
            OriginalAmountZar: 55m,
            DiscountedAmountZar: 49.50m,
            DurationDescription: "10% afslag vir die volle leeftyd van die intekening.");

        var result = await service.InitializeDiscountedAuthorizationCheckoutAsync(
            PaymentPlanCatalog.FindBySlug("schink-stories-maandeliks")!,
            "TENOFF",
            discount,
            CreateHttpContext(),
            returnUrl: "/luister");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("https://checkout.paystack.com/discounted-session", result.AuthorizationUrl);
        Assert.IsTrue(initializePayload.HasValue);
        Assert.AreEqual(4950L, initializePayload.Value.GetProperty("amount").GetInt64());
        Assert.IsFalse(initializePayload.Value.TryGetProperty("plan", out _), "Discounted authorization checkout must not send a Paystack plan code.");
        Assert.AreEqual("subscription_discount", initializePayload.Value.GetProperty("metadata").GetProperty("checkout_kind").GetString());
        Assert.AreEqual(55m, initializePayload.Value.GetProperty("metadata").GetProperty("original_amount_zar").GetDecimal());
        Assert.AreEqual(49.50m, initializePayload.Value.GetProperty("metadata").GetProperty("discounted_amount_zar").GetDecimal());
        Assert.AreEqual(true, initializePayload.Value.GetProperty("metadata").GetProperty("requires_reusable_authorization").GetBoolean());
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

    [TestMethod]
    public void PaystackPercentageDiscountMigration_AddsBillingScheduleAndAttemptGuards()
    {
        var migration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260522_paystack_percentage_discount_codes.sql"));

        StringAssert.Contains(migration, "discount_kind");
        StringAssert.Contains(migration, "discount_percent");
        StringAssert.Contains(migration, "discount_duration");
        StringAssert.Contains(migration, "discount_payment_count");
        StringAssert.Contains(migration, "recurring_billing_mode");
        StringAssert.Contains(migration, "paystack_authorization_schedule");
        StringAssert.Contains(migration, "discount_payments_used");
        StringAssert.Contains(migration, "authorization_reusable");
        StringAssert.Contains(migration, "create table if not exists public.subscription_recurring_charge_attempts");
        StringAssert.Contains(migration, "unique (reference)");
    }

    [TestMethod]
    public void PaystackAuthorizationScheduleWorker_IsDedicatedAndSkipsDuplicateAttemptReferences()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var recoveryWorker = File.ReadAllText(GetRepoPath("Shink", "Services", "SubscriptionPaymentRecoveryWorker.cs"));
        var billingWorker = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackAuthorizationSubscriptionBillingWorker.cs"));
        var scheduleService = File.ReadAllText(GetRepoPath(
            "Shink",
            "Services",
            "SupabaseSubscriptionLedgerService.PaystackAuthorizationSchedule.cs"));

        StringAssert.Contains(program, "AddHostedService<PaystackAuthorizationSubscriptionBillingWorker>");
        StringAssert.Contains(billingWorker, "ProcessPaystackAuthorizationScheduleAsync");
        Assert.IsFalse(
            recoveryWorker.Contains("ProcessPaystackAuthorizationScheduleAsync", StringComparison.Ordinal),
            "Payment recovery worker must not also process recurring authorization charges.");
        Assert.IsFalse(
            scheduleService.Contains("HttpStatusCode.Conflict", StringComparison.Ordinal),
            "Duplicate recurring charge attempt references must skip charging instead of being treated as inserted.");
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
                SecretKey = "secret-key"
            }));

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("schink.example.com");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "ouer@example.com")],
            "test"));
        return context;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string GetRepoPath(params string[] parts)
    {
        var testDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var candidate = Path.GetFullPath(Path.Combine([testDirectory, "..", .. parts]));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException("Could not find repo file.", Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
