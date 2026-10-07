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

### Google Play setup on Azure

Google purchase verification requires the Google Play Android Developer API (`androidpublisher.googleapis.com`) to be enabled in the service account's Google Cloud project. A Google sign-in OAuth client and a Play upload key do not provide this access.

Perform all Google billing setup while signed in as `admin@prioritybit.co.za`, including Cloud project ownership, API enablement, service account/key creation, and Play permissions. Use the dedicated `Schink Stories Billing` project (`schink-stories-billing`) under that account.

On 6 October 2026, approved setup created the project under `admin@prioritybit.co.za`, enabled the API, created the billing service account and JSON key, granted Schink-only Play billing permissions, and applied the two Google settings to live Azure. The project's IAM page confirmed `admin@prioritybit.co.za` with the Owner role. The local credential is kept outside the repository in a private directory; it is not included in this document.

The dedicated billing service account is `schink-play-billing@schink-stories-billing.iam.gserviceaccount.com`. Grant it access to **Schink Stories only** in the Schink Play developer account (`8275093652983572360`), with the two billing permissions required by Google's [API setup documentation](https://developers.google.com/android-publisher/getting_started):

- View financial data (the app-level equivalent of the account-level "View financial data, orders, and cancellation survey responses" permission).
- Manage orders and subscriptions.

No Google Cloud project-wide IAM role is needed for these Play permissions. Generate a JSON key for that account and keep it outside the repository. Configure Azure App Service `schink`, resource group `Schink_Stories`, with:

- `MobileStore__GoogleServiceAccountJson`: the complete service account JSON, including its private key.
- `MobileStore__GooglePackageName`: `com.schink.stories.mobile`.

Preserve the existing Apple settings. Store the JSON as a server setting, never in source, chat, shell arguments, or mobile artifacts. If using the Azure CLI, supply settings from a protected temporary JSON file and suppress setting values in command output. Applying App Service settings restarts the live app and requires explicit production approval under `AGENTS.md`. Creating the account/key and granting Play access also require approval before those actions.

Initial inspection on 6 October 2026 found no Google service-account setting in live Azure and no service account in Play Console. The earlier `schink-stories-492109` Cloud project, visible through the personal Google account, was left unchanged. Missing configuration makes first purchases fail verification before persistence and acknowledgment; Google's [billing integration documentation](https://developer.android.com/google/play/billing/integrate#process) requires acknowledgment within three days or the purchase is automatically refunded.

Azure read-after-write verification confirmed the credential and package name exactly and preserved all unrelated settings. The app restarted successfully and the public mobile plans endpoint returned HTTP 200. Play's exported user list confirmed `ACCESS_GRANTED` for this service account, with `CAN_VIEW_FINANCIAL_DATA`, `CAN_MANAGE_ORDERS`, `CAN_VIEW_NON_FINANCIAL_DATA`, and `CAN_VIEW_APP_QUALITY` for `com.schink.stories.mobile` only, and no account-wide permissions. These match Google's [app-level permission definitions](https://support.google.com/googleplay/android-developer/answer/9844686?hl=en).

OAuth authorization and the subscription catalogue returned HTTP 200. Order and voided-purchase lookups returned HTTP 401 `permissionDenied`; bounded retries of a real subscription purchase lookup still returned the same error at 09:33 UTC on 6 October ("The current user has insufficient permissions to perform the requested operation"). Purchase verification and acknowledgment therefore remain unverified; saved permissions and a healthy website alone do not prove the billing connection is ready. Permission propagation is a possible explanation, but its cause and completion time have not been confirmed.

After approved setup, verify OAuth authorization and read-only Android Publisher access before exercising a new purchase through the documented internal test track. Confirm that the purchase is persisted, grants access on the same Schink account, and becomes `ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED`. Check app-close recovery as well. Already-refunded orders cannot be repaired by acknowledging them, and this configuration issue alone does not identify which store or order belongs to an individual customer.

Focused local verification on 6 October 2026: all 13 `MobileStoreDeliveryTests` and `StorePurchaseRecoveryTests` passed; `git diff --check` passed. No new purchase, acknowledgment, customer entitlement change, application deployment, Git push, or store release was performed during configuration repair.

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
