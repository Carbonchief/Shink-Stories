using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class PaystackCallbackRoutingTests
{
    [TestMethod]
    public void SubscriptionPaystackCallbackReturnsCustomersToLuister()
    {
        var options = File.ReadAllText(GetRepoPath("Shink", "Services", "PaystackOptions.cs"));
        var appSettings = File.ReadAllText(GetRepoPath("Shink", "appsettings.json"));
        var developmentSettings = File.ReadAllText(GetRepoPath("appsettings.Development.json"));

        StringAssert.Contains(options, "public string CallbackUrlPath { get; set; } = \"/luister\";");
        StringAssert.Contains(appSettings, "\"CallbackUrlPath\": \"/luister\"");
        StringAssert.Contains(developmentSettings, "\"CallbackUrlPath\": \"/luister\"");
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
