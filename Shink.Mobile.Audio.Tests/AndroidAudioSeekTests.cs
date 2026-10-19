using System.Reflection;
using Android.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Services;

[assembly: DoNotParallelize]

namespace Shink.Mobile.Audio.Tests;

[TestClass]
public sealed class AndroidAudioSeekTests
{
    [TestMethod]
    public async Task CompletedSeekDetachesListenerExactlyOnceAndClampsPosition()
    {
        var (service, player, analytics) = await CreatePlayingServiceAsync();
        var seek = service.SeekAsync(TimeSpan.FromMinutes(5));
        Assert.AreEqual(1, player.ListenerCount);
        Assert.AreEqual(player.DurationValue, player.LastSeekPosition);

        player.CompleteSeek();
        await seek.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(0, player.ListenerCount);
        Assert.AreEqual(1, player.RemoveAttempts);
        Assert.AreEqual(1, analytics.Events.Count(name => name == "mobile_audio_seeked"));
        service.Stop();
        Assert.AreEqual(1, player.RemoveAttempts);
    }

    [TestMethod]
    public async Task StopDuringOutstandingSeekDetachesBeforeDisposalAndFinishesPromptly()
    {
        var (service, player, analytics) = await CreatePlayingServiceAsync();
        var seek = service.SeekAsync(TimeSpan.FromSeconds(30));
        var lateCallback = player.CaptureSeekCallback();

        service.Stop();
        lateCallback?.Invoke(player, EventArgs.Empty);
        await seek.WaitAsync(TimeSpan.FromSeconds(1));
        service.Stop();

        Assert.IsTrue(player.IsDisposed);
        Assert.AreEqual(0, player.ListenerCount);
        Assert.AreEqual(1, player.RemoveAttempts);
        Assert.AreEqual(0, player.AccessesAfterRelease);
        Assert.IsLessThan(player.Calls.IndexOf("release"), player.Calls.IndexOf("remove"));
        Assert.IsLessThan(player.Calls.IndexOf("release"), player.Calls.IndexOf("stop"));
        Assert.DoesNotContain("mobile_audio_seeked", analytics.Events);
    }

    [TestMethod]
    public async Task ReplacingStoryInvalidatesOldSeekWithoutAffectingNewPlayer()
    {
        var (service, oldPlayer, analytics) = await CreatePlayingServiceAsync();
        var oldSeek = service.SeekAsync(TimeSpan.FromSeconds(30));
        await service.PlayAsync("https://example.invalid/authorized-second-story");
        await oldSeek.WaitAsync(TimeSpan.FromSeconds(1));
        var newPlayer = GetPlayer(service);
        Assert.AreNotSame(oldPlayer, newPlayer);
        Assert.IsTrue(oldPlayer.IsDisposed);
        Assert.AreEqual(0, oldPlayer.AccessesAfterRelease);
        Assert.DoesNotContain("mobile_audio_seeked", analytics.Events);

        var newSeek = service.SeekAsync(TimeSpan.FromSeconds(10));
        newPlayer.CompleteSeek();
        await newSeek;
        Assert.AreEqual(1, analytics.Events.Count(name => name == "mobile_audio_seeked"));
        service.Stop();
    }

    [TestMethod]
    public async Task CancellationPropagatesOriginalTokenAndDetachesListener()
    {
        var (service, player, analytics) = await CreatePlayingServiceAsync();
        using var cancellation = new CancellationTokenSource();
        var seek = service.SeekAsync(TimeSpan.FromSeconds(20), cancellation.Token);
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => seek.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreEqual(0, player.ListenerCount);
        Assert.AreEqual(1, player.RemoveAttempts);
        Assert.DoesNotContain("mobile_audio_seeked", analytics.Events);

        var nextSeek = service.SeekAsync(TimeSpan.FromSeconds(25));
        player.CompleteSeek();
        await nextSeek;
        service.Stop();
        Assert.AreEqual(2, player.RemoveAttempts);
    }

    [TestMethod]
    public async Task AlreadyCanceledRequestNeverTouchesPlayer()
    {
        var (service, player, _) = await CreatePlayingServiceAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.SeekAsync(TimeSpan.Zero, cancellation.Token));
        Assert.AreEqual(0, player.ListenerCount);
        Assert.DoesNotContain("seek", player.Calls);
        service.Stop();
    }

    [TestMethod]
    public async Task MissingCompletionTimesOutAndRemovesListener()
    {
        var (service, player, analytics) = await CreatePlayingServiceAsync();
        await service.SeekAsync(TimeSpan.FromSeconds(30)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, player.ListenerCount);
        Assert.AreEqual(1, player.RemoveAttempts);
        Assert.Contains("mobile_audio_seeked", analytics.Events);
        service.Stop();
    }

    [TestMethod]
    public async Task UnexpectedSeekFailureStillCleansUpAndIsNotSwallowed()
    {
        var (service, player, analytics) = await CreatePlayingServiceAsync();
        var failure = new InvalidOperationException("unexpected seek failure");
        player.SeekException = failure;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SeekAsync(TimeSpan.Zero));
        Assert.AreSame(failure, exception);
        Assert.AreEqual(0, player.ListenerCount);
        Assert.AreEqual(1, player.RemoveAttempts);
        Assert.DoesNotContain("mobile_audio_seeked", analytics.Events);
        service.Stop();
    }

    [TestMethod]
    public async Task NativeInvalidStateDuringSeekCleansUpWithoutReportingSuccess()
    {
        var (service, player, analytics) = await CreatePlayingServiceAsync();
        player.SeekException = new Java.Lang.IllegalStateException();
        await service.SeekAsync(TimeSpan.Zero);
        Assert.AreEqual(0, player.ListenerCount);
        Assert.AreEqual(1, player.RemoveAttempts);
        Assert.DoesNotContain("mobile_audio_seeked", analytics.Events);
        service.Stop();
    }

    [TestMethod]
    public async Task ListenerAttachmentFailureIsIncludedInCleanupBoundary()
    {
        var (service, player, analytics) = await CreatePlayingServiceAsync();
        player.AddException = new ObjectDisposedException("native listener");
        await service.SeekAsync(TimeSpan.Zero);
        Assert.AreEqual(1, player.RemoveAttempts);
        Assert.AreEqual(0, player.ListenerCount);
        Assert.DoesNotContain("seek", player.Calls);
        Assert.DoesNotContain("mobile_audio_seeked", analytics.Events);
        service.Stop();
        Assert.AreEqual(1, player.RemoveAttempts);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CleanupFailureCannotMaskCallerCancellation(bool illegalState)
    {
        var (service, player, _) = await CreatePlayingServiceAsync();
        player.RemoveException = illegalState
            ? new Java.Lang.IllegalStateException()
            : new ObjectDisposedException("native listener");
        using var cancellation = new CancellationTokenSource();
        var seek = service.SeekAsync(TimeSpan.Zero, cancellation.Token);
        cancellation.Cancel();
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => seek);
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreEqual(1, player.RemoveAttempts);
        service.Stop();
        Assert.AreEqual(1, player.RemoveAttempts);
    }

    [TestMethod]
    public async Task StopClearsAllOutstandingSeeks()
    {
        var (service, player, analytics) = await CreatePlayingServiceAsync();
        var seeks = Enumerable.Range(1, 8)
            .Select(position => service.SeekAsync(TimeSpan.FromSeconds(position))).ToArray();
        Assert.AreEqual(seeks.Length, player.ListenerCount);
        service.Stop();
        await Task.WhenAll(seeks).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(0, player.ListenerCount);
        Assert.AreEqual(seeks.Length, player.RemoveAttempts);
        Assert.AreEqual(0, player.AccessesAfterRelease);
        Assert.DoesNotContain("mobile_audio_seeked", analytics.Events);
    }

    [TestMethod]
    public async Task StopCannotReleasePlayerWhileNativeSeekIsStarting()
    {
        var (service, player, _) = await CreatePlayingServiceAsync();
        var enteredSeek = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var allowSeek = new ManualResetEventSlim();
        player.OnSeek = () =>
        {
            enteredSeek.TrySetResult();
            if (!allowSeek.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        };
        var seek = Task.Run(() => service.SeekAsync(TimeSpan.Zero));
        await enteredSeek.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var stop = Task.Run(service.Stop);
        try
        {
            Assert.IsFalse(player.IsReleased);
        }
        finally
        {
            allowSeek.Set();
        }

        await Task.WhenAll(seek, stop).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.AreEqual(0, player.AccessesAfterRelease);
        Assert.AreEqual(1, player.RemoveAttempts);
        Assert.AreEqual(0, player.ListenerCount);
    }

    private static async Task<(AudioPlaybackService, MediaPlayer, MobileAnalyticsService)> CreatePlayingServiceAsync()
    {
        var analytics = new MobileAnalyticsService();
        var service = new AudioPlaybackService(analytics);
        // The test player performs no I/O and prepares synchronously.
        await service.PlayAsync("https://example.invalid/authorized-story");
        return (service, GetPlayer(service), analytics);
    }

    private static MediaPlayer GetPlayer(AudioPlaybackService service) =>
        (MediaPlayer)typeof(AudioPlaybackService).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;
}
