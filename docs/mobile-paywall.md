# Native mobile paywall

The native `PlansPage` follows the approved 21 October 2026 paywall concept. It uses the existing monthly and annual store products and keeps the existing server verification, transaction finalization, purchase recovery, and story return paths.

## Artwork

The forest/book background comes from the approved background-only design plate. Characters are separate native image layers, with byte-identical packaged copies of the original assets:

| Packaged image | Original source |
| --- | --- |
| `paywall_skillie.png` | `Shink/wwwroot/branding/characters/skillie.png` |
| `paywall_oortjies.png` | `Shink.Mobile/Resources/Images/oortjies_02.png` |
| `paywall_prinses_panda.png` | `Shink/wwwroot/branding/characters/prinses-panda.png` |

No character artwork was generated or edited for this implementation. `PaywallHeroView` scales and positions the original transparent PNGs. `paywall_book.png` is a foreground cutout of the background plate. The existing white logo, rounded brand font, and Font Awesome icons are reused.

## Behavior

- A fresh four-digit reading challenge protects the parent area before store products and external legal links become available. Approval lasts only for that page instance; it is unrelated to subscription entitlement.
- Available annual access is selected initially. Monthly and annual choices update one purchase button and one renewal disclosure together.
- Prices are the store's localized strings. Missing prices disable the affected choice, expose a reload action, and never fall back to a hard-coded charge.
- The annual value badge appears only when both localized prices exist and same-currency numeric store prices demonstrate a saving over twelve monthly payments.
- Renewal copy includes the full recurring amount and billing period. iOS refers to App Store subscription cancellation; Android refers to Google Play.
- Signed-out users must sign in before purchasing or restoring. Paid users bypass the paywall. Closing or continuing free returns to story discovery; a successful entitlement resumes the existing story return path.
- Restore, terms, privacy, and free continuation remain available through the scrollable footer. Interactive links have a minimum 44-point height. Content is centered at a maximum width of 580 points on tablets.

## Verification on 21 October 2026

- 22 focused presentation, original-artwork, mobile-plan source, and purchase-recovery tests passed.
- iOS simulator and Android arm64 Debug builds passed with zero errors. Existing SDK target/RID warnings remain.
- Native iPhone simulator: parent challenge, annual selection, monthly selection, consistent button/disclosure prices, empty restore response, free continuation, and footer reachability verified.
- Native iPad simulator: portrait, landscape, and unavailable annual price verified.
- Legal destinations responded successfully over HTTPS.
- Visual comparison and screenshots: `artifacts/paywall-design/implementation/`; review: root `design-qa.md`.

Runtime preview uses an isolated copy outside the repository with synthetic session and billing services. Preview values are R99/month and R990/year; production reads store metadata. No preview fixtures are included in app source. Real StoreKit/Google Play transactions, sandbox purchase/restore, Android runtime, and physical-device installation remain release verification tasks. No release, upload, deployment, or live subscription change was performed.
