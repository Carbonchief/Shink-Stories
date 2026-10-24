# Supabase egress follow-ups — 23 October 2026

These four changes were prepared and verified locally on 23 October 2026.
Production deployment and live verification are tracked separately by the
publish workflow and its deployment journal.

## Behavior and savings

1. **Character listening summaries.** The server reads fresh per-character
   counts and last-listened timestamps from PostgreSQL instead of downloading
   the subscriber's complete profile-audio history. Raw events remain available
   for analytics. A read-only measurement of the largest production history
   returned 47 summaries instead of 1,866 events: 266,346 to 7,835 JSON bytes
   (97.1% smaller).
2. **Story listing and details.** Listings keep the fields needed for discovery,
   search, ordering, media eligibility, access decisions and character previews.
   The selected story loads its full lessons, questions and other player fields
   separately. Measuring the final SQL projection against the 142 published
   production stories returned 225,464 versus 305,446 JSON bytes (26.2% smaller).
3. **Notifications.** One database response replaces separate page, unread-count
   and history-count reads. Website background refreshes fetch only badge data;
   opening the panel still loads messages. Read status, clearing, history and
   character-unlock evaluation retain their existing behavior. Native clients
   keep their existing response contract and receive the backend improvements.
4. **Longer public-content caching.** Successful public JSON payloads for stories,
   playlists, characters, products, resources and published landing pages are
   cached in each server process for **seven days**. Each read checks a tiny
   database revision before reusing cached content. Database triggers change
   that revision on publish, edit, unpublish, delete and other content writes,
   including changes made on another server or directly in the database.

The byte measurements above are uncompressed JSON comparisons, not billed
network bytes or a promise about total monthly usage. The earlier HTTP response
compression remains enabled. Production billing impact still needs a comparable
post-deployment traffic window.

## Freshness and safety

- Revision checks have no time-based cache. A request starting after a committed
  publish sees the new revision. Existing short object caches are revision-keyed;
  rebuilding them reuses the seven-day payload cache.
- A missing revision or unavailable revision service bypasses cached content.
  Errors, malformed responses and empty/not-found payloads are not retained for
  seven days. Cache identities include credentials and response-selection headers.
- Scheduled blog visibility can change with time without a database write.
  Blogs therefore keep their existing short cache; explicit blog writes also
  change the revision immediately.
- Listening progress, subscription access, favourites, subscriber records and
  notifications never enter the public payload cache. Listening and notification
  summaries are fresh database reads scoped to one subscriber.
- Personal summary RPCs are executable only by `service_role`. Public catalogue
  reads retain RLS. Public users can read revision counters but cannot write them.
- Audio protection and delivery are unchanged. The anonymous runtime check still
  required sign-in and exposed no public static audio URL. Authenticated physical
  device playback was not exercised for this batch.

## Verification

- 73 focused .NET tests passed, including catalogue ordering/detail behavior,
  cross-instance invalidation, seven-day expiry, private-data exclusion, fresh
  counts, cancellation and compression.
- Seven JavaScript interaction tests passed against the actual notification
  functions: badge/page races, queued refresh, read state and history paging.
- The real migration passed local PostgreSQL fixtures for count parity, immediate
  writes, subscriber isolation, all 12 content-table invalidation triggers,
  publish/unpublish/delete and role permissions. Fixtures were rolled back.
- Release publish from a clean copy containing only this batch passed, including
  published static-asset manifest validation. Existing ImageSharp dependency
  advisories are unchanged.
- A running Release build with a local simulated Supabase origin passed health,
  warm catalogue reuse, immediate publish revision refresh, full selected-story
  details and anonymous access gating. No production credentials were used.
- The working-tree full suite passed 1,083 tests with four unrelated mobile
  source-assertion failures: `MobileStoryDetailUsesNativePlayerAndDirectR2AudioUrls`,
  `MobileAppCapturesPostHogAnalytics`,
  `MobileLuisterPullToRefreshUsesUiSafeCancelableLoadPath`, and
  `MobileLuisterCachesStoryDataForFastColdStart`. The final four additional
  focused tests also passed. A clean batch-only suite passed 1,064 tests with the
  first two failures and one missing ignored development-settings file. That
  callback test passed separately with a temporary, credential-free fixture.
- All 52 original unrelated dirty/untracked files were checked against their
  original SHA-256 hashes. None changed, and none is in the prepared batch patch.

## Production rollout

Apply `Shink/Database/migrations/20261023084940_public_content_egress_followups.sql`
to project `btpsoyiyhtfbeznonygn` first, then deploy the server source.
The migration is additive: three read-only RPCs, six revision counters and
content invalidation triggers. Explicit missing-RPC fallbacks permit staged
rollout, but deploying without the migration does not enable the intended savings.

After deployment, check catalogue/search/detail fields, sign-in and protected
playback, notification badges/read/clear/history, and revision invalidation after
an authorized content publish. Compare daily Supabase database egress and
request patterns over equivalent traffic windows; the 250 GB quota cannot be
verified from local payload measurements alone.

## Full working-tree validation, 24 October 2026

The full Release suite passes all 1,091 tests after updating four stale mobile
source checks to follow the current cancellation, cached-load, playback retry,
analytics identity and crash-redaction paths. The isolated PostgreSQL migration
fixture, 49 deployment tests and seven notification interaction tests also pass.
