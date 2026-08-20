# AGENTS.md

Project-specific instructions for agents working in this repository.

## 1) Working Rules
- Read and follow this file before changing UI, content, audio, admin, email, Supabase, deployment, or publishing behavior.
- Keep changes narrow and preserve the existing visual style unless the user explicitly asks for a redesign.
- Afrikaans-first copy and tone are required for public-facing content.
- Mobile responsiveness is required for all page changes.
- Do not rename technical identifiers, namespaces, slugs, or asset names just because visible branding uses `Schink`.
- If the user asks only to inspect, review, or check something, do not make changes.

## 2) Production / Live Safety
- Never publish, deploy, release, or otherwise promote changes to Production/Live without explicit user approval.
- Never run production data migrations, destructive database operations, or live content changes without explicit user approval.
- `git push` is allowed only when the user explicitly asks to push, sync, publish a branch, or otherwise requests the Git remote update.

## 3) Branding and Navigation
- Header must use the text logo only: `/branding/schink-logo-text.png`.
- Do not re-add the extra header tagline text.
- Main nav currently uses:
  - `Meer oor Ons` -> `/meer-oor-ons`
- Free stories are no longer a separate `/gratis` navigation route; keep free and paid story discovery under the `/luister` root.

## 4) Footer (All Pages)
- Footer background color must be `#222222`.
- Show the text logo in the footer.
- Show social links as icon buttons, not text links, below the logo:
  - Facebook: `https://www.facebook.com/SchinkPublishing`
  - Instagram: `https://www.instagram.com/schinkpublishing/`
- Footer copyright text is fixed to: `© 2023 Schink`.

## 5) Home Page
- Main hero image is `/branding/Schink_Stories_01.png`.
- White logo above hero image is `/branding/schink-stories-logo-white.png`.
- Hero logo and image should visually blend.
- Current overlap target is 50px for desktop and mobile.

## 6) Meer Oor Ons Page
- Route: `/meer-oor-ons`.
- Hero image: `/branding/Schink_Die_Ware_Wenner_Schink_Stories_600x600.png`.
- Keep all established copy blocks from the live source page adaptation.
- Keep founder image above founder text:
  - `/branding/Matin-Profile-Photo.webp`
- Keep this section before `Ons is Martin & Simone`:
  - Header: `Wie ons is`
  - Image: `/branding/Schwella.webp`
- Promise section requirements:
  - Header: `Ons Belofte aan Ouer & Kind`
  - Promise text remains in that block
  - Centered panda image below promise text: `/branding/Panda.webp`
  - Panda image size should match Schwella image size and use rounded corners
- `Wat ouers se` should remain a review-card block style.
- Review display names stay in this order:
  1. `Renske` with role `Arbeidsterapeut` below name
  2. `Sivonne`
  3. `Elmarette`
- Review text should not include double quotation marks.

## 7) Audio Protection
- Do not expose public static audio URLs for playback.
- Browser-facing playback markup should start from signed, expiring URLs at `/media/audio/{slug}?token=...`.
- R2-backed audio should redirect or stream directly from signed R2 read URLs after `/media/audio` token and access checks so app-server egress stays low.
- Legacy local audio files may still be served from the server-side `Stories` folder.
- Public direct audio file access under `/stories/*` for common audio extensions is blocked.
- Keep right-click suppression on the player area and audio element.
- Keep `controlslist` restrictions on audio where supported.
- Keep no-cache and same-origin oriented response headers on the `/media/audio` authorization endpoint where applicable.
- Keep rate limiting enabled for the audio stream endpoint.
- When touching audio playback or routing, verify that generated markup does not expose public static audio file URLs and that R2 delivery uses signed, expiring read URLs only after app-side authorization.

## 8) Admin Page Localization
- The `/admin` page must support both Afrikaans and English.
- Any new admin UI copy must be added in both languages, not one language only.
- Keep the admin language toggle and persisted preference behavior working when making admin changes.

## 9) Email
- When creating new emails through Resend, always use published Resend Email Templates instead of inline HTML/text payloads.
- Ask which sending account or sending method to use before sending email.

## 10) Supabase MCP Setup
- This project uses the Supabase MCP server for project ref `btpsoyiyhtfbeznonygn`.
- Add the server to Codex with:
  - `codex mcp add supabase --url 'https://mcp.supabase.com/mcp?project_ref=btpsoyiyhtfbeznonygn'`
- Ensure remote MCP client support is enabled in `~/.codex/config.toml`:
  - `[mcp]`
  - `remote_mcp_client_enabled = true`
- Authenticate the server with:
  - `codex mcp login supabase`
- Verify the connection with:
  - `codex mcp list`
  - `codex mcp get supabase`
- Optional: install the Supabase agent skill for Postgres best practices with:
  - `npx skills add supabase/agent-skills`
- Current installed skill path on this machine:
  - `~/.agents/skills/supabase-postgres-best-practices`

## 11) Mobile APK Demo Builds
- Keep the mobile package ID fixed at `com.schink.stories.mobile`.
- Keep Android demo APKs signed with the same stable release/demo keystore each time; changing the signing key forces clients to uninstall and lose app data.
- Build demo APKs in the Huawei-compatible shape that worked on the Huawei P30 Pro: `targetSdkVersion=35`, `minSdkVersion=21`, and Android runtime identifiers `android-arm;android-arm64`.
- Use `scripts/build-mobile-demo-apk.sh` for shareable APKs; it copies the signed artifact to `artifacts/mobile-demo/schink-stories-mobile-demo-release-v{ApplicationVersion}-huawei.apk`.
- Increment `Shink.Mobile/Shink.Mobile.csproj` `ApplicationVersion` before producing every shareable APK so Android can install it over the previous APK as an update.
- Tell demo clients to install the new APK over the old one instead of uninstalling first, otherwise Android removes the remembered account/session data.

## 12) Google Play Console
- Sign in to Google Play Console with `admin@prioritybit.co.za` and select the `Schink` organization developer account.
- Confirmed Play Console identifiers:
  - Developer account ID: `8275093652983572360`
  - App name: `Schink Stories`
  - Play app ID: `4973766880920266709`
  - Android package: `com.schink.stories.mobile`
- The app is configured as an `App`, `Free`, with `Afrikaans – af` as its default language.
- Use the `Internal testing` track for non-production Google Play testing. It supports up to 100 testers and must remain separate from Production.
- The `Schink Internal Testers` list contains `admin@prioritybit.co.za`, `schinkpicsend@gmail.com`, and `simoneschwella@gmail.com`.
- Tester opt-in URL: `https://play.google.com/apps/internaltest/4700359835935368853`.
- Current setup state as of 2026-08-15: internal release `12 (1.0) – Google-aanmelding regstelling` is active and available to internal testers. The app is not released to Production, open testing, or another public track.
- Mobile Google OAuth uses the registered `schinkstories://auth/google` callback in every build configuration; the server's HTTPS callback remains the OAuth-provider bridge before returning the short-lived mobile token to the app.
- Android app icons are wired explicitly in `Shink.Mobile/Platforms/Android/AndroidManifest.xml` to `@mipmap/schink_appicon` and `@mipmap/schink_appicon_round`; keep both references so Android does not fall back to the default puzzle-block icon.
- Google Play's store-listing icon is a separate asset from the launcher icon. Keep `Shink.Mobile/Resources/AppIcon/schink_appicon_playstore.png` at 512x512px and under 1 MB, and submit it through the Default store listing after the required descriptions, feature graphic, and screenshots are complete.
- The Default store listing draft now contains the Afrikaans descriptions, the store-listing icon, and a 1024x500 feature graphic. The listing still needs truthful phone screenshots before it can be completed; do not invent screenshots or submit the draft as a public release.
- On 2026-08-12 the user explicitly approved a fresh Google Play signing lineage because no existing demo installs need in-place upgrades. Google Play may generate and retain the app-signing key for this app.
- The Google-managed app-signing private key is not required on development machines. Windows and macOS use the shared upload keystore to sign bundles submitted to Play.
- Upload key details (never commit the keystore or password):
  - Filename: `schink-stories-play-upload.keystore`
  - Alias: `schink-stories-play-upload`
  - Windows path: `%USERPROFILE%\.android\schink-stories-play-upload.keystore`
  - Windows recovery backup: `%USERPROFILE%\Documents\Schink\Google Play Signing Backup\schink-stories-play-upload.keystore`
  - Google Drive recovery backup: `admin@prioritybit.co.za` My Drive root, filename `schink-stories-play-upload.keystore`
  - Windows Credential Manager target: `Schink Stories Google Play Upload Key`
  - Google Password Manager account: `admin@prioritybit.co.za`
  - Google Password Manager entry: website `https://play.google.com`, username `schink-stories-play-upload`
  - macOS path: `$HOME/.android/schink-stories-play-upload.keystore`
  - macOS Keychain service: `Schink Stories Google Play Upload Key`
  - macOS Keychain account: `schink-stories-play-upload`
  - Upload certificate SHA-256: `88:22:D2:69:CF:3E:81:EE:E0:9C:02:B1:EF:64:6F:59:91:AF:03:7C:9A:D5:F0:0C:13:B8:70:8C:F6:89:60:B0`
- Use `scripts/build-mobile-play-aab.ps1` on Windows and `scripts/build-mobile-play-aab.sh` on macOS. Both read the upload-key password from the operating-system credential store and do not print or commit it.
- On a new Mac, download the encrypted keystore from the `admin@prioritybit.co.za` Google Drive backup, place it at `$HOME/.android/schink-stories-play-upload.keystore`, retrieve the saved password from that account's Google Password Manager entry, and add it in Keychain Access using the documented service and account. Then run `scripts/build-mobile-play-aab.sh`.
- Before submitting a bundle from any new machine, verify that the upload certificate SHA-256 matches the documented fingerprint.
- Never upload a bundle signed with an unverified local default/debug certificate.
- Never roll out to `Production`, open testing, or another public track without explicit user approval.

## 13) Physical Install on Luan iPhone 15 Pro
- Use the connected device `Luan iPhone 15 Pro` with identifier `14ED89FE-8F9D-5A68-ABFF-2E70A158253C`. Confirm it is available with `xcrun devicectl list devices` before building.
- Preserve the installed app and its local data: it is signed as `6DP8F4CY29.com.schink.stories.mobile`. A build signed by another team cannot update it; do not uninstall it just to work around a signing mismatch without explicit approval.
- Sign an in-place device build with the `SCHINK PTY. LTD. (6DP8F4CY29)` Apple Distribution identity and the active `Schink Stories Luan iPhone Ad Hoc` profile. That profile includes the registered phone and the required Associated Domains, In-App Purchase, and Sign in with Apple capabilities.
- If the Ad Hoc profile is not installed locally, download it from Apple Developer → Certificates, Identifiers & Profiles → Profiles, then copy it to `~/Library/MobileDevice/Provisioning Profiles/` immediately before the build. Never substitute a Personal Team or another project profile.
- Build the current source for `net10.0-ios`, `Debug`, and `ios-arm64`, explicitly supplying the Schink team, distribution identity, and the Ad Hoc profile UUID. Verify the build output reports `App Id: 6DP8F4CY29.com.schink.stories.mobile` and the `Schink Stories Luan iPhone Ad Hoc` profile.
- macOS may reject the generated app bundle because of `com.apple.FinderInfo` metadata even after compiling it. In that case, make a fresh temporary copy with `ditto --norsrc --noextattr`, then re-sign the copy with the Schink distribution identity before installing. The re-signing entitlements must include the profile's `application-identifier`, team identifier, `get-task-allow=false`, and keychain-access groups, in addition to the app's Associated Domains and Sign in with Apple entitlements; otherwise iOS rejects the app.
- Install and launch with:
  - `xcrun devicectl device install app --device 14ED89FE-8F9D-5A68-ABFF-2E70A158253C <signed-app-path>`
  - `xcrun devicectl device process launch --device 14ED89FE-8F9D-5A68-ABFF-2E70A158253C com.schink.stories.mobile`
- Verify both states separately: `xcrun devicectl device info apps --device 14ED89FE-8F9D-5A68-ABFF-2E70A158253C` confirms installation; the launch command confirms the handoff to iOS. A console-attached launch stops the app when the console session is terminated, so finish with one normal launch.
- This is a local Ad Hoc device install only. It does not upload to TestFlight or release anything to Production.

## 14) iOS TestFlight Publishing
- Use Xcode Organizer for Schink Stories TestFlight uploads. The verified workflow is Xcode, not Transporter.
- App identity:
  - Bundle ID: `com.schink.stories.mobile`
  - Team: `SCHINK PTY. LTD. (6DP8F4CY29)`
  - App version: `1.0`
- Before every upload, increment `Shink.Mobile/Shink.Mobile.csproj` `ApplicationVersion`; never reuse an App Store Connect build number.
- Build and sign Release for `ios-arm64` with the Schink Apple Distribution certificate/profile. Verify the IPA/archive bundle ID, version, build number, team, architecture, and code signature before uploading.
- Open the signed archive in Xcode Organizer. If the normal MAUI archive/export is blocked by temporary macOS `com.apple.FinderInfo` or resizetizer artifacts, use a temporary archive workspace and preserve the signed app; do not change unrelated source files or signing assets.
- In Organizer choose: `Distribute App` → `App Store Connect` → `Distribute`. Wait for Xcode to confirm the app upload completed before closing Organizer.
- In App Store Connect, wait for `Build Uploads` to show `Complete` and the build to appear under version `1.0`. Complete export compliance before adding the build to testers. For this app, select `None of the algorithms mentioned above` only when current source/build verification still confirms there is no custom or non-Apple encryption.
- Attach the new build to the existing `Schink Team` internal group and `Public` external group. Verify tester counts and build status first; adding a build to a group is separate from adding testers.
- Add internal or external testers only when the user provides the tester addresses. Do not invent addresses or send invitations without user approval.
- For external testing, fill in `What to Test`, keep `Automatically notify testers` aligned with the user’s instruction, and submit for beta review if App Store Connect requires it.
- Report these states separately: local archive/IPA, Xcode upload complete, App Store Connect processing, export compliance complete, TestFlight `Testing`/availability, external beta review, and production App Review/release. TestFlight publishing does not release the app to Production.
- After the replacement build is confirmed active in the required groups, open the old build’s detail page, choose `Expire Build`, confirm, and verify that the old build is `Expired` while the new build remains active.
- Do not use the App Store Connect API-key or `xcrun altool` route as the default for this team. The API route currently requires Account Holder access, and Apple password prompts must never be handled in chat or an invisible terminal.

## 15) Verification
- Run the narrowest relevant verification for the change, such as focused source tests, `dotnet test`, or a targeted build.
- If auth-gated pages prevent browser verification, report the limitation and use source assertions, compiled scoped CSS, or focused tests as evidence.
- Before finishing, report what was changed and what verification was run.
