using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class WordPressLastLoginResolverTests
{
    [TestMethod]
    public void Resolve_PrefersMostRecentTimestampAcrossAllSignals()
    {
        var socialLoginAt = new DateTimeOffset(2026, 4, 27, 21, 9, 15, TimeSpan.Zero);
        const string pmproLogins = "a:1:{s:4:\"last\";s:14:\"April 28, 2026\";}";
        const string sessionTokens = "a:1:{s:64:\"token\";a:4:{s:10:\"expiration\";i:1778563777;s:5:\"login\";i:1777354177;}}";

        var result = WordPressLastLoginResolver.Resolve(socialLoginAt, pmproLogins, sessionTokens);

        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1777354177), result);
    }

    [TestMethod]
    public void Resolve_LetsMorePreciseTimestampBeatDateOnlyPmproValueOnSameDay()
    {
        var socialLoginAt = new DateTimeOffset(2026, 4, 28, 7, 29, 37, TimeSpan.Zero);
        const string pmproLogins = "a:1:{s:4:\"last\";s:14:\"April 28, 2026\";}";

        var result = WordPressLastLoginResolver.Resolve(socialLoginAt, pmproLogins, sessionTokensSerialized: null);

        Assert.AreEqual(socialLoginAt, result);
    }

    [TestMethod]
    public void ParseSessionTokenLastLogin_UsesLatestLoginAcrossMultipleTokens()
    {
        const string sessionTokens =
            "a:2:{s:64:\"one\";a:4:{s:10:\"expiration\";i:1778563777;s:5:\"login\";i:1777354177;}" +
            "s:64:\"two\";a:4:{s:10:\"expiration\";i:1778526555;s:5:\"login\";i:1777454177;}}";

        var result = WordPressLastLoginResolver.ParseSessionTokenLastLogin(sessionTokens);

        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1777454177), result);
    }
}
