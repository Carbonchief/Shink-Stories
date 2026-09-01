using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Shink.Tests;

[TestClass]
public class StoryTrackingSourceTests
{
    [TestMethod]
    public void SharedStoryPlayerRefreshesTrackingWhenPlaylistAudioChanges()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor.js"));

        StringAssert.Contains(script, "function refreshStoryTrackingState(audioElement, shouldTrackView)");
        StringAssert.Contains(script, "trackingStateMatchesAudio(currentState, audioElement)");
        StringAssert.Contains(script, "storyTrackingStateCache.delete(audioElement);");
        StringAssert.Contains(script, "refreshStoryTrackingState(audioElement, true);");
        StringAssert.Contains(script, "const trackingState = getStoryTrackingState(audioElement) ?? refreshStoryTrackingState(audioElement, true);");
    }

    [TestMethod]
    public void SharedStoryPlayerKeepsInitialDurationWhileMetadataLoads()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor.js"));

        StringAssert.Contains(script, "function resolveAudioDurationForDisplay(audioElement)");
        StringAssert.Contains(script, "audioElement.dataset.storyDurationSeconds");
        StringAssert.Contains(script, "function resolveAudioCurrentTimeForDisplay(audioElement, duration)");
        StringAssert.Contains(script, "loadStoryProgress(audioElement)");
        StringAssert.Contains(script, "const duration = resolveAudioDurationForDisplay(audioElement);");
        StringAssert.Contains(script, "const currentTime = resolveAudioCurrentTimeForDisplay(audioElement, duration);");
        Assert.IsFalse(
            script.Contains("const duration = Number.isFinite(audioElement.duration) ? audioElement.duration : 0;", StringComparison.Ordinal),
            "The custom player should not overwrite an initial catalog duration with 0 while browser metadata is pending.");
    }

    [TestMethod]
    public void SharedStoryPlayerKeepsTimeRowBusyUntilMetadataIsReady()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor.js"));

        StringAssert.Contains(script, "const MEDIA_HAVE_METADATA = 1;");
        StringAssert.Contains(script, "const TIME_ROW_SELECTOR = \".story-time-row\";");
        StringAssert.Contains(script, "function isAudioTimeDisplayLoading(audioElement)");
        StringAssert.Contains(script, "audioElement.readyState < MEDIA_HAVE_METADATA");
        StringAssert.Contains(script, "timeRow.classList.toggle(\"is-loading\", isLoading);");
        StringAssert.Contains(script, "timeRow.setAttribute(\"aria-busy\", String(isLoading));");
        StringAssert.Contains(script, "audioElement.addEventListener(\"loadstart\", updateCustomPlayerState);");
        StringAssert.Contains(script, "audioElement.addEventListener(\"emptied\", updateCustomPlayerState);");
        StringAssert.Contains(script, "audioElement.addEventListener(\"canplay\", updateCustomPlayerState);");
    }

    [TestMethod]
    public void SharedStoryPlayerQueuesFinalListenFlushWhileAnotherRequestIsInFlight()
    {
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor.js"));

        StringAssert.Contains(script, "deferredFlushEventType: null");
        StringAssert.Contains(script, "function deferStoryListenFlush(audioElement, trackingState, eventType, useKeepalive)");
        StringAssert.Contains(script, "if (force || eventType === \"ended\")");
        StringAssert.Contains(script, "deferredEventType,");
        StringAssert.Contains(script, "flushStoryListen(audioElement, trackingState, \"ended\", true, true);");
        StringAssert.Contains(script, "const listenedSeconds = pendingSeconds >= LISTEN_MIN_EVENT_SECONDS");
    }

    [TestMethod]
    public void LuisterStoryUsesRepeatToggleInsteadOfSpeedControl()
    {
        var markup = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor"));
        var styles = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "LuisterStory.razor.css"));
        var script = File.ReadAllText(GetRepoPath("Shink", "Components", "Pages", "GratisStory.razor.js"));

        StringAssert.Contains(markup, "story-repeat-toggle");
        StringAssert.Contains(markup, "aria-pressed=\"false\"");
        StringAssert.Contains(markup, "fa-solid fa-repeat");
        Assert.IsFalse(markup.Contains("story-speed-toggle", StringComparison.Ordinal));
        StringAssert.Contains(styles, ".story-repeat-toggle.is-active");
        StringAssert.Contains(script, "const REPEAT_TOGGLE_SELECTOR = \".story-repeat-toggle\";");
        StringAssert.Contains(script, "audioElement.dataset.repeatEnabled = String(audioElement.dataset.repeatEnabled !== \"true\");");
        StringAssert.Contains(script, "setAttribute(\"aria-pressed\", String(repeatEnabled));");
        StringAssert.Contains(script, "if (audioElement.dataset.repeatEnabled === \"true\")");
        StringAssert.Contains(script, "flushStoryListen(audioElement, trackingState, \"ended\", true, true);");
        Assert.IsFalse(script.Contains("audioElement.loop", StringComparison.Ordinal));

        var completionFlushIndex = script.IndexOf(
            "flushStoryListen(audioElement, trackingState, \"ended\", true, true);",
            StringComparison.Ordinal);
        var repeatRestartIndex = script.IndexOf(
            "if (audioElement.dataset.repeatEnabled === \"true\")",
            completionFlushIndex,
            StringComparison.Ordinal);
        Assert.IsGreaterThan(completionFlushIndex, repeatRestartIndex);
    }

    [TestMethod]
    public void MobileStoryPlayerPostsViewAndListenTracking()
    {
        var page = File.ReadAllText(GetRepoPath("Shink.Mobile", "Pages", "StoryDetailPage.cs"));
        var playbackSession = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "StoryPlaybackSession.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));

        StringAssert.Contains(page, "TrackStoryViewAsync(detail.Story.Slug, detail.Story.Source)");
        StringAssert.Contains(page, "_storyPlaybackSession.PlayAsync(");
        StringAssert.Contains(page, "_storyPlaybackSession.NotifyPageHidden();");
        StringAssert.Contains(playbackSession, "FlushPendingListen(\"progress\", force: false)");
        StringAssert.Contains(playbackSession, "FlushPendingListen(\"pause\", force: true)");
        StringAssert.Contains(playbackSession, "FlushPendingListen(\"ended\", force: true, isCompleted: true)");
        StringAssert.Contains(playbackSession, "FlushPendingListen(\"pagehide\", force: true)");
        StringAssert.Contains(playbackSession, "_lifecycleService.Stopping += OnAppStopping;");
        Assert.IsFalse(playbackSession.Contains("eventType,\n            \"play\",", StringComparison.Ordinal));
        StringAssert.Contains(page, "StartProgressTimer()");
        StringAssert.Contains(page, "UpdateProgressState()");
        Assert.IsFalse(page.Contains("schink-track://listen?", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains("visibilityhidden", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains("private void FlushPendingListen", StringComparison.Ordinal));
        StringAssert.Contains(client, "TrackStoryViewAsync(string slug, string source");
        StringAssert.Contains(client, "TrackStoryListenAsync(");
        StringAssert.Contains(client, "\"/api/stories/{Uri.EscapeDataString(slug)}/view\"");
        StringAssert.Contains(client, "\"/api/stories/{Uri.EscapeDataString(trackingEvent.Slug)}/listen\"");
    }

    [TestMethod]
    public void MobileAppCapturesPostHogAnalytics()
    {
        var project = File.ReadAllText(GetRepoPath("Shink.Mobile", "Shink.Mobile.csproj"));
        var mauiProgram = File.ReadAllText(GetRepoPath("Shink.Mobile", "MauiProgram.cs"));
        var analytics = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileAnalyticsService.cs"));
        var crashReporter = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileCrashReporter.cs"));
        var app = File.ReadAllText(GetRepoPath("Shink.Mobile", "App.xaml.cs"));
        var shell = File.ReadAllText(GetRepoPath("Shink.Mobile", "AppShell.xaml.cs"));
        var client = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "MobileApiClient.cs"));
        var downloads = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "OfflineStoryDownloadService.cs"));
        var playback = File.ReadAllText(GetRepoPath("Shink.Mobile", "Services", "AudioPlaybackService.cs"));

        StringAssert.Contains(project, "<PackageReference Include=\"PostHog\" Version=\"");
        StringAssert.Contains(mauiProgram, "builder.Services.AddPostHog");
        StringAssert.Contains(mauiProgram, "options.ProjectToken = analyticsSettings.ProjectApiKey;");
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<MobileAnalyticsService>();");
        StringAssert.Contains(mauiProgram, "builder.Services.AddSingleton<MobileCrashReporter>();");
        StringAssert.Contains(mauiProgram, "GetRequiredService<MobileCrashReporter>().Start();");
        StringAssert.Contains(analytics, "public sealed class MobileAnalyticsService");
        StringAssert.Contains(analytics, "_postHog.Capture(");
        StringAssert.Contains(analytics, "_postHog.CaptureScreenView(");
        StringAssert.Contains(analytics, "private string ResolveDistinctId() => _anonymousDistinctId;");
        Assert.IsFalse(analytics.Contains("_postHog.IdentifyAsync(", StringComparison.Ordinal));
        Assert.IsFalse(analytics.Contains("[\"email\"]", StringComparison.Ordinal));
        StringAssert.Contains(analytics, "_postHog.CaptureException(");
        StringAssert.Contains(analytics, "_postHog.FlushAsync()");
        StringAssert.Contains(analytics, "TrackExceptionAndFlushAsync(");
        StringAssert.Contains(crashReporter, "AppDomain.CurrentDomain.UnhandledException +=");
        StringAssert.Contains(crashReporter, "TaskScheduler.UnobservedTaskException +=");
        StringAssert.Contains(crashReporter, "AndroidEnvironment.UnhandledExceptionRaiser +=");
        StringAssert.Contains(crashReporter, "PersistPendingCrash(");
        StringAssert.Contains(crashReporter, "ReplayPendingCrashesAsync()");
        StringAssert.Contains(crashReporter, "ExceptionDispatchInfo.SetRemoteStackTrace(");
        StringAssert.Contains(crashReporter, "SensitiveValuePattern.Replace(");
        StringAssert.Contains(app, "_analytics.TrackAppOpened();");
        StringAssert.Contains(shell, "_analytics.TrackScreenView(");
        StringAssert.Contains(shell, "mobile_shell_rendered");
        StringAssert.Contains(client, "mobile_api_request");
        StringAssert.Contains(client, "mobile_auth_signed_in");
        StringAssert.Contains(client, "mobile_auth_signed_up");
        StringAssert.Contains(client, "mobile_story_viewed");
        StringAssert.Contains(client, "mobile_story_listened");
        StringAssert.Contains(client, "mobile_story_listen_queue_flushed");
        StringAssert.Contains(downloads, "mobile_story_download_started");
        StringAssert.Contains(downloads, "mobile_story_download_completed");
        StringAssert.Contains(downloads, "mobile_story_download_removed");
        StringAssert.Contains(playback, "mobile_audio_played");
        StringAssert.Contains(playback, "mobile_audio_paused");
        StringAssert.Contains(playback, "mobile_audio_completed");
        StringAssert.Contains(playback, "mobile_audio_speed_changed");
    }

    private static string GetRepoPath(params string[] segments)
    {
        var testsDirectory = Path.GetDirectoryName(GetSourceFilePath())!;
        var pathSegments = new[] { testsDirectory, ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static string GetSourceFilePath([CallerFilePath] string path = "") => path;
}
