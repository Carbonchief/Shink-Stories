using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class AbandonedCartPaymentCleanupSourceTests
{
    [TestMethod]
    public void PaymentConfirmedRoutesResolveAbandonedCartRecoveriesWithoutRequestAbortCancellation()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        var paystackCallback = Slice(program, "app.MapGet(\"/betaal/paystack/callback\"", "app.MapPost(\"/api/payfast/notify\"");
        StringAssert.Contains(paystackCallback, "ResolveByCheckoutReferenceAsync(");
        StringAssert.Contains(paystackCallback, "ResolveSubscriptionRecoveriesAsync(");
        StringAssert.Contains(paystackCallback, "CancellationToken.None");
        Assert.IsFalse(
            paystackCallback.Contains("ResolveByCheckoutReferenceAsync(\n        \"subscription\",\n        paidReference,\n        \"paid\",\n        httpContext.RequestAborted", StringComparison.Ordinal) ||
            paystackCallback.Contains("ResolveSubscriptionRecoveriesAsync(\n        verifyResult.CustomerEmail,\n        selectedPlan?.TierCode,\n        \"paid\",\n        httpContext.RequestAborted", StringComparison.Ordinal),
            "Confirmed Paystack callback cleanup must not be abortable by the browser request.");

        var payFastNotify = Slice(program, "app.MapPost(\"/api/payfast/notify\"", "app.MapPost(\"/api/paystack/webhook\"");
        StringAssert.Contains(payFastNotify, "payment_status\"].ToString(), \"COMPLETE\"");
        StringAssert.Contains(payFastNotify, "ResolveByCheckoutReferenceAsync(");
        StringAssert.Contains(payFastNotify, "ResolveSubscriptionRecoveriesAsync(");
        StringAssert.Contains(payFastNotify, "CancellationToken.None");

        var paystackWebhook = Slice(program, "app.MapPost(\"/api/paystack/webhook\"", "static string ResolvePayFastValidationFailureStage");
        StringAssert.Contains(paystackWebhook, "ResolveByCheckoutReferenceAsync(");
        StringAssert.Contains(paystackWebhook, "ResolveSubscriptionRecoveriesAsync(");
        StringAssert.Contains(paystackWebhook, "CancellationToken.None");

        var recoveredCheckout = Slice(program, "static async Task<IResult?> TryRedirectRecoveredPaystackSubscriptionAsync", "static string ResolveSuccessfulSubscriptionPaymentReturnPath");
        StringAssert.Contains(recoveredCheckout, "ResolveByCheckoutReferenceAsync(");
        StringAssert.Contains(recoveredCheckout, "ResolveSubscriptionRecoveriesAsync(");
        StringAssert.Contains(recoveredCheckout, "CancellationToken.None");
    }

    [TestMethod]
    public void ResendCancelAuditMigrationAllowsNotCancellableStatus()
    {
        var migration = File.ReadAllText(GetRepoPath(
            "Shink",
            "Database",
            "migrations",
            "20260520_abandoned_cart_email_cancel_audit.sql"));

        StringAssert.Contains(migration, "not_cancellable");
        StringAssert.Contains(migration, "chk_abandoned_cart_email_cancel_status");
    }

    private static string Slice(string source, string startNeedle, string endNeedle)
    {
        var start = source.IndexOf(startNeedle, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, $"Could not find {startNeedle}.");
        var end = source.IndexOf(endNeedle, start, StringComparison.Ordinal);
        Assert.IsTrue(end > start, $"Could not isolate block ending at {endNeedle}.");
        return source[start..end];
    }

    private static string GetRepoPath(params string[] segments)
    {
        var parts = new[]
        {
            Path.GetDirectoryName(GetSourceFilePath())!,
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
