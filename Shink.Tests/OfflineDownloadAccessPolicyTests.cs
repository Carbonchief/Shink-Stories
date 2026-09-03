using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public sealed class OfflineDownloadAccessPolicyTests
{
    private static readonly TimeSpan AccessWindow = TimeSpan.FromDays(30);
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void OwnerKeyNormalizesEmailWithoutStoringTheAddress()
    {
        var first = OfflineDownloadAccessPolicy.BuildOwnerKey(" Parent@Example.com ");
        var second = OfflineDownloadAccessPolicy.BuildOwnerKey("parent@example.com");
        var other = OfflineDownloadAccessPolicy.BuildOwnerKey("other@example.com");

        Assert.IsNotNull(first);
        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, other);
        Assert.AreNotEqual("parent@example.com", first);
    }

    [TestMethod]
    public void FreeDownloadRemainsPlayableWhileSignedOut()
    {
        var isPlayable = OfflineDownloadAccessPolicy.IsPlayable(
            requiresSubscription: false,
            downloadOwnerKey: null,
            lastAccessVerifiedAt: Now.Subtract(TimeSpan.FromDays(90)),
            isSignedIn: false,
            hasFullStoryAccess: false,
            currentOwnerKey: null,
            now: Now,
            accessRefreshWindow: AccessWindow);

        Assert.IsTrue(isPlayable);
    }

    [TestMethod]
    public void PaidDownloadIsLockedWhileSignedOutButRestoredForSameActiveAccount()
    {
        var ownerKey = OfflineDownloadAccessPolicy.BuildOwnerKey("parent@example.com");

        var whileSignedOut = OfflineDownloadAccessPolicy.IsPlayable(
            requiresSubscription: true,
            downloadOwnerKey: ownerKey,
            lastAccessVerifiedAt: Now.Subtract(TimeSpan.FromDays(2)),
            isSignedIn: false,
            hasFullStoryAccess: false,
            currentOwnerKey: null,
            now: Now,
            accessRefreshWindow: AccessWindow);
        var afterSameAccountSignsIn = OfflineDownloadAccessPolicy.IsPlayable(
            requiresSubscription: true,
            downloadOwnerKey: ownerKey,
            lastAccessVerifiedAt: Now.Subtract(TimeSpan.FromDays(2)),
            isSignedIn: true,
            hasFullStoryAccess: true,
            currentOwnerKey: ownerKey,
            now: Now,
            accessRefreshWindow: AccessWindow);

        Assert.IsFalse(whileSignedOut);
        Assert.IsTrue(afterSameAccountSignsIn);
    }

    [TestMethod]
    public void PaidDownloadDoesNotOpenForAnotherAccountOrExpiredAccess()
    {
        var ownerKey = OfflineDownloadAccessPolicy.BuildOwnerKey("parent@example.com");
        var otherOwnerKey = OfflineDownloadAccessPolicy.BuildOwnerKey("other@example.com");

        var differentAccount = OfflineDownloadAccessPolicy.IsPlayable(
            requiresSubscription: true,
            downloadOwnerKey: ownerKey,
            lastAccessVerifiedAt: Now.Subtract(TimeSpan.FromDays(2)),
            isSignedIn: true,
            hasFullStoryAccess: true,
            currentOwnerKey: otherOwnerKey,
            now: Now,
            accessRefreshWindow: AccessWindow);
        var expiredAccess = OfflineDownloadAccessPolicy.IsPlayable(
            requiresSubscription: true,
            downloadOwnerKey: ownerKey,
            lastAccessVerifiedAt: Now.Subtract(TimeSpan.FromDays(31)),
            isSignedIn: true,
            hasFullStoryAccess: true,
            currentOwnerKey: ownerKey,
            now: Now,
            accessRefreshWindow: AccessWindow);

        Assert.IsFalse(differentAccount);
        Assert.IsFalse(expiredAccess);
    }

    [TestMethod]
    public void StoryCornerDownloadUsesPaidEntitlementWithoutFullLibraryAccess()
    {
        var ownerKey = OfflineDownloadAccessPolicy.BuildOwnerKey("parent@example.com");

        var isPlayable = OfflineDownloadAccessPolicy.IsPlayable(
            requiresSubscription: true,
            downloadOwnerKey: ownerKey,
            lastAccessVerifiedAt: Now.Subtract(TimeSpan.FromDays(2)),
            isSignedIn: true,
            hasFullStoryAccess: false,
            currentOwnerKey: ownerKey,
            now: Now,
            accessRefreshWindow: AccessWindow,
            hasPaidSubscription: true,
            requiresFullStoryAccess: false);

        Assert.IsTrue(isPlayable);
    }

    [TestMethod]
    public void FullLibraryDownloadStillRequiresFullLibraryEntitlement()
    {
        var ownerKey = OfflineDownloadAccessPolicy.BuildOwnerKey("parent@example.com");

        var isPlayable = OfflineDownloadAccessPolicy.IsPlayable(
            requiresSubscription: true,
            downloadOwnerKey: ownerKey,
            lastAccessVerifiedAt: Now.Subtract(TimeSpan.FromDays(2)),
            isSignedIn: true,
            hasFullStoryAccess: false,
            currentOwnerKey: ownerKey,
            now: Now,
            accessRefreshWindow: AccessWindow,
            hasPaidSubscription: true,
            requiresFullStoryAccess: true);

        Assert.IsFalse(isPlayable);
    }
}
