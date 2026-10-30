# Mobile push notifications

New `subscriber_notifications` rows automatically enqueue pushes for that same subscriber's registered phones. The existing story, character unlock, blog and resource publishing workflows remain the source of notification copy, audience and deduplication. There is no separate mobile audience or send action. Existing inbox rows are not replayed when a phone opts in.

Users enable push through **Kennisgewings → Skakel selfoonkennisgewings aan** or **Instellings → Kennisgewings → Selfoonkennisgewings**. Permission is requested only after this action. Settings also supports disabling push and opening the phone's notification settings. Stories and characters open in the app; blogs and resources open on the Schink website. Unknown notification types open the inbox.

## Activation (requires approval)

Implementation and tests are local. No provider credential, live schema, deployment, app distribution or actual recipient send was changed for this work. Live rollout and any test notification require explicit approval under `AGENTS.md`.

1. Apply only `Shink/Database/migrations/20261029135259_mobile_push_notifications.sql` to the Shink project `btpsoyiyhtfbeznonygn`, after approval and a current schema check. This migration does not depend on the other pending egress migrations. It creates server-only device and delivery tables, restricted RPCs and an insert trigger; it does not send or backfill existing notifications.
2. Configure provider credentials securely on the backend. Do not put private keys, service account JSON, registration secrets or device tokens in source control, chat or logs.
3. Deploy the backend with `MobilePush:Enabled=false`. Keep the ASP.NET process running (Azure App Service Always On, if supported by the hosting plan), since its hosted worker drains the queue.
4. Prepare push-enabled mobile builds using the existing signed build scripts, with `SchinkPushEnabled=true` in their environment. Complete the Apple/Firebase prerequisites below first. Increment the build number for each shareable build as usual. Verify the signed output's entitlements and configuration before distribution.
5. Enable only configured providers through backend settings and restart the backend. The mobile capability endpoint hides provider availability until both the master flag and that provider's configuration are present.
6. On an explicitly approved test account/device, opt in, create one new notification using the existing workflow, and check receipt while foregrounded, backgrounded and terminated. Test permission denial, OS-level revocation, sign-out and switching accounts. An approved content publication still targets its existing web audience; use an isolated test notification for an individual-device check.

### Backend settings

`Shink/appsettings.json` contains empty configuration placeholders and `Enabled=false`. Production environment variable names:

| Setting | Purpose |
| --- | --- |
| `MobilePush__Enabled` | Master delivery and registration switch; default `false` |
| `MobilePush__AppleTeamId` | `6DP8F4CY29` |
| `MobilePush__AppleKeyId` | APNs signing key ID; an App Store API key is not a substitute |
| `MobilePush__ApplePrivateKey` | APNs `.p8` PEM; actual newlines or literal `\n` are accepted |
| `MobilePush__AppleBundleId` | `com.schink.stories.mobile` |
| `MobilePush__FirebaseProjectId` | Firebase project containing the Android application |
| `MobilePush__FirebaseServiceAccountJson` | Service account JSON authorized for Firebase Cloud Messaging HTTP v1 |

APNs uses ES256 token authentication over HTTP/2. Android uses scoped Google OAuth and FCM HTTP v1. The existing Google Play billing service account and Apple App Store API settings are separate and are not automatically reused. Only configured platforms are claimed from the queue.

### Apple setup

Enable the Push Notifications capability for the existing Schink bundle ID under the Schink team and regenerate the required distribution/Ad Hoc profiles. Preserve Associated Domains, Sign in with Apple, In-App Purchase and the same signing team. Existing profiles may not include `aps-environment`; a push-enabled build requires profiles that do.

Create or use an authorized APNs signing key for this app/team. Store its private `.p8` only in secure backend configuration. TestFlight, App Store and distribution Ad Hoc builds use `SchinkPushEnvironment=production` (the default). A development-signed device build uses `SchinkPushEnvironment=sandbox`; this selects the entitlement value `development` and registers the device for the sandbox endpoint. Do not infer APNs environment from the Debug/Release configuration.

`SchinkPushEnabled` defaults to `false`, preserving existing signing workflows. When enabled, the project selects `Entitlements.Push.plist` or `Entitlements.Push.Sandbox.plist`. These retain the existing Associated Domains and Sign in with Apple entitlements. Normal builds do not require an APNs-enabled profile until push is intentionally enabled.

### Firebase / Android setup

Register Android package `com.schink.stories.mobile` in the intended Firebase project, enable the Cloud Messaging HTTP v1 API, and grant the backend service account permission to send messages in that project. Download that Android app's `google-services.json` into `Shink.Mobile/Platforms/Android/google-services.json` (ignored by Git). It must belong to the same project as the backend sender.

Set `SchinkPushEnabled=true` for the existing Android build scripts. `SchinkFirebaseConfig` optionally selects a different local configuration path. Push-enabled Android builds fail clearly if the configuration file is missing. Firebase client configuration is bundled; server service account credentials must never be bundled. The package is pinned to `Xamarin.Firebase.Messaging` 124.1.2 because the Firebase 25 series raises Android's minimum API to 23, while demo APKs must retain API 21. Launcher icons and package identity are unchanged.

Android 13+ requests notification permission. Android 8+ uses the **Schink-kennisgewings** channel. FCM requires working Google Play services; devices without those services retain the in-app inbox but do not receive this FCM delivery. Huawei-specific HMS delivery is not implemented.

## Delivery and account behavior

- The insert trigger creates at most one delivery per notification and installation, within the notification transaction. Read, cleared, disabled-account, stale-device and expired notifications are excluded or cancelled. No new delivery is created for an inbox deduplication conflict.
- Registration derives the recipient from the authenticated server session. A random installation capability is kept in SecureStorage; the server stores its SHA-256 hash. Knowing an email or installation ID does not permit reassignment or revocation. Direct access to both tables and every RPC is denied to anonymous and authenticated Data API clients.
- The capability is persisted before attempting registration, so a lost server response still permits later revocation. Logout serializes against registration and revokes the device before clearing authentication. If revocation fails, logout reports a failure and can be retried. Session expiry can revoke through the same capability without an active login cookie.
- Account or token changes rotate the registration ID and cancel old pending deliveries. The worker rechecks the current registration immediately before sending. Android data messages check this registration and local opt-in before displaying. Taps also check the signed-in account. Switching accounts cannot make an old queued notification navigate into the new account.
- A provider-accepted notification may already be in flight when a user signs out or opts out; it cannot be recalled from APNs/FCM. Delivered tray entries are cleared locally on sign-out/opt-out. OS delivery timing is best effort, including when the app is force-stopped or background execution is restricted.
- The worker polls every 30 seconds, claims 20 items with five-minute leases and `SKIP LOCKED`, and sends up to four concurrently. Network failures, rate limits and provider authentication errors retry, honoring bounded `Retry-After` values. After eight attempts or 24 hours, the queue item is terminal. Provider storage TTL is one hour after acceptance.
- APNs collapse IDs and stable Android tray tags use the notification ID to reduce duplicate presentation after retries. Dispatch is at least once; a crash between provider acceptance and queue acknowledgement can retry the same notification.
- Explicit invalid-device responses disable only the matching registration. Generic FCM 404 and provider authentication failures do not erase device tokens. Error storage uses safe codes and contains no raw provider responses. Completed delivery records are removed after 30 days; device tokens remain in the server-only table until replaced or the subscriber is deleted. Inactive devices stop receiving after 90 days without registration refresh.

## Verification

Local verification on 29 October 2026: 324 mobile and notification regression tests passed, including the provider and persistence tests introduced here. The actual SQL migration passed in a disposable PostgreSQL 17 database, including lease recovery and suppression after account changes. The web project and iOS Simulator build passed. Android verification covers push disabled and push enabled with a fake client configuration. Existing ImageSharp advisory warnings and Android SDK target warnings were present; dependency upgrades are outside this change.

Focused tests use generated ephemeral signing keys and fake HTTP handlers; they do not contact APNs, Google OAuth or FCM. They verify signed payloads, audience binding, disabled behavior, error classification, capability hashing, input validation and existing notification generation/navigation. Run:

```sh
dotnet test Shink.Tests/Shink.Tests.csproj --filter 'FullyQualifiedName~MobilePushTests|FullyQualifiedName~MobileNotificationNavigationTests|FullyQualifiedName~SupabaseUserNotification'
```

The PostgreSQL integration test uses a disposable local database named `schink_push_test*`, checks its host/name before creating fixtures, and refuses other databases:

```sh
psql -h 127.0.0.1 -p PORT -d schink_push_test -v ON_ERROR_STOP=1 -f scripts/tests/mobile_push_database.sql
```

It exercises the actual migration, RLS/grants, recipient targeting, deduplication, leases, retry scheduling, registration proof, sign-out revocation, account reassignment, token rotation, invalidation, expiry, read/cleared suppression and disabled accounts. Use a fresh disposable database for each run. No production migration is needed to run it.

Build verification uses iOS Simulator with push enabled and signing disabled, Android Debug with push disabled, and Android Debug with push enabled using a clearly fake local Firebase client configuration. The fake configuration is only a compiler fixture and must never be distributed. Real APNs/FCM receipt, signed distribution profiles and device permission/tap behavior still require approved device testing.

Provider references: [Apple APNs token authentication](https://developer.apple.com/documentation/usernotifications/establishing-a-token-based-connection-to-apns), [Firebase HTTP v1](https://firebase.google.com/docs/cloud-messaging/send/v1-api), [Firebase Android setup and permission](https://firebase.google.com/docs/cloud-messaging/android/client), [Microsoft MAUI native push guidance](https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/push-notifications?view=net-maui-10.0), [Firebase version requirements](https://firebase.google.com/support/release-notes/android#messaging_v25-0-0).
