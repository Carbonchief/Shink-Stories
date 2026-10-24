# Android Karakters scrolling follow-up — 22 October 2026

The final local build removes unnecessary gallery resets and reduces repeated bar composition while preserving live blur. The emulator comparison shows a modest improvement during ordinary scrolling. Fast-fling p95 remains about 32 ms, so this is not evidence that every dropped frame has been eliminated.

## Changes

- Android now retains the character gallery's collection, header and footer across equivalent cached/network responses. Previously every response replaced the entire ItemsSource and rebuilt the header, forcing native cells and layout to be recreated. Equivalent rows now emit no collection event; changed rows emit a replacement. Real additions, removals or reordering still produce one reset.
- Row comparison includes refreshed preview-audio authorization and related-story metadata. Keeping cells alive must not retain expired playback URLs or stale unlock states.
- Android caches each bar's composed bitmap, tint and fade in a hardware layer between live captures. Every completed capture still invalidates that layer. The existing 32 ms minimum capture interval, scroll notifications, bitmap rotation, blur spread and edge fades remain. There is no wait for scrolling to stop.
- iOS retains its existing collection rendering path and native material. Both platforms remain on MAUI 10.0.120; no further framework upgrade was introduced in this follow-up.
- The benchmark script can require visible account/page text before each run, preventing signed-out or wrong-page comparisons.

This addresses app-side collection and rendering work. It does not establish a new .NET MAUI framework defect. Temporary shader, RenderNode, grid-culling and larger-cache experiments were reverted because they did not show a repeatable benefit. Only the changes above remain.

## Emulator comparison

Android API 35 ARM64 Pixel 6 emulator, 1080 × 2400, density 420, 60 Hz. Every run checked the signed-in `36 van 116` gallery state. Installs were in-place, preserving the account. Each build received an excluded warmup before measured runs. Builds and video recording were kept separate from benchmarks; build intermediates and measurement output used temporary directories to reduce project-folder synchronization activity.

Ordinary scrolling used four 450 ms swipes in each direction per run. Fast scrolling used eight 120 ms swipes in each direction. Frame durations come from Android `dumpsys gfxinfo`; lower is better. The median column is the median of three per-run p95 values, not a pooled percentile.

| Comparison | Three p95 values | Median p95 | Frames over 50 ms / sampled frames |
| --- | --- | --- | --- |
| Original live-blur build, ordinary | 34 / 23 / 19 ms | 23 ms | 8 / 1,063 |
| Final build, ordinary | 18 / 21 / 20 ms | 20 ms | 0 / 1,099 |
| Original build repeated afterward, ordinary | 28 / 21 / 21 ms | 21 ms | 1 / 1,103 |
| Original build, fast | 34 / 32 / 32 ms | 32 ms | 0 / 1,124 |
| Final build, fast | 32 / 34 / 32 ms | 32 ms | 0 / 1,351 |

The first ordinary control suggests a 13% median-p95 reduction, but the repeated original control narrows that to about 5%. Android's modern jank counter was 2.63% for the first ordinary control, 2.18% for the final build and 1.99% for the repeated control. These counters and host variation do not support a uniform performance gain across every metric. Fast-fling p95 was unchanged. No final comparison run contained a frame over 700 ms.

Earlier much slower exploratory runs are retained as diagnostic evidence, not used as matched before/after claims. Host load and cold artwork/loading changed substantially during the investigation. Sustained 60 fps, physical Android performance and active-audio scrolling have not been established.

## Verification

- Android Release build: passed, 13 existing warnings, no errors. The final APK is installed on the emulator.
- iOS simulator Debug build of the final shared source: passed, one existing runtime-identifier override warning, no errors. This follow-up did not repeat iOS runtime profiling; the earlier [iPhone/iPad verification](ios-scroll-verification-2026-10-22.md) remains separate evidence.
- 55 focused tests passed, including four new tests covering unchanged payloads, unlock updates, renewed audio/story metadata, and reorder/removal notifications.
- On-device smoke checks: character profile open/close, Stories → Search → Karakters navigation, scrolling and pull-to-refresh. The gallery retained its signed-in unlock count. The running process log contained no fatal, disposal, illegal-state or segmentation-fault markers.
- A separate twelve-second recording shows the top and bottom materials changing during a slow drag and reverse. Extracted frames at 2 and 3 seconds show different content under both bars while the gesture is active. Recording resolution was 540 × 1200; emulator display settings were unchanged.
- Python syntax verification and `git diff --check` passed.

Raw measurements, screenshots, video, build/test logs and original/final APK hashes are in `artifacts/android-scroll-review/2026-10-22/karakters-followup/final/` (Git ignored). The video is `live-blur.mp4`. Focused TRX results are in the adjacent `test-results/` directory. No credentials are stored in this evidence.

No commit, push, deployment or store release was performed.
