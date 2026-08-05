using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class StoreKitConfigurationSourceTests
{
    [TestMethod]
    public void DebugStoreKitConfigurationContainsHouseholdSubscriptionsOnly()
    {
        var configPath = GetRepoPath(
            "Shink.Mobile",
            "Platforms",
            "iOS",
            "StoreKit",
            "SchinkStories.storekit");
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));

        var subscriptions = document.RootElement
            .GetProperty("subscriptionGroups")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("subscriptions").EnumerateArray())
            .ToArray();
        var productIds = subscriptions
            .Select(subscription => subscription.GetProperty("productID").GetString())
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "storie_hoekie_maandeliks",
                "schink_stories_maandeliks",
                "schink_stories_jaarliks"
            },
            productIds);
        Assert.IsTrue(subscriptions.All(subscription =>
            subscription.GetProperty("type").GetString() == "RecurringSubscription"));
        Assert.IsFalse(productIds.Any(productId =>
            productId?.StartsWith("skool-", StringComparison.OrdinalIgnoreCase) == true));
    }

    [TestMethod]
    public void StoreKitConfigurationIsDebugOnlyInTheMobileProject()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));

        StringAssert.Contains(project, "SchinkStories.storekit");
        StringAssert.Contains(project, "'$(Configuration)' == 'Debug'");
        StringAssert.Contains(project, "<BundleResource");
    }

    [TestMethod]
    public void LocalStoreKitRunnerSelectsTheDebugConfigurationFile()
    {
        var scheme = File.ReadAllText(GetRepoPath(
            "Shink.Mobile.StoreKitRunner.xcodeproj",
            "xcshareddata",
            "xcschemes",
            "SchinkStories-StoreKit.xcscheme"));

        StringAssert.Contains(
            scheme,
            "identifier = \"../../Shink.Mobile/Platforms/iOS/StoreKit/SchinkStories.storekit\"");
        StringAssert.Contains(scheme, "selectedDebuggerIdentifier = \"\"");
        StringAssert.Contains(
            scheme,
            "selectedLauncherIdentifier = \"Xcode.IDEFoundation.Launcher.PosixSpawn\"");
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
