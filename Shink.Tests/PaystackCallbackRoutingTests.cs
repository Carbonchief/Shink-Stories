using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;
using System.Text.Json;

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
        var jsonOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var appSettings = JsonDocument.Parse(File.ReadAllText(GetRepoPath("Shink", "appsettings.json")), jsonOptions);

        StringAssert.Contains(program, "app.MapGet(\"/betaal/paystack/callback\"");
        StringAssert.Contains(program, "VerifyTransactionAsync(resolvedReference");
        StringAssert.Contains(program, "RecordPaystackEventAsync(payload");
        StringAssert.Contains(checkoutService, "public const string SubscriptionCallbackPath = \"/betaal/paystack/callback\";");
        StringAssert.Contains(options, "PaystackCheckoutService.SubscriptionCallbackPath");
        Assert.AreEqual("/betaal/paystack/callback", appSettings.RootElement.GetProperty("Paystack").GetProperty("CallbackUrlPath").GetString());

        // Local overrides are optional and deliberately excluded from Git. Check
        // an override when present without making CI depend on private settings
        // or including their entire contents in an assertion failure.
        var developmentPath = GetRepoPath("Shink", "appsettings.Development.json");
        if (File.Exists(developmentPath))
        {
            using var development = JsonDocument.Parse(File.ReadAllText(developmentPath), jsonOptions);
            if (development.RootElement.TryGetProperty("Paystack", out var paystack) &&
                paystack.TryGetProperty("CallbackUrlPath", out var callback))
                Assert.AreEqual("/betaal/paystack/callback", callback.GetString(), "The local Paystack callback override must verify the payment.");
        }
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
