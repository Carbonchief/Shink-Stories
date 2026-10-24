# Free signup design QA — 12 October 2026

**Final result: passed**

Source visual: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-d3ac0e0a-e0bf-4eea-8af5-d2f304006637.png` (1600 × 900).

Implementation: `http://127.0.0.1:5265/teken-op?returnUrl=%2Fluister`.

Evidence directory: `/Users/luanvanderwalt/.codex/visualizations/2026/10/12/01a131b7-8bce-7e12-b43b-a937786a5e65/free-signup/`.

- `desktop-viewport.png`: 1600 × 900 CSS/pixel viewport, device scale factor 1, signed out, empty form, light theme.
- `desktop.png`: 1600 × 1133 full page, including the existing footer.
- `mobile.png`: 390 × 844 CSS viewport, full-page capture, device scale factor 1.
- `mobile-small.png`: 320 × 740 CSS viewport, full-page capture, device scale factor 1.
- `comparison.png`: source and implementation side by side; full-page implementation cropped to the matching 1600 × 900 viewport. No density scaling before comparison.
- `comparison-focused.png`: source and implementation card regions at original density, inspected together for asset, type, field, and button fidelity.

## Findings

No remaining actionable P0/P1/P2 findings.

- **Fonts and typography:** existing local Poppins matches the reference. The heading retains its two-line wrapping and 23px desktop size. Input text remains at least 16px on mobile. Placeholder contrast is stronger than the reference.
- **Spacing and layout:** desktop card width is 466px, with the reference's centered narrow input column and wider green button. The card grows vertically for the requested provider buttons and the existing email preference and sign-in link. At 320px the card is 288px wide and the inputs are 242px wide; document width equals viewport width. All actions remain reachable by normal scrolling.
- **Colors and tokens:** teal-to-yellow background, cream card, dark teal text, and green primary action follow the reference. Provider buttons use white/Google and black/Apple styling.
- **Image quality:** original Schink turquoise logo and original Oortjies holding a book are reused. The mouse's paper background blends into the card without a visible rectangular edge. Icons use the installed Font Awesome brand font.
- **Copy and content:** the heading, four fields, and “Volgende” follow the source. Added provider actions appear below an “of” divider in Afrikaans. The site's existing header, required footer, email preference, and sign-in link are intentionally retained.

## Comparison history

1. Initial desktop capture showed a visible paper rectangle around Oortjies and excess vertical space pushing the providers below the fold. Changed the blend mode and tightened image/header/card spacing. The final full-view and focused comparisons show the rectangle removed and the complete desktop card visible within the 900px viewport.
2. Checked 390px and 320px layouts after refinement. No clipping or horizontal overflow; fields, button, providers, and sign-in link remain readable and usable.

## Functional verification

- Home “Luister 3 Stories Gratis” opens the compact free form.
- Home paid signup retains the selected monthly plan and full membership form.
- Switching from paid to free opens the compact form and returns password sign-in, Google, and Apple to `/luister`, without a checkout destination.
- Empty required fields and mismatched passwords show Afrikaans validation messages.
- A valid local submission reaches the existing signup endpoint and displays its configured-service error without losing the form. External credentials and background billing jobs were disabled in the preview.
- Google gets the existing browser-specific PKCE/implicit configuration; both provider links preserve a safe local listening/story destination.
- Fresh final-preview console check: no errors. Earlier connection errors were caused by deliberately restarting the local preview during rebuilds.
- 63 focused tests pass, covering rendered free/paid/school/voucher forms, safe authentication return paths, browser autofill input synchronization, free story routing, referrals, email preference behavior, and existing signup checks.

Live account creation and external Google/Apple authorization were not exercised. No production data, content, deployment, or Git remote was changed.

## Implementation checklist

- [x] Match the supplied card design using existing brand assets.
- [x] Keep paid, school, and voucher signup flows available.
- [x] Add Google and Apple below “of”.
- [x] Verify desktop, mobile, validation, navigation, and console state.
- [x] Leave the local preview open.

final result: passed

---

# Native paywall design QA — 21 October 2026

**Findings**

No remaining actionable P0/P1/P2 issues in the implemented native paywall.

Source visual: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-e6ac576e-cf8e-4d01-9c2d-4339929322f6.png` (1312 × 1199).

Implementation: native MAUI `PlansPage`, mirrored at `http://localhost:3200/`. Evidence: `artifacts/paywall-design/implementation/`.

**Comparison conditions**

- Annual selected, parent area approved, Afrikaans, light theme. The reference's R999 annual amount is illustrative; the preview uses the existing catalog's R990. App source displays localized store metadata.
- Reference iOS screen crop: x111/y26, 514 × 1095 pixels, normalized to 402 × 856. Native iPhone: 1206 × 2622 pixels, 402 × 874 points at 3x, normalized to 402 × 874. The source screen aspect ratio and native simulator chrome differ slightly; device bezels are excluded from judgment.
- `comparison.png`: source and native screen together at equal content width. `comparison-hero.png`: focused original-character, logo, and background comparison. These combined images were opened and inspected.
- `phone-annual.png`, `phone-monthly.png`, `phone-footer.png`, `phone-initial.png`, `phone-restore.png`, and `phone-continue-free.png` cover the native phone states.
- `tablet-portrait.png`: 1668 × 2420 pixels, 834 × 1210 points at 2x. `tablet-landscape.png` and `tablet-unavailable-price.png`: 2420 × 1668 pixels, 1210 × 834 points at 2x.

**Required fidelity surfaces**

- Fonts and typography: the existing rounded Schink font supplies the two-line display heading. Native UI text remains readable, with 14-point renewal disclosure. Native font metrics differ from the illustrated mock; no text truncation in the verified standard-size states.
- Spacing and layout: original forest/book composition, centered logo, benefits, two plan cards, and single primary action follow the reference hierarchy. Native safe areas and minimum 44-point footer actions require a short scroll on the phone; all footer actions are reachable, as captured in `phone-footer.png`. Tablets use a centered 580-point maximum-width column and scroll in shorter landscape windows.
- Colors and tokens: cream `#FFF7E8`, dark teal `#003F46`, mint selection, and gold value badge follow the concept. Selected radio/card states remain clear. System status/navigation chrome stays native.
- Image quality: Skillie, Oortjies, and Prinses Panda are byte-identical copies of existing original PNGs, rendered as separate native layers. No generated character substitute, stretching, or visible rectangular character background. Existing original white logo reused. The forest/book background is retained from the approved background plate.
- Copy and content: Afrikaans benefits and plan labels retained. Purchase button and renewal terms show the same selected store price and period. Platform-specific cancellation wording, free continuation, restore, terms, and privacy are present. Value badge is suppressed when comparable store prices are unavailable.

**Comparison history**

1. Initial phone capture (`phone-annual-initial.png`) exposed an incorrect foreground mask covering part of Oortjies. Rebuilt the book-only mask before image scaling and recaptured. `phone-annual.png` and `comparison-hero.png` show the complete original characters, with the book foreground confined below them.
2. Reduced header/body spacing after initial phone review. Final `comparison.png` shows the complete primary conversion area, with readable renewal text and free continuation. `phone-footer.png` confirms restore/legal actions after a short scroll.

**Functional verification**

- Parent challenge opens the protected area only after the matching digits are entered.
- Monthly selection changes the radio, card, button, and renewal text together.
- Empty restore result shows the existing native result dialog; continuing free returns to Stories.
- Missing annual price disables that option and defaults to the available monthly product; no fabricated annual price or value badge appears.
- 22 focused tests passed; iOS simulator and Android arm64 builds passed with zero errors. `git diff --check` passed. Original source artwork hashes and packaged copy equality passed.
- Preview billing/session stubs exist only in an isolated temporary copy. No real store purchase, Android runtime session, physical install, release, or live billing mutation was performed. Real sandbox transactions remain unverified.

**Follow-up polish**

- P3: native font metrics, solid teal header strip, and system status-bar background differ slightly from the painted reference. Existing brand typography and safe-area behavior are retained.
- Larger accessibility text sizes were not exercised in this pass; the page is scrollable and labels wrap.

**Implementation checklist**

- [x] Use original character artwork and approved background.
- [x] Preserve native store purchase verification and recovery.
- [x] Bind price, selection, purchase button, and disclosure consistently.
- [x] Verify phone, tablet portrait/landscape, unavailable price, and footer states.
- [x] Keep the local phone preview available.

final result: passed
