using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class PayFastWebhookHardeningTests
{
    [TestMethod]
    public void FailedPayFastWebhookAttemptsAreLoggedWithPayload()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var webhookStart = program.IndexOf("app.MapPost(\"/api/payfast/notify\"", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, webhookStart, "The PayFast ITN route must exist.");

        var webhookEnd = program.IndexOf("app.MapPost(\"/api/paystack/webhook\"", webhookStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(webhookStart, webhookEnd, "The PayFast ITN route block could not be isolated.");

        var webhookBlock = program[webhookStart..webhookEnd];
        StringAssert.Contains(webhookBlock, "RecordPayFastWebhookFailureAsync(");
        StringAssert.Contains(webhookBlock, "failureStage: \"request-content-type\"");
        StringAssert.Contains(webhookBlock, "failureStage: ResolvePayFastValidationFailureStage(signatureValid, serverConfirmed)");
        StringAssert.Contains(webhookBlock, "failureStage: \"subscription-persist\"");

        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        StringAssert.Contains(ledgerService, "public async Task RecordPayFastWebhookFailureAsync(");
        StringAssert.Contains(ledgerService, "InsertPaymentWebhookFailureAsync(");
        StringAssert.Contains(ledgerService, "provider: \"payfast\"");
        StringAssert.Contains(ledgerService, "payload: SerializePayFastPayload(formCollection)");
    }

    [TestMethod]
    public void PayFastRecurringAmountValidationUsesStoredAmountBeforeCurrentPlanAmount()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));

        StringAssert.Contains(ledgerService, "ValidatePayFastAmountAsync(");
        StringAssert.Contains(ledgerService, "existingContext?.BillingAmountZar");
        StringAssert.Contains(ledgerService, "amount_gross");
        StringAssert.Contains(ledgerService, "PayFast amount mismatch");
        StringAssert.Contains(ledgerService, "billing_amount_zar = billingAmountZar");
        StringAssert.Contains(ledgerService, "billing_amount_source = billingAmountSource");
    }

    [TestMethod]
    public void PayFastUnknownStoredAmountIsSeededFromFirstWebhook()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        var validationStart = ledgerService.IndexOf("private async Task<PayFastAmountValidationResult> ValidatePayFastAmountAsync", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, validationStart, "The PayFast amount validation helper must exist.");

        var validationEnd = ledgerService.IndexOf("private async Task<PaystackUpsertResult> UpsertActivePaystackSubscriptionAsync", validationStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(validationStart, validationEnd, "The PayFast amount validation block could not be isolated.");

        var validationBlock = ledgerService[validationStart..validationEnd];
        StringAssert.Contains(validationBlock, "if (existingContext?.BillingAmountZar is decimal storedAmount)");
        StringAssert.Contains(validationBlock, "return new PayFastAmountValidationResult(true, grossAmount, \"payfast_itn\");");
        Assert.IsFalse(
            validationBlock.Contains("existingContext?.BillingAmountZar is null") &&
            validationBlock.Contains("return new PayFastAmountValidationResult(false"),
            "Unknown historical PayFast amounts must be seeded from the webhook instead of failing the first renewal.");
    }

    [TestMethod]
    public void SuccessfulPaystackWebhookMarksPaymentPauseResumedBeforeFinalizingEvent()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        var markerIndex = ledgerService.IndexOf("TryMarkSubscriptionPaymentPauseResumedAsync(", StringComparison.Ordinal);
        var finalizerIndex = ledgerService.IndexOf("FinalizeClaimedSubscriptionEventAsync(", markerIndex, StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, markerIndex, "Successful Paystack processing must mark resumed payment pauses.");
        Assert.IsGreaterThan(markerIndex, finalizerIndex, "Payment pause resume marking should happen before event finalization.");
        StringAssert.Contains(ledgerService, "ShouldActivatePaystackSubscription(eventType, eventStatus)");

        var schedulePartial = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.PaystackAuthorizationSchedule.cs"));
        StringAssert.Contains(schedulePartial, "status = \"resumed\"");
        StringAssert.Contains(schedulePartial, "status=eq.resume_pending");
    }

    [TestMethod]
    public void PaystackEnableSubscriptionMirrorsDisableTokenAndErrorHandling()
    {
        var service = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackCheckoutService.cs"));
        var methodStart = service.IndexOf("public async Task<PaystackSubscriptionEnableResult> EnableSubscriptionAsync", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, methodStart, "Paystack enable helper must exist.");

        var methodEnd = service.IndexOf("public async Task<PaystackAuthorizationChargeResult> ChargeAuthorizationAsync", methodStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(methodStart, methodEnd, "Paystack enable helper block could not be isolated.");

        var method = service[methodStart..methodEnd];
        StringAssert.Contains(method, "FetchSubscriptionEmailTokenAsync(normalizedCode");
        StringAssert.Contains(method, "https://api.paystack.co/subscription/enable");
        StringAssert.Contains(method, "code = normalizedCode");
        StringAssert.Contains(method, "token = normalizedToken");
        StringAssert.Contains(method, "!response.IsSuccessStatusCode");
        StringAssert.Contains(method, "JsonException");
        StringAssert.Contains(service, "public sealed record PaystackSubscriptionEnableResult");
    }

    [TestMethod]
    public void SubscriptionsStoreProviderBillingAmountForRecurringAmountChecks()
    {
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260501_payfast_recurring_amount_and_failure_logging.sql"));

        StringAssert.Contains(migration, "billing_amount_zar numeric(10, 2)");
        StringAssert.Contains(migration, "billing_period_months integer");
        StringAssert.Contains(migration, "billing_amount_source text");
        StringAssert.Contains(migration, "amount_gross");
        StringAssert.Contains(migration, "payfast_itn");
    }

    [TestMethod]
    public void PlanChangeBillingAmountSourceIsAllowedBySubscriptionConstraint()
    {
        var migration = File.ReadAllText(GetRepoPath("Shink", "Database", "migrations", "20260529_allow_plan_change_billing_amount_source.sql"));

        StringAssert.Contains(migration, "subscriptions_billing_amount_source_known");
        StringAssert.Contains(migration, "'plan_change'");
        StringAssert.Contains(migration, "validate constraint subscriptions_billing_amount_source_known");
    }

    [TestMethod]
    public void PlanChangeUpgradeChargeReferencesAreStablePerRenewalWindow()
    {
        var ledgerService = File.ReadAllText(GetRepoPath("Shink", "Services", "SupabaseSubscriptionLedgerService.cs"));
        var methodStart = ledgerService.IndexOf("public async Task<SubscriptionPlanChangeResult> ChangePaidSubscriptionPlanAsync", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, methodStart, "The plan-change method must exist.");

        var methodEnd = ledgerService.IndexOf("public async Task<SubscriptionCardUpdateLinkResult> CreatePaystackCardUpdateLinkAsync", methodStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(methodStart, methodEnd, "The plan-change method block could not be isolated.");

        var planChangeBlock = ledgerService[methodStart..methodEnd];
        StringAssert.Contains(planChangeBlock, "BuildPlanChangeReference(currentSubscription.SubscriptionId, targetPlan.TierCode, accessEndsAtUtc)");
        StringAssert.Contains(planChangeBlock, "IsDuplicatePaystackReferenceFailure(chargeResult)");

        var helperStart = ledgerService.IndexOf("private static string BuildPlanChangeReference", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, helperStart, "The plan-change reference helper must exist.");

        var helperEnd = ledgerService.IndexOf("private static int ResolvePaidPlanAccessRank", helperStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(helperStart, helperEnd, "The plan-change reference helper block could not be isolated.");

        var helperBlock = ledgerService[helperStart..helperEnd];
        StringAssert.Contains(helperBlock, "DateTimeOffset effectiveAtUtc");
        StringAssert.Contains(helperBlock, "effectiveAtUtc.UtcDateTime.ToString(\"yyyyMMddHHmmss\", CultureInfo.InvariantCulture)");
        Assert.IsFalse(helperBlock.Contains("DateTime.UtcNow", StringComparison.Ordinal), "Upgrade charge references must not change on retry.");
        Assert.IsFalse(helperBlock.Contains("Guid.NewGuid", StringComparison.Ordinal), "Upgrade charge references must not include random data.");
    }

    [TestMethod]
    public void StatusCodeErrorRouteIsNotDuplicatedByCase()
    {
        var errorPage = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "Error.razor"));
        var routes = errorPage
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("@page ", StringComparison.Ordinal))
            .Select(line => line["@page ".Length..].Trim().Trim('"'))
            .ToList();

        var duplicateRoutes = routes
            .GroupBy(route => route, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            duplicateRoutes,
            "Case-variant error routes cause ASP.NET Core endpoint ambiguity when status code pages re-execute /error/{status}.");
        CollectionAssert.Contains(routes, "/error/{StatusCode:int}");
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
