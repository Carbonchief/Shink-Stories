# Android scrolling review — 22 October 2026

The bar refresh strategy from this initial pass has since been replaced. See the [live blur follow-up](android-live-blur-2026-10-22.md) for the current implementation and its verification. The measurements below document the initial scrolling pass.

The initial emulator measurements show faster slow frames on Luister, Karakters, Search and the larger Storie Hoekie playlist. The smaller Bybelstories playlist has approximately unchanged overall frame times. This is a measured improvement, not a claim that every screen now maintains 60 fps.

## Causes and changes

Part of the problem was a confirmed .NET MAUI Android regression. MAUI 10.0.100's grid spacing calculation traverses the adapter repeatedly during layout. The application used that version. Android now uses **10.0.120**, whose release includes the grid spacing fix. This initial pass kept iOS on its existing package version; the subsequent [iOS verification](ios-scroll-verification-2026-10-22.md) updates and tests iOS with 10.0.120 too. An isolated framework-only comparison on the 116-character grid reduced median p95 from 34 to 26 ms; the additional application changes reduced it to 21 ms.

Primary references: [MAUI issue #38925](https://github.com/dotnet/maui/issues/38925), [servicing fix #39335](https://github.com/dotnet/maui/pull/39335), [10.0.120 release notes](https://github.com/dotnet/maui/releases/tag/10.0.120).

The application changes address additional work during scrolling:

- The initial Android glass change reused the blurred bitmap during dragging and flinging, then refreshed after 160 ms without a scroll event. This caused a visibly static blur and has been replaced by the live update strategy described in the follow-up. Hidden/detached surfaces still skip captures.
- Android cached images keep the current image source when a recycled cell requests the same local artwork file, avoiding another image request and layout invalidation.
- Search creates each card hierarchy once and updates its text, artwork, lock state and current tap target when recycled. Its automation ID stays stable, as MAUI rejects changing an existing ID. The accessible title follows the current story.
- Android search and playlist collections use the existing native RecyclerView tuning, with bounded nearby-cell caches and item animations disabled. `HasFixedSize` describes the bounded viewport; item heights remain variable where required.
- Playlist galleries paint the full-screen background once, use a solid brush for equal gradient endpoints, and request the existing smaller card artwork on Android. Hero/detail artwork keeps its original resolution.

Visual style and Afrikaans copy were preserved. Existing unrelated onboarding, paywall and access-refresh work was retained. No Git push, deployment or store release was performed.

## Repeated emulator measurements

Device: API 35 ARM64 Pixel 6 emulator, 1080 × 2400, density 420, 60 Hz, host GPU, 4 virtual CPUs and 2 GiB RAM. Builds: Android Release, `android-arm64`, package `com.schink.stories.mobile`, version 1.1/build 36. The baseline was captured from the task-start working tree, including its existing local changes.

The script resets Android `gfxinfo`, scrolls down and back up, waits two seconds for the final fling/deferred work, then saves frame statistics. Each comparison uses three runs, loaded catalogue artwork and the same gestures. Normal runs use four 450 ms swipes in each direction. The larger-playlist stress comparison uses eight 120 ms swipes in each direction. Start/end screenshots identify the page. Samples that changed app processes or were on the wrong page were discarded.

The table reports the **median of the three per-run 95th-percentile frame durations**. Lower is better; these values are not average FPS.

| Page | Before: p95 in each run | After: p95 in each run | Median before → after |
| --- | --- | --- | --- |
| Luister | 27 / 26 / 26 ms | 24 / 19 / 19 ms | **26 → 19 ms** (27% lower) |
| Karakters, 116 characters | 34 / 32 / 34 ms | 31 / 19 / 21 ms | **34 → 21 ms** (38% lower) |
| Search, `a`, 142 results | 30 / 27 / 30 ms | 20 / 18 / 18 ms | **30 → 18 ms** (40% lower) |
| Storie Hoekie, 78 stories, fast flings | 31 / 25 / 25 ms | 22 / 22 / 21 ms | **25 → 22 ms** (12% lower) |
| Bybelstories, 23 stories | 30 / 22 / 28 ms | 27 / 28 / 28 ms | **28 → 28 ms**, approximately unchanged |

The framework-only Karakters runs were 32 / 26 / 25 ms. Luister/Karakters timings were captured after the shared scrolling fixes; subsequent changes concerned playlist rendering and Search only. The profiled final APK and the installed final APK differ by a final Search accessibility-description placement correction. Rendering, image/cache and scrolling logic are identical between those two builds; both builds' identities are recorded.

The result needs several qualifications:

- Android's modern jank/deadline counter did not improve uniformly: Luister was 1.23% → 2.27%, Karakters 3.01% → 2.87%, Search 2.64% → 3.67%, Storie Hoekie 1.03% → 0.82%. Lower p95 durations do not establish improvement in every metric.
- A repeat baseline for Bybelstories gave median p95 26 ms, versus the initial 28 ms and final 28 ms. Its total frame duration has no demonstrated improvement. Selected frame samples suggest much less UI layout work, with remaining drawing/GPU queue time dominant; that is an inference, not a full native GPU profile.
- A later baseline Search comparison reached 85 / 85 / 81 ms while the final runs were 20 / 18 / 18 ms. The table deliberately uses the earlier, faster baseline. Valid slower intermediate candidates and repeat baselines are retained in the evidence.
- macOS background activity and emulator GPU scheduling varied. Final larger-playlist runs were all below the baseline minimum, but these measurements do not prove equivalent gains on physical Android phones or tablets. No retained primary sample contained a frame above 700 ms.

For the measurement interpretation, see [Android's rendering performance guidance](https://developer.android.com/topic/performance/issues/render).

## Page coverage

All 22 `*Page.cs` files and `MainPage.xaml` were reviewed for scrolling and layout paths. Shared glass/image changes apply beyond pages with direct edits.

| Surface | Android runtime coverage |
| --- | --- |
| Luister | Repeated vertical scrolling; catalogue and horizontal playlist navigation |
| Karakters | Repeated grid scrolling; unlocked Roomie profile opened, scrolled and closed |
| Playlist galleries | Bybelstories (23) and Storie Hoekie (78), repeated scrolling and direction reversals |
| Playlist player | 23-track list scrolling; visible Back button returns to the gallery |
| Search | 142-result scrolling, empty query result, different query, correct selected story opens |
| Story detail | Die Goue Kalf detail opened after changing Search query |
| Downloads | Existing two-story list inspected; no delete/download actions |
| Settings / Profile | Scrolled; profile data not edited or saved |
| Karakter-pare | Configuration scrolled and beginner board opened; no game completed |
| Karakter Raai | Configuration scrolled and beginner stage opened; no round completed |
| Notifications | Cached list scrolled offline to avoid automatic server read-state updates; app stopped before reconnecting, Wi-Fi/data restored |
| Account / Onboarding | Account/sign-in flow and initial onboarding screen observed |

Plans' parent-gated paid-account branch, remaining onboarding slides, StoryFullscreen and the legacy Home/Gratis/About/MainPage routes received source/build review rather than full runtime interaction. Legacy routes are not exposed by the current Shell navigation. Active audio playback, purchases/restores, all device sizes and physical-device performance were not profiled.

## Verification and reproduction

- Android Release build: passed, 13 existing warnings, zero errors.
- iOS simulator Debug compilation: passed, one runtime-identifier override warning, zero errors. This is compilation coverage, not an iOS scrolling benchmark.
- Targeted scrolling/image/chrome/search/iOS collection/playlist regression checks: **51 passed**.
- Broader mobile/character selection: **305 passed, 4 failed**. The remaining source assertions expect older code in files untouched by this scrolling change: `MobileLuisterPullToRefreshUsesUiSafeCancelableLoadPath`, `MobileLuisterCachesStoryDataForFastColdStart`, `MobileStoryDetailUsesNativePlayerAndDirectR2AudioUrls`, and `MobileAppCapturesPostHogAnalytics`.
- `git diff --check` and Python compilation of the benchmark script passed.
- Final APK installed in place and launched on the emulator; the signed-in session was retained. No uninstall/data clear was used.

Navigate to the desired page and start position, wait for artwork, then run:

```sh
python3 scripts/measure-android-scroll.py /tmp/schink-scroll-check
# Larger-playlist stress gestures:
python3 scripts/measure-android-scroll.py /tmp/schink-scroll-fast --duration 120 --swipes 8
```

Use the same emulator, start position, data, build configuration and gesture settings for both builds. Store live measurements outside cloud-synced folders, then copy the evidence afterward. The script only scrolls the foreground app; page navigation, sign-in and APK installation are separate.

Raw screenshots, per-run `gfxinfo`, summaries, APK hashes and verification logs are retained locally under `artifacts/android-scroll-review/2026-10-22/` (Git ignored). `comparisons.json` includes the earlier valid candidates as well as the final results. No credentials are included in this report or benchmark script.
