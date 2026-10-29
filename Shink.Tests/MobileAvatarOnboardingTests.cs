using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Models;
using Shink.Mobile.Services;
using Shink.Shared;

namespace Shink.Tests;

[TestClass]
public sealed class MobileAvatarOnboardingTests
{
    [TestMethod]
    public void RefreshSession_KeepsPendingAvatarSelectionForSameAccount()
    {
        var pending = Session() with { RequiresAvatarSelection = true };
        var refreshed = MobileAvatarOnboarding.Merge(pending, Session());
        Assert.IsTrue(refreshed.RequiresAvatarSelection);
    }

    [TestMethod]
    public void SessionTransitions_ClearPendingSelectionAfterSaveSignOutOrAccountSwitch()
    {
        var pending = Session() with { RequiresAvatarSelection = true };
        Assert.IsFalse(MobileAvatarOnboarding.Merge(pending, Session(), completed: true).RequiresAvatarSelection);
        Assert.IsFalse(MobileAvatarOnboarding.Merge(pending, Session() with { IsSignedIn = false }).RequiresAvatarSelection);
        Assert.IsFalse(MobileAvatarOnboarding.Merge(pending, Session() with { Email = "another@example.com" }).RequiresAvatarSelection);
    }

    [TestMethod]
    public void SecureCacheHydration_PreservesPendingStateUntilEmailIsAvailable()
    {
        var cached = Session() with { Email = null, RequiresAvatarSelection = true };
        Assert.IsTrue(MobileAvatarOnboarding.Merge(cached, Session()).RequiresAvatarSelection);
    }

    [TestMethod]
    public void OlderServerSession_DoesNotPromptExistingAccounts()
    {
        var json = JsonSerializer.Serialize(Session());
        using var document = JsonDocument.Parse(json);
        var legacy = document.RootElement.EnumerateObject()
            .Where(property => property.Name is not nameof(MobileSession.RequiresAvatarSelection) and not nameof(MobileSession.SupportsProfileAvatars))
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        var session = JsonSerializer.Deserialize<MobileSession>(JsonSerializer.Serialize(legacy));
        Assert.IsNotNull(session);
        Assert.IsFalse(session.SupportsProfileAvatars);
        Assert.IsFalse(MobileAvatarOnboarding.Merge(Session(), session).RequiresAvatarSelection);
    }

    [TestMethod]
    public void OlderBackend_DoesNotTrapSignupOrAnExistingPendingStepInThePicker()
    {
        var pending = Session() with { RequiresAvatarSelection = true };
        var legacy = Session() with { SupportsProfileAvatars = false };
        Assert.IsFalse(MobileAvatarOnboarding.Merge(pending, legacy).RequiresAvatarSelection);
        Assert.IsFalse(MobileAvatarOnboarding.Merge(Session(), legacy with { RequiresAvatarSelection = true }).RequiresAvatarSelection);
    }

    [TestMethod]
    [DataRow("/profielprentjie", true)]
    [DataRow("/profielprentjie?returnUrl=%2Fluister", true)]
    [DataRow("/luister", false)]
    [DataRow("https://untrusted.example/profielprentjie", false)]
    public void AuthRedirect_RecognizesFirstSignupWithoutAWebView(string path, bool expected)
    {
        Assert.AreEqual(expected, MobileAvatarOnboarding.IsAvatarRedirect(path));
    }

    [TestMethod]
    [DataRow("https://www.schink.co.za/branding/profile-avatars/panda.png", "panda")]
    [DataRow("/branding/profile-avatars/roomys.png", "roomys")]
    [DataRow("https://legacy.example/avatar.jpg", null)]
    public void AvatarCatalogue_PreselectsTheSamePortraitAcrossWebAndMobile(string url, string? expected)
    {
        Assert.AreEqual(expected, ProfileAvatarOptions.IdFromImageUrl(url));
        Assert.HasCount(16, ProfileAvatarOptions.All);
    }

    private static MobileSession Session() => new(true, "listener@example.com", "Listener", null,
        "Listener", null, null, false, false, [], "", "", "", SupportsProfileAvatars: true);
}
