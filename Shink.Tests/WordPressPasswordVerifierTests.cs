using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class WordPressPasswordVerifierTests
{
    [TestMethod]
    public void Verify_ReturnsTrue_ForWordPressBcryptHash()
    {
        const string password = "SecreT123!";
        var passwordToHash = Convert.ToBase64String(
            HMACSHA384.HashData(
                Encoding.UTF8.GetBytes("wp-sha384"),
                Encoding.UTF8.GetBytes(password.Trim())));
        var hash = "$wp" + BCrypt.Net.BCrypt.HashPassword(passwordToHash);

        var verifier = new WordPressPasswordVerifier();

        Assert.IsTrue(verifier.Verify(password, hash));
    }

    [TestMethod]
    public void Verify_ReturnsTrue_ForPortablePhpHash()
    {
        const string password = "test";
        const string hash = "$P$B55D6LjfHDkINU5wF.v2BuuzO0/XPk/";

        var verifier = new WordPressPasswordVerifier();

        Assert.IsTrue(verifier.Verify(password, hash));
    }

    [TestMethod]
    public void Verify_ReturnsTrue_ForLegacyPhpBcrypt2yHash()
    {
        const string password = "AnotherSecret!";
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var phpHash = hash.StartsWith("$2b$", StringComparison.Ordinal)
            ? "$2y$" + hash[4..]
            : hash;

        var verifier = new WordPressPasswordVerifier();

        Assert.IsTrue(verifier.Verify(password, phpHash));
        Assert.IsFalse(verifier.Verify("wrong-password", phpHash));
    }

    [TestMethod]
    public void DetectFormat_ReturnsExpectedLabels()
    {
        var verifier = new WordPressPasswordVerifier();

        Assert.AreEqual("wp_bcrypt", verifier.DetectFormat("$wp$2y$12$abcdefghijklmnopqrstuv"));
        Assert.AreEqual("phpass", verifier.DetectFormat("$P$B55D6LjfHDkINU5wF.v2BuuzO0/XPk/"));
        Assert.AreEqual("bcrypt", verifier.DetectFormat("$2y$12$abcdefghijklmnopqrstuv12345678901234567890123456789012"));
        Assert.AreEqual("md5", verifier.DetectFormat("5f4dcc3b5aa765d61d8327deb882cf99"));
    }
}
