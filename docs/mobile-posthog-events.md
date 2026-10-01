# Mobile PostHog events

Schink Stories uses the PostHog .NET SDK with manual MAUI instrumentation in
`MobileAnalyticsService`. Project: **Schink Stories (143269)**, EU region.
The contract below applies to `analytics_schema_version = 2`. It starts producing
data when a build containing these changes is installed and used; older events
are not backfilled. Existing event names are retained. Signed-in events now use
the normalized account email as their distinct ID, matching the website.

## Filter by phone

All mobile events, screen views and exceptions include these event properties:

| Property | Meaning / examples |
| --- | --- |
| `app` | `schink_stories_mobile` (separates app traffic from the website) |
| `platform` | `iOS`, `Android` |
| `device_brand` | Normalized manufacturer: `Apple`, `Samsung`, `Huawei`, etc. |
| `device_family` | `iPhone`, `iPad`, `Android phone`, `Android tablet` |
| `device_type` | `mobile` (phone), `tablet`, etc. |
| `device_model` | OS-reported model, e.g. `iPhone16,1`, `SM-S918B`, `VOG-L29` |
| `device_manufacturer` | Original OS-reported manufacturer |
| `is_emulator` | `true` for virtual devices |
| `os_version`, `app_version`, `app_build` | Device OS and installed app version/build |
| `build_configuration` | `debug` or `release`; this does not identify store distribution |

Apple models stay as hardware identifiers rather than an incomplete marketing-name
lookup. Device names set by their owners are never collected. Standard aliases
`$os`, `$os_version`, `$device_type`, `$app_version` and `$app_build` are also sent.

In [PostHog Activity](https://eu.posthog.com/project/143269/activity), use **event
property** filters such as `app = schink_stories_mobile`, `device_brand = Samsung`
and `device_type = mobile`. In an insight, break down by `device_model` or `platform`.
To exclude emulator development traffic, filter `is_emulator = false` and
`build_configuration = release`. Physical test devices can still be release builds.
Older mobile events already have `platform`, `device_model`, `device_manufacturer`,
`os_version`, `app_version` and `app_build`, so these existing fields can also be used
for historical filtering.

## Filter by activity

| Property | Values / meaning |
| --- | --- |
| `event_kind` | `user_activity`, `screen_view`, `lifecycle`, `diagnostic`, `error` |
| `activity` | `listening`, `searching`, `downloads`, `favorites`, `games`, `characters`, `playlists`, `notifications`, `stories`, `account`, `subscriptions`, `app_lifecycle`, `diagnostics`, `errors`, `navigation` |
| `action` | Specific action, e.g. `audio_played`, `story_favorite_changed`, `character_guess_answered` |
| `screen_name` | Current stable screen such as `listen`, `story_detail`, `search`, `playlist_detail` |
| `last_screen_name` | Retained compatibility property with the same current screen |
| `previous_screen_name` | Previous screen, on `$screen` events |
| `outcome` | `succeeded` / `failed` when the event carries a success flag; exceptions are `failed` |

For Samsung listening activity, combine `device_brand = Samsung`, `activity = listening`
and `event_kind = user_activity`. API requests, cache activity and listen-queue uploads
are diagnostic events and are excluded by that last filter. Screen visits use
`event_kind = screen_view`; app background/resume events use `lifecycle`.

`user_activity` describes observed product use, not necessarily a button press:
audio can also pause/stop through OS controls, replacement or app code, and playback
can advance automatically. No-player pause/stop calls do not emit playback events.
Unknown future event names default to `diagnostic` until explicitly classified.

## API request noise

Only `mobile_api_request` diagnostics are sampled before capture:

- Failed operations are retained at 100%, including timeouts and cancellations.
- Successful operations taking at least 2,000 ms are retained at 100%.
- Faster successful operations are independently sampled at 10%.

Retained events include `sample_rate` (1 or 0.1), `sample_weight` (1 or 10), and
`sampling_reason` (`failed`, `slow`, or `routine_success`). Duration measures the
whole API operation, including response parsing and any GET retry. Failures retain
the HTTP status when a response was received; status 0 means no response was available.
A POST response that fails JSON parsing produces one failed diagnostic rather than
both a success and a failure.

Raw request counts and raw failure percentages are no longer representative of all
requests. Sum `sample_weight` to estimate request volume for events carrying this
metadata; weighted ratios are estimates. Older events without sampling metadata
were unsampled. Sampling does not change the actual requests, listening-progress
sync, user-activity events, screen views, or exceptions.

## Content and actions

- Playback (`mobile_audio_played`, `paused`, `stopped`, `completed`, `speed_changed`,
  `seeked`) includes `content_type`, `story_slug`, `source`, `playlist_slug` when
  present, position/duration in seconds and playback speed. Character previews carry
  `content_type = character_preview` and `character_slug` instead of a story slug.
- `playback_source` is `streaming` or `local_file`. Local files include downloaded
  stories **and** temporary playback-cache files; it is not an offline-download count.
- Search emits `mobile_story_search` with query length/result count and
  `mobile_story_search_result_selected` with story/source/locked state. Search text
  is not sent.
- Playlist pages emit `mobile_playlist_viewed`, `mobile_playlist_player_opened`,
  `mobile_playlist_story_selected`, `mobile_playlist_shuffle_changed`,
  `mobile_playlist_autoplay_changed` and `mobile_playlist_limit_changed`. A limit of
  `0` means no limit. These control events currently cover the playlist player.
- Character profiles emit `mobile_character_profile_viewed` and
  `mobile_character_preview_played` with character/clip slugs.
- Notifications emit `mobile_notification_opened` with type/destination/previous
  read state, and `mobile_notifications_cleared` after a successful clear operation.
- Existing authentication, favorites, downloads, game and subscription events keep
  their original names and receive the common filters.

Navigation routes exclude query strings and fragments. Analytics metadata does not
include signed audio URLs, artwork URLs, local media paths, notification bodies,
search text or additional profile/contact fields.

## Account attribution

Signed-in events, screen views and exceptions include `email` (trimmed and lowercased)
and set the PostHog person's `email` property. Their distinct ID matches the website's
email identity, so activity from both platforms belongs to the same account.
Session changes, including cached-email hydration, update identification. Signed-out
events and sessions whose email has not loaded use the existing anonymous installation
ID and omit email. No anonymous-ID alias or historical account backfill is performed,
which avoids merging different accounts sharing an installation.

New persisted crash reports retain their original email and distinct ID for replay
after restart; a later login cannot change their account attribution. Older reports
without identity metadata remain anonymous. Analytics identities are accounts or
anonymous installations, not verified humans or store downloads.

## Verification after distribution

Use a real updated iPhone and Android phone: open the app, search/select a story,
play/pause/seek, visit a playlist/character and open a notification. Check each event
in PostHog for schema version 2, phone properties and action/content context. Verify
filtering by brand/model and `event_kind = user_activity`. Local tests and compilation
verify the contract and source integration; ingestion requires running the updated app.
Also verify `email` on a signed-in event and person profile, then sign out and check
that subsequent events omit email. Switch accounts on the same device and confirm
each signed-in event uses the correct account identity.

References: [PostHog .NET SDK](https://posthog.com/docs/libraries/dotnet),
[custom event properties](https://posthog.com/docs/product-analytics/capture-events).
