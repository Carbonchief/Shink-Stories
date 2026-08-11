using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Services;

namespace Shink.Tests;

[TestClass]
public class VideoAccessServiceTests
{
    [TestMethod]
    public void CreateSignedVideoUrl_UsesDistinctLongLivedVideoToken()
    {
        var provider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var service = new VideoAccessService(provider);
        var beforeCreate = DateTimeOffset.UtcNow;

        var signedUrl = service.CreateSignedVideoUrl("video-storie");

        StringAssert.StartsWith(signedUrl, "/media/video/video-storie?token=");
        var token = ExtractToken(signedUrl);
        var payloadJson = provider
            .CreateProtector("Shink.Video.StreamToken.v1")
            .Unprotect(token);
        using var payload = JsonDocument.Parse(payloadJson);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
            payload.RootElement.GetProperty("ExpiresAtUnixSeconds").GetInt64());

        Assert.IsGreaterThanOrEqualTo(beforeCreate.AddHours(2), expiresAt);
        Assert.IsTrue(service.IsTokenValid("video-storie", token));
        Assert.IsFalse(service.IsTokenValid("ander-storie", token));
    }

    [TestMethod]
    public void VideoTokensCannotBeReusedAsAudioTokens()
    {
        var provider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var videoService = new VideoAccessService(provider);
        var audioService = new AudioAccessService(provider);
        var videoToken = ExtractToken(videoService.CreateSignedVideoUrl("video-storie"));

        Assert.IsFalse(audioService.IsTokenValid("video-storie", videoToken));
    }

    private static string ExtractToken(string signedUrl)
    {
        const string tokenPrefix = "?token=";
        var tokenIndex = signedUrl.IndexOf(tokenPrefix, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, tokenIndex);
        return Uri.UnescapeDataString(signedUrl[(tokenIndex + tokenPrefix.Length)..]);
    }
}
