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

    [TestMethod]
    public void AudioPlaybackDoesNotWaitForArtworkAndAndroidPreparationHasATimeout()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Services",
            "AudioPlaybackService.cs"));

        StringAssert.Contains(source, "_ = LoadArtworkForMetadataAsync(metadata);");
        Assert.IsFalse(source.Contains("await artworkLoadTask;", StringComparison.Ordinal));
        StringAssert.Contains(source, "private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(10);");
        StringAssert.Contains(source, "timeout.CancelAfter(ReadyTimeout);");
        StringAssert.Contains(source, "ReleasePlayer(player, stopFirst: false);");
        StringAssert.Contains(source, "catch (Java.Lang.IllegalStateException)");
        StringAssert.Contains(source, "player?.Dispose();");
    }

    [TestMethod]
    public void FrequentlyReadMobileStateUsesMemoryAfterInitialLoad()
    {
        var continueListening = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Services",
            "ContinueListeningState.cs"));
        var offlineDownloads = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Services",
            "OfflineStoryDownloadService.cs"));

        StringAssert.Contains(continueListening, "public ContinueListeningItem? Current => _current;");
        StringAssert.Contains(continueListening, "_current = item;");
        StringAssert.Contains(offlineDownloads, "private IReadOnlyList<OfflineStoryDownload>? _cachedDownloads;");
        StringAssert.Contains(offlineDownloads, "if (_cachedDownloads is not null)");
        StringAssert.Contains(offlineDownloads, "_cachedDownloads = downloads.ToArray();");
    }

    [TestMethod]
    public void LuisterBackgroundRefreshesStopWhenPageDisappears()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Pages",
            "LuisterPage.cs"));

        StringAssert.Contains(source, "_pageActivityCancellation?.Cancel();");
        StringAssert.Contains(source, "StartKaraktersDestinationWarmup(_pageActivityCancellation.Token)");
        StringAssert.Contains(source, "WarmCharactersCacheAsync(cancellationToken)");
        StringAssert.Contains(source, "PreloadCachedContentAsync(cancellationToken)");
        StringAssert.Contains(source, "RefreshVisibleStateAfterNavigationAsync(_pageActivityCancellation.Token)");
        StringAssert.Contains(source, "await Task.Delay(120, cancellationToken);");
        StringAssert.Contains(source, "await RefreshSessionInBackgroundAsync();");
        Assert.IsFalse(source.Contains("GetPlayableDownloadsAsync", StringComparison.Ordinal));
        StringAssert.Contains(source, "GetSessionAsync(cancellationToken)");
        StringAssert.Contains(source, "GetNotificationsAsync(cancellationToken: cancellationToken)");
    }

    [TestMethod]
    public void MobileStartupDoesNotBlockOnAsyncStorageOrPackageFiles()
    {
        var apiClient = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Services",
            "MobileApiClient.cs"));
        var accountPage = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Pages",
            "AccountPage.cs"));
        var appShell = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "AppShell.xaml.cs"));

        Assert.IsFalse(apiClient.Contains("GetAwaiter().GetResult()", StringComparison.Ordinal));
        Assert.IsFalse(accountPage.Contains("GetAwaiter().GetResult()", StringComparison.Ordinal));
        StringAssert.Contains(apiClient, "public async Task HydrateSensitiveCacheAsync()");
        StringAssert.Contains(apiClient, "startingVersion != Volatile.Read(ref _updateVersion)");
        StringAssert.Contains(accountPage, "ImageSource.FromStream(_ => FileSystem.OpenAppPackageFileAsync(fileName))");
        StringAssert.Contains(appShell, "_ = _sessionState.HydrateSensitiveCacheAsync();");
    }

    [TestMethod]
    public void LuisterMomentumScrollDefersNonessentialCarouselWork()
    {
        var luisterPage = File.ReadAllText(Path.Combine(RepoRoot, "Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mauiProgram = File.ReadAllText(Path.Combine(RepoRoot, "Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(luisterPage, "ScrollVisualUpdateInterval = TimeSpan.FromMilliseconds(100)");
        StringAssert.Contains(luisterPage, "QueueLuisterScrollUpdate(args.VerticalOffset)");
        StringAssert.Contains(luisterPage, "ApplyLuisterGradientForScroll(_pendingGradientScrollOffset)");
        var scrollTickStart = luisterPage.IndexOf(
            "private void OnScrollVisualUpdateTimerTick",
            StringComparison.Ordinal);
        var idleGuard = luisterPage.IndexOf(
            "Environment.TickCount64 - _lastScrollEventTick < ScrollIdleThresholdMilliseconds",
            scrollTickStart,
            StringComparison.Ordinal);
        var gradientUpdate = luisterPage.IndexOf(
            "ApplyLuisterGradientForScroll(_pendingGradientScrollOffset)",
            scrollTickStart,
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, scrollTickStart);
        Assert.IsGreaterThan(scrollTickStart, idleGuard);
        Assert.IsGreaterThan(scrollTickStart, gradientUpdate);
        Assert.IsGreaterThan(gradientUpdate, idleGuard);
        StringAssert.Contains(luisterPage, "PauseImageWarmupForScroll();");
        StringAssert.Contains(luisterPage, "ResumeImageWarmupAfterScroll();");
        StringAssert.Contains(luisterPage, "ResolvePlaylistShowcaseCoverHeight(wideLayout, pageWidth)");
        Assert.IsFalse(luisterPage.Contains("cover.SizeChanged +=", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "AutomationId = \"luister-feed\"");
        StringAssert.Contains(luisterPage, "AutomationId = \"luister-carousel\"");
        StringAssert.Contains(luisterPage, "ItemTemplate = new LuisterFeedTemplateSelector(this)");
        StringAssert.Contains(mauiProgram, "SchinkLuisterCollectionViewPerformance");
        StringAssert.Contains(mauiProgram, "layoutManager.InitialPrefetchItemCount = 4;");
    }

    [TestMethod]
    public void KaraktersReusesCardsAndWarmsPortraitsBeforeStoryArtwork()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Pages",
            "KaraktersPage.cs"));

        StringAssert.Contains(source, "new ReusableCharacterCardView(this)");
        StringAssert.Contains(source, "private sealed class ReusableCharacterCardView : ContentView");
        StringAssert.Contains(source, "_image.WidthRequest = imageSize;");
        StringAssert.Contains(source, "_image.HeightRequest = imageSize;");
        StringAssert.Contains(source, "AutomationId = \"characters-grid\"");
        StringAssert.Contains(source, "if (!IsIOS && !IsAndroid && _imageSourceCache.TryGetValue(cacheKey, out var source))");
        StringAssert.Contains(source, "await Task.Delay(TimeSpan.FromMilliseconds(750), token);");
        StringAssert.Contains(source, "maxDegreeOfParallelism: IsAndroid || IsIOS ? 1 : 3");
        Assert.IsFalse(source.Contains("host.Content = host.BindingContext", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("_imageSourceCache.Clear();", StringComparison.Ordinal));

        var portraits = source.IndexOf(".Select(character => character.ImageUrl)", StringComparison.Ordinal);
        var storyArtwork = source.IndexOf(".Concat(response.Characters.SelectMany", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, portraits);
        Assert.IsGreaterThan(portraits, storyArtwork);
    }

    [TestMethod]
    public void AndroidFeedsUseDisplaySizedDiskImagesAndNativeRecyclerPools()
    {
        var apiClient = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Services",
            "MobileApiClient.cs"));
        var optimizer = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "Platforms",
            "Android",
            "AndroidImageCacheOptimizer.cs"));
        var mauiProgram = File.ReadAllText(Path.Combine(
            RepoRoot,
            "Shink.Mobile",
            "MauiProgram.cs"));

        StringAssert.Contains(apiClient, "AndroidImageCacheOptimizer.ResolveDisplayPath(cachedPath)");
        StringAssert.Contains(apiClient, "AndroidImageCacheOptimizer.EnsureOptimized(cachePath, cancellationToken)");
        StringAssert.Contains(optimizer, "private const int MaxPixelDimension = 1280;");
        StringAssert.Contains(optimizer, "InJustDecodeBounds = true");
        StringAssert.Contains(optimizer, "InSampleSize = sampleSize");
        StringAssert.Contains(optimizer, "Bitmap.CreateScaledBitmap(decoded, targetWidth, targetHeight, filter: true)");
        StringAssert.Contains(mauiProgram, "or \"characters-grid\"");
        StringAssert.Contains(mauiProgram, "\"characters-grid\" => 12");
        StringAssert.Contains(mauiProgram, "_ => 8");
        StringAssert.Contains(mauiProgram, "collectionView.AutomationId == \"luister-feed\"");
        StringAssert.Contains(mauiProgram, "layoutManager.InitialPrefetchItemCount = 3;");
        StringAssert.Contains(mauiProgram, "layoutManager.InitialPrefetchItemCount = 9;");
        StringAssert.Contains(optimizer, "Math.Max(bounds.OutWidth, bounds.OutHeight) <= MaxPixelDimension");
    }

    [TestMethod]
    public void AndroidScrollingSurfacesAvoidPerItemShadows()
    {
        var search = File.ReadAllText(Path.Combine(RepoRoot, "Shink.Mobile", "Pages", "SearchPage.cs"));
        var characters = File.ReadAllText(Path.Combine(RepoRoot, "Shink.Mobile", "Pages", "KaraktersPage.cs"));
        var playlists = File.ReadAllText(Path.Combine(RepoRoot, "Shink.Mobile", "Pages", "PlaylistStoriesPage.cs"));
        var helpers = File.ReadAllText(Path.Combine(RepoRoot, "Shink.Mobile", "Pages", "PageHelpers.cs"));
        var storyDetail = File.ReadAllText(Path.Combine(RepoRoot, "Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(search, "Shadow = IsAndroid\n                ? null");
        StringAssert.Contains(characters, "Shadow = IsAndroid\n                ? null");
        StringAssert.Contains(playlists, "Shadow = IsAndroid\n                    ? null");
        StringAssert.Contains(helpers, "Shadow = DeviceInfo.Current.Platform == DevicePlatform.Android\n                ? null");
        StringAssert.Contains(storyDetail, "Shadow = IsAndroid\n                ? null");
        StringAssert.Contains(storyDetail, "IsAndroid\n            ? new SolidColorBrush(Color.FromArgb(\"#F3F0EA\"))");
        StringAssert.Contains(storyDetail, "IsAndroid\n            ? new SolidColorBrush(Colors.Transparent)");
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
