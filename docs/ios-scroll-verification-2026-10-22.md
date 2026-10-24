# iOS scrolling verification — 22 October 2026

The later [Luister carousel correction](luister-carousel-gutter-2026-10-22.md) fixes the gutter loss observed after this verification, with a specific before/after reproduction and native layout measurements.

iOS now uses **.NET MAUI 10.0.120**, matching Android. The shared package version was moved to the main property group; Android's effective version is unchanged. The existing stable iOS `CollectionViewHandler` and disabled native prefetch remain in place on both phones and tablets because of the previously observed `TemplatedCell2` / `NativeHandle` crash.

The [official 10.0.120 release notes](https://github.com/dotnet/maui/releases/tag/10.0.120) include iOS ScrollView offset preservation and measure-loop fixes, plus CollectionView layout fixes. Some CollectionView fixes concern the newer handler and therefore do not apply to this app's retained legacy handler. The Android grid spacing regression is platform specific; it is not evidence of the same defect on iOS.

iOS bars continue using native `UIVisualEffectView` / `UIBlurEffect`. The Android bitmap capture and scheduling changes are excluded from iOS compilation. No new iOS blur timer, frozen snapshot, or scroll-idle refresh was introduced.

## Runtime checks

The current source was built with .NET SDK 10.0.301 for `net10.0-ios`, Debug, `iossimulator-arm64`, using Xcode 27.1. All resolved Microsoft.Maui packages in that target are 10.0.120. Local version remains 1.1/build 36.

| Simulator | Checks and observed result |
| --- | --- |
| iPhone 17e, iOS 27.0 (`Schink Onboarding QA`) | Signed in; Luister and the 116-character grid scrolled with slow drags, fast flings and direction reversals; top and bottom blur changed during movement. Search rendered and scrolled 115 results for `die`; changing to `goue` after recycling opened the correct Die Goue Kalf detail, then returned to Search. The 78-story Storie Hoekie gallery and track list scrolled both ways, then returned to the gallery and feed. |
| iPad mini (A17 Pro), iOS 27.0 (`Schink Onboarding Tablet QA`) | Signed in; Luister and Karakters scrolled both ways, including fast flings and reversals. The larger layout, artwork and native blur remained present, and the app stayed responsive. |

The captured application logs for both sessions contain no unhandled exception, `TemplatedCell`, `NativeHandle`, fatal error, or constraint warning. Simulator accessibility/IDE-connection notices are present. Story detail was loaded without starting audio; playback, purchases and physical-device behavior were not tested in this pass.

These are functional and visual simulator checks, not an iOS before/after frame-time benchmark or a claim of sustained 60 fps. The iPad runtime received a narrower Luister/Karakters smoke check. Older iOS versions and a signed physical-device build remain unverified.

## Build and regression verification

- Simulator compilation: passed, zero errors; one existing command-line RID override warning.
- Focused scrolling, image caching, bars, Search, iOS collection-crash and playlist checks: **51 passed**.
- `git diff --check`: passed.
- Both installed simulator apps launched successfully and preserved app data.

The first unsigned simulator bundle was rejected before application startup with a code-signature error. An explicitly signed build also hit this workspace's existing Finder metadata problem. Copying the compiled bundle byte-for-byte to a clean temporary folder and ad hoc signing its native libraries and app resolved simulator launch; signature verification passed. This was a local simulator packaging issue, not a managed application crash or a production signing change.

Both QA simulators had an old `mobile_api_base_url` pointing to `http://[::1]:9`; only that local test preference was restored to the normal API address, without clearing account data or changing app source defaults.

Evidence is retained under `artifacts/ios-scroll-review/2026-10-22/`: build/test logs, TRX results, package/binary identities, UI observations, runtime logs, `iphone-live-blur.mp4`, `iphone-playlist-scroll.mp4`, and `ipad-live-blur.mp4`. No store upload, release, deployment, commit or push was performed.
