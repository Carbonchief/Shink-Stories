using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Models;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MobileAnalyticsIdentityTests
{
    private const string AnonymousId = "mobile-anon-test";

    [TestMethod]
    public void AuthenticatedEmailMatchesWebsiteIdentityAndIsAvailableOnEventsAndPersons()
    {
        var identity = MobileAnalyticsIdentity.FromSession(Session(true, " Parent@Example.Test "), AnonymousId);
        var properties = new Dictionary<string, object>();
        identity.ApplyTo(properties);
        Assert.AreEqual("parent@example.test", identity.DistinctId);
        Assert.AreEqual("parent@example.test", properties["email"]);
        Assert.AreEqual(true, properties["$is_identified"]);
        Assert.AreEqual("parent@example.test", ((Dictionary<string, object>)properties["$set"])["email"]);
        Assert.IsFalse(properties.ContainsKey("$anon_distinct_id"), "Shared installations must not merge different accounts.");
    }

    [TestMethod]
    [DataRow(false, "previous@example.test")]
    [DataRow(true, null)]
    [DataRow(true, "   ")]
    public void SignedOutOrUnhydratedSessionsNeverExposeAnEmail(bool signedIn, string? email)
    {
        var identity = MobileAnalyticsIdentity.FromSession(Session(signedIn, email), AnonymousId);
        var properties = new Dictionary<string, object>
        {
            ["email"] = "stale@example.test",
            ["$set"] = new Dictionary<string, object> { ["email"] = "stale@example.test" }
        };
        identity.ApplyTo(properties);
        Assert.AreEqual(AnonymousId, identity.DistinctId);
        Assert.AreEqual(false, properties["$is_identified"]);
        Assert.IsFalse(properties.ContainsKey("email"));
        Assert.IsFalse(properties.ContainsKey("$set"));
    }

    [TestMethod]
    public void AccountChangesAndSignOutCannotMutateAnEarlierCrashIdentity()
    {
        var first = MobileAnalyticsIdentity.FromSession(Session(true, "first@example.test"), AnonymousId);
        var signedOut = MobileAnalyticsIdentity.FromSession(Session(false, "first@example.test"), AnonymousId);
        var second = MobileAnalyticsIdentity.FromSession(Session(true, "second@example.test"), AnonymousId);
        var replay = new Dictionary<string, object>();
        first.ApplyTo(replay);
        Assert.AreEqual(AnonymousId, signedOut.DistinctId);
        Assert.AreEqual("second@example.test", second.DistinctId);
        Assert.AreEqual("first@example.test", first.DistinctId);
        Assert.AreEqual("first@example.test", replay["email"]);
    }

    private static MobileSession Session(bool signedIn, string? email) => new(
        signedIn, email, null, null, null, null, null, false, false,
        Array.Empty<string>(), "/login", "/signup", "/plans");
}
