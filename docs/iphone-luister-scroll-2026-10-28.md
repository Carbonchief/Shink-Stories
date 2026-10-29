# Luister scrolling on Luan iPhone — 28 October 2026

The optimized local build was installed on Luan iPhone 15 Pro, and the user reported **“Noticeably smoother”** after the requested scrolling test. The feed layout, carousel gutters and live top/bottom blur were not changed in this follow-up.

## Finding and change

The previous local Ad Hoc build used MAUI's Debug defaults: `UseInterpreter=True`, `MtouchInterpreter=all` and `Optimize=false`. The physical-phone trace also showed `mono_interp_exec_method` executing on the app's main thread. Microsoft documents the [Debug interpreter default and its execution-speed cost](https://learn.microsoft.com/en-us/dotnet/maui/macios/interpreter?view=net-maui-10.0).

The project now selects optimized ahead-of-time compilation when all three conditions match: `net10.0-ios`, `ios-arm64`, and the exact `Schink Stories Luan iPhone Ad Hoc` signing profile. This preserves the existing Debug install workflow while setting `UseInterpreter=false`, clearing `MtouchInterpreter`, and enabling C# optimization and LLVM. Normal simulator debugging, other signing profiles and Android retain their existing settings. Store Release builds already use their own optimized configuration.

The simulator/Android property checks confirmed that the new group does not select those targets. MAUI remains at 10.0.120, with the existing stable iOS collection handler and carousel inset correction. No image-quality reduction or static blur workaround was introduced.

## Physical-device verification

- Device: Luan iPhone 15 Pro, iOS 27.0.1, connected by cable.
- Installed app: version 1.2/build 38, updated in place with the existing Schink team/profile. The version numbers were already present in the working tree and were not incremented by this change.
- Signature and full Ad Hoc entitlements verified; installation and normal launch both verified. The running replacement process was confirmed separately.
- The final 30-second native Time Profiler capture contains 6,902 CPU samples for the replacement app, including 6,601 main-thread samples spread throughout the test. No `mono_interp_exec_method` frame occurs in those app main-thread stacks. This supports the build-mode verification; sample totals are not a before/after speed ratio.
- The user confirmed that Luister scrolling felt noticeably smoother after the test.
- All 38 focused Luister safe-area, iOS collection-handler, scrolling and chrome regression checks passed. The project-file diff passed whitespace checks.

The initial completed capture established interpreter use and included an app update of about 66 ms, but had too little active Luister scrolling for a matched performance comparison. A longer capture with Schink inactive and an incomplete recorder run were excluded. The incomplete run was discarded. No FPS percentage, sustained 60/120 fps result or complete elimination of every possible hitch is claimed.

## Local build workarounds and evidence

Native compilation completed, but debug-symbol sidecar generation stalled during packaging. The successful local retry skipped that sidecar step (`NoDSymUtil=true`) while retaining native symbols in the executable (`NoSymbolStrip=true`). These two retry arguments were temporary and are not project defaults. The familiar FinderInfo signing failure was then handled with the documented metadata-clean copy and full-entitlement re-sign; deep signature verification passed.

Completed native traces are kept outside the synced project directory in this chat's local evidence folder. Filtered app-only summaries, evaluated build properties, binary identity, build/install logs and focused TRX results are also retained. Detailed CPU hotspot attribution is limited by unresolved symbols in some native frames; the trace was used to verify execution mode, not to assign every remaining frame cost.

No profiler code was added to the app. No commit, push, deployment, TestFlight upload or store release was performed. Other existing working-tree changes were preserved.
