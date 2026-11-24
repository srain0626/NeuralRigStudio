"use client";

import React, { useEffect, useState } from "react";
import { HERO_VOICES, Locale, MINIMAL_COPY } from "./LandingData";
import { StudioSlotCard } from "./StudioUiPrimitives";

interface FeatureTrioStagesProps {
  locale: Locale;
  installerHref: string;
  onJumpToChapter: (chapterIdx: number) => void;
}

const MIC_PRESETS = [
  { name: "57+121", ax: 78, ay: 58, bx: 128, by: 78, posA: "25%", posB: "55%" },
  { name: "HAAS", ax: 64, ay: 48, bx: 158, by: 96, posA: "16%", posB: "74%" },
  { name: "EDGE", ax: 142, ay: 64, bx: 172, by: 82, posA: "62%", posB: "82%" },
];

export function FeatureTrioStages({
  locale,
  installerHref,
  onJumpToChapter,
}: FeatureTrioStagesProps) {
  const t = MINIMAL_COPY[locale];
  const [presetIdx, setPresetIdx] = useState(1); // 1C Parallel .NAM
  const [activeCol, setActiveCol] = useState(1);
  const [micPresetIdx, setMicPresetIdx] = useState(0);
  const [loopSec, setLoopSec] = useState(2.4);
  const [copied, setCopied] = useState(false);

  // Continuous live studio micro-animations
  useEffect(() => {
    const id1 = window.setInterval(() => {
      setActiveCol((prev) => (prev + 1) % 4);
    }, 900);
    const id2 = window.setInterval(() => {
      setMicPresetIdx((prev) => (prev + 1) % MIC_PRESETS.length);
    }, 2400);
    const id3 = window.setInterval(() => {
      setLoopSec((prev) => Number(((prev + 0.1) % 8.0).toFixed(1)));
    }, 180);

    return () => {
      window.clearInterval(id1);
      window.clearInterval(id2);
      window.clearInterval(id3);
    };
  }, []);

  const rig = HERO_VOICES[presetIdx];
  const mic = MIC_PRESETS[micPresetIdx];

  const handleCopyMcp = () => {
    navigator.clipboard?.writeText("npx -y neuralrig-mcp install --client all").catch(() => {});
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1800);
  };

  return (
    <section className="nr-trio-section">
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

      <div className="nr-trio-grid">
        {/* ================================================================
            STAGE 1: ACTUAL CORTEXGRID 2x8 PORCELAIN CARDS + PARALLEL CABLE
           ================================================================ */}
        <article className="nr-stage-card">
          <div
            className="nr-obsidian-box"
            onClick={() => setPresetIdx((prev) => (prev + 1) % HERO_VOICES.length)}
            style={{
              cursor: "pointer",
              padding: "22px 20px",
              background:
                "radial-gradient(circle at 20% 14%, rgba(255, 118, 42, 0.36) 0%, rgba(13, 17, 19, 0) 58%), #0d1113",
            }}
          >
            <div style={{ width: "100%", position: "relative" }}>
              {/* Mini Studio Preset Header */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  marginBottom: 14,
                  color: "#f5f7f6",
                }}
              >
                <div style={{ display: "flex", alignItems: "baseline", gap: 6 }}>
                  <span
                    style={{
                      fontFamily: "var(--font-mono)",
                      fontSize: 10.5,
                      fontWeight: 700,
                      color: "#ff6b24",
                    }}
                  >
                    {rig.bank}
                  </span>
                  <span style={{ fontSize: 13.5, fontWeight: 450 }}>{rig.presetTitle}</span>
                </div>
                <span
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 9.5,
                    padding: "2px 7px",
                    borderRadius: 999,
                    background: "rgba(255,255,255,0.09)",
                    color: "rgba(255,255,255,0.75)",
                  }}
                >
                  SCENE {rig.scene}
                </span>
              </div>

              {/* 2x4 Cropped Focus of Real CortexGrid */}
              <div style={{ position: "relative", padding: "6px 0" }}>
                <svg
                  viewBox="0 0 320 144"
                  preserveAspectRatio="none"
                  style={{
                    position: "absolute",
                    inset: 0,
                    width: "100%",
                    height: "100%",
                    pointerEvents: "none",
                  }}
                >
                  <line
                    x1={8}
                    y1={34}
                    x2={312}
                    y2={34}
                    stroke="rgba(255,255,255,0.18)"
                    strokeWidth={1.5}
                  />
                  <path
                    d="M 118 34 L 118 68 Q 118 74 124 74 L 154 74 Q 160 74 160 82 L 160 110 L 304 110"
                    fill="none"
                    stroke="#ff6b24"
                    strokeWidth={1.8}
                    strokeDasharray="5 4"
                  />
                </svg>

                <div
                  style={{
                    position: "relative",
                    zIndex: 2,
                    display: "grid",
                    gridTemplateColumns: "repeat(4, minmax(0, 1fr))",
                    gap: 8,
                  }}
                >
                  {rig.row1.slice(0, 4).map((slot, idx) => (
                    <StudioSlotCard key={idx} slot={slot} compact selected={idx === activeCol} />
                  ))}
                </div>

                <div
                  style={{
                    position: "relative",
                    zIndex: 2,
                    marginTop: 14,
                    display: "grid",
                    gridTemplateColumns: "repeat(4, minmax(0, 1fr))",
                    gap: 8,
                  }}
                >
                  {rig.row2.slice(0, 4).map((slot, idx) => (
                    <StudioSlotCard
                      key={idx}
                      slot={slot}
                      compact
                      selected={Boolean(slot) && activeCol === 2}
                    />
                  ))}
                </div>
              </div>
            </div>
          </div>

          <div className="nr-stage-caption">
            <h3 className="nr-stage-title">{t.trio[0].title}</h3>
            <p className="nr-stage-line">{t.trio[0].line}</p>
            <button
              type="button"
              className="nr-dot-btn"
              onClick={() => setPresetIdx((prev) => (prev + 1) % HERO_VOICES.length)}
            >
              <span className="nr-dot" />
              <span>{t.trio[0].action}</span>
            </button>
          </div>
        </article>

        {/* ================================================================
            STAGE 2: ACTUAL VIRTUAL CAB ROOM 2D SPEAKER CONE RADAR (step_8)
           ================================================================ */}
        <article className="nr-stage-card">
          <div
            className="nr-obsidian-box"
            onClick={() => setMicPresetIdx((prev) => (prev + 1) % MIC_PRESETS.length)}
            style={{
              cursor: "pointer",
              padding: "20px",
              background:
                "radial-gradient(circle at 80% 18%, rgba(255, 118, 42, 0.34) 0%, rgba(13, 17, 19, 0) 58%), #0d1113",
            }}
          >
            {/* Porcelain Sheet Replica of VirtualCabRoom */}
            <div
              style={{
                width: "100%",
                borderRadius: 14,
                overflow: "hidden",
                background: "#f4f6f5",
                color: "#0b0e10",
                boxShadow: "0 20px 44px rgba(0,0,0,0.4)",
              }}
            >
              {/* Sheet Header */}
              <div
                style={{
                  padding: "8px 11px",
                  borderBottom: "1px solid rgba(11,14,16,0.08)",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  background: "#e9eeec",
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                  <span
                    style={{
                      width: 16,
                      height: 16,
                      borderRadius: 4,
                      background: "#ff6b24",
                      color: "#fff",
                      fontSize: 9,
                      display: "inline-flex",
                      alignItems: "center",
                      justifyContent: "center",
                    }}
                  >
                    ⏻
                  </span>
                  <span style={{ fontSize: 11.5, fontWeight: 600 }}>2x12 JP Stereo Jazz</span>
                </div>
                <span
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 8.5,
                    fontWeight: 600,
                    padding: "3px 7px",
                    borderRadius: 999,
                    background: "#0b0e10",
                    color: "#ffffff",
                  }}
                >
                  CAB ROOM 2×
                </span>
              </div>

              {/* 2D Speaker Cone Radar + Mic Cards */}
              <div
                style={{
                  padding: "10px",
                  display: "grid",
                  gridTemplateColumns: "1.15fr 0.85fr",
                  gap: 8,
                  alignItems: "stretch",
                }}
              >
                <div
                  style={{
                    borderRadius: 8,
                    background: "#dde3e0",
                    border: "1px solid rgba(11,14,16,0.08)",
                    padding: 6,
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                  }}
                >
                  <svg viewBox="0 0 210 116" style={{ width: "100%", height: "auto" }}>
                    {/* Radar Gridlines */}
                    {[32, 62, 92].map((y) => (
                      <line
                        key={y}
                        x1={12}
                        y1={y}
                        x2={198}
                        y2={y}
                        stroke="rgba(11,14,16,0.12)"
                        strokeDasharray="2 3"
                      />
                    ))}
                    {/* Speaker Cone Concentric Rings */}
                    <ellipse
                      cx={38}
                      cy={62}
                      rx={26}
                      ry={44}
                      fill="rgba(11,14,16,0.04)"
                      stroke="rgba(11,14,16,0.16)"
                    />
                    <ellipse
                      cx={38}
                      cy={62}
                      rx={11}
                      ry={19}
                      fill="rgba(11,14,16,0.07)"
                      stroke="rgba(11,14,16,0.2)"
                    />

                    {/* Mic A (SM57 Orange) */}
                    <g
                      style={{
                        transform: `translate(${mic.ax}px, ${mic.ay}px)`,
                        transition: "transform 620ms cubic-bezier(0.22, 1, 0.36, 1)",
                      }}
                    >
                      <circle r={11} fill="rgba(255,107,36,0.2)" stroke="#ff6b24" strokeWidth={1.5} />
                      <circle r={3.5} fill="#ff6b24" />
                      <text
                        y={-15}
                        textAnchor="middle"
                        fill="#ff6b24"
                        fontFamily="var(--font-mono)"
                        fontSize={8}
                        fontWeight={700}
                      >
                        A·SM57
                      </text>
                    </g>

                    {/* Mic B (R-121 Green) */}
                    <g
                      style={{
                        transform: `translate(${mic.bx}px, ${mic.by}px)`,
                        transition: "transform 620ms cubic-bezier(0.22, 1, 0.36, 1)",
                      }}
                    >
                      <circle r={11} fill="rgba(21,128,61,0.18)" stroke="#15803d" strokeWidth={1.5} />
                      <circle r={3.5} fill="#15803d" />
                      <text
                        y={21}
                        textAnchor="middle"
                        fill="#15803d"
                        fontFamily="var(--font-mono)"
                        fontSize={8}
                        fontWeight={700}
                      >
                        B·R121
                      </text>
                    </g>
                  </svg>

                  {/* 57+121 | HAAS | EDGE Quick Buttons */}
                  <div style={{ display: "grid", gridTemplateColumns: "repeat(3, 1fr)", gap: 4 }}>
                    {MIC_PRESETS.map((mp, idx) => (
                      <span
                        key={mp.name}
                        style={{
                          textAlign: "center",
                          fontFamily: "var(--font-mono)",
                          fontSize: 8.5,
                          fontWeight: 600,
                          padding: "3px 0",
                          borderRadius: 4,
                          background: idx === micPresetIdx ? "#0b0e10" : "#ffffff",
                          color: idx === micPresetIdx ? "#ffffff" : "#0b0e10",
                        }}
                      >
                        {mp.name}
                      </span>
                    ))}
                  </div>
                </div>

                {/* Right Mic Readout Cards */}
                <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                  <div
                    style={{
                      padding: "7px 8px",
                      borderRadius: 7,
                      background: "#ffffff",
                      border: "1px solid rgba(11,14,16,0.08)",
                    }}
                  >
                    <div
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 8.5,
                        fontWeight: 700,
                        color: "#ff6b24",
                      }}
                    >
                      MIC A · SM57
                    </div>
                    <div
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 9.5,
                        color: "#51595b",
                        marginTop: 3,
                      }}
                    >
                      POS {mic.posA} · 0.0dB
                    </div>
                  </div>

                  <div
                    style={{
                      padding: "7px 8px",
                      borderRadius: 7,
                      background: "#ffffff",
                      border: "1px solid rgba(11,14,16,0.08)",
                    }}
                  >
                    <div
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 8.5,
                        fontWeight: 700,
                        color: "#15803d",
                      }}
                    >
                      MIC B · R-121
                    </div>
                    <div
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 9.5,
                        color: "#51595b",
                        marginTop: 3,
                      }}
                    >
                      POS {mic.posB} · -1.5dB
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <div className="nr-stage-caption">
            <h3 className="nr-stage-title">{t.trio[1].title}</h3>
            <p className="nr-stage-line">{t.trio[1].line}</p>
            <button
              type="button"
              className="nr-dot-btn"
              onClick={() => setMicPresetIdx((prev) => (prev + 1) % MIC_PRESETS.length)}
            >
              <span className="nr-dot" />
              <span>{t.trio[1].action}</span>
            </button>
          </div>
        </article>

        {/* ================================================================
            STAGE 3: ACTUAL TP-7 TAPE LOOPER / RE-AMP DECK + MCP (step_10)
           ================================================================ */}
        <article className="nr-stage-card">
          <div
            className="nr-obsidian-box"
            onClick={handleCopyMcp}
            style={{
              cursor: "pointer",
              padding: "20px",
              background:
                "radial-gradient(circle at 50% 24%, rgba(255, 118, 42, 0.36) 0%, rgba(13, 17, 19, 0) 60%), #0d1113",
            }}
          >
            {/* Authentic Dark Floating TAPE · LOOP + REC · RE-AMP Deck */}
            <div
              style={{
                width: "100%",
                borderRadius: 14,
                padding: "13px 14px",
                background: "#13181b",
                border: "1px solid rgba(255,255,255,0.12)",
                color: "#f5f7f6",
                boxShadow: "0 22px 48px rgba(0,0,0,0.45)",
              }}
            >
              <style>{`
                @keyframes nrReelSpin { to { transform: rotate(360deg); } }
              `}</style>

              {/* Top Tabs: TAPE · LOOP | MR DECK */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  marginBottom: 11,
                }}
              >
                <div
                  style={{
                    display: "inline-flex",
                    padding: 2,
                    borderRadius: 6,
                    background: "rgba(255,255,255,0.06)",
                    fontFamily: "var(--font-mono)",
                    fontSize: 9,
                  }}
                >
                  <span
                    style={{
                      padding: "3px 8px",
                      borderRadius: 4,
                      background: "#f5f7f6",
                      color: "#0b0e10",
                      fontWeight: 700,
                    }}
                  >
                    TAPE · LOOP
                  </span>
                  <span style={{ padding: "3px 8px", color: "rgba(255,255,255,0.5)" }}>
                    MR DECK
                  </span>
                </div>
                <span
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 9.5,
                    color: "rgba(255,255,255,0.55)",
                  }}
                >
                  120 BPM
                </span>
              </div>

              {/* Rotating TP-7 3-Spoke Tape Reel + Loop Transport */}
              <div
                style={{
                  padding: "10px 11px",
                  borderRadius: 10,
                  background: "rgba(255,255,255,0.03)",
                  border: "1px solid rgba(255,255,255,0.08)",
                  display: "flex",
                  alignItems: "center",
                  gap: 12,
                }}
              >
                <svg
                  width={42}
                  height={42}
                  viewBox="0 0 44 44"
                  style={{
                    flexShrink: 0,
                    animation: "nrReelSpin 4.2s linear infinite",
                  }}
                >
                  <circle
                    cx={22}
                    cy={22}
                    r={19}
                    fill="none"
                    stroke="rgba(255,255,255,0.22)"
                    strokeWidth={1.3}
                  />
                  <circle cx={22} cy={22} r={5.5} fill="none" stroke="#f5f7f6" strokeWidth={1.5} />
                  <circle cx={22} cy={22} r={2} fill="#ff6b24" />
                  {[0, 120, 240].map((deg) => (
                    <line
                      key={deg}
                      x1={22}
                      y1={7}
                      x2={22}
                      y2={15}
                      stroke="rgba(255,255,255,0.65)"
                      strokeWidth={1.5}
                      transform={`rotate(${deg} 22 22)`}
                    />
                  ))}
                </svg>

                <div style={{ flex: 1 }}>
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                    }}
                  >
                    <span
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 15,
                        fontVariantNumeric: "tabular-nums",
                      }}
                    >
                      {loopSec.toFixed(1)}s
                    </span>
                    <div
                      style={{
                        width: 58,
                        height: 4,
                        borderRadius: 99,
                        background: "rgba(255,255,255,0.16)",
                        overflow: "hidden",
                      }}
                    >
                      <div style={{ width: "68%", height: "100%", background: "#ff6b24" }} />
                    </div>
                  </div>

                  <div style={{ display: "flex", gap: 5, marginTop: 7 }}>
                    {["● REC", "½X", "REV", "SYNC"].map((b, i) => (
                      <span
                        key={b}
                        style={{
                          fontFamily: "var(--font-mono)",
                          fontSize: 8.5,
                          padding: "3px 6px",
                          borderRadius: 4,
                          background: i === 0 ? "rgba(255,107,36,0.2)" : "rgba(255,255,255,0.06)",
                          color: i === 0 ? "#ff8c42" : "rgba(255,255,255,0.7)",
                          border: "1px solid rgba(255,255,255,0.1)",
                        }}
                      >
                        {b}
                      </span>
                    ))}
                  </div>
                </div>
              </div>

              {/* Bottom DI+WET Re-Amp & MCP Bar */}
              <div
                style={{
                  marginTop: 9,
                  paddingTop: 9,
                  borderTop: "1px solid rgba(255,255,255,0.08)",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  fontFamily: "var(--font-mono)",
                  fontSize: 9.5,
                }}
              >
                <div style={{ display: "flex", gap: 6 }}>
                  <span
                    style={{
                      padding: "3px 7px",
                      borderRadius: 4,
                      background: "rgba(255,255,255,0.06)",
                    }}
                  >
                    ● REC DI+WET
                  </span>
                  <span
                    style={{
                      padding: "3px 7px",
                      borderRadius: 4,
                      background: "rgba(255,255,255,0.06)",
                    }}
                  >
                    ((•)) RE-AMP
                  </span>
                </div>
                <span style={{ color: copied ? "#4ade80" : "#ff8c42" }}>
                  {copied ? "✓ MCP" : "npx MCP"}
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
