using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MobileFailureRecoveryTests
{
    [TestMethod]
    public void UnavailableStoreBacksOffAndSuccessfulConnectionRestoresNormalPolling()
    {
        var now = DateTimeOffset.Parse("2026-10-01T06:00:00Z");
        var backoff = new StoreRecoveryBackoff();
        Assert.IsTrue(backoff.CanAttempt(now));
        foreach (var seconds in new[] { 30, 60, 120, 240, 480, 900, 900 })
        {
            var delay = backoff.Failed(now);
            Assert.AreEqual(TimeSpan.FromSeconds(seconds), delay);
            Assert.IsFalse(backoff.CanAttempt(now + delay - TimeSpan.FromMilliseconds(1)));
            now += delay;
            Assert.IsTrue(backoff.CanAttempt(now));
        }

        backoff.Succeeded();
        Assert.IsTrue(backoff.CanAttempt(now));
        Assert.AreEqual(TimeSpan.FromSeconds(30), backoff.Failed(now));
    }

    [TestMethod]
    public void ExpectedConnectionFailuresAreDistinctFromProgrammingAndAccessErrors()
    {
        Assert.IsNotNull(MobileTransientFailure.Reason(new TaskCanceledException()));
        Assert.IsNotNull(MobileTransientFailure.Reason(new HttpRequestException()));
        Assert.IsNotNull(MobileTransientFailure.Reason(new WebException("closed", WebExceptionStatus.ConnectionClosed)));
        Assert.AreEqual("store_unavailable", MobileTransientFailure.Reason(new MobileStoreUnavailableException()));
        Assert.IsNotNull(MobileTransientFailure.Reason(
            new HttpRequestException("gateway", null, HttpStatusCode.BadGateway)));
        Assert.IsNull(MobileTransientFailure.Reason(
            new HttpRequestException("denied", null, HttpStatusCode.Forbidden)));
        Assert.IsNull(MobileTransientFailure.Reason(new NullReferenceException()));
        Assert.IsNull(MobileTransientFailure.Reason(new InvalidOperationException()));
        Assert.IsNull(MobileTransientFailure.Reason(new AudioPlaybackLoadException("cannot open")));
    }

    [TestMethod]
    public async Task FailedStreamRenewsAuthorizationOnceAndReturnsThePlayedUrl()
    {
        var attempts = new List<string>();
        var refreshes = 0;
        var result = await AudioPlaybackRetry.PlayAsync("https://example.test/media/audio/story?token=old", url =>
        {
            attempts.Add(url);
            if (attempts.Count == 1) throw new AudioPlaybackLoadException("cannot open");
            return Task.CompletedTask;
        }, () =>
        {
            refreshes++;
            return Task.FromResult<string?>("https://example.test/media/audio/story?token=new");
        });

        Assert.AreEqual(1, refreshes);
        Assert.AreEqual(2, attempts.Count);
        Assert.AreEqual(attempts[1], result);
        Assert.AreNotEqual(attempts[0], attempts[1]);
    }

    [TestMethod]
    public async Task PersistentStreamFailureStopsAfterOneRetry()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<AudioPlaybackLoadException>(() => AudioPlaybackRetry.PlayAsync(
            "https://example.test/audio", _ =>
            {
                attempts++;
                throw new AudioPlaybackLoadException("cannot open");
            }, () => Task.FromResult<string?>("https://example.test/renewed")));
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public async Task OfflineFileFailuresNeverRequestNetworkAuthorization()
    {
        var refreshes = 0;
        foreach (var url in new[] { "/app/offline/story.mp3", "file:///app/offline/story.mp3" })
            await Assert.ThrowsAsync<AudioPlaybackLoadException>(() => AudioPlaybackRetry.PlayAsync(url,
                _ => throw new AudioPlaybackLoadException("local file failed"), () =>
                {
                    refreshes++;
                    return Task.FromResult<string?>("https://example.test/audio");
                }));
        Assert.AreEqual(0, refreshes);
    }

    [TestMethod]
    public async Task CancellationAndProgrammingErrorsDoNotRestartPlayback()
    {
        var refreshes = 0;
        Task<string?> Refresh()
        {
            refreshes++;
            return Task.FromResult<string?>("https://example.test/audio");
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => AudioPlaybackRetry.PlayAsync(
            "https://example.test/audio", _ => throw new OperationCanceledException(), Refresh));
        await Assert.ThrowsAsync<NullReferenceException>(() => AudioPlaybackRetry.PlayAsync(
            "https://example.test/audio", _ => throw new NullReferenceException(), Refresh));
        Assert.AreEqual(0, refreshes);
    }

    [TestMethod]
    public async Task MissingRenewedAuthorizationPreservesTheOriginalPlaybackFailure()
    {
        var failure = new AudioPlaybackLoadException("cannot open");
        var observed = await Assert.ThrowsAsync<AudioPlaybackLoadException>(() => AudioPlaybackRetry.PlayAsync(
            "https://example.test/audio", _ => throw failure, () => Task.FromResult<string?>(null)));
        Assert.AreSame(failure, observed);
    }

    [TestMethod]
    public void CrashDetailsKeepFramesWhileRedactingCredentialsAndBoundingSize()
    {
        var raw = "at app.Play https://example.test/audio?token=secret-token&sig=secret-signature\nBearer secret-bearer\npassword=secret-password\nat android.view.ViewGroup.dispatchDragEvent";
        var result = MobileCrashDiagnostics.Sanitize(raw, 1000);
        StringAssert.Contains(result, "android.view.ViewGroup.dispatchDragEvent");
        StringAssert.Contains(result, "token=[redacted]");
        foreach (var secret in new[] { "secret-token", "secret-signature", "secret-bearer", "secret-password" })
            Assert.IsFalse(result.Contains(secret, StringComparison.Ordinal));
        Assert.AreEqual(20, MobileCrashDiagnostics.Sanitize(new string('x', 100), 20).Length);
        Assert.AreEqual(string.Empty, MobileCrashDiagnostics.Sanitize(null, 20));
    }
}
