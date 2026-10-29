# Android scrolling verification — 28 October 2026

The current Android Release build was installed and tested on the API 35 ARM64 emulator. Luister scrolling, horizontal carousel interaction and live top/bottom blur work with the existing Android optimizations. No additional Android runtime or UI change was necessary for the iPhone follow-up.

## Execution mode

The iPhone improvement removed interpreter overhead from a locally signed **Debug** build. Android's demo APK and Google Play scripts already publish **Release**, which selects `Optimize=true`, `RunAOTCompilation=true` and `AndroidEnableProfiledAot=true`. Both evaluated Release configurations have `AndroidUseInterpreter=False` and `AndroidAotMode=Normal`.

The built APK contains 117 ARM64 AOT libraries, including `libaot-Shink.Mobile.dll.so` and the MAUI Controls libraries. Its generated native environment confirms `uses_mono_aot=true` and AOT mode `normal`. This is Mono's profiled AOT with JIT fallback. The iPhone-specific LLVM/signing-profile settings were not copied to Android. Microsoft's [Android build-property documentation](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties) describes the Release AOT defaults and profiled AOT settings.

Normal Android Debug remains a debugging configuration and should not be used for a shipping-performance comparison. The project still uses MAUI 10.0.120.

## Emulator checks

- Built current source for `net10.0-android`, Release, `android-arm64`: zero errors, 13 existing platform/build warnings.
- Installed version 1.2/build 38 in place with the existing local emulator signing key. Package identity is `com.schink.stories.mobile`; the installed package is not debuggable. No uninstall or data clear was performed. The version numbers were already present and were not changed by this verification.
- The expired session required direct sign-in before testing. Measurements were taken with the loaded Luister feed visible, including its showcase and carousel rows.
- Excluded one warm-up run, then measured three normal and three fast runs using `scripts/measure-android-scroll.py`. The script checks the foreground page and stable app PID throughout the gestures.

| Gesture profile | Gestures per run | 95th-percentile frame times across three runs | Frames over 50 ms per run |
| --- | --- | --- | --- |
| Normal, 450 ms | Four up, four down | 25 / 19 / 20 ms | 0 / 2 / 2 |
| Fast, 120 ms | Eight up, eight down | 34 / 28 / 32 ms | 6 / 5 / 5 |

There was no app restart during the measurements. The gestures return to the top and can exercise pull-to-refresh as well as the feed. These are current-build emulator measurements, not a matched before/after improvement percentage or proof of sustained 60 fps. Raw modern/legacy jank counters are retained with the frame histograms; emulator GPU percentile buckets are not used as evidence.

The first carousel's title returned to the same left position (49 physical pixels) after a forward swipe and reverse swipes. A separate 13-second recording used two-second drags in both directions. Frames sampled **during** those drags show both bar backdrops changing with the content. The live blur cadence and appearance were retained; the bars do not wait for scrolling to stop before refreshing.

A separate Karakters smoke check completed four up/four down gestures with its character grid loaded and a stable app PID. This was a navigation/scrolling regression check, not a matched performance comparison.

All 38 focused Luister safe-area, collection-handler, performance/reliability and platform-chrome source checks passed. The app's crash buffer was empty after the interaction checks. Physical Android hardware was not connected, so the runtime interaction proof is limited to the emulator.

## Evidence and scope

Build properties, package verification, raw frame statistics, test results, carousel screenshots/XML and the live-blur recording are stored in this chat's local evidence directory under `android-runtime-2026-10-28`. The APK is a local emulator test build, not a shareable demo or Google Play upload.

No Android source change, commit, push, store upload or release was performed. Existing unrelated working-tree changes were preserved.
