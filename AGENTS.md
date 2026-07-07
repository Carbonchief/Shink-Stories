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

## 12) Verification
- Run the narrowest relevant verification for the change, such as focused source tests, `dotnet test`, or a targeted build.
- If auth-gated pages prevent browser verification, report the limitation and use source assertions, compiled scoped CSS, or focused tests as evidence.
- Before finishing, report what was changed and what verification was run.
