using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Shink.Tests;

[TestClass]
public sealed class MobileOfflinePlaybackSourceTests
{
    [TestMethod]
    public void PlaylistAndAutoplayResolveDownloadsBeforePreparingNetworkAudio()
    {
        var playlist = Read("Pages/PlaylistDetailPage.cs");
        var start = playlist[playlist.IndexOf("private async Task StartPlaybackAsync", StringComparison.Ordinal)..];
        Assert.IsLessThan(start.IndexOf("PrepareAudioPlaybackSourceAsync", StringComparison.Ordinal),
            start.IndexOf("ResolvePlayableAudioAsync", StringComparison.Ordinal));
        StringAssert.Contains(start, "if (string.IsNullOrWhiteSpace(playbackUrl))");
        var session = Read("Services/StoryPlaybackSession.cs");
        StringAssert.Contains(session, "!_playlistPlaybackState.IsOfflineQueue && string.IsNullOrWhiteSpace(localAudio)");
        StringAssert.Contains(session, "GetStoryAsync(nextStory.Slug, nextStory.Source, cancellationToken)");
    }

    [TestMethod]
    public void ResumeCanSwitchToTheDownloadAndPreservePosition()
    {
        var session = Read("Services/StoryPlaybackSession.cs");
        var resume = session[session.IndexOf("public async Task ResumeAsync()", StringComparison.Ordinal)..session.IndexOf("public async Task SeekAsync", StringComparison.Ordinal)];
        StringAssert.Contains(resume, "ResolvePlayableAudioAsync(");
        StringAssert.Contains(resume, "_current = current with { PlaybackUrl = playbackUrl }");
        StringAssert.Contains(resume, "await _audioPlaybackService.SeekAsync(position)");
    }

    [TestMethod]
    public void OfflineDetailsDoNotWaitForNetworkAndCachedRenderingCannotRenewAccess()
    {
        var page = Read("Pages/StoryDetailPage.cs");
        var load = page[page.IndexOf("private async Task LoadAsync(", StringComparison.Ordinal)..page.IndexOf("private async Task<bool> TryRenderCachedStoryAsync", StringComparison.Ordinal)];
        Assert.IsLessThan(load.IndexOf("_apiClient.GetStoryAsync", StringComparison.Ordinal), load.IndexOf("RenderOfflineDetail(offlineDownload)", StringComparison.Ordinal));
        StringAssert.Contains(load, "Connectivity.Current.NetworkAccess != NetworkAccess.Internet");
        StringAssert.Contains(load, "await _offlineDownloadService.RefreshAccessAsync(detail, cancellationToken)");
        Assert.DoesNotContain("_ = _offlineDownloadService.RefreshAccessAsync(detail)", page, StringComparison.Ordinal);
    }

    [TestMethod]
    public void DurationProbeUsesLocalAudioAndIncompleteDownloadsAreRejected()
    {
        var page = Read("Pages/StoryDetailPage.cs");
        StringAssert.Contains(page, "duration = await _audioPlaybackService.GetDurationAsync(localAudio, cancellationToken)");
        StringAssert.Contains(page, "else if (string.IsNullOrWhiteSpace(localAudio))");
        var api = Read("Services/MobileApiClient.cs");
        StringAssert.Contains(api, "bytesReceived == 0 || (totalBytes.HasValue && bytesReceived != totalBytes.Value)");
    }

    [TestMethod]
    public void AndroidDefersStopUntilPendingForegroundStartsAreAcknowledged()
    {
        var service = Read("Platforms/Android/AudioPlaybackForegroundService.cs");
        StringAssert.Contains(service, "_pendingStarts++;");
        StringAssert.Contains(service, "_pendingStarts = Math.Max(0, _pendingStarts - 1)");
        StringAssert.Contains(service, "_playbackRequested || _pendingStarts != 0 || _runningService is null");
        var audio = Read("Services/AudioPlaybackService.cs");
        StringAssert.Contains(audio, "AudioPlaybackForegroundService.RequestStop()");
        Assert.DoesNotContain("context.StopService(", audio, StringComparison.Ordinal);
    }

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, "Shink.Mobile", relative);
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
