using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class AudioAccessServiceTests
{
    [TestMethod]
    public void CreateSignedAudioUrl_DefaultLifetimeSupportsLongStoryPlayback()
    {
        var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var service = new AudioAccessService(provider);
        var beforeCreate = DateTimeOffset.UtcNow;

        var signedUrl = service.CreateSignedAudioUrl("lang-storie");

        var token = ExtractToken(signedUrl);
        var payloadJson = provider
            .CreateProtector("Shink.Audio.StreamToken.v1")
            .Unprotect(token);
        using var payload = JsonDocument.Parse(payloadJson);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.RootElement.GetProperty("ExpiresAtUnixSeconds").GetInt64());

        Assert.IsGreaterThanOrEqualTo(
            beforeCreate.AddHours(2),
            expiresAt,
            "Default signed audio URLs should remain valid for long stories and delayed browser range requests.");
    }

    private static string ExtractToken(string signedUrl)
    {
        var tokenPrefix = "?token=";
        var tokenIndex = signedUrl.IndexOf(tokenPrefix, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, tokenIndex, "Signed audio URL should include a token query string.");

        return Uri.UnescapeDataString(signedUrl[(tokenIndex + tokenPrefix.Length)..]);
    }
}
