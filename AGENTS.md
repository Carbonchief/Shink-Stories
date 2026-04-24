# AGENT.md

Project-specific rules for this repository.

## 1) Branding and Navigation
- Header must use the text logo only: `/branding/schink-logo-text.png`.
- Do not re-add the extra header tagline text.
- Main nav currently uses:
  - `Meer oor Ons` -> `/meer-oor-ons`
  - `Gratis stories` -> `/gratis`

## 2) Footer (All Pages)
- Footer background color must be `#222222`.
- Show text logo in footer.
- Show social links as icon buttons (not text), below the logo:
  - Facebook: `https://www.facebook.com/SchinkPublishing`
  - Instagram: `https://www.instagram.com/schinkpublishing/`
- Footer copyright text is fixed to: `© 2023 Schink`.

## 3) Home Page Rules
- Main hero image is `/branding/Schink_Stories_01.png`.
- White logo above hero image: `/branding/schink-stories-logo-white.png`.
- Hero logo and image should visually blend.
- Current overlap target is 50px for desktop and mobile.

## 4) Meer Oor Ons Page Rules
- Route: `/meer-oor-ons`.
- Hero image: `/branding/Schink_Die_Ware_Wenner_Schink_Stories_600x600.png`.
- Keep all established copy blocks from the live source page adaptation.
- Keep founder image above founder text:
  - `/branding/Matin-Profile-Photo.webp`.
- Keep section before "Ons is Martin & Simone":
  - Header: `Wie ons is`
  - Image: `/branding/Schwella.webp`.
- Promise section requirements:
  - Header: `Ons Belofte aan Ouer & Kind`
  - Promise text remains in that block
  - Centered panda image below promise text: `/branding/Panda.webp`
  - Panda image size should match Schwella image size and use rounded corners.
- "Wat ouers se" should remain a review-card block style.
- Review display names in order:
  1. `Renske` (with role `Arbeidsterapeut` below name)
  2. `Sivonne`
  3. `Elmarette`
- Review text should not include double quotation marks.

## 5) Audio Protection Rules (Hardening)
- Do not expose direct static audio URLs for playback.
- Playback must use signed, expiring URLs from:
  - `/media/audio/{slug}?token=...`
- Audio files are served from server-side `Stories` folder.
- Public direct audio file access under `/stories/*` for common audio extensions is blocked.
- Keep right-click suppression on player area/audio element.
- Keep `controlslist` restrictions on audio where supported.
- Keep no-cache and same-origin oriented response headers for audio streams.
- Keep rate limiting enabled for audio stream endpoint.

## 6) Content and UX
- Afrikaans-first copy and tone.
- Mobile responsiveness is required for all page changes.
- Preserve existing visual style unless explicitly asked to redesign.
- When creating new emails through Resend, always use published Resend Email Templates instead of inline HTML/text payloads.

## 7) Admin Page Localization
- The `/admin` page must support both Afrikaans and English.
- Any new admin UI copy must be added in both languages (not one language only).
- Keep the admin language toggle and persisted preference behavior working when making admin changes.

## 8) Supabase MCP Setup
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
