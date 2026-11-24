"use client";

import React, { useEffect, useState } from "react";
import { Locale, MINIMAL_COPY } from "./LandingData";

interface FeatureTrioStagesProps {
  locale: Locale;
  installerHref: string;
  onJumpToChapter: (chapterIdx: number) => void;
}

const SPICE_PROFILES = [
  { name: "BOSS SD-1", metric: "H2 -23.6 dBc", match: "99.2%", bars: [92, 68, 78, 48, 56, 34] },
  { name: "Ibanez TS808", metric: "+10.3 dB @ 720Hz", match: "97.6%", bars: [94, 8, 82, 7, 60, 6] },
  { name: "Ge AC128 Fuzz", metric: "Vc2 -4.52 V", match: "98.5%", bars: [96, 78, 88, 66, 74, 54] },
  { name: "'65 Twin 12AX7", metric: "-9.7 dB Scoop", match: "95.9%", bars: [90, 52, 38, 24, 18, 11] },
];

const MATRIX_TILES = [
  { code: "GATE", sub: "-68dB", accent: false },
  { code: "SD-1", sub: "MNA", accent: true },
  { code: "2203", sub: "12AX7", accent: true },
  { code: "V30", sub: "16k IR", accent: false },
  { code: "SPLIT", sub: "Row B", accent: false },
  { code: ".NAM", sub: "AVX2", accent: true },
  { code: "G12M", sub: "Cab B", accent: false },
  { code: "0 B", sub: "0.05ms", accent: true },
];

export function FeatureTrioStages({
  locale,
  installerHref,
  onJumpToChapter,
}: FeatureTrioStagesProps) {
  const t = MINIMAL_COPY[locale];
  const [spiceIdx, setSpiceIdx] = useState(0);
  const [activeTile, setActiveTile] = useState(0);
  const [mcpStep, setMcpStep] = useState(0);
  const [copied, setCopied] = useState(false);

  // Autonomous cycling micro-animations across all 3 obsidian stages
  useEffect(() => {
    const t1 = window.setInterval(() => {
      setSpiceIdx((prev) => (prev + 1) % SPICE_PROFILES.length);
    }, 2600);
    const t2 = window.setInterval(() => {
      setActiveTile((prev) => (prev + 1) % MATRIX_TILES.length);
    }, 620);
    const t3 = window.setInterval(() => {
      setMcpStep((prev) => (prev + 1) % 3);
    }, 2100);

    return () => {
      window.clearInterval(t1);
      window.clearInterval(t2);
      window.clearInterval(t3);
    };
  }, []);

  const spice = SPICE_PROFILES[spiceIdx];

  const handleCopyMcp = () => {
    navigator.clipboard?.writeText("npx -y neuralrig-mcp install --client all").catch(() => {});
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1800);
  };

  return (
    <section className="nr-trio-section">
      {/* Clean Header Row */}
      <div className="nr-trio-top">
        <h2 className="nr-display-h2">
          {t.trioHeading[0]}
          <br />
          {t.trioHeading[1]}
        </h2>

        <a href={installerHref} className="nr-solid-btn">
          <span className="nr-arrow">→</span>
          <span>{t.trioCta}</span>
        </a>
      </div>

      {/* 3 Square Dark Obsidian Stages */}
      <div className="nr-trio-grid">
        {/* ================================================================
            STAGE 1: LIVE SPICE CIRCUIT MORPH
           ================================================================ */}
        <article className="nr-stage-card">
          <div
            className="nr-obsidian-box"
            onClick={() => setSpiceIdx((prev) => (prev + 1) % SPICE_PROFILES.length)}
            style={{
              cursor: "pointer",
              background:
                "radial-gradient(circle at 18% 18%, rgba(255, 126, 48, 0.52) 0%, rgba(13, 17, 19, 0) 58%), #0d1113",
            }}
          >
            <div
              style={{
                width: "100%",
                maxWidth: 288,
                borderRadius: 10,
                overflow: "hidden",
                background: "rgba(255, 255, 255, 0.07)",
                backdropFilter: "blur(12px)",
                border: "1px solid rgba(255, 255, 255, 0.14)",
                boxShadow: "0 20px 44px rgba(0, 0, 0, 0.35)",
              }}
            >
              <div style={{ padding: "18px 18px 14px", color: "#f5f7f6" }}>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    fontFamily: "var(--font-mono)",
                    fontSize: 11,
                    color: "rgba(255,255,255,0.65)",
                  }}
                >
                  <span>{spice.name}</span>
                  <span style={{ color: "#ff8c42" }}>{spice.match}</span>
                </div>

                <div
                  style={{
                    marginTop: 10,
                    fontSize: 26,
                    fontWeight: 350,
                    letterSpacing: "-0.03em",
                    fontVariantNumeric: "tabular-nums",
                  }}
                >
                  {spice.metric}
                </div>

                {/* Morphing Harmonic Bars */}
                <div
                  style={{
                    marginTop: 16,
                    height: 58,
                    display: "grid",
                    gridTemplateColumns: "repeat(6, 1fr)",
                    gap: 7,
                    alignItems: "end",
                  }}
                >
                  {spice.bars.map((h, i) => (
                    <div
                      key={i}
                      style={{
                        height: `${h}%`,
                        borderRadius: "2px 2px 0 0",
                        background: i % 2 === 1 ? "#ff7730" : "rgba(245,247,246,0.85)",
                        transition: "height 520ms cubic-bezier(0.22, 1, 0.36, 1)",
                      }}
                    />
                  ))}
                </div>
              </div>

              {/* Crisp White Bottom Strip */}
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(4, 1fr)",
                  background: "#f4f6f5",
                  color: "#0b0e10",
                }}
              >
                {["SD-1", "TS808", "AC128", "12AX7"].map((lbl, idx) => (
                  <div
                    key={lbl}
                    style={{
                      padding: "9px 4px",
                      textAlign: "center",
                      fontFamily: "var(--font-mono)",
                      fontSize: 10,
                      fontWeight: idx === spiceIdx ? 600 : 400,
                      background: idx === spiceIdx ? "#ffffff" : "transparent",
                      color: idx === spiceIdx ? "#ff6b24" : "#5a6264",
                      borderRight: idx < 3 ? "1px solid rgba(11,14,16,0.08)" : 0,
                    }}
                  >
                    {lbl}
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div className="nr-stage-caption">
            <h3 className="nr-stage-title">{t.trio[0].title}</h3>
            <p className="nr-stage-line">{t.trio[0].line}</p>
            <button
              type="button"
              className="nr-dot-btn"
              onClick={() => setSpiceIdx((prev) => (prev + 1) % SPICE_PROFILES.length)}
            >
              <span className="nr-dot" />
              <span>{t.trio[0].action}</span>
            </button>
          </div>
        </article>

        {/* ================================================================
            STAGE 2: 2x8 PARALLEL & .NAM SIGNAL PULSE GRID
           ================================================================ */}
        <article className="nr-stage-card">
          <div
            className="nr-obsidian-box"
            onClick={() => onJumpToChapter(1)}
            style={{
              cursor: "pointer",
              background:
                "radial-gradient(circle at 52% 56%, rgba(255, 122, 42, 0.46) 0%, rgba(13, 17, 19, 0) 62%), #0d1113",
            }}
          >
            <div
              style={{
                width: "100%",
                maxWidth: 304,
                display: "grid",
                gridTemplateColumns: "repeat(4, 1fr)",
                gap: 9,
              }}
            >
              {MATRIX_TILES.map((tile, idx) => {
                const lit = idx === activeTile || (activeTile === 2 && idx === 5);
                return (
                  <div
                    key={tile.code}
                    style={{
                      aspectRatio: "1 / 1",
                      borderRadius: 9,
                      background: lit ? "#ffffff" : "rgba(244, 246, 245, 0.92)",
                      color: "#0b0e10",
                      display: "flex",
                      flexDirection: "column",
                      alignItems: "center",
                      justifyContent: "center",
                      gap: 3,
                      transform: lit ? "translate3d(0, -5px, 0) scale(1.04)" : "translate3d(0, 0, 0)",
                      boxShadow: lit
                        ? "0 14px 28px rgba(255, 107, 36, 0.36), 0 0 0 2px #ff6b24"
                        : "0 8px 18px rgba(0, 0, 0, 0.28)",
                      transition:
                        "transform 320ms cubic-bezier(0.22, 1, 0.36, 1), box-shadow 320ms ease",
                    }}
                  >
                    <span
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 12,
                        fontWeight: 600,
                        color: lit ? "#ff6b24" : "#0b0e10",
                      }}
                    >
                      {tile.code}
                    </span>
                    <span
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 9,
                        color: "#636b6d",
                      }}
                    >
                      {tile.sub}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>

          <div className="nr-stage-caption">
            <h3 className="nr-stage-title">{t.trio[1].title}</h3>
            <p className="nr-stage-line">{t.trio[1].line}</p>
            <button type="button" className="nr-dot-btn" onClick={() => onJumpToChapter(1)}>
              <span className="nr-dot" />
              <span>{t.trio[1].action}</span>
            </button>
          </div>
        </article>

        {/* ================================================================
            STAGE 3: AI SCHEMATIC-TO-PEDAL CONVERTER (NPX NEURALRIG-MCP)
           ================================================================ */}
        <article className="nr-stage-card">
          <div
            className="nr-obsidian-box"
            onClick={handleCopyMcp}
            style={{
              cursor: "pointer",
              background:
                "radial-gradient(circle at 50% 42%, rgba(255, 132, 54, 0.5) 0%, rgba(13, 17, 19, 0) 64%), #0d1113",
            }}
          >
            <div
              style={{
                width: "100%",
                maxWidth: 292,
                borderRadius: 10,
                padding: "18px",
                background: "rgba(18, 23, 26, 0.78)",
                backdropFilter: "blur(14px)",
                border: "1px solid rgba(255, 255, 255, 0.14)",
                color: "#f5f7f6",
                boxShadow: "0 20px 44px rgba(0, 0, 0, 0.36)",
              }}
            >
              {/* Input Schematic Row */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <div>
                  <div style={{ fontSize: 18, fontWeight: 400, letterSpacing: "-0.02em" }}>
                    {mcpStep === 0 ? "Datasheet" : mcpStep === 1 ? ".AC / .TRAN" : "Hot-Swap"}
                  </div>
                  <div
                    style={{
                      fontFamily: "var(--font-mono)",
                      fontSize: 10,
                      color: "rgba(255,255,255,0.56)",
                      marginTop: 2,
                    }}
                  >
                    {mcpStep === 0
                      ? "1S1588 · M5218AL"
                      : mcpStep === 1
                        ? "0 B RT Verified"
                        : "Mounted Slot 02"}
                  </div>
                </div>
                <span
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 10.5,
                    padding: "4px 9px",
                    borderRadius: 999,
                    background: "rgba(255,255,255,0.1)",
                    border: "1px solid rgba(255,255,255,0.14)",
                  }}
                >
                  MCP
                </span>
              </div>

              {/* Center Glowing Node Divider (00:02 Reference Homage) */}
              <div
                style={{
                  position: "relative",
                  margin: "18px 0",
                  height: 1,
                  background: "rgba(255,255,255,0.14)",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                }}
              >
                <span
                  style={{
                    width: 28,
                    height: 28,
                    borderRadius: "50%",
                    background: "#ffffff",
                    color: "#0b0e10",
                    display: "inline-flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontFamily: "var(--font-mono)",
                    fontSize: 13,
                    fontWeight: 600,
                    boxShadow: "0 0 22px rgba(255, 132, 54, 0.85)",
                    transform: `rotate(${mcpStep * 90}deg)`,
                    transition: "transform 480ms cubic-bezier(0.22, 1, 0.36, 1)",
                  }}
                >
                  +
                </span>
              </div>

              {/* Output Compiled Pedal Row */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <code
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 11.5,
                    color: copied ? "#4ade80" : "#ff9b5e",
                  }}
                >
                  {copied ? "✓ Copied to clipboard" : "npx neuralrig-mcp"}
                </code>
                <span
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 10,
                    padding: "4px 8px",
                    borderRadius: 999,
                    background: "rgba(34, 197, 94, 0.16)",
                    color: "#4ade80",
                  }}
                >
                  0 B
                </span>
              </div>
            </div>
          </div>

          <div className="nr-stage-caption">
            <h3 className="nr-stage-title">{t.trio[2].title}</h3>
            <p className="nr-stage-line">{t.trio[2].line}</p>
            <button type="button" className="nr-dot-btn" onClick={handleCopyMcp}>
              <span className="nr-dot" />
              <span>{copied ? (locale === "ko" ? "복사됨" : "Copied") : t.trio[2].action}</span>
            </button>
          </div>
        </article>
      </div>
    </section>
  );
}
