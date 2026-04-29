import type { CSSProperties, ReactNode } from "react";
import {
  AbsoluteFill,
  Easing,
  Img,
  Sequence,
  interpolate,
  spring,
  staticFile,
  useCurrentFrame,
} from "remotion";

const sceneDuration = 360;

const flows = [
  {
    title: "Kontak vorm",
    kicker: "When a parent sends a website message",
    steps: ["Website form", "Internal admin email", "Customer auto-reply template"],
    note: "The team gets the full message. The parent immediately receives a friendly acknowledgement.",
    accent: "#32c4c0",
    icon: "message",
  },
  {
    title: "Winkel bestelling",
    kicker: "When a teddy order is paid",
    steps: ["Paystack checkout", "Payment verified", "Admin paid-order email", "Customer confirmation template"],
    note: "The order email is only sent after payment verification, with order reference, items, total, and delivery details.",
    accent: "#f4b33d",
    icon: "cart",
  },
  {
    title: "Verlate betaling",
    kicker: "When checkout starts but is not completed",
    steps: ["Start recovery row", "1 hour reminder", "24 hour reminder", "7 day reminder"],
    note: "Each email has a continue link and an opt-out link. A completed payment cancels the remaining reminders.",
    accent: "#ff7a59",
    icon: "clock",
  },
  {
    title: "Intekening bevestig",
    kicker: "When a subscription payment succeeds",
    steps: ["Paystack or PayFast event", "Subscription ledger update", "Confirmation template", "Admin ops alert"],
    note: "The customer gets the plan, amount, provider reference, and next renewal date where available.",
    accent: "#81c784",
    icon: "check",
  },
  {
    title: "Mislukte hernuwing",
    kicker: "What happens before recovery emails go out",
    steps: ["Payment fails", "Wait 1 day", "Try Paystack saved authorization", "Then send Day 1, Day 3, Day 5 recovery emails"],
    note: "If the automatic retry succeeds, the recovery is resolved and the later emails are never sent.",
    accent: "#b388ff",
    icon: "retry",
  },
  {
    title: "Toegang verander",
    kicker: "When a subscription ends or recovery expires",
    steps: ["Status becomes ended or failed", "Customer ended-access template", "Admin ops alert"],
    note: "Paid access is stopped with clear wording, while free stories remain available.",
    accent: "#ef6c73",
    icon: "lock",
  },
  {
    title: "Rekening e-posse",
    kicker: "Authentication emails",
    steps: ["Password reset request", "Supabase Auth email", "Email-change flow"],
    note: "Account security emails come from the Supabase auth flow, separate from the Resend marketing and operations templates.",
    accent: "#64b5f6",
    icon: "key",
  },
  {
    title: "Beskermings",
    kicker: "How the site avoids email noise",
    steps: ["Published Resend templates", "Idempotency keys", "Stored scheduled email IDs", "Background worker every 10 minutes"],
    note: "The system remembers what was scheduled, cancels stale emails, and alerts admins when something needs attention.",
    accent: "#26a69a",
    icon: "shield",
  },
];

export const EmailFlows = () => {
  return (
    <AbsoluteFill style={styles.stage}>
      <Backdrop />
      <Sequence durationInFrames={sceneDuration}>
        <TitleScene />
      </Sequence>
      <Sequence from={sceneDuration} durationInFrames={sceneDuration}>
        <OverviewScene />
      </Sequence>
      {flows.map((flow, index) => (
        <Sequence
          key={flow.title}
          from={(index + 2) * sceneDuration}
          durationInFrames={sceneDuration}
        >
          <FlowScene flow={flow} index={index} />
        </Sequence>
      ))}
    </AbsoluteFill>
  );
};

const TitleScene = () => {
  const frame = useCurrentFrame();
  const logoY = interpolate(frame, [0, 50], [40, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const titleOpacity = interpolate(frame, [18, 58], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const characterY = spring({
    frame: frame - 55,
    fps: 30,
    config: { damping: 18, stiffness: 80, mass: 0.8 },
  });

  return (
    <SceneShell>
      <div style={styles.header}>
        <Img src={staticFile("branding/schink-logo-text.png")} style={styles.logo} />
        <div style={styles.headerLabel}>Website email flows</div>
      </div>
      <div style={styles.titleGrid}>
        <div>
          <div style={{ ...styles.kicker, opacity: titleOpacity }}>Schink Stories</div>
          <h1 style={{ ...styles.title, opacity: titleOpacity, transform: `translateY(${logoY}px)` }}>
            What emails send, when they send, and how they stop.
          </h1>
          <p style={{ ...styles.lead, opacity: titleOpacity }}>
            A practical walkthrough of contact replies, store orders, checkout reminders,
            subscription notices, and payment recovery.
          </p>
        </div>
        <div style={styles.heroArtWrap}>
          <Img
            src={staticFile("branding/Schink_Stories_01.png")}
            style={{
              ...styles.heroArt,
              transform: `translateY(${(1 - characterY) * 70}px) scale(${0.94 + characterY * 0.06})`,
              opacity: interpolate(frame, [45, 90], [0, 1], {
                extrapolateLeft: "clamp",
                extrapolateRight: "clamp",
              }),
            }}
          />
        </div>
      </div>
    </SceneShell>
  );
};

const OverviewScene = () => {
  const frame = useCurrentFrame();
  const nodes = [
    { label: "Website", sub: "Forms and checkout", x: 170, y: 340, color: "#32c4c0" },
    { label: "Supabase", sub: "Auth + recovery rows", x: 620, y: 210, color: "#3ecf8e" },
    { label: "Paystack", sub: "Payment events", x: 620, y: 500, color: "#f4b33d" },
    { label: "Resend", sub: "Published templates", x: 1090, y: 340, color: "#ef6c73" },
    { label: "Inbox", sub: "Customer + admin", x: 1510, y: 340, color: "#64b5f6" },
  ];

  return (
    <SceneShell>
      <TopBar label="System map" />
      <h2 style={styles.sceneTitle}>One website, a few clear email lanes.</h2>
      <p style={styles.sceneIntro}>
        The website records intent, payments confirm what really happened, and Resend
        sends the template emails that customers or admins should receive.
      </p>
      <div style={styles.mapArea}>
        <Connector from={[350, 430]} to={[620, 310]} frame={frame} delay={30} />
        <Connector from={[350, 430]} to={[620, 600]} frame={frame} delay={45} />
        <Connector from={[820, 310]} to={[1090, 430]} frame={frame} delay={70} />
        <Connector from={[820, 600]} to={[1090, 430]} frame={frame} delay={90} />
        <Connector from={[1290, 430]} to={[1510, 430]} frame={frame} delay={115} />
        {nodes.map((node, index) => (
          <MapNode key={node.label} node={node} delay={index * 18} />
        ))}
        <Img src={staticFile("branding/Schink_Stories_Char_Line_up.png")} style={styles.lineup} />
      </div>
    </SceneShell>
  );
};

type Flow = (typeof flows)[number];

const FlowScene = ({ flow, index }: { flow: Flow; index: number }) => {
  const frame = useCurrentFrame();
  const progress = Math.min(1, Math.max(0, frame / sceneDuration));
  const art = index % 3 === 0 ? "Panda.webp" : index % 3 === 1 ? "Rammetjie.png" : "Whatsapp_Eendjie.png";

  return (
    <SceneShell>
      <TopBar label={`Flow ${index + 1}`} />
      <div style={styles.flowGrid}>
        <div>
          <div style={{ ...styles.kicker, color: flow.accent }}>{flow.kicker}</div>
          <h2 style={styles.sceneTitle}>{flow.title}</h2>
          <p style={styles.sceneIntro}>{flow.note}</p>
          <div style={styles.flowMeta}>
            <MiniBadge label="Customer safe" />
            <MiniBadge label="Admin visible" />
            <MiniBadge label="Tracked" />
          </div>
        </div>
        <div style={styles.timelinePanel}>
          <div style={styles.timelineRail} />
          {flow.steps.map((step, stepIndex) => {
            const appear = spring({
              frame: frame - 34 - stepIndex * 28,
              fps: 30,
              config: { damping: 18, stiffness: 90, mass: 0.75 },
            });
            const y = 112 + stepIndex * 150;
            return (
              <div
                key={step}
                style={{
                  ...styles.timelineItem,
                  top: y,
                  opacity: appear,
                  transform: `translateX(${(1 - appear) * 80}px)`,
                }}
              >
                <div style={{ ...styles.timelineDot, backgroundColor: flow.accent }}>
                  {stepIndex + 1}
                </div>
                <div style={styles.timelineCard}>
                  <Icon type={flow.icon} color={flow.accent} />
                  <span>{step}</span>
                </div>
              </div>
            );
          })}
        </div>
      </div>
      <div
        style={{
          ...styles.progressBar,
          background: `linear-gradient(90deg, ${flow.accent} ${progress * 100}%, rgba(255,255,255,0.16) ${progress * 100}%)`,
        }}
      />
      <Img
        src={staticFile(`branding/${art}`)}
        style={{
          ...styles.mascot,
          transform: `translateY(${Math.sin(frame / 18) * 10}px) rotate(${Math.sin(frame / 40) * 2}deg)`,
        }}
      />
    </SceneShell>
  );
};

const SceneShell = ({ children }: { children: ReactNode }) => {
  const frame = useCurrentFrame();
  const opacity = interpolate(frame, [0, 22, sceneDuration - 28, sceneDuration], [0, 1, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  return <AbsoluteFill style={{ ...styles.scene, opacity }}>{children}</AbsoluteFill>;
};

const TopBar = ({ label }: { label: string }) => (
  <div style={styles.topBar}>
    <Img src={staticFile("branding/schink-logo-text.png")} style={styles.smallLogo} />
    <span>{label}</span>
  </div>
);

const Backdrop = () => {
  const frame = useCurrentFrame();
  return (
    <AbsoluteFill style={styles.backdrop}>
      <div
        style={{
          ...styles.backdropWash,
          transform: `translate3d(${Math.sin(frame / 95) * 18}px, ${Math.cos(frame / 120) * 16}px, 0)`,
        }}
      />
      <div style={styles.gridPattern} />
    </AbsoluteFill>
  );
};

const Connector = ({
  from,
  to,
  frame,
  delay,
}: {
  from: [number, number];
  to: [number, number];
  frame: number;
  delay: number;
}) => {
  const progress = interpolate(frame - delay, [0, 42], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const width = Math.hypot(to[0] - from[0], to[1] - from[1]);
  const angle = (Math.atan2(to[1] - from[1], to[0] - from[0]) * 180) / Math.PI;
  return (
    <div
      style={{
        ...styles.connector,
        left: from[0],
        top: from[1],
        width: width * progress,
        transform: `rotate(${angle}deg)`,
      }}
    />
  );
};

const MapNode = ({
  node,
  delay,
}: {
  node: { label: string; sub: string; x: number; y: number; color: string };
  delay: number;
}) => {
  const frame = useCurrentFrame();
  const scale = spring({
    frame: frame - delay,
    fps: 30,
    config: { damping: 16, stiffness: 90, mass: 0.8 },
  });
  return (
    <div
      style={{
        ...styles.mapNode,
        left: node.x,
        top: node.y,
        transform: `scale(${scale})`,
        borderColor: node.color,
      }}
    >
      <div style={{ ...styles.mapNodeIcon, backgroundColor: node.color }} />
      <strong>{node.label}</strong>
      <span>{node.sub}</span>
    </div>
  );
};

const MiniBadge = ({ label }: { label: string }) => <span style={styles.badge}>{label}</span>;

const Icon = ({ type, color }: { type: string; color: string }) => {
  const common: CSSProperties = {
    width: 56,
    height: 56,
    borderRadius: 16,
    backgroundColor: `${color}22`,
    color,
    display: "grid",
    placeItems: "center",
    fontSize: 28,
    fontWeight: 900,
    flexShrink: 0,
  };
  const labels: Record<string, string> = {
    message: "@",
    cart: "R",
    clock: "1h",
    check: "OK",
    retry: "R2",
    lock: "!",
    key: "#",
    shield: "OK",
  };
  return <div style={common}>{labels[type] ?? ">"}</div>;
};

const styles: Record<string, CSSProperties> = {
  stage: {
    backgroundColor: "#0a1113",
    color: "#f7fbfb",
    fontFamily: "Arial, Helvetica, sans-serif",
    overflow: "hidden",
  },
  backdrop: {
    background:
      "radial-gradient(circle at 18% 12%, rgba(50,196,192,0.35), transparent 34%), radial-gradient(circle at 85% 18%, rgba(244,179,61,0.26), transparent 31%), linear-gradient(145deg, #071011 0%, #112528 54%, #18201f 100%)",
  },
  backdropWash: {
    position: "absolute",
    inset: 80,
    borderRadius: 80,
    background:
      "linear-gradient(120deg, rgba(255,255,255,0.08), rgba(255,255,255,0.02) 42%, rgba(50,196,192,0.12))",
    filter: "blur(1px)",
  },
  gridPattern: {
    position: "absolute",
    inset: 0,
    backgroundImage:
      "linear-gradient(rgba(255,255,255,0.055) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.055) 1px, transparent 1px)",
    backgroundSize: "80px 80px",
    opacity: 0.24,
  },
  scene: {
    padding: "76px 96px",
  },
  header: {
    display: "flex",
    alignItems: "center",
    gap: 32,
  },
  logo: {
    width: 260,
    height: "auto",
    background: "rgba(0,0,0,0.24)",
    border: "1px solid rgba(255,255,255,0.16)",
    borderRadius: 18,
    padding: "18px 24px",
  },
  smallLogo: {
    width: 170,
    height: "auto",
    background: "rgba(0,0,0,0.24)",
    border: "1px solid rgba(255,255,255,0.16)",
    borderRadius: 14,
    padding: "12px 16px",
  },
  headerLabel: {
    fontSize: 30,
    color: "rgba(255,255,255,0.72)",
    letterSpacing: 0,
  },
  titleGrid: {
    display: "grid",
    gridTemplateColumns: "1.02fr 0.98fr",
    alignItems: "center",
    gap: 56,
    height: "calc(100% - 120px)",
  },
  kicker: {
    fontSize: 34,
    fontWeight: 800,
    color: "#32c4c0",
    marginBottom: 26,
  },
  title: {
    fontSize: 104,
    lineHeight: 1,
    margin: 0,
    maxWidth: 920,
    letterSpacing: 0,
  },
  lead: {
    fontSize: 36,
    lineHeight: 1.34,
    color: "rgba(247,251,251,0.78)",
    maxWidth: 820,
    marginTop: 34,
  },
  heroArtWrap: {
    height: 760,
    display: "grid",
    placeItems: "center",
  },
  heroArt: {
    width: 740,
    maxHeight: 740,
    objectFit: "contain",
    filter: "drop-shadow(0 36px 50px rgba(0,0,0,0.35))",
  },
  topBar: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    fontSize: 26,
    color: "rgba(255,255,255,0.72)",
    marginBottom: 48,
  },
  sceneTitle: {
    fontSize: 72,
    lineHeight: 1.04,
    margin: 0,
    maxWidth: 980,
    letterSpacing: 0,
  },
  sceneIntro: {
    fontSize: 31,
    lineHeight: 1.38,
    color: "rgba(247,251,251,0.78)",
    maxWidth: 1000,
    marginTop: 24,
  },
  mapArea: {
    position: "relative",
    height: 690,
    marginTop: 24,
  },
  connector: {
    position: "absolute",
    height: 10,
    borderRadius: 999,
    background: "linear-gradient(90deg, rgba(255,255,255,0.88), rgba(50,196,192,0.8))",
    transformOrigin: "0 50%",
    boxShadow: "0 0 26px rgba(50,196,192,0.28)",
  },
  mapNode: {
    position: "absolute",
    width: 250,
    height: 180,
    borderRadius: 28,
    border: "4px solid",
    background: "rgba(8,17,19,0.84)",
    boxShadow: "0 28px 70px rgba(0,0,0,0.28)",
    display: "flex",
    flexDirection: "column",
    justifyContent: "center",
    gap: 12,
    padding: 28,
  },
  mapNodeIcon: {
    width: 44,
    height: 44,
    borderRadius: 14,
  },
  lineup: {
    position: "absolute",
    right: 40,
    bottom: -24,
    width: 380,
    opacity: 0.94,
    filter: "drop-shadow(0 28px 38px rgba(0,0,0,0.36))",
  },
  flowGrid: {
    display: "grid",
    gridTemplateColumns: "0.82fr 1.18fr",
    gap: 66,
    alignItems: "center",
    height: "calc(100% - 118px)",
  },
  flowMeta: {
    display: "flex",
    gap: 16,
    marginTop: 36,
    flexWrap: "wrap",
  },
  badge: {
    fontSize: 23,
    color: "#eaf3f2",
    background: "rgba(255,255,255,0.12)",
    border: "1px solid rgba(255,255,255,0.14)",
    borderRadius: 999,
    padding: "12px 18px",
  },
  timelinePanel: {
    position: "relative",
    height: 760,
    borderRadius: 34,
    background: "rgba(5,12,14,0.62)",
    border: "1px solid rgba(255,255,255,0.14)",
    boxShadow: "0 28px 90px rgba(0,0,0,0.34)",
    overflow: "hidden",
  },
  timelineRail: {
    position: "absolute",
    left: 88,
    top: 96,
    bottom: 96,
    width: 8,
    borderRadius: 999,
    background: "rgba(255,255,255,0.18)",
  },
  timelineItem: {
    position: "absolute",
    left: 56,
    right: 54,
    display: "flex",
    alignItems: "center",
    gap: 28,
  },
  timelineDot: {
    width: 72,
    height: 72,
    borderRadius: 22,
    display: "grid",
    placeItems: "center",
    fontSize: 30,
    fontWeight: 900,
    color: "#071011",
    boxShadow: "0 16px 34px rgba(0,0,0,0.32)",
    flexShrink: 0,
  },
  timelineCard: {
    minHeight: 94,
    flex: 1,
    borderRadius: 24,
    background: "rgba(255,255,255,0.1)",
    border: "1px solid rgba(255,255,255,0.14)",
    padding: "20px 26px",
    display: "flex",
    alignItems: "center",
    gap: 22,
    fontSize: 28,
    fontWeight: 800,
  },
  progressBar: {
    position: "absolute",
    left: 96,
    right: 96,
    bottom: 54,
    height: 12,
    borderRadius: 999,
  },
  mascot: {
    position: "absolute",
    left: 108,
    bottom: 82,
    width: 170,
    height: 170,
    objectFit: "contain",
    filter: "drop-shadow(0 20px 28px rgba(0,0,0,0.32))",
  },
};
