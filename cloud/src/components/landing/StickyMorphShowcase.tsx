"use client";

import React, { useEffect, useRef, useState } from "react";
import { HERO_VOICES, Locale, MINIMAL_COPY } from "./LandingData";
import {
  StudioKnobCell,
  StudioLiquidDock,
  StudioScenePill,
  StudioSlotCard,
} from "./StudioUiPrimitives";
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

const FFT_FREQS = ["80Hz", "250Hz", "800Hz", "2.5kHz", "8kHz"];
const DI_STEPS = ["1. 무음", "2. E2", "3. D3", "4. B3", "5. E4", "6. 스트럼"];

export function StickyMorphShowcase({
  locale,
  trackRef,
  liveHubItem,
}: StickyMorphShowcaseProps) {
  const t = MINIMAL_COPY[locale];
  const [chapter, setChapter] = useState<0 | 1 | 2>(0);
  const [progress, setProgress] = useState(0);
  const [bankIdx, setBankIdx] = useState(0);
  const [selectedSlot, setSelectedSlot] = useState(2);
  const [knobVals, setKnobVals] = useState<[number, number, number]>([4.6, 6.4, 5.5]);
  const [playing, setPlaying] = useState(false);
  const [copied, setCopied] = useState(false);
  const [fftTick, setFftTick] = useState(0);

  const windowFrameRef = useRef<HTMLDivElement | null>(null);
  const audioRef = useRef<GuitarAuditionController | null>(null);

  const voice = HERO_VOICES[bankIdx];

  useEffect(() => {
    setKnobVals(voice.knobs.map((k) => k.val) as [number, number, number]);
  }, [voice]);

  useEffect(() => {
    audioRef.current = new GuitarAuditionController();
    return () => {
      audioRef.current?.stop();
    };
  }, []);

  useEffect(() => {
    audioRef.current?.update(voice, Math.max(0.1, Math.min(0.98, knobVals[0] / 10)));
  }, [voice, knobVals]);

  // Animate 28-band Harmonic Spectrum bars inside Chapter 1
  useEffect(() => {
    const id = window.setInterval(() => {
      setFftTick((prev) => prev + 1);
    }, 140);
    return () => window.clearInterval(id);
  }, []);

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
      target = Math.max(0, Math.min(1, -rect.top / scrollable));
    };

    const loop = () => {
      computeTarget();
      cur += (target - cur) * 0.11;
      setProgress(cur);

      const nextChap: 0 | 1 | 2 = cur < 0.34 ? 0 : cur < 0.68 ? 1 : 2;
      setChapter((prev) => (prev === nextChap ? prev : nextChap));

      if (windowFrameRef.current) {
        const zoom = 1 - Math.sin(cur * Math.PI) * 0.035;
        const shiftY = (0.5 - cur) * 6;
        windowFrameRef.current.style.transform = `translate3d(0, ${shiftY.toFixed(1)}px, 0) scale(${zoom.toFixed(3)})`;
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
    window.scrollTo({
      top: el.offsetTop + scrollable * fractions[idx],
      behavior: "smooth",
    });
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
      audioRef.current?.start(
        voice,
        Math.max(0.1, Math.min(0.98, knobVals[0] / 10)),
        () => setPlaying(false)
      );
      setPlaying(true);
    }
  };

  const activeCopy = t.stickyChapters[chapter];
  const activeSlot = voice.row1[selectedSlot] || voice.row1[2];

  return (
    <section ref={trackRef as React.RefObject<HTMLElement>} className="nr-sticky-track">
      <div className="nr-sticky-pin">
        {/* Top Minimalist 3-Dot Workspace Jump Strip */}
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

        <div className="nr-sticky-body">
          {/* Left Airy Editorial Column */}
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

              <div
                style={{
                  fontFamily: "var(--font-display)",
                  fontSize: 19.5,
                  fontWeight: 300,
                  letterSpacing: "-0.02em",
                }}
              >
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

          {/* Right Slate Viewport Framing the Actual Wide Desktop Studio Window */}
          <div className="nr-sticky-viewport" style={{ padding: "22px" }}>
            <style>{`
              @keyframes nrFadeSlideIn {
                from { opacity: 0; transform: translate3d(0, 14px, 0); }
                to { opacity: 1; transform: translate3d(0, 0, 0); }
              }
            `}</style>

            <div
              ref={windowFrameRef}
              style={{
                width: "100%",
                maxWidth: 680,
                height: 462,
                borderRadius: 18,
                overflow: "hidden",
                background: "#d5dcd9",
                border: "1px solid rgba(255, 255, 255, 0.45)",
                boxShadow: "0 36px 84px rgba(0, 0, 0, 0.42)",
                position: "relative",
                willChange: "transform",
              }}
            >
              {/* 3-Screen Vertical Morphing Reel of Actual NeuralRig Studio Screens */}
              <div
                style={{
                  height: "300%",
                  width: "100%",
                  transform: `translate3d(0, -${(chapter * 100) / 3}%, 0)`,
                  transition: "transform 680ms cubic-bezier(0.22, 1, 0.36, 1)",
                }}
              >
                {/* ==========================================================
                    SCREEN 0: ACTUAL MAIN STUDIO + FLOATING 11-TICK INSPECTOR
                   ========================================================== */}
                <div
                  style={{
                    height: "33.3333%",
                    padding: "16px 18px 14px",
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    background:
                      "radial-gradient(circle at 10% 6%, rgba(255, 118, 42, 0.28) 0%, rgba(213, 220, 217, 0) 52%), #d5dcd9",
                  }}
                >
                  {/* Top Studio Header */}
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      gap: 8,
                    }}
                  >
                    <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                      <button
                        type="button"
                        onClick={() => setBankIdx((p) => (p + 1) % HERO_VOICES.length)}
                        style={{
                          width: 24,
                          height: 24,
                          borderRadius: "50%",
                          border: "1px solid rgba(11,14,16,0.12)",
                          background: "#f4f6f5",
                          cursor: "pointer",
                        }}
                      >
                        ›
                      </button>
                      <span
                        style={{
                          fontFamily: "var(--font-mono)",
                          fontSize: 10.5,
                          fontWeight: 700,
                          color: "#ff6b24",
                        }}
                      >
                        {voice.bank}
                      </span>
                      <span
                        style={{
                          fontFamily: "var(--font-display)",
                          fontSize: 17,
                          fontWeight: 300,
                          color: "#0b0e10",
                        }}
                      >
                        {voice.presetTitle}
                      </span>
                    </div>

                    <StudioScenePill activeScene={voice.scene} />
                  </div>

                  {/* Center Obsidian 2x8 CortexGrid */}
                  <div
                    style={{
                      position: "relative",
                      borderRadius: 16,
                      padding: "14px 12px",
                      background:
                        "radial-gradient(circle at 18% 0%, rgba(255, 118, 42, 0.22) 0%, rgba(13, 17, 19, 0) 56%), #0d1113",
                    }}
                  >
                    <svg
                      viewBox="0 0 620 132"
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
                        x1={22}
                        y1={38}
                        x2={598}
                        y2={38}
                        stroke="rgba(255,255,255,0.16)"
                        strokeWidth={1.4}
                      />
                      {voice.parallel && (
                        <path
                          d="M 196 38 L 196 66 L 82 66 L 82 100 L 580 100"
                          fill="none"
                          stroke="#ff6b24"
                          strokeWidth={1.6}
                        />
                      )}
                    </svg>

                    <div
                      style={{
                        position: "relative",
                        zIndex: 2,
                        display: "grid",
                        gridTemplateColumns: "32px repeat(6, minmax(0, 1fr))",
                        gap: 6,
                      }}
                    >
                      <div
                        style={{
                          height: 54,
                          borderRadius: 9,
                          border: "1px solid rgba(255,255,255,0.12)",
                          color: "rgba(255,255,255,0.7)",
                          fontFamily: "var(--font-mono)",
                          fontSize: 9,
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "center",
                        }}
                      >
                        IN
                      </div>
                      {voice.row1.map((s, i) => (
                        <StudioSlotCard
                          key={i}
                          slot={s}
                          compact
                          selected={i === selectedSlot}
                          onClick={() => s && setSelectedSlot(i)}
                        />
                      ))}
                    </div>

                    <div
                      style={{
                        position: "relative",
                        zIndex: 2,
                        marginTop: 10,
                        display: "grid",
                        gridTemplateColumns: "repeat(6, minmax(0, 1fr)) 34px",
                        gap: 6,
                      }}
                    >
                      {voice.row2.map((s, i) => (
                        <StudioSlotCard key={i} slot={s} compact />
                      ))}
                      <div
                        style={{
                          height: 54,
                          borderRadius: 9,
                          border: "1px solid rgba(255,255,255,0.12)",
                          color: "rgba(255,255,255,0.7)",
                          fontFamily: "var(--font-mono)",
                          fontSize: 8.5,
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "center",
                        }}
                      >
                        OUT
                      </div>
                    </div>
                  </div>

                  {/* Floating Porcelain Inspector Sheet + Bottom Liquid Glass Dock */}
                  <div
                    style={{
                      borderRadius: 12,
                      overflow: "hidden",
                      background: "#f7f9f8",
                      border: "1px solid rgba(11,14,16,0.1)",
                      boxShadow: "0 12px 28px rgba(11,14,16,0.1)",
                    }}
                  >
                    <div
                      style={{
                        padding: "6px 12px",
                        background: "#e7ecea",
                        borderBottom: "1px solid rgba(11,14,16,0.08)",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "space-between",
                        fontSize: 11.5,
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
                        <strong>{activeSlot?.name || voice.inspectorModel}</strong>
                      </div>
                      <span
                        style={{
                          fontFamily: "var(--font-mono)",
                          fontSize: 9.5,
                          color: "#616a6c",
                        }}
                      >
                        11-TICK ROTARY INSPECTOR
                      </span>
                    </div>
                    <div style={{ display: "grid", gridTemplateColumns: "repeat(3, 1fr)" }}>
                      {voice.knobs.map((k, idx) => (
                        <StudioKnobCell
                          key={k.label}
                          label={k.label}
                          value={knobVals[idx]}
                          unit={k.unit}
                          onChange={(v) =>
                            setKnobVals((prev) => {
                              const next = [...prev] as [number, number, number];
                              next[idx] = v;
                              return next;
                            })
                          }
                        />
                      ))}
                    </div>
                  </div>

                  <div style={{ display: "flex", justifyContent: "center" }}>
                    <StudioLiquidDock
                      playing={playing}
                      onTogglePlay={handlePrimaryAction}
                      locale={locale}
                    />
                  </div>
                </div>

                {/* ==========================================================
                    SCREEN 1: ACTUAL HARMONIC SPECTRUM · SCOPE + DI PROFILER (step_16)
                   ========================================================== */}
                <div
                  style={{
                    height: "33.3333%",
                    padding: "16px 18px",
                    display: "flex",
                    flexDirection: "column",
                    justifyContent: "space-between",
                    background:
                      "radial-gradient(circle at 12% 8%, rgba(255, 118, 42, 0.25) 0%, rgba(213, 220, 217, 0) 50%), #d5dcd9",
                  }}
                >
                  {/* Top Lab Header (matching final_step_16.png!) */}
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                    }}
                  >
                    <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                      <span
                        style={{
                          padding: "4px 10px",
                          borderRadius: 999,
                          background: "#f4f6f5",
                          border: "1px solid rgba(11,14,16,0.1)",
                          fontSize: 10.5,
                        }}
                      >
                        ← {locale === "ko" ? "스튜디오로 돌아가기" : "Back to Studio"}
                      </span>
                      <span
                        style={{
                          fontFamily: "var(--font-display)",
                          fontSize: 16.5,
                          fontWeight: 300,
                        }}
                      >
                        {locale === "ko" ? "스펙트럼 · 기타 DI 분석" : "Harmonic Spectrum & DI Lab"}
                      </span>
                    </div>
                    <span
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 9.5,
                        padding: "3px 8px",
                        borderRadius: 999,
                        background: "rgba(21,128,61,0.14)",
                        color: "#15803d",
                        fontWeight: 600,
                      }}
                    >
                      LIVE ENGINE
                    </span>
                  </div>

                  {/* 2-Column Split: Left Obsidian 36-Band Spectrum / Right DI Profiler */}
                  <div
                    style={{
                      flex: 1,
                      marginTop: 12,
                      display: "grid",
                      gridTemplateColumns: "1.2fr 0.95fr",
                      gap: 12,
                      minHeight: 0,
                    }}
                  >
                    {/* Left: HARMONIC SPECTRUM · SCOPE */}
                    <div
                      style={{
                        borderRadius: 14,
                        padding: "13px 14px",
                        background:
                          "radial-gradient(circle at 18% 12%, rgba(255, 118, 42, 0.28) 0%, rgba(13, 17, 19, 0) 58%), #0d1113",
                        color: "#f5f7f6",
                        display: "flex",
                        flexDirection: "column",
                        justifyContent: "space-between",
                      }}
                    >
                      <div
                        style={{
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "space-between",
                          fontFamily: "var(--font-mono)",
                          fontSize: 9.5,
                        }}
                      >
                        <span style={{ letterSpacing: "0.06em" }}>HARMONIC SPECTRUM · SCOPE</span>
                        <div
                          style={{
                            display: "inline-flex",
                            gap: 4,
                            background: "rgba(255,255,255,0.08)",
                            padding: 2,
                            borderRadius: 5,
                          }}
                        >
                          <span
                            style={{
                              padding: "2px 6px",
                              borderRadius: 3,
                              background: "#ffffff",
                              color: "#0b0e10",
                              fontWeight: 600,
                            }}
                          >
                            {locale === "ko" ? "복합" : "MIX"}
                          </span>
                          <span style={{ padding: "2px 6px", color: "rgba(255,255,255,0.6)" }}>
                            FFT
                          </span>
                          <span style={{ padding: "2px 6px", color: "rgba(255,255,255,0.6)" }}>
                            {locale === "ko" ? "파형" : "WAVE"}
                          </span>
                        </div>
                      </div>

                      {/* L/R Level Bars */}
                      <div style={{ display: "flex", flexDirection: "column", gap: 4, marginTop: 8 }}>
                        {["L", "R"].map((ch, idx) => (
                          <div
                            key={ch}
                            style={{
                              display: "flex",
                              alignItems: "center",
                              gap: 6,
                              fontFamily: "var(--font-mono)",
                              fontSize: 8.5,
                              color: "rgba(255,255,255,0.5)",
                            }}
                          >
                            <span>{ch}</span>
                            <div
                              style={{
                                flex: 1,
                                height: 4,
                                borderRadius: 99,
                                background: "rgba(255,255,255,0.08)",
                                overflow: "hidden",
                              }}
                            >
                              <div
                                style={{
                                  height: "100%",
                                  width: `${68 + Math.sin(fftTick * 0.6 + idx) * 14}%`,
                                  background: "linear-gradient(90deg, #22c55e, #ff6b24)",
                                  transition: "width 140ms linear",
                                }}
                              />
                            </div>
                            <span>-6.2dB</span>
                          </div>
                        ))}
                      </div>

                      {/* Live 24-Band Spectrum Analyzer */}
                      <div
                        style={{
                          height: 178,
                          display: "grid",
                          gridTemplateColumns: "repeat(24, 1fr)",
                          gap: 3,
                          alignItems: "end",
                          padding: "14px 0 6px",
                          borderBottom: "1px solid rgba(255,255,255,0.1)",
                        }}
                      >
                        {Array.from({ length: 24 }).map((_, i) => {
                          const bell = Math.exp(-Math.pow((i - 8) / 6.5, 2));
                          const wave = Math.abs(Math.sin(fftTick * 0.35 + i * 0.55)) * 0.34;
                          const pct = Math.max(8, Math.min(96, Math.round((bell * 0.68 + wave) * 100)));
                          return (
                            <div
                              key={i}
                              style={{
                                height: `${pct}%`,
                                borderRadius: "2px 2px 0 0",
                                background:
                                  i >= 5 && i <= 12
                                    ? "#ff6b24"
                                    : "rgba(245, 247, 246, 0.42)",
                                transition: "height 140ms linear",
                              }}
                            />
                          );
                        })}
                      </div>

                      <div
                        style={{
                          display: "flex",
                          justifyContent: "space-between",
                          fontFamily: "var(--font-mono)",
                          fontSize: 8.5,
                          color: "rgba(255,255,255,0.45)",
                        }}
                      >
                        {FFT_FREQS.map((f) => (
                          <span key={f}>{f}</span>
                        ))}
                      </div>
                    </div>

                    {/* Right: GUITAR DI PROFILER Porcelain Card */}
                    <div
                      style={{
                        borderRadius: 14,
                        padding: "13px 14px",
                        background: "#f7f9f8",
                        border: "1px solid rgba(11,14,16,0.1)",
                        color: "#0b0e10",
                        display: "flex",
                        flexDirection: "column",
                        justifyContent: "space-between",
                      }}
                    >
                      <div>
                        <div
                          style={{
                            display: "flex",
                            justifyContent: "space-between",
                            alignItems: "center",
                            paddingBottom: 9,
                            borderBottom: "1px solid rgba(11,14,16,0.08)",
                          }}
                        >
                          <span
                            style={{
                              fontFamily: "var(--font-mono)",
                              fontSize: 10,
                              fontWeight: 700,
                              letterSpacing: "0.06em",
                            }}
                          >
                            GUITAR DI PROFILER
                          </span>
                          <span
                            style={{
                              fontFamily: "var(--font-mono)",
                              fontSize: 8.5,
                              padding: "2px 6px",
                              borderRadius: 999,
                              border: "1px solid rgba(11,14,16,0.14)",
                            }}
                          >
                            READY
                          </span>
                        </div>

                        <div
                          style={{
                            marginTop: 10,
                            padding: "7px 10px",
                            borderRadius: 7,
                            background: "#e7ecea",
                            fontSize: 11.5,
                            fontWeight: 500,
                          }}
                        >
                          Custom Strat · Neck Single
                        </div>

                        <div
                          style={{
                            marginTop: 12,
                            fontFamily: "var(--font-mono)",
                            fontSize: 9.5,
                            color: "#616a6c",
                          }}
                        >
                          {locale === "ko"
                            ? "6단계 픽업 측정 시퀀스 (약 12초)"
                            : "6-Step Pickup Sequence (12s)"}
                        </div>

                        <div
                          style={{
                            marginTop: 7,
                            display: "grid",
                            gridTemplateColumns: "repeat(3, 1fr)",
                            gap: 5,
                          }}
                        >
                          {DI_STEPS.map((st, idx) => {
                            const active = idx === fftTick % 6;
                            return (
                              <div
                                key={st}
                                style={{
                                  padding: "7px 4px",
                                  textAlign: "center",
                                  borderRadius: 6,
                                  fontFamily: "var(--font-mono)",
                                  fontSize: 9.5,
                                  fontWeight: 600,
                                  background: active ? "#0b0e10" : "#ffffff",
                                  color: active ? "#ffffff" : "#0b0e10",
                                  border: "1px solid rgba(11,14,16,0.1)",
                                }}
                              >
                                {st}
                              </div>
                            );
                          })}
                        </div>
                      </div>

                      <div
                        style={{
                          padding: "9px 11px",
                          borderRadius: 8,
                          background: "#ff6b24",
                          color: "#ffffff",
                          fontSize: 11.5,
                          fontWeight: 600,
                          textAlign: "center",
                        }}
                      >
                        {locale === "ko" ? "기타 DI 분석 및 AI 매칭" : "Analyze Pickup & Match Tone"}
                      </div>
                    </div>
                  </div>
                </div>

                {/* ==========================================================
                    SCREEN 2: ACTUAL CRAFT AI MCP DIALOG + TONE HUB (step_14)
                   ========================================================== */}
                <div
                  style={{
                    height: "33.3333%",
                    padding: "18px 20px",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    background:
                      "radial-gradient(circle at 80% 16%, rgba(255, 118, 42, 0.24) 0%, rgba(13, 17, 19, 0.4) 64%), #22282a",
                  }}
                >
                  {/* Authentic Porcelain CustomGearGuideDialog Modal */}
                  <div
                    style={{
                      width: "100%",
                      borderRadius: 15,
                      overflow: "hidden",
                      background: "#f7f9f8",
                      color: "#0b0e10",
                      border: "1px solid rgba(255,255,255,0.5)",
                      boxShadow: "0 24px 60px rgba(0,0,0,0.38)",
                    }}
                  >
                    {/* Modal Header */}
                    <div
                      style={{
                        padding: "11px 16px",
                        background: "#e9eeec",
                        borderBottom: "1px solid rgba(11,14,16,0.08)",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "space-between",
                      }}
                    >
                      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                        <span
                          style={{
                            padding: "2px 7px",
                            borderRadius: 4,
                            background: "#ff6b24",
                            color: "#fff",
                            fontFamily: "var(--font-mono)",
                            fontSize: 9.5,
                            fontWeight: 700,
                          }}
                        >
                          CRAFT · MCP
                        </span>
                        <strong style={{ fontSize: 13 }}>
                          {locale === "ko"
                            ? "새 페달 · 앰프 · 캐비넷 생성 & AI MCP 연동"
                            : "Custom SPICE Gear & Multi-AI MCP Pipeline"}
                        </strong>
                      </div>
                      <span
                        style={{
                          fontFamily: "var(--font-mono)",
                          fontSize: 9.5,
                          color: "#15803d",
                          fontWeight: 600,
                        }}
                      >
                        ● 7 AI CLIENTS
                      </span>
                    </div>

                    <div style={{ padding: "14px 16px", display: "flex", flexDirection: "column", gap: 11 }}>
                      {/* Anti-Hallucination Notice Banner from CustomGearGuideDialog */}
                      <div
                        style={{
                          padding: "8px 11px",
                          borderRadius: 8,
                          background: "rgba(255, 107, 36, 0.1)",
                          border: "1px solid rgba(255, 107, 36, 0.28)",
                          fontSize: 11.5,
                          color: "#9a3412",
                          fontWeight: 500,
                        }}
                      >
                        {locale === "ko"
                          ? "할루시네이션 차단 원칙 — 검증 가능한 실제 회로도·데이터시트가 없으면 추측 생성을 즉시 중단합니다."
                          : "Honest Stop Policy — Halts immediately if no verifiable schematic or datasheet exists."}
                      </div>

                      {/* 5 Horizontal Step Cards */}
                      <div
                        style={{
                          display: "grid",
                          gridTemplateColumns: "repeat(5, minmax(0, 1fr))",
                          gap: 6,
                        }}
                      >
                        {[
                          { s: "01", t: "SPICE 규격" },
                          { s: "02", t: "실제 회로도" },
                          { s: "03", t: ".MODEL 등록" },
                          { s: "04", t: "0 B 시뮬 검증" },
                          { s: "05", t: "즉시 핫스왑" },
                        ].map((st) => (
                          <div
                            key={st.s}
                            style={{
                              padding: "8px 7px",
                              borderRadius: 8,
                              background: "#ffffff",
                              border: "1px solid rgba(11,14,16,0.09)",
                            }}
                          >
                            <div
                              style={{
                                fontFamily: "var(--font-mono)",
                                fontSize: 9,
                                fontWeight: 700,
                                color: "#ff6b24",
                              }}
                            >
                              STEP {st.s}
                            </div>
                            <div style={{ fontSize: 11, fontWeight: 600, marginTop: 3 }}>
                              {st.t}
                            </div>
                          </div>
                        ))}
                      </div>

                      {/* Dark CLI Install Strip */}
                      <div
                        style={{
                          padding: "10px 13px",
                          borderRadius: 8,
                          background: "#0d1113",
                          color: "#f5f7f6",
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "space-between",
                          fontFamily: "var(--font-mono)",
                          fontSize: 11.5,
                        }}
                      >
                        <span>
                          <span style={{ color: "#ff6b24" }}>$</span> npx -y neuralrig-mcp install
                          --client all
                        </span>
                        <button
                          type="button"
                          onClick={handlePrimaryAction}
                          style={{
                            border: 0,
                            borderRadius: 4,
                            padding: "4px 9px",
                            background: copied ? "#22c55e" : "#ff6b24",
                            color: "#ffffff",
                            fontFamily: "var(--font-mono)",
                            fontSize: 9.5,
                            fontWeight: 600,
                            cursor: "pointer",
                          }}
                        >
                          {copied ? "COPIED" : "COPY"}
                        </button>
                      </div>

                      {/* Live Community Gear Hub Row */}
                      <div
                        style={{
                          padding: "9px 12px",
                          borderRadius: 8,
                          background: "#ffffff",
                          border: "1px solid rgba(11,14,16,0.09)",
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "space-between",
                          fontSize: 12,
                        }}
                      >
                        <div>
                          <span
                            style={{
                              fontFamily: "var(--font-mono)",
                              fontSize: 9.5,
                              color: "#ff6b24",
                              fontWeight: 700,
                              marginRight: 8,
                            }}
                          >
                            TONE HUB LIVE
                          </span>
                          <strong>{liveHubItem?.name || "BOSS MD-2 Mega Distortion"}</strong>
                        </div>
                        <span
                          style={{
                            fontFamily: "var(--font-mono)",
                            fontSize: 9.5,
                            color: "#15803d",
                            fontWeight: 600,
                          }}
                        >
                          SPICE 0 B VERIFIED ✓
                        </span>
                      </div>
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
