using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class PaystackWebhookHardeningTests
{
    [TestMethod]
    public void PaystackWebhookDoesNotAcknowledgeInvalidOrUnpersistedEventsAsSuccessful()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var webhookStart = program.IndexOf("static async Task<IResult> HandlePaystackWebhookAsync", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, webhookStart, "The Paystack webhook route must exist.");

        var webhookEnd = program.IndexOf("static string BuildStorePageRedirectPath", webhookStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(webhookStart, webhookEnd, "The Paystack webhook route block could not be isolated.");

        var webhookBlock = program[webhookStart..webhookEnd];
        StringAssert.Contains(webhookBlock, "return Results.Unauthorized();");
        StringAssert.Contains(webhookBlock, "return Results.Problem(");
        Assert.IsFalse(
            webhookBlock.Contains("// Return 200 to avoid repeated retries; process failed validations via logs/monitoring.", StringComparison.Ordinal),
            "Paystack should retry valid webhooks that cannot be persisted.");
    }

    [TestMethod]
    public void PaystackWebhookLegacyPmproPathRoutesThroughSharedHardenedHandler()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var legacyRouteStart = program.IndexOf("app.MapPost(\"/wp-admin/admin-ajax.php\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, legacyRouteStart, "The legacy PMPro Paystack webhook route must exist during cutover.");

        var legacyRouteEnd = program.IndexOf("app.MapGet(\"/api/auth/google/start\"", legacyRouteStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(legacyRouteStart, legacyRouteEnd, "The legacy PMPro Paystack webhook route block could not be isolated.");

        var legacyRouteBlock = program[legacyRouteStart..legacyRouteEnd];
        StringAssert.Contains(legacyRouteBlock, "pmpro_paystack_ipn");
        StringAssert.Contains(legacyRouteBlock, "HandlePaystackWebhookAsync(");
    }

    [TestMethod]
    public void PaystackSubscriptionCreateCanResolveDirectPlanCode()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        var resolverStart = ledgerService.IndexOf("private async Task<PaymentPlan?> ResolvePaystackPlanAsync", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, resolverStart, "The Paystack plan resolver must exist.");

        var resolverEnd = ledgerService.IndexOf("private async Task<string?> ResolveTierCodeByPaystackPlanCodeAsync", resolverStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(resolverStart, resolverEnd, "The Paystack plan resolver block could not be isolated.");

        var resolverBlock = ledgerService[resolverStart..resolverEnd];
        StringAssert.Contains(resolverBlock, "TryReadNestedString(data, \"plan\", \"plan_code\")");
        StringAssert.Contains(resolverBlock, "TryReadString(data, \"plan\")");
    }

    [TestMethod]
    public void PaystackSubscriptionCreateCanFallbackToPlanAmountAndInterval()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        var resolverStart = ledgerService.IndexOf("private async Task<PaymentPlan?> ResolvePaystackPlanAsync", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, resolverStart, "The Paystack plan resolver must exist.");

        var resolverEnd = ledgerService.IndexOf("private async Task<string?> ResolveTierCodeByPaystackPlanCodeAsync", resolverStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(resolverStart, resolverEnd, "The Paystack plan resolver block could not be isolated.");

        var resolverBlock = ledgerService[resolverStart..resolverEnd];
        StringAssert.Contains(resolverBlock, "ResolvePaystackPlanByAmountAndInterval(data)");

        StringAssert.Contains(ledgerService, "private static PaymentPlan? ResolvePaystackPlanByAmountAndInterval(JsonElement data)");
        StringAssert.Contains(ledgerService, "TryReadNestedDecimal(data, \"plan\", \"amount\")");
        StringAssert.Contains(ledgerService, "TryReadNestedString(data, \"plan\", \"interval\")");
        StringAssert.Contains(ledgerService, "\"monthly\"");
        StringAssert.Contains(ledgerService, "\"annually\"");
    }

    [TestMethod]
    public void PaystackSubscriptionCreateStoresBillingAmountForRevenueAnalytics()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        var upsertStart = ledgerService.IndexOf("private async Task<PaystackUpsertResult> UpsertActivePaystackSubscriptionAsync", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, upsertStart, "The Paystack upsert helper must exist.");

        var upsertEnd = ledgerService.IndexOf("private async Task<PaymentPlan?> ResolvePaystackPlanAsync", upsertStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(upsertStart, upsertEnd, "The Paystack upsert helper block could not be isolated.");

        var upsertBlock = ledgerService[upsertStart..upsertEnd];
        StringAssert.Contains(upsertBlock, "ResolvePaystackBillingAmountZar(data, plan)");
        StringAssert.Contains(upsertBlock, "billingAmountZar: billingAmountZar");
        StringAssert.Contains(upsertBlock, "billingPeriodMonths: plan.BillingPeriodMonths");
        StringAssert.Contains(upsertBlock, "billingAmountSource: \"paystack_payload\"");

        StringAssert.Contains(ledgerService, "private static decimal ResolvePaystackBillingAmountZar(JsonElement data, PaymentPlan plan)");
        StringAssert.Contains(ledgerService, "TryReadStringAsDecimal(data, \"amount\")");
    }

    [TestMethod]
    public void FailedPaystackWebhookAttemptsAreLoggedWithPayloadBeforeAlerting()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        StringAssert.Contains(ledgerService, "InsertPaymentWebhookFailureAsync(");
        StringAssert.Contains(ledgerService, "rest/v1/payment_webhook_failures");
        StringAssert.Contains(ledgerService, "failureStage: \"subscription-upsert\"");
        StringAssert.Contains(ledgerService, "payload: DeserializePayloadObject(payloadJson)");

        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260430_failed_payment_webhook_payload_logs.sql"));
        StringAssert.Contains(migration, "create table if not exists public.payment_webhook_failures");
        StringAssert.Contains(migration, "payload jsonb not null");
        StringAssert.Contains(migration, "alter table public.payment_webhook_failures enable row level security");
    }

    [TestMethod]
    public void PaystackBillingAmountBackfillMigrationExists()
    {
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260502_paystack_subscription_revenue_backfill.sql"));

        StringAssert.Contains(migration, "billing_amount_zar");
        StringAssert.Contains(migration, "paystack_payload");
        StringAssert.Contains(migration, "subscription_events");
        StringAssert.Contains(migration, "provider = 'paystack'");
        StringAssert.Contains(migration, "coalesce(subscription.billing_amount_zar, 0) = 0");
    }

    [TestMethod]
    public void PaystackInvoiceUpdateCanRefreshExistingSubscription()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));

        StringAssert.Contains(ledgerService, "string.Equals(eventType, \"invoice.update\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(ledgerService, "TryReadNestedString(data, \"subscription\", \"next_payment_date\")");
        StringAssert.Contains(ledgerService, "TryReadNestedString(data, \"transaction\", \"reference\")");
    }

    [TestMethod]
    public void PaystackInvoiceChargeSuccessLinksToExistingSubscription()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));

        StringAssert.Contains(ledgerService, "IsPaystackInvoiceChargeSuccess(eventType, data)");
        StringAssert.Contains(ledgerService, "TryGetSubscriptionContextByProviderTransactionIdAsync");
        StringAssert.Contains(ledgerService, "providerPaymentId = paystackContext.ProviderPaymentId");
    }

    [TestMethod]
    public void PaystackNotRenewSchedulesCancellationWithoutEndingAccessImmediately()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));

        StringAssert.Contains(ledgerService, "string.Equals(eventType, \"subscription.not_renew\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(ledgerService, "string.Equals(eventStatus, \"non-renewing\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(ledgerService, "ScheduleSubscriptionCancellationAsync(");
        StringAssert.Contains(ledgerService, "TryReadString(data, \"next_payment_date\")");
        StringAssert.Contains(ledgerService, "failureStage: \"subscription-cancellation-schedule\"");
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
