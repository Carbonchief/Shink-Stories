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
