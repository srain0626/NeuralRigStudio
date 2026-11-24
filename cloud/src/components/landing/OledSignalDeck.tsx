"use client";

import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { DeckBlock, Locale, SHOWCASE_RIGS, ShowcaseRig } from "./LandingData";

interface OledSignalDeckProps {
  locale: Locale;
  activeBank: string;
  onSelectBank: (bank: string) => void;
}

interface AudioEngineHandles {
  ctx: AudioContext;
  shaper: WaveShaperNode;
  midFilter: BiquadFilterNode;
  cabLowpass: BiquadFilterNode;
  masterGain: GainNode;
  timerId: number;
}

function buildTransferCurve(drive: number, asymmetry: number): Float32Array<ArrayBuffer> {
  const samples = 2048;
  const buffer = new ArrayBuffer(samples * 4);
  const curve = new Float32Array(buffer);
  const preGain = 1 + drive * 16;
  const asymBias = asymmetry * 0.55;

  for (let i = 0; i < samples; i++) {
    const x = (i * 2) / (samples - 1) - 1;
    const driven = x * preGain + asymBias;
    const posSlope = 1.0;
    const negSlope = 1.0 + asymmetry * 1.35;
    const shaped =
      driven >= 0
        ? Math.tanh(driven * posSlope)
        : Math.tanh(driven * negSlope) / (1 + asymmetry * 0.42);
    const dcOffset = Math.tanh(asymBias * posSlope);
    curve[i] = Math.max(-0.98, Math.min(0.98, (shaped - dcOffset * 0.65) * 0.88));
  }
  return curve;
}

interface RotaryPotProps {
  label: string;
  value: number;
  unitDisplay: string;
  accent?: string;
  onChange: (next: number) => void;
}

function RotaryPot({ label, value, unitDisplay, accent = "#ff4400", onChange }: RotaryPotProps) {
  const dragRef = useRef<{ startY: number; startVal: number } | null>(null);

  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    e.currentTarget.setPointerCapture(e.pointerId);
    dragRef.current = { startY: e.clientY, startVal: value };
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragRef.current) return;
    const delta = (dragRef.current.startY - e.clientY) / 110;
    const next = Math.max(0, Math.min(1, dragRef.current.startVal + delta));
    onChange(Number(next.toFixed(3)));
  };

  const handlePointerUp = () => {
    dragRef.current = null;
  };

  const angleDeg = -135 + value * 270;
  const radius = 19;
  const startRad = ((-135 - 90) * Math.PI) / 180;
  const endRad = ((angleDeg - 90) * Math.PI) / 180;
  const x1 = 24 + radius * Math.cos(startRad);
  const y1 = 24 + radius * Math.sin(startRad);
  const x2 = 24 + radius * Math.cos(endRad);
  const y2 = 24 + radius * Math.sin(endRad);
  const largeArc = value * 270 > 180 ? 1 : 0;

  return (
    <div
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerUp}
      style={{
        userSelect: "none",
        touchAction: "none",
        cursor: "ns-resize",
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        padding: "8px 6px",
        background: "#101210",
        border: "1px solid #232622",
        borderRadius: 2,
      }}
      title="Drag vertically to adjust"
    >
      <svg width={48} height={48} viewBox="0 0 48 48" aria-label={label}>
        <circle cx={24} cy={24} r={19} fill="none" stroke="#252824" strokeWidth={2.5} />
        {value > 0.01 && (
          <path
            d={`M ${x1.toFixed(2)} ${y1.toFixed(2)} A ${radius} ${radius} 0 ${largeArc} 1 ${x2.toFixed(2)} ${y2.toFixed(2)}`}
            fill="none"
            stroke={accent}
            strokeWidth={2.5}
            strokeLinecap="round"
          />
        )}
        <circle cx={24} cy={24} r={14} fill="#181a18" stroke="#343833" strokeWidth={1.2} />
        <g transform={`rotate(${angleDeg.toFixed(1)} 24 24)`}>
          <line x1={24} y1={12} x2={24} y2={19} stroke="#f2efe8" strokeWidth={2} strokeLinecap="round" />
        </g>
      </svg>
      <div
        style={{
          fontFamily: "var(--font-mono)",
          fontSize: 9.5,
          letterSpacing: "0.08em",
          color: "#8a887f",
          marginTop: 2,
        }}
      >
        {label}
      </div>
      <div
        style={{
          fontFamily: "var(--font-mono)",
          fontSize: 11,
          fontWeight: 600,
          color: "#f2efe8",
          fontVariantNumeric: "tabular-nums",
        }}
      >
        {unitDisplay}
      </div>
    </div>
  );
}

export function OledSignalDeck({ locale, activeBank, onSelectBank }: OledSignalDeckProps) {
  const rig: ShowcaseRig = useMemo(
    () => SHOWCASE_RIGS.find((r) => r.bank === activeBank) || SHOWCASE_RIGS[1],
    [activeBank]
  );

  const [drive, setDrive] = useState(rig.defaults.drive);
  const [asymmetry, setAsymmetry] = useState(rig.defaults.asymmetry);
  const [tone, setTone] = useState(rig.defaults.tone);
  const [air, setAir] = useState(rig.defaults.air);
  const [selectedBlockId, setSelectedBlockId] = useState<string>(rig.blocks[1]?.id || rig.blocks[0].id);
  const [bypassedIds, setBypassedIds] = useState<Record<string, boolean>>({});
  const [isAuditioning, setIsAuditioning] = useState(false);

  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const audioRef = useRef<AudioEngineHandles | null>(null);
  const paramsRef = useRef({ drive, asymmetry, tone, air, bank: rig.bank });

  useEffect(() => {
    setDrive(rig.defaults.drive);
    setAsymmetry(rig.defaults.asymmetry);
    setTone(rig.defaults.tone);
    setAir(rig.defaults.air);
    setSelectedBlockId(rig.blocks[1]?.id || rig.blocks[0].id);
    setBypassedIds({});
  }, [rig]);

  useEffect(() => {
    paramsRef.current = { drive, asymmetry, tone, air, bank: rig.bank };
    const engine = audioRef.current;
    if (engine) {
      engine.shaper.curve = buildTransferCurve(drive, asymmetry);
      engine.midFilter.frequency.setTargetAtTime(520 + tone * 680, engine.ctx.currentTime, 0.03);
      engine.midFilter.gain.setTargetAtTime(2.5 + drive * 6.5, engine.ctx.currentTime, 0.03);
      engine.cabLowpass.frequency.setTargetAtTime(2400 + air * 3800, engine.ctx.currentTime, 0.03);
    }
  }, [drive, asymmetry, tone, air, rig.bank]);

  const stopAudition = useCallback(() => {
    const engine = audioRef.current;
    if (engine) {
      window.clearInterval(engine.timerId);
      try {
        engine.masterGain.gain.setTargetAtTime(0.0001, engine.ctx.currentTime, 0.04);
        setTimeout(() => {
          engine.ctx.close().catch(() => {});
        }, 120);
      } catch {
        // Ignore close errors
      }
      audioRef.current = null;
    }
    setIsAuditioning(false);
  }, []);

  useEffect(() => {
    return () => {
      if (audioRef.current) {
        window.clearInterval(audioRef.current.timerId);
        audioRef.current.ctx.close().catch(() => {});
      }
    };
  }, []);

  const triggerGuitarPluck = useCallback((ctx: AudioContext, dest: AudioNode, freqs: number[], dur: number) => {
    const now = ctx.currentTime;
    freqs.forEach((freq, idx) => {
      const start = now + idx * 0.022;
      const osc1 = ctx.createOscillator();
      const osc2 = ctx.createOscillator();
      const bodyFilter = ctx.createBiquadFilter();
      const noteGain = ctx.createGain();

      osc1.type = "sawtooth";
      osc2.type = "triangle";
      osc1.frequency.setValueAtTime(freq, start);
      osc2.frequency.setValueAtTime(freq * 1.0018, start);

      bodyFilter.type = "lowpass";
      bodyFilter.frequency.setValueAtTime(freq * 6.5, start);
      bodyFilter.frequency.exponentialRampToValueAtTime(Math.max(260, freq * 1.7), start + dur);

      noteGain.gain.setValueAtTime(0.0001, start);
      noteGain.gain.linearRampToValueAtTime(0.22 / Math.sqrt(freqs.length), start + 0.009);
      noteGain.gain.exponentialRampToValueAtTime(0.0008, start + dur);

      osc1.connect(bodyFilter);
      osc2.connect(bodyFilter);
      bodyFilter.connect(noteGain);
      noteGain.connect(dest);

      osc1.start(start);
      osc2.start(start);
      osc1.stop(start + dur + 0.03);
      osc2.stop(start + dur + 0.03);
    });
  }, []);

  const toggleAudition = useCallback(() => {
    if (isAuditioning) {
      stopAudition();
      return;
    }

    const AudioCtx = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
    const ctx = new AudioCtx();

    const hpInput = ctx.createBiquadFilter();
    hpInput.type = "highpass";
    hpInput.frequency.value = 88;

    const midFilter = ctx.createBiquadFilter();
    midFilter.type = "peaking";
    midFilter.Q.value = 1.15;
    midFilter.frequency.value = 520 + paramsRef.current.tone * 680;
    midFilter.gain.value = 2.5 + paramsRef.current.drive * 6.5;

    const shaper = ctx.createWaveShaper();
    shaper.oversample = "4x";
    shaper.curve = buildTransferCurve(paramsRef.current.drive, paramsRef.current.asymmetry);

    const cabResonance = ctx.createBiquadFilter();
    cabResonance.type = "peaking";
    cabResonance.frequency.value = 115;
    cabResonance.Q.value = 2.1;
    cabResonance.gain.value = 3.2;

    const cabLowpass = ctx.createBiquadFilter();
    cabLowpass.type = "lowpass";
    cabLowpass.Q.value = 0.85;
    cabLowpass.frequency.value = 2400 + paramsRef.current.air * 3800;

    const masterGain = ctx.createGain();
    masterGain.gain.value = 0.24;

    hpInput.connect(midFilter);
    midFilter.connect(shaper);
    shaper.connect(cabResonance);
    cabResonance.connect(cabLowpass);
    cabLowpass.connect(masterGain);
    masterGain.connect(ctx.destination);

    let step = 0;
    const playStep = () => {
      const b = paramsRef.current.bank;
      const patterns: Record<string, { notes: number[]; dur: number }[]> = {
        "1A": [
          { notes: [164.81, 246.94, 311.13, 392.0], dur: 0.72 },
          { notes: [329.63, 493.88], dur: 0.42 },
          { notes: [146.83, 220.0, 293.66, 369.99], dur: 0.72 },
          { notes: [293.66, 440.0], dur: 0.45 },
        ],
        "1C": [
          { notes: [110.0, 164.81, 220.0], dur: 0.55 },
          { notes: [110.0, 164.81], dur: 0.26 },
          { notes: [146.83, 220.0, 293.66], dur: 0.55 },
          { notes: [130.81, 196.0, 261.63], dur: 0.52 },
        ],
        "2A": [
          { notes: [73.42, 110.0], dur: 0.22 },
          { notes: [73.42], dur: 0.18 },
          { notes: [87.31, 130.81], dur: 0.34 },
          { notes: [77.78, 116.54], dur: 0.28 },
        ],
        "2D": [
          { notes: [82.41, 164.81, 246.94], dur: 0.62 },
          { notes: [196.0, 293.66], dur: 0.44 },
          { notes: [110.0, 220.0, 261.63], dur: 0.58 },
          { notes: [146.83, 220.0], dur: 0.48 },
        ],
      };
      const seq = patterns[b] || patterns["1C"];
      const item = seq[step % seq.length];
      triggerGuitarPluck(ctx, hpInput, item.notes, item.dur);
      step += 1;
    };

    playStep();
    const timerId = window.setInterval(playStep, 480);

    audioRef.current = { ctx, shaper, midFilter, cabLowpass, masterGain, timerId };
    setIsAuditioning(true);
  }, [isAuditioning, stopAudition, triggerGuitarPluck]);

  // 60fps Oscilloscope Canvas
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    let raf = 0;
    let phase = 0;

    const render = () => {
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

      // Oscilloscope Graticule
      ctx.strokeStyle = "rgba(242, 239, 232, 0.06)";
      ctx.lineWidth = 1;
      for (let gx = 1; gx < 8; gx++) {
        const x = (w / 8) * gx;
        ctx.beginPath();
        ctx.moveTo(x, 0);
        ctx.lineTo(x, h);
        ctx.stroke();
      }
      ctx.beginPath();
      ctx.moveTo(0, h / 2);
      ctx.lineTo(w, h / 2);
      ctx.stroke();

      const { drive: drv, asymmetry: asym, tone: tn, air: ar } = paramsRef.current;
      const speed = isAuditioning ? 0.085 : 0.028;
      phase += speed;

      // Trace 1: Dry Guitar DI Fundamental + Pluck Harmonics
      ctx.strokeStyle = "rgba(138, 136, 127, 0.48)";
      ctx.lineWidth = 1.25;
      ctx.beginPath();
      for (let px = 0; px <= w; px += 2) {
        const t = (px / w) * Math.PI * 4 + phase;
        const env = isAuditioning ? 0.78 + 0.22 * Math.sin(phase * 0.7) : 0.85;
        const dry = (Math.sin(t) * 0.76 + Math.sin(t * 2 + 0.4) * 0.18 + Math.sin(t * 3) * 0.06) * env;
        const y = h / 2 - dry * (h * 0.34);
        if (px === 0) ctx.moveTo(px, y);
        else ctx.lineTo(px, y);
      }
      ctx.stroke();

      // Trace 2: MNA Clipped & Tone-Shaped Output Wave
      ctx.strokeStyle = "#ff4400";
      ctx.lineWidth = 2;
      ctx.beginPath();
      const preGain = 1 + drv * 11.5;
      const bias = asym * 0.52;
      const hfRipple = (tn * 0.5 + ar * 0.5) * 0.16;

      for (let px = 0; px <= w; px += 2) {
        const t = (px / w) * Math.PI * 4 + phase;
        const env = isAuditioning ? 0.82 + 0.18 * Math.sin(phase * 0.7) : 0.88;
        const raw =
          (Math.sin(t) * 0.78 +
            Math.sin(t * 2 + 0.35) * 0.16 +
            Math.sin(t * 5) * hfRipple * drv) *
          env;
        const u = raw * preGain + bias;
        const clipped =
          u >= 0
            ? Math.tanh(u * (1.0 + tn * 0.4))
            : Math.tanh(u * (1.0 + asym * 1.45)) / (1 + asym * 0.36);
        const centered = clipped - Math.tanh(bias) * 0.55;
        const y = h / 2 - Math.max(-0.95, Math.min(0.95, centered)) * (h * 0.38);
        if (px === 0) ctx.moveTo(px, y);
        else ctx.lineTo(px, y);
      }
      ctx.stroke();

      ctx.restore();
      raf = window.requestAnimationFrame(render);
    };

    raf = window.requestAnimationFrame(render);
    return () => window.cancelAnimationFrame(raf);
  }, [isAuditioning]);

  const selectedBlock: DeckBlock =
    rig.blocks.find((b) => b.id === selectedBlockId) || rig.blocks[0];

  const thdEstimate = (1.8 + drive * drive * 34.5 + asymmetry * 6.2).toFixed(1);
  const h2Estimate = (-68 + asymmetry * 46 + drive * 6).toFixed(1);

  const rowABlocks = rig.blocks.filter((b) => b.row === 0);
  const rowBBlocks = rig.blocks.filter((b) => b.row === 1);

  const toggleBypass = (id: string) => {
    setBypassedIds((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  return (
    <div className="nr-oled-bezel">
      {/* Machined Screw Header */}
      <div className="nr-screw-row">
        <span className="nr-screw" />
        <span>NR-300 // RECESSED OLED 2×8 SIGNAL DECK · INTERACTIVE</span>
        <span className="nr-screw" />
      </div>

      <div className="nr-oled-screen">
        {/* Top OLED Status & Bank Bar */}
        <div
          style={{
            display: "flex",
            flexWrap: "wrap",
            alignItems: "center",
            justifyContent: "space-between",
            gap: 10,
            padding: "11px 14px",
            borderBottom: "1px solid var(--oled-border)",
            background: "var(--oled-elevated)",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 6, flexWrap: "wrap" }}>
            {SHOWCASE_RIGS.map((r) => {
              const active = r.bank === rig.bank;
              return (
                <button
                  key={r.bank}
                  type="button"
                  onClick={() => onSelectBank(r.bank)}
                  style={{
                    border: `1px solid ${active ? "#ff4400" : "#2c302b"}`,
                    background: active ? "rgba(255, 68, 0, 0.16)" : "#0e100e",
                    color: active ? "#ffffff" : "#9c9a91",
                    fontFamily: "var(--font-mono)",
                    fontSize: 10.5,
                    fontWeight: 600,
                    padding: "4px 8px",
                    borderRadius: 2,
                    cursor: "pointer",
                    letterSpacing: "0.04em",
                  }}
                >
                  {r.bank}
                </button>
              );
            })}
            <span
              style={{
                marginLeft: 4,
                fontSize: 12.5,
                fontWeight: 600,
                color: "#f2efe8",
                letterSpacing: "-0.01em",
              }}
            >
              {rig.name}
            </span>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <span
              style={{
                fontFamily: "var(--font-mono)",
                fontSize: 10,
                color: "#8a887f",
                display: "inline-flex",
                alignItems: "center",
                gap: 5,
              }}
            >
              <span
                className="nr-led-live"
                style={{
                  width: 6,
                  height: 6,
                  borderRadius: "50%",
                  background: "var(--led-ok)",
                  display: "inline-block",
                }}
              />
              {rig.latencyMs}ms · 0 B
            </span>

            <button
              type="button"
              onClick={toggleAudition}
              style={{
                border: `1px solid ${isAuditioning ? "#ff4400" : "#3a3e39"}`,
                background: isAuditioning ? "#ff4400" : "#171a17",
                color: "#ffffff",
                fontFamily: "var(--font-mono)",
                fontSize: 10.5,
                fontWeight: 600,
                letterSpacing: "0.05em",
                padding: "5px 10px",
                borderRadius: 2,
                cursor: "pointer",
                display: "inline-flex",
                alignItems: "center",
                gap: 6,
              }}
            >
              <span>{isAuditioning ? "■" : "▶"}</span>
              <span>
                {isAuditioning
                  ? locale === "ko"
                    ? "리프 정지"
                    : "STOP RIFF"
                  : locale === "ko"
                    ? "톤 오디션"
                    : "AUDITION"}
              </span>
            </button>
          </div>
        </div>

        {/* 2x8 Signal Chain Matrix Area */}
        <div style={{ padding: "16px 14px 12px", position: "relative" }}>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              fontFamily: "var(--font-mono)",
              fontSize: 9.5,
              color: "#6f6e67",
              letterSpacing: "0.08em",
              marginBottom: 10,
            }}
          >
            <span>IN 1 · HI-Z 1MΩ ({locale === "ko" ? rig.pickupKo : rig.pickupEn})</span>
            <span>{rig.parallelSplit ? "PARALLEL DUAL-ROW SPLIT" : "SERIAL DIRECT PATH"}</span>
          </div>

          {/* Row A */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: 8,
              overflowX: "auto",
              paddingBottom: 6,
            }}
          >
            <span
              style={{
                fontFamily: "var(--font-mono)",
                fontSize: 9.5,
                color: "#7e7c74",
                minWidth: 34,
              }}
            >
              ROW A
            </span>
            {rowABlocks.map((blk, i) => {
              const isSel = blk.id === selectedBlock.id;
              const isByp = Boolean(bypassedIds[blk.id]);
              return (
                <React.Fragment key={blk.id}>
                  <button
                    type="button"
                    onClick={() => setSelectedBlockId(blk.id)}
                    onDoubleClick={() => toggleBypass(blk.id)}
                    style={{
                      minWidth: 74,
                      padding: "7px 8px",
                      background: isSel ? "#1b1f1b" : "#111311",
                      border: `1.2px solid ${isByp ? "#2b2d2a" : isSel ? blk.color : "#313530"}`,
                      borderRadius: 3,
                      color: isByp ? "#5c5b55" : "#f2efe8",
                      cursor: "pointer",
                      textAlign: "left",
                      opacity: isByp ? 0.52 : 1,
                      position: "relative",
                      flexShrink: 0,
                    }}
                  >
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "space-between",
                        gap: 4,
                      }}
                    >
                      <span
                        style={{
                          fontFamily: "var(--font-mono)",
                          fontSize: 10.5,
                          fontWeight: 600,
                          letterSpacing: "0.03em",
                        }}
                      >
                        {blk.code}
                      </span>
                      <span
                        onClick={(e) => {
                          e.stopPropagation();
                          toggleBypass(blk.id);
                        }}
                        title={locale === "ko" ? "클릭하여 바이패스 전환" : "Toggle block bypass"}
                        style={{
                          width: 6,
                          height: 6,
                          borderRadius: "50%",
                          background: isByp ? "#3a3b37" : blk.color,
                          display: "inline-block",
                        }}
                      />
                    </div>
                    <div
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 8.5,
                        color: "#8a887f",
                        marginTop: 3,
                        whiteSpace: "nowrap",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                      }}
                    >
                      {blk.metric}
                    </div>
                  </button>
                  {i < rowABlocks.length - 1 && (
                    <svg width={14} height={8} viewBox="0 0 14 8" style={{ flexShrink: 0 }}>
                      <line
                        x1={0}
                        y1={4}
                        x2={14}
                        y2={4}
                        stroke={isAuditioning ? "#ff4400" : "#52554e"}
                        strokeWidth={1.5}
                        className={isAuditioning ? "nr-cable-live" : undefined}
                      />
                    </svg>
                  )}
                </React.Fragment>
              );
            })}
          </div>

          {/* Row B (Parallel Split Branch or Standby Rail) */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: 8,
              marginTop: 6,
              paddingTop: 6,
              borderTop: "1px dashed #1f221e",
              overflowX: "auto",
            }}
          >
            <span
              style={{
                fontFamily: "var(--font-mono)",
                fontSize: 9.5,
                color: rowBBlocks.length > 0 ? "#facc15" : "#4d4e48",
                minWidth: 34,
              }}
            >
              ROW B
            </span>
            {rowBBlocks.length > 0 ? (
              <>
                <span
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 9.5,
                    color: "#8a887f",
                    padding: "0 6px",
                  }}
                >
                  ↳ SPLIT @ NODE 2 →
                </span>
                {rowBBlocks.map((blk) => {
                  const isSel = blk.id === selectedBlock.id;
                  return (
                    <button
                      key={blk.id}
                      type="button"
                      onClick={() => setSelectedBlockId(blk.id)}
                      style={{
                        minWidth: 78,
                        padding: "6px 8px",
                        background: isSel ? "#1c1f1c" : "#111311",
                        border: `1.2px solid ${isSel ? blk.color : "#323631"}`,
                        borderRadius: 3,
                        color: "#f2efe8",
                        cursor: "pointer",
                        textAlign: "left",
                      }}
                    >
                      <div style={{ fontFamily: "var(--font-mono)", fontSize: 10, fontWeight: 600 }}>
                        {blk.code}
                      </div>
                      <div style={{ fontFamily: "var(--font-mono)", fontSize: 8.5, color: "#8a887f" }}>
                        {blk.metric}
                      </div>
                    </button>
                  );
                })}
                <span
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 9.5,
                    color: "#8a887f",
                    paddingLeft: 4,
                  }}
                >
                  → PHASE-LOCKED SUM ↗
                </span>
              </>
            ) : (
              <span
                style={{
                  fontFamily: "var(--font-mono)",
                  fontSize: 9.5,
                  color: "#57564f",
                  letterSpacing: "0.04em",
                }}
              >
                {locale === "ko"
                  ? "병렬 슬롯 대기 중 (1C 프리셋을 선택해 듀얼 앰프 + .NAM 분기를 확인하세요)"
                  : "PARALLEL BUS IDLE — SELECT BANK 1C TO INSPECT DUAL-AMP + .NAM SPLIT"}
              </span>
            )}
          </div>

          {/* Selected Node Micro-Readout */}
          <div
            style={{
              marginTop: 10,
              padding: "8px 10px",
              background: "#111411",
              border: "1px solid #232622",
              borderRadius: 2,
              display: "flex",
              flexWrap: "wrap",
              alignItems: "center",
              justifyContent: "space-between",
              gap: 8,
              fontFamily: "var(--font-mono)",
              fontSize: 10.5,
            }}
          >
            <div>
              <span style={{ color: selectedBlock.color, fontWeight: 600 }}>
                [{selectedBlock.code}] {selectedBlock.name}
              </span>
              <span style={{ color: "#8a887f", marginLeft: 8 }}>
                {locale === "ko" ? selectedBlock.topologyKo : selectedBlock.topologyEn}
              </span>
            </div>
            <span style={{ color: "#6f6e67", fontSize: 9.5 }}>
              {locale === "ko" ? "블록 클릭: 회로 조회" : "CLICK NODE TO INSPECT"}
            </span>
          </div>
        </div>

        {/* Oscilloscope + 4 Tactile Rotary Knobs */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "minmax(0, 1.25fr) minmax(0, 1fr)",
            borderTop: "1px solid var(--oled-border)",
          }}
        >
          {/* Live 60fps Transfer Scope */}
          <div
            style={{
              padding: "10px 12px",
              borderRight: "1px solid var(--oled-border)",
              display: "flex",
              flexDirection: "column",
              justifyContent: "space-between",
              minHeight: 132,
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                fontFamily: "var(--font-mono)",
                fontSize: 9.5,
                color: "#8a887f",
                letterSpacing: "0.05em",
              }}
            >
              <span>TRANSIENT WAVEFORM · 4× HIIR</span>
              <span style={{ color: "#f2efe8" }}>
                THD {thdEstimate}% · H2 {h2Estimate}dBc
              </span>
            </div>

            <canvas
              ref={canvasRef}
              style={{
                width: "100%",
                height: 86,
                display: "block",
                marginTop: 6,
              }}
            />

            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                fontFamily: "var(--font-mono)",
                fontSize: 9,
                color: "#68675f",
              }}
            >
              <span>GRAY: HI-Z DI INPUT</span>
              <span style={{ color: "#ff4400" }}>ORANGE: MNA SOLVED OUTPUT</span>
            </div>
          </div>

          {/* 4 Rotary Pots */}
          <div
            style={{
              padding: 10,
              display: "grid",
              gridTemplateColumns: "repeat(2, minmax(0, 1fr))",
              gap: 6,
              background: "#0d0f0d",
            }}
          >
            <RotaryPot
              label="DRIVE"
              value={drive}
              unitDisplay={`${(drive * 10).toFixed(1)}`}
              accent="#ff4400"
              onChange={setDrive}
            />
            <RotaryPot
              label={locale === "ko" ? "ASYM BIAS" : "ASYMMETRY"}
              value={asymmetry}
              unitDisplay={`${Math.round(asymmetry * 100)}%`}
              accent="#f59e0b"
              onChange={setAsymmetry}
            />
            <RotaryPot
              label="TONE"
              value={tone}
              unitDisplay={`${Math.round(520 + tone * 680)}Hz`}
              accent="#22c55e"
              onChange={setTone}
            />
            <RotaryPot
              label="CAB AIR"
              value={air}
              unitDisplay={`${((2400 + air * 3800) / 1000).toFixed(1)}kHz`}
              accent="#6366f1"
              onChange={setAir}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
