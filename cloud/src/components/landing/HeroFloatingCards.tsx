"use client";

import React, { useEffect, useRef, useState } from "react";
import { HERO_VOICES, Locale, RigPresetVoice } from "./LandingData";
import {
  StudioKnobCell,
  StudioLiquidDock,
  StudioScenePill,
  StudioSlotCard,
} from "./StudioUiPrimitives";
import { GuitarAuditionController } from "./webAudioEngine";

interface HeroFloatingCardsProps {
  locale: Locale;
  ready: boolean;
}

export function HeroFloatingCards({ locale, ready }: HeroFloatingCardsProps) {
  const [voiceIdx, setVoiceIdx] = useState(0); // Default 1A Velvet Neo-Soul Clean (matches real Studio first boot!)
  const voice: RigPresetVoice = HERO_VOICES[voiceIdx];
  const [selectedSlotIdx, setSelectedSlotIdx] = useState(2); // AMP slot highlighted
  const [knobValues, setKnobValues] = useState<[number, number, number]>(
    voice.knobs.map((k) => k.val) as [number, number, number]
  );
  const [playing, setPlaying] = useState(false);

  const stageRef = useRef<HTMLDivElement | null>(null);
  const windowRef = useRef<HTMLDivElement | null>(null);
  const inspectorRef = useRef<HTMLDivElement | null>(null);
  const audioRef = useRef<GuitarAuditionController | null>(null);

  useEffect(() => {
    setKnobValues(voice.knobs.map((k) => k.val) as [number, number, number]);
  }, [voice]);

  useEffect(() => {
    audioRef.current = new GuitarAuditionController();
    return () => {
      audioRef.current?.stop();
    };
  }, []);

  useEffect(() => {
    const normalizedDrive = Math.max(0.1, Math.min(0.98, knobValues[0] / 10));
    audioRef.current?.update(voice, normalizedDrive);
  }, [voice, knobValues]);

  // Smooth 60fps pointer spring parallax across the real Studio Window & Floating Inspector Sheet
  useEffect(() => {
    let tx = 0;
    let ty = 0;
    let cx = 0;
    let cy = 0;
    let raf = 0;
    let t = 0;

    const onMove = (e: PointerEvent) => {
      const rect = stageRef.current?.getBoundingClientRect();
      if (!rect) return;
      tx = Math.max(-1, Math.min(1, ((e.clientX - rect.left) / rect.width - 0.5) * 2));
      ty = Math.max(-1, Math.min(1, ((e.clientY - rect.top) / rect.height - 0.5) * 2));
    };

    const onLeave = () => {
      tx = 0;
      ty = 0;
    };

    const loop = () => {
      t += 0.018;
      cx += (tx - cx) * 0.075;
      cy += (ty - cy) * 0.075;

      if (windowRef.current) {
        windowRef.current.style.transform = `translate3d(${(cx * -7).toFixed(2)}px, ${(
          cy * -5 +
          Math.sin(t) * 3.5
        ).toFixed(2)}px, 0)`;
      }
      if (inspectorRef.current) {
        inspectorRef.current.style.transform = `translate3d(${(cx * 13).toFixed(2)}px, ${(
          cy * 9 +
          Math.cos(t * 1.1) * 5
        ).toFixed(2)}px, 0)`;
      }
      raf = window.requestAnimationFrame(loop);
    };

    window.addEventListener("pointermove", onMove, { passive: true });
    window.addEventListener("pointerleave", onLeave);
    raf = window.requestAnimationFrame(loop);

    return () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerleave", onLeave);
      window.cancelAnimationFrame(raf);
    };
  }, []);

  const stepPreset = (dir: -1 | 1) => {
    setVoiceIdx((prev) => (prev + dir + HERO_VOICES.length) % HERO_VOICES.length);
  };

  const toggleAudio = () => {
    if (playing) {
      audioRef.current?.stop();
      setPlaying(false);
    } else {
      const drv = Math.max(0.1, Math.min(0.98, knobValues[0] / 10));
      audioRef.current?.start(voice, drv, () => setPlaying(false));
      setPlaying(true);
    }
  };

  const activeSlot = voice.row1[selectedSlotIdx] || voice.row1[2];

  return (
    <div
      ref={stageRef}
      style={{
        position: "relative",
        minHeight: 470,
        paddingBottom: 68,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        opacity: ready ? 1 : 0,
        transform: ready ? "translate3d(0,0,0)" : "translate3d(0,32px,0)",
        transition:
          "opacity 920ms cubic-bezier(0.16, 1, 0.3, 1) 120ms, transform 920ms cubic-bezier(0.16, 1, 0.3, 1) 120ms",
      }}
    >
      {/* ====================================================================
          PLANE 1: ACTUAL NEURALRIG STUDIO WORKSPACE WINDOW (step_4_shot_1)
         ==================================================================== */}
      <div
        ref={windowRef}
        style={{
          width: "100%",
          maxWidth: 630,
          borderRadius: 20,
          padding: "16px 18px 18px",
          background:
            "radial-gradient(circle at 12% 8%, rgba(255, 118, 42, 0.25) 0%, rgba(226, 232, 229, 0.92) 48%), #dde3e0",
          border: "1px solid rgba(255, 255, 255, 0.72)",
          boxShadow: "0 34px 80px rgba(11, 14, 16, 0.18)",
          willChange: "transform",
        }}
      >
        {/* Studio Top Bar: Brand + Preset Stepper + Scene A-D Pill + BPM */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: 8,
            flexWrap: "wrap",
            marginBottom: 14,
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <button
              type="button"
              onClick={() => stepPreset(-1)}
              aria-label="Previous preset"
              style={{
                width: 24,
                height: 24,
                borderRadius: "50%",
                border: "1px solid rgba(11,14,16,0.12)",
                background: "rgba(255,255,255,0.8)",
                cursor: "pointer",
                fontSize: 12,
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
              }}
            >
              ‹
            </button>
            <button
              type="button"
              onClick={() => stepPreset(1)}
              aria-label="Next preset"
              style={{
                width: 24,
                height: 24,
                borderRadius: "50%",
                border: "1px solid rgba(11,14,16,0.12)",
                background: "rgba(255,255,255,0.8)",
                cursor: "pointer",
                fontSize: 12,
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
              }}
            >
              ›
            </button>

            <button
              type="button"
              onClick={() => stepPreset(1)}
              style={{
                border: 0,
                background: "transparent",
                display: "inline-flex",
                alignItems: "baseline",
                gap: 6,
                cursor: "pointer",
                padding: "2px 4px",
              }}
            >
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
                  letterSpacing: "-0.025em",
                  color: "#0b0e10",
                }}
              >
                {voice.presetTitle}
              </span>
            </button>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <StudioScenePill activeScene={voice.scene} compact />
            <span
              style={{
                padding: "4px 9px",
                borderRadius: 999,
                background: "rgba(244,246,245,0.9)",
                border: "1px solid rgba(11,14,16,0.1)",
                fontFamily: "var(--font-mono)",
                fontSize: 9.5,
                color: "#0b0e10",
              }}
            >
              {voice.bpm} BPM · {voice.parallel ? (locale === "ko" ? "병렬" : "Parallel") : "Serial"}
            </span>
          </div>
        </div>

        {/* Centerpiece: Actual Dark Obsidian 2x8 CortexGrid Stage */}
        <div
          style={{
            position: "relative",
            borderRadius: 18,
            padding: "18px 14px",
            background:
              "radial-gradient(circle at 18% 0%, rgba(255, 118, 42, 0.24) 0%, rgba(13, 17, 19, 0) 58%), #0d1113",
            border: "1px solid rgba(255,255,255,0.1)",
            overflow: "hidden",
          }}
        >
          {/* SVG Orthogonal Parallel Routing Cable (matching step_4_shot_1.png!) */}
          <svg
            viewBox="0 0 580 156"
            preserveAspectRatio="none"
            style={{
              position: "absolute",
              inset: 0,
              width: "100%",
              height: "100%",
              pointerEvents: "none",
            }}
          >
            {/* Row 1 Horizontal Spine */}
            <line
              x1={26}
              y1={46}
              x2={554}
              y2={46}
              stroke="rgba(255,255,255,0.16)"
              strokeWidth={1.4}
            />
            {/* Orange Parallel Split & Merge Path */}
            {voice.parallel && (
              <path
                d="M 188 46 L 188 76 Q 188 84 180 84 L 78 84 Q 70 84 70 92 L 70 112 Q 70 120 78 120 L 534 120"
                fill="none"
                stroke="#ff6b24"
                strokeWidth={1.7}
                strokeDasharray={playing ? "6 4" : undefined}
              />
            )}
          </svg>

          {/* Row 1: IN + 6 Porcelain Gear Slots */}
          <div
            style={{
              position: "relative",
              zIndex: 2,
              display: "grid",
              gridTemplateColumns: "34px repeat(6, minmax(0, 1fr))",
              gap: 7,
              alignItems: "center",
            }}
          >
            <div
              style={{
                height: 54,
                borderRadius: 9,
                border: "1px solid rgba(255,255,255,0.12)",
                background: "rgba(255,255,255,0.04)",
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
            {voice.row1.map((slot, idx) => (
              <StudioSlotCard
                key={`${voice.bank}-r1-${idx}`}
                slot={slot}
                compact
                selected={idx === selectedSlotIdx}
                onClick={() => slot && setSelectedSlotIdx(idx)}
              />
            ))}
          </div>

          {/* Row 2: Parallel Row + OUT */}
          <div
            style={{
              position: "relative",
              zIndex: 2,
              marginTop: 14,
              display: "grid",
              gridTemplateColumns: "repeat(6, minmax(0, 1fr)) 36px",
              gap: 7,
              alignItems: "center",
            }}
          >
            {voice.row2.map((slot, idx) => (
              <StudioSlotCard key={`${voice.bank}-r2-${idx}`} slot={slot} compact />
            ))}
            <div
              style={{
                height: 54,
                borderRadius: 9,
                border: "1px solid rgba(255,255,255,0.12)",
                background: "rgba(255,255,255,0.04)",
                color: "rgba(255,255,255,0.75)",
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

        {/* Bottom Centered Liquid Glass Floating Dock */}
        <div style={{ marginTop: 14, display: "flex", justifyContent: "center" }}>
          <StudioLiquidDock playing={playing} onTogglePlay={toggleAudio} locale={locale} />
        </div>
      </div>

      {/* ====================================================================
          PLANE 2 (FRONT OVERLAPPING): ACTUAL 11-TICK SLOT INSPECTOR SHEET
         ==================================================================== */}
      <div
        ref={inspectorRef}
        style={{
          position: "absolute",
          right: -6,
          bottom: 0,
          width: 356,
          borderRadius: 15,
          overflow: "hidden",
          background: "rgba(247, 249, 248, 0.97)",
          backdropFilter: "blur(16px)",
          border: "1px solid rgba(11, 14, 16, 0.12)",
          boxShadow: "0 24px 56px rgba(11, 14, 16, 0.22)",
          willChange: "transform",
          zIndex: 5,
        }}
      >
        {/* Inspector Header Bar (matching step_8_shot_1.png!) */}
        <div
          style={{
            padding: "8px 12px",
            background: "rgba(226, 232, 229, 0.55)",
            borderBottom: "1px solid rgba(11, 14, 16, 0.08)",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: 8,
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 7, minWidth: 0 }}>
            <span
              style={{
                width: 20,
                height: 20,
                borderRadius: 6,
                background: "#ff6b24",
                color: "#ffffff",
                fontSize: 10,
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                flexShrink: 0,
              }}
            >
              ⏻
            </span>
            <span
              style={{
                fontFamily: "var(--font-mono)",
                fontSize: 9.5,
                padding: "2px 6px",
                borderRadius: 4,
                border: "1px solid rgba(11,14,16,0.14)",
                background: "#ffffff",
                color: "#0b0e10",
                flexShrink: 0,
              }}
            >
              {activeSlot?.cat || voice.inspectorBadge}
            </span>
            <span
              style={{
                fontSize: 12.5,
                fontWeight: 500,
                color: "#0b0e10",
                whiteSpace: "nowrap",
                overflow: "hidden",
                textOverflow: "ellipsis",
              }}
            >
              {activeSlot?.name || voice.inspectorModel}
            </span>
          </div>

          <span style={{ fontFamily: "var(--font-mono)", fontSize: 9.5, color: "#616a6c" }}>
            DRAG ↕
          </span>
        </div>

        {/* 3 Interactive 11-Tick CortexKnob Cells */}
        <div style={{ display: "grid", gridTemplateColumns: "repeat(3, minmax(0, 1fr))" }}>
          {voice.knobs.map((k, idx) => (
            <StudioKnobCell
              key={`${voice.bank}-${k.label}`}
              label={k.label}
              value={knobValues[idx]}
              unit={k.unit}
              onChange={(next) =>
                setKnobValues((prev) => {
                  const copy = [...prev] as [number, number, number];
                  copy[idx] = next;
                  return copy;
                })
              }
            />
          ))}
        </div>
      </div>
    </div>
  );
}
