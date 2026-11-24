"use client";

import React, { useMemo, useState } from "react";
import { CIRCUIT_MODELS, COPY, CircuitModelSpec, Locale } from "./LandingData";

interface CircuitAnatomyLabProps {
  locale: Locale;
}

const FREQ_LABELS = ["40", "100", "250", "720", "1.5k", "3.5k", "8k", "16k"];

function buildSmoothSvgPath(points: { x: number; y: number }[]): string {
  if (points.length === 0) return "";
  if (points.length === 1) return `M ${points[0].x} ${points[0].y}`;
  let d = `M ${points[0].x.toFixed(1)} ${points[0].y.toFixed(1)}`;
  for (let i = 0; i < points.length - 1; i++) {
    const p0 = points[Math.max(0, i - 1)];
    const p1 = points[i];
    const p2 = points[i + 1];
    const p3 = points[Math.min(points.length - 1, i + 2)];

    const cp1x = p1.x + (p2.x - p0.x) / 6;
    const cp1y = p1.y + (p2.y - p0.y) / 6;
    const cp2x = p2.x - (p3.x - p1.x) / 6;
    const cp2y = p2.y - (p3.y - p1.y) / 6;

    d += ` C ${cp1x.toFixed(1)} ${cp1y.toFixed(1)}, ${cp2x.toFixed(1)} ${cp2y.toFixed(1)}, ${p2.x.toFixed(1)} ${p2.y.toFixed(1)}`;
  }
  return d;
}

export function CircuitAnatomyLab({ locale }: CircuitAnatomyLabProps) {
  const [selectedId, setSelectedId] = useState<CircuitModelSpec["id"]>("sd1");
  const [tonePot, setTonePot] = useState<number>(0.5);

  const t = COPY[locale].lab;
  const model = useMemo(
    () => CIRCUIT_MODELS.find((m) => m.id === selectedId) || CIRCUIT_MODELS[1],
    [selectedId]
  );

  // Compute interactive Bode points (SVG viewBox 560 x 210)
  const plotW = 560;
  const plotH = 210;
  const padL = 40;
  const padR = 20;
  const padT = 22;
  const padB = 30;
  const innerW = plotW - padL - padR;
  const innerH = plotH - padT - padB;

  // Map dB range [-16, +12] -> Y
  const dbToY = (db: number) => {
    const clamped = Math.max(-16, Math.min(12, db));
    const norm = (clamped - -16) / (12 - -16);
    return padT + innerH * (1 - norm);
  };

  const zeroDbY = dbToY(0);

  const bodePoints = useMemo(() => {
    const tilt = (tonePot - 0.5) * 2; // -1..+1
    return model.bodeDb.map((baseDb, idx) => {
      // Interactive KCL-inspired treble/scoop shift on upper-mid & treble bands
      const highFactor = (idx - 2.5) / 5.5;
      const scoopShift = model.id === "twin65" && idx === 3 ? -Math.abs(tilt) * 1.4 : 0;
      const adjustedDb = baseDb + tilt * highFactor * 4.8 + scoopShift;
      const x = padL + (innerW * idx) / (model.bodeDb.length - 1);
      const y = dbToY(adjustedDb);
      return { x, y, db: adjustedDb };
    });
  }, [model, tonePot, innerW]);

  const curvePath = useMemo(() => buildSmoothSvgPath(bodePoints), [bodePoints]);
  const areaPath = useMemo(() => {
    if (bodePoints.length === 0) return "";
    const first = bodePoints[0];
    const last = bodePoints[bodePoints.length - 1];
    return `${curvePath} L ${last.x.toFixed(1)} ${zeroDbY.toFixed(1)} L ${first.x.toFixed(1)} ${zeroDbY.toFixed(1)} Z`;
  }, [bodePoints, curvePath, zeroDbY]);

  const peakPt = bodePoints[model.peakAnnotation.freqIndex] || bodePoints[3];
  const notes = locale === "ko" ? model.schematicNotesKo : model.schematicNotesEn;

  return (
    <div>
      {/* Section Header */}
      <div className="nr-sec-head">
        <div className="nr-sec-head-left">
          <div className="nr-index-tag">{t.index}</div>
          <h2 className="nr-sec-title">
            {t.titlePre}{" "}
            <span className="nr-serif-accent" style={{ color: "var(--signal)" }}>
              {t.titleSerif}
            </span>
          </h2>
        </div>
        <div className="nr-sec-head-right">
          <p className="nr-sec-desc">{t.desc}</p>
        </div>
      </div>

      {/* 4-Way Physical Circuit Selector Bar */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
          borderBottom: "1px solid var(--hairline)",
        }}
      >
        {CIRCUIT_MODELS.map((item, idx) => {
          const active = item.id === model.id;
          return (
            <button
              key={item.id}
              type="button"
              onClick={() => setSelectedId(item.id)}
              style={{
                textAlign: "left",
                padding: "18px 22px",
                background: active ? "var(--bg-elevated)" : "transparent",
                border: 0,
                borderRight: idx < CIRCUIT_MODELS.length - 1 ? "1px solid var(--hairline)" : 0,
                boxShadow: active ? `inset 0 -3px 0 ${item.accentColor}` : "none",
                cursor: "pointer",
                color: "var(--ink)",
                transition: "background-color 140ms ease",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  fontFamily: "var(--font-mono)",
                  fontSize: 10,
                  letterSpacing: "0.06em",
                  color: "var(--ink-3)",
                }}
              >
                <span>{item.code}</span>
                <span
                  style={{
                    color: active ? item.accentColor : "var(--ink-2)",
                    fontWeight: 600,
                  }}
                >
                  MATCH {item.matchScore}
                </span>
              </div>
              <div
                style={{
                  marginTop: 6,
                  fontSize: 15,
                  fontWeight: 700,
                  letterSpacing: "-0.02em",
                }}
              >
                {item.name}
              </div>
              <div
                style={{
                  marginTop: 3,
                  fontFamily: "var(--font-mono)",
                  fontSize: 11,
                  color: "var(--ink-2)",
                }}
              >
                {item.topologyTag}
              </div>
            </button>
          );
        })}
      </div>

      {/* Dual Instrument Readout + Circuit Explanation */}
      <div className="nr-split-2">
        {/* Left:Circuit Narrative & Key Metric Plate */}
        <div
          className="nr-col-left"
          style={{
            padding: "28px 32px",
            display: "flex",
            flexDirection: "column",
            justifyContent: "space-between",
            gap: 24,
          }}
        >
          <div>
            <div
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: 10,
                padding: "6px 10px",
                border: "1px solid var(--hairline)",
                background: "var(--bg-elevated)",
                fontFamily: "var(--font-mono)",
                fontSize: 11,
              }}
            >
              <span style={{ color: "var(--ink-3)" }}>{model.keyMetricLabel}:</span>
              <strong style={{ color: model.accentColor, fontSize: 12.5 }}>
                {model.keyMetricValue}
              </strong>
            </div>

            <p
              style={{
                margin: "18px 0 0",
                fontSize: 15,
                lineHeight: 1.7,
                color: "var(--ink)",
              }}
            >
              {locale === "ko" ? model.summaryKo : model.summaryEn}
            </p>

            <ul
              style={{
                listStyle: "none",
                padding: 0,
                margin: "20px 0 0",
                display: "flex",
                flexDirection: "column",
                gap: 10,
              }}
            >
              {notes.map((note, idx) => (
                <li
                  key={idx}
                  style={{
                    display: "grid",
                    gridTemplateColumns: "24px 1fr",
                    fontSize: 13.5,
                    lineHeight: 1.55,
                    color: "var(--ink-2)",
                  }}
                >
                  <span
                    style={{
                      fontFamily: "var(--font-mono)",
                      fontSize: 11,
                      color: model.accentColor,
                      fontWeight: 600,
                    }}
                  >
                    0{idx + 1}
                  </span>
                  <span>{note}</span>
                </li>
              ))}
            </ul>
          </div>

          {/* Interactive Tone Stack Potentiometer Sweep */}
          <div
            style={{
              padding: "14px 16px",
              background: "var(--bg-recessed)",
              border: "1px solid var(--hairline)",
              borderRadius: 2,
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                fontFamily: "var(--font-mono)",
                fontSize: 10.5,
                letterSpacing: "0.05em",
                marginBottom: 8,
              }}
            >
              <span>{t.toneSweepLabel}</span>
              <strong>{Math.round(tonePot * 100)}%</strong>
            </div>
            <input
              type="range"
              min={0}
              max={1}
              step={0.01}
              value={tonePot}
              onChange={(e) => setTonePot(parseFloat(e.target.value))}
              aria-label={t.toneSweepLabel}
              style={{
                width: "100%",
                accentColor: model.accentColor,
                cursor: "pointer",
              }}
            />
          </div>
        </div>

        {/* Right: Precision .AC Bode Plot + Harmonic Spectrum */}
        <div
          style={{
            padding: "24px 28px",
            background: "var(--bg-recessed)",
            display: "flex",
            flexDirection: "column",
            gap: 18,
          }}
        >
          {/* Instrument 1: .AC Bode Plot */}
          <div className="nr-oled-screen" style={{ padding: "14px 16px" }}>
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                fontFamily: "var(--font-mono)",
                fontSize: 10,
                color: "#8a887f",
                letterSpacing: "0.06em",
                marginBottom: 6,
              }}
            >
              <span>{t.bodeTitle}</span>
              <span style={{ color: model.accentColor }}>SPICE .AC VERIFIED</span>
            </div>

            <svg
              viewBox={`0 0 ${plotW} ${plotH}`}
              style={{ width: "100%", height: "auto", display: "block" }}
              role="img"
              aria-label={`${model.name} Bode frequency response`}
            >
              {/* Horizontal dB Gridlines (+6, 0, -6, -12) */}
              {[6, 0, -6, -12].map((db) => {
                const y = dbToY(db);
                return (
                  <g key={db}>
                    <line
                      x1={padL}
                      y1={y}
                      x2={plotW - padR}
                      y2={y}
                      stroke={db === 0 ? "rgba(242,239,232,0.22)" : "rgba(242,239,232,0.07)"}
                      strokeDasharray={db === 0 ? "3 3" : undefined}
                      strokeWidth={1}
                    />
                    <text
                      x={padL - 6}
                      y={y + 3}
                      textAnchor="end"
                      fill="#75736b"
                      fontFamily="var(--font-mono)"
                      fontSize={9}
                    >
                      {db > 0 ? `+${db}` : db}dB
                    </text>
                  </g>
                );
              })}

              {/* Vertical Frequency Ticks */}
              {FREQ_LABELS.map((lbl, idx) => {
                const x = padL + (innerW * idx) / (FREQ_LABELS.length - 1);
                return (
                  <g key={lbl}>
                    <line
                      x1={x}
                      y1={padT}
                      x2={x}
                      y2={plotH - padB}
                      stroke="rgba(242,239,232,0.05)"
                      strokeWidth={1}
                    />
                    <text
                      x={x}
                      y={plotH - 10}
                      textAnchor="middle"
                      fill="#8a887f"
                      fontFamily="var(--font-mono)"
                      fontSize={9.5}
                    >
                      {lbl}
                    </text>
                  </g>
                );
              })}

              {/* Shaded Response Envelope */}
              <path d={areaPath} fill={model.accentColor} fillOpacity={0.14} />

              {/* Bode Curve */}
              <path
                d={curvePath}
                fill="none"
                stroke={model.accentColor}
                strokeWidth={2.4}
                strokeLinecap="round"
              />

              {/* Peak/Scoop Crosshair Callout */}
              <line
                x1={peakPt.x}
                y1={padT}
                x2={peakPt.x}
                y2={plotH - padB}
                stroke={model.accentColor}
                strokeOpacity={0.45}
                strokeDasharray="2 3"
              />
              <circle
                cx={peakPt.x}
                cy={peakPt.y}
                r={4.5}
                fill="#0a0b0a"
                stroke={model.accentColor}
                strokeWidth={2}
              />
              <rect
                x={Math.min(plotW - 195, Math.max(padL + 4, peakPt.x + 8))}
                y={Math.max(padT + 2, peakPt.y - 22)}
                width={182}
                height={18}
                rx={2}
                fill="#141714"
                stroke={model.accentColor}
                strokeWidth={0.9}
              />
              <text
                x={Math.min(plotW - 195, Math.max(padL + 4, peakPt.x + 8)) + 7}
                y={Math.max(padT + 2, peakPt.y - 22) + 12}
                fill="#f2efe8"
                fontFamily="var(--font-mono)"
                fontSize={9.5}
              >
                {model.peakAnnotation.label}
              </text>
            </svg>
          </div>

          {/* Instrument 2: Harmonic Spectrum H1..H7 */}
          <div className="nr-oled-screen" style={{ padding: "14px 16px" }}>
            <div
              style={{
                display: "flex",
                flexWrap: "wrap",
                justifyContent: "space-between",
                gap: 8,
                fontFamily: "var(--font-mono)",
                fontSize: 10,
                color: "#8a887f",
                letterSpacing: "0.06em",
                marginBottom: 12,
              }}
            >
              <span>{t.harmTitle}</span>
              <span>
                <span style={{ color: "#f59e0b" }}>■ EVEN (H2/H4/H6)</span>
                {" · "}
                <span style={{ color: "#22c55e" }}>■ ODD (H3/H5/H7)</span>
              </span>
            </div>

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(7, minmax(0, 1fr))",
                gap: 10,
                alignItems: "end",
                height: 112,
                paddingTop: 10,
              }}
            >
              {model.harmonicsDbc.map((dbc, i) => {
                const order = i + 1;
                const isFundamental = order === 1;
                const isEven = order % 2 === 0;
                // Convert 0..-120 dBc into pixel bar height (max 68px) so labels never touch top
                const barPx = Math.max(3, Math.round(((120 + dbc) / 120) * 68));
                const barColor = isFundamental
                  ? "#f2efe8"
                  : isEven
                    ? "#f59e0b"
                    : "#22c55e";

                return (
                  <div
                    key={order}
                    style={{
                      display: "flex",
                      flexDirection: "column",
                      alignItems: "center",
                      justifyContent: "flex-end",
                    }}
                  >
                    <div
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 9,
                        color: dbc > -90 ? "#d6d3ca" : "#57564f",
                        marginBottom: 5,
                        fontVariantNumeric: "tabular-nums",
                      }}
                    >
                      {dbc === 0 ? "0dB" : `${dbc.toFixed(0)}`}
                    </div>
                    <div
                      style={{
                        width: "100%",
                        maxWidth: 28,
                        height: `${barPx}px`,
                        background: barColor,
                        opacity: dbc <= -100 ? 0.22 : 0.9,
                        borderRadius: "1px 1px 0 0",
                        transition: "height 240ms cubic-bezier(0.22, 1, 0.36, 1)",
                      }}
                    />
                    <div
                      style={{
                        marginTop: 5,
                        fontFamily: "var(--font-mono)",
                        fontSize: 10,
                        fontWeight: 600,
                        color: isEven ? "#f59e0b" : "#9c9a91",
                      }}
                    >
                      H{order}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
