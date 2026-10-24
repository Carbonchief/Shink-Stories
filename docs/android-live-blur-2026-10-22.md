# Live Android bar blur — 22 October 2026

The top and bottom bars now refresh while content moves. This replaces the initial 160 ms idle debounce, which made the blur appear frozen until scrolling stopped. The existing tint, opacity and edge fades remain; iOS continues to use its existing native material.

The later [Karakters follow-up](android-karakters-scroll-followup-2026-10-22.md) preserves live blur, removes unnecessary gallery resets and adds a matched emulator comparison, including the remaining fast-fling limitation. The measurements below describe the earlier stage of the investigation.

The shared Android material now uses:

- A coalesced refresh with a 32 ms minimum interval, without extending the deadline for every scroll event. Actual refresh frequency also depends on frame scheduling.
- A reusable animation callback that posts the capture to the UI queue after the frame's layout/draw work, rather than doing the snapshot inside the animation callback.
- Three small bitmap buffers, keeping the displayed image separate from the one being captured and blurred. Canvases, pixel arrays and location arrays are reused.
- A smaller image with three radius-one blur passes. Its blur spread is approximately the same as the previous larger buffer.
- The visible scrolling viewport and its background layers as the preferred capture source. A root capture remains available when no suitable viewport exists.
- Checks for actual visible bounds, including cached Shell pages outside the screen. Detached callbacks and native buffers are released.

All changes for this follow-up are in `MobileLiquidGlass.cs` and its existing platform-chrome source checks. Earlier MAUI 10.0.120, image reuse, Search recycling and playlist changes remain in place. No UI copy, account data, deployment or store release was changed.

## Verification

Android Release compilation passed with 13 existing warnings and no errors. iOS simulator Debug compilation passed with its existing runtime-identifier override warning. All 51 targeted scrolling, image, chrome, Search, iOS collection and playlist checks passed. `git diff --check` passed.

The subsequent [iOS verification](ios-scroll-verification-2026-10-22.md) updates iOS to MAUI 10.0.120 and adds actual iPhone/iPad simulator coverage. A later Android Karakters repeat after Steam was closed still showed p95 values of 117 / 117 / 150 ms. Both iOS simulators were shut down before that repeat. Closing Steam did not eliminate the spikes, so background load alone does not explain or resolve the remaining Android problem. A framework-only comparison was aborted on the sign-in screen after a build swap and is excluded from the results; no before/after claim is based on that invalid run.

The Android API 35 emulator retained its signed-in session through in-place installs. The measurement device remained 1080 × 2400 at density 420, with a 60 Hz display. A twelve-second recording demonstrates the material changing during a slow drag and its reverse; it was encoded at 540 × 1200 without changing the emulator's display settings. Recording was separate from the frame benchmarks.

## Measurements and limits

Each entry below is three runs of four 450 ms swipes in each direction. Values are per-run 95th-percentile frame durations; lower is better.

| Build / page | Three p95 values | Median |
| --- | --- | --- |
| Initial static blur / Luister | 25 / 19 / 18 ms | 19 ms |
| Repeated static blur / Luister, later host load | 36 / 32 / 36 ms | 36 ms |
| Live blur with buffer rotation / Luister | 21 / 18 / 32 ms | 21 ms |
| Live blur with buffer rotation / Karakters | 93 / 85 / 85 ms | 85 ms |
| Earlier live implementation, MAUI 10.0.120 only / Karakters, repeat under current conditions | 150 / 125 / 150 ms | 150 ms |

Luister returned close to the initial static version while keeping the material live. Karakters still had substantial spikes in the later tests. The earlier live implementation also became much slower under the later conditions, so these results do not isolate a universal speed gain or establish smooth 60 fps. Host CPU/GPU contention and first-visit artwork loading affected the emulator. Initial low-load results from the [scrolling review](android-scroll-performance-2026-10-22.md) must not be treated as matched controls for these later runs.

GPU RenderEffect trials and a direct RenderNode capture were investigated. The direct prototype exposed a recursive capture through Shell's paging container; it was discarded. Other GPU trials added rendering cost on this emulator. A root capture specifically for the character grid also performed worse and was reverted. None of those RenderNode implementations or page wrappers remain in the final source.

Raw measurements, start/end screenshots, intermediate candidates, verification logs, video and APK identities are retained locally in `artifacts/android-scroll-review/2026-10-22/live-blur-correction/` (Git ignored). The video and measurements contain no sign-in credentials. Physical Android performance and active-audio scrolling have not been profiled.
