using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public sealed class MobilePersistentPlaybackSourceTests
{
    [TestMethod]
    public void StoryPlaybackSessionIsAppWideAndSurvivesPageNavigation()
    {
        var mauiProgram = ReadSource("Shink.Mobile", "MauiProgram.cs");
        var storyDetail = ReadSource("Shink.Mobile", "Pages", "StoryDetailPage.cs");
        var playlistDetail = ReadSource("Shink.Mobile", "Pages", "PlaylistDetailPage.cs");
        var playbackSession = ReadSource("Shink.Mobile", "Services", "StoryPlaybackSession.cs");

        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<StoryPlaybackSession>();");
        StringAssert.Contains(storyDetail, "_storyPlaybackSession.NotifyPageHidden();");
        StringAssert.Contains(playlistDetail, "_storyPlaybackSession.NotifyPageHidden();");
        StringAssert.Contains(playbackSession, "public sealed class StoryPlaybackSession");
        StringAssert.Contains(playbackSession, "public StoryPlaybackItem? Current => _current;");
        StringAssert.Contains(playbackSession, "public async Task ResumeAsync()");

        var storyDisappearing = ExtractMethod(storyDetail, "protected override void OnDisappearing()", "private void SubscribeDownloadEvents()");
        var playlistDisappearing = ExtractMethod(playlistDetail, "protected override void OnDisappearing()", "private void SetPlaylist(");
        Assert.DoesNotContain(".Stop();", storyDisappearing, StringComparison.Ordinal);
        Assert.DoesNotContain(".Stop();", playlistDisappearing, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PersistentNowPlayingControlsCoverMainAndKarakterGameRoutes()
    {
        var nowPlaying = ReadSource("Shink.Mobile", "Pages", "PersistentNowPlayingBar.cs");
        var playlistDetail = ReadSource("Shink.Mobile", "Pages", "PlaylistDetailPage.cs");
        var storyDetail = ReadSource("Shink.Mobile", "Pages", "StoryDetailPage.cs");
        var luister = ReadSource("Shink.Mobile", "Pages", "LuisterPage.cs");
        var search = ReadSource("Shink.Mobile", "Pages", "SearchPage.cs");
        var karakters = ReadSource("Shink.Mobile", "Pages", "KaraktersPage.cs");
        var pareConfig = ReadSource("Shink.Mobile", "Pages", "KarakterPareConfigPage.cs");
        var pareGame = ReadSource("Shink.Mobile", "Pages", "KarakterPareGamePage.cs");
        var raaiConfig = ReadSource("Shink.Mobile", "Pages", "KarakterRaaiConfigPage.cs");
        var raaiGame = ReadSource("Shink.Mobile", "Pages", "KarakterRaaiGamePage.cs");
        var account = ReadSource("Shink.Mobile", "Pages", "AccountPage.cs");
        var profile = ReadSource("Shink.Mobile", "Pages", "ProfilePage.cs");
        var settings = ReadSource("Shink.Mobile", "Pages", "SettingsPage.cs");

        StringAssert.Contains(nowPlaying, "AutomationId = \"persistent-now-playing\"");
        StringAssert.Contains(nowPlaying, "Text = \"Nou speel\"");
        StringAssert.Contains(nowPlaying, "private const string PlayIconGlyph = \"\\uf04b\";");
        StringAssert.Contains(nowPlaying, "private const string PauseIconGlyph = \"\\uf04c\";");
        StringAssert.Contains(nowPlaying, "_playPauseButton.FontFamily = \"FontAwesomeSolid\";");
        StringAssert.Contains(nowPlaying, "_playPauseButton.Text = _playbackSession.IsPlaying ? PauseIconGlyph : PlayIconGlyph;");
        Assert.DoesNotContain("_playPauseButton.Text = _playbackSession.IsPlaying ? \"II\" : \"▶\";", nowPlaying, StringComparison.Ordinal);
        StringAssert.Contains(
            nowPlaying,
            "LineBreakMode = LineBreakMode.NoWrap,\n            VerticalOptions = LayoutOptions.End,\n            VerticalTextAlignment = TextAlignment.End");
        StringAssert.Contains(
            nowPlaying,
            "LineBreakMode = LineBreakMode.TailTruncation,\n            VerticalOptions = LayoutOptions.Start,\n            VerticalTextAlignment = TextAlignment.Start");
        StringAssert.Contains(nowPlaying, "HeightRequest = 52");
        StringAssert.Contains(nowPlaying, "MinimumHeightRequest = 52");
        StringAssert.Contains(nowPlaying, "Grid.SetRow(_titleLabel, 1);");
        StringAssert.Contains(
            nowPlaying,
            "RowDefinitions =\n            {\n                new RowDefinition(GridLength.Star),\n                new RowDefinition(GridLength.Star)\n            },");
        StringAssert.Contains(nowPlaying, "await _playbackSession.ResumeAsync();");
        StringAssert.Contains(nowPlaying, "_playbackSession.Stop();");
        StringAssert.Contains(nowPlaying, "nameof(StoryDetailPage)");
        StringAssert.Contains(nowPlaying, "current.OriginPlaylist is { } playlist");
        StringAssert.Contains(nowPlaying, "nameof(PlaylistDetailPage)");
        StringAssert.Contains(nowPlaying, "[\"playlist\"] = playlist");
        StringAssert.Contains(playlistDetail, "originPlaylist: _playlist");
        StringAssert.Contains(storyDetail, "originPlaylist: ResolveOriginPlaylist()");
        StringAssert.Contains(luister, "new PersistentNowPlayingBar(_storyPlaybackSession)");
        StringAssert.Contains(search, "new PersistentNowPlayingBar(_storyPlaybackSession)");
        StringAssert.Contains(karakters, "new PersistentNowPlayingBar(_storyPlaybackSession)");
        StringAssert.Contains(pareConfig, "PersistentPlaybackHost.Wrap(root, storyPlaybackSession)");
        StringAssert.Contains(pareGame, "PersistentPlaybackHost.Wrap(root, storyPlaybackSession, edgeToEdge: true)");
        StringAssert.Contains(raaiConfig, "PersistentPlaybackHost.Wrap(root, storyPlaybackSession)");
        StringAssert.Contains(raaiGame, "PersistentPlaybackHost.Wrap(root, storyPlaybackSession, edgeToEdge: true)");
        StringAssert.Contains(account, "PersistentPlaybackHost.Wrap(pageContent, storyPlaybackSession, edgeToEdge: true)");
        StringAssert.Contains(profile, "PersistentPlaybackHost.Wrap(profileScroll, storyPlaybackSession)");
        StringAssert.Contains(settings, "PersistentPlaybackHost.Wrap(scrollView, storyPlaybackSession)");
    }

    [TestMethod]
    public void PlaylistAutoplayIsPreparedAndAdvancedByTheAppWideSession()
    {
        var playbackSession = ReadSource("Shink.Mobile", "Services", "StoryPlaybackSession.cs");
        var storyDetail = ReadSource("Shink.Mobile", "Pages", "StoryDetailPage.cs");
        var playlistDetail = ReadSource("Shink.Mobile", "Pages", "PlaylistDetailPage.cs");
        var playlistState = ReadSource("Shink.Mobile", "Services", "PlaylistPlaybackState.cs");
        var program = ReadSource("Shink", "Program.cs");

        StringAssert.Contains(playbackSession, "ScheduleAutoplayPreparation(playbackItem);");
        StringAssert.Contains(playbackSession, "await _apiClient.GetStoryAsync(nextStory.Slug, \"luister\", cancellationToken);");
        StringAssert.Contains(playbackSession, "await _audioPlaybackService.PrepareAsync(playbackUrl, cancellationToken);");
        StringAssert.Contains(playbackSession, "_ = AdvanceAutoplayAsync(endedItem);");
        StringAssert.Contains(playbackSession, "prepared is null && !_lifecycleService.IsBackgrounded");
        StringAssert.Contains(playbackSession, "RaiseAutoplayAdvanced(prepared.Detail, prepared.Playlist);");
        StringAssert.Contains(storyDetail, "_storyPlaybackSession.AutoplayAdvanced += OnAutoplayAdvanced;");
        StringAssert.Contains(playlistDetail, "_storyPlaybackSession.AutoplayAdvanced += OnAutoplayAdvanced;");
        Assert.DoesNotContain("_playlistPlaybackState.CanAutoplayAdvance(currentDetail.Story)", storyDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("await SelectRelativeStoryAsync(1, autoplay: true);", playlistDetail, StringComparison.Ordinal);
        StringAssert.Contains(playlistState, "story with { Source = \"luister\" }");
        StringAssert.Contains(program, "storyMediaStorageService.CreateAudioReadUrlAsync(");
        StringAssert.Contains(program, "TimeSpan.FromHours(4)");
    }

    [TestMethod]
    public void DownloadedPlayAllBuildsAnOfflineOnlyAutoplayQueue()
    {
        var downloadedPage = ReadSource("Shink.Mobile", "Pages", "DownloadedPage.cs");
        var playbackSession = ReadSource("Shink.Mobile", "Services", "StoryPlaybackSession.cs");
        var playlistState = ReadSource("Shink.Mobile", "Services", "PlaylistPlaybackState.cs");

        StringAssert.Contains(downloadedPage, "AutomationId = \"downloads-play-all\"");
        StringAssert.Contains(downloadedPage, "Text = \"▶  Speel alles\"");
        StringAssert.Contains(downloadedPage, "_playlistPlaybackState.SetOfflineQueue(playlist, firstStory);");
        StringAssert.Contains(downloadedPage, "_playlistPlaybackState.SetAutoplay(true);");
        StringAssert.Contains(downloadedPage, "ResolvePlayableAudioAsync(firstDetail)");
        StringAssert.Contains(downloadedPage, "originPlaylist: null");

        StringAssert.Contains(playlistState, "public bool IsOfflineQueue { get; private set; }");
        StringAssert.Contains(playlistState, "public void SetOfflineQueue(");
        StringAssert.Contains(playlistState, "preserveSource: isOfflineQueue");
        StringAssert.Contains(playlistState, "preserveSource: IsOfflineQueue");

        StringAssert.Contains(playbackSession, "if (_playlistPlaybackState.IsOfflineQueue)");
        StringAssert.Contains(playbackSession, "nextStory.Source,");
        StringAssert.Contains(playbackSession, "_playlistPlaybackState.IsOfflineQueue && string.IsNullOrWhiteSpace(offlinePlaybackUrl)");
        StringAssert.Contains(playbackSession, "_playlistPlaybackState.IsOfflineQueue ? null : prepared.Playlist");
    }

    [TestMethod]
    public void DownloadedContentStartsBelowTheFloatingNavbarSafeArea()
    {
        var downloadedPage = ReadSource("Shink.Mobile", "Pages", "DownloadedPage.cs");

        StringAssert.Contains(
            downloadedPage,
            "var scrollView = new ScrollView\n        {\n            // The floating navbar already occupies the top safe-area band. Keep\n            // the scrollable header below that band while the page artwork stays edge-to-edge.\n            SafeAreaEdges = new SafeAreaEdges(\n                SafeAreaRegions.None,\n                SafeAreaRegions.Container,\n                SafeAreaRegions.None,\n                SafeAreaRegions.None),");
        StringAssert.Contains(downloadedPage, "SafeAreaEdges = SafeAreaEdges.None;\n        Shell.SetNavBarIsVisible(this, false);");
    }

    [TestMethod]
    public void StoryAndPlaylistPlayButtonsAnimateWhilePlaybackStarts()
    {
        var storyDetail = ReadSource("Shink.Mobile", "Pages", "StoryDetailPage.cs");
        var playlistDetail = ReadSource("Shink.Mobile", "Pages", "PlaylistDetailPage.cs");

        StringAssert.Contains(storyDetail, "private async Task RunPlaybackRequestAsync(Button playButton, Func<Task> action)");
        StringAssert.Contains(storyDetail, "var loadingIndicator = new ActivityIndicator");
        StringAssert.Contains(storyDetail, "loadingState.Indicator.IsRunning = isLoading;");
        StringAssert.Contains(storyDetail, "await RunPlaybackRequestAsync(playButton, () => StartPlaybackAsync(detail, playButton));");
        StringAssert.Contains(playlistDetail, "_playLoadingIndicator = new ActivityIndicator");
        StringAssert.Contains(playlistDetail, "_playLoadingIndicator.IsRunning = isLoading;");
        StringAssert.Contains(playlistDetail, "await LoadCurrentStoryAsync(autoplay: true);");
        StringAssert.Contains(playlistDetail, "_playButton.TextColor = isLoading ? Colors.Transparent : Colors.White;");
        StringAssert.Contains(playlistDetail, "actionLoadingIndicator.SetBinding(ActivityIndicator.IsRunningProperty, nameof(PlaylistTrackItem.IsLoading));");
        StringAssert.Contains(playlistDetail, "loadingTrack?.SetLoading(true);");
        StringAssert.Contains(playlistDetail, "loadingTrack?.SetLoading(false);");
    }

    [TestMethod]
    public void StoryAndPlaylistProgressSlidersSeekTheSharedPlaybackSession()
    {
        var audioPlayback = ReadSource("Shink.Mobile", "Services", "AudioPlaybackService.cs");
        var playbackSession = ReadSource("Shink.Mobile", "Services", "StoryPlaybackSession.cs");
        var storyDetail = ReadSource("Shink.Mobile", "Pages", "StoryDetailPage.cs");
        var playlistDetail = ReadSource("Shink.Mobile", "Pages", "PlaylistDetailPage.cs");

        StringAssert.Contains(audioPlayback, "Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);");
        StringAssert.Contains(audioPlayback, "player.SeekAsync(CoreMedia.CMTime.FromSeconds(targetSeconds, 600))");
        StringAssert.Contains(audioPlayback, "player.SeekComplete += seekCompletedHandler;");
        StringAssert.Contains(audioPlayback, "Android.Media.MediaPlayerSeekMode.Closest");
        StringAssert.Contains(playbackSession, "FlushPendingListen(\"seek\", force: true);");
        StringAssert.Contains(playbackSession, "await _audioPlaybackService.SeekAsync(target, cancellationToken);");
        StringAssert.Contains(playbackSession, "_lastTrackedPosition = target;");
        StringAssert.Contains(storyDetail, "private Slider BuildProgressSlider(double value, Color maximumTrackColor)");
        StringAssert.Contains(storyDetail, "slider.DragCompleted += async (_, _) =>");
        StringAssert.Contains(storyDetail, "timelineTap.Tapped += async (_, args) => await SeekFromTimelineTapAsync(slider, args);");
        StringAssert.Contains(storyDetail, "AutomationId = \"story-progress-slider\"");
        StringAssert.Contains(storyDetail, "await _storyPlaybackSession.SeekAsync(");
        StringAssert.Contains(playlistDetail, "_progressSlider = new Slider");
        StringAssert.Contains(playlistDetail, "_progressSlider.DragCompleted += async (_, _) => await CompleteProgressSeekAsync();");
        StringAssert.Contains(playlistDetail, "timelineTap.Tapped += async (_, args) => await SeekFromTimelineTapAsync(args);");
        StringAssert.Contains(playlistDetail, "AutomationId = \"playlist-progress-slider\"");
        StringAssert.Contains(playlistDetail, "await _storyPlaybackSession.SeekAsync(");
        Assert.DoesNotContain("private ProgressBar? _activeProgressBar;", storyDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("private ProgressBar? _progressBar;", playlistDetail, StringComparison.Ordinal);
    }

    [TestMethod]
    public void CharacterPreviewExplicitlyReplacesTheActiveStory()
    {
        var karakters = ReadSource("Shink.Mobile", "Pages", "KaraktersPage.cs");
        var method = ExtractMethod(
            karakters,
            "private async Task PlayCharacterAudioAsync(MobileCharacterCard character)",
            "private async Task OpenPrimaryStoryAsync(");

        var stopIndex = method.IndexOf("_storyPlaybackSession.Stop();", StringComparison.Ordinal);
        var playIndex = method.IndexOf("_audioPlaybackService.PlayAsync(", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, stopIndex);
        Assert.IsGreaterThan(stopIndex, playIndex);
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);
        return source[start..end];
    }

    private static string ReadSource(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        return File.ReadAllText(Path.GetFullPath(Path.Combine(new[] { testsDirectory, ".." }.Concat(segments).ToArray())));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
