# Suurlemoentjie Remotion Video Design

## Goal

Create the first Schink Karakters Remotion video as a standalone `1920x1080` landscape composition that opens with a Schink Stories intro, moves into a bold Afrikaans-first "Who’s that Pokemon?"-style mystery card, holds the mystery art for a couple of seconds, and reveals the full `Suurlemoentjie` character art.

## Scope

This rollout covers only the first character pass:

- One new Remotion composition for `Suurlemoentjie`
- No audio yet
- Afrikaans-first copy
- Landscape output only
- Visual direction locked to `Opsie A: Speletjieskou`

This rollout does not yet cover:

- `Kwaaibok`
- `Seekoei`
- Alternate aspect ratios
- Music, voice-over, or sound effects
- Batch generation for all characters

## Repo Context

The existing Remotion workspace already lives in `email-flows-video/`. That project currently contains an unrelated email-flow explainer composition. The Karakters work should be added as a new composition rather than replacing the existing one, so both outputs can coexist.

The approved source assets already exist in the main app:

- `Shink/wwwroot/branding/schink-stories-logo-white.png`
- `Shink/wwwroot/branding/characters/suurlemoentjie-mystery.png`
- `Shink/wwwroot/branding/characters/suurlemoentjie.png`

For Remotion, the required files should be copied into `email-flows-video/public/branding/...` and referenced through `staticFile()`.

## Visual Direction

The selected style is the high-energy game-show direction:

- Bold teal/yellow burst background
- Large all-caps title card
- Fast punchy intro motion
- Bright reveal transition
- Big centered character staging

The visual language should feel inspired by the familiar reveal format without directly copying any one original frame design verbatim. It should read as Schink-branded, not generic fan art.

## Timeline

Target duration: about `10 seconds` at `30fps` (`300 frames`).

Suggested scene breakdown:

1. `0:00-0:02` Schink Stories intro
2. `0:02-0:03` Title hit: `Wie is daardie karakter?`
3. `0:03-0:06` Mystery hold with `suurlemoentjie-mystery.png`
4. `0:06-0:07` Flash / burst reveal
5. `0:07-0:10` Final answer hold with `suurlemoentjie.png`

Small timing adjustments are allowed during implementation if they improve readability or motion, but the mystery image must stay on-screen for a clear multi-second hold before the reveal.

## Motion Rules

- All motion must be frame-driven with Remotion hooks and helpers.
- No CSS transitions or CSS animations.
- Use `useCurrentFrame()` plus `interpolate()` and/or `spring()` for scale, opacity, translation, burst, and flash effects.
- Keep motion legible and punchy rather than hyperactive.

## Copy

Afrikaans-first copy for the first video:

- Intro brand treatment: `Schink Stories`
- Main question card: `Wie is daardie karakter?`
- Reveal lockup: either `Dis Suurlemoentjie!` or `Suurlemoentjie`

If the reveal frame feels visually overloaded, prefer the simpler single-name lockup.

## Composition Strategy

Add a new composition alongside the existing one.

Expected structure:

- Keep `email-flows-video/src/Root.tsx` as the composition registry
- Add a dedicated component file for the Karakters reveal flow
- Keep the composition self-contained so future characters can reuse the same structure with different props or a small dataset

This first pass should avoid premature abstraction, but the file layout should make it easy to add `Kwaaibok` and `Seekoei` next.

## Verification

Required validation for this rollout:

- `npm` or `npx` lint/type-check path for the Remotion project
- One still render or short render sanity check to confirm layout, timing, and asset loading

Because this repo often contains unrelated active work, verification should stay scoped to `email-flows-video` and not trigger broad solution-level validation.

## Risks and Guardrails

- Do not overwrite the existing email-flow composition unless there is a deliberate migration reason
- Do not pull in unnecessary new dependencies if the effect can be built with base Remotion primitives
- Do not introduce audio placeholders that imply sound is implemented
- Do not use app runtime paths directly from the main site inside Remotion; copy the needed assets into the Remotion `public/` tree

## Success Criteria

The first pass is successful when:

1. A new `1920x1080` Remotion composition exists for the Suurlemoentjie reveal
2. The video starts with a Schink Stories intro
3. The mystery image appears first and holds for a couple of seconds
4. The full-colour Suurlemoentjie reveal lands cleanly
5. The composition builds and renders successfully without audio
