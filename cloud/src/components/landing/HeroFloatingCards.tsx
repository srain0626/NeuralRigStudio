"use client";

import React, { useEffect, useRef, useState } from "react";
import { HERO_VOICES, Locale, RigPresetVoice } from "./LandingData";
import { GuitarAuditionController } from "./webAudioEngine";

interface HeroFloatingCardsProps {
  locale: Locale;
  ready: boolean;
}

export function HeroFloatingCards({ locale, ready }: HeroFloatingCardsProps) {
  const [voiceIndex, setVoiceIndex] = useState(1); // 1C Asym Crunch default
  const voice: RigPresetVoice = HERO_VOICES[voiceIndex];
  const [drive, setDrive] = useState(voice.drive);
  const [playing, setPlaying] = useState(false);
  const [latencyTick, setLatencyTick] = useState("0.054");

  const stageRef = useRef<HTMLDivElement | null>(null);
  const backCardRef = useRef<HTMLDivElement | null>(null);
  const frontCardRef = useRef<HTMLDivElement | null>(null);
  const scopeRef = useRef<HTMLCanvasElement | null>(null);
  const audioCtrlRef = useRef<GuitarAuditionController | null>(null);
  const liveParamsRef = useRef({ drive, asymmetry: voice.asymmetry, playing });

  useEffect(() => {
    setDrive(voice.drive);
  }, [voice]);

  useEffect(() => {
    liveParamsRef.current = { drive, asymmetry: voice.asymmetry, playing };
    audioCtrlRef.current?.update(voice, drive);
  }, [drive, voice, playing]);

  useEffect(() => {
    audioCtrlRef.current = new GuitarAuditionController();
    return () => {
      audioCtrlRef.current?.stop();
    };
  }, []);

  // Subtle live micro-telemetry jitter (0.052 ~ 0.055 ms, mirroring live $122.10 counter in reference)
  useEffect(() => {
    const id = window.setInterval(() => {
      const jitter = 0.052 + Math.random() * 0.004;
      setLatencyTick(jitter.toFixed(3));
    }, 1500);
    return () => window.clearInterval(id);
  }, []);

  // Smooth 60fps pointer spring parallax + ambient float across the 2 card planes
  useEffect(() => {
    let targetX = 0;
    let targetY = 0;
    let curX = 0;
    let curY = 0;
    let raf = 0;
    let t = 0;

    const onMove = (e: PointerEvent) => {
      const rect = stageRef.current?.getBoundingClientRect();
      if (!rect) return;
      const nx = ((e.clientX - rect.left) / rect.width - 0.5) * 2;
      const ny = ((e.clientY - rect.top) / rect.height - 0.5) * 2;
      targetX = Math.max(-1, Math.min(1, nx));
      targetY = Math.max(-1, Math.min(1, ny));
    };

    const onLeave = () => {
      targetX = 0;
      targetY = 0;
    };

    const tick = () => {
      t += 0.02;
      curX += (targetX - curX) * 0.075;
      curY += (targetY - curY) * 0.075;

      const idleY1 = Math.sin(t) * 4;
      const idleY2 = Math.cos(t * 1.15) * 6;

      if (backCardRef.current) {
        backCardRef.current.style.transform = `translate3d(${(curX * -9).toFixed(2)}px, ${(
          curY * -7 +
          idleY1
        ).toFixed(2)}px, 0)`;
      }
      if (frontCardRef.current) {
        frontCardRef.current.style.transform = `translate3d(${(curX * 15).toFixed(2)}px, ${(
          curY * 11 +
          idleY2
        ).toFixed(2)}px, 0)`;
      }
      raf = window.requestAnimationFrame(tick);
    };

    window.addEventListener("pointermove", onMove, { passive: true });
    window.addEventListener("pointerleave", onLeave);
    raf = window.requestAnimationFrame(tick);

    return () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerleave", onLeave);
      window.cancelAnimationFrame(raf);
    };
  }, []);

  // 60fps Glowing Waveform inside Floating Front Modal
  useEffect(() => {
    const canvas = scopeRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    let raf = 0;
    let phase = 0;

    const draw = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const w = canvas.clientWidth;
      const h = canvas.clientHeight;
      if (canvas.width !== Math.floor(w * dpr) || canvas.height !== Math.floor(h * dpr)) {
        canvas.width = Math.floor(w * dpr);
        canvas.height = Math.floor(h * dpr);
      }

      ctx.save();
      ctx.scale(dpr, dpr);
      ctx.clearRect(0, 0, w, h);

      // Center hairline
      ctx.strokeStyle = "rgba(255,255,255,0.08)";
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(0, h / 2);
      ctx.lineTo(w, h / 2);
      ctx.stroke();

      const { drive: drv, asymmetry: asym, playing: isPlay } = liveParamsRef.current;
      phase += isPlay ? 0.095 : 0.034;

      // Soft Dry Sine Shadow
      ctx.strokeStyle = "rgba(255,255,255,0.18)";
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      for (let x = 0; x <= w; x += 2) {
        const u = (x / w) * Math.PI * 3.5 + phase;
        const y = h / 2 - Math.sin(u) * (h * 0.32);
        if (x === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
      }
      ctx.stroke();

      // Solved MNA Clipped Wave
      ctx.strokeStyle = "#ff7730";
      ctx.lineWidth = 2.1;
      ctx.beginPath();
      const pre = 1 + drv * 10.5;
      const bias = asym * 0.48;
      for (let x = 0; x <= w; x += 2) {
        const u = (x / w) * Math.PI * 3.5 + phase;
        const raw = Math.sin(u) * 0.82 + Math.sin(u * 2 + 0.3) * 0.14;
        const s = raw * pre + bias;
        const c = s >= 0 ? Math.tanh(s) : Math.tanh(s * (1 + asym * 1.4)) / (1 + asym * 0.35);
        const norm = Math.max(-0.94, Math.min(0.94, c - Math.tanh(bias) * 0.52));
        const y = h / 2 - norm * (h * 0.37);
        if (x === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
      }
      ctx.stroke();

      ctx.restore();
      raf = window.requestAnimationFrame(draw);
    };

    raf = window.requestAnimationFrame(draw);
    return () => window.cancelAnimationFrame(raf);
  }, []);

  const togglePlay = () => {
    if (playing) {
      audioCtrlRef.current?.stop();
      setPlaying(false);
    } else {
      audioCtrlRef.current?.start(voice, drive, () => setPlaying(false));
      setPlaying(true);
    }
  };

  return (
    <div
      ref={stageRef}
      style={{
        position: "relative",
        minHeight: 450,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        opacity: ready ? 1 : 0,
        transform: ready ? "translate3d(0,0,0)" : "translate3d(0,36px,0)",
        transition:
          "opacity 950ms cubic-bezier(0.16, 1, 0.3, 1) 140ms, transform 950ms cubic-bezier(0.16, 1, 0.3, 1) 140ms",
      }}
    >
      {/* ====================================================================
          PLANE 1 (BACK): ARCHITECTURAL STUDIO TELEMETRY & RIG CARD
         ==================================================================== */}
      <div
        ref={backCardRef}
        style={{
          width: "100%",
          maxWidth: 382,
          borderRadius: 12,
          overflow: "hidden",
          background: "#ecefed",
          boxShadow: "0 30px 70px rgba(11, 14, 16, 0.16)",
          border: "1px solid rgba(11, 14, 16, 0.08)",
          marginRight: 148,
          willChange: "transform",
        }}
      >
        {/* Warm Tangerine-Mist Top Surface */}
        <div
          style={{
            padding: "22px 22px 20px",
            background:
              "radial-gradient(circle at 92% 8%, rgba(255, 118, 42, 0.85) 0%, rgba(247, 160, 96, 0.36) 44%, rgba(236, 239, 237, 0.95) 78%)",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              fontSize: 12,
              color: "#22282a",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: 9 }}>
              <span
                style={{
                  width: 28,
                  height: 28,
                  borderRadius: "50%",
                  background: "#0d1113",
                  color: "#ffffff",
                  display: "inline-flex",
                  alignItems: "center",
                  justifyContent: "center",
                  fontFamily: "var(--font-mono)",
                  fontSize: 10.5,
                }}
              >
                {voice.bank}
              </span>
              <div>
                <div style={{ fontWeight: 500, lineHeight: 1.2 }}>
                  {locale === "ko" ? voice.labelKo : voice.labelEn}
                </div>
                <div
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 10.5,
                    color: "#5c6466",
                  }}
                >
                  {voice.circuit}
                </div>
              </div>
            </div>
          </div>

          {/* Large Clean Live Latency Readout */}
          <div style={{ marginTop: 42 }}>
            <div
              style={{
                fontSize: 42,
                fontWeight: 350,
                letterSpacing: "-0.04em",
                color: "#0b0e10",
                fontVariantNumeric: "tabular-nums",
                lineHeight: 1,
              }}
            >
              {latencyTick}
              <span style={{ fontSize: 22, opacity: 0.68, marginLeft: 4 }}>ms</span>
            </div>
            <div
              style={{
                marginTop: 8,
                display: "inline-flex",
                alignItems: "center",
                gap: 6,
                fontFamily: "var(--font-mono)",
                fontSize: 11,
                color: "#15803d",
              }}
            >
              <span>●</span>
              <span>0 B RT Heap Alloc</span>
            </div>
          </div>
        </div>

        {/* 3 Crisp White Voice Tiles */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(3, 1fr)",
            background: "#f7f9f8",
            borderTop: "1px solid rgba(11, 14, 16, 0.07)",
            borderBottom: "1px solid rgba(11, 14, 16, 0.07)",
          }}
        >
          {HERO_VOICES.map((v, idx) => {
            const active = idx === voiceIndex;
            return (
              <button
                key={v.id}
                type="button"
                onClick={() => setVoiceIndex(idx)}
                style={{
                  border: 0,
                  borderRight: idx < 2 ? "1px solid rgba(11, 14, 16, 0.07)" : 0,
                  background: active ? "#ffffff" : "transparent",
                  padding: "15px 8px",
                  cursor: "pointer",
                  display: "flex",
                  flexDirection: "column",
                  alignItems: "center",
                  gap: 6,
                  color: active ? "#0b0e10" : "#687173",
                  transition: "background-color 160ms ease, color 160ms ease",
                }}
              >
                <span
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 11,
                    padding: "2px 6px",
                    borderRadius: 2,
                    background: active ? "#0b0e10" : "rgba(11,14,16,0.06)",
                    color: active ? "#ffffff" : "#454d4f",
                  }}
                >
                  {v.bank}
                </span>
                <span style={{ fontSize: 11.5, fontWeight: active ? 550 : 450 }}>
                  {locale === "ko" ? v.labelKo : v.labelEn}
                </span>
              </button>
            );
          })}
        </div>

        {/* Bottom Obsidian Rows */}
        <div style={{ background: "#0d1113", color: "#f5f7f6", padding: "6px 18px" }}>
          <div
            style={{
              padding: "14px 2px",
              borderBottom: "1px solid rgba(255,255,255,0.08)",
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
            }}
          >
            <div>
              <div style={{ fontSize: 14, fontWeight: 450 }}>SPICE MNA Solver</div>
              <div style={{ fontSize: 11.5, color: "rgba(255,255,255,0.5)", marginTop: 2 }}>
                {voice.metricLabel}: {voice.metricValue}
              </div>
            </div>
            <span style={{ fontFamily: "var(--font-mono)", fontSize: 12, color: "#ff7730" }}>
              98.2%
            </span>
          </div>

          <div
            style={{
              padding: "14px 2px",
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
            }}
          >
            <div>
              <div style={{ fontSize: 14, fontWeight: 450 }}>2×8 Parallel .NAM</div>
              <div style={{ fontSize: 11.5, color: "rgba(255,255,255,0.5)", marginTop: 2 }}>
                4× HIIR · 16,384-Tap Cab IR
              </div>
            </div>
            <span style={{ fontFamily: "var(--font-mono)", fontSize: 12, opacity: 0.7 }}>→</span>
          </div>
        </div>
      </div>

      {/* ====================================================================
          PLANE 2 (FRONT OVERLAPPING): LIVE WAVEFORM & AUDITION MODAL
         ==================================================================== */}
      <div
        ref={frontCardRef}
        style={{
          position: "absolute",
          right: 0,
          top: "2%",
          width: 252,
          borderRadius: 10,
          padding: "14px 15px 13px",
          background:
            "radial-gradient(circle at 12% 8%, rgba(255, 118, 42, 0.34) 0%, rgba(16, 21, 24, 0.96) 52%), #101518",
          border: "1px solid rgba(255, 255, 255, 0.13)",
          boxShadow: "0 28px 60px rgba(7, 10, 12, 0.44)",
          color: "#f5f7f6",
          willChange: "transform",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            fontSize: 11.5,
            color: "rgba(255,255,255,0.72)",
          }}
        >
          <span>Live Harmonic Scope</span>
          <span style={{ fontFamily: "var(--font-mono)", fontSize: 11, color: "#ff8c42" }}>
            DRV {(drive * 10).toFixed(1)}
          </span>
        </div>

        <canvas
          ref={scopeRef}
          style={{
            width: "100%",
            height: 62,
            display: "block",
            marginTop: 6,
          }}
        />

        <input
          type="range"
          min={0.08}
          max={0.98}
          step={0.01}
          value={drive}
          onChange={(e) => setDrive(parseFloat(e.target.value))}
          aria-label="Drive level"
          style={{
            width: "100%",
            marginTop: 8,
            accentColor: "#ff6b24",
            cursor: "pointer",
          }}
        />

        <div
          style={{
            marginTop: 10,
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: 8,
          }}
        >
          <div style={{ display: "flex", gap: 5 }}>
            {["TS808", "SD-1", ".NAM"].map((chip) => (
              <span
                key={chip}
                style={{
                  fontFamily: "var(--font-mono)",
                  fontSize: 9.5,
                  padding: "3px 6px",
                  borderRadius: 3,
                  background: "rgba(255,255,255,0.08)",
                  color: "rgba(255,255,255,0.78)",
                }}
              >
                {chip}
              </span>
            ))}
          </div>

          <button
            type="button"
            onClick={togglePlay}
            style={{
              border: 0,
              borderRadius: 3,
              padding: "6px 11px",
              background: playing ? "#ff6b24" : "#f5f7f6",
              color: playing ? "#ffffff" : "#0b0e10",
              fontSize: 11,
              fontWeight: 550,
              cursor: "pointer",
              display: "inline-flex",
              alignItems: "center",
              gap: 5,
            }}
          >
            <span>{playing ? "■" : "▶"}</span>
            <span>
              {playing
                ? locale === "ko"
                  ? "정지"
                  : "Stop"
                : locale === "ko"
                  ? "오디션"
                  : "Play"}
            </span>
          </button>
        </div>
      </div>
    </div>
  );
}
