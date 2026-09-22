using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClientStoryDetail = Shink.Mobile.Models.MobileStoryDetailResponse;

namespace Shink.Tests;

[TestClass]
public sealed class MobileVideoContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void MobileResponseRetainsProtectedVideoUrlEvenWhenAudioIsNull()
    {
        const string json = """
            {"story":{"slug":"liedjie","storyType":"video"},"audioUrl":null,
             "videoUrl":"https://www.schink.co.za/media/video/liedjie?token=expiring",
             "requiresSubscription":false}
            """;
        var detail = JsonSerializer.Deserialize<ClientStoryDetail>(json, Options)!;
        Assert.IsTrue(detail.IsVideo);
        Assert.IsNull(detail.AudioUrl);
        Assert.AreEqual("https://www.schink.co.za/media/video/liedjie?token=expiring", detail.VideoUrl);
        var cached = JsonSerializer.Deserialize<ClientStoryDetail>(JsonSerializer.Serialize(detail, Options), Options)!;
        Assert.AreEqual(detail.VideoUrl, cached.VideoUrl);
        Assert.IsTrue(cached.IsVideo);
    }

    [TestMethod]
    public void OldCachedVideoStillIdentifiesItsTypeWithoutThePreviouslyDiscardedUrl()
    {
        var detail = JsonSerializer.Deserialize<ClientStoryDetail>(
            """{"story":{"slug":"liedjie","storyType":"video"},"audioUrl":null}""", Options)!;
        Assert.IsTrue(detail.IsVideo);
        Assert.IsNull(detail.VideoUrl);
    }

    [TestMethod]
    public void AudioStoryResponsesRemainCompatibleWithoutAVideoField()
    {
        var detail = JsonSerializer.Deserialize<ClientStoryDetail>(
            """{"story":{"slug":"ben","storyType":"story"},"audioUrl":"https://example.invalid/signed-audio"}""", Options)!;
        Assert.IsFalse(detail.IsVideo);
        Assert.IsNull(detail.VideoUrl);
        Assert.AreEqual("https://example.invalid/signed-audio", detail.AudioUrl);
    }
}
