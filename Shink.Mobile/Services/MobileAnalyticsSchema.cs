namespace Shink.Mobile.Services;

// Shared, platform-independent event contract. Keep existing event names so
// saved PostHog insights continue to work across app versions.
public static class MobileAnalyticsSchema
{
    public const int Version = 2;

    private static readonly HashSet<string> UserActivityEvents = new(StringComparer.Ordinal)
    {
        "mobile_auth_signed_in", "mobile_auth_signed_up", "mobile_auth_signed_out",
        "mobile_profile_updated", "mobile_story_favorite_changed", "mobile_story_viewed",
        "mobile_story_search", "mobile_story_search_result_selected", "mobile_story_listened",
        "mobile_audio_played", "mobile_audio_paused", "mobile_audio_stopped",
        "mobile_audio_completed", "mobile_audio_speed_changed", "mobile_audio_seeked",
        "mobile_story_download_started", "mobile_story_download_completed", "mobile_story_download_removed",
        "mobile_plan_selected", "mobile_plans_viewed", "mobile_paywall_viewed", "mobile_store_purchase_synced",
        "mobile_character_match_started", "mobile_character_match_completed",
        "mobile_character_guess_started", "mobile_character_guess_answered",
        "mobile_character_guess_completed", "mobile_character_guess_hint_used",
        "mobile_character_profile_viewed", "mobile_character_preview_played",
        "mobile_playlist_viewed", "mobile_playlist_player_opened", "mobile_playlist_story_selected",
        "mobile_playlist_shuffle_changed", "mobile_playlist_autoplay_changed", "mobile_playlist_limit_changed",
        "mobile_notification_opened", "mobile_notifications_cleared"
    };

    public static Dictionary<string, object> DeviceProperties(
        string platform, string manufacturer, string model, string idiom, bool isVirtual,
        string osVersion, string appVersion, string appBuild)
    {
        var deviceType = idiom.ToLowerInvariant() switch
        {
            "phone" => "mobile",
            "tablet" => "tablet",
            "desktop" => "desktop",
            "tv" => "tv",
            "watch" => "watch",
            _ => "unknown"
        };
        var brand = platform == "iOS" ? "Apple" : manufacturer.Trim().ToLowerInvariant() switch
        {
            "samsung" => "Samsung",
            "huawei" => "Huawei",
            "honor" => "Honor",
            "google" => "Google",
            "xiaomi" => "Xiaomi",
            "oneplus" => "OnePlus",
            "motorola" => "Motorola",
            "oppo" => "Oppo",
            "vivo" => "Vivo",
            "apple" => "Apple",
            "" => "Unknown",
            _ => manufacturer.Trim()
        };
        var family = (platform, deviceType) switch
        {
            ("iOS", "mobile") => "iPhone",
            ("iOS", "tablet") => "iPad",
            ("Android", "mobile") => "Android phone",
            ("Android", "tablet") => "Android tablet",
            _ => idiom
        };

        return new Dictionary<string, object>
        {
            ["platform"] = platform,
            ["device_manufacturer"] = manufacturer,
            ["device_model"] = model,
            ["device_brand"] = brand,
            ["device_family"] = family,
            ["device_type"] = deviceType,
            ["is_emulator"] = isVirtual,
            ["os_version"] = osVersion,
            ["app_version"] = appVersion,
            ["app_build"] = appBuild,
            ["$os"] = platform,
            ["$os_version"] = osVersion,
            ["$device_type"] = deviceType,
            ["$app_version"] = appVersion,
            ["$app_build"] = appBuild
        };
    }

    public static void EnrichEvent(Dictionary<string, object> properties, string eventName,
        string? screenName, bool isException = false)
    {
        properties["analytics_schema_version"] = Version;
        properties["event_kind"] = isException ? "error"
            : eventName == "$screen" ? "screen_view"
            : eventName is "mobile_app_opened" or "mobile_app_lifecycle" ? "lifecycle"
            : UserActivityEvents.Contains(eventName) ? "user_activity" : "diagnostic";
        properties["activity"] = isException ? "errors" : ResolveActivity(eventName, screenName);
        properties["action"] = eventName == "$screen" ? "screen_viewed"
            : eventName.StartsWith("mobile_", StringComparison.Ordinal) ? eventName[7..] : eventName;

        if (isException)
            properties["outcome"] = "failed";
        else if (properties.TryGetValue("success", out var success) && success is bool succeeded)
            properties["outcome"] = succeeded ? "succeeded" : "failed";
    }

    private static string ResolveActivity(string eventName, string? screenName) => eventName switch
    {
        "$screen" => ScreenActivity(screenName),
        "mobile_story_search" or "mobile_story_search_result_selected" => "searching",
        "mobile_story_favorite_changed" => "favorites",
        "mobile_story_listened" => "listening",
        _ when eventName.StartsWith("mobile_story_download_", StringComparison.Ordinal) => "downloads",
        _ when eventName.StartsWith("mobile_character_match_", StringComparison.Ordinal) ||
               eventName.StartsWith("mobile_character_guess_", StringComparison.Ordinal) => "games",
        _ when eventName.StartsWith("mobile_character_", StringComparison.Ordinal) => "characters",
        _ when eventName.StartsWith("mobile_playlist_", StringComparison.Ordinal) => "playlists",
        _ when eventName.StartsWith("mobile_notification", StringComparison.Ordinal) => "notifications",
        _ when eventName.StartsWith("mobile_auth_", StringComparison.Ordinal) ||
               eventName == "mobile_profile_updated" => "account",
        _ when eventName.StartsWith("mobile_plan", StringComparison.Ordinal) ||
               eventName == "mobile_paywall_viewed" ||
               eventName.StartsWith("mobile_store_", StringComparison.Ordinal) => "subscriptions",
        _ when eventName.StartsWith("mobile_audio_", StringComparison.Ordinal) && UserActivityEvents.Contains(eventName) => "listening",
        "mobile_story_viewed" => "stories",
        "mobile_app_opened" or "mobile_app_lifecycle" => "app_lifecycle",
        _ => "diagnostics"
    };

    private static string ScreenActivity(string? screenName) => screenName switch
    {
        "listen" or "story_detail" => "stories",
        "playlist_stories" or "playlist_detail" => "playlists",
        "characters" => "characters",
        "character_match" or "character_match_config" or "character_guess" or "character_guess_config" => "games",
        "search" => "searching",
        "downloads" => "downloads",
        "notifications" => "notifications",
        "plans" => "subscriptions",
        "account" or "profile" or "settings" => "account",
        _ => "navigation"
    };

    public static string CleanRoute(string? route) =>
        (route ?? string.Empty).Split('?', '#')[0];

    public static string ScreenName(string? pageType, string? route) => pageType switch
    {
        "LuisterPage" => "listen",
        "KaraktersPage" => "characters",
        "StoryDetailPage" => "story_detail",
        "PlaylistStoriesPage" => "playlist_stories",
        "PlaylistDetailPage" => "playlist_detail",
        "DownloadedPage" => "downloads",
        "SearchPage" => "search",
        "KennisgewingsPage" => "notifications",
        "KarakterPareGamePage" => "character_match",
        "KarakterPareConfigPage" => "character_match_config",
        "KarakterRaaiGamePage" => "character_guess",
        "KarakterRaaiConfigPage" => "character_guess_config",
        "AccountPage" => "account",
        "ProfilePage" => "profile",
        "SettingsPage" => "settings",
        "PlansPage" => "plans",
        // Do not let cumulative Shell paths or query strings create new screens.
        _ => CleanRoute(route).Trim('/').Split('/').LastOrDefault()?.ToLowerInvariant() is { Length: > 0 } name
            ? name : "unknown"
    };
}
