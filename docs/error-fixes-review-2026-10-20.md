# Error fixes and combined review — 20 October 2026

All four issues have implementations. One dedicated agent handled each issue; the coordinating agent reviewed the combined patch and ran the verification below before committing. No production deployment or migration was performed as part of this review.

## Changes and evidence

| Issue | Correction | Verification |
| --- | --- | --- |
| Personal playlist crash | Bind the constrained personal route to `Guid? PersonalPlaylistId`; retain string public slugs and existing URL shapes. | Rendered tests cover GUID binding, public GUID-looking slugs, route switching, anonymous privacy, unavailable/private playlists, entitlement filtering, playback order, and signed audio in the player and autoplay queue. |
| Admin analytics timeout | Reuse one listening-history rollup for daily and all-time metrics. Execute the read-only aggregation through PL/pgSQL so PostgreSQL can actually launch its planned parallel workers. | Full JSON parity across empty data, completion signals, nullable identities, cross-day/story sessions, rounding, favorites, three timezones, wrapper fields, and server-only grants. The 3,080,013-event / 93,309-view local fixture passed under the service role and an eight-second statement budget. |
| Static file / assembly mismatch | Validate published manifest lengths and SHA-256 ETags, including compressed representations. Enable Windows Web Deploy app-offline behavior. Stop the Azure app before replacing files, confirm deployment completion, then restore its prior running state. | Kestrel regression reproduces the exact 7065-byte mismatch and HTTP 500; consistent output plus restart restores GET, HEAD and range requests. Actual Release publish passed its automatic validation; same-length tampering was rejected with exit code 1, and restoring the bytes passed. |
| Recovery-email cancellation 422 | Recognize known terminal scheduling responses for HTTP 400 and 422, preserving audit details. | Mocked HTTP tests cover the recorded provider envelope, already-sent/cancelled outcomes, continued cancellation, unchanged existing audit status, and real authentication, validation, rate-limit and server failures. No real provider calls were made. |

The static-file errors originated on Windows host `MERCURY`, despite their `Production` environment label. The older Azure assembly-loading failures overlap a file-copy deployment. These are separate host observations. The historical failing static filename is unavailable in the old rows; the logger now retains the original exception/status path without query strings or signed tokens.

## Combined review

The review checked route compatibility, authentication and entitlements, signed audio, analytics metric parity and grants, cancellation error classification, static integrity and compression, publish ordering, upload persistence, deployment concurrency, and failure recovery.

Corrections made during review:

- Rejected a read-only package mount because the existing local playlist-image upload fallback writes to the web root. The deployment retains writable content and explicitly disables deployment cleanup.
- Serialized deployment across every branch/ref with one app-scoped lock; an accepted or incomplete deployment cannot trigger application startup.
- Preserved already-stopped apps and refused deployment if SCM would also stop, if its configuration is unknown, or if the app is in an unexpected state.
- Prevented a failed or interrupted upload from starting a potentially mixed application. An operator must restore a known-good package before starting such a failed deployment.
- Strengthened the analytics fix after initial measurements showed insufficient improvement. Nested-plan profiling found planned workers were not launching in the SQL function; the final implementation launched both workers and retained invoker security and the same JSON contract.
- Updated one stale video-upload source assertion left by the previous admin form binding fix. Corrected an overly broad playlist test that had incorrectly rejected legitimate public artwork paths; audio URLs are checked directly instead.

No remaining correctness findings were identified in the changed production code. Existing mobile application edits remain untouched (the tracked mobile diff hash matches the starting snapshot).

## Verification results

- 1,020 .NET tests passed in Release, excluding only the two independently confirmed pre-existing mobile source assertion failures below.
- 10 deployment safety tests passed, including SCM configuration refusal, original state preservation, stop/upload/start failures, and deployment status that has not reached completion.
- Local SQL regression passed and rolled back all fixtures. Coordinator timings: previous full RPC 4,202 ms; optimized validation including full RPC and plan check 1,668 ms. These are local results, not production latency guarantees.
- Release publish passed, including automatic manifest validation. Published-output tamper rejection and recovery passed.
- Shell syntax, workflow YAML parsing, and `git diff --check` passed.

The initial unrestricted test run found these two existing mobile source assertions that also contradict the committed `HEAD` implementation:

- `StoryTrackingSourceTests.MobileAppCapturesPostHogAnalytics` expects an anonymous-only `ResolveDistinctId` expression that the committed implementation no longer contains.
- `MobileAbsoluteUrlSourceTests.MobileStoryDetailUsesNativePlayerAndDirectR2AudioUrls` expects a direct awaited player call; the committed implementation delegates playback through its retry helper.

Neither mobile production code nor those two tests were changed in this batch. The later 1,020-test run passed all other tests.

## Rollout boundaries

The analytics migration is `Shink/Database/migrations/20261020122224_admin_usage_daily_rollup.sql`. It still requires explicit production approval and a fresh live RPC measurement after application. The website and the Windows host still require approved publication/restart before these source fixes affect them.

The deployment safeguard intentionally introduces a maintenance interval while files are copied. SCM compatibility was verified read-only on the current Linux Basic B1 app (`scmSiteAlsoStopped=false`), and the script rechecks it before stopping anything. No live stop/deploy/restart probe was performed. Failed or uncertain deployments deliberately remain stopped instead of serving partially replaced assemblies.
