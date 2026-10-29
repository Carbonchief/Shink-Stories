# Profile avatar selection — 28 October 2026

final result: passed

## Follow-up: onboarding and dropdown actions

The user's updated requirements distinguish two flows. First-time setup shows **Begin luister**, allows submission without a selection, assigns a random catalogue avatar on the server when none was chosen, saves it, and always redirects to `/luister`. Entering through the account dropdown adds `mode=edit` and the originating local page address. That flow shows the requested **Save** label and returns to the original address, including its query string. Existing avatar and cookie protection remain in place.

Verified in the actual local browser: an empty onboarding selection saved a random Panda avatar and opened the real Luister page; opening the dropdown from `/meer-oor-ons?bron=avatar-toets` showed Save, changing to Roomys and saving returned to that exact address, and reopening preselected Roomys. No browser warnings or errors occurred in the checked flow. The Save state is captured at 390 × 844 CSS pixels in `output/avatar-selection/save-flow.png`. The temporary capture viewport was cleared afterward. Current focused regression batch: **52 passed**, including selected and random onboarding, edit return paths, retry mode preservation, CSRF, and existing sign-up rendering. Log: `output/avatar-selection/flow-tests.log`. The website was rebuilt by this test run; `git diff --check` passed.

These updated action and navigation requirements supersede the initial required-choice and post-sign-up continuation behavior described in the historical comparison notes below. They do not change the supplied layout or artwork.

## Annotation follow-up: circular header avatar

The user's browser annotation requested a circular portrait without the square behind it. Updated the header avatar wrapper to `border-radius: 50%`, made the image inherit that radius, removed the mobile rounded-square override, and removed the image wrapper's background and shadow. Rebuilt the website successfully and refreshed the annotated 284px browser viewport. Computed styles confirm a 24px circular image and wrapper, transparent background, and no shadow. Screenshot: `output/avatar-selection/header-circle.png`. `git diff --check` passed.

## Reference and browser evidence

- Source visual truth: `output/avatar-selection/source-mockup.png`, copied from the user's attached mockup. Source dimensions: 1600 × 900 pixels.
- Implementation: the actual compiled `/profielprentjie` Razor page, MainLayout, and avatar POST endpoint, hosted by a temporary local preview harness at `http://localhost:5278/preview`.
- The harness is outside this repository at `/tmp/shink-avatar-preview/`. It supplies an isolated sample account and in-memory profile storage. No preview authentication route or fake service was added to the product.
- Desktop CSS viewport: 1600 × 1000; browser density: 2. Captured at 3200 × 2000, normalized to 1600 × 1000. The native browser capture uses Display P3 pixels; captures were tagged accordingly and converted to sRGB before comparison.
- Full desktop, with the saved Panda portrait in the header and picker: `output/avatar-selection/final-desktop.png`.
- Unselected content region: `output/avatar-selection/desktop-picker.png`. The existing header begins above the mockup content. Cropping at y=66 removes that existing header and compares a 1600 × 900 content region with the source. The picker section's natural height is approximately 903 pixels; the final three background pixels are outside this crop.
- Combined full-view comparison, source on the left and implementation on the right: `output/avatar-selection/visual-comparison.png`. The composite has a 2× backing resolution of 6400 × 1800.
- Focused typography and image comparison: `output/avatar-selection/heading-comparison.png`.
- Focused CTA comparison: `output/avatar-selection/button-comparison.png`.
- Open profile dropdown: `output/avatar-selection/profile-menu.png`.
- Mobile, 390 × 844 CSS pixels, normalized from a 2× capture: `output/avatar-selection/mobile.png`.
- Additional narrow-screen check: 320 × 640 CSS pixels. Document width remained 320 pixels, avatar targets measured 58.5 pixels, and the CTA remained fully within the viewport.

## Comparison history

1. Initial browser capture found a P1 background extraction artifact: a centered one-pixel source strip included heading pixels, repeating as horizontal lines. Extracted the unobstructed strip at x=1, refreshed without the old cached asset, and captured again. No lines remain in the combined comparison or final desktop/mobile captures.
2. The initial native capture appeared desaturated. DOM color values matched the implementation; normalizing the native Display P3 capture to sRGB resolved this comparison-only discrepancy. No product palette changes were made to compensate for capture color management.
3. Rechecked the final build after the account and return-path tests. Required selection blocked an empty submit, saving Panda reached the local success page, reopening restored Panda, and the final capture shows Panda in both the header and selected state.

## Required fidelity surfaces

- **Fonts and typography:** Existing local Poppins assets are used. Desktop heading is 32px/500, CTA is 24px/700. Mobile heading is 24px and wraps to two balanced lines. The copy matches the supplied Afrikaans mockup. Native text remains selectable and accessible.
- **Spacing and layout rhythm:** Centered 636px content area, four columns, approximately 142px portraits, 22px column gaps, and 10px desktop row gaps. The CTA is 396 × 60px with 16px corners. On mobile, the grid and CTA shrink within 22px side padding. Selection rings and focus outlines do not overlap neighboring portraits.
- **Colors and tokens:** The supplied background was reused as an unobstructed raster strip. The green CTA and dark teal heading match the reference after capture color normalization. The existing site header and footer remain present as required by AGENTS.md; they were excluded only from the standalone mockup comparison.
- **Image quality and fidelity:** All 16 portraits were individually extracted from the supplied artwork in the original order, with carefully aligned 142 × 142 bounds and circular presentation. No replacement illustrations or placeholders were generated. Portraits remain at or below their source CSS size.
- **Copy and content:** “Kies ’n prentjie vir jou profiel” and the onboarding “Begin luister” are preserved. The dropdown adds “Verander profielprentjie” and opens the **Save** action specifically requested by the user. Authentication and save failures use Afrikaans messages. Onboarding goes to Luister; editing returns to the originating local page.

## Findings

No actionable P0, P1, or P2 findings remain.

P3: Native font antialiasing differs slightly from the raster mockup. The final row and CTA are approximately 3–4 pixels lower than the source. This does not change the composition or interaction and is accepted as minor rendering/layout polish.

## Interaction and account verification

- All 16 portrait files loaded successfully in the browser.
- Empty onboarding submission assigned and saved a random avatar; editing retains selection validation.
- Clicking Panda displayed its selection ring and check mark.
- Submitting saved the portrait through the actual antiforgery-protected form endpoint into the isolated preview store. Begin luister opened Luister and Save returned to the original page with its query string intact.
- Reopening restored the saved choice and displayed the same image in the header.
- The profile dropdown displayed the new option, and clicking it opened `/profielprentjie`.
- Keyboard ArrowRight moved the selected radio from Panda to Lama; Panda could be reselected.
- Desktop, 390px mobile, and 320px narrow mobile had no horizontal overflow.
- Browser console checks after the final flow returned no errors or warnings.
- Automated verification: 84 tests passed across avatar behavior, sign-up rendering/routing, authentication, MainLayout, and WordPress migration coverage. The test build compiled the current website source. `git diff --check` passed. Detailed output: `output/avatar-selection/tests.log`.
- Tests cover signed-in account ownership, CSRF rejection, invalid avatar IDs, save failures, unchanged session expiry/roles, local return-path validation, random and selected onboarding, edit-mode return paths, and preservation during WordPress sign-in sync.

## Scope and remaining integration limits

The existing Supabase profile columns were verified with a read-only query through the project-specific Shink connector. The write implementation is covered using a stub HTTP server/handler contract; no live profile was changed. Real provider sign-up and production persistence were not exercised in the browser. No migration is needed. No commit, push, deployment, release, or communication was performed.

## Implementation checklist

- [x] Match the supplied portraits, order, background, typography, and primary CTA.
- [x] Implement first-sign-up routing to Luister, automatic random selection, and return to the original page after editing.
- [x] Add the profile dropdown entry and preselect the saved portrait.
- [x] Save only the authenticated subscriber's avatar and refresh the existing cookie.
- [x] Preserve chosen avatars during WordPress profile synchronization.
- [x] Verify browser interactions, responsive behavior, and focused regression tests.
- [x] Leave the local preview available for review.
