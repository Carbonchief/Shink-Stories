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
