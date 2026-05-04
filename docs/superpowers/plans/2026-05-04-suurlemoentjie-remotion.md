# Suurlemoentjie Remotion Video Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new standalone `1920x1080` Remotion composition for the Afrikaans-first Suurlemoentjie mystery-and-reveal video inside the existing `email-flows-video` project.

**Architecture:** Keep the existing email-flow video intact and register a second composition in `Root.tsx`. Implement the Suurlemoentjie video in a dedicated component file that owns the timing, burst background, intro, mystery hold, reveal flash, and final answer lockup. Copy only the required Schink assets into Remotion `public/branding/...` and validate with the project’s own type/lint checks plus a render sanity pass.

**Tech Stack:** Remotion 4, React 19, TypeScript, project-local public assets, npm scripts

---

### Task 1: Add the required Remotion assets

**Files:**
- Create: `email-flows-video/public/branding/schink-stories-logo-white.png`
- Create: `email-flows-video/public/branding/characters/suurlemoentjie-mystery.png`
- Create: `email-flows-video/public/branding/characters/suurlemoentjie.png`

- [ ] **Step 1: Copy the approved source assets into the Remotion public tree**

Run:

```powershell
New-Item -ItemType Directory -Force -Path 'C:\Users\LVanderwalt\source\repos\Shink\email-flows-video\public\branding\characters' | Out-Null
Copy-Item 'C:\Users\LVanderwalt\source\repos\Shink\Shink\wwwroot\branding\schink-stories-logo-white.png' 'C:\Users\LVanderwalt\source\repos\Shink\email-flows-video\public\branding\schink-stories-logo-white.png' -Force
Copy-Item 'C:\Users\LVanderwalt\source\repos\Shink\Shink\wwwroot\branding\characters\suurlemoentjie-mystery.png' 'C:\Users\LVanderwalt\source\repos\Shink\email-flows-video\public\branding\characters\suurlemoentjie-mystery.png' -Force
Copy-Item 'C:\Users\LVanderwalt\source\repos\Shink\Shink\wwwroot\branding\characters\suurlemoentjie.png' 'C:\Users\LVanderwalt\source\repos\Shink\email-flows-video\public\branding\characters\suurlemoentjie.png' -Force
```

- [ ] **Step 2: Verify the copied files exist**

Run:

```powershell
Get-ChildItem 'C:\Users\LVanderwalt\source\repos\Shink\email-flows-video\public\branding' -Recurse | Select-Object FullName,Length
```

Expected: all three files appear under the Remotion `public` folder.

### Task 2: Create the Suurlemoentjie composition component

**Files:**
- Create: `email-flows-video/src/SuurlemoentjieReveal.tsx`

- [ ] **Step 1: Write the component file with the full video timeline**

Add a dedicated component that:

```tsx
export const SUURLEMOENTJIE_REVEAL_DURATION = 300;

export const SuurlemoentjieReveal = () => {
  return (
    <AbsoluteFill>
      <BurstBackground />
      <IntroLogo />
      <QuestionCard />
      <MysteryCharacter />
      <RevealFlash />
      <AnswerCharacter />
    </AbsoluteFill>
  );
};
```

The real implementation must:

- use `staticFile('branding/schink-stories-logo-white.png')`
- use `staticFile('branding/characters/suurlemoentjie-mystery.png')`
- use `staticFile('branding/characters/suurlemoentjie.png')`
- hold the mystery art for multiple seconds before reveal
- use only frame-driven animation

- [ ] **Step 2: Include helper sections inside the same file for the first pass**

Implement focused helpers such as:

```tsx
const BurstBackground = () => { /* frame-driven radial + stripe layers */ };
const IntroLogo = () => { /* intro-only logo scale/fade */ };
const QuestionCard = () => { /* "Wie is daardie karakter?" title hit */ };
const MysteryCharacter = () => { /* silhouette hold */ };
const RevealFlash = () => { /* brief bright overlay around reveal */ };
const AnswerCharacter = () => { /* full-colour Suurlemoentjie + lockup */ };
```

Keep the first pass in one file unless the file becomes clearly unwieldy.

### Task 3: Register the composition without breaking the existing one

**Files:**
- Modify: `email-flows-video/src/Root.tsx`

- [ ] **Step 1: Import the new component and duration constant**

Expected import shape:

```tsx
import { SuurlemoentjieReveal, SUURLEMOENTJIE_REVEAL_DURATION } from "./SuurlemoentjieReveal";
```

- [ ] **Step 2: Register a second composition**

Add:

```tsx
<Composition
  id="SuurlemoentjieReveal"
  component={SuurlemoentjieReveal}
  durationInFrames={SUURLEMOENTJIE_REVEAL_DURATION}
  fps={30}
  width={1920}
  height={1080}
/>
```

Expected result: the existing `EmailFlows` composition still exists, and the new Suurlemoentjie composition appears beside it.

### Task 4: Run project-local validation

**Files:**
- Validate: `email-flows-video/package.json`
- Validate: `email-flows-video/src/Root.tsx`
- Validate: `email-flows-video/src/SuurlemoentjieReveal.tsx`

- [ ] **Step 1: Run the project lint/type-check**

Run:

```powershell
npm run lint
```

Working directory:

```text
C:\Users\LVanderwalt\source\repos\Shink\email-flows-video
```

Expected: ESLint and TypeScript pass with no errors.

- [ ] **Step 2: If lint fails, make the minimal source fix and rerun**

Fix only the reported issue, then rerun:

```powershell
npm run lint
```

Expected: PASS.

### Task 5: Render sanity check

**Files:**
- Validate: `email-flows-video/out/`

- [ ] **Step 1: Render a still frame from the reveal composition**

Run:

```powershell
npx remotion still SuurlemoentjieReveal C:\Users\LVanderwalt\source\repos\Shink\email-flows-video\out\suurlemoentjie-frame-210.png --frame=210
```

Expected: a still image is written successfully near the reveal section.

- [ ] **Step 2: If the still looks wrong, adjust timing or layout and rerun the still render**

Use the same command:

```powershell
npx remotion still SuurlemoentjieReveal C:\Users\LVanderwalt\source\repos\Shink\email-flows-video\out\suurlemoentjie-frame-210.png --frame=210
```

Expected: the final frame is readable, centered, and uses the correct assets.

- [ ] **Step 3: Optionally render the full clip once the still is correct**

Run:

```powershell
npx remotion render SuurlemoentjieReveal C:\Users\LVanderwalt\source\repos\Shink\email-flows-video\out\suurlemoentjie-reveal.mp4
```

Expected: the full landscape video renders successfully with no audio track requirements.
