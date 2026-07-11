using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobilePerformanceReliabilitySourceTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [TestMethod]
    public void MobileGetRequestsRetryTransientFailuresOnly()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Services",
            "MobileApiClient.cs"));

        StringAssert.Contains(source, "SendGetWithTransientRetryAsync(path, cancellationToken)");
        StringAssert.Contains(source, "HttpCompletionOption.ResponseHeadersRead");
        StringAssert.Contains(source, "IsTransientGetStatusCode(response.StatusCode)");
        StringAssert.Contains(source, "catch (HttpRequestException) when (attempt == 0");
        StringAssert.Contains(source, "HttpStatusCode.TooManyRequests");
        StringAssert.Contains(source, "HttpStatusCode.ServiceUnavailable");
        Assert.IsFalse(
            source.Contains("SendPostWithTransientRetry", StringComparison.Ordinal),
            "Non-idempotent mobile mutations must not be retried automatically.");
    }

    [TestMethod]
    public void MobileCookiePersistenceSkipsUnchangedSecureStorageWrites()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Services",
            "MobileApiClient.cs"));

        StringAssert.Contains(source, "private string? _lastPersistedAuthCookies;");
        StringAssert.Contains(
            source,
            "string.Equals(serializedCookies, _lastPersistedAuthCookies, StringComparison.Ordinal)");
        StringAssert.Contains(source, "_lastPersistedAuthCookies = serializedCookies;");
    }

    [TestMethod]
    public void LuisterRefreshCancelsItsNetworkRequestsWhenPageDisappears()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Pages",
            "LuisterPage.cs"));

        StringAssert.Contains(source, "_apiClient.GetSessionAsync(cancellationToken)");
        StringAssert.Contains(source, "_apiClient.GetLuisterAsync(cancellationToken)");
    }

    [TestMethod]
    public void IosInfoPlistUsesTheGeneratedMauiAppIconCatalogName()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Platforms",
            "iOS",
            "Info.plist"));

        StringAssert.Contains(source, "Assets.xcassets/schink_appicon.appiconset");
        Assert.IsFalse(source.Contains("Assets.xcassets/appicon.appiconset", StringComparison.Ordinal));
    }

    [TestMethod]
    public void IosOrientationRefreshIsGuardedForIos16AndUsesSceneWindows()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Services",
            "OrientationService.cs"));

        var versionGuard = source.IndexOf("OperatingSystem.IsIOSVersionAtLeast(16)", StringComparison.Ordinal);
        var refreshCall = source.IndexOf("SetNeedsUpdateOfSupportedInterfaceOrientations", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, versionGuard);
        Assert.IsGreaterThan(versionGuard, refreshCall);
        StringAssert.Contains(source, ".FirstOrDefault(window => window.IsKeyWindow)");
        Assert.IsFalse(source.Contains("SharedApplication.KeyWindow", StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Shink-Stories.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Shink repository root.");
    }
}
