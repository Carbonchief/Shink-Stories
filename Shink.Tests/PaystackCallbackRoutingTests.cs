using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class PaystackCallbackRoutingTests
{
    [TestMethod]
    public void SubscriptionPaystackCallbackUsesVerifyingCallbackRoute()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var checkoutService = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackCheckoutService.cs"));
        var options = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackOptions.cs"));
        var appSettings = File.ReadAllText(GetRepoPath("Shink", "appsettings.json"));
        var developmentSettings = File.ReadAllText(GetRepoPath("appsettings.Development.json"));

        StringAssert.Contains(program, "app.MapGet(\"/betaal/paystack/callback\"");
        StringAssert.Contains(program, "VerifyTransactionAsync(resolvedReference");
        StringAssert.Contains(program, "RecordPaystackEventAsync(payload");
        StringAssert.Contains(checkoutService, "public const string SubscriptionCallbackPath = \"/betaal/paystack/callback\";");
        StringAssert.Contains(options, "PaystackCheckoutService.SubscriptionCallbackPath");
        StringAssert.Contains(appSettings, "\"CallbackUrlPath\": \"/betaal/paystack/callback\"");
        StringAssert.Contains(developmentSettings, "\"CallbackUrlPath\": \"/betaal/paystack/callback\"");
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
