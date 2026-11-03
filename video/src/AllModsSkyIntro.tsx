import React from "react";
import {
  AbsoluteFill,
  Easing,
  Img,
  interpolate,
  staticFile,
  useCurrentFrame,
} from "remotion";
import {TransitionSeries, linearTiming} from "@remotion/transitions";
import {fade} from "@remotion/transitions/fade";
import {slide} from "@remotion/transitions/slide";

const colors = {
  ink: "#050B12",
  ink2: "#07111C",
  panel: "#0D1927",
  line: "rgba(133, 210, 255, 0.18)",
  text: "#EEF8FF",
  muted: "#9BB4C8",
  cyan: "#53D9FF",
  blue: "#5B8CFF",
  violet: "#9A7BFF",
  green: "#4BE1AA",
};

const fontFamily =
  '"Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Segoe UI", sans-serif';

const transitionFrames = 15;
const sceneDurations = {
  intro: 180,
  library: 270,
  nexus: 270,
  tools: 270,
  proof: 150,
  outro: 150,
};

const clamp = {
  extrapolateLeft: "clamp" as const,
  extrapolateRight: "clamp" as const,
};

const luxeEase = Easing.bezier(0.16, 1, 0.3, 1);

const stars = Array.from({length: 92}, (_, index) => ({
  x: (index * 47 + 13) % 100,
  y: (index * 83 + 7) % 100,
  size: 1 + ((index * 17) % 5) * 0.48,
  alpha: 0.14 + ((index * 29) % 56) / 100,
}));

const SpaceBackground: React.FC<{
  readonly accent?: "cyan" | "violet";
  readonly grid?: boolean;
}> = ({accent = "cyan", grid = true}) => {
  const frame = useCurrentFrame();
  const accentColor = accent === "cyan" ? colors.cyan : colors.violet;

  return (
    <AbsoluteFill style={{backgroundColor: colors.ink, overflow: "hidden"}}>
      <AbsoluteFill
        style={{
          background: `radial-gradient(circle at 78% 12%, ${
            accent === "cyan" ? "rgba(51, 177, 255, 0.21)" : "rgba(151, 107, 255, 0.2)"
          } 0%, transparent 32%), radial-gradient(circle at 18% 82%, rgba(42, 90, 184, 0.18) 0%, transparent 34%), linear-gradient(145deg, #07101A 0%, #04080E 55%, #07121E 100%)`,
        }}
      />
      <AbsoluteFill
        style={{
          opacity: grid ? 0.26 : 0,
          backgroundImage:
            "linear-gradient(rgba(88, 199, 255, .18) 1px, transparent 1px), linear-gradient(90deg, rgba(88, 199, 255, .18) 1px, transparent 1px)",
          backgroundSize: "90px 90px",
          backgroundPosition: `0px ${frame * 0.12}px`,
          maskImage: "linear-gradient(to bottom, transparent 8%, black 62%, transparent 100%)",
        }}
      />
      {stars.map((star, index) => (
        <div
          key={index}
          style={{
            position: "absolute",
            left: `${star.x}%`,
            top: `${star.y}%`,
            width: star.size,
            height: star.size,
            borderRadius: 999,
            background: index % 7 === 0 ? accentColor : "#DDF7FF",
            opacity: star.alpha,
            boxShadow: index % 9 === 0 ? `0 0 12px ${accentColor}` : undefined,
            translate: `0 ${Math.sin((frame + index * 13) / 34) * 4}px`,
          }}
        />
      ))}
      <div
        style={{
          position: "absolute",
          width: 900,
          height: 900,
          right: -410,
          top: -470,
          borderRadius: "50%",
          border: `1px solid ${accentColor}33`,
          boxShadow: `0 0 120px ${accentColor}15 inset`,
          rotate: `${frame * 0.03}deg`,
        }}
      />
      <div
        style={{
          position: "absolute",
          width: 620,
          height: 620,
          right: -280,
          top: -330,
          borderRadius: "50%",
          border: `1px dashed ${accentColor}44`,
          rotate: `${-frame * 0.02}deg`,
        }}
      />
    </AbsoluteFill>
  );
};

const AnimatedText: React.FC<{
  readonly children: React.ReactNode;
  readonly delay?: number;
  readonly duration?: number;
  readonly style?: React.CSSProperties;
}> = ({children, delay = 0, duration = 24, style}) => {
  const frame = useCurrentFrame();
  const progress = interpolate(frame, [delay, delay + duration], [0, 1], {
    ...clamp,
    easing: luxeEase,
  });

  return (
    <div
      style={{
        ...style,
        opacity: progress,
        translate: `0 ${(1 - progress) * 34}px`,
        filter: `blur(${(1 - progress) * 7}px)`,
      }}
    >
      {children}
    </div>
  );
};

const Eyebrow: React.FC<{
  readonly index: string;
  readonly children: React.ReactNode;
  readonly color?: string;
  readonly delay?: number;
}> = ({index, children, color = colors.cyan, delay = 0}) => (
  <AnimatedText
    delay={delay}
    style={{
      display: "flex",
      alignItems: "center",
      gap: 16,
      color,
      fontSize: 26,
      fontWeight: 700,
      letterSpacing: 4,
    }}
  >
    <span
      style={{
        width: 48,
        height: 2,
        background: color,
        boxShadow: `0 0 16px ${color}`,
      }}
    />
    <span>{index}</span>
    <span style={{color: colors.muted, fontWeight: 500, letterSpacing: 2}}>{children}</span>
  </AnimatedText>
);

const HoloOrb: React.FC<{readonly size?: number}> = ({size = 190}) => {
  const frame = useCurrentFrame();
  const angle = frame * 1.7;
  const halo = interpolate(Math.sin(frame / 28), [-1, 1], [0.42, 0.78]);

  return (
    <div
      style={{
        width: size,
        height: size,
        position: "relative",
        display: "grid",
        placeItems: "center",
      }}
    >
      <div
        style={{
          position: "absolute",
          width: size * 0.92,
          height: size * 0.92,
          borderRadius: "50%",
          background: `radial-gradient(circle, rgba(83,217,255,${halo}) 0%, rgba(72,124,255,.25) 36%, transparent 68%)`,
          filter: "blur(12px)",
        }}
      />
      <div
        style={{
          position: "absolute",
          width: size * 0.67,
          height: size * 0.67,
          borderRadius: "50%",
          background:
            "radial-gradient(circle at 34% 26%, #E7FCFF 0%, #63D9FF 18%, #3784DA 47%, #253B97 72%, #101B43 100%)",
          boxShadow: "0 0 48px rgba(83,217,255,.5), inset -18px -16px 34px rgba(7,16,36,.72)",
        }}
      />
      <div
        style={{
          position: "absolute",
          width: size * 0.83,
          height: size * 0.83,
          borderRadius: "50%",
          borderTop: `2px solid ${colors.violet}`,
          borderRight: "2px solid transparent",
          borderBottom: `2px solid ${colors.cyan}`,
          borderLeft: "2px solid transparent",
          rotate: `${angle}deg`,
          scale: "1 0.4",
          opacity: 0.85,
        }}
      />
      <div
        style={{
          position: "absolute",
          width: size,
          height: size * 0.45,
          borderRadius: "50%",
          border: "2px dashed rgba(126,220,255,.48)",
          rotate: "-16deg",
        }}
      />
      <div
        style={{
          position: "absolute",
          width: size * 0.18,
          height: size * 0.18,
          borderRadius: "50%",
          background: "#E7FDFF",
          boxShadow: "0 0 24px 8px rgba(99,225,255,.8)",
          left: size / 2 + Math.cos((angle * Math.PI) / 180) * size * 0.38,
          top: size / 2 + Math.sin((angle * Math.PI) / 180) * size * 0.17,
          translate: "-50% -50%",
        }}
      />
    </div>
  );
};

const Pill: React.FC<{
  readonly children: React.ReactNode;
  readonly color?: string;
  readonly delay?: number;
}> = ({children, color = colors.cyan, delay = 0}) => {
  const frame = useCurrentFrame();
  const progress = interpolate(frame, [delay, delay + 18], [0, 1], {
    ...clamp,
    easing: luxeEase,
  });

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 10,
        padding: "13px 20px",
        borderRadius: 999,
        border: `1px solid ${color}50`,
        background: `${color}12`,
        color: colors.text,
        fontSize: 25,
        fontWeight: 600,
        opacity: progress,
        translate: `0 ${(1 - progress) * 18}px`,
      }}
    >
      <span
        style={{
          width: 8,
          height: 8,
          borderRadius: 99,
          background: color,
          boxShadow: `0 0 12px ${color}`,
        }}
      />
      {children}
    </div>
  );
};

const IntroScene: React.FC = () => {
  const frame = useCurrentFrame();
  const orbScale = interpolate(frame, [0, 34], [0.66, 1], {
    ...clamp,
    easing: luxeEase,
    output: "perceptual-scale",
  });

  return (
    <AbsoluteFill style={{fontFamily}}>
      <SpaceBackground grid={false} />
      <div
        style={{
          position: "absolute",
          top: 70,
          left: 140,
          right: 140,
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          color: colors.muted,
          fontSize: 22,
          letterSpacing: 3,
        }}
      >
        <span>ALL MOD'S SKY</span>
        <span
          style={{
            border: `1px solid ${colors.cyan}66`,
            background: `${colors.cyan}12`,
            color: colors.cyan,
            padding: "9px 16px",
            borderRadius: 999,
          }}
        >
          v2.0.5
        </span>
      </div>
      <div
        style={{
          position: "absolute",
          inset: "142px 140px 96px",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          textAlign: "center",
        }}
      >
        <div style={{scale: orbScale, marginBottom: 34}}>
          <HoloOrb size={188} />
        </div>
        <AnimatedText
          delay={10}
          duration={30}
          style={{
            fontSize: 142,
            lineHeight: 1.02,
            letterSpacing: -5,
            fontWeight: 850,
            color: colors.text,
            textShadow: "0 14px 60px rgba(55,173,255,.25)",
          }}
        >
          All Mod's Sky
        </AnimatedText>
        <AnimatedText
          delay={20}
          style={{
            marginTop: 22,
            fontSize: 48,
            fontWeight: 500,
            color: colors.muted,
            letterSpacing: 3,
          }}
        >
          《无人深空》模组管理器
        </AnimatedText>
        <div style={{marginTop: 38, display: "flex", gap: 12}}>
          <Pill delay={36}>启停</Pill>
          <Pill delay={42} color={colors.violet}>
            排序
          </Pill>
          <Pill delay={48} color={colors.green}>
            冲突检测
          </Pill>
        </div>
      </div>
    </AbsoluteFill>
  );
};

const ScreenshotFrame: React.FC<{
  readonly src: string;
  readonly left: number;
  readonly top: number;
  readonly width: number;
  readonly height: number;
  readonly objectPosition?: string;
  readonly zoom?: number;
}> = ({src, left, top, width, height, objectPosition = "center top", zoom = 0.025}) => {
  const frame = useCurrentFrame();
  const enter = interpolate(frame, [0, 28], [0.965, 1], {
    ...clamp,
    easing: luxeEase,
    output: "perceptual-scale",
  });
  const drift = interpolate(frame, [0, sceneDurations.library], [1, 1 + zoom], clamp);

  return (
    <div
      style={{
        position: "absolute",
        left,
        top,
        width,
        height,
        padding: 10,
        borderRadius: 28,
        background: "linear-gradient(145deg, rgba(83,217,255,.32), rgba(137,120,255,.12))",
        boxShadow:
          "0 42px 100px rgba(0,0,0,.62), 0 0 0 1px rgba(190,238,255,.15), 0 0 60px rgba(63,167,255,.15)",
        scale: enter,
      }}
    >
      <div
        style={{
          width: "100%",
          height: "100%",
          borderRadius: 20,
          overflow: "hidden",
          background: colors.ink2,
          position: "relative",
        }}
      >
        <Img
          src={src}
          style={{
            width: "100%",
            height: "100%",
            objectFit: "cover",
            objectPosition,
            scale: drift,
          }}
        />
        <div
          style={{
            position: "absolute",
            inset: 0,
            background:
              "linear-gradient(120deg, rgba(255,255,255,.06), transparent 20%, transparent 78%, rgba(73,199,255,.06))",
          }}
        />
      </div>
    </div>
  );
};

const LibraryScene: React.FC = () => (
  <AbsoluteFill style={{fontFamily}}>
    <SpaceBackground />
    <div style={{position: "absolute", left: 140, top: 176, width: 510}}>
      <Eyebrow index="01">MOD LIBRARY</Eyebrow>
      <AnimatedText
        delay={8}
        style={{
          marginTop: 24,
          fontSize: 86,
          lineHeight: 1.14,
          letterSpacing: -2,
          fontWeight: 820,
          color: colors.text,
        }}
      >
        所有模组
        <br />
        <span style={{color: colors.cyan}}>一处掌握</span>
      </AnimatedText>
      <AnimatedText
        delay={18}
        style={{
          marginTop: 28,
          fontSize: 34,
          lineHeight: 1.58,
          color: colors.muted,
        }}
      >
        不复制第二份库。
        <br />
        直接管理游戏真实的 MODS 目录。
      </AnimatedText>
    </div>
    <ScreenshotFrame
      src={staticFile("library.png")}
      left={690}
      top={164}
      width={1090}
      height={662}
      objectPosition="left top"
      zoom={0.035}
    />
    <div style={{position: "absolute", left: 690, bottom: 154, display: "flex", gap: 12}}>
      <Pill delay={20}>一键启停</Pill>
      <Pill delay={26} color={colors.violet}>
        拖动排序
      </Pill>
      <Pill delay={32} color={colors.green}>
        同步到游戏
      </Pill>
    </div>
    <AnimatedText
      delay={35}
      style={{
        position: "absolute",
        left: 141,
        bottom: 92,
        color: colors.muted,
        fontSize: 24,
        letterSpacing: 2,
      }}
    >
      REAL APPLICATION · REAL LIBRARY
    </AnimatedText>
  </AbsoluteFill>
);

const NexusScene: React.FC = () => (
  <AbsoluteFill style={{fontFamily}}>
    <SpaceBackground accent="violet" grid={false} />
    <div style={{position: "absolute", left: 140, top: 110}}>
      <Eyebrow index="02" color={colors.violet}>
        DISCOVER
      </Eyebrow>
      <AnimatedText
        delay={8}
        style={{
          marginTop: 18,
          fontSize: 80,
          lineHeight: 1.12,
          letterSpacing: -2,
          fontWeight: 820,
          color: colors.text,
        }}
      >
        发现模组，不必来回切换
      </AnimatedText>
    </div>
    <AnimatedText
      delay={18}
      style={{
        position: "absolute",
        right: 140,
        top: 150,
        color: colors.green,
        fontSize: 24,
        fontWeight: 700,
        letterSpacing: 2,
        border: `1px solid ${colors.green}55`,
        background: `${colors.green}12`,
        borderRadius: 999,
        padding: "10px 18px",
      }}
    >
      ● NEXUS API 已连接
    </AnimatedText>
    <ScreenshotFrame
      src={staticFile("nexus.png")}
      left={140}
      top={260}
      width={1640}
      height={670}
      objectPosition="left top"
      zoom={0.045}
    />
    <AnimatedText
      delay={26}
      style={{
        position: "absolute",
        left: 140,
        bottom: 72,
        display: "flex",
        gap: 12,
      }}
    >
      <Pill delay={26}>热门 / 最新 / 最近更新</Pill>
      <Pill delay={32} color={colors.violet}>
        分类筛选
      </Pill>
      <Pill delay={38} color={colors.green}>
        详情浏览
      </Pill>
    </AnimatedText>
  </AbsoluteFill>
);

const FlowRow: React.FC<{
  readonly index: number;
  readonly children: React.ReactNode;
  readonly delay: number;
  readonly color: string;
}> = ({index, children, delay, color}) => {
  const frame = useCurrentFrame();
  const progress = interpolate(frame, [delay, delay + 18], [0, 1], {
    ...clamp,
    easing: luxeEase,
  });

  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "52px 1fr",
        gap: 16,
        alignItems: "center",
        opacity: progress,
        translate: `${(1 - progress) * -26}px 0`,
      }}
    >
      <div
        style={{
          width: 42,
          height: 42,
          display: "grid",
          placeItems: "center",
          border: `1px solid ${color}66`,
          borderRadius: 12,
          color,
          background: `${color}12`,
          fontWeight: 800,
          fontSize: 18,
        }}
      >
        {String(index).padStart(2, "0")}
      </div>
      <div
        style={{
          height: 76,
          borderLeft: `1px solid ${color}30`,
          borderBottom: `1px solid ${color}30`,
          display: "flex",
          alignItems: "center",
          paddingLeft: 20,
          color: colors.text,
          fontSize: 29,
          fontWeight: 650,
        }}
      >
        {children}
      </div>
    </div>
  );
};

const ToolsScene: React.FC = () => (
  <AbsoluteFill style={{fontFamily}}>
    <SpaceBackground />
    <div style={{position: "absolute", left: 140, top: 152, width: 430}}>
      <Eyebrow index="03">TOOLCHAIN</Eyebrow>
      <AnimatedText
        delay={8}
        style={{
          marginTop: 22,
          fontSize: 80,
          lineHeight: 1.14,
          fontWeight: 820,
          color: colors.text,
        }}
      >
        从冲突
        <br />
        到修复
      </AnimatedText>
      <div style={{marginTop: 36, display: "flex", flexDirection: "column", gap: 8}}>
        <FlowRow index={1} delay={18} color={colors.cyan}>
          冲突检测
        </FlowRow>
        <FlowRow index={2} delay={25} color={colors.blue}>
          日志诊断
        </FlowRow>
        <FlowRow index={3} delay={32} color={colors.violet}>
          加载顺序
        </FlowRow>
        <FlowRow index={4} delay={39} color={colors.green}>
          AMUMSS 重建
        </FlowRow>
      </div>
    </div>
    <ScreenshotFrame
      src={staticFile("tools.png")}
      left={620}
      top={142}
      width={1160}
      height={705}
      objectPosition="left top"
      zoom={0.04}
    />
    <AnimatedText
      delay={30}
      style={{
        position: "absolute",
        right: 142,
        bottom: 76,
        color: colors.muted,
        fontSize: 24,
        letterSpacing: 2,
      }}
    >
      把模糊的问题，变成可执行的下一步。
    </AnimatedText>
  </AbsoluteFill>
);

const ProofCard: React.FC<{
  readonly label: string;
  readonly value: string;
  readonly detail: string;
  readonly color: string;
  readonly delay: number;
}> = ({label, value, detail, color, delay}) => {
  const frame = useCurrentFrame();
  const progress = interpolate(frame, [delay, delay + 22], [0, 1], {
    ...clamp,
    easing: luxeEase,
  });

  return (
    <div
      style={{
        width: 390,
        height: 210,
        padding: "28px 30px",
        border: `1px solid ${color}40`,
        borderRadius: 24,
        background: `linear-gradient(145deg, ${color}13, rgba(9,20,32,.76))`,
        boxShadow: "0 24px 70px rgba(0,0,0,.25), inset 0 1px 0 rgba(255,255,255,.04)",
        opacity: progress,
        translate: `0 ${(1 - progress) * 36}px`,
      }}
    >
      <div style={{fontSize: 19, color, letterSpacing: 3, fontWeight: 750}}>{label}</div>
      <div style={{marginTop: 14, fontSize: 43, color: colors.text, fontWeight: 800}}>{value}</div>
      <div style={{marginTop: 9, fontSize: 23, color: colors.muted, lineHeight: 1.4}}>{detail}</div>
    </div>
  );
};

const ProofScene: React.FC = () => (
  <AbsoluteFill style={{fontFamily}}>
    <SpaceBackground accent="violet" />
    <div style={{position: "absolute", left: 140, top: 176, display: "flex", alignItems: "center", gap: 30}}>
      <HoloOrb size={126} />
      <div>
        <AnimatedText delay={4} style={{fontSize: 63, color: colors.text, fontWeight: 820, letterSpacing: -1}}>
          All Mod's Sky
        </AnimatedText>
        <AnimatedText delay={10} style={{marginTop: 8, fontSize: 28, color: colors.muted, letterSpacing: 2}}>
          A NMS MOD MANAGER · v2.0.5
        </AnimatedText>
      </div>
    </div>
    <AnimatedText
      delay={12}
      style={{
        position: "absolute",
        left: 140,
        top: 386,
        width: 640,
        fontSize: 48,
        lineHeight: 1.42,
        color: colors.text,
        fontWeight: 720,
      }}
    >
      保留游戏的<span style={{color: colors.cyan}}>真实目录</span>
      <br />
      让每一次调整都有迹可循。
    </AnimatedText>
    <div
      style={{
        position: "absolute",
        right: 140,
        top: 230,
        display: "grid",
        gridTemplateColumns: "390px 390px",
        gap: 20,
      }}
    >
      <ProofCard label="LIBRARY" value="统一管理" detail="导入、启停、排序、同步" color={colors.cyan} delay={18} />
      <ProofCard label="DIAGNOSTICS" value="提前排雷" detail="冲突、日志与加载顺序" color={colors.violet} delay={24} />
      <ProofCard label="DISCOVERY" value="双源浏览" detail="Nexus · oogc.cc" color={colors.green} delay={30} />
      <ProofCard label="PLATFORM" value="轻量原生" detail="Windows 10 / 11 · .NET 8" color={colors.blue} delay={36} />
    </div>
    <AnimatedText
      delay={42}
      style={{
        position: "absolute",
        left: 140,
        bottom: 108,
        color: colors.muted,
        fontSize: 25,
        letterSpacing: 3,
      }}
    >
      功能围绕真实的《无人深空》目录与工作流设计。
    </AnimatedText>
  </AbsoluteFill>
);

const OutroScene: React.FC = () => {
  const frame = useCurrentFrame();
  const ring = interpolate(frame, [0, 150], [0.8, 1.08], clamp);

  return (
    <AbsoluteFill style={{fontFamily}}>
      <SpaceBackground grid={false} />
      <div style={{position: "absolute", left: "50%", top: 165, translate: "-50% 0", scale: ring}}>
        <HoloOrb size={230} />
      </div>
      <div
        style={{
          position: "absolute",
          left: 140,
          right: 140,
          bottom: 122,
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          textAlign: "center",
        }}
      >
        <AnimatedText
          delay={6}
          style={{fontSize: 112, lineHeight: 1.05, color: colors.text, fontWeight: 850, letterSpacing: -3}}
        >
          All Mod's Sky
        </AnimatedText>
        <AnimatedText delay={14} style={{marginTop: 14, fontSize: 39, color: colors.muted, letterSpacing: 2}}>
          让宇宙，按你的方式展开。
        </AnimatedText>
        <AnimatedText
          delay={24}
          style={{
            marginTop: 34,
            display: "flex",
            alignItems: "center",
            gap: 16,
            padding: "14px 24px",
            border: `1px solid ${colors.cyan}44`,
            borderRadius: 999,
            background: "rgba(7, 20, 31, .7)",
            color: colors.cyan,
            fontSize: 26,
            letterSpacing: 1,
          }}
        >
          <span>github.com/Dlover32214/NMSModManager</span>
          <span style={{color: colors.muted}}>v2.0.5</span>
        </AnimatedText>
      </div>
    </AbsoluteFill>
  );
};

export const AllModsSkyIntro: React.FC = () => {
  return (
    <>
      <AbsoluteFill style={{backgroundColor: colors.ink, fontFamily, color: colors.text}}>
        <TransitionSeries>
          <TransitionSeries.Sequence durationInFrames={sceneDurations.intro}>
            <IntroScene />
          </TransitionSeries.Sequence>
          <TransitionSeries.Transition
            presentation={fade()}
            timing={linearTiming({durationInFrames: transitionFrames})}
          />
          <TransitionSeries.Sequence durationInFrames={sceneDurations.library}>
            <LibraryScene />
          </TransitionSeries.Sequence>
          <TransitionSeries.Transition
            presentation={slide({direction: "from-right"})}
            timing={linearTiming({durationInFrames: transitionFrames})}
          />
          <TransitionSeries.Sequence durationInFrames={sceneDurations.nexus}>
            <NexusScene />
          </TransitionSeries.Sequence>
          <TransitionSeries.Transition
            presentation={fade()}
            timing={linearTiming({durationInFrames: transitionFrames})}
          />
          <TransitionSeries.Sequence durationInFrames={sceneDurations.tools}>
            <ToolsScene />
          </TransitionSeries.Sequence>
          <TransitionSeries.Transition
            presentation={slide({direction: "from-left"})}
            timing={linearTiming({durationInFrames: transitionFrames})}
          />
          <TransitionSeries.Sequence durationInFrames={sceneDurations.proof}>
            <ProofScene />
          </TransitionSeries.Sequence>
          <TransitionSeries.Transition
            presentation={fade()}
            timing={linearTiming({durationInFrames: transitionFrames})}
          />
          <TransitionSeries.Sequence durationInFrames={sceneDurations.outro}>
            <OutroScene />
          </TransitionSeries.Sequence>
        </TransitionSeries>
        <AbsoluteFill
          style={{
            background:
              "linear-gradient(to bottom, rgba(255,255,255,.018), transparent 10%, transparent 88%, rgba(0,0,0,.22))",
          }}
        />
        <div
          style={{
            position: "absolute",
            left: 0,
            right: 0,
            bottom: 0,
            height: 5,
            background: `linear-gradient(90deg, transparent 0%, ${colors.cyan} 30%, ${colors.violet} 70%, transparent 100%)`,
            opacity: 0.68,
          }}
        />
      </AbsoluteFill>
    </>
  );
};
