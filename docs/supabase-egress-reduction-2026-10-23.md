# Supabase egress reduction

Prepared on 23 October 2026. This records local verification and read-only
measurements taken before the approved production rollout.

The main cost was transferring a listener's entire event history after every
listening update, notification refresh and progress lookup. Players report about
every 12 seconds. The previous 24-hour sample contained 49,675 history page reads
and 9,092 new listening events. The current cycle already used 332.10 GB against
250 GB; optimizations cannot reverse that accumulated usage.

The application now calls `get_story_progress_snapshot(uuid)`, which calculates
the existing progress rules inside PostgreSQL and returns one JSON summary per
story. The existing index filters by subscriber. Raw history stays intact. This
uses a scalar JSON result so the Data API row limit cannot truncate progress.

Player reporting, notification polling, completion rules, repeated-session
counts, resume positions, character unlock checks, public response contracts and
audio delivery are unchanged. Results are calculated on each read, including
new/offline events and events written by another instance or device, without a
stale progress cache. Tied timestamps consistently prefer the higher event ID.

Supabase HTTP clients negotiate gzip, deflate and Brotli and transparently decode
responses. The existing audio-origin streaming client is untouched. The admin
client retains its existing decompression and connection-lifetime settings.

The new function is `STABLE`, `SECURITY INVOKER`, has an empty `search_path`, and
can be executed only by `service_role`. It neither changes RLS nor stores a new
copy of anyone's progress. The application still resolves subscriber identity
from its authenticated session. Only an explicitly missing RPC (`404/PGRST202`)
uses the previous paginated calculation for deployment compatibility, with a
warning. Other RPC failures never silently restore large history transfers.

## Measurements

Read-only queries against the Shink project ran the aggregation expression
without creating the production function or modifying any records.

| Measure | Result |
| --- | ---: |
| Largest real history | 28,987 events / 135 stories |
| Previous JSON volume for that history | 10,761,462 bytes |
| Equivalent progress summary | 46,585 bytes |
| Reduction before HTTP compression | 99.567% |
| Aggregation execution time for that history | 380.841 ms |
| Largest summary among 115 listeners active on 22 October | 47,928 bytes |
| Summary volume for their 9,584 listening updates | 0.1865 GB/day |
| Same listening-update volume over 31 days | 5.78 GB |
| Conservative history-path estimate: all 49,675 page reads/day replaced by the largest summary | 73.81 GB / 31 days |

The conservative estimate deliberately assumes more full summaries than the
application needs, since one snapshot replaces several old history pages. It
also excludes savings from compression. It leaves substantial room within
250 GB at the observed traffic, but is a model of this path, not a guarantee of
future organization-wide usage. Other endpoints and traffic growth still count.
Target total egress below 6.5 GB/day (201.5 GB over 31 days) to preserve headroom.

## Verification

- Local PostgreSQL checks cover completion flags, the 95% threshold, session
  counts, zero-second ended events, missing duration, latest/null positions,
  late/offline writes, equal timestamps, subscriber isolation and role grants.
- A 30,000-event fixture produces 160 complete summaries, with less than 2% of
  the raw JSON volume. The new response matches the previous C# calculation
  field for field, including mixed-case/whitespace slugs and invalid metadata.
- 18 focused .NET tests pass, including real HTTP decoding for gzip, deflate,
  Brotli and uncompressed responses, fresh reads after writes, migration
  compatibility, non-truncation and existing character unlock tests.
- The full suite passes 1,066 of 1,070 tests. Four unrelated mobile source
  assertions fail: `MobileStoryDetailUsesNativePlayerAndDirectR2AudioUrls`,
  `MobileAppCapturesPostHogAnalytics`,
  `MobileLuisterPullToRefreshUsesUiSafeCancelableLoadPath`, and
  `MobileLuisterCachesStoryDataForFastColdStart`. Their inspected mobile source
  was not changed by this fix. Existing unrelated dirty files were preserved.

To reproduce the SQL/C# comparison, use an empty local database only:

```sh
createdb shink_story_progress_regression
psql -X -qAt -v ON_ERROR_STOP=1 -v scale=1 \
  -d shink_story_progress_regression \
  -f Shink/Database/tests/story_progress_snapshot.sql \
  > /tmp/shink-progress-parity.json
SHINK_PROGRESS_PARITY_JSON=/tmp/shink-progress-parity.json \
  dotnet test Shink.Tests/Shink.Tests.csproj \
  --filter 'FullyQualifiedName~SupabaseStoryProgressSnapshotTests|FullyQualifiedName~SupabaseResponseCompressionTests|FullyQualifiedName~SupabaseTrackingPaginationTests|FullyQualifiedName~SupabaseUserNotificationCharacterUnlockTests'
```

The SQL fixture is guarded to require that database name and an empty schema,
and rolls back its schema and data. Ordinary .NET tests also run against the
checked-in small synthetic JSON fixture when the environment variable is absent.

## Production rollout, after explicit approval

1. Apply `Shink/Database/migrations/20261023070629_story_progress_snapshot.sql`
   to project `btpsoyiyhtfbeznonygn`. This adds a read-only function; it does not
   backfill, delete or update user data. Confirm its existence and service-only
   execution grants before updating the application.
2. Deploy the scoped server changes through the existing migration-before-Azure
   workflow. A mobile store update is not required. Keep unrelated mobile work
   out of the commit and deployed source.
3. Check authenticated resume positions, repeat/completion counts, character
   unlocks and notification refreshes. Confirm RPC success and absence of the
   missing-migration warning.
4. Check API logs for a sharp drop in the old nine-field full-history GET
   projection. Limited admin history queries may remain. Check provider egress
   after its hourly refresh, then over a representative day, comparing similar
   traffic. Investigate if total egress exceeds the 6.5 GB/day planning budget.
5. If application rollback is needed, leave the additive function in place; the
   previous server can still read raw events. Removing the function while the
   new server runs re-enables compatibility downloads and loses the main saving.

References: [Supabase egress guidance](https://supabase.com/docs/guides/platform/manage-your-usage/egress)
and [database function permissions](https://supabase.com/docs/guides/database/functions).
