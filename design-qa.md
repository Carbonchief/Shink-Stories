# Soek Hero and Live Results QA — 2026-08-31

**Comparison Target**

- Supplied title artwork: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-2ffb99a5-88b0-47ee-a670-81aacfd298d3.png`.
- Initial implementation: `/tmp/schink-search-initial.png`.
- Typed/results implementation: `/tmp/schink-search-typed-results.png`.
- Viewport: Schink Stories iPhone 11 Pro Max simulator, portrait, 414 x 896 logical points at 3x native density.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested scope.
- Asset fidelity: `soek_stories_title.png` is a byte-for-byte copy of the supplied transparent white `Soek Stories` artwork; both files have SHA-256 `d20535854de11e813e347ff69027ee54626a77cd327ee7778fffda9200679d9b`.
- Initial layout: the title artwork remains sharp and centered. The Afrikaans instruction has a clear gap above the full-width search field, with no overlap.
- Typed state: entering `Beer` keeps the live Entry focused, removes the mascot/title/instruction group, places the search field directly below the shared top bar, and renders matching story cards immediately below it.
- Clearing the query restores the initial hero and removes the result cards after the existing debounce.

**Verification**

- iOS simulator source compiled through `Shink.Mobile.dll`; the known `com.apple.FinderInfo` signing defect required a metadata-clean temporary app copy.
- The clean copy passed strict signature verification, installed, launched, and was exercised through empty, focused, typed, results, and cleared states.
- `git diff --check` passed for the scoped source and test changes.
- The focused test project is currently blocked before test execution by the unrelated existing `StoryCatalog.cs` compile error for missing `PlaceholderImagePath`.

final result: passed

# Canonical Placeholder Artwork QA — 2026-08-31

**Comparison Target**

- Source visual truth: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-bca87127-f5ac-4667-824f-c864c08a1e1d.png` (2480 x 2480 RGBA).
- Browser-rendered desktop implementation: `/tmp/schink-placeholder-implementation-desktop.png` (1440 x 280 focused header/search capture) and `/tmp/schink-luister-desktop.png` (1440 x 1000 full viewport).
- Browser-rendered mobile implementation: `/tmp/schink-placeholder-implementation-mobile.png` (390 x 844 viewport).
- Side-by-side source and implementation evidence: `/tmp/schink-placeholder-comparison.png` (922 x 844; supplied transparent artwork composited on the implementation's `#146D69` backing at left, live mobile implementation at right).
- CSS viewports: desktop 1440 x 1000 and mobile 390 x 844. Browser screenshots matched those CSS dimensions at 1:1; no density normalization was required for the implementation. The source asset was scaled proportionally into a 512 x 512 comparison panel without cropping.
- State: `/luister`, signed out, global site search expanded with `tuis`, showing two real results whose missing/generic thumbnails use the canonical placeholder.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested placeholder scope.
- Fonts and typography: no page typography or copy was changed; result labels remain readable beside the 44 x 44 placeholder.
- Spacing and layout rhythm: the supplied square emblem fits the existing 44 x 44 result slots without changing row height, padding, radii, or surrounding layout. Story-card layouts retain their established geometry.
- Colors and visual tokens: transparent artwork is backed by the existing Schink teal `#146D69`, producing sufficient contrast for the white emblem on desktop and mobile.
- Image quality and asset fidelity: the web and mobile bundles are byte-for-byte copies of the supplied 2480 x 2480 RGBA PNG. The live browser reported `naturalWidth = 2480`, `object-fit: contain`, and the expected teal background. The emblem remains centered with no crop, stretch, transparency halo, or substitute artwork.
- Copy and content: no customer-facing copy changed.

**Full-View and Focused Comparison Evidence**

- The desktop full viewport confirms the existing Luister hierarchy and responsive layout were preserved.
- The focused mobile capture clearly shows the complete emblem, its square teal backing, adjacent result copy, search control, and surrounding hero. A separate smaller crop was unnecessary because the 44 x 44 rendered assets are clearly readable in the mobile capture.
- The combined comparison places the supplied artwork and live browser-rendered result in one visual input; linework, proportions, transparency, and centering match.

**Comparison History**

- First pass: no P0, P1, or P2 mismatch was found, so no visual correction loop was required.

**Interaction and Verification Evidence**

- Opened `/luister` in the Codex in-app browser at desktop and mobile breakpoints.
- Expanded global search, entered `tuis`, and confirmed both returned placeholder images loaded at their natural 2480-pixel width with `rgb(20, 109, 105)` backing and `object-fit: contain`.
- Main Luister navigation, search controls, live results, hero, and visible story content remained usable.
- Browser console check returned no errors or warnings.

**Open Questions**

- None for the requested scope.

**Implementation Checklist**

- [x] Bundle the exact supplied placeholder for web and mobile.
- [x] Use it for missing and failed story/content images without replacing real artwork.
- [x] Preserve transparency and provide a consistent Schink teal backing.
- [x] Verify desktop and mobile browser rendering plus mobile compilation.

**Follow-up Polish**

- None required for this pass.

final result: passed

# Karakter Raai Visual Hierarchy QA — 2026-08-31

**Comparison Target**

- Source visual truth: `/tmp/codex-remote-attachments/01a058d4-c76f-70b2-b036-8fe4d8511ae1/62746F2A-3D52-46DF-8CEB-12CCA69BDDDE/1-Pasted-Image-1.jpg`.
- Rendered implementation: `/tmp/schink-karakter-raai-layer-fix-next-round.png`.
- Full-view side-by-side comparison: `/tmp/schink-karakter-raai-layer-fix-comparison.png`.
- Viewport: Schink Stories iPhone 11 Pro Max simulator, portrait, 414 x 896 logical points at 3x native density.
- Source pixels: 590 x 1280. Implementation pixels: 1242 x 2688. Both were normalized to a 590 x 1280 comparison frame; the combined AppKit canvas has a 2x backing scale and is 2360 x 2560 pixels. Browser CSS size and `deviceScaleFactor` do not apply to this native MAUI capture.
- State: unanswered second Beginner round after a successful reveal and automatic advance, with four choices. The randomized character artwork differs, but the interaction state and layout are the same.
- Focused evidence: a separate crop was unnecessary because the heading, score, mystery stage, message, and complete answer row are all clearly readable in the full-view comparison.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested scope.
- Fonts and typography: Poppins Bold, color, weight, wrapping, and alignment are preserved. The heading is reduced from 50 to 36 points and the score labels from 42 to 30 points, producing a clear hierarchy without overpowering the game artwork.
- Spacing and layout rhythm: the heading row is reduced from 174 to 120 points, the score row from 68 to 50 points, and the mystery-message row from 76 to 60 points. The recovered 88 vertical points flow into the flexible artwork stage while the top controls and answer row retain their existing positions and touch targets.
- Colors and visual tokens: the mint-to-yellow background and blue-grey foreground token remain unchanged.
- Image quality and asset fidelity: the existing signed mystery artwork remains sharp and uses `AspectFit`. Removing the 18-point horizontal inset lets it use the full stage width without cropping or stretching; no asset was recreated or substituted.
- Transition loading: the game prepares the exact upcoming round and caches its mystery artwork, reveal artwork, previews, and four choice images during the current guess. Automatic advancement waits for that warm-up task, so the generic lineup fallback is not knowingly presented between rounds.
- Copy and content: all Afrikaans copy and game data remain unchanged. The current source correctly renders `Rondte 1`; the older supplied capture truncates the label to `Rondt`.

**Comparison History**

- Earlier P2: the supplied screen devoted 242 fixed vertical points to oversized heading and score copy, leaving the mystery character visually secondary.
- Fix: reduced only those text sizes and fixed rows, shortened the message row, and removed the artwork's side inset.
- Follow-up P1: progressive cached-image loading could reset the hidden real image's opacity to 1, allowing coloured artwork to leak around the mystery silhouette.
- Layer fix: both progressive images now render inside independent parent layers. The reveal layer remains `IsVisible = false` while its image preloads, and only the parent layers participate in the reveal animation.
- Follow-up P2: the next round was selected only when advancement began, so its mystery artwork could briefly show the generic lineup fallback while downloading.
- Preload fix: `CharacterGuessGame.PrepareNextRound()` now fixes the next target and choices without advancing state. The page warms up to ten distinct full/preview URLs during the current round and awaits completion after the normal reveal delay.
- Post-fix evidence: the side-by-side comparison shows a clean, uninterrupted mystery silhouette with no real-character colour at its edges. The mystery character remains the dominant focal point while the heading, round, score, message, and four answer choices remain fully visible.

**Interaction and Verification Evidence**

- Navigated from the authenticated Stories screen through `Menu` → `Karakter Raai`, selected `Beginner`, confirmed a clean hidden state, answered correctly to exercise the reveal, and waited for automatic advancement to confirm the next real image remained fully hidden after progressive loading completed.
- Cold-cache transition evidence: `/tmp/schink-preload-before-transition.png`, `/tmp/schink-preload-transition.png`, and `/tmp/schink-preload-next-round.png` show the prepared second mystery and all four choices fully rendered with no lineup placeholder.
- Focused `CharacterGuessGameTests`: 9 passed, 0 failed.
- iOS simulator Debug build completed with 0 errors, installed successfully, and launched successfully.
- `git diff --check` passed.

**Open Questions**

- None for the requested scope.

**Implementation Checklist**

- [x] Reduce the heading and round/score copy.
- [x] Give the mystery artwork the recovered vertical space and full stage width.
- [x] Preserve the answer row and game behavior.
- [x] Verify the actual native render at the supplied device profile.

**Follow-up Polish**

- None required for this pass.

final result: passed

# Karakter-pare Card Density QA — 2026-08-31

**Comparison Target**

- Source visual truth: `artifacts/design-qa/karakter-pare-card-density/reference.jpg`, together with the requested change to make its corners less rounded and its gaps very small.
- Rendered implementation: `artifacts/design-qa/karakter-pare-card-density/implementation-ios.jpg`.
- Source pixels: 946 x 1280, board-only crop. Implementation pixels: 369 x 800, complete iOS simulator screen at the simulator screenshot tool's 1x optimized output. Browser CSS size and `deviceScaleFactor` do not apply to this native MAUI capture.
- Viewport: Schink Stories iPhone 11 Pro Max simulator, portrait, 369 x 800 captured points.
- State: Beginner difficulty, 3 x 4 board, all cards face down before the first turn.
- Full-view evidence: the reference and simulator implementation were opened together in one comparison input. Their crops differ, so full-screen chrome and vertical placement were excluded from fidelity judgments.
- Focused evidence: the complete 3 x 4 card regions are clearly readable in both images; no separate crop was needed for the requested radius and spacing comparison.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested scope.
- Fonts and typography: card-back logo typography is the unchanged source artwork. The existing score typography is outside the supplied board-only crop and was not modified.
- Spacing and layout rhythm: phone boards now use a consistent 4-point row and column gap instead of 10 points for Beginner and 12 points for denser difficulties. The implementation visibly keeps only a thin separation between tiles and uses the recovered width to enlarge each square card. Tablet boards retain their original 10-point three-column and 12-point four-column gaps. Existing outer board margins and centering remain unchanged.
- Radii: on phones, both the outer card mask and face-up card surface now use a 12-point radius instead of the former 22-point outer radius and 15-point front radius. The simulator capture shows visibly flatter corners that remain softly rounded rather than square. Tablets retain the original 22-point outer and 15-point face-up radii.
- Colors and visual tokens: the turquoise artwork, vertical mint-to-yellow background, and existing shadow treatment are unchanged.
- Image quality and asset fidelity: the original `karakter_pare_card_back.png` is still used with its existing crop and sharpness; no placeholder or recreated asset was introduced.
- Copy and content: no customer-facing copy changed.

**Comparison History**

- Earlier reference: cards had substantially rounder corners and wider gaps, leaving more background visible between tiles.
- Fix: introduced phone-only 12-point radius and 4-point spacing values while preserving the original tablet radii and per-grid spacing.
- Post-fix evidence: the iOS simulator capture shows twelve larger square cards with a narrow, even gutter and visibly reduced corner curvature. No further P0/P1/P2 issue was found.

**Interaction and Verification Evidence**

- Navigated from the Stories menu to `Karakter-pare`, selected `Beginner`, and confirmed the 3 x 4 board rendered and remained interactive.
- Focused `CharacterMatchGameTests`: 11 passed, 0 failed.
- iOS simulator Debug build completed with 0 errors, installed successfully, and launched successfully.
- The regression assertions require phone-only 12-point/4-point values and preserve tablet 22-point outer radii, 15-point face-up radii, and 10/12-point grid spacing behind `DeviceIdiom.Tablet`.
- The same build installed and launched successfully on the booted `AdoMobile iPad QA` simulator. That simulator is signed out, so the Karakter-pare board itself could not be reopened for a fresh tablet screenshot without account access; the tablet preservation claim is source/test/build evidence rather than board-level visual proof.
- Runtime logs were checked after navigation and rendering. No app layout, image-loading, fatal, or crash error appeared; only simulator/debugger environment warnings were present.
- `git diff --check` passed.

**Open Questions**

- None for the requested scope.

final result: passed

# Canonical Login Mouse Asset QA — 2026-08-31

**Comparison Target**

- Source visual truth: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-88245752-13a5-4e14-af42-403deb81d866.png`.
- Rendered implementation: `/tmp/shink-login-canonical-final.png`.
- Full-view comparison: `/tmp/shink-login-canonical-comparison.png`.
- Focused artwork comparison: `/tmp/shink-login-canonical-focused-comparison.png`.
- Source artwork: 797 x 827 RGBA PNG. Implementation capture: 1206 x 2622 physical pixels from the iPhone 17 Pro simulator at native 3x density (402 x 874 logical points). Browser CSS size and `deviceScaleFactor` do not apply to this native MAUI capture.
- State: signed-out account landing screen on iOS 26.5.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested image scope.
- Fonts and typography: existing Poppins hierarchy, weights, wrapping, and Afrikaans copy are unchanged by the asset replacement.
- Spacing and layout rhythm: the existing responsive image slot, logo overlap, controls, margins, and safe-area treatment are unchanged.
- Colors and visual tokens: the turquoise landing background, cream copy, dark controls, and image transparency remain intact.
- Image quality and asset fidelity: the login screen now renders the exact user-supplied mouse with closed eyes, raised finger, tail, and green book. The source asset and the sole packaged `schink_login_mouse.png` have the same SHA-256 (`28dff89a1a1000638781b4adb343d2fb89dc9dde69016add581e8d5a8f57c88a`). No stale copies of the previous artwork remain in the clean app bundle.
- Copy and content: no customer-facing text changed.

**Comparison History**

- Earlier implementation: the stable login filename contained a different 1448 x 1086 mouse illustration, and incremental simulator packaging retained two stale duplicate copies.
- Fix: replaced the stable asset with the exact supplied 797 x 827 PNG, kept the login and focused test pointed at that stable filename, removed the temporary `oortjies_02.png` login reference, and rebuilt from a clean iOS simulator bundle.
- Post-fix evidence: the full screen and focused comparison show the canonical artwork rendered without crop, halo, or pose drift; package inspection finds one matching login-mouse file.

**Interaction and Verification Evidence**

- Focused `MobileWelcomeScreenUsesResponsiveLandingMetrics` test passed.
- Current iOS simulator source compiled through `Shink.Mobile.dll`; the repository's known `com.apple.FinderInfo` signing defect required a metadata-clean copy.
- The clean copy passed strict signature verification, installed over the simulator app, launched successfully, and rendered the signed-out landing screen.
- The clean physical-device build contains one `schink_login_mouse.png` with the matching source hash and was installed in place on Luan iPhone 15 Pro as version 1.0 build 22; launch is blocked only because the phone is locked.
- Primary auth controls were not activated because the requested change is confined to the static login artwork.

**Open Questions**

- None for the requested asset replacement.

final result: passed

# Shared Glass Top and Bottom Bars QA — 2026-08-31

**Comparison Target**

- Source visual truth: the live Schink website navbar captured at `/tmp/shink-website-navbar-mobile-reference.png`, plus the user-supplied strong-blur tab-bar reference at `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-e4502e83-299d-4661-b110-d3a43e72747c.png`.
- Rendered implementation: `/tmp/shink-more-transparent-glass-current.png`.
- Full-view comparison: `/tmp/shink-website-navbar-more-transparent-full-comparison.png` (website left, native app right).
- Focused top-and-bottom comparison: `/tmp/shink-website-navbar-more-transparent-comparison.png` (website navbar, app top bar, app bottom bar from left to right).
- Focused blur comparison: `/tmp/shink-more-transparent-reference-comparison.png` (user blur reference left, app bottom bar right).
- Opacity comparison: `/tmp/shink-less-grey-more-transparent-comparison.png` (previous approved less-grey pass left, revised clearer pass right).
- Browser reference viewport and pixels: 390 x 844 CSS pixels at 1x density. The measured website navbar uses `blur(5px) saturate(1.25)`, a 4%-to-8% warm-white gradient, no border, and a soft shadow.
- The blur reference is 738 x 228 pixels. Its app comparison uses the bottom 450 pixels of the 1242-pixel-wide native capture, normalized to 738 x 267 and padded beside the source without changing aspect ratio.
- Native implementation viewport and pixels: Schink Stories iPhone 11 Pro Max simulator, 414 x 896 logical points at 3x density, captured at 1242 x 2688 pixels. The full app capture was normalized to 390 x 844 for the website comparison.
- State: the website reference shows the public mobile home hero, the user reference shows a blurred tab bar over colourful cards, and the app implementation shows the authenticated Stories feed. Content differs, so fidelity judgments are limited to the shared transparent material, backdrop softening, colour preservation, edge fade, controls, and absence of a hard separator.

**Findings**

- No actionable P0, P1, or P2 difference remains in the requested chrome scope.
- Fonts and typography: the existing Schink logo, icon glyphs, bold tab labels, sizes, and spacing remain unchanged. The material change does not alter wrapping, alignment, or touch targets.
- Spacing and layout rhythm: both app surfaces remain full-width and edge-to-edge. The top bar keeps its existing status-area coverage; the bottom bar keeps its safe-area placement. Their content edges now dissolve through mirrored gradients, so no horizontal stroke or abrupt material boundary remains.
- Colors and visual tokens: both bars now use a lighter 3%-to-6% warm-white surface (`#08FFFEFA` to `#10FFFEFA`) instead of the previous 4%-to-8% range. On iOS, both native bars share a clearer `#14005E68` teal wash at roughly 8% source opacity, replacing the darker 9% wash so the glass reads less neutral-grey. The top surface fades down into the page and the bottom surface fades upward using the unchanged matching 72%, 28%, and transparent edge stops.
- Native material: iOS uses a real `SystemUltraThinMaterialDark` backdrop effect behind both bars at 88% view opacity. Rabbit artwork and clothing detail remain softened, but more of their blue, pink, and teal colour now shows through. The previous bright, beveled `UIGlassEffect` lens remains removed.
- Android material: Android receives the same warm-white gradient and mirrored fades without live backdrop capture, preserving the app's existing scrolling-performance safeguard.
- Image quality and asset fidelity: no logo, icon, or story artwork was replaced or recreated. The combined visual evidence shows the app artwork remains visible and colour-preserving beneath the material.
- Copy and content: no customer-facing copy changed; the four Afrikaans tab labels remain `Stories`, `Soek`, `Afgelaai`, and `Karakters`.

**Comparison History**

- Earlier P1: the iOS 26 `UIGlassEffect` treatment produced a noticeably bright, white, beveled lens that looked unlike the website navbar and made the top and bottom surfaces feel heavier.
- Fix: replaced the lens with the same conventional ultra-thin backdrop material on both bars and mapped the website's measured 4%-to-8% warm-white gradient onto the shared MAUI surface.
- Earlier P2: a material edge could read as a horizontal line where the blur ended.
- Fix: retained the existing feather and made the top and bottom masks true mirrors, with multiple opacity stops carrying the material gradually into the page.
- Follow-up P1: the fade mask was created while the native MAUI host still had zero-sized bounds. The effect view later resized, but its Core Animation mask stayed at 0 x 0, so the tint/fade rendered while the real backdrop blur remained invisible.
- Fix: the effect now lives inside a dedicated transparent `GlassContainerView`. Its native `LayoutSubviews()` method resizes both the effect content and gradient mask to the final bounds whenever UIKit lays out either bar.
- Follow-up P3: the user approved the blur and fade but found the fully clear native tint slightly too neutral-grey.
- Fix: applied one shared 9% Schink-teal native tint to the top and bottom effect views, leaving the material style, warm-white surface, fade masks, geometry, typography, and controls unchanged.
- Latest P3: the less-grey pass still felt slightly too grey and opaque.
- Fix: reduced the shared warm-white surface to 3%-to-6%, changed the native wash to a clearer low-opacity teal, and set the same native effect opacity to 88% on both bars. Blur style, fade masks, geometry, typography, and controls remain unchanged.
- Post-fix evidence: `/tmp/shink-more-transparent-reference-comparison.png` shows the revised bar still removing fine detail while allowing more colour and character form through. `/tmp/shink-website-navbar-more-transparent-comparison.png` confirms the top and bottom use the same clearer material and retain mirrored, line-free fades. `/tmp/shink-less-grey-more-transparent-comparison.png` shows the requested opacity reduction directly against the previous pass.

**Verification**

- 25 focused mobile chrome, safe-area, and Search source tests passed after the shared opacity and tint correction.
- Android Debug build passed with 0 errors; only the existing target-SDK warnings remain. No Android emulator was connected for a fresh screenshot.
- iOS source compiled through `Shink.Mobile.dll`. Normal packaging encountered the known `com.apple.FinderInfo` metadata issue; a metadata-clean temporary app was ad-hoc signed, strictly verified, installed over the simulator app, and launched successfully.
- The final authenticated simulator capture and combined comparisons confirm active native blur, matching clearer top and bottom materials, mirrored fades, readable white controls, and no visible divider line.

**Open Questions**

- None for the requested scope.

final result: passed

# Single-Curve Bottom Tab Bar Fade QA — 2026-08-30

**Comparison Target**

- Source visual truth: `/tmp/codex-remote-attachments/01a05369-1142-7282-b9c7-4d91f8700958/D036B215-741C-4E1C-AB83-B7B70E5CF7DB/1-Pasted-Image-1.jpg`.
- Revised Android implementation screenshot: `/tmp/shink-bottom-single-fade-android.png`.
- Focused side-by-side evidence: `/tmp/shink-bottom-single-fade-comparison.png` (physical-iPhone defect left, revised Android render right).
- Source pixels: 1179 x 411. Implementation pixels: 1080 x 2400. The comparison canvas is 2378 x 411; the bottom 1080 x 600 implementation region is normalized to 1179 x 411 beside the source. Native-app CSS size and browser density do not apply.
- Android viewport: API 35 emulator, 1080 x 2400 physical pixels at 420 dpi (approximately 411 x 914 dp), portrait.
- State: both artifacts show the persistent four-item bar over an authenticated Stories feed. The feed content and platform differ, so the comparison is limited to the fade geometry, surface continuity, icons, labels, and visible content behind the material.
- Full-view comparison: unavailable because the supplied source is intentionally cropped to the bottom-bar region; no claims are made about unrelated page layout.
- Focused comparison: `/tmp/shink-bottom-single-fade-comparison.png` keeps the complete fade, icon row, labels, and underlying page content readable in one combined input.

**Findings**

- Android: no actionable P0, P1, or P2 difference remains in the requested fade scope. The revised render has one uninterrupted transparent-to-teal transition instead of two stacked horizontal bands.
- iOS physical-device visual confirmation is blocked. The corrected Ad Hoc build is installed on Luan iPhone 15 Pro, but the locked phone denied launch and no post-fix physical screenshot is available for a same-platform comparison.
- Fonts and typography: the existing white icons and bold tab labels remain unchanged, crisp, centered, and readable.
- Spacing and layout rhythm: the 92 dp interactive tab row and four equal-width targets remain unchanged. The 32 dp fade is non-interactive and is part of the same 124 dp backing layer, so there is no row seam.
- Colors and visual tokens: one full-height gradient uses transparent teal at the top and `#A812343B` from 32/124 of the host downward. The tab row itself is transparent, eliminating a second color boundary.
- Image quality and asset fidelity: existing vector icons and story artwork are unchanged; no assets were recreated or substituted.
- Copy and content: `Stories`, `Soek`, `Afgelaai`, and `Karakters` are unchanged.

**Comparison History**

- Earlier P1 evidence: the supplied physical-iPhone screenshot showed two visible horizontal transitions. The static feather and tab surface were separate MAUI layers, while the iOS native blur used an independent multi-stop mask.
- Fix: replaced the two MAUI backing surfaces with one full-height `materialBackdrop`, made the tab surface transparent, and aligned the iOS native blur to the same single 0-to-0.26 fade curve.
- Post-fix evidence: `/tmp/shink-bottom-single-fade-comparison.png` shows the revised Android bar fading continuously without the former double-line boundary.

**Interaction and Build Evidence**

- 19 focused mobile chrome and safe-area tests passed.
- Android Release build, signed APK installation, launch, and authenticated Stories-feed capture passed.
- iOS simulator source compiled; after the known `com.apple.FinderInfo` packaging issue, a metadata-clean app copy was ad-hoc signed, strict-verified, installed, and launched.
- The current source was also built with the exact Schink Ad Hoc profile, clean-copy signed with full profile entitlements, strict-verified, and installed in place on Luan iPhone 15 Pro as build 21. Automatic launch remains blocked because the phone is locked.

**Open Questions**

- Once the iPhone is unlocked, visually confirm that its native blur follows the same single fade curve and send a screenshot if any boundary remains.

final result: blocked

# Corrected Translucent Bottom Tab Bar QA — 2026-08-30

**Comparison Target**

- Source visual truth: `/tmp/codex-remote-attachments/01a05369-1142-7282-b9c7-4d91f8700958/BAE4B75C-D617-4026-B4B3-2D8C17D61832/1-Pasted-Image-1.jpg`
- Final Android implementation screenshot: `/tmp/shink-bottom-material-pass2.png`
- Final Android navigation-state screenshot: `/tmp/shink-bottom-material-karakters-pass2.png`
- Focused side-by-side comparison: `/tmp/shink-bottom-material-pass2-comparison.png` (reference left, implementation right).
- Android viewport: API 35 emulator, 1080 x 2400 physical pixels at 420 dpi (approximately 411 x 914 dp), portrait.
- Source pixels: 1179 x 435. Implementation pixels: 1080 x 2400. The focused comparison uses the bottom 600 implementation pixels, normalized to 1179 x 435 beside the supplied reference so the complete fade, tabs, labels, and Android home indicator remain visible.
- State: authenticated Stories feed with colourful artwork and copy continuing behind the shared bottom tab bar.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested bottom-bar scope.
- Separation treatment: a dedicated 32 dp feather transitions from fully transparent through two translucent teal stops into the tab surface. There is no stroke, divider, or hard horizontal line.
- Surface visibility and transparency: the lower 92 dp tab surface now has an explicit `#A812343B` translucent teal background. The card art and text remain visibly continuous underneath it, so the surface reads as transparent material rather than as no background.
- Android material: the bar deliberately uses the fixed translucent surface and feather rather than live backdrop capture. This keeps the requested glass-like hierarchy without reintroducing scroll-driven blur invalidation.
- Apple material: iOS 15 and later places `SystemUltraThinMaterialDark` native blur behind the entire 124 dp material host. Its mask becomes fully visible as the static feather reaches the tab surface, avoiding both a hard blur edge and an unbacked icon row.
- Fonts and typography: the existing crisp, bold, white tab labels and vector icons are unchanged. `Stories`, `Soek`, `Afgelaai`, and `Karakters` match the supplied reference.
- Spacing and layout rhythm: the icon and label row remains 92 dp with the same four equal touch targets and spacing. Only the non-content material host grows to 124 dp to add the 32 dp feather, so the earlier excess top padding does not return.
- Colors and visual tokens: the body uses `#A812343B`; the feather starts transparent and moves through `#3812343B` and `#7412343B` before meeting the body. Every stop uses the requested starting teal hue `#12343B`.
- Image quality and asset fidelity: existing card artwork and navigation icons are unchanged; no placeholder or recreated assets were introduced.
- Copy and content: no customer-facing copy changed.

**Interaction and Performance Evidence**

- Installed and cold-launched the corrected signed Android Release APK on the API 35 emulator.
- Verified the authenticated Stories feed, repeated scrolling, and navigation from `Stories` to `Karakters` after installation.
- A warm six-swipe sample rendered 111 frames with 4 janky frames (3.60%) and zero missed-vsync frames. Additional SwiftShader samples remained noisy, so this is comparative emulator evidence rather than physical-device performance proof.
- Compiled the final iOS source through `Shink.Mobile.dll`. The normal package signature encountered the repository's known `com.apple.FinderInfo` metadata failure; a clean temporary copy was ad-hoc signed, strict-verified, installed, and launched on the booted iPhone 17 Pro simulator.
- The iOS simulator is signed out, so the native material's authenticated feed state remains source/build/launch verified rather than screenshot verified.

**Comparison History**

- Rejected pass: `/tmp/shink-faded-glass-final-comparison.png` compressed the entire fade into the 92 dp tab row. User evidence correctly identified the P1 result: the tint was too subtle and the bar read as having no background.
- Correction: separated the compact 92 dp tab row from a 32 dp feather, applied an explicit translucent body to the whole tab row, and moved iOS native blur to the complete material host.
- Final pass: `/tmp/shink-bottom-material-pass2-comparison.png` visibly shows the teal body, underlying page continuity, and the gradual boundary. `/tmp/shink-bottom-material-karakters-pass2.png` confirms the same effect over high-contrast character artwork and proves tab navigation still works.

**Verification**

- 19 focused mobile chrome and safe-area source tests passed.
- Android Release build passed and produced `Shink.Mobile/bin/Release/net10.0-android/com.schink.stories.mobile-Signed.apk`.
- Android emulator install, launch, scrolling, navigation, screenshot comparison, and comparative frame rendering checks passed.
- iOS final source compile, clean-copy signature verification, simulator installation, and launch passed; authenticated tab-bar visual inspection is blocked by signed-out simulator state.

**Open Questions**

- None for the requested implementation scope.

final result: passed

# Android Shared Top and Bottom Glass QA — 2026-09-01

**Comparison Target**

- Source visual truth: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-e4502e83-299d-4661-b110-d3a43e72747c.png` (738 x 228 physical pixels, 369 x 114 logical pixels at 2x).
- Approved iOS material reference: `/tmp/shink-more-transparent-glass-current.png` (1242 x 2688 pixels).
- Final Android implementation: `/tmp/shink-android-glass-final.png` and the scrolled state `/tmp/shink-android-glass-final-scroll-verified.png` (1080 x 2400 pixels at 420 dpi, approximately 411 x 914 dp).
- Focused source-to-Android comparison: `/tmp/shink-glass-reference-android-comparison-final.png` (1080 x 834 pixels).
- Focused iOS-to-Android comparison: `/tmp/shink-glass-ios-android-comparison-final.png` (2160 x 820 pixels).
- Density normalization: the source bottom bar was scaled to 1080 pixels wide; the iOS top and bottom crops were normalized to the Android crop width. The source artwork and device content differ, so the comparison is limited to blur softness, colour preservation, transparency, fade continuity, icon contrast, and bar geometry.
- State: authenticated Stories feed at rest and after scrolling, plus authenticated Search, Downloads, and Characters destinations.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested Android glass scope.
- Fonts and typography: the existing bold white Afrikaans labels, logo, notification bell, menu icon, and four navigation labels retain their family, weight, size, line height, alignment, and antialiasing. No typography was recreated or substituted.
- Spacing and layout rhythm: the 92dp tab surface, 124dp feathered navigation host, four equal touch targets, toolbar controls, safe-area placement, and edge-to-edge widths remain unchanged. Android now extends the top material continuously behind the status icons, eliminating the former status-area seam.
- Colors and visual tokens: top and bottom use the same `#14005E68` low-opacity teal wash at 0.88 native-layer opacity, with the approved warm-white surface stops. The material preserves artwork colour instead of introducing a grey or white cast.
- Image quality and asset fidelity: both bars blur real page artwork captured beneath them. Existing raster artwork, text logo, and vector navigation icons remain sharp in the foreground; no visible asset was approximated or replaced.
- Copy and content: `Stories`, `Soek`, `Afgelaai`, and `Karakters` are unchanged. No customer-facing copy changed.
- Edge treatment: the bottom material fades in from the page-facing top edge and the top material fades out from its page-facing bottom edge using mirrored masks. Neither bar has a hard divider line.

**Full-View and Focused Comparison Evidence**

- `/tmp/shink-glass-reference-android-comparison-final.png` places the supplied glass target and final Android bottom bar in one normalized comparison input. Both retain recognizable colour fields beneath a softened translucent surface, keep crisp white controls above the blur, and dissolve into the content without a separator.
- `/tmp/shink-glass-ios-android-comparison-final.png` places approved iOS and final Android top/bottom crops in one input. The material geometry, opacity model, icon hierarchy, and opposing fades agree; remaining colour differences come from the different artwork underneath each capture.
- `/tmp/shink-android-glass-final-scroll-verified.png` confirms the top material remains continuous through the Android status area after scrolling and that its own logo/actions are excluded from the backdrop capture.
- Focused regions were required because blur softness and edge fades are not reliably judged from a whole-screen thumbnail.

**Interaction and Runtime Evidence**

- Built, installed over the existing app without clearing data, and cold-launched the final Android Debug package on the API 35 emulator.
- Tapped `Soek`, `Afgelaai`, `Karakters`, and `Stories`; all four destinations opened and retained the shared top and bottom treatment.
- Repeatedly scrolled the Stories feed and captured both settled and moving-content states. The app remained running with no fatal exception or ANR in the checked log window.
- The final material reuses downsampled native backdrop snapshots while content moves and refreshes after scrolling settles. A six-swipe Debug/emulator trace recorded 97 frames, 51 janky frames (52.58%), 42ms median, 85ms p90, and one slow bitmap upload. This is materially lower than the rejected continuous-live-blur pass (95.54% janky, 85ms median, 150ms p90), but it is not physical-device smoothness proof.

**Comparison History**

- Baseline: Android used only transparent/static colour, leaving the artwork sharp beneath both bars. P1: it did not provide the requested glass blur.
- Pass 1: two continuously updating native BlurView surfaces achieved the target appearance. P2: the six-swipe trace measured 95.54% janky frames, so the live implementation was rejected.
- Pass 2: cached, downsampled native captures removed continuous blur invalidation. P2: radius 2 left too much detail, and Android's status-area strip remained visually separate from the top material.
- Final fix: increased the cached blur to radius 4 over three passes, added the same edge-to-edge top material layer to Stories, Search, Downloads, and Characters, mirrored the edge masks, staggered settled-scroll refreshes, and excluded the foreground toolbar controls from capture.
- Post-fix evidence: the final focused comparisons show the softer Apple-style backdrop, approved clear teal, matching transparency, continuous status/top-bar surface, and borderless fades. The earlier P1/P2 visual findings are resolved.

**Verification**

- 26 focused mobile chrome, Luister safe-area, and Search source tests passed.
- Android Debug build for `net10.0-android`: passed with 0 errors and the two existing API 35/36 target-SDK warnings.
- The old BlurView AAR is excluded from the Android build; the final build no longer emits its native binding warning.
- Final emulator install, cold launch, authenticated tab navigation, scrolling, screenshot comparison, process check, and focused frame diagnostics completed.

**Open Questions**

- None for visual acceptance. A physical Android phone remains the appropriate evidence layer for final scrolling smoothness.

**Implementation Checklist**

- [x] Use the same transparent teal material values for both Android bars.
- [x] Apply real backdrop softening rather than a flat translucent fill.
- [x] Mirror the page-facing fade so neither edge creates a visible line.
- [x] Extend the top material behind the Android status area.
- [x] Apply the shared result to all four bottom-tab destinations.
- [x] Preserve existing bar geometry, typography, icons, copy, and navigation behavior.

**Follow-up Polish**

- None required for the requested visual scope.

final result: passed

# Game Config Close Icon Alignment QA — 2026-09-01

**Comparison Target**

- Source visual truth: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-2e0a6411-2911-44ee-980a-48cb1f10f3f1.png`.
- Final Android screenshot: `/Users/luanvanderwalt/.codex/visualizations/2026/08/31/01a05808-1597-7c12-8ac0-5e6a30a7166b/karakter-raai-close-centered.png`.
- Focused implementation crop: `/Users/luanvanderwalt/.codex/visualizations/2026/08/31/01a05808-1597-7c12-8ac0-5e6a30a7166b/karakter-raai-close-centered-focus.png`.
- Viewport: Android emulator, 1080 x 2400 physical pixels, portrait.
- State: Karakter Raai configuration screen with no difficulty selected.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested alignment scope.
- The close icon is a fixed 26 x 26 Font Awesome image centered horizontally and vertically inside the existing 48 x 48 circular button.
- Removing the font-baseline-dependent text glyph and its manual vertical translation eliminates the visible offset.
- Border, fill, close colour, circle size, placement, and back-navigation behavior remain unchanged.
- The Karakter Raai and Karakter Pare configuration screens use the same close-button implementation.

**Comparison History**

- Initial state: the close mark used a text glyph with a manual `TranslationY = -1`, so the visual centre depended on the font baseline.
- Final fix: replaced it with the app's existing Font Awesome close icon in a centered square image and removed the translation.
- Post-fix evidence: the reference and focused emulator crop were inspected together; the icon centre aligns with the circular control centre on both axes.

**Verification**

- 20 focused game configuration tests passed.
- Android Debug build for `net10.0-android` passed with 0 errors and the existing target-SDK warnings.
- The signed Debug APK installed and launched on the Android emulator.
- The lower close button returned to the previous screen during the emulator interaction check.

**Open Questions**

- None for the requested scope.

final result: passed

# Previous Bottom-Bar Blur QA

**Comparison Target**

- Source visual truth: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-4f6a1ffc-77f1-486c-9064-43082b827d4a.png`
- Implementation screenshot: `/tmp/schink-live-blur-after.png`
- Focused side-by-side comparison: `/tmp/schink-transparent-blur-comparison.png`
- Dynamic before/after comparison: `/tmp/schink-live-blur-before-after.png`
- Viewport: Android API 35 emulator, 1080 x 2400 physical pixels at 420 dpi (approximately 411 x 914 dp), portrait.
- Source pixels: 737 x 1600. Implementation pixels: 1080 x 2400.
- Density normalization: the source bottom region was cropped and resized to 1080 pixels wide; the implementation bottom region was cropped at native density. Both regions were placed in one 2184 x 620 comparison image. Browser CSS size and `deviceScaleFactor` do not apply to this native app capture.
- State: the reference shows the Stories feed over colourful cards; the implementation shows the authenticated Karakters grid over colourful cards. The app content differs, but the persistent bottom-bar state and its interaction with underlying colour are directly comparable.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested bottom-bar blur scope.
- Backdrop material: both reference and implementation preserve broad colour regions from the underlying cards while removing image detail. The implementation has no flat opaque slab and keeps the content visibly continuous behind the bar.
- Fonts and typography: tab labels remain crisp, bold, white, centered, and comparable in size and weight to the reference. All Afrikaans copy is unchanged.
- Spacing and layout rhythm: four equal-width touch targets, icon-to-label spacing, full-width placement, and bottom safe-area treatment align with the reference. The Android system gesture indicator accounts for the small extra lower inset.
- Colors and visual tokens: the blur overlay colour is fully transparent. Pink, green, yellow, and dark teal come only from the cards beneath the bar, with no additional teal, grey, or milky wash.
- Image quality and asset fidelity: the original app cards remain sharp above the bar and become intentionally soft only under the material. Existing Font Awesome/vector navigation icons remain crisp; no placeholder or recreated artwork is used.
- Copy and content: `Stories`, `Soek`, `Afgelaai`, and `Karakters` match the reference.

**Interaction Evidence**

- Launched the installed Android build and verified the live blur after remote feed content loaded.
- Scrolled the feed and confirmed the backdrop updates with the page rather than showing a static colour.
- Tapped `Karakters` and verified navigation to the character grid.
- Tapped `Stories` and verified navigation back to the Luister feed.
- Checked Android logs after launch, scroll, and navigation; no fatal app exception was present.
- Compared the blur before and after a native CollectionView scroll; the initial purple/yellow/green capture is replaced by the green/olive/grey colours of the newly underlying artwork.

**Comparison History**

- Pass 1: `/tmp/schink-blur-pass1.png` showed only a translucent tint with no visible backdrop softening. P1: the requested iPhone-style blur was missing.
- Pass 2: the first custom RenderNode capture attempts did not visibly blur and later caused an Android `IllegalStateException` during nested recording. P0: runtime crash. Fix: removed the recursive custom capture implementation entirely.
- Pass 3: `/tmp/schink-blur-blurview-pass3.png` used a maintained backdrop-blur view and removed the crash, but a 24-point radius over-softened the colours. P2: the result was more uniform than the reference.
- Pass 4: `/tmp/schink-bottom-blur-karakters.png` retained a subtle dark-teal overlay. P2: although blurred, the bar still read as a separate coloured material rather than transparent blur.
- Final fix: kept the 12-point live blur but removed the overlay tint entirely; a transparent colour is now passed through without fallback substitution.
- Pass 5: `/tmp/schink-scroll-blur-before-after-bottom.png` showed that Android retained the initial purple/green capture after scrolling even though some background pixels changed. P1: the blur snapshot was stale and did not represent the artwork currently underneath.
- Final refresh fix: added a native scroll observer that invalidates both the captured root and BlurView surface whenever MAUI's CollectionView scrolls.
- Post-fix evidence: `/tmp/schink-live-blur-before-after.png` shows the initial story-card colours disappear after scrolling and the new artwork colours replace them while icons and labels remain fixed and sharp. All earlier P0/P1/P2 findings are resolved.

**Open Questions**

- None for the requested scope.

**Implementation Checklist**

- [x] Replace the Android flat tint with live backdrop blur.
- [x] Keep the blur overlay tint fully transparent.
- [x] Refresh the Android blur capture continuously during native scrolling.
- [x] Preserve colourful shapes beneath the bar while obscuring details.
- [x] Keep icons, labels, safe-area placement, and touch targets sharp and functional.
- [x] Preserve the transparent top-left logo treatment.
- [x] Verify focused source tests, Android build, emulator rendering, scroll updates, navigation, and fatal logs.

**Follow-up Polish**

- None required for this pass.

final result: passed

# Tablet Menu Close-Control QA — 2026-08-28

**Comparison Target**

- Source visual truth: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-f887902a-68b0-44c3-a380-b48ce334ebe2.png`
- Implementation screenshot: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/screenshot_optimized_b6fbff7b-0d46-4874-9d26-3921e9d5fed9.jpg`
- Viewport: AdoMobile iPad QA simulator, iOS 26.5, portrait.
- Source pixels: 966 x 804. Implementation capture pixels: 551 x 800. Native MAUI CSS size and browser device scale do not apply. The supplied image is a differently cropped tablet state, so the comparison evaluates the requested responsive redesign rather than pixel-for-pixel placement.
- State: authenticated Luister screen with the shared menu overlay open.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested scope.
- Fonts and typography: the existing bold menu hierarchy and button labels remain legible; all six Afrikaans action labels fit without wrapping.
- Spacing and layout rhythm: the tablet card is centered and capped at 720 device-independent units. Six actions form three balanced two-column rows instead of six oversized full-width rows.
- Colors and visual tokens: the existing cream card, charcoal actions, turquoise dimmed backdrop, radii, and shadow remain consistent with the supplied screen.
- Image quality and asset fidelity: the packaged Oortjies artwork remains centered and sharp; no artwork was recreated.
- Copy and content: the `Kanselleer` row is removed. A compact top-right Font Awesome close control replaces it and has the Afrikaans accessibility description `Maak menu toe`.

**Full-View and Focused Comparison Evidence**

- The source and implementation were opened together in one comparison input. The full view confirms the card no longer fills nearly the entire tablet width or crowds the bottom navigation.
- A separate focused crop was unnecessary because the menu labels, close icon, card bounds, and row spacing are all clearly readable in the 551 x 800 implementation capture.

**Interaction Evidence**

- Runtime accessibility snapshot exposed the close control as `mobile-menu-close`, label `Maak menu toe`.
- Tapping the close control dismissed the overlay; a follow-up runtime wait confirmed the identifier was gone.
- The six menu actions remained present as tappable controls.

**Comparison History**

- Initial supplied state: edge-to-edge tablet card, six vertically stacked full-width actions, and a seventh full-width `Kanselleer` row.
- Fix: capped and centered the tablet card, changed actions to a responsive two-column grid, and moved dismissal to a compact top-right close icon.
- Post-fix evidence: the iPad simulator capture shows a balanced three-row card with clear surrounding context and no `Kanselleer` row.

**Verification**

- Focused responsive-layout source tests: 5 passed, 0 failed.
- Android Debug build for `net10.0-android`: passed with 0 errors.
- iOS simulator source compiled successfully. Normal packaging encountered the known `com.apple.FinderInfo` metadata issue; the app was clean-copied, ad-hoc signed, strictly verified, installed, launched, and visually inspected on AdoMobile iPad QA.

**Open Questions**

- None for the requested scope.

final result: passed

# Persistent Mini-Player Physical iPhone Text QA — 2026-08-27

**Comparison Target**

- Defect reference: `/tmp/codex-remote-attachments/01a03f52-a7fc-7321-ab2c-ef34c04b393f/6FE4BD3F-4E38-42FE-8F58-4C414AA857BF/1-Pasted-Image-1.jpg`
- Corrected iOS simulator capture: `/tmp/schink-now-playing-physical-fix-after-clean.png`
- Focused side-by-side comparison: `/tmp/schink-now-playing-physical-fix-comparison.jpg`
- Viewport: iPhone 17 Pro simulator, iOS 26.5, portrait, 1206 x 2622 physical pixels.
- State: active persistent mini-player with `Nou speel` and `Beer baklei net`; a local preview-only startup state was removed immediately after capture.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested mini-player text scope.
- The supplied physical-iPhone screenshot compressed both text labels into an unreadable horizontal strip.
- The corrected player reserves the same 52-point height as the artwork, uses intrinsic-height text rows, and vertically centers both labels within that area.
- `Nou speel` and `Beer baklei net` are fully legible without changing the player artwork, controls, colors, radius, or overall 70-point height.
- The title remains single-line and tail-truncated for long story names.

**Verification**

- Persistent playback source tests: 3 passed, 0 failed.
- Android Debug build for `net10.0-android`: passed with 0 errors.
- iOS simulator source compiled to `Shink.Mobile.dll`; normal packaging encountered the repository's known `com.apple.FinderInfo` metadata issue.
- The preview app was clean-copied, ad-hoc signed, strictly verified, installed, launched, and captured.
- The final-source simulator app was clean-copied, ad-hoc signed, strictly verified, installed, and launched after the preview state was removed.
- The supplied reference and rendered implementation were inspected together in the focused side-by-side comparison.
- The temporary preview-only startup code was removed before final verification.

**Open Questions**

- None for the requested scope.

final result: passed

# Karakter Raai Persistent-Player Clearance QA — 2026-08-27

**Comparison Target**

- Source visual truth: `/tmp/codex-remote-attachments/01a04238-ef61-7a22-93f9-eaf210af5325/91FA43CC-B9B1-4F0A-A5D5-F6718F988EF7/1-Pasted-Image-1.jpg`
- Corrected simulator screenshot: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/screenshot_optimized_40833678-8bba-46c5-8a39-f8fa20186d95.jpg`
- Side-by-side comparison: `/tmp/schink-raai-player-scroll-comparison.jpg`
- Viewport: iPhone 17 Pro simulator, portrait, 368 x 800 screenshot pixels. The supplied 590 x 1280 screenshot and implementation screenshot were normalized into one 1180 x 1280 comparison.
- State: active persistent story player over the Karakter Raai difficulty screen.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested player-clearance scope.
- The existing artwork, typography, difficulty cards, Afrikaans copy, colors, spacing, and selection behavior remain unchanged.
- The difficulty content now scrolls independently while the back button and persistent player stay fixed.
- After one natural upward swipe, all three difficulty choices and the full `SPEEL NOU` button are visible above the player.
- No artwork was recreated or replaced.

**Interaction Evidence**

- Launched the page with an active persistent playback session in the iPhone simulator.
- Swiped the configuration content upward once and confirmed that `SPEEL NOU` became fully visible and tappable above the player.
- Confirmed the scroll container through automation id `karakter-raai-config-scroll` and the action through `karakter-raai-play`.
- Removed the temporary preview-only launch setup before the final builds.

**Verification**

- Focused `CharacterGuessGameTests`: 7 passed, 0 failed.
- Android Debug build for `net10.0-android`: passed with 0 errors.
- iOS simulator source compiled to `Shink.Mobile.dll`; the normal packaging step encountered the repository's known `com.apple.FinderInfo` signing metadata issue.
- A clean `ditto --norsrc --noextattr` copy was ad-hoc signed, passed strict code-sign verification, installed, and launched with the final source on the iPhone 17 Pro simulator.

**Open Questions**

- None for the requested scope.

final result: passed

# Karakter Pare Persistent-Player Clearance QA — 2026-08-27

**Comparison Target**

- Defect reference: `/tmp/codex-remote-attachments/01a04238-ef61-7a22-93f9-eaf210af5325/5B8562F6-A085-44B1-8D2E-5CF4CBFB4C45/1-Pasted-Image-1.jpg`
- Corrected scrolled-state capture: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/screenshot_optimized_e1d78285-b9b2-442f-a45d-424faf426bf0.jpg`
- Side-by-side comparison: `/tmp/schink-pare-player-scroll-comparison.jpg`
- Viewport: iPhone 17 Pro simulator, iOS 26.5, portrait.
- State: Karakter Pare configuration with the persistent player visible and the content scrolled far enough to reveal the primary action.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested player-clearance scope.
- The configuration content now owns a vertical scroll area inside the space reserved above the persistent player.
- One natural upward swipe reveals all three difficulty choices and the complete `SPEEL NOU` button above the player.
- The player remains fixed in its own bottom row and no longer covers the action.
- The back control remains fixed while the logo, tagline, options, and action scroll together.
- Existing artwork, typography, colours, card dimensions, selection behavior, and navigation code are unchanged.

**Verification**

- Character Match focused tests: 10 passed, 0 failed.
- Android Debug build for `net10.0-android`: passed with 0 errors.
- iOS simulator runtime snapshot identified the new `karakter-pare-config-scroll` scroll area and the `karakter-pare-play` action.
- Simulator swipe and post-scroll screenshot: passed; `SPEEL NOU` is fully visible above the player.
- The temporary preview-only startup harness was removed. The final simulator bundle was clean-copied, ad-hoc signed, strictly verified, installed, and launched.

final result: passed

# Persistent Now Playing Text Alignment QA — 2026-08-27

**Comparison Target**

- Defect reference: `/tmp/codex-remote-attachments/01a04238-ef61-7a22-93f9-eaf210af5325/2D4E08BA-BB98-4DCE-AA65-A969CCDD544B/1-Pasted-Image-1.jpg`
- Corrected iOS simulator capture: `/tmp/schink-now-playing-alignment-after.png`
- Focused side-by-side comparison: `/tmp/schink-now-playing-alignment-comparison.jpg`
- State: the same `Nou speel` and `Beer baklei net` copy was rendered in the persistent player. The simulator used a local preview state because the installed simulator session was signed out; the preview-only startup code was removed immediately after capture.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested text-alignment scope.
- The status and title now occupy two equal rows around the player midpoint instead of depending on a vertically centered stack measurement.
- `Nou speel` aligns to the bottom of the upper row and the title aligns to the top of the lower row, keeping the pair centered across iOS font metrics.
- The title remains inside the cream panel with clear lower padding and no clipping or overlap.
- Artwork size, action controls, typography, colours, outer dimensions, and interaction behavior are unchanged.

**Verification**

- Focused persistent-playback source tests: 3 passed, 0 failed.
- Android Debug build for `net10.0-android`: passed with 0 errors.
- iPhone 17 Pro simulator visual comparison: passed for the corrected text position.
- The temporary visual-preview harness was reverted; only the shared player layout and its focused source assertions remain.

final result: passed

## Supplied Knibbels Asset Update — 2026-08-24

- Replaced only the dedicated Soek-page mascot with the user-supplied 606 x 612 RGBA Knibbels artwork.
- Preserved the artwork's original pixels and aspect ratio; no generated or reconstructed artwork was used.
- Existing Oortjies artwork elsewhere in the app remains unchanged.

# Search Page Design QA — 2026-08-24

**Comparison Target**

- Source visual truth: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-37e6dc01-052f-4e50-9488-a33eaff29a88.png`
- Initial implementation screenshot: `/tmp/schink-search-final-initial.png`
- Active search screenshot: `/tmp/schink-search-final-typed.png`
- Viewport: Android API 35 emulator, 1080 x 2400 physical pixels at 420 dpi, portrait.
- State coverage: dedicated initial search page, focused keyboard state, populated result state, and persistent navigation chrome.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested search-page scope.
- Initial composition follows the reference hierarchy: branded header, illustrated character, large Afrikaans heading and instruction, prominent rounded search field, open teal breathing room, and persistent bottom navigation.
- The canonical existing Oortjies artwork is used instead of recreating or inventing brand artwork from the reference.
- Search mode collapses the decorative hero so the field and matching stories remain visible above the software keyboard.
- Result cards reuse the established story artwork and styling, with title, description, availability-aware action, and navigation to the story detail flow.
- The result entrance uses staggered fade, upward translation, and scale easing. Search input is debounced and focus remains on the field when the result collection refreshes.

**Interaction Evidence**

- Tapped the bottom `Soek` action and confirmed navigation to the dedicated page.
- Entered `arno`; the page returned `Arno Arend Sprei Sy Vlerke` and kept the search entry focused after the result appeared.
- Confirmed the populated result has automation id `story-search-result-arno-arend-sprei-sy-vlerke`.
- Checked Android error logs after launch, navigation, typing, and result rendering; no fatal app exception was present.

**Verification**

- Focused source tests: 17 passed, 0 failed.
- Android Debug build: passed for `net10.0-android`.
- Emulator navigation, initial layout, typing, focus retention, matching result, and keyboard-safe result layout: passed.

**Open Questions**

- None for the requested scope.

final result: passed

# Account Screen Safe-Area Color QA — 2026-08-27

**Comparison Target**

- Source visual truth: `/tmp/codex-remote-attachments/01a0422b-bbba-70e1-821d-b1fa465f70cf/E3EECE46-2EFA-4C1A-8850-0F43482140FF/1-Pasted-Image-1.jpg`
- Implementation screenshot: `/tmp/schink-account-after.png`
- Full-view comparison: `/tmp/schink-account-comparison.png`
- Focused safe-area comparison: `/tmp/schink-account-safe-area-comparison.png`
- Viewport: iPhone 17 Pro simulator, iOS 26.5, portrait; 1206 x 2622 physical pixels.
- Source pixels: 590 x 1280. Implementation pixels: 1206 x 2622. The implementation was normalized to 590 x 1280 for the side-by-side comparisons; native MAUI CSS size and browser `deviceScaleFactor` do not apply.
- State: signed-out account landing screen. The supplied screenshot shows the defect; the requested target is for both outer safe-area bands to continue the turquoise screen background.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested safe-area color scope.
- Fonts and typography: existing type family, weight, size, line height, wrapping, and Afrikaans copy are unchanged by the fix.
- Spacing and layout rhythm: the artwork, text, buttons, margins, radii, and vertical composition are unchanged. The newer simulator device has Dynamic Island chrome, which is an expected platform difference from the supplied device screenshot.
- Colors and visual tokens: the same background image and turquoise overlay now render continuously behind the status bar and home-indicator areas. The previous flat dark-teal bands are absent.
- Image quality and asset fidelity: the existing packaged background, mouse, and Schink Stories logo assets remain unchanged and sharp; no artwork was recreated.
- Copy and content: all visible copy matches the supplied screen.

**Interaction Evidence**

- Launched the clean-signed simulator app bundle and captured the signed-out account landing screen.
- The primary buttons were not activated because the change is confined to the outer host's safe-area layout and does not alter gesture recognizers or navigation.

**Comparison History**

- Initial supplied state: the persistent playback host inset its content, exposing the page's flat dark-teal background above and below the turquoise artwork.
- Fix: enabled the host's existing edge-to-edge mode for `AccountPage`.
- Post-fix evidence: both full-view and focused comparisons show the turquoise backdrop reaching the physical top and bottom edges with no separate color bands.

**Open Questions**

- None for the requested scope.

**Implementation Checklist**

- [x] Extend the existing account background through the top safe area.
- [x] Extend the existing account background through the bottom safe area.
- [x] Preserve all account-screen artwork, content, controls, and navigation.
- [x] Pass the focused source tests and verify the simulator rendering.

**Follow-up Polish**

- None required for this pass.

final result: passed

# Karakter Difficulty Initial-Fit QA — 2026-08-27

**Comparison Target**

- Source visual truth: `/tmp/codex-remote-attachments/01a04238-ef61-7a22-93f9-eaf210af5325/AA0A4320-E8B3-47BB-9AD9-8F13E506ADF0/1-Pasted-Image-1.jpg`
- Karakter Pare initial screenshot: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/screenshot_optimized_106c287f-b75e-4a78-84c7-c89870e5e215.jpg`
- Karakter Raai initial screenshot: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/screenshot_optimized_4ff5d4ec-9599-481b-8909-3ff5be1ce2ad.jpg`
- Pare side-by-side comparison: `/tmp/schink-difficulty-initial-pare-comparison.jpg`
- Raai before-and-after comparison: `/tmp/schink-difficulty-initial-raai-comparison.jpg`
- Viewport: iPhone 17 Pro simulator, portrait, 368 x 800 screenshot pixels. Comparisons were normalized to the supplied 590 x 1280 reference size.
- State: initial difficulty selection with an active persistent player and no content swipe.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested initial-fit scope.
- Karakter Pare now follows the supplied reference's vertical bands for the back button, full-size logo, tagline, difficulty cards, `SPEEL NOU`, and persistent player.
- Karakter Raai uses the same responsive treatment and shows its full primary action above the player on initial load.
- Normal phone layouts preserve the established artwork and component sizes; the composition moves upward by removing excess top and bottom whitespace.
- Both screens retain a vertical `ScrollView` with a hidden indicator. A compact layout activates only below 700 device-independent units, and very short screens can still scroll if needed.

**Interaction Evidence**

- Fresh runtime snapshots exposed `karakter-pare-play` and `karakter-raai-play` in the initial frame without a swipe.
- Both snapshots retained their configuration scroll areas and all three accessible difficulty choices.
- The fixed back button and persistent player remained outside the scrolling content.
- The temporary preview-only startup harness was removed before final verification.

**Verification**

- Focused Character Match and Character Guess tests: 17 passed, 0 failed.
- Android Debug build for `net10.0-android`: passed with 0 errors.
- iOS simulator source compiled to `Shink.Mobile.dll`; normal packaging encountered the repository's known `com.apple.FinderInfo` metadata issue.
- The final-source simulator app was clean-copied, ad-hoc signed, strictly verified, installed, and launched.

**Open Questions**

- None for the requested scope.

final result: passed

# iPhone Menu Visibility Regression QA — 2026-08-28

**Comparison Target**

- Source visual truth: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/codex-clipboard-f887902a-68b0-44c3-a380-b48ce334ebe2.png` plus the user's report that the iPhone showed only the dim backdrop.
- Implementation screenshot: `/var/folders/rs/x50hrf3x2hz_92h06ptk8s_w0000gn/T/screenshot_optimized_5c2f42b1-a62a-4934-8437-fdc62a633989.jpg`
- Viewport: iPhone 17 Pro simulator, iOS 26.5, portrait; 368 x 800 captured pixels.
- Source pixels: 966 x 804 tablet crop. Implementation pixels: 368 x 800 phone capture. Native MAUI CSS size and browser device scale do not apply; the comparison checks component presence, hierarchy, and preserved visual language across responsive states.
- State: authenticated Luister screen with the menu overlay open.

**Findings**

- No actionable P0, P1, or P2 differences remain in the phone visibility scope.
- Fonts and typography: all six action labels and the `Menu` heading are visible, centered, and unwrapped.
- Spacing and layout rhythm: the phone retains the intended single-column action stack. The card uses a finite width equal to the viewport minus 40 device-independent units and remains fully visible above the bottom bar.
- Colors and visual tokens: cream card, charcoal buttons, turquoise dim layer, rounded corners, and shadow match the established menu styling.
- Image quality and asset fidelity: the existing packaged Oortjies image remains sharp and centered.
- Copy and content: `Kanselleer` remains replaced by the compact top-right close icon; all six menu destinations remain present.

**Full-View and Focused Comparison Evidence**

- The original tablet reference and repaired iPhone implementation were opened together in one comparison input. The phone capture clearly shows the card rather than only the dim backdrop.
- A focused crop was unnecessary because the complete phone card, every label, the close icon, and both horizontal gutters are readable in the full 368 x 800 capture.

**Interaction Evidence**

- Logged into the local iPhone simulator using user-provided credentials; credentials were not written to source files.
- Tapping the top-right menu trigger exposed `mobile-menu-close` and all six action controls in the runtime accessibility snapshot.
- Tapping `mobile-menu-close` dismissed the overlay; a follow-up runtime wait confirmed the identifier was gone.

**Comparison History**

- Regression state: the phone branch set `WidthRequest` and `MaximumWidthRequest` to `-1`, allowing the iOS overlay host to collapse the card while leaving the dim layer visible.
- Fix: assign the phone card a finite viewport-derived width and center it.
- Post-fix evidence: the final-source iPhone capture shows the complete single-column menu and working close control.

**Verification**

- Focused responsive-layout source tests: 5 passed, 0 failed.
- Android Debug build for `net10.0-android`: passed with 0 errors after a targeted restore.
- iOS simulator source compiled successfully. Normal packaging encountered the known `com.apple.FinderInfo` metadata issue; the app was clean-copied, ad-hoc signed, strictly verified, installed, launched, logged into, and visually inspected on iPhone 17 Pro.

**Open Questions**

- None for the requested scope.

final result: passed

# Persistent Mini-Player Alignment QA — 2026-08-30

**Comparison Target**

- Reference screenshot: `/tmp/codex-remote-attachments/01a05145-3dea-7b10-a637-8e4b948b6f7b/06834E4E-A141-4F01-BB06-5E80E6E46CA5/1-Pasted-Image-1.jpg`.
- Signed-in implementation screenshot: `/tmp/schink-mini-player-signed-in-qa.png`.
- Focused side-by-side comparison: `/tmp/schink-mini-player-signed-in-focused-comparison.png`.
- Viewport: iPhone 17 Pro simulator, iOS 26.5, portrait; 1206 x 2622 captured pixels. The supplied reference is 590 x 1280 and has the same phone aspect ratio.
- State: authenticated Luister screen with the persistent mini-player above the four-item bottom navigation.

**Findings**

- No actionable P0, P1, or P2 alignment differences remain in the requested mini-player scope.
- The reference shows the status line escaping above the cream card while the title remains lower in the content area.
- The corrected player uses two equal-height text rows: status is bottom-aligned in the first row and title is top-aligned in the second row.
- Artwork, text block, circular play control, and close control now share a centered 52-unit inner row inside the existing 70-unit card.
- Existing colors, typography, corner radius, margins, control sizes, and bottom-navigation spacing are preserved.

**Full-View and Focused Comparison Evidence**

- The full signed-in screenshot confirms the player remains above the bottom navigation and within the phone safe area.
- The focused comparison puts the supplied screenshot and corrected player in one image. It shows the original clipped status line on the left and the contained, vertically centered status/title stack on the right.
- Representative packaged artwork was used only to activate the real persistent-player layout during visual QA; artwork selection is outside this alignment change.

**Interaction Evidence**

- The local simulator signed in through the app's normal email authentication service using user-provided credentials.
- Credentials were supplied only as process-scoped QA environment values and were not written to source files.
- The preview-only playback state was removed after capture. A clean final-source app was rebuilt, installed over the QA build, and launched successfully while preserving the authenticated session.

**Verification**

- Focused persistent-playback source tests: 5 passed, 0 failed.
- Android Debug build for `net10.0-android`: passed with 0 errors and the two existing target-SDK warnings.
- iOS simulator source compiled to `Shink.Mobile.dll`; normal packaging encountered the known `com.apple.FinderInfo` metadata issue.
- The final-source iOS app was clean-copied, ad-hoc signed, strictly verified, installed, and launched on iPhone 17 Pro.

**Open Questions**

- None for the requested alignment scope.

final result: passed

# Bottom Navigation Top-Spacing QA — 2026-08-30

**Comparison Target**

- Source visual truth: `/tmp/codex-remote-attachments/01a05369-1142-7282-b9c7-4d91f8700958/952D8386-354D-4278-98D9-7DFAA3F025F9/1-Pasted-Image-1.jpg`.
- Implementation screenshot: `/tmp/shink-nav-huawei-profile.png`.
- Source-to-final comparison: `/tmp/shink-bottom-nav-reference-final.png`.
- Exact emulator before/after comparison: `/tmp/shink-bottom-nav-before-after.png`.
- Source viewport: 590 x 1280 supplied tester screenshot. Implementation viewport: Android API 35 emulator overridden to a tester-like 1080 x 2340 at 480 dpi, portrait.
- Density normalization: the implementation was scaled to the source's 590 x 1280 comparison frame. The combined comparison was rendered at 2x backing scale as 2360 x 2560 pixels. Native MAUI CSS size and browser device scale do not apply.
- State: both captures show the authenticated Stories feed with the `Stories` tab selected. Feed content and scroll position differ, so the comparison is limited to the persistent bottom-navigation geometry and preserved visual treatment.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested top-spacing scope.
- Fonts and typography: the four existing bold white Afrikaans labels retain their family, size, weight, centering, and single-line treatment.
- Spacing and layout rhythm: the shared navigation surface is 92 device-independent units high instead of 114. The top edge moves down while all four tab-item bounds stay unchanged, removing only the empty band above the controls.
- Colors and visual tokens: the existing static translucent dark-teal tint, icon colors, and selected treatment are unchanged.
- Image quality and asset fidelity: existing vector navigation icons and story artwork remain unchanged and sharp; no assets were recreated.
- Copy and content: `Stories`, `Soek`, `Afgelaai`, and `Karakters` are unchanged.

**Full-View and Focused Comparison Evidence**

- `/tmp/shink-bottom-nav-reference-final.png` places the supplied tester screenshot and the tester-profile emulator capture in one normalized comparison input. The final bar has materially less empty space above the icon row while retaining the same bottom alignment.
- `/tmp/shink-bottom-nav-before-after.png` compares the same emulator viewport and authenticated feed state before and after. The navigation bounds changed from `[0,2100][1080,2400]` to `[0,2158][1080,2400]`, a 58-pixel or 22dp reduction, while each tab remained at its original bounds.
- A separate crop was unnecessary because the complete navigation surface, icon row, labels, and gesture inset are clearly readable in the combined comparisons.

**Interaction Evidence**

- Installed and cold-launched the signed Release APK on the Android API 35 emulator without clearing app data.
- Tapped `Soek`, `Afgelaai`, `Karakters`, and `Stories`; each destination opened successfully.
- Runtime accessibility snapshots showed the 92dp navigation bounds on all four destinations.
- At the tester-like 480dpi profile, the final bar measured `[0,2064][1080,2340]`, exactly 276 pixels or 92dp.

**Comparison History**

- Initial state: the visible bar was 114dp high and the page overlay host was 152dp high. P2: the supplied tester screenshot showed the resulting excess top space, and the oversized transparent host also intercepted touches above the visible bar.
- Pass 1: tying the host to the visible 114dp bar removed the invisible 38dp host mismatch but did not materially change the rendered top band. The P2 visual finding remained.
- Final fix: reduced the shared visible navigation height to 92dp and tied every page host to that single value.
- Post-fix evidence: the bar top moved down by 22dp while tab-item bounds, typography, colors, bottom inset, and navigation behavior stayed unchanged. The earlier P2 finding is resolved.

**Verification**

- Focused mobile chrome, safe-area, and search source tests: 23 passed, 0 failed.
- Android Release build for `net10.0-android`: passed with 0 errors and the existing target-SDK warnings.
- iOS simulator source compiled to `Shink.Mobile.dll`. Normal packaging encountered the repository's known `com.apple.FinderInfo` metadata issue; the app was clean-copied, ad-hoc signed, strictly verified, installed, and launched on iPhone simulators. Those simulators stopped at the unauthenticated landing screen, so the iOS bottom bar was not visually exercised in this pass.
- Physical tester-device confirmation remains outside this local emulator pass.

**Open Questions**

- None for the requested scope.

**Implementation Checklist**

- [x] Reduce only the empty top portion of the shared bottom navigation.
- [x] Apply the same shared geometry to Stories, Search, Downloads, and Characters.
- [x] Preserve icon, label, tint, touch-target, and gesture-inset placement.
- [x] Verify the tester-like density profile and all four tab destinations on Android.
- [x] Keep the layout correction shared so iOS receives the same benefit.

**Follow-up Polish**

- None required for this pass.

final result: passed
# Feathered Bottom Tab Bar QA — 2026-08-30

**Comparison Target**

- Source visual truth: `/tmp/codex-remote-attachments/01a05369-1142-7282-b9c7-4d91f8700958/BAE4B75C-D617-4026-B4B3-2D8C17D61832/1-Pasted-Image-1.jpg`
- Final Android implementation screenshot: `/tmp/shink-faded-glass-final-launch.png`
- Focused side-by-side comparison: `/tmp/shink-faded-glass-final-comparison.png` (reference left, implementation right).
- Android viewport: API 35 emulator, 1080 x 2400 physical pixels at 420 dpi (approximately 411 x 914 dp), portrait.
- Source pixels: 1179 x 435. The final comparison uses the Android screenshot's bottom 399 pixels, normalized to 1179 x 435 beside the supplied reference.
- State: authenticated Stories feed with colourful artwork and copy continuing behind the shared bottom tab bar.

**Findings**

- No actionable P0, P1, or P2 differences remain in the requested bottom-bar scope.
- Separation treatment: the top of the bar begins transparent and feathers through two teal opacity stops into the fixed lower teal. The page therefore flows into the bar without a border or hard horizontal line.
- Android material: the bar deliberately uses a compositor-friendly static gradient rather than live backdrop capture. This preserves the translucent/glass-like visual hierarchy without reintroducing scroll-driven blur invalidation.
- Apple material: iOS 15 and later layers `SystemUltraThinMaterialDark` native blur beneath the shared teal gradient. A gradient mask fades the native material itself at the top edge, so the blur does not begin on a hard line.
- Fonts and typography: the existing crisp, bold, white tab labels and vector icons are unchanged. `Stories`, `Soek`, `Afgelaai`, and `Karakters` match the supplied reference.
- Spacing and layout rhythm: the compact 92 dp overlay height, four equal touch targets, icon-to-label spacing, and safe-area placement are unchanged.
- Colors and visual tokens: the fade starts transparent, moves through `#1812343B` and `#7812343B`, and reaches `#B812343B` at the bottom. The fixed base hue remains the requested starting teal.
- Image quality and asset fidelity: existing card artwork and navigation icons are unchanged; no placeholder or recreated assets were introduced.
- Copy and content: no customer-facing copy changed.

**Interaction and Performance Evidence**

- Installed and cold-launched the final signed Android Release APK on the API 35 emulator.
- Verified the authenticated Stories feed, scrolling, and shared tab navigation after installation.
- Six repeated feed swipes rendered 101 frames with 2 janky frames (1.98%), 17 ms median, 20 ms p90, 21 ms p95, and zero missed-vsync frames.
- Compiled the final iOS source through `Shink.Mobile.dll`. The normal package signature encountered the repository's known `com.apple.FinderInfo` metadata failure; a clean temporary copy was ad-hoc signed, strict-verified, installed, and launched on the booted iPhone 17 Pro simulator.
- The iOS simulator is signed out, so the native material's authenticated feed state remains source/build/launch verified rather than screenshot verified.

**Comparison History**

- Pass 1: `/tmp/shink-faded-glass-comparison.png` showed that the first fixed tint left too much sharp card detail competing with the labels. P2: the lower bar needed slightly more visual separation.
- Final fix: strengthened the midpoint from `#7012343B` to `#7812343B` and the lower stop from `#A612343B` to `#B812343B`, while keeping the leading stop transparent.
- Pass 2: `/tmp/shink-faded-glass-final-comparison.png` shows a soft, borderless entry into the bar and cleaner white-label contrast. The remaining sharper Android artwork is an intentional platform performance choice; iOS receives native blur beneath the same fade.

**Verification**

- 19 focused mobile chrome and safe-area source tests passed.
- Android Release build passed and produced `Shink.Mobile/bin/Release/net10.0-android/com.schink.stories.mobile-Signed.apk`.
- Android emulator install, launch, scrolling, navigation, screenshot comparison, and frame rendering checks passed.
- iOS final source compile, clean-copy signature verification, simulator installation, and launch passed; authenticated tab-bar visual inspection is blocked by signed-out simulator state.

**Open Questions**

- None for the requested implementation scope.

final result: passed
