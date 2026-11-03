import {AbsoluteFill, Img, staticFile} from "remotion";

const colors = {
  ink: "#030A10",
  text: "#F4FAFF",
  muted: "#A7BCD0",
  faint: "#6E879C",
  cyan: "#56DFFF",
  blue: "#648DFF",
  violet: "#A383FF",
  green: "#5BE7B5",
};

const displayFont =
  '"Bahnschrift", "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", sans-serif';
const bodyFont =
  '"Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Segoe UI", sans-serif';

const BrandOrb: React.FC<{size: number}> = ({size}) => (
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
        inset: size * 0.12,
        borderRadius: "50%",
        background: "rgba(82,217,255,.28)",
        filter: `blur(${size * 0.16}px)`,
      }}
    />
    <div
      style={{
        width: size * 0.62,
        height: size * 0.62,
        borderRadius: "50%",
        background:
          "radial-gradient(circle at 31% 24%, #F4FFFF 0%, #7DEBFF 13%, #42B9EE 35%, #3974D7 67%, #17215A 100%)",
        boxShadow: `0 0 ${size * 0.34}px rgba(72,211,255,.72), inset ${-size * 0.05}px ${-size * 0.04}px ${size * 0.12}px rgba(10,28,83,.86)`,
      }}
    />
    <div
      style={{
        position: "absolute",
        width: size * 0.9,
        height: size * 0.28,
        border: `1px solid rgba(151,239,255,.65)`,
        borderRadius: "50%",
        rotate: "-14deg",
      }}
    />
  </div>
);

const Tag: React.FC<{
  readonly children: React.ReactNode;
  readonly color?: string;
}> = ({children, color = colors.cyan}) => (
  <div
    style={{
      display: "inline-flex",
      alignItems: "center",
      gap: 9,
      height: 40,
      padding: "0 17px",
      borderRadius: 999,
      border: `1px solid ${color}55`,
      background: `linear-gradient(135deg, ${color}17, rgba(5,16,25,.82))`,
      color: colors.text,
      fontFamily: bodyFont,
      fontSize: 20,
      fontWeight: 650,
      whiteSpace: "nowrap",
      boxShadow: `inset 0 0 18px ${color}08`,
    }}
  >
    <span
      style={{
        width: 7,
        height: 7,
        borderRadius: "50%",
        background: color,
        boxShadow: `0 0 10px ${color}`,
      }}
    />
    {children}
  </div>
);

const Feature: React.FC<{
  readonly index: string;
  readonly title: string;
  readonly detail: string;
  readonly color: string;
}> = ({index, title, detail, color}) => (
  <div
    style={{
      width: 245,
      padding: "15px 17px 16px",
      borderRadius: 15,
      border: `1px solid ${color}35`,
      background: "linear-gradient(145deg, rgba(10,26,39,.86), rgba(4,12,20,.66))",
      boxShadow: "0 14px 34px rgba(0,0,0,.22)",
    }}
  >
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 9,
        color,
        fontFamily: displayFont,
        fontSize: 13,
        fontWeight: 700,
        letterSpacing: 2,
      }}
    >
      <span>{index}</span>
      <span
        style={{
          width: 28,
          height: 1,
          background: `linear-gradient(90deg, ${color}, transparent)`,
        }}
      />
    </div>
    <div style={{marginTop: 8, color: colors.text, fontSize: 21, fontWeight: 750}}>
      {title}
    </div>
    <div style={{marginTop: 4, color: colors.faint, fontSize: 14, lineHeight: 1.4}}>
      {detail}
    </div>
  </div>
);

export const AllModsSkyCover: React.FC = () => {
  return (
    <AbsoluteFill
      style={{
        backgroundColor: colors.ink,
        color: colors.text,
        fontFamily: bodyFont,
        overflow: "hidden",
      }}
    >
      <Img
        src={staticFile("nms-wallpaper-725605.png")}
        style={{
          position: "absolute",
          width: "126%",
          height: "126%",
          left: "-13%",
          top: "-30%",
          objectFit: "cover",
          opacity: 0.9,
        }}
      />

      <AbsoluteFill
        style={{
          background:
            "linear-gradient(90deg, rgba(2,8,13,.94) 0%, rgba(2,9,15,.84) 37%, rgba(2,11,18,.28) 68%, rgba(2,9,14,.66) 100%)",
        }}
      />
      <AbsoluteFill
        style={{
          background:
            "linear-gradient(180deg, rgba(1,7,12,.8) 0%, transparent 22%, transparent 72%, rgba(1,7,12,.94) 100%)",
        }}
      />
      <AbsoluteFill
        style={{
          background:
            "radial-gradient(circle at 75% 41%, rgba(72,213,255,.2), transparent 29%), radial-gradient(circle at 88% 65%, rgba(102,225,182,.13), transparent 24%)",
          mixBlendMode: "screen",
        }}
      />

      <div
        style={{
          position: "absolute",
          left: 80,
          right: 80,
          top: 58,
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
        }}
      >
        <div style={{display: "flex", alignItems: "center", gap: 18}}>
          <BrandOrb size={66} />
          <div>
            <div
              style={{
                fontFamily: displayFont,
                fontSize: 23,
                fontWeight: 800,
                letterSpacing: 5,
              }}
            >
              ALL MODS SKY
            </div>
            <div style={{marginTop: 4, color: colors.faint, fontSize: 16, letterSpacing: 2}}>
              无人深空 · 模组管理器
            </div>
          </div>
        </div>
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 15,
            color: colors.muted,
            fontFamily: displayFont,
            fontSize: 14,
            letterSpacing: 2.5,
          }}
        >
          <span
            style={{
              width: 7,
              height: 7,
              borderRadius: "50%",
              background: colors.green,
              boxShadow: `0 0 12px ${colors.green}`,
            }}
          />
          VERSION 2.0.5
        </div>
      </div>

      <div style={{position: "absolute", left: 90, top: 248, width: 760}}>
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 13,
            color: colors.cyan,
            fontFamily: displayFont,
            fontSize: 16,
            fontWeight: 700,
            letterSpacing: 4,
          }}
        >
          <span
            style={{
              width: 44,
              height: 2,
              background: `linear-gradient(90deg, ${colors.cyan}, transparent)`,
            }}
          />
          MOD CONTROL CENTER
        </div>

        <div
          style={{
            marginTop: 27,
            fontSize: 126,
            fontWeight: 900,
            lineHeight: 1.05,
            letterSpacing: -5,
            textShadow: "0 18px 58px rgba(0,0,0,.35)",
          }}
        >
          所有模组
          <br />
          <span style={{color: colors.cyan}}>一处掌握</span>
        </div>
        <div
          style={{
            marginTop: 23,
            width: 690,
            color: colors.muted,
            fontSize: 28,
            lineHeight: 1.55,
            letterSpacing: 0.5,
          }}
        >
          看得见排序，找得到冲突，修得好问题。
          <br />
          <span style={{color: colors.text}}>让《无人深空》的模组管理真正清晰。</span>
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          left: 88,
          bottom: 88,
          display: "flex",
          gap: 12,
          flexWrap: "wrap",
          width: 840,
        }}
      >
        <Tag>模组库</Tag>
        <Tag color={colors.violet}>拖拽排序</Tag>
        <Tag color={colors.blue}>冲突检测</Tag>
        <Tag color={colors.green}>AMUMSS 一键修复</Tag>
      </div>

      <div
        style={{
          position: "absolute",
          right: 80,
          top: 132,
          width: 936,
          height: 690,
          rotate: "-1.5deg",
        }}
      >
        <div
          style={{
            position: "absolute",
            inset: 0,
            borderRadius: 28,
            border: "1px solid rgba(112,220,255,.36)",
            background: "linear-gradient(145deg, rgba(63,202,255,.14), rgba(7,19,30,.9))",
            boxShadow:
              "0 48px 110px rgba(0,0,0,.58), 0 0 0 1px rgba(255,255,255,.04), inset 0 1px 0 rgba(255,255,255,.12)",
            overflow: "hidden",
          }}
        >
          <div
            style={{
              height: 56,
              padding: "0 20px",
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              borderBottom: "1px solid rgba(153,226,255,.14)",
              background: "rgba(3,13,21,.9)",
            }}
          >
            <div style={{display: "flex", alignItems: "center", gap: 12}}>
              <BrandOrb size={34} />
              <span style={{fontSize: 18, fontWeight: 750}}>实时模组库</span>
              <span
                style={{
                  color: colors.faint,
                  fontFamily: displayFont,
                  fontSize: 12,
                  letterSpacing: 2,
                }}
              >
                LIVE LIBRARY
              </span>
            </div>
            <span
              style={{
                color: colors.green,
                fontSize: 13,
                letterSpacing: 1.5,
              }}
            >
              ● 已同步
            </span>
          </div>
          <Img
            src={staticFile("library.png")}
            style={{
              width: "100%",
              height: "calc(100% - 56px)",
              objectFit: "cover",
              objectPosition: "left top",
            }}
          />
          <div
            style={{
              position: "absolute",
              right: 22,
              bottom: 20,
              padding: "11px 16px",
              borderRadius: 12,
              border: "1px solid rgba(91,231,181,.42)",
              background: "rgba(4,18,25,.9)",
              color: colors.green,
              fontSize: 17,
              fontWeight: 700,
              boxShadow: "0 12px 30px rgba(0,0,0,.3)",
            }}
          >
            拖拽即可排序
          </div>
        </div>

        <div
          style={{
            position: "absolute",
            left: -55,
            bottom: -54,
            width: 390,
            height: 180,
            rotate: "3deg",
            borderRadius: 18,
            border: "1px solid rgba(163,131,255,.45)",
            background: "rgba(5,14,25,.94)",
            boxShadow: "0 28px 60px rgba(0,0,0,.5)",
            overflow: "hidden",
          }}
        >
          <Img
            src={staticFile("tools.png")}
            style={{
              width: "100%",
              height: "100%",
              objectFit: "cover",
              objectPosition: "right top",
              opacity: 0.95,
            }}
          />
          <div
            style={{
              position: "absolute",
              left: 14,
              top: 13,
              padding: "6px 11px",
              borderRadius: 8,
              background: "rgba(3,12,21,.89)",
              border: "1px solid rgba(163,131,255,.4)",
              color: colors.violet,
              fontSize: 14,
              fontWeight: 700,
            }}
          >
            冲突诊断 · AMUMSS 修复
          </div>
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          left: 92,
          bottom: 172,
          display: "flex",
          gap: 14,
        }}
      >
        <Feature
          index="01"
          title="模组库"
          detail="唯一真实目录 · 不复制第二份"
          color={colors.cyan}
        />
        <Feature
          index="02"
          title="加载顺序"
          detail="整行拖拽 · 自动备份 MXML"
          color={colors.violet}
        />
        <Feature
          index="03"
          title="诊断修复"
          detail="日志定位 · 冲突检测 · 重建"
          color={colors.green}
        />
      </div>

      <div
        style={{
          position: "absolute",
          left: 80,
          right: 80,
          bottom: 28,
          display: "flex",
          justifyContent: "space-between",
          color: colors.faint,
          fontFamily: displayFont,
          fontSize: 13,
          letterSpacing: 2,
        }}
      >
        <span>NEXUS · OOGC.CC · GAMEDATA / MODS</span>
        <span>排序 · 备份 · 诊断 · 修复</span>
      </div>
    </AbsoluteFill>
  );
};
