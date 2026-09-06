# Mobile store delivery and shared access

## Account access

The server's subscription ledger and `StoryAccessPolicy` control story access on both mobile and web. A valid paid website subscription bypasses the mobile paywall, including Storie Hoekie. Its existing tier remains unchanged: Storie Hoekie unlocks its included stories; full-access and school tiers unlock their allowed catalogue. A story outside a paid user's tier displays an explanation, not a purchase screen. Gratis/free accounts can see the native paywall.

Both mobile store products (`schink_stories_maandeliks`, `schink_stories_jaarliks`) map to full-access tiers in that same ledger. No client-reported purchase grants access without provider verification and durable persistence.

## Delivery and recovery

- Apple successful transactions remain in StoreKit until server delivery succeeds. A startup transaction observer and foreground recovery handle interrupted purchases. Deferred approval returns a pending message; completed approvals are processed on updates/next app opening. Recovery never invokes the iOS restore prompt automatically.
- Android initial subscriptions are acknowledged by the server after access is saved. The client retains its finalization fallback. Failed acknowledgment is retried by the server worker even if the app closes, once the purchase has reached the ledger.
- Foreground recovery checks outstanding purchases on startup, account change, resume, and every 30 seconds while foregrounded. Manual restore continues past expired/rejected receipts and individual failures. Store operations are serialized; product queries, restore, and finalization have bounded timeouts.
- A subscription's owner cannot be overwritten by an upsert race: store inserts ignore conflicts, and subsequent updates require the existing subscriber ID. Ownership lookup errors fail closed. Google's obfuscated account identifier is validated when supplied. Requests also check that the authenticated account still matches the account that initiated delivery.
- Inactive provider results are distinguished from unavailable/invalid responses. The latter are retried and do not trigger revocation.
- Paywall prices come from the native storefront. Unavailable prices show no rand fallback and cannot be purchased; a reload button is available. The fixed two-month saving claim was removed because prices differ across countries.

## Renewal, expiry, and refund reconciliation

`StoreSubscriptionReconciliationWorker` starts with the server and polls every five minutes. It pages through only Apple/Google subscriptions, rechecks provider state, and updates the shared tier, expiry and active/cancelled state. Website payment-provider rows are excluded. Compare-and-update filters protect a newer purchase/restore from a stale worker response. Apple monthly/yearly switches are followed only within the same original transaction lineage. Linked Google tokens are superseded only for the same subscriber.

This implementation uses provider polling; it does not require enabling unimplemented webhook URLs. Renewals/refunds can take up to the polling interval plus processing time to appear. A first purchase that has not yet reached the server is recovered from the native store when the app opens; banking alone cannot verify that flow.

## Required server configuration

Keep all credentials server-side:

- `MobileStore__AppleIssuerId`, `MobileStore__AppleKeyId`, `MobileStore__ApplePrivateKey`: valid authorized Apple In-App Purchase key. `MobileStore__AppleBundleId`: `com.schink.stories.mobile`.
- `MobileStore__GoogleServiceAccountJson`: service account with Android Publisher API access to Schink Stories, including purchase verification and subscription acknowledgment. `MobileStore__GooglePackageName`: `com.schink.stories.mobile`.
- Existing `Supabase__Url` and server secret with subscription ledger access.

No new database migration is needed; the existing unique `(provider, provider_payment_id)` key is used. No credentials were added to source. Production credentials were not validated by the local implementation tests.

## Rollout and acceptance

Local validation on 6 September 2026: 114 focused tests passed, including shared-ledger regressions, ownership failures/races, Apple signed revocation versus outage, Google lifecycle/acknowledgment/reconciliation, restore fault isolation, and paid website paywall routing. The iOS simulator Debug build passed (one existing runtime-identifier warning); the Android Debug build using Google Play configuration passed with zero warnings/errors. `git diff --check` passed. No live deployment, store upload, or actual transaction was performed.

Deploy the server changes before distributing the new clients. The entitlement response adds optional fields and remains compatible with existing requests. Publish new store builds through the documented signing workflows only after approval; the source changes do not update the already-uploaded build 27.

Before submission, use TestFlight on iPhone/iPad and a Play-installed internal-test Android build. Verify:

1. Paid website Storie Hoekie and full-access accounts skip the paywall and retain the correct story limits; a gratis account sees the paywall.
2. Monthly and annual purchases unlock all mobile stories and the website on the same account.
3. Cancellation and pending approval do not grant premature access; approval and interrupted purchase recover on reopening.
4. Expired purchase history does not stop restoration of a valid purchase; another Schink account cannot take ownership.
5. Failed verification/finalization recovers; Google acknowledges after persistence.
6. Renewal, grace, expiry, refund and monthly/yearly switching update shared access after reconciliation.

Local tests and builds establish implementation behavior, not successful real-store transactions. Owner account activation, server deployment/configuration, and new app builds remain release gates.
