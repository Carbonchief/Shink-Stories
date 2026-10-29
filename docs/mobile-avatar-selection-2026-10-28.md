# Mobile avatar selection — 28 October 2026

The native MAUI app now opens the avatar picker after a new email, Google, or Apple account signs in. Existing accounts continue through their normal sign-in flow. Email accounts requiring confirmation reach the picker after their first successful sign-in.

The picker uses the same 16 portraits and catalogue as the website. Portraits are packaged with the app, laid out in four responsive columns, and clipped to circles. **Begin luister** saves the selected portrait, or asks the server to assign a random portrait when no selection was made, then switches the native Shell root to Luister. The pending step survives session refreshes and app restarts for the same account.

The profile page also offers **Verander profielprentjie**. This opens the native picker with **Save** and returns to the profile page after saving. Unsaved profile text fields are preserved when returning. The header avatar uses a circular shape.

The authenticated mobile save endpoint validates catalogue IDs and the expected signed-in account, writes through the existing subscriber avatar service, and refreshes the existing cookie. Failed writes do not complete onboarding or update the avatar cookie. Google and Apple completion responses identify new accounts; the Google flag travels in the protected short-lived callback payload.

## Verification

- 70 focused tests passed: avatar persistence and endpoint behavior, explicit and random selection, rejected avatar IDs and changed accounts, cookie/session preservation, native pending-state transitions, legacy JSON compatibility, auth redirect recognition, and existing signup/auth regression tests.
- 11 existing mobile platform chrome source checks passed.
- Android Debug build and iOS Simulator Debug build passed with existing platform/package warnings. No application version or signing settings were changed for this task.
- The native screen was installed and rendered in an isolated iPhone 17 Pro simulator using a loopback-only test service and a sample account. Screenshot: `output/avatar-selection/mobile-onboarding-iphone.jpg`.
- HTTP checks against the actual mobile save handler, using real local cookies and an in-memory subscriber store, passed for random assignment, explicit Panda selection, subsequent session refresh, a different-account rejection (409), and an invalid-ID rejection (400). Evidence: `output/avatar-selection/mobile-http-verification.json`.
- `git diff --check` passed.

Simulator accessibility/tap automation was unreliable with the installed Xcode/runtime combination. The rendered picker was visually inspected; a full UI-driven signup → save → Luister → edit sequence was not verified. No real signup, email, provider login, production profile write, or physical-device install was performed.

The website and backend changes remain local. The backend endpoint and auth response updates must be deployed before a distributed mobile build can use this flow against the live service. No migration is required: avatar updates use the existing subscriber image fields. No commit, push or backend deployment was performed.

## Tester-build follow-up

The user requested new tester builds and explicitly chose to leave the live backend unchanged. Build 39 therefore checks the server's `SupportsProfileAvatars` session capability before showing avatar onboarding or the profile change button. Older server responses default to unsupported, so signup continues normally and a previously pending avatar step is cleared instead of blocking access to Luister. The local backend advertises support, allowing the feature to activate after a separately approved deployment without another mobile update.

Release preflight now passes 82 focused tests, including legacy-server JSON and pending-step rollback behavior. Build 1.2 (39) is available on Google Play Internal testing and in both existing TestFlight groups. Tester publishing status and signed artifact verification are recorded separately under `artifacts/mobile-beta-2026-10-28/`.
