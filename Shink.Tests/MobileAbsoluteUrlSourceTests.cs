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
        Assert.IsFalse(luisterPage.Contains("Source = \"dis_storietyd.png\"", StringComparison.Ordinal));
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
        StringAssert.Contains(luisterPage, "_apiClient.BuildCachedImageSource(playlist.ArtworkUrl, \"schink_background.jpeg\")");
        Assert.IsFalse(luisterPage.Contains("firstStory?.ImageUrl", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("firstStory?.ThumbnailUrl", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("IsBundledPlaylistFallback", StringComparison.Ordinal));
        StringAssert.Contains(program, "ArtworkUrl: BuildMobilePlaylistArtworkUri(httpContext, playlist)");
        StringAssert.Contains(program, "playlist.ShowcaseImagePath");
        StringAssert.Contains(program, "playlist.PreferredStory?.ImagePath");
        StringAssert.Contains(program, "playlist.PreferredStory?.ThumbnailPath");
        StringAssert.Contains(program, "playlist.BackdropImagePath");
        StringAssert.Contains(program, "playlist.LogoImagePath");
    }

    [TestMethod]
    public void MobileLuisterUsesOrderedSectionsForSpeellysteParity()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));
        var program = File.ReadAllText(GetRepoPath("Shink", "Program.cs"));

        StringAssert.Contains(models, "public sealed record MobileLuisterSection(");
        StringAssert.Contains(program, "IsMobileSpeellysteSystemPlaylist");
        StringAssert.Contains(program, "item.Playlist.IncludeInSpeellysteCarousel");
        StringAssert.Contains(program, "MobileLuisterSectionKinds.Speellyste");
        StringAssert.Contains(program, ".OrderBy(section => section.SortOrder)");
        StringAssert.Contains(luisterPage, "FilterSections(_sections, _searchEntry.Text)");
        StringAssert.Contains(luisterPage, "IsSpeellysteSection(section)");
        StringAssert.Contains(luisterPage, "BuildPlaylistShowcase(section.Title, section.Playlists)");
        Assert.IsFalse(luisterPage.Contains("_playlistContent.Children.Add(BuildPlaylistShowcase(filteredPlaylists));", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterSearchDebouncesTypingAndMatchesStoryMetadata()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "_searchEntry.TextChanged += (_, _) => QueueSearchRender();");
        StringAssert.Contains(luisterPage, "private async Task DebounceSearchRenderAsync(CancellationToken cancellationToken)");
        StringAssert.Contains(luisterPage, "await Task.Delay(220, cancellationToken);");
        StringAssert.Contains(luisterPage, "MainThread.BeginInvokeOnMainThread(RenderPlaylistContent);");
        StringAssert.Contains(luisterPage, "StoryMatches(story, normalizedQuery)");
        StringAssert.Contains(luisterPage, "ContainsNormalized(story.Description, normalizedQuery)");
        StringAssert.Contains(luisterPage, "ContainsNormalized(story.Slug, normalizedQuery)");
        Assert.IsFalse(luisterPage.Contains("_searchEntry.TextChanged += (_, _) => RenderPlaylistContent();", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterShowcaseMatchesWebDatabaseFlagsAndPreferredStoryFallback()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));

        StringAssert.Contains(models, "bool? ShowShowcaseImageOnLuisterPage = null");
        StringAssert.Contains(luisterPage, "var showcaseStory = ResolvePlaylistShowcaseStory(playlist);");
        StringAssert.Contains(luisterPage, "if (showcaseStory is not null && ShouldShowPlaylistShowcase(playlist))");
        StringAssert.Contains(luisterPage, "playlist.ShowcaseStory ?? playlist.Stories.FirstOrDefault();");
        StringAssert.Contains(luisterPage, "playlist.ShowShowcaseImageOnLuisterPage == true;");
        Assert.IsFalse(luisterPage.Contains("return playlist.ShowcaseStory is not null || playlist.Stories.Count > 0;", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("if (playlist.ShowShowcaseImageOnLuisterPage is { } explicitValue)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterDoesNotShowSignedInAccountSummary()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "if (!_sessionState.Current.IsSignedIn)");
        StringAssert.Contains(luisterPage, "_content.Children.Add(BuildAccountPanel());");
        Assert.IsFalse(luisterPage.Contains("\"Alles oop\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("\"Gratis toegang\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("Text = session.Email ?? \"Ingeteken\"", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("if (session.IsSignedIn)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterTopBarMirrorsWebNotificationCenter()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var models = File.ReadAllText(GetRepoPath("Shink.Mobile", "Models", "MobileApiModels.cs"));

        StringAssert.Contains(luisterPage, "BuildNotificationButton()");
        StringAssert.Contains(luisterPage, "searchButton,");
        StringAssert.Contains(luisterPage, "notificationButton,");
        StringAssert.Contains(luisterPage, "profileButton");
        StringAssert.Contains(luisterPage, "_notificationPage?.UnreadCount");
        StringAssert.Contains(luisterPage, "FormatNotificationCount(unreadCount)");
        StringAssert.Contains(luisterPage, "private async Task ShowNotificationsAsync()");
        StringAssert.Contains(luisterPage, "await _apiClient.GetNotificationsAsync(");
        StringAssert.Contains(luisterPage, "await _apiClient.MarkAllNotificationsReadAsync()");
        StringAssert.Contains(luisterPage, "await _apiClient.ClearNotificationsAsync()");
        StringAssert.Contains(luisterPage, "await _apiClient.ClearNotificationAsync(notification.Id)");
        StringAssert.Contains(luisterPage, "await _apiClient.GetNotificationsAsync(before: before, history: _notificationPage.HasHistory");
        StringAssert.Contains(luisterPage, "Teken in om kennisgewings te sien.");
        StringAssert.Contains(luisterPage, "BuildNotificationCloseButton()");
        StringAssert.Contains(luisterPage, "Margin = new Thickness(0, -4, 0, 0)");
        StringAssert.Contains(luisterPage, "RowDefinitions =");
        StringAssert.Contains(luisterPage, "new RowDefinition { Height = GridLength.Star }");
        StringAssert.Contains(luisterPage, "Grid.SetRow(notificationScrollView, 2);");
        Assert.IsFalse(luisterPage.Contains("Content = new VerticalStackLayout\n            {\n                Padding = new Thickness(18, 18, 18, 28),\n                Spacing = 16,\n                Children =\n                {\n                    header,\n                    statusLabel,\n                    new ScrollView", StringComparison.Ordinal));

        StringAssert.Contains(models, "public sealed record MobileNotificationPage(");
        StringAssert.Contains(models, "public sealed record MobileNotificationItem(");
        StringAssert.Contains(models, "public sealed record MobileNotificationMutationResponse(");
        StringAssert.Contains(client, "GetNotificationsAsync(");
        StringAssert.Contains(client, "int limit = 10,");
        StringAssert.Contains(client, "DateTimeOffset? before = null,");
        StringAssert.Contains(client, "bool history = false,");
        StringAssert.Contains(client, "BuildNotificationRequestPath(limit, before, history)");
        StringAssert.Contains(client, "return $\"/api/notifications?{string.Join(\"&\", queryParts)}\";");
        StringAssert.Contains(client, "PostAsync<MobileNotificationMutationResponse>(\"/api/notifications/read-all\"");
        StringAssert.Contains(client, "PostAsync<MobileNotificationMutationResponse>(\"/api/notifications/clear\"");
        StringAssert.Contains(client, "$\"/api/notifications/{notificationId:D}/read\"");
        StringAssert.Contains(client, "$\"/api/notifications/{notificationId:D}/clear\"");
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
        StringAssert.Contains(luisterPage, "WidthRequest = 168");
        StringAssert.Contains(luisterPage, "StrokeShape = new RoundRectangle { CornerRadius = 16 }");
    }

    [TestMethod]
    public void MobileLuisterFavoriteHeartPersistsAndUpdatesVisibleStoryState()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "var isFavorite = await _apiClient.SetFavoriteAsync(story.Slug, story.Source, !story.IsFavorite);");
        StringAssert.Contains(luisterPage, "_favoriteRequestsInFlight.Add(favoriteKey)");
        StringAssert.Contains(luisterPage, "_favoriteRequestsInFlight.Remove(favoriteKey)");
        StringAssert.Contains(luisterPage, "IsFavoriteRequestInFlight(story)");
        StringAssert.Contains(luisterPage, "new ActivityIndicator");
        StringAssert.Contains(luisterPage, "IsRunning = true");
        StringAssert.Contains(luisterPage, "UpdateFavoriteState(story.Slug, isFavorite);");
        StringAssert.Contains(luisterPage, "RenderPlaylistContent();");
        StringAssert.Contains(luisterPage, "private void UpdateFavoriteState(string slug, bool isFavorite)");
        StringAssert.Contains(luisterPage, "playlist.ShowcaseStory is null ? null : UpdateStoryFavoriteState(playlist.ShowcaseStory, slug, isFavorite)");
        StringAssert.Contains(luisterPage, "story with { IsFavorite = isFavorite }");
        Assert.IsFalse(luisterPage.Contains("await LoadAsync();", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileLuisterStoryTitlesOpenPlayerWithPlaylistContext()
    {
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));

        StringAssert.Contains(luisterPage, "story => BuildLuisterStoryCarouselCard(playlist, story)");
        StringAssert.Contains(luisterPage, "private View BuildLuisterStoryCarouselCard(MobilePlaylist playlist, MobileStorySummary story)");
        StringAssert.Contains(luisterPage, "await OpenPlaylistStoryAsync(story, playlist);");
        StringAssert.Contains(luisterPage, "private async Task OpenPlaylistStoryAsync(MobileStorySummary story, MobilePlaylist playlist)");
        StringAssert.Contains(luisterPage, "await CapturePlayerTransitionBackdropAsync();");
        StringAssert.Contains(luisterPage, "private async Task CapturePlayerTransitionBackdropAsync()");
        StringAssert.Contains(luisterPage, "await _transitionBackdropState.CaptureAsync();");
        StringAssert.Contains(luisterPage, "[\"playlistTitle\"] = playlist.Title");
        StringAssert.Contains(luisterPage, "[\"playlistSlug\"] = playlist.Slug");
        Assert.IsFalse(luisterPage.Contains("story => BuildLuisterStoryCarouselCard(story)", StringComparison.Ordinal));
        Assert.IsFalse(luisterPage.Contains("tap.Tapped += async (_, _) => await OpenStoryAsync(story);", StringComparison.Ordinal));
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
        StringAssert.Contains(storyDetail, "private static double CoverArtHeight");
        StringAssert.Contains(storyDetail, "var height = ScreenHeight;");
        StringAssert.Contains(storyDetail, "_activeCatalogDuration = ResolveCatalogDuration(detail);");
        StringAssert.Contains(storyDetail, "_activeCatalogDuration is null ? \"--:--\" : FormatTime(_activeCatalogDuration.Value)");
        StringAssert.Contains(storyDetail, "private TimeSpan? ResolveCatalogDuration(MobileStoryDetailResponse detail) =>");
        StringAssert.Contains(storyDetail, "private decimal? ResolveCatalogDurationSeconds(MobileStoryDetailResponse detail)");
        StringAssert.Contains(storyDetail, "if (detail.Story.DurationSeconds is > 0)");
        StringAssert.Contains(storyDetail, "_previewStory is { DurationSeconds: > 0 } previewStory");
        StringAssert.Contains(storyDetail, "var playlistStory = _playlistStories.FirstOrDefault");
        StringAssert.Contains(storyDetail, "var duration = _audioPlaybackService.Duration ?? _activeCatalogDuration;");
        StringAssert.Contains(storyDetail, "var durationSeconds = NormalizeTrackingSeconds(duration?.TotalSeconds);");
        StringAssert.Contains(storyDetail, "EnsureCatalogDurationVisibleAsync(detail);");
        StringAssert.Contains(storyDetail, "private void EnsureCatalogDurationVisibleAsync(MobileStoryDetailResponse detail)");
        StringAssert.Contains(storyDetail, "var audioUrl = _apiClient.BuildAbsoluteUrl(detail.AudioUrl);");
        StringAssert.Contains(storyDetail, "var duration = await _audioPlaybackService.GetDurationAsync(audioUrl, cancellationToken);");
        StringAssert.Contains(storyDetail, "PrepareAudioPlaybackSourceAsync(");
        StringAssert.Contains(storyDetail, "DownloadAudioForPlaybackAsync(");
        Assert.IsFalse(storyDetail.Contains("Gereed om te luister", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("Onderbreek", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("Besig om te speel", StringComparison.Ordinal));
        StringAssert.Contains(audioService, "TimeSpan CurrentPosition");
        StringAssert.Contains(audioService, "TimeSpan? Duration");
        StringAssert.Contains(audioService, "Task<TimeSpan?> GetDurationAsync(string audioUrl, CancellationToken cancellationToken = default);");
        StringAssert.Contains(audioService, "public async Task<TimeSpan?> GetDurationAsync(string audioUrl, CancellationToken cancellationToken = default)");
        StringAssert.Contains(audioService, "probePlayer = new AVFoundation.AVPlayer(playerItem);");
        StringAssert.Contains(audioService, "WaitUntilReadyToPlayAsync(playerItem)");
        StringAssert.Contains(audioService, "WaitUntilReadyToPlayAsync(playerItem, cancellationToken)");
        StringAssert.Contains(audioService, "AVFoundation.AVPlayerItemStatus.ReadyToPlay");
        StringAssert.Contains(audioService, "AVFoundation.AVPlayerItemStatus.Failed");
        StringAssert.Contains(audioService, "AVFoundation.AVAudioSessionCategory.Playback");
        StringAssert.Contains(audioService, "MPRemoteCommandCenter.Shared");
        StringAssert.Contains(audioService, "MPNowPlayingInfoCenter.DefaultCenter.NowPlaying");
        StringAssert.Contains(audioService, "MediaPlayer.MPMediaItemArtwork");
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
    public void MobileStoryDetailAnimatesCloseBeforeFastShellPop()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(storyDetail, "PrepareCloseBackdrop();");
        StringAssert.Contains(storyDetail, "private void PrepareCloseBackdrop()");
        StringAssert.Contains(storyDetail, "_closeBackdrop = new Image");
        StringAssert.Contains(storyDetail, "_playerSurface = new Grid");
        StringAssert.Contains(storyDetail, "_closeBackdrop.Margin = ResolveBackdropMargin();");
        StringAssert.Contains(storyDetail, "private Thickness ResolveBackdropMargin()");
        StringAssert.Contains(storyDetail, "var safeAreaInsets = iOSPage.GetSafeAreaInsets(this);");
        StringAssert.Contains(storyDetail, "var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;");
        StringAssert.Contains(storyDetail, "var systemBarInsets = insets.GetInsets(WindowInsets.Type.SystemBars());");
        StringAssert.Contains(storyDetail, "_closeBackdrop.IsVisible = true;");
        StringAssert.Contains(storyDetail, "await AnimateCloseAsync();");
        StringAssert.Contains(storyDetail, "private async Task AnimateCloseAsync()");
        StringAssert.Contains(storyDetail, "private const uint CloseAnimationDurationMs = 170;");
        StringAssert.Contains(storyDetail, "var closeDistance = Height > 0");
        StringAssert.Contains(storyDetail, "? Height + 40");
        StringAssert.Contains(storyDetail, "_playerSurface.TranslateToAsync(0, closeDistance, CloseAnimationDurationMs, Easing.CubicIn)");
        Assert.IsFalse(storyDetail.Contains("_content.FadeToAsync", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("_root.FadeToAsync", StringComparison.Ordinal));
        StringAssert.Contains(storyDetail, "await Shell.Current.GoToAsync(\"..\", animate: false);");
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<PlayerTransitionBackdropState>();");
    }

    [TestMethod]
    public void MobileStoryDetailUsesCleanPlayerChromeWithoutQueueHintOrSaveSharePills()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(storyDetail, "BuildDownCaretButton()");
        StringAssert.Contains(storyDetail, "DownCaretDrawable");
        StringAssert.Contains(storyDetail, "CastIconDrawable");
        StringAssert.Contains(storyDetail, "BuildFavoriteOverlay(detail)");
        StringAssert.Contains(storyDetail, "Text = detail.Story.IsFavorite ? \"♥\" : \"♡\"");
        StringAssert.Contains(storyDetail, "BuildInfoPillButton()");
        StringAssert.Contains(storyDetail, "Drawable = new InfoIconDrawable()");
        StringAssert.Contains(storyDetail, "Text = \"Info\"");
        StringAssert.Contains(storyDetail, "await ToggleFavoriteAsync(detail);");
        Assert.IsFalse(storyDetail.Contains("BuildQueueHint()", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("\"Jou ry\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("\"☰+  Stoor\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("\"↗\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("BuildTopIconButton(\"⌄\")", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("Gunsteling\")", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileStoryDetailCoverArtCanOpenFullscreenImage()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));
        var orientationService = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "OrientationService.cs"));
        var infoPlist = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "Info.plist"));
        var appDelegate = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "iOS", "AppDelegate.cs"));

        StringAssert.Contains(storyDetail, "BuildFullscreenCoverButton()");
        StringAssert.Contains(storyDetail, "FullscreenIconDrawable");
        StringAssert.Contains(storyDetail, "HorizontalOptions = LayoutOptions.End");
        StringAssert.Contains(storyDetail, "VerticalOptions = LayoutOptions.End");
        StringAssert.Contains(storyDetail, "await ShowFullscreenCoverAsync(detail);");
        StringAssert.Contains(storyDetail, "private async Task ShowFullscreenCoverAsync(MobileStoryDetailResponse detail)");
        StringAssert.Contains(storyDetail, "Navigation.PushModalAsync(fullscreenPage, true)");
        StringAssert.Contains(storyDetail, "Aspect = Aspect.AspectFit");
        StringAssert.Contains(storyDetail, "fullscreenImageTap.Tapped += (_, _) => _ = ToggleFullscreenPlaybackAsync(detail);");
        StringAssert.Contains(storyDetail, "await Navigation.PopModalAsync(true)");
        StringAssert.Contains(storyDetail, "Padding = new Thickness(8)");
        StringAssert.Contains(storyDetail, "new ColumnDefinition(GridLength.Star)");
        StringAssert.Contains(storyDetail, "new ColumnDefinition(GridLength.Auto)");
        StringAssert.Contains(storyDetail, "BuildFullscreenMediaControls(detail)");
        StringAssert.Contains(storyDetail, "HeightRequest = 4");
        StringAssert.Contains(storyDetail, "BuildFullscreenTransportControls(detail, playButton)");
        StringAssert.Contains(storyDetail, "private async Task ToggleFullscreenPlaybackAsync(MobileStoryDetailResponse detail)");
        StringAssert.Contains(storyDetail, "BuildCompactPlaybackButton(playButton.Text)");
        StringAssert.Contains(storyDetail, "BuildCompactTransportButton(\"|‹\")");
        StringAssert.Contains(storyDetail, "RestoreFullscreenPlaybackUi(detail);");
        StringAssert.Contains(storyDetail, "IOrientationService _orientationService");
        StringAssert.Contains(storyDetail, "_orientationService.RequestLandscape();");
        StringAssert.Contains(storyDetail, "_orientationService.RequestPortrait();");
        StringAssert.Contains(storyDetail, "DeviceDisplay.Current.KeepScreenOn = true;");
        StringAssert.Contains(storyDetail, "DeviceDisplay.Current.KeepScreenOn = _wasKeepScreenOnBeforeFullscreen;");
        StringAssert.Contains(storyDetail, "fullscreenPage.Disappearing += (_, _) =>");
        StringAssert.Contains(storyDetail, "RestoreFullscreenCoverDeviceState();");
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<IOrientationService, OrientationService>();");
        StringAssert.Contains(orientationService, "public interface IOrientationService");
        StringAssert.Contains(orientationService, "RequestLandscape()");
        StringAssert.Contains(orientationService, "RequestPortrait()");
        StringAssert.Contains(infoPlist, "UIInterfaceOrientationLandscapeLeft");
        StringAssert.Contains(infoPlist, "UIInterfaceOrientationLandscapeRight");
        StringAssert.Contains(appDelegate, "GetSupportedInterfaceOrientations");
        StringAssert.Contains(appDelegate, "OrientationService.CurrentIosOrientationMask");
    }

    [TestMethod]
    public void MobileStoryDetailPlayerButtonsUseRealActionsOnly()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(storyDetail, "menuButton.Clicked += async (_, _) => await ShowPlayerMenuAsync();");
        StringAssert.Contains(storyDetail, "await Share.Default.RequestAsync(new ShareTextRequest");
        StringAssert.Contains(storyDetail, "titleRow.GestureRecognizers.Add(tap);");
        StringAssert.Contains(storyDetail, "private MobileStorySummary? ResolvePreviousStory(");
        StringAssert.Contains(storyDetail, "private MobileStorySummary? ResolveNextStory(");
        StringAssert.Contains(storyDetail, "await OpenPlaylistStoryAsync(previousStory);");
        StringAssert.Contains(storyDetail, "await OpenPlaylistStoryAsync(nextStory);");
        Assert.IsFalse(storyDetail.Contains("var shuffleButton = BuildTransportButton", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("var repeatButton = BuildTransportButton", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("grid.Children.Add(shuffleButton)", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("grid.Children.Add(repeatButton)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileStoryDetailPlaylistQueueReplacesCurrentStoryInPlace()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(storyDetail, "private async Task ReplaceActiveStoryAsync(MobileStorySummary story)");
        StringAssert.Contains(storyDetail, "await ReplaceActiveStoryAsync(story);");
        StringAssert.Contains(storyDetail, "StorySlug = story.Slug;");
        StringAssert.Contains(storyDetail, "Source = story.Source;");
        StringAssert.Contains(storyDetail, "RenderPreview(story);");
        StringAssert.Contains(storyDetail, "await LoadAsync(showLoading: false, cancellationToken: _loadCts.Token);");

        var methodStart = storyDetail.IndexOf("private async Task OpenPlaylistStoryAsync", StringComparison.Ordinal);
        Assert.IsTrue(methodStart >= 0);
        var methodEnd = storyDetail.IndexOf("\n    private sealed class", methodStart, StringComparison.Ordinal);
        Assert.IsTrue(methodEnd > methodStart);
        var playlistMethod = storyDetail[methodStart..methodEnd];

        Assert.IsFalse(playlistMethod.Contains("Shell.Current.GoToAsync", StringComparison.Ordinal));
        Assert.IsFalse(playlistMethod.Contains("StoryDetailPage?slug=", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileStoryDetailCastSheetUsesPlatformControlsAndSwipeDismiss()
    {
        var storyDetail = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));

        StringAssert.Contains(storyDetail, "if (IsNativeRoutePickerAvailable)");
        StringAssert.Contains(storyDetail, "private static bool IsNativeRoutePickerAvailable");
        StringAssert.Contains(storyDetail, "DeviceInfo.Platform == DevicePlatform.iOS");
        StringAssert.Contains(storyDetail, "DeviceInfo.Platform == DevicePlatform.MacCatalyst");
        StringAssert.Contains(storyDetail, "BuildCastAvailableControlsHeader()");
        StringAssert.Contains(storyDetail, "new SwipeGestureRecognizer { Direction = SwipeDirection.Down }");
        StringAssert.Contains(storyDetail, "swipeDown.Swiped += (_, _) => DismissCastPicker();");
        StringAssert.Contains(storyDetail, "\"AirPlay and Bluetooth devices\"");
        Assert.IsFalse(storyDetail.Contains("\"Living Room Speaker\"", StringComparison.Ordinal));
        Assert.IsFalse(storyDetail.Contains("BuildCastAllDevicesHeader()", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileAuthFieldsRemoveNativeEntryChromeInsideRoundedBorders()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));
        var luisterPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "LuisterPage.cs"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));

        StringAssert.Contains(accountPage, "entry.BackgroundColor = Colors.Transparent;");
        StringAssert.Contains(luisterPage, "Shell.SetNavBarIsVisible(this, false);");
        StringAssert.Contains(accountPage, "Content = entry");
        StringAssert.Contains(mauiProgram, "ConfigureEntryChrome();");
        StringAssert.Contains(mauiProgram, "EntryHandler.Mapper.AppendToMapping(\"SchinkPlainEntryChrome\"");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;");
        StringAssert.Contains(mauiProgram, "handler.PlatformView.Background = null;");
    }

    [TestMethod]
    public void MobileAuthFormModesCenterPanelOnScreen()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(accountPage, "_authPanelTopSpacer");
        StringAssert.Contains(accountPage, "protected override void OnSizeAllocated(double width, double height)");
        StringAssert.Contains(accountPage, "UpdateAuthPanelTopSpacer();");
        StringAssert.Contains(accountPage, "_signedOutState.Children.Add(_authPanelTopSpacer);");
        StringAssert.Contains(accountPage, "if (_authPanelMode == AuthPanelMode.Landing)");
        StringAssert.Contains(accountPage, "var estimatedPanelHeight = _authPanelMode == AuthPanelMode.SignIn ? 330 : 620;");
        StringAssert.Contains(accountPage, "Math.Floor((screenHeight - estimatedPanelHeight) / 2)");
        StringAssert.Contains(accountPage, "_authPanelTopSpacer.IsVisible = true;");
    }

    [TestMethod]
    public void MobileAuthFormModesHaveBackButtonToLanding()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(accountPage, "BuildAuthPanelHeader(");
        StringAssert.Contains(accountPage, "Source = \"auth_caret_dark_rendered.png\"");
        StringAssert.Contains(accountPage, "Rotation = 180");
        StringAssert.Contains(accountPage, "WidthRequest = 38");
        StringAssert.Contains(accountPage, "new ColumnDefinition { Width = 38 }");
        StringAssert.Contains(accountPage, "SetAuthPanelMode(AuthPanelMode.Landing);");
        Assert.IsFalse(accountPage.Contains("BuildAuthPanelHeading(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MobileWelcomeScreenUsesResponsiveLandingMetrics()
    {
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));

        StringAssert.Contains(accountPage, "private sealed record LandingLayoutMetrics(");
        StringAssert.Contains(accountPage, "GetLandingLayoutMetrics()");
        StringAssert.Contains(accountPage, "var compact = height < 740;");
        StringAssert.Contains(accountPage, "var tight = height < 680;");
        StringAssert.Contains(accountPage, "LogoHeight: Math.Clamp(height * (tight ? 0.18 : 0.2), 112, 182)");
        StringAssert.Contains(accountPage, "CharacterHeight: Math.Clamp(height * (tight ? 0.12 : 0.17), 76, 158)");
        StringAssert.Contains(accountPage, "ModeButtonHeight: tight ? 64 : compact ? 70 : 78");
        StringAssert.Contains(accountPage, "_authPanelContent.Spacing = _authPanelMode == AuthPanelMode.Landing");
        StringAssert.Contains(accountPage, "ApplyLandingLayoutMetrics();");
    }

    [TestMethod]
    public void MobileWelcomeLogoKeepsTransparentBackground()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var accountPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "AccountPage.cs"));
        var logoBytes = File.ReadAllBytes(GetRepoPath("Shink.Mobile", "Resources", "Images", "schink_stories_logo_white.png"));

        StringAssert.Contains(project, "<MauiImage Update=\"Resources/Images/schink_stories_logo_white.png\" Resize=\"False\" />");
        StringAssert.Contains(accountPage, "Source = \"schink_stories_logo_white.png\"");
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, logoBytes.Take(4).ToArray());
        Assert.AreEqual(6, logoBytes[25]);
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
        var androidManifest = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "AndroidManifest.xml"));
        var androidMainActivity = File.ReadAllText(GetRepoPath("Shink.Mobile", "Platforms", "Android", "MainActivity.cs"));
        var iconPath = GetRepoPath("Shink.Mobile", "Resources", "AppIcon", "schink_appicon.png");
        var iconBytes = File.ReadAllBytes(iconPath);

        StringAssert.Contains(project, "<MauiIcon Include=\"Resources/AppIcon/schink_appicon.png\" />");
        StringAssert.Contains(infoPlist, "<key>XSAppIconAssets</key>");
        StringAssert.Contains(infoPlist, "<string>Assets.xcassets/schink_appicon.appiconset</string>");
        StringAssert.Contains(androidManifest, "android:icon=\"@mipmap/schink_appicon\"");
        StringAssert.Contains(androidManifest, "android:roundIcon=\"@mipmap/schink_appicon_round\"");
        StringAssert.Contains(androidMainActivity, "Icon = \"@mipmap/schink_appicon\"");
        StringAssert.Contains(androidMainActivity, "RoundIcon = \"@mipmap/schink_appicon_round\"");
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, iconBytes.Take(4).ToArray());
        Assert.IsTrue(iconBytes.Length > 100_000);
    }

    [TestMethod]
    public void MobileSplashScreenUsesGeneratedSchinkStoriesArtwork()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var splashPath = GetRepoPath("Shink.Mobile", "Resources", "Splash", "schink_stories_logo_white.png");
        var splashBytes = File.ReadAllBytes(splashPath);

        StringAssert.Contains(project, "<MauiSplashScreen Include=\"Resources/Splash/schink_stories_logo_white.png\" Color=\"#023333\" BaseSize=\"320,140\" />");
        Assert.IsFalse(project.Contains("<MauiSplashScreen Include=\"Resources/Splash/splash.svg\"", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, splashBytes.Take(4).ToArray());
        Assert.IsTrue(splashBytes.Length > 20_000);
    }

    [TestMethod]
    public void MobileVisibleBrandCopyUsesSchinkSpelling()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var mainPage = File.ReadAllText(GetRepoPath("Shink.Mobile", "MainPage.xaml"));

        StringAssert.Contains(project, "<ApplicationTitle>Schink Stories</ApplicationTitle>");
        StringAssert.Contains(mainPage, "Text=\"Schink Stories\"");
        StringAssert.Contains(mainPage, "bestaande Schink Stories dienste");
        Assert.IsFalse(project.Contains(">Shink Stories<", StringComparison.Ordinal));
        Assert.IsFalse(mainPage.Contains("Shink Stories", StringComparison.Ordinal));
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
