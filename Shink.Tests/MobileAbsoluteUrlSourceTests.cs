using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public class MobileAbsoluteUrlSourceTests
{
    [TestMethod]
    public void MobileAbsoluteUrlHelperDoesNotTreatRootRelativePathsAsFileUrls()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "trimmedPathOrUrl.StartsWith(\"//\", StringComparison.Ordinal)");
        StringAssert.Contains(program, "absoluteUri.Scheme is \"http\" or \"https\"");
        StringAssert.Contains(program, "return $\"{baseUrl}/{trimmedPathOrUrl.TrimStart('/')}\";");
        Assert.IsFalse(program.Contains("if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absoluteUri))", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileStorySummariesUseDirectMediaUrlsInsteadOfBrowserImageProxy()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(program, "static string ToMobileMediaUri(HttpContext httpContext, string? pathOrUrl)");
        StringAssert.Contains(program, "TryExtractImageProxySource(trimmedPathOrUrl, out var proxiedSourceUrl)");
        StringAssert.Contains(program, "QueryHelpers.ParseQuery(query)");
        StringAssert.Contains(program, "ImageUrl: ToMobileMediaUri(httpContext, story.ImagePath)");
        StringAssert.Contains(program, "ThumbnailUrl: ToMobileMediaUri(httpContext, story.ThumbnailPath)");
        Assert.IsFalse(program.Contains("ImageUrl: ToAbsoluteUri(httpContext, story.ImagePath)", StringComparison.Ordinal));
        Assert.IsFalse(program.Contains("ThumbnailUrl: ToAbsoluteUri(httpContext, story.ThumbnailPath)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileClientUnwrapsLiveImageProxyUrlsAndRejectsFileUrls()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(client, "NormalizeIncomingUrl(url.Trim())");
        StringAssert.Contains(client, "TryExtractImageProxySource(trimmedUrl, out var proxiedImageUrl)");
        StringAssert.Contains(client, "string.Equals(parsed.Scheme, Uri.UriSchemeFile");
        StringAssert.Contains(client, "return $\"{path}{parsed.Query}{parsed.Fragment}\";");
        StringAssert.Contains(client, "private static bool IsWebUri(Uri uri)");
        StringAssert.Contains(client, "Uri.UriSchemeHttps");
        Assert.IsFalse(client.Contains("if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "Source = \"dis_storietyd.png\"");
        Assert.IsFalse(luisterPage.Contains("_apiClient.BuildImageUrl(\"/branding/DIS_STORIETYD.png\")", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterPlaylistCardsUseDedicatedPlaylistArtwork()
    {
        var helper = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PageHelpers.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(helper, "ResolveStoryCardImageSource(story, apiClient)");
        StringAssert.Contains(helper, "IsLegacyWebsiteAsset(story.ThumbnailUrl)");
        StringAssert.Contains(helper, "return apiClient.BuildImageUrl(story.ImageUrl);");
        StringAssert.Contains(helper, "normalized.StartsWith(\"/stories/\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(luisterPage, "playlist.ArtworkUrl");
        StringAssert.Contains(luisterPage, "_apiClient.BuildImageUrl(playlist.ArtworkUrl)");
        Assert.IsFalse(luisterPage.Contains("firstStory?.ImageUrl", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("firstStory?.ThumbnailUrl", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("IsBundledPlaylistFallback", StringComparison.Ordinal));
        StringAssert.Contains(program, "ArtworkUrl: BuildMobilePlaylistArtworkUri(httpContext, playlist)");
        StringAssert.Contains(program, "playlist.ShowcaseImagePath");
        StringAssert.Contains(program, "playlist.BackdropImagePath");
        StringAssert.Contains(program, "playlist.LogoImagePath");
        Assert.IsFalse(program.Contains("preferredStory?.ImagePath", StringComparison.Ordinal));
        Assert.IsFalse(program.Contains("preferredStory?.ThumbnailPath", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterStoryCardsUseNativeArtworkAndFavoriteHeartOverlay()
    {
        var helper = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "PageHelpers.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(helper, "BuildFavoriteHeart(story, onFavoriteTap)");
        StringAssert.Contains(helper, "Text = story.IsFavorite ? \"♥\" : \"♡\"");
        StringAssert.Contains(helper, "TextColor = story.IsFavorite ? Color.FromArgb(\"#E11D48\") : Color.FromArgb(\"#8A938D\")");
        StringAssert.Contains(helper, "HorizontalOptions = LayoutOptions.End");
        StringAssert.Contains(helper, "VerticalOptions = LayoutOptions.Start");
        StringAssert.Contains(helper, "StrokeShape = new RoundRectangle { CornerRadius = 24 }");
        StringAssert.Contains(helper, "Text = story.IsLocked ? \"Maak oop\" : \"Luister nou\"");
        Assert.IsFalse(helper.Contains("Text = story.IsFavorite ? \"Hartjie af\" : \"Hartjie\"", StringComparison.Ordinal));
        Assert.IsFalse(helper.Contains("new HorizontalStackLayout", StringComparison.Ordinal));
        StringAssert.Contains(luisterPage, "card.WidthRequest = 226;");
        StringAssert.Contains(luisterPage, "StrokeShape = new RoundRectangle { CornerRadius = 20 }");
    }

    [TestMethod]
    public void MobileStoryDetailUsesNativePlayerAndDirectR2AudioUrls()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var audioService = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "AudioPlaybackService.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(program, "IStoryMediaStorageService storyMediaStorageService");
        StringAssert.Contains(program, "ResolveMobileAudioUrlAsync(");
        StringAssert.Contains(program, "storyMediaStorageService.CreateAudioReadUrlAsync(");
        StringAssert.Contains(program, "DurationSeconds: story.DurationSeconds");
        StringAssert.Contains(program, "decimal? DurationSeconds");
        StringAssert.Contains(models, "decimal? DurationSeconds");
        StringAssert.Contains(storyDetail, "IAudioPlaybackService _audioPlaybackService");
        StringAssert.Contains(storyDetail, "await _audioPlaybackService.PlayAsync(");
        StringAssert.Contains(storyDetail, "new AudioPlaybackMetadata(");
        StringAssert.Contains(storyDetail, "Shell.SetTabBarIsVisible(this, false)");
        StringAssert.Contains(storyDetail, "BuildCoverArt(detail)");
        StringAssert.Contains(storyDetail, "HeightRequest = CoverArtHeight");
        StringAssert.Contains(storyDetail, "return Math.Clamp(height * 0.36, 260, 330);");
        StringAssert.Contains(storyDetail, "BuildTransportControls(detail, playButton)");
        StringAssert.Contains(storyDetail, "HeightRequest = 82");
        StringAssert.Contains(storyDetail, "WidthRequest = 76");
        StringAssert.Contains(storyDetail, "if (ScreenHeight >= TallScreenThreshold)");
        StringAssert.Contains(storyDetail, "_activeCatalogDuration = ToTimeSpan(detail.Story.DurationSeconds);");
        StringAssert.Contains(storyDetail, "_activeCatalogDuration is null ? \"--:--\" : FormatTime(_activeCatalogDuration.Value)");
        StringAssert.Contains(storyDetail, "var duration = _audioPlaybackService.Duration ?? _activeCatalogDuration;");
        StringAssert.Contains(storyDetail, "durationSeconds: detail.Story.DurationSeconds");
        StringAssert.Contains(storyDetail, "PrepareAudioPlaybackSourceAsync(");
        StringAssert.Contains(storyDetail, "DownloadAudioForPlaybackAsync(");
        Assert.IsFalse(storyDetail.Contains("Gereed om te luister", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("Onderbreek", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("Besig om te speel", StringComparison.Ordinal));
        StringAssert.Contains(audioService, "TimeSpan CurrentPosition");
        StringAssert.Contains(audioService, "TimeSpan? Duration");
        StringAssert.Contains(audioService, "WaitUntilReadyToPlayAsync(playerItem)");
        StringAssert.Contains(audioService, "AVPlayerItemStatus.ReadyToPlay");
        StringAssert.Contains(audioService, "AVPlayerItemStatus.Failed");
        StringAssert.Contains(audioService, "AVAudioSessionCategory.Playback");
        StringAssert.Contains(audioService, "MPRemoteCommandCenter.Shared");
        StringAssert.Contains(audioService, "MPNowPlayingInfoCenter.DefaultCenter.NowPlaying");
        StringAssert.Contains(audioService, "MPMediaItemArtwork");
        StringAssert.Contains(audioService, "LoadArtworkForMetadataAsync(metadata)");
        StringAssert.Contains(audioService, "info.Artwork = _artwork;");
        StringAssert.Contains(audioService, "GetByteArrayAsync(artworkUrl");
        Assert.IsFalse(storyDetail.Contains("<audio id=\"story-audio\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("new WebView", StringComparison.Ordinal));
        StringAssert.Contains(audioService, "AVFoundation.AVPlayer");
        StringAssert.Contains(mauiProgram, "ConfigureEntryChrome();");
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();");
    }

    [TestMethod]
    public void MobileIosDeclaresBackgroundAudioForNativePlayback()
    {
        var infoPlist = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Info.plist"));

        StringAssert.Contains(infoPlist, "<key>UIBackgroundModes</key>");
        StringAssert.Contains(infoPlist, "<string>audio</string>");
    }

    [TestMethod]
    public void MobileClientDownloadsProtectedWebsiteAudioBeforeNativePlayback()
    {
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));

        StringAssert.Contains(client, "PrepareAudioPlaybackSourceAsync(");
        StringAssert.Contains(client, "ShouldDownloadAudioForPlayback(playableUrl)");
        StringAssert.Contains(client, "DownloadAudioForPlaybackAsync(");
        StringAssert.Contains(client, "FileSystem.CacheDirectory");
        StringAssert.Contains(client, "HttpCompletionOption.ResponseHeadersRead");
        StringAssert.Contains(client, "uri.AbsolutePath.StartsWith(\"/media/audio/\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(client, "return new Uri(cachePath).AbsoluteUri;");
    }

    [TestMethod]
    public void MobileStoryDetailOpensAndClosesWithoutSlowExtraWork()
    {
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var audioService = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "AudioPlaybackService.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var gratisPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "GratisPage.cs"));
        var homePage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "HomePage.cs"));

        StringAssert.Contains(storyDetail, "public void ApplyQueryAttributes(IDictionary<string, object> query)");
        StringAssert.Contains(storyDetail, "RenderPreview(_previewStory)");
        StringAssert.Contains(storyDetail, "await LoadAsync(showLoading: _previewStory is null, cancellationToken: _loadCts.Token)");
        StringAssert.Contains(storyDetail, "Shell.Current.GoToAsync(\"..\", animate: false)");
        StringAssert.Contains(storyDetail, "CancelActiveLoad();");
        StringAssert.Contains(storyDetail, "UnsubscribePlaybackEvents();");
        StringAssert.Contains(storyDetail, "TryStopAudioPlayback();");
        StringAssert.Contains(storyDetail, "if (cancellationToken.IsCancellationRequested || !_isPageActive)");
        StringAssert.Contains(storyDetail, "if (!_isPageActive)");
        StringAssert.Contains(storyDetail, "_isClosing");
        StringAssert.Contains(storyDetail, "BuildInlineLoadingState()");
        StringAssert.Contains(luisterPage, "animate: false");
        StringAssert.Contains(gratisPage, "animate: false");
        StringAssert.Contains(homePage, "animate: false");
        StringAssert.Contains(audioService, "_player?.Pause();");
        Assert.IsFalse(audioService.Contains("_player?.Seek(CoreMedia.CMTime.Zero)", StringComparison.Ordinal));
        StringAssert.Contains(program, "RelatedStories: Array.Empty<MobileStorySummaryResponse>()");
    }

    [TestMethod]
    public void MobileAuthFieldsRemoveNativeEntryChromeInsideRoundedBorders()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(accountPage, "entry.BackgroundColor = Colors.Transparent;");
        StringAssert.Contains(accountPage, "Content = entry");
        StringAssert.Contains(mauiProgram, "ConfigureEntryChrome();");
        StringAssert.Contains(mauiProgram, "EntryHandler.Mapper.AppendToMapping(\"SchinkPlainEntryChrome\"");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.Background = null;");
    }

    [TestMethod]
    public void MobileAccountPageAppliesCachedSessionBeforeRendering()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(accountPage, "var hasCachedSession = _sessionState.Current.IsSignedIn;");
        StringAssert.Contains(accountPage, "_signedInState = new VerticalStackLayout { Spacing = 0, IsVisible = hasCachedSession };");
        StringAssert.Contains(accountPage, "_signedOutState = new VerticalStackLayout { Spacing = 0, IsVisible = !hasCachedSession };");
        StringAssert.Contains(accountPage, "if (_signedOutState.Children.Count == 0)");
        StringAssert.Contains(accountPage, "_sessionState.Changed += _ => MainThread.BeginInvokeOnMainThread(ApplySessionState);");
        StringAssert.Contains(accountPage, "_signedInState.IsVisible = true;");
        StringAssert.Contains(accountPage, "_signedOutState.IsVisible = false;");
    }

    [TestMethod]
    public void MobileAppIconUsesGeneratedWordlessSchinkAsset()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var infoPlist = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Info.plist"));
        var iconPath = GetRepoPath("Shink.Mobile", "Resources", "AppIcon", "schink_appicon.png");
        var iconBytes = File.ReadAllBytes(iconPath);

        StringAssert.Contains(project, "<MauiIcon Include=\"Resources/AppIcon/schink_appicon.png\" />");
        StringAssert.Contains(infoPlist, "<key>XSAppIconAssets</key>");
        StringAssert.Contains(infoPlist, "<string>Assets.xcassets/schink_appicon.appiconset</string>");
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, iconBytes.Take(4).ToArray());
        Assert.IsTrue(iconBytes.Length > 100_000);
    }

    [TestMethod]
    public void MobileSplashScreenUsesGeneratedSchinkStoriesArtwork()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var splashPath = GetRepoPath("Shink.Mobile", "Resources", "Splash", "schink_stories_splash.png");
        var splashBytes = File.ReadAllBytes(splashPath);

        StringAssert.Contains(project, "<MauiSplashScreen Include=\"Resources/Splash/schink_stories_splash.png\" Color=\"#023333\" BaseSize=\"320,320\" />");
        Assert.IsFalse(project.Contains("<MauiSplashScreen Include=\"Resources/Splash/splash.svg\"", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, splashBytes.Take(4).ToArray());
        Assert.IsTrue(splashBytes.Length > 100_000);
    }

    private static string GetRepoPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(segments)} from {AppContext.BaseDirectory}.");
    }
}
