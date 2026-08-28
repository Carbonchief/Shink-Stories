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
            "LineBreakMode = LineBreakMode.NoWrap,\n            VerticalOptions = LayoutOptions.Center,\n            VerticalTextAlignment = TextAlignment.Center");
        StringAssert.Contains(
            nowPlaying,
            "LineBreakMode = LineBreakMode.TailTruncation,\n            VerticalOptions = LayoutOptions.Center,\n            VerticalTextAlignment = TextAlignment.Center");
        StringAssert.Contains(nowPlaying, "new RowDefinition(GridLength.Auto)");
        StringAssert.Contains(nowPlaying, "HeightRequest = 52");
        StringAssert.Contains(nowPlaying, "MinimumHeightRequest = 52");
        StringAssert.Contains(nowPlaying, "Grid.SetRow(_statusLabel, 1);");
        StringAssert.Contains(nowPlaying, "Grid.SetRow(_titleLabel, 2);");
        Assert.DoesNotContain("Grid.SetRow(_titleLabel, 1);", nowPlaying, StringComparison.Ordinal);
        StringAssert.Contains(nowPlaying, "await _playbackSession.ResumeAsync();");
        StringAssert.Contains(nowPlaying, "_playbackSession.Stop();");
        StringAssert.Contains(nowPlaying, "nameof(StoryDetailPage)");
        StringAssert.Contains(nowPlaying, "current.OriginPlaylist is { } playlist");
        StringAssert.Contains(nowPlaying, "nameof(PlaylistDetailPage)");
        StringAssert.Contains(nowPlaying, "[\"playlist\"] = playlist");
        StringAssert.Contains(playlistDetail, "originPlaylist: _playlist");
        Assert.DoesNotContain("originPlaylist:", storyDetail, StringComparison.Ordinal);
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
