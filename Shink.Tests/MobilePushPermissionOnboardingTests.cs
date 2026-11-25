using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MobilePushPermissionOnboardingTests
{
    [TestInitialize]
    public void Reset() => Preferences.Default.Values.Clear();

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public async Task FirstSignInAsksOnceAndPreservesTheAnswerAcrossLaunches(bool allow, bool android)
    {
        var prompts = 0;
        var refreshes = 0;
        var banners = 0;
        for (var launch = 0; launch < 2; launch++)
        {
            var flow = new MobilePushPermissionOnboarding();
            await flow.OfferAsync(() => true, () => { prompts++; return Task.FromResult(allow); },
                () => refreshes++, android ? () => { banners++; return Task.FromResult(true); } : null);
        }
        Assert.AreEqual(1, prompts);
        Assert.AreEqual(1, refreshes);
        Assert.AreEqual(allow && android ? 1 : 0, banners);
        Assert.AreEqual(allow, MobilePushInstallation.Enabled);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingExplicitChoiceIsPreserved(bool enabled)
    {
        Preferences.Default.Set("mobile_push_enabled_v1", enabled);
        var banners = 0;
        await new MobilePushPermissionOnboarding().OfferAsync(() => true, () => throw new AssertFailedException(),
            () => Assert.Fail(), () => { banners++; return Task.FromResult(true); });
        Assert.AreEqual(enabled, MobilePushInstallation.Enabled);
        Assert.AreEqual(enabled ? 1 : 0, banners);
    }

    [TestMethod]
    public async Task ConcurrentAuthenticationCallbacksShareOneDecision()
    {
        var flow = new MobilePushPermissionOnboarding();
        var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompts = 0;
        Task Offer() => flow.OfferAsync(() => true, () => { prompts++; return response.Task; }, () => { }, null);
        var first = Offer();
        var second = Offer();
        response.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.AreEqual(1, prompts);
    }

    [TestMethod]
    public async Task AccountChangeDuringPermissionDialogDoesNotEnablePushForSomeoneElse()
    {
        var signedIn = true;
        var flow = new MobilePushPermissionOnboarding();
        await flow.OfferAsync(() => signedIn, () => { signedIn = false; return Task.FromResult(true); },
            () => Assert.Fail(), () => throw new AssertFailedException());
        await flow.OfferAsync(() => signedIn, () => throw new AssertFailedException(), () => Assert.Fail(), null);
        Assert.IsFalse(MobilePushInstallation.HasNotificationChoice);
    }
}
