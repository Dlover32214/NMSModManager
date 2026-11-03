import type {Caption} from "@remotion/captions";
import {Audio} from "@remotion/media";
import {
  AbsoluteFill,
  Easing,
  Img,
  Sequence,
  interpolate,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import {TransitionSeries, linearTiming} from "@remotion/transitions";
import {fade} from "@remotion/transitions/fade";
import {slide} from "@remotion/transitions/slide";
import lyricsData from "./lyrics.json";

const lyrics = lyricsData as Caption[];

const colors = {
  ink: "#040A11",
  ink2: "#07131F",
  panel: "#0B1724",
  text: "#F0F8FF",
  muted: "#9CB5CA",
  faint: "#637E95",
  cyan: "#52D9FF",
  blue: "#5D8DFF",
  violet: "#A07BFF",
  green: "#51E1AC",
  amber: "#F1B75F",
};

const displayFont =
  '"Bahnschrift", "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", sans-serif';
const bodyFont =
  '"Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Segoe UI", sans-serif';

const clamp = {
  extrapolateLeft: "clamp" as const,
  extrapolateRight: "clamp" as const,
};
const ease = Easing.bezier(0.16, 1, 0.3, 1);
const transitionFrames = 15;

const sceneDurations = {
  intro: 180,
  library: 300,
  discover: 270,
  tools: 270,
  principles: 270,
  feedback: 360,
  outro: 120,
};

const starfield = Array.from({length: 110}, (_, index) => ({
  x: (index * 61 + 17) % 100,
  y: (index * 37 + 11) % 100,
  size: 0.8 + ((index * 13) % 5) * 0.58,
  opacity: 0.12 + ((index * 31) % 62) / 100,
  warm: index % 11 === 0,
}));

const Reveal: React.FC<{
  readonly children: React.ReactNode;
  readonly delay?: number;
  readonly duration?: number;
  readonly from?: "up" | "left" | "right";
  readonly style?: React.CSSProperties;
}> = ({children, delay = 0, duration = 24, from = "up", style}) => {
  const frame = useCurrentFrame();
  const progress = interpolate(frame, [delay, delay + duration], [0, 1], {
    ...clamp,
    easing: ease,
  });
  const distance = (1 - progress) * 42;
  const translate =
    from === "left" ? `${-distance}px 0` : from === "right" ? `${distance}px 0` : `0 ${distance}px`;

  return (
    <div
      style={{
        ...style,
        opacity: progress,
        translate,
        filter: `blur(${(1 - progress) * 5}px)`,
      }}
    >
      {children}
    </div>
  );
};

const AmbientBackground: React.FC<{
  readonly variant?: "cyan" | "violet" | "green";
  readonly grid?: boolean;
  readonly quiet?: boolean;
}> = ({variant = "cyan", grid = true, quiet = false}) => {
  const frame = useCurrentFrame();
  const accent =
    variant === "violet" ? colors.violet : variant === "green" ? colors.green : colors.cyan;

  return (
    <AbsoluteFill style={{backgroundColor: colors.ink, overflow: "hidden"}}>
      <AbsoluteFill
        style={{
          background:
            variant === "violet"
              ? "radial-gradient(circle at 73% 5%, rgba(139,107,255,.2), transparent 36%), radial-gradient(circle at 15% 85%, rgba(42,102,189,.16), transparent 38%), linear-gradient(142deg, #07111D 0%, #040910 55%, #0A0B18 100%)"
              : variant === "green"
                ? "radial-gradient(circle at 76% 12%, rgba(69,221,161,.13), transparent 34%), radial-gradient(circle at 16% 84%, rgba(53,119,221,.16), transparent 38%), linear-gradient(142deg, #06111A 0%, #040A10 58%, #071712 100%)"
                : "radial-gradient(circle at 76% 10%, rgba(53,186,255,.2), transparent 34%), radial-gradient(circle at 16% 82%, rgba(61,91,210,.18), transparent 38%), linear-gradient(142deg, #07111C 0%, #040A11 58%, #07111C 100%)",
        }}
      />
      {starfield.map((star, index) => (
        <div
          key={index}
          style={{
            position: "absolute",
            left: `${star.x}%`,
            top: `${star.y}%`,
            width: star.size,
            height: star.size,
            borderRadius: 999,
            background: star.warm ? colors.amber : "#E7FBFF",
            opacity: quiet ? star.opacity * 0.6 : star.opacity,
            boxShadow: index % 13 === 0 ? `0 0 10px ${accent}` : undefined,
            translate: `0 ${Math.sin((frame + index * 17) / 39) * 3.5}px`,
          }}
        />
      ))}
      {grid ? (
        <AbsoluteFill
          style={{
            opacity: quiet ? 0.12 : 0.2,
            backgroundImage:
              "linear-gradient(rgba(87,205,255,.16) 1px, transparent 1px), linear-gradient(90deg, rgba(87,205,255,.16) 1px, transparent 1px)",
            backgroundSize: "92px 92px",
            backgroundPosition: `0px ${frame * 0.1}px`,
            maskImage: "linear-gradient(to bottom, transparent 8%, black 56%, transparent 100%)",
          }}
        />
      ) : null}
      <div
        style={{
          position: "absolute",
          width: 940,
          height: 940,
          right: -470,
          top: -540,
          borderRadius: "50%",
          border: `1px solid ${accent}2E`,
          rotate: `${frame * 0.025}deg`,
        }}
      />
      <div
        style={{
          position: "absolute",
          width: 690,
          height: 690,
          right: -330,
          top: -410,
          borderRadius: "50%",
          border: `1px dashed ${accent}40`,
          rotate: `${-frame * 0.018}deg`,
        }}
      />
      <div
        style={{
          position: "absolute",
          left: 64,
          top: 52,
          width: 54,
          height: 1,
          background: `${accent}80`,
          boxShadow: `0 0 16px ${accent}`,
        }}
      />
      <div
        style={{
          position: "absolute",
          right: 64,
          bottom: 62,
          width: 54,
          height: 1,
          background: `${accent}80`,
          boxShadow: `0 0 16px ${accent}`,
        }}
      />
    </AbsoluteFill>
  );
};

const BrandOrb: React.FC<{readonly size?: number; readonly pulseOffset?: number}> = ({
  size = 180,
  pulseOffset = 0,
}) => {
  const frame = useCurrentFrame();
  const angle = frame * 1.42 + pulseOffset;
  const glow = interpolate(Math.sin((frame + pulseOffset) / 24), [-1, 1], [0.34, 0.72]);

  return (
    <div style={{width: size, height: size, position: "relative", display: "grid", placeItems: "center"}}>
      <div
        style={{
          position: "absolute",
          width: size * 1.03,
          height: size * 1.03,
          borderRadius: "50%",
          background: `radial-gradient(circle, rgba(83,217,255,${glow}) 0%, rgba(93,141,255,.18) 38%, transparent 68%)`,
          filter: "blur(13px)",
        }}
      />
      <div
        style={{
          position: "absolute",
          width: size * 0.7,
          height: size * 0.7,
          borderRadius: "50%",
          background:
            "radial-gradient(circle at 31% 24%, #F4FFFF 0%, #7DEBFF 13%, #42B9EE 35%, #3974D7 67%, #17215A 100%)",
          boxShadow:
            "0 0 54px rgba(83,217,255,.5), inset -22px -18px 38px rgba(5,10,31,.74)",
        }}
      />
      <div
        style={{
          position: "absolute",
          width: size * 0.88,
          height: size * 0.88,
          borderRadius: "50%",
          borderTop: `2px solid ${colors.violet}`,
          borderRight: "2px solid transparent",
          borderBottom: `2px solid ${colors.cyan}`,
          borderLeft: "2px solid transparent",
          rotate: `${angle}deg`,
          scale: "1 0.38",
        }}
      />
      <div
        style={{
          position: "absolute",
          width: size * 1.03,
          height: size * 0.48,
          borderRadius: "50%",
          border: "1.5px dashed rgba(131,230,255,.55)",
          rotate: "-13deg",
        }}
      />
      <div
        style={{
          position: "absolute",
          width: size * 0.17,
          height: size * 0.17,
          borderRadius: "50%",
          background: "#F3FFFF",
          boxShadow: "0 0 24px 9px rgba(109,231,255,.9)",
          left: size / 2 + Math.cos((angle * Math.PI) / 180) * size * 0.42,
          top: size / 2 + Math.sin((angle * Math.PI) / 180) * size * 0.18,
          translate: "-50% -50%",
        }}
      />
    </div>
  );
};

const SectionTag: React.FC<{
  readonly index: string;
  readonly label: string;
  readonly color?: string;
  readonly delay?: number;
}> = ({index, label, color = colors.cyan, delay = 0}) => (
  <Reveal delay={delay} from="left">
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 14,
        fontFamily: displayFont,
        fontSize: 22,
        fontWeight: 650,
        letterSpacing: 3.5,
        color,
      }}
    >
      <span style={{width: 42, height: 2, background: color, boxShadow: `0 0 14px ${color}`}} />
      <span>{index}</span>
      <span style={{color: colors.faint}}>{label}</span>
    </div>
  </Reveal>
);

const Tag: React.FC<{
  readonly children: React.ReactNode;
  readonly color?: string;
  readonly delay?: number;
}> = ({children, color = colors.cyan, delay = 0}) => (
  <Reveal delay={delay}>
    <div
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 9,
        padding: "11px 17px",
        borderRadius: 999,
        border: `1px solid ${color}55`,
        background: `${color}12`,
        color: colors.text,
        fontFamily: bodyFont,
        fontSize: 21,
        fontWeight: 600,
      }}
    >
      <span style={{width: 7, height: 7, borderRadius: "50%", background: color, boxShadow: `0 0 10px ${color}`}} />
      {children}
    </div>
  </Reveal>
);

const ScreenshotPanel: React.FC<{
  readonly src: string;
  readonly label: string;
  readonly left: number;
  readonly top: number;
  readonly width: number;
  readonly height: number;
  readonly zoom?: number;
  readonly objectPosition?: string;
}> = ({src, label, left, top, width, height, zoom = 0.035, objectPosition = "left top"}) => {
  const frame = useCurrentFrame();
  const reveal = interpolate(frame, [0, 31], [0, 1], {...clamp, easing: ease});
  const scale = interpolate(frame, [0, 300], [1, 1 + zoom], clamp);
  const scan = interpolate(frame, [18, 90], [-15, 110], clamp);

  return (
    <div
      style={{
        position: "absolute",
        left,
        top,
        width,
        height,
        padding: 9,
        borderRadius: 26,
        background: "linear-gradient(145deg, rgba(83,217,255,.36), rgba(160,123,255,.12))",
        boxShadow: "0 44px 100px rgba(0,0,0,.62), 0 0 0 1px rgba(208,243,255,.14)",
        opacity: reveal,
        translate: `0 ${(1 - reveal) * 34}px`,
        scale: interpolate(reveal, [0, 1], [0.978, 1], {...clamp, output: "perceptual-scale"}),
        clipPath: `inset(0 ${(1 - reveal) * 8}% 0 0 round 26px)`,
      }}
    >
      <div
        style={{
          position: "absolute",
          left: 26,
          top: 14,
          zIndex: 3,
          padding: "7px 14px",
          borderRadius: 999,
          border: "1px solid rgba(116,220,255,.28)",
          background: "#09131E",
          color: colors.cyan,
          fontFamily: displayFont,
          fontSize: 16,
          fontWeight: 650,
          letterSpacing: 2,
        }}
      >
        {label}
      </div>
      <div
        style={{
          position: "relative",
          width: "100%",
          height: "100%",
          borderRadius: 19,
          overflow: "hidden",
          background: colors.ink2,
        }}
      >
        <Img
          src={src}
          style={{
            width: "100%",
            height: "100%",
            objectFit: "cover",
            objectPosition,
            scale,
          }}
        />
        <div
          style={{
            position: "absolute",
            inset: 0,
            background:
              "linear-gradient(118deg, rgba(255,255,255,.055), transparent 21%, transparent 74%, rgba(74,204,255,.05))",
          }}
        />
        <div
          style={{
            position: "absolute",
            left: 0,
            right: 0,
            top: `${scan}%`,
            height: 2,
            opacity: frame < 92 ? 0.45 : 0,
            background: "linear-gradient(90deg, transparent, rgba(104,231,255,.8), transparent)",
            boxShadow: "0 0 15px rgba(104,231,255,.6)",
          }}
        />
      </div>
    </div>
  );
};

const LyricsOverlay: React.FC = () => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const timeMs = (frame / fps) * 1_000;
  const captionIndex = lyrics.findIndex(
    (caption) => timeMs >= caption.startMs && timeMs < caption.endMs,
  );
  const caption = lyrics[captionIndex];

  if (!caption) return null;

  const duration = Math.max(1, caption.endMs - caption.startMs);
  const local = Math.max(0, Math.min(1, (timeMs - caption.startMs) / duration));
  const entrance = interpolate(timeMs - caption.startMs, [0, 240], [0, 1], clamp);
  const exit = interpolate(caption.endMs - timeMs, [0, 180], [0, 1], clamp);
  const opacity = Math.min(entrance, exit);
  const [english, chinese] = caption.text.trim().split("\n");

  return (
    <div
      key={`${caption.startMs}-${caption.text}`}
      style={{
        position: "absolute",
        left: 280,
        right: 280,
        bottom: 40,
        padding: "15px 22px 12px",
        border: "1px solid rgba(154,226,255,.22)",
        borderRadius: 18,
        background: "rgba(4,11,18,.82)",
        boxShadow: "0 20px 50px rgba(0,0,0,.28), inset 0 1px 0 rgba(255,255,255,.04)",
        opacity,
        translate: `0 ${(1 - entrance) * 13}px`,
        fontFamily: bodyFont,
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          color: colors.faint,
          fontFamily: displayFont,
          fontSize: 14,
          letterSpacing: 2.5,
          lineHeight: 1.2,
        }}
      >
        <span style={{color: colors.cyan}}>♪ LOST &amp; FOUND</span>
        <span>FEINT / R7CKY / SKYELLE</span>
      </div>
      <div
        style={{
          marginTop: 7,
          color: colors.text,
          fontSize: 26,
          fontWeight: 620,
          lineHeight: 1.25,
          textAlign: "center",
          textShadow: "0 4px 20px rgba(0,0,0,.5)",
        }}
      >
        {english}
      </div>
      <div
        style={{
          marginTop: 4,
          color: colors.cyan,
          fontSize: 20,
          fontWeight: 500,
          lineHeight: 1.35,
          textAlign: "center",
          letterSpacing: 0.4,
        }}
      >
        {chinese}
      </div>
      <div
        style={{
          height: 2,
          marginTop: 10,
          borderRadius: 3,
          background: "rgba(130,189,221,.16)",
          overflow: "hidden",
        }}
      >
        <div
          style={{
            width: `${local * 100}%`,
            height: "100%",
            background: `linear-gradient(90deg, ${colors.cyan}, ${colors.violet})`,
          }}
        />
      </div>
    </div>
  );
};

const IntroScene: React.FC = () => {
  const frame = useCurrentFrame();
  const scale = interpolate(frame, [0, 38], [0.72, 1], {
    ...clamp,
    easing: ease,
    output: "perceptual-scale",
  });

  return (
    <AbsoluteFill style={{fontFamily: bodyFont}}>
      <AmbientBackground grid={false} quiet />
      <div
        style={{
          position: "absolute",
          left: 140,
          right: 140,
          top: 62,
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          color: colors.faint,
          fontFamily: displayFont,
          fontSize: 20,
          letterSpacing: 3,
        }}
      >
        <span>ALL MODS SKY</span>
        <span>版本 2.0.5</span>
      </div>
      <div
        style={{
          position: "absolute",
          inset: "126px 140px 160px",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          textAlign: "center",
        }}
      >
        <div style={{scale, marginBottom: 22}}>
          <BrandOrb size={180} />
        </div>
        <Reveal
          delay={12}
          duration={34}
          style={{
            color: colors.text,
            fontFamily: displayFont,
            fontSize: 148,
            fontWeight: 800,
            lineHeight: 0.98,
            letterSpacing: -5,
            textShadow: "0 18px 70px rgba(72,188,255,.22)",
          }}
        >
          All Mods Sky
        </Reveal>
        <Reveal
          delay={23}
          style={{
            marginTop: 30,
            color: colors.muted,
            fontFamily: displayFont,
            fontSize: 32,
            fontWeight: 600,
            letterSpacing: 8,
          }}
        >
          《无人深空》模组管理器
        </Reveal>
        <Reveal
          delay={36}
          style={{
            marginTop: 32,
            display: "flex",
            gap: 10,
          }}
        >
          <Tag>管理</Tag>
          <Tag delay={4} color={colors.violet}>
            排序
          </Tag>
          <Tag delay={8} color={colors.green}>
            诊断
          </Tag>
        </Reveal>
      </div>
    </AbsoluteFill>
  );
};

const LibraryScene: React.FC = () => (
  <AbsoluteFill style={{fontFamily: bodyFont}}>
    <AmbientBackground />
    <div style={{position: "absolute", left: 140, top: 155, width: 490}}>
      <SectionTag index="01" label="模组库" />
      <Reveal
        delay={8}
        style={{
          marginTop: 22,
          color: colors.text,
          fontFamily: bodyFont,
          fontSize: 84,
          fontWeight: 800,
          lineHeight: 1.13,
          letterSpacing: -2,
        }}
      >
        所有模组
        <br />
        <span style={{color: colors.cyan}}>一处掌握</span>
      </Reveal>
      <Reveal
        delay={18}
        style={{
          marginTop: 28,
          color: colors.muted,
          fontSize: 31,
          lineHeight: 1.72,
        }}
      >
        直接管理游戏真实的
        <br />
        <span style={{color: colors.text, fontFamily: displayFont}}>GAMEDATA / MODS</span>
      </Reveal>
      <div style={{marginTop: 31, display: "flex", flexDirection: "column", gap: 8, alignItems: "flex-start"}}>
        <Tag delay={26}>启停 · 批量操作</Tag>
        <Tag delay={32} color={colors.violet}>
          整行拖拽排序
        </Tag>
        <Tag delay={38} color={colors.green}>
          自动同步 DISABLEMODS.TXT
        </Tag>
      </div>
    </div>
    <ScreenshotPanel
      src={staticFile("library.png")}
      label="实时模组库"
      left={675}
      top={145}
      width={1110}
      height={675}
      zoom={0.038}
    />
    <Reveal
      delay={30}
      style={{
        position: "absolute",
        right: 140,
        bottom: 145,
        color: colors.faint,
        fontFamily: displayFont,
        fontSize: 18,
        letterSpacing: 2.6,
      }}
    >
      唯一真实目录 · 不复制第二份库
    </Reveal>
  </AbsoluteFill>
);

const DiscoverScene: React.FC = () => (
  <AbsoluteFill style={{fontFamily: bodyFont}}>
    <AmbientBackground variant="violet" grid={false} />
    <div
      style={{
        position: "absolute",
        left: 140,
        right: 140,
        top: 100,
        display: "flex",
        alignItems: "flex-end",
        justifyContent: "space-between",
      }}
    >
      <div>
        <SectionTag index="02" label="发现模组" color={colors.violet} />
        <Reveal
          delay={8}
          style={{
            marginTop: 18,
            color: colors.text,
            fontSize: 82,
            fontWeight: 800,
            lineHeight: 1.1,
            letterSpacing: -2,
          }}
        >
          浏览模组，不必来回切换
        </Reveal>
      </div>
      <Reveal
        delay={17}
        from="right"
        style={{
          display: "flex",
          gap: 10,
          paddingBottom: 8,
        }}
      >
        <Tag color={colors.violet}>NEXUS</Tag>
        <Tag delay={4} color={colors.cyan}>
          OOGC.CC
        </Tag>
      </Reveal>
    </div>
    <ScreenshotPanel
      src={staticFile("nexus.png")}
      label="NEXUS · 实时数据"
      left={140}
      top={255}
      width={1640}
      height={660}
      zoom={0.052}
    />
  </AbsoluteFill>
);

const FlowStep: React.FC<{
  readonly number: string;
  readonly label: string;
  readonly detail: string;
  readonly color: string;
  readonly delay: number;
}> = ({number, label, detail, color, delay}) => {
  const frame = useCurrentFrame();
  const reveal = interpolate(frame, [delay, delay + 22], [0, 1], {...clamp, easing: ease});

  return (
    <div
      style={{
        position: "relative",
        paddingLeft: 58,
        opacity: reveal,
        translate: `${(1 - reveal) * -22}px 0`,
      }}
    >
      <div
        style={{
          position: "absolute",
          left: 0,
          top: 3,
          width: 38,
          height: 38,
          display: "grid",
          placeItems: "center",
          borderRadius: 11,
          border: `1px solid ${color}66`,
          background: `${color}13`,
          color,
          fontFamily: displayFont,
          fontWeight: 700,
          fontSize: 15,
        }}
      >
        {number}
      </div>
      <div style={{color: colors.text, fontSize: 27, fontWeight: 700}}>{label}</div>
      <div style={{marginTop: 4, color: colors.faint, fontSize: 18}}>{detail}</div>
    </div>
  );
};

const ToolsScene: React.FC = () => {
  const frame = useCurrentFrame();
  const lineHeight = interpolate(frame, [20, 80], [0, 1], {...clamp, easing: ease});

  return (
    <AbsoluteFill style={{fontFamily: bodyFont}}>
      <AmbientBackground />
      <div style={{position: "absolute", left: 140, top: 145, width: 430}}>
        <SectionTag index="03" label="工具链" />
        <Reveal
          delay={8}
          style={{
            marginTop: 20,
            color: colors.text,
            fontSize: 82,
            fontWeight: 800,
            lineHeight: 1.12,
          }}
        >
          从冲突
          <br />
          到修复
        </Reveal>
        <div style={{position: "relative", marginTop: 38}}>
          <div
            style={{
              position: "absolute",
              left: 18,
              top: 22,
              width: 1,
              height: 278 * lineHeight,
              background: `linear-gradient(${colors.cyan}, ${colors.violet}, ${colors.green})`,
              boxShadow: "0 0 12px rgba(83,217,255,.45)",
            }}
          />
          <div style={{display: "flex", flexDirection: "column", gap: 25}}>
            <FlowStep number="01" label="冲突检测" detail="预测覆盖关系与胜出模组" color={colors.cyan} delay={18} />
            <FlowStep number="02" label="日志诊断" detail="从游戏日志定位可疑模组" color={colors.blue} delay={25} />
            <FlowStep number="03" label="加载顺序" detail="写入前自动备份 MXML" color={colors.violet} delay={32} />
            <FlowStep number="04" label="AMUMSS 重建" detail="失败可恢复原目录" color={colors.green} delay={39} />
          </div>
        </div>
      </div>
      <ScreenshotPanel
        src={staticFile("tools.png")}
        label="工具 · 实时界面"
        left={615}
        top={132}
        width={1165}
        height={715}
        zoom={0.043}
      />
      <Reveal
        delay={31}
        style={{
          position: "absolute",
          right: 145,
          bottom: 145,
          color: colors.faint,
          fontSize: 21,
          letterSpacing: 2,
        }}
      >
        把模糊的问题，变成可执行的下一步。
      </Reveal>
    </AbsoluteFill>
  );
};

const PrincipleCard: React.FC<{
  readonly number: string;
  readonly title: string;
  readonly detail: string;
  readonly color: string;
  readonly delay: number;
}> = ({number, title, detail, color, delay}) => {
  const frame = useCurrentFrame();
  const reveal = interpolate(frame, [delay, delay + 25], [0, 1], {...clamp, easing: ease});

  return (
    <div
      style={{
        width: 390,
        height: 250,
        padding: "28px 30px",
        borderRadius: 23,
        border: `1px solid ${color}42`,
        background: `linear-gradient(145deg, ${color}12, rgba(8,18,29,.82))`,
        boxShadow: "0 24px 62px rgba(0,0,0,.25)",
        opacity: reveal,
        translate: `0 ${(1 - reveal) * 34}px`,
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          color,
          fontFamily: displayFont,
          fontSize: 18,
          letterSpacing: 2,
        }}
      >
        <span>设计细节</span>
        <span>{number}</span>
      </div>
      <div style={{marginTop: 21, color: colors.text, fontSize: 38, fontWeight: 760, lineHeight: 1.2}}>
        {title}
      </div>
      <div style={{marginTop: 13, color: colors.muted, fontSize: 22, lineHeight: 1.55}}>{detail}</div>
    </div>
  );
};

const PrinciplesScene: React.FC = () => (
  <AbsoluteFill style={{fontFamily: bodyFont}}>
    <AmbientBackground variant="violet" />
    <div style={{position: "absolute", left: 140, top: 145, width: 520}}>
      <SectionTag index="04" label="用心设计" color={colors.violet} />
      <Reveal
        delay={8}
        style={{
          marginTop: 20,
          color: colors.text,
          fontSize: 75,
          fontWeight: 800,
          lineHeight: 1.18,
        }}
      >
        比“能启动”
        <br />
        <span style={{color: colors.violet}}>多考虑一步</span>
      </Reveal>
      <Reveal
        delay={17}
        style={{
          marginTop: 28,
          color: colors.muted,
          fontSize: 26,
          lineHeight: 1.7,
        }}
      >
        这些取舍来自 README，
        <br />
        也来自真实的模组管理场景。
      </Reveal>
    </div>
    <div
      style={{
        position: "absolute",
        right: 140,
        top: 153,
        display: "grid",
        gridTemplateColumns: "390px 390px",
        gap: 18,
      }}
    >
      <PrincipleCard
        number="01"
        title="单一真实位置"
        detail="模组只留在游戏 MODS，不复制第二份库。"
        color={colors.cyan}
        delay={18}
      />
      <PrincipleCard
        number="02"
        title="索引可丢，文件不丢"
        detail="索引丢失后，仍能发现磁盘上的外部模组。"
        color={colors.violet}
        delay={24}
      />
      <PrincipleCard
        number="03"
        title="写入前备份"
        detail="加载顺序与 AMUMSS 重建都有可恢复路径。"
        color={colors.green}
        delay={30}
      />
      <PrincipleCard
        number="04"
        title="批量操作不误触"
        detail="独立批量模式，整行选择，不干扰开关与按钮。"
        color={colors.blue}
        delay={36}
      />
    </div>
    <Reveal
      delay={43}
      style={{
        position: "absolute",
        left: 140,
        bottom: 145,
        display: "flex",
        gap: 10,
      }}
    >
      <Tag color={colors.cyan}>旧版数据兼容</Tag>
      <Tag delay={4} color={colors.violet}>
        .hg 存档读取
      </Tag>
      <Tag delay={8} color={colors.green}>
        --data 自定义目录
      </Tag>
    </Reveal>
  </AbsoluteFill>
);

const FeedbackScene: React.FC = () => {
  const frame = useCurrentFrame();
  const ringScale = interpolate(frame, [0, 100], [0.84, 1.08], clamp);

  return (
    <AbsoluteFill style={{fontFamily: bodyFont}}>
      <AmbientBackground variant="green" />
      <div
        style={{
          position: "absolute",
          width: 760,
          height: 760,
          left: "50%",
          top: -410,
          translate: "-50% 0",
          borderRadius: "50%",
          border: "1px solid rgba(81,225,172,.13)",
          scale: ringScale,
        }}
      />
      <div
        style={{
          position: "absolute",
          left: 140,
          right: 140,
          top: 132,
          display: "flex",
          justifyContent: "center",
          textAlign: "center",
        }}
      >
        <div>
          <Reveal
            delay={4}
            style={{
              color: colors.green,
              fontFamily: displayFont,
              fontSize: 22,
              fontWeight: 700,
              letterSpacing: 5,
            }}
          >
            你的反馈，决定下一次更新
          </Reveal>
          <Reveal
            delay={12}
            style={{
              marginTop: 28,
              color: colors.text,
              fontSize: 72,
              fontWeight: 800,
              lineHeight: 1.25,
              letterSpacing: -1,
            }}
          >
            你们的每一次反馈、点赞与指正，
            <br />
            都是<span style={{color: colors.green}}>我继续前进的动力</span>。
          </Reveal>
          <Reveal
            delay={22}
            style={{
              marginTop: 26,
              color: colors.muted,
              fontSize: 29,
              lineHeight: 1.7,
            }}
          >
            遇到问题、发现遗漏，或有好点子，
            <br />
            都欢迎在 GitHub Issues 里告诉我。
          </Reveal>
        </div>
      </div>
      <div
        style={{
          position: "absolute",
          left: 140,
          right: 140,
          top: 640,
          display: "flex",
          justifyContent: "center",
          gap: 12,
        }}
      >
        <Tag color={colors.green}>问题反馈</Tag>
        <Tag delay={5} color={colors.cyan}>
          功能建议
        </Tag>
        <Tag delay={10} color={colors.violet}>
          GitHub Star
        </Tag>
      </div>
      <Reveal
        delay={31}
        style={{
          position: "absolute",
          left: 140,
          right: 140,
          top: 740,
          textAlign: "center",
          color: colors.faint,
          fontSize: 23,
          letterSpacing: 1.5,
        }}
      >
        github.com/Dlover32214/NMSModManager
      </Reveal>
    </AbsoluteFill>
  );
};

const OutroScene: React.FC = () => {
  const frame = useCurrentFrame();
  const scale = interpolate(frame, [0, 60], [0.86, 1], {
    ...clamp,
    easing: ease,
    output: "perceptual-scale",
  });

  return (
    <AbsoluteFill style={{fontFamily: bodyFont}}>
      <AmbientBackground grid={false} quiet />
      <div
        style={{
          position: "absolute",
          left: "50%",
          top: 138,
          translate: "-50% 0",
          scale,
        }}
      >
        <BrandOrb size={190} />
      </div>
      <div
        style={{
          position: "absolute",
          left: 140,
          right: 140,
          bottom: 138,
          textAlign: "center",
        }}
      >
        <Reveal
          delay={5}
          style={{
            color: colors.text,
            fontFamily: displayFont,
            fontSize: 118,
            lineHeight: 1,
            fontWeight: 820,
            letterSpacing: -3,
          }}
        >
          All Mods Sky
        </Reveal>
        <Reveal
          delay={14}
          style={{
            marginTop: 20,
            color: colors.muted,
            fontFamily: displayFont,
            fontSize: 28,
            fontWeight: 600,
            letterSpacing: 5,
          }}
        >
          《无人深空》模组管理器
        </Reveal>
        <Reveal
          delay={23}
          style={{
            marginTop: 30,
            color: colors.cyan,
            fontSize: 24,
            letterSpacing: 1,
          }}
        >
          感谢观看 · github.com/Dlover32214/NMSModManager
        </Reveal>
      </div>
      <div
        style={{
          position: "absolute",
          left: 0,
          right: 0,
          bottom: 38,
          textAlign: "center",
          color: colors.faint,
          fontSize: 17,
          letterSpacing: 1.2,
        }}
      >
        音乐：Feint / R7CKY / Skyelle — Lost &amp; Found
      </div>
    </AbsoluteFill>
  );
};

const FinalFeedbackScene: React.FC = () => {
  const frame = useCurrentFrame();
  const pulse = interpolate(Math.sin(frame / 30), [-1, 1], [0.45, 0.8]);

  return (
    <AbsoluteFill style={{fontFamily: bodyFont}}>
      <AmbientBackground variant="green" grid={false} quiet />
      <div
        style={{
          position: "absolute",
          left: "50%",
          top: 170,
          translate: "-50% 0",
          width: 430,
          height: 430,
          borderRadius: "50%",
          border: `1px solid rgba(81,225,172,${pulse})`,
          boxShadow: "0 0 90px rgba(81,225,172,.08)",
        }}
      />
      <div
        style={{
          position: "absolute",
          left: 140,
          right: 140,
          top: 300,
          textAlign: "center",
        }}
      >
        <Reveal
          delay={5}
          style={{
            color: colors.green,
            fontSize: 24,
            fontWeight: 700,
            letterSpacing: 4,
          }}
        >
          欢迎继续反馈
        </Reveal>
        <Reveal
          delay={13}
          style={{
            marginTop: 24,
            color: colors.text,
            fontSize: 68,
            fontWeight: 800,
            lineHeight: 1.25,
          }}
        >
          你的想法，会让下一版更好。
        </Reveal>
        <Reveal
          delay={22}
          style={{
            marginTop: 22,
            color: colors.muted,
            fontSize: 27,
            lineHeight: 1.7,
          }}
        >
          问题、建议与使用体验，都欢迎在 GitHub Issues 告诉我。
        </Reveal>
        <Reveal
          delay={31}
          style={{
            marginTop: 36,
            display: "flex",
            justifyContent: "center",
            gap: 12,
          }}
        >
          <Tag color={colors.green}>问题反馈</Tag>
          <Tag delay={5} color={colors.cyan}>
            功能建议
          </Tag>
          <Tag delay={10} color={colors.violet}>
            GitHub Star
          </Tag>
        </Reveal>
      </div>
    </AbsoluteFill>
  );
};

const mainTimelineFrames =
  Object.values(sceneDurations).reduce((total, duration) => total + duration, 0) -
  transitionFrames * 6;
const closingFrames = 270;

export const AllModsSkyIntroV2: React.FC = () => {
  return (
    <AbsoluteFill style={{backgroundColor: colors.ink, color: colors.text}}>
      <Audio src={staticFile("lost-and-found.ogg")} volume={0.78} />
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
        <TransitionSeries.Sequence durationInFrames={sceneDurations.discover}>
          <DiscoverScene />
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
        <TransitionSeries.Sequence durationInFrames={sceneDurations.principles}>
          <PrinciplesScene />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition
          presentation={fade()}
          timing={linearTiming({durationInFrames: transitionFrames})}
        />
        <TransitionSeries.Sequence durationInFrames={sceneDurations.feedback}>
          <FeedbackScene />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition
          presentation={fade()}
          timing={linearTiming({durationInFrames: transitionFrames})}
        />
        <TransitionSeries.Sequence durationInFrames={sceneDurations.outro}>
          <OutroScene />
        </TransitionSeries.Sequence>
      </TransitionSeries>
      <Sequence from={mainTimelineFrames} durationInFrames={closingFrames}>
        <FinalFeedbackScene />
      </Sequence>
      <AbsoluteFill
        style={{
          background:
            "linear-gradient(to bottom, rgba(255,255,255,.014), transparent 11%, transparent 90%, rgba(0,0,0,.15))",
        }}
      />
      <LyricsOverlay />
      <div
        style={{
          position: "absolute",
          left: 0,
          right: 0,
          bottom: 0,
          height: 4,
          background: `linear-gradient(90deg, transparent, ${colors.cyan} 26%, ${colors.violet} 68%, transparent)`,
          opacity: 0.72,
        }}
      />
    </AbsoluteFill>
  );
};
