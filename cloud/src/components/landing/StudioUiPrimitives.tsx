"use client";

import React, { useRef } from "react";
import { StudioSlotPreview } from "./LandingData";

/**
 * Pixel-faithful miniature of CortexGrid's Porcelain-White rounded-[14px] Gear Card
 * (matching web-ui/src/components/CortexGrid.tsx lines 186-279)
 */
export function StudioSlotCard({
  slot,
  selected = false,
  compact = false,
  onClick,
}: {
  slot: StudioSlotPreview | null;
  selected?: boolean;
  compact?: boolean;
  onClick?: () => void;
}) {
  const h = compact ? 54 : 66;
  const radius = compact ? 10 : 12;

  if (!slot) {
    return (
      <div
        style={{
          height: h,
          borderRadius: radius,
          border: "1px dashed rgba(255, 255, 255, 0.14)",
          background: "rgba(255, 255, 255, 0.025)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          color: "rgba(255, 255, 255, 0.28)",
          fontFamily: "var(--font-mono)",
          fontSize: compact ? 13 : 15,
          userSelect: "none",
        }}
      >
        +
      </div>
    );
  }

  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        width: "100%",
        height: h,
        borderRadius: radius,
        padding: compact ? "6px 8px" : "8px 10px",
        background: selected ? "#ffffff" : "#f4f6f5",
        color: "#0b0e10",
        border: 0,
        boxShadow: selected
          ? "0 12px 26px rgba(255, 107, 36, 0.38), 0 0 0 2px #ff6b24"
          : "0 6px 16px rgba(0, 0, 0, 0.28)",
        transform: selected ? "translate3d(0, -3px, 0) scale(1.02)" : "translate3d(0, 0, 0)",
        transition: "transform 240ms cubic-bezier(0.22, 1, 0.36, 1), box-shadow 240ms ease",
        display: "flex",
        flexDirection: "column",
        justifyContent: "space-between",
        textAlign: "left",
        cursor: onClick ? "pointer" : "default",
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          width: "100%",
          fontFamily: "var(--font-mono)",
          fontSize: compact ? 8 : 9,
          fontWeight: 600,
          letterSpacing: "0.05em",
        }}
      >
        <span style={{ color: selected ? "#ff6b24" : "rgba(11,14,16,0.52)" }}>{slot.cat}</span>
        <span
          style={{
            width: compact ? 6 : 7,
            height: compact ? 6 : 7,
            borderRadius: "50%",
            background: selected ? "#ff6b24" : slot.dot,
            boxShadow: `0 0 6px ${selected ? "#ff6b24" : slot.dot}`,
          }}
        />
      </div>

      <div
        style={{
          fontSize: compact ? 10.5 : 11.5,
          fontWeight: 600,
          letterSpacing: "-0.015em",
          whiteSpace: "nowrap",
          overflow: "hidden",
          textOverflow: "ellipsis",
          width: "100%",
        }}
      >
        {slot.name}
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: 4, width: "100%" }}>
        <div
          style={{
            height: 2,
            flex: 1,
            borderRadius: 99,
            background: "rgba(11,14,16,0.1)",
            overflow: "hidden",
          }}
        >
          <div
            style={{
              height: "100%",
              width: `${slot.pct}%`,
              background: selected ? "#ff6b24" : "#0b0e10",
              transition: "width 220ms ease",
            }}
          />
        </div>
        {slot.badge && (
          <span
            style={{
              fontFamily: "var(--font-mono)",
              fontSize: 7.5,
              fontWeight: 700,
              color: selected ? "#ff6b24" : "rgba(11,14,16,0.55)",
            }}
          >
            {slot.badge}
          </span>
        )}
      </div>
    </button>
  );
}

/**
 * Pixel-faithful 11-Tick CortexKnob Cell from web-ui/src/components/CortexKnob.tsx
 * Left: uppercase mono label + tabular numeric value. Right: 11 radial ticks + brushed cap + orange pointer.
 */
export function StudioKnobCell({
  label,
  value,
  min = 0,
  max = 10,
  unit = "",
  onChange,
}: {
  label: string;
  value: number;
  min?: number;
  max?: number;
  unit?: string;
  onChange?: (next: number) => void;
}) {
  const dragRef = useRef<{ y: number; v: number } | null>(null);
  const norm = Math.max(0, Math.min(1, (value - min) / (max - min)));
  const angleDeg = -135 + norm * 270;

  return (
    <div
      onPointerDown={(e) => {
        if (!onChange) return;
        e.currentTarget.setPointerCapture(e.pointerId);
        dragRef.current = { y: e.clientY, v: value };
      }}
      onPointerMove={(e) => {
        if (!dragRef.current || !onChange) return;
        const dy = dragRef.current.y - e.clientY;
        const next = Math.max(min, Math.min(max, dragRef.current.v + (dy / 110) * (max - min)));
        onChange(Number(next.toFixed(1)));
      }}
      onPointerUp={() => {
        dragRef.current = null;
      }}
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "9px 12px",
        background: "#f4f6f5",
        borderRight: "1px solid rgba(11, 14, 16, 0.08)",
        borderBottom: "1px solid rgba(11, 14, 16, 0.08)",
        cursor: onChange ? "ns-resize" : "default",
        userSelect: "none",
        touchAction: "none",
      }}
    >
      <div>
        <div
          style={{
            fontFamily: "var(--font-mono)",
            fontSize: 9.5,
            fontWeight: 500,
            letterSpacing: "0.08em",
            color: "#616a6c",
          }}
        >
          {label}
        </div>
        <div
          style={{
            marginTop: 4,
            fontSize: 16,
            fontWeight: 350,
            color: "#0b0e10",
            fontVariantNumeric: "tabular-nums",
          }}
        >
          {value.toFixed(1)}
          {unit && <span style={{ fontSize: 10.5, color: "#616a6c", marginLeft: 2 }}>{unit}</span>}
        </div>
      </div>

      <svg width={44} height={44} viewBox="0 0 52 52" aria-hidden="true">
        {Array.from({ length: 11 }).map((_, idx) => {
          const frac = idx / 10;
          const deg = -135 + frac * 270;
          const rad = (deg * Math.PI) / 180;
          const rIn = 19;
          const rOut = 23;
          const x1 = 26 + Math.sin(rad) * rIn;
          const y1 = 26 - Math.cos(rad) * rIn;
          const x2 = 26 + Math.sin(rad) * rOut;
          const y2 = 26 - Math.cos(rad) * rOut;
          const lit = frac <= norm + 0.01;
          return (
            <line
              key={idx}
              x1={x1}
              y1={y1}
              x2={x2}
              y2={y2}
              stroke={lit ? "#ff6b24" : "#0b0e10"}
              strokeOpacity={lit ? 1 : 0.2}
              strokeWidth={lit ? 1.9 : 1.15}
              strokeLinecap="round"
            />
          );
        })}
        <circle
          cx={26}
          cy={26}
          r={14.5}
          fill="#e7ecea"
          stroke="rgba(11, 14, 16, 0.55)"
          strokeWidth={1.2}
        />
        <g transform={`rotate(${angleDeg.toFixed(1)} 26 26)`}>
          <line
            x1={26}
            y1={13}
            x2={26}
            y2={20.5}
            stroke="#ff6b24"
            strokeWidth={2.2}
            strokeLinecap="round"
          />
        </g>
      </svg>
    </div>
  );
}

/**
 * Pixel-faithful Scene A..H Segmented Pill from CortexStudio.tsx
 */
export function StudioScenePill({
  activeScene,
  onSelect,
  compact = false,
}: {
  activeScene: string;
  onSelect?: (s: "A" | "B" | "C" | "D") => void;
  compact?: boolean;
}) {
  const scenes = compact ? ["A", "B", "C", "D"] : ["A", "B", "C", "D", "E", "F", "G", "H"];
  return (
    <div
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 2,
        padding: 3,
        borderRadius: 999,
        background: "rgba(244, 246, 245, 0.9)",
        border: "1px solid rgba(11, 14, 16, 0.1)",
        boxShadow: "0 4px 12px rgba(11, 14, 16, 0.05)",
      }}
    >
      {scenes.map((s) => {
        const active = s === activeScene;
        return (
          <button
            key={s}
            type="button"
            onClick={() => onSelect && (s === "A" || s === "B" || s === "C" || s === "D") && onSelect(s)}
            style={{
              width: compact ? 20 : 22,
              height: compact ? 20 : 22,
              borderRadius: "50%",
              border: 0,
              padding: 0,
              fontFamily: "var(--font-mono)",
              fontSize: 9.5,
              fontWeight: 600,
              background: active ? "#0b0e10" : "transparent",
              color: active ? "#ffffff" : "#616a6c",
              cursor: onSelect ? "pointer" : "default",
              transition: "all 180ms ease",
            }}
          >
            {s}
          </button>
        );
      })}
    </div>
  );
}

/**
 * Pixel-faithful Liquid Glass Floating Dock from App.tsx (▶ 오디오 시작 | ● 녹음 | ≡ 메뉴)
 */
export function StudioLiquidDock({
  playing,
  onTogglePlay,
  locale,
  activeTab,
  onClickRecord,
  onClickMenu,
}: {
  playing: boolean;
  onTogglePlay: () => void;
  locale: "ko" | "en";
  activeTab?: "record" | "menu" | null;
  onClickRecord?: () => void;
  onClickMenu?: () => void;
}) {
  return (
    <div
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 6,
        padding: "5px 6px",
        borderRadius: 999,
        background: "rgba(244, 246, 245, 0.86)",
        backdropFilter: "blur(16px)",
        border: "1px solid rgba(255, 255, 255, 0.72)",
        boxShadow: "0 14px 34px rgba(11, 14, 16, 0.14)",
      }}
    >
      <button
        type="button"
        onClick={onTogglePlay}
        style={{
          border: 0,
          borderRadius: 999,
          padding: "7px 14px",
          background: playing ? "#ff6b24" : "#0b0e10",
          color: "#ffffff",
          fontSize: 11.5,
          fontWeight: 500,
          display: "inline-flex",
          alignItems: "center",
          gap: 6,
          cursor: "pointer",
          transition: "background-color 180ms ease",
        }}
      >
        <span style={{ color: playing ? "#ffffff" : "#ff6b24", fontSize: 9.5 }}>
          {playing ? "■" : "▶"}
        </span>
        <span>
          {playing
            ? locale === "ko"
              ? "연주 중 · 1.3ms"
              : "LIVE · 1.3ms"
            : locale === "ko"
              ? "오디오 시작"
              : "Start Audio"}
        </span>
      </button>

      <span style={{ width: 1, height: 14, background: "rgba(11,14,16,0.12)" }} />

      <button
        type="button"
        onClick={onClickRecord}
        style={{
          border: 0,
          borderRadius: 999,
          padding: "7px 12px",
          background: activeTab === "record" ? "#0b0e10" : "transparent",
          color: activeTab === "record" ? "#ffffff" : "#0b0e10",
          fontSize: 11.5,
          fontWeight: 500,
          display: "inline-flex",
          alignItems: "center",
          gap: 6,
          cursor: onClickRecord ? "pointer" : "default",
        }}
      >
        <span
          style={{
            width: 7,
            height: 7,
            borderRadius: "50%",
            background: "#ff6b24",
          }}
        />
        <span>{locale === "ko" ? "녹음" : "Record"}</span>
      </button>

      <span style={{ width: 1, height: 14, background: "rgba(11,14,16,0.12)" }} />

      <button
        type="button"
        onClick={onClickMenu}
        style={{
          border: 0,
          borderRadius: 999,
          padding: "7px 12px",
          background: activeTab === "menu" ? "#0b0e10" : "transparent",
          color: activeTab === "menu" ? "#ffffff" : "#0b0e10",
          fontSize: 11.5,
          fontWeight: 500,
          display: "inline-flex",
          alignItems: "center",
          gap: 6,
          cursor: onClickMenu ? "pointer" : "default",
        }}
      >
        <span style={{ fontFamily: "var(--font-mono)", fontSize: 11 }}>≡</span>
        <span>{locale === "ko" ? "메뉴" : "Menu"}</span>
      </button>
    </div>
  );
}
