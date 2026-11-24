"use client";

import React, { useEffect, useRef, useState } from "react";
import { HERO_VOICES, Locale, MINIMAL_COPY } from "./LandingData";
import { GuitarAuditionController } from "./webAudioEngine";

export interface StickyHubItem {
  id: string;
  name: string;
  summary: string;
}

interface StickyMorphShowcaseProps {
  locale: Locale;
  trackRef: React.RefObject<HTMLElement | null>;
  liveHubItem?: StickyHubItem;
}

const DRIVE_PRESETS = [
  { label: "25%", val: 0.25 },
  { label: "50%", val: 0.5 },
  { label: "75%", val: 0.75 },
  { label: "Max", val: 0.94 },
];

export function StickyMorphShowcase({
  locale,
  trackRef,
  liveHubItem,
}: StickyMorphShowcaseProps) {
  const t = MINIMAL_COPY[locale];
  const [chapter, setChapter] = useState<0 | 1 | 2>(0);
  const [progress, setProgress] = useState(0);
  const [drivePct, setDrivePct] = useState(0.75);
  const [selectedBankIdx, setSelectedBankIdx] = useState(1);
  const [playing, setPlaying] = useState(false);
  const [copied, setCopied] = useState(false);

  const deviceRef = useRef<HTMLDivElement | null>(null);
  const audioRef = useRef<GuitarAuditionController | null>(null);

  useEffect(() => {
    audioRef.current = new GuitarAuditionController();
    return () => {
      audioRef.current?.stop();
    };
  }, []);

  useEffect(() => {
    audioRef.current?.update(HERO_VOICES[selectedBankIdx], drivePct);
  }, [selectedBankIdx, drivePct]);

  // Smooth rAF lerped scroll progress along the 330vh track
  useEffect(() => {
    let cur = 0;
    let target = 0;
    let raf = 0;

    const computeTarget = () => {
      const el = trackRef.current;
      if (!el) return;
      const rect = el.getBoundingClientRect();
      const scrollable = rect.height - window.innerHeight;
      if (scrollable <= 10) {
        target = 0;
        return;
      }
      const raw = -rect.top / scrollable;
      target = Math.max(0, Math.min(1, raw));
    };

    const loop = () => {
      computeTarget();
      cur += (target - cur) * 0.11;
      setProgress(cur);

      const nextChap: 0 | 1 | 2 = cur < 0.34 ? 0 : cur < 0.68 ? 1 : 2;
      setChapter((prev) => (prev === nextChap ? prev : nextChap));

      // Subtle cinematic zoom breathing between states (homage to 00:05 -> 00:14)
      if (deviceRef.current) {
        const waveScale = 1 - Math.sin(cur * Math.PI) * 0.055;
        const tiltY = (0.5 - cur) * 8;
        deviceRef.current.style.transform = `translate3d(0, ${tiltY.toFixed(1)}px, 0) scale(${waveScale.toFixed(3)})`;
      }

      raf = window.requestAnimationFrame(loop);
    };

    raf = window.requestAnimationFrame(loop);
    return () => window.cancelAnimationFrame(raf);
  }, [trackRef]);

  const scrollToChapter = (idx: 0 | 1 | 2) => {
    const el = trackRef.current;
    if (!el) return;
    const scrollable = el.offsetHeight - window.innerHeight;
    if (scrollable <= 10) {
      setChapter(idx);
      return;
    }
    const fractions = [0.08, 0.5, 0.9];
    const top = el.offsetTop + scrollable * fractions[idx];
    window.scrollTo({ top, behavior: "smooth" });
  };

  const handlePrimaryAction = () => {
    if (chapter === 2) {
      navigator.clipboard?.writeText("npx -y neuralrig-mcp install --client all").catch(() => {});
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1800);
      return;
    }
    if (playing) {
      audioRef.current?.stop();
      setPlaying(false);
    } else {
      audioRef.current?.start(HERO_VOICES[selectedBankIdx], drivePct, () => setPlaying(false));
      setPlaying(true);
    }
  };

  const activeCopy = t.stickyChapters[chapter];

  return (
    <section ref={trackRef as React.RefObject<HTMLElement>} className="nr-sticky-track">
      <div className="nr-sticky-pin">
        {/* Top Minimalist 3-Dot Navigation Strip (00:04 Reference Homage) */}
        <div className="nr-sticky-tabs">
          {t.stickyTabs.map((lbl, idx) => {
            const active = chapter === idx;
            return (
              <button
                key={lbl}
                type="button"
                onClick={() => scrollToChapter(idx as 0 | 1 | 2)}
                className="nr-dot-btn"
                style={{
                  justifyContent: "flex-start",
                  opacity: active ? 1 : 0.45,
                  fontWeight: active ? 550 : 400,
                }}
              >
                <span
                  className="nr-dot"
                  style={{
                    transform: active ? "scale(1.3)" : "scale(0.85)",
                    transition: "transform 220ms ease",
                  }}
                />
                <span>{lbl}</span>
              </button>
            );
          })}
        </div>

        {/* Main Split: Airy Left Column + Centered Morphing Slate Viewport */}
        <div className="nr-sticky-body">
          {/* Left Minimalist Sticky Copy */}
          <div className="nr-sticky-left">
            <div>
              <div
                key={chapter}
                style={{
                  animation: "nrFadeSlideIn 420ms cubic-bezier(0.16, 1, 0.3, 1)",
                }}
              >
                <h2 className="nr-display-h2">
                  {activeCopy.title[0]}
                  <br />
                  {activeCopy.title[1]}
                </h2>

                <div style={{ marginTop: 26 }}>
                  <button type="button" onClick={handlePrimaryAction} className="nr-solid-btn">
                    <span className="nr-arrow">
                      {chapter === 2 ? (copied ? "✓" : "→") : playing ? "■" : "▶"}
                    </span>
                    <span>
                      {chapter === 2
                        ? copied
                          ? activeCopy.stopCta
                          : activeCopy.cta
                        : playing
                          ? activeCopy.stopCta
                          : activeCopy.cta}
                    </span>
                  </button>
                </div>
              </div>
            </div>

            {/* Bottom Minimal Rule + Single-Line Telemetry */}
            <div>
              <div
                style={{
                  position: "relative",
                  height: 1,
                  background: "rgba(11, 14, 16, 0.16)",
                  marginBottom: 20,
                  overflow: "hidden",
                }}
              >
                <div
                  style={{
                    position: "absolute",
                    left: 0,
                    top: 0,
                    bottom: 0,
                    width: `${Math.round(progress * 100)}%`,
                    background: "var(--accent)",
                  }}
                />
              </div>

              <div style={{ fontSize: 20, fontWeight: 450, letterSpacing: "-0.02em" }}>
                {activeCopy.subTitle}
              </div>
              <p
                style={{
                  margin: "7px 0 0",
                  fontSize: 13.5,
                  color: "var(--ink-muted)",
                  lineHeight: 1.5,
                }}
              >
                {activeCopy.subLine}
              </p>
            </div>
          </div>

          {/* Right Slate Viewport Containing the Morphing Centerpiece Window */}
          <div className="nr-sticky-viewport">
            <style>{`
              @keyframes nrFadeSlideIn {
                from { opacity: 0; transform: translate3d(0, 14px, 0); }
                to { opacity: 1; transform: translate3d(0, 0, 0); }
              }
            `}</style>

            <div
              ref={deviceRef}
              style={{
                width: "100%",
                maxWidth: 368,
                height: 456,
                borderRadius: 12,
                overflow: "hidden",
                background: "#0d1113",
                border: "1px solid rgba(255, 255, 255, 0.12)",
                boxShadow: "0 34px 80px rgba(0, 0, 0, 0.45)",
                position: "relative",
                willChange: "transform",
              }}
            >
              {/* 3-Slide Vertical Morphing Reel Inside the Device Frame */}
              <div
                style={{
                  height: "300%",
                  width: "100%",
                  transform: `translate3d(0, -${(chapter * 100) / 3}%, 0)`,
                  transition: "transform 680ms cubic-bezier(0.22, 1, 0.36, 1)",
                }}
              >
                {/* ==========================================================
                    SLIDE 0: SPICE HARMONIC & DRIVE DIAL PAD (00:08-00:13 Homage)
                   ========================================================== */}
                <div
                  style={{
                    height: "33.3333%",
                    padding: "22px",
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    color: "#f5f7f6",
                    background:
                      "radial-gradient(circle at 86% 6%, rgba(255, 122, 42, 0.58) 0%, rgba(13, 17, 19, 0) 52%), #0d1113",
                  }}
                >
                  <div>
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        fontSize: 12.5,
                        color: "rgba(255,255,255,0.7)",
                      }}
                    >
                      <span>SPICE Drive</span>
                      <span style={{ fontFamily: "var(--font-mono)", fontSize: 11 }}>4× HIIR</span>
                    </div>

                    <div
                      style={{
                        marginTop: 22,
                        display: "flex",
                        alignItems: "baseline",
                        justifyContent: "space-between",
                      }}
                    >
                      <span
                        style={{
                          fontSize: 44,
                          fontWeight: 350,
                          letterSpacing: "-0.04em",
                          color: "#ff8c42",
                          fontVariantNumeric: "tabular-nums",
                        }}
                      >
                        {(drivePct * 10).toFixed(1)}
                      </span>
                      <span
                        style={{
                          fontFamily: "var(--font-mono)",
                          fontSize: 11,
                          padding: "4px 10px",
                          borderRadius: 999,
                          background: "rgba(255,255,255,0.1)",
                        }}
                      >
                        1S1588 × 3
                      </span>
                    </div>

                    <div
                      style={{
                        margin: "18px 0",
                        height: 1,
                        background: "rgba(255,255,255,0.1)",
                      }}
                    />

                    <div
                      style={{
                        display: "flex",
                        alignItems: "baseline",
                        justifyContent: "space-between",
                      }}
                    >
                      <div>
                        <div
                          style={{
                            fontSize: 26,
                            fontWeight: 350,
                            fontVariantNumeric: "tabular-nums",
                          }}
                        >
                          {(-58 + drivePct * 38).toFixed(1)} dBc
                        </div>
                        <div style={{ fontSize: 11, color: "rgba(255,255,255,0.48)", marginTop: 2 }}>
                          Even 2nd Harmonic (H2)
                        </div>
                      </div>
                      <span
                        style={{
                          fontFamily: "var(--font-mono)",
                          fontSize: 11,
                          color: "#4ade80",
                        }}
                      >
                        99.2% Match
                      </span>
                    </div>
                  </div>

                  {/* Tactile Quick-Drive Pills & Numeric Matrix */}
                  <div>
                    <div
                      style={{
                        display: "grid",
                        gridTemplateColumns: "repeat(4, 1fr)",
                        gap: 8,
                        marginBottom: 12,
                      }}
                    >
                      {DRIVE_PRESETS.map((p) => {
                        const on = Math.abs(drivePct - p.val) < 0.05;
                        return (
                          <button
                            key={p.label}
                            type="button"
                            onClick={() => setDrivePct(p.val)}
                            style={{
                              border: "1px solid rgba(255,255,255,0.12)",
                              borderRadius: 999,
                              padding: "7px 0",
                              background: on ? "#f5f7f6" : "rgba(255,255,255,0.04)",
                              color: on ? "#0b0e10" : "rgba(255,255,255,0.78)",
                              fontFamily: "var(--font-mono)",
                              fontSize: 11,
                              cursor: "pointer",
                            }}
                          >
                            {p.label}
                          </button>
                        );
                      })}
                    </div>

                    <div
                      style={{
                        display: "grid",
                        gridTemplateColumns: "repeat(3, 1fr)",
                        gap: 8,
                      }}
                    >
                      {HERO_VOICES.map((v, i) => {
                        const active = i === selectedBankIdx;
                        return (
                          <button
                            key={v.bank}
                            type="button"
                            onClick={() => setSelectedBankIdx(i)}
                            style={{
                              border: `1px solid ${
                                active ? "#ff7730" : "rgba(255,255,255,0.09)"
                              }`,
                              borderRadius: 6,
                              padding: "12px 6px",
                              background: active
                                ? "rgba(255, 119, 48, 0.16)"
                                : "rgba(255,255,255,0.03)",
                              color: "#f5f7f6",
                              cursor: "pointer",
                            }}
                          >
                            <div style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>
                              {v.bank}
                            </div>
                            <div
                              style={{
                                fontSize: 10.5,
                                color: "rgba(255,255,255,0.6)",
                                marginTop: 2,
                              }}
                            >
                              {locale === "ko" ? v.labelKo : v.labelEn}
                            </div>
                          </button>
                        );
                      })}
                    </div>
                  </div>
                </div>

                {/* ==========================================================
                    SLIDE 1: 2x8 PARALLEL .NAM & CAB ROUTING VIEW (00:10 Homage)
                   ========================================================== */}
                <div
                  style={{
                    height: "33.3333%",
                    padding: "22px",
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    color: "#f5f7f6",
                    background:
                      "radial-gradient(circle at 15% 12%, rgba(255, 118, 42, 0.42) 0%, rgba(13, 17, 19, 0) 50%), #0d1113",
                  }}
                >
                  <div>
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        fontSize: 12,
                        color: "rgba(255,255,255,0.64)",
                      }}
                    >
                      <span>2×8 Parallel Routing</span>
                      <span style={{ fontFamily: "var(--font-mono)", color: "#4ade80" }}>
                        0.054ms · 0 B
                      </span>
                    </div>

                    {/* Visual Split Routing SVG */}
                    <svg
                      viewBox="0 0 320 110"
                      style={{ width: "100%", height: "auto", marginTop: 18 }}
                    >
                      <path
                        d="M 12 36 L 308 36"
                        stroke="rgba(255,255,255,0.22)"
                        strokeWidth={1.5}
                        fill="none"
                      />
                      <path
                        d="M 88 36 C 102 36, 102 82, 118 82 L 226 82 C 242 82, 242 36, 256 36"
                        stroke="#ff7730"
                        strokeWidth={1.6}
                        strokeDasharray="4 4"
                        fill="none"
                      />
                      <circle cx={88} cy={36} r={4} fill="#ff7730" />
                      <circle cx={256} cy={36} r={4} fill="#4ade80" />

                      {/* Row A Nodes */}
                      {[
                        { x: 24, t: "SD-1" },
                        { x: 114, t: "2203" },
                        { x: 180, t: "V30" },
                        { x: 266, t: "HALL" },
                      ].map((n) => (
                        <g key={n.t} transform={`translate(${n.x}, 21)`}>
                          <rect
                            width={46}
                            height={30}
                            rx={5}
                            fill="#181f23"
                            stroke="rgba(255,255,255,0.22)"
                          />
                          <text
                            x={23}
                            y={19}
                            textAnchor="middle"
                            fill="#f5f7f6"
                            fontFamily="var(--font-mono)"
                            fontSize={10}
                          >
                            {n.t}
                          </text>
                        </g>
                      ))}

                      {/* Row B Parallel .NAM Nodes */}
                      {[
                        { x: 118, t: ".NAM" },
                        { x: 178, t: "G12M" },
                      ].map((n) => (
                        <g key={n.t} transform={`translate(${n.x}, 67)`}>
                          <rect width={46} height={30} rx={5} fill="#241b16" stroke="#ff7730" />
                          <text
                            x={23}
                            y={19}
                            textAnchor="middle"
                            fill="#ffb084"
                            fontFamily="var(--font-mono)"
                            fontSize={10}
                          >
                            {n.t}
                          </text>
                        </g>
                      ))}
                    </svg>
                  </div>

                  {/* Active Signal Engines List */}
                  <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                    {[
                      { name: "ASIO / Driverless WASAPI", badge: "Raw Bypass", on: true },
                      { name: "AVX2 WaveNet .NAM Host", badge: "ESR 0.00", on: true },
                      { name: "16,384-Tap Partitioned IR", badge: "0 Latency", on: true },
                    ].map((row) => (
                      <div
                        key={row.name}
                        style={{
                          padding: "11px 13px",
                          borderRadius: 7,
                          background: "rgba(255,255,255,0.05)",
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "space-between",
                          fontSize: 12.5,
                        }}
                      >
                        <span>{row.name}</span>
                        <span
                          style={{
                            fontFamily: "var(--font-mono)",
                            fontSize: 10.5,
                            color: "#ff8c42",
                          }}
                        >
                          {row.badge} ✓
                        </span>
                      </div>
                    ))}
                  </div>
                </div>

                {/* ==========================================================
                    SLIDE 2: NPX NEURALRIG-MCP LIVE SCHEMATIC COMPILER
                   ========================================================== */}
                <div
                  style={{
                    height: "33.3333%",
                    padding: "22px",
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    color: "#f5f7f6",
                    background:
                      "radial-gradient(circle at 82% 85%, rgba(255, 118, 42, 0.45) 0%, rgba(13, 17, 19, 0) 56%), #0d1113",
                  }}
                >
                  <div>
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        fontSize: 12,
                        color: "rgba(255,255,255,0.65)",
                      }}
                    >
                      <span>Schematic Compiler</span>
                      <span style={{ fontFamily: "var(--font-mono)", color: "#ff8c42" }}>
                        MCP Pipe
                      </span>
                    </div>

                    <div
                      style={{
                        marginTop: 16,
                        padding: "13px 14px",
                        borderRadius: 7,
                        background: "#151b1f",
                        border: "1px solid rgba(255,255,255,0.12)",
                        fontFamily: "var(--font-mono)",
                        fontSize: 11.5,
                        color: "#f5f7f6",
                      }}
                    >
                      <span style={{ color: "#ff7730" }}>$</span> npx -y neuralrig-mcp install
                      --client all
                    </div>

                    <div
                      style={{
                        marginTop: 14,
                        display: "flex",
                        flexDirection: "column",
                        gap: 8,
                      }}
                    >
                      {[
                        "01  Datasheet & Schematic Check",
                        "02  Semiconductor .MODEL Register",
                        "03  .OP / .AC / .TRAN + 0 B Gate",
                        "04  Live Hot-Swap into Signal Chain",
                      ].map((step) => (
                        <div
                          key={step}
                          style={{
                            display: "flex",
                            justifyContent: "space-between",
                            fontFamily: "var(--font-mono)",
                            fontSize: 11,
                            color: "rgba(255,255,255,0.8)",
                            padding: "6px 2px",
                            borderBottom: "1px solid rgba(255,255,255,0.07)",
                          }}
                        >
                          <span>{step}</span>
                          <span style={{ color: "#4ade80" }}>PASS</span>
                        </div>
                      ))}
                    </div>
                  </div>

                  {/* Live Community Hub Card (Wired to real Supabase hub_gears!) */}
                  <div
                    style={{
                      padding: "13px 14px",
                      borderRadius: 8,
                      background: "rgba(255, 255, 255, 0.07)",
                      border: "1px solid rgba(255, 255, 255, 0.14)",
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        fontFamily: "var(--font-mono)",
                        fontSize: 10,
                        color: "#ff9b5e",
                      }}
                    >
                      <span>LIVE COMMUNITY HUB</span>
                      <span>VERIFIED SPICE</span>
                    </div>
                    <div style={{ marginTop: 5, fontSize: 14, fontWeight: 500 }}>
                      {liveHubItem?.name || "Centaur Silver Ge MNA"}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
