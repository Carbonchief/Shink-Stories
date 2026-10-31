using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

public partial class SupabaseSubscriptionLedgerSelfServiceTests
{
    [TestMethod]
    [DataRow(true, false, false, StatusCodes.Status200OK)]
    [DataRow(true, true, false, StatusCodes.Status500InternalServerError)]
    [DataRow(false, false, false, StatusCodes.Status401Unauthorized)]
    [DataRow(true, false, true, StatusCodes.Status200OK)]
    [DataRow(true, true, true, StatusCodes.Status500InternalServerError)]
    [DataRow(false, false, true, StatusCodes.Status401Unauthorized)]
    public async Task CardUpdateEndpoint_AuthenticatesAndDurablyQueuesBeforeAcknowledging(
        bool validSignature, bool storageUnavailable, bool isRefund, int expectedStatus)
    {
        var fixture = new CardUpdateFixture { ProviderUnavailable = true, QueueStorageUnavailable = storageUnavailable };
        var payload = isRefund
            ? """{"event":"refund.processed","data":{"id":902,"transaction_reference":"update-card-transaction","status":"processed","amount":100,"currency":"ZAR"}}"""
            : fixture.Payload;
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes("paystack-test-key"));
        context.Request.Headers["x-paystack-signature"] = validSignature
            ? Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()
            : "invalid";
        var paystack = new PaystackCheckoutService(new HttpClient(fixture.Handler), Options.Create(new PaystackOptions { SecretKey = "paystack-test-key" }));

        // Invoke the actual mapped route helper without starting the production
        // application or any of its background workers/configured integrations.
        var program = typeof(PaystackCheckoutService).Assembly.GetType("Program")!;
        var route = program.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name.Contains("g__HandlePaystackWebhookAsync", StringComparison.Ordinal));
        var logger = Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(program));
        var result = await (Task<IResult>)route.Invoke(null, [context, paystack, CreateService(fixture.Handler),
            DispatchProxy.Create<IStoreOrderService, UnexpectedCardUpdateRouteCall>(),
            DispatchProxy.Create<IStoreOrderNotificationService, UnexpectedCardUpdateRouteCall>(),
            DispatchProxy.Create<IAbandonedCartRecoveryService, UnexpectedCardUpdateRouteCall>(), logger])!;

        Assert.AreEqual(expectedStatus, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.AreEqual(validSignature && !storageUnavailable ? 1 : 0, fixture.Jobs.Count);
        Assert.AreEqual(0, fixture.ProviderRequestCount);
        Assert.IsEmpty(fixture.Handler.SubscriptionPatchPayloads);
    }

    public class UnexpectedCardUpdateRouteCall : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new AssertFailedException("A deferred card verification must not run checkout, marketing or store operations: " + targetMethod?.Name);
    }
}
