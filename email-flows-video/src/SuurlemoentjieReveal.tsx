import type { CSSProperties } from "react";
import {
  AbsoluteFill,
  Easing,
  Img,
  interpolate,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";

export const SUURLEMOENTJIE_REVEAL_DURATION = 354;

const introEndFrame = 108;
const questionStartFrame = 88;
const mysteryStartFrame = 132;
const revealStartFrame = 234;
const answerStartFrame = 246;

export const SuurlemoentjieReveal = () => {
  return (
    <AbsoluteFill style={styles.stage}>
      <BurstBackground />
      <IntroLogo />
      <QuestionCard />
      <MysteryCharacter />
      <RevealFlash />
      <AnswerCharacter />
      <CornerBadge />
    </AbsoluteFill>
  );
};

const BurstBackground = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const burstRotate = interpolate(frame, [0, SUURLEMOENTJIE_REVEAL_DURATION], [-8, 10], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const burstScale = interpolate(frame, [0, 2 * fps, 6 * fps], [0.94, 1.02, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.bezier(0.16, 1, 0.3, 1),
  });
  const glowOpacity = interpolate(frame, [0, mysteryStartFrame, answerStartFrame, SUURLEMOENTJIE_REVEAL_DURATION], [0.45, 0.66, 0.85, 0.92], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  return (
    <AbsoluteFill style={styles.backgroundShell}>
      <div
        style={{
          ...styles.outerGlow,
          opacity: glowOpacity,
          transform: `scale(${burstScale})`,
        }}
      />
      <div
        style={{
          ...styles.sunburst,
          transform: `scale(${burstScale}) rotate(${burstRotate}deg)`,
        }}
      />
      <div style={styles.centerDisc} />
      <div
        style={{
          ...styles.answerHalo,
          opacity: interpolate(frame, [revealStartFrame - 6, revealStartFrame + 12, answerStartFrame + 18], [0, 0.72, 0.38], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
          }),
        }}
      />
    </AbsoluteFill>
  );
};

const IntroLogo = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const rise = spring({
    frame,
    fps,
    config: { damping: 18, stiffness: 110, mass: 0.9 },
    durationInFrames: 28,
  });
  const opacity = interpolate(frame, [0, 8, 38, introEndFrame], [0, 1, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  return (
    <div
      style={{
        ...styles.logoWrap,
        opacity,
        transform: `translateY(${(1 - rise) * 46}px) scale(${0.9 + rise * 0.1})`,
      }}
    >
      <Img src={staticFile("branding/schink-stories-logo-white.png")} style={styles.logo} />
      <div style={styles.logoSubline}>Raai saam met Schink Karakters</div>
    </div>
  );
};

const QuestionCard = () => {
  const frame = useCurrentFrame();
  const titleIn = spring({
    frame: frame - questionStartFrame,
    fps: 30,
    config: { damping: 18, stiffness: 120, mass: 0.85 },
    durationInFrames: 28,
  });
  const opacity = interpolate(frame, [questionStartFrame, questionStartFrame + 8, revealStartFrame - 8, revealStartFrame + 8], [0, 1, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  return (
    <div
      style={{
        ...styles.questionCard,
        opacity,
        transform: `translateY(${(1 - titleIn) * 60}px) scale(${0.9 + titleIn * 0.1})`,
      }}
    >
      <div style={styles.questionEyebrow}>Wie is daardie</div>
      <div style={styles.questionTitle}>KARAKTER?</div>
      <div style={styles.questionHint}>Kyk mooi voor die onthulling.</div>
    </div>
  );
};

const MysteryCharacter = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const appear = spring({
    frame: frame - mysteryStartFrame,
    fps,
    config: { damping: 16, stiffness: 80, mass: 0.9 },
    durationInFrames: 24,
  });
  const opacity = interpolate(frame, [mysteryStartFrame, mysteryStartFrame + 6, revealStartFrame, answerStartFrame - 4], [0, 1, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const hover = Math.sin(frame / 11) * 12;

  return (
    <div
      style={{
        ...styles.characterZone,
        opacity,
        transform: `translate(-50%, -50%) translateY(${(1 - appear) * 90 + hover}px) scale(${0.88 + appear * 0.12})`,
      }}
    >
      <div style={styles.shadow} />
      <Img
        src={staticFile("branding/characters/suurlemoentjie-mystery.png")}
        style={styles.characterImage}
      />
    </div>
  );
};

const RevealFlash = () => {
  const frame = useCurrentFrame();
  const flashOpacity = interpolate(frame, [revealStartFrame - 2, revealStartFrame + 4, revealStartFrame + 12], [0, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const burstScale = interpolate(frame, [revealStartFrame - 2, revealStartFrame + 8], [0.5, 1.45], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });

  return (
    <>
      <AbsoluteFill
        style={{
          ...styles.flashOverlay,
          opacity: flashOpacity,
        }}
      />
      <div
        style={{
          ...styles.flashRing,
          opacity: flashOpacity * 0.8,
          transform: `translate(-50%, -50%) scale(${burstScale})`,
        }}
      />
    </>
  );
};

const AnswerCharacter = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const answerIn = spring({
    frame: frame - answerStartFrame,
    fps,
    config: { damping: 17, stiffness: 95, mass: 0.85 },
    durationInFrames: 34,
  });
  const opacity = interpolate(frame, [answerStartFrame - 2, answerStartFrame + 8, SUURLEMOENTJIE_REVEAL_DURATION], [0, 1, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const hover = Math.sin(frame / 13) * 7;

  return (
    <>
      <div
        style={{
          ...styles.answerCharacterZone,
          opacity,
          transform: `translate(-50%, -50%) translateY(${(1 - answerIn) * 84 + hover}px) scale(${0.9 + answerIn * 0.1})`,
        }}
      >
        <div style={{ ...styles.shadow, width: 390, opacity: 0.28, bottom: 54 }} />
        <Img
          src={staticFile("branding/characters/suurlemoentjie.png")}
          style={styles.answerCharacterImage}
        />
      </div>
      <div
        style={{
          ...styles.answerCard,
          opacity,
          transform: `translateY(${(1 - answerIn) * 70}px) scale(${0.92 + answerIn * 0.08})`,
        }}
      >
        <div style={styles.answerEyebrow}>Dis</div>
        <div style={styles.answerTitle}>SUURLEMOENTJIE!</div>
      </div>
    </>
  );
};

const CornerBadge = () => {
  const frame = useCurrentFrame();
  const opacity = interpolate(frame, [0, 18, SUURLEMOENTJIE_REVEAL_DURATION - 20, SUURLEMOENTJIE_REVEAL_DURATION], [0, 0.82, 0.82, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  return (
    <div style={{ ...styles.cornerBadge, opacity }}>
      Gratis karakter
    </div>
  );
};

const styles: Record<string, CSSProperties> = {
  stage: {
    background: "linear-gradient(180deg, #0e7378 0%, #0c4048 54%, #092730 100%)",
    color: "#ffffff",
    fontFamily: '"Trebuchet MS", "Segoe UI", sans-serif',
    overflow: "hidden",
  },
  backgroundShell: {
    overflow: "hidden",
  },
  outerGlow: {
    position: "absolute",
    inset: -140,
    background:
      "radial-gradient(circle at center, rgba(255, 245, 165, 0.92) 0%, rgba(255, 211, 78, 0.58) 16%, rgba(255, 211, 78, 0.08) 44%, rgba(255, 211, 78, 0) 68%)",
  },
  sunburst: {
    position: "absolute",
    inset: -160,
    background:
      "repeating-conic-gradient(from -12deg, rgba(255, 210, 76, 0.95) 0deg 9deg, rgba(16, 170, 166, 0.98) 9deg 18deg, rgba(10, 96, 104, 0.98) 18deg 27deg, rgba(245, 170, 31, 0.96) 27deg 36deg)",
    opacity: 0.9,
  },
  centerDisc: {
    position: "absolute",
    inset: 190,
    borderRadius: "50%",
    background:
      "radial-gradient(circle at center, rgba(255, 248, 196, 0.98) 0%, rgba(255, 225, 113, 0.96) 20%, rgba(255, 200, 59, 0.78) 40%, rgba(255, 200, 59, 0.18) 70%, rgba(255, 200, 59, 0) 100%)",
  },
  answerHalo: {
    position: "absolute",
    left: "50%",
    top: "52%",
    width: 980,
    height: 980,
    transform: "translate(-50%, -50%)",
    borderRadius: "50%",
    background:
      "radial-gradient(circle at center, rgba(255,255,255,0.9) 0%, rgba(255,243,174,0.65) 12%, rgba(255,243,174,0.2) 34%, rgba(255,243,174,0) 70%)",
  },
  logoWrap: {
    position: "absolute",
    inset: 0,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: 28,
  },
  logo: {
    width: 560,
    height: "auto",
    filter: "drop-shadow(0 22px 30px rgba(0,0,0,0.28))",
  },
  logoSubline: {
    padding: "14px 24px",
    borderRadius: 999,
    background: "rgba(8, 39, 48, 0.46)",
    border: "2px solid rgba(255, 255, 255, 0.22)",
    fontSize: 24,
    fontWeight: 800,
    letterSpacing: 1.2,
    textTransform: "uppercase",
  },
  questionCard: {
    position: "absolute",
    left: "50%",
    top: 120,
    transform: "translateX(-50%)",
    width: 1160,
    padding: "28px 36px 34px",
    borderRadius: 42,
    background: "rgba(8, 34, 42, 0.88)",
    border: "8px solid #ffe17a",
    boxShadow: "0 26px 56px rgba(0,0,0,0.28)",
    textAlign: "center",
  },
  questionEyebrow: {
    fontSize: 32,
    lineHeight: 1,
    color: "#a7ffef",
    fontWeight: 900,
    letterSpacing: 5,
    textTransform: "uppercase",
    marginBottom: 14,
  },
  questionTitle: {
    fontFamily: 'Impact, Haettenschweiler, "Arial Narrow Bold", sans-serif',
    fontSize: 100,
    lineHeight: 0.95,
    letterSpacing: 2,
    color: "#ffffff",
    textTransform: "uppercase",
    textShadow: "0 8px 0 rgba(0,0,0,0.18)",
  },
  questionHint: {
    marginTop: 18,
    fontSize: 26,
    color: "rgba(255,255,255,0.82)",
    fontWeight: 700,
  },
  characterZone: {
    position: "absolute",
    left: "50%",
    top: "52%",
    width: 760,
    height: 760,
    transform: "translate(-50%, -50%)",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
  },
  characterImage: {
    width: 620,
    height: 620,
    objectFit: "contain",
    filter: "drop-shadow(0 34px 44px rgba(0,0,0,0.34))",
  },
  shadow: {
    position: "absolute",
    bottom: 84,
    width: 360,
    height: 72,
    borderRadius: "50%",
    background: "rgba(7, 29, 36, 0.35)",
    filter: "blur(10px)",
  },
  flashOverlay: {
    background:
      "radial-gradient(circle at center, rgba(255,255,255,0.94) 0%, rgba(255,250,208,0.84) 26%, rgba(255,250,208,0) 64%)",
    mixBlendMode: "screen",
    pointerEvents: "none",
  },
  flashRing: {
    position: "absolute",
    left: "50%",
    top: "52%",
    width: 540,
    height: 540,
    borderRadius: "50%",
    border: "34px solid rgba(255, 255, 255, 0.86)",
    boxShadow: "0 0 100px rgba(255, 244, 180, 0.78)",
  },
  answerCard: {
    position: "absolute",
    left: "50%",
    bottom: 42,
    transform: "translateX(-50%)",
    minWidth: 960,
    maxWidth: 1220,
    padding: "22px 34px 26px",
    borderRadius: 36,
    background: "rgba(7, 31, 38, 0.9)",
    border: "7px solid #ffe17a",
    boxShadow: "0 20px 42px rgba(0,0,0,0.28)",
    textAlign: "center",
  },
  answerEyebrow: {
    fontSize: 30,
    lineHeight: 1,
    color: "#a7ffef",
    fontWeight: 900,
    letterSpacing: 4,
    textTransform: "uppercase",
    marginBottom: 10,
  },
  answerTitle: {
    fontFamily: 'Impact, Haettenschweiler, "Arial Narrow Bold", sans-serif',
    fontSize: 78,
    lineHeight: 0.98,
    color: "#fffef2",
    letterSpacing: 2,
    textTransform: "uppercase",
    textShadow: "0 8px 0 rgba(0,0,0,0.18)",
  },
  answerCharacterZone: {
    position: "absolute",
    left: "50%",
    top: "48%",
    width: 700,
    height: 700,
    transform: "translate(-50%, -50%)",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
  },
  answerCharacterImage: {
    width: 500,
    height: 500,
    objectFit: "contain",
    filter: "drop-shadow(0 34px 44px rgba(0,0,0,0.34))",
  },
  cornerBadge: {
    position: "absolute",
    left: 34,
    top: 30,
    padding: "16px 24px",
    borderRadius: 999,
    background: "rgba(7, 31, 38, 0.42)",
    border: "2px solid rgba(255, 255, 255, 0.22)",
    color: "#fff8d0",
    fontSize: 24,
    fontWeight: 900,
    letterSpacing: 1.2,
    textTransform: "uppercase",
  },
};
