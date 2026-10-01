using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shink.Mobile.Services;

namespace Shink.Tests;

[TestClass]
public sealed class MobileAnalyticsSchemaTests
{
    [TestMethod]
    [DataRow("iOS", "Apple", "iPhone16,1", "Phone", "Apple", "iPhone", "mobile")]
    [DataRow("iOS", "Apple", "iPad13,4", "Tablet", "Apple", "iPad", "tablet")]
    [DataRow("Android", "samsung", "SM-S918B", "Phone", "Samsung", "Android phone", "mobile")]
    [DataRow("Android", "HUAWEI", "VOG-L29", "Phone", "Huawei", "Android phone", "mobile")]
    public void DeviceFiltersKeepRawModelAndNormalizeBrandAndFormFactor(
        string platform, string manufacturer, string model, string idiom,
        string brand, string family, string type)
    {
        var properties = MobileAnalyticsSchema.DeviceProperties(
            platform, manufacturer, model, idiom, false, "27.0", "1.1", "35");
        Assert.AreEqual(model, properties["device_model"]);
        Assert.AreEqual(manufacturer, properties["device_manufacturer"]);
        Assert.AreEqual(brand, properties["device_brand"]);
        Assert.AreEqual(family, properties["device_family"]);
        Assert.AreEqual(type, properties["device_type"]);
        Assert.AreEqual(type, properties["$device_type"]);
        Assert.AreEqual(platform, properties["$os"]);
        Assert.AreEqual("27.0", properties["$os_version"]);
        Assert.AreEqual("35", properties["$app_build"]);
        Assert.AreEqual(false, properties["is_emulator"]);
        Assert.IsFalse(properties.ContainsKey("device_name"));
    }

    [TestMethod]
    public void VirtualAndUnknownDevicesCanBeSeparatedFromRealPhones()
    {
        var properties = MobileAnalyticsSchema.DeviceProperties(
            "Android", "", "unknown", "Unknown", true, "16", "1.1", "35");
        Assert.AreEqual(true, properties["is_emulator"]);
        Assert.AreEqual("Unknown", properties["device_brand"]);
        Assert.AreEqual("unknown", properties["device_type"]);
    }

    [TestMethod]
    [DataRow("mobile_audio_played", "listening", "user_activity", "audio_played")]
    [DataRow("mobile_story_listened", "listening", "user_activity", "story_listened")]
    [DataRow("mobile_story_search_result_selected", "searching", "user_activity", "story_search_result_selected")]
    [DataRow("mobile_story_download_completed", "downloads", "user_activity", "story_download_completed")]
    [DataRow("mobile_story_favorite_changed", "favorites", "user_activity", "story_favorite_changed")]
    [DataRow("mobile_character_guess_answered", "games", "user_activity", "character_guess_answered")]
    [DataRow("mobile_character_preview_played", "characters", "user_activity", "character_preview_played")]
    [DataRow("mobile_playlist_viewed", "playlists", "user_activity", "playlist_viewed")]
    [DataRow("mobile_notification_opened", "notifications", "user_activity", "notification_opened")]
    [DataRow("mobile_paywall_viewed", "subscriptions", "user_activity", "paywall_viewed")]
    [DataRow("mobile_app_lifecycle", "app_lifecycle", "lifecycle", "app_lifecycle")]
    [DataRow("mobile_api_request", "diagnostics", "diagnostic", "api_request")]
    [DataRow("mobile_audio_playback_cache_hit", "diagnostics", "diagnostic", "audio_playback_cache_hit")]
    [DataRow("mobile_story_listen_queue_flushed", "diagnostics", "diagnostic", "story_listen_queue_flushed")]
    [DataRow("mobile_unknown_future_event", "diagnostics", "diagnostic", "unknown_future_event")]
    public void ActivityFiltersSeparateProductUsageFromBackgroundWork(
        string eventName, string activity, string kind, string action)
    {
        var properties = new Dictionary<string, object>();
        MobileAnalyticsSchema.EnrichEvent(properties, eventName, "search");
        Assert.AreEqual(activity, properties["activity"]);
        Assert.AreEqual(kind, properties["event_kind"]);
        Assert.AreEqual(action, properties["action"]);
        Assert.AreEqual(2, properties["analytics_schema_version"]);
    }

    [TestMethod]
    public void ScreenAndErrorsHaveTheirOwnKindsAndAccurateOutcomes()
    {
        var properties = new Dictionary<string, object>();
        MobileAnalyticsSchema.EnrichEvent(properties, "$screen", "character_match");
        Assert.AreEqual("screen_view", properties["event_kind"]);
        Assert.AreEqual("games", properties["activity"]);
        Assert.IsFalse(properties.ContainsKey("outcome"));

        properties["success"] = false;
        MobileAnalyticsSchema.EnrichEvent(properties, "mobile_api_request", "search");
        Assert.AreEqual("failed", properties["outcome"]);
        properties["success"] = true;
        MobileAnalyticsSchema.EnrichEvent(properties, "$exception", "search", isException: true);
        Assert.AreEqual("error", properties["event_kind"]);
        Assert.AreEqual("failed", properties["outcome"]);
    }

    [TestMethod]
    public void ScreensAreStableAcrossNavigationPathsAndRoutesExcludeSensitiveQueries()
    {
        const string route = "//Luister/SearchPage/StoryDetailPage?slug=nee&token=secret#fragment";
        Assert.AreEqual("story_detail", MobileAnalyticsSchema.ScreenName("StoryDetailPage", route));
        Assert.AreEqual("story_detail", MobileAnalyticsSchema.ScreenName("StoryDetailPage", "//Other/StoryDetailPage"));
        Assert.AreEqual("//Luister/SearchPage/StoryDetailPage", MobileAnalyticsSchema.CleanRoute(route));
        Assert.AreEqual("search", MobileAnalyticsSchema.ScreenName("SearchPage", "//Luister/SearchPage?email=private"));
        Assert.AreEqual("unknown", MobileAnalyticsSchema.ScreenName(null, null));
    }

    [TestMethod]
    public void PlaybackPropertiesIdentifyContentWithoutExposingSignedMediaOrDevicePaths()
    {
        var metadata = new AudioPlaybackMetadata("Nee", "Schink Stories",
            "https://example.test/artwork?token=secret", "nee", "luister", "woordjieland",
            ContentType: "story", PlaybackSource: "streaming");
        var properties = metadata.ToAnalyticsProperties(30, 120, 1.25);
        Assert.AreEqual("nee", properties["story_slug"]);
        Assert.AreEqual("luister", properties["source"]);
        Assert.AreEqual("woordjieland", properties["playlist_slug"]);
        Assert.AreEqual("story", properties["content_type"]);
        Assert.AreEqual(30d, properties["position_seconds"]);
        Assert.AreEqual(120d, properties["duration_seconds"]);
        Assert.AreEqual(1.25, properties["playback_speed"]);
        Assert.IsFalse(properties.Values.Any(value => value.ToString()!.Contains("secret", StringComparison.Ordinal)));
        Assert.IsFalse(properties.ContainsKey("artwork_url"));

        var preview = new AudioPlaybackMetadata("Neelsie", ContentType: "character_preview", CharacterSlug: "neelsie");
        var previewProperties = preview.ToAnalyticsProperties(0, 15, 1);
        Assert.AreEqual("neelsie", previewProperties["character_slug"]);
        Assert.IsFalse(previewProperties.ContainsKey("story_slug"));
    }

    [TestMethod]
    [DataRow("https://media.example.test/story.mp3?token=secret", "streaming")]
    [DataRow("file:///private/app/downloads/story.mp3", "local_file")]
    [DataRow("/data/user/0/app/cache/story.mp3", "local_file")]
    public void PlaybackDeliveryIsAFilterValueRatherThanAMediaUrl(string url, string expected) =>
        Assert.AreEqual(expected, AudioPlaybackMetadata.ResolvePlaybackSource(url));
}
