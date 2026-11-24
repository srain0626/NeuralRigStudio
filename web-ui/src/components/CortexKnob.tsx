import React, { useRef, useState } from "react"
import { Radio, RotateCcw } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { Hint } from "@/components/ui/tooltip"
import { useI18n } from "@/lib/i18n"

interface CortexKnobProps {
  label: string
  value: number
  min: number
  max: number
  step: number
  defaultVal: number
  unit?: string
  accentHex: string
  syncedText?: string | null
  midiCc?: number | null
  isMidiLearning?: boolean
  onChange: (next: number) => void
  onContextLearn?: () => void
}

const TICK_COUNT = 11

export const CortexKnob: React.FC<CortexKnobProps> = ({
  label,
  value,
  min,
  max,
  step,
  defaultVal,
  unit,
  syncedText,
  midiCc,
  isMidiLearning,
  onChange,
  onContextLearn,
}) => {
  const { tx } = useI18n()
  const dragRef = useRef<{ startY: number; startVal: number } | null>(null)
  const [editing, setEditing] = useState(false)
  const [draftVal, setDraftVal] = useState("")

  const clamped = Math.min(max, Math.max(min, value))
  const norm = max > min ? (clamped - min) / (max - min) : 0
  const startAngle = -135
  const sweep = 270
  const angleDeg = startAngle + norm * sweep

  const quantize = (v: number) => {
    const q = Math.round(v / step) * step
    const decimals = step < 0.1 ? 2 : step < 1 ? 1 : 0
    return Number(Math.min(max, Math.max(min, q)).toFixed(decimals))
  }

  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    if (editing || e.button !== 0) return
    e.preventDefault()
    e.currentTarget.setPointerCapture(e.pointerId)
    dragRef.current = { startY: e.clientY, startVal: clamped }
  }

  const handlePointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragRef.current) return
    const dy = dragRef.current.startY - e.clientY
    const sens = e.shiftKey ? 450 : 145
    const next = quantize(dragRef.current.startVal + (dy / sens) * (max - min))
    if (next !== clamped) onChange(next)
  }

  const handlePointerUp = (e: React.PointerEvent<HTMLDivElement>) => {
    if (dragRef.current) {
      e.currentTarget.releasePointerCapture(e.pointerId)
      dragRef.current = null
    }
  }

  const handleWheel = (e: React.WheelEvent<HTMLDivElement>) => {
    if (editing) return
    e.preventDefault()
    const dir = e.deltaY < 0 ? 1 : -1
    const inc = step * (e.shiftKey ? 1 : max - min > 100 ? 5 : 2)
    onChange(quantize(clamped + dir * inc))
  }

  const formattedVal =
    step >= 1 ? clamped.toFixed(0) : step >= 0.1 ? clamped.toFixed(1) : clamped.toFixed(2)

  const commitDraft = () => {
    setEditing(false)
    const parsed = Number.parseFloat(draftVal)
    if (!Number.isNaN(parsed) && Number.isFinite(parsed)) {
      onChange(quantize(parsed))
    }
  }

  const activeStroke = syncedText ? "var(--signal-warn)" : "var(--primary)"

  const knobCell = (
    <div
      className={`relative min-w-0 overflow-hidden flex items-center justify-between px-3.5 py-2.5 bg-card hover:bg-secondary/50 transition-colors border-r border-b border-border cursor-ns-resize select-none ${
        isMidiLearning ? "ring-1 ring-inset ring-signal-warn bg-signal-warn/10" : ""
      }`}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onWheel={handleWheel}
      onDoubleClick={() => {
        if (!editing) onChange(defaultVal)
      }}
    >
      <div className="flex flex-col justify-between h-11 min-w-0 pr-2">
        <div className="flex items-center gap-1 min-w-0">
          <span className="font-mono text-[10px] font-medium tracking-[0.08em] text-muted-foreground uppercase truncate">
            {label}
          </span>
          {isMidiLearning ? (
            <Badge variant="warn" size="xs">
              MIDI…
            </Badge>
          ) : midiCc !== null && midiCc !== undefined ? (
            <Badge variant="ok" size="xs">
              CC{midiCc}
            </Badge>
          ) : null}
        </div>

        {editing ? (
          <input
            type="text"
            autoFocus
            value={draftVal}
            onPointerDown={(e) => e.stopPropagation()}
            onDoubleClick={(e) => e.stopPropagation()}
            onChange={(e) => setDraftVal(e.target.value)}
            onBlur={commitDraft}
            onKeyDown={(e) => {
              if (e.key === "Enter") commitDraft()
              if (e.key === "Escape") setEditing(false)
            }}
            className="w-16 h-5 px-1 rounded-[2px] bg-background border border-primary font-mono text-xs text-foreground outline-none tabular-nums"
          />
        ) : syncedText ? (
          <div className="flex items-baseline gap-1 font-mono text-xs font-semibold text-signal-warn tabular-nums">
            <span>{syncedText}</span>
          </div>
        ) : (
          <button
            type="button"
            onPointerDown={(e) => e.stopPropagation()}
            onDoubleClick={(e) => e.stopPropagation()}
            onClick={() => {
              setDraftVal(formattedVal)
              setEditing(true)
            }}
            className="flex items-baseline gap-1 font-display text-[17px] font-light tracking-[-0.02em] text-foreground hover:text-primary transition-colors text-left cursor-text"
          >
            <span className="tabular-nums">{formattedVal}</span>
            {unit && <span className="font-mono text-[10px] text-muted-foreground">{unit}</span>}
          </button>
        )}
      </div>

      {/* Architectural 11-Tick Precision Dial */}
      <svg width={48} height={48} viewBox="0 0 52 52" className="shrink-0 overflow-visible">
        {Array.from({ length: TICK_COUNT }, (_, idx) => {
          const tickFrac = idx / (TICK_COUNT - 1)
          const rad = ((startAngle + tickFrac * sweep) * Math.PI) / 180
          const rInner = 20.5
          const rOuter = idx === 0 || idx === 5 || idx === 10 ? 24.5 : 22.8
          const x1 = 26 + Math.sin(rad) * rInner
          const y1 = 26 - Math.cos(rad) * rInner
          const x2 = 26 + Math.sin(rad) * rOuter
          const y2 = 26 - Math.cos(rad) * rOuter
          const lit = tickFrac <= norm + 0.008
          return (
            <line
              key={idx}
              x1={x1}
              y1={y1}
              x2={x2}
              y2={y2}
              stroke={lit ? activeStroke : "currentColor"}
              strokeOpacity={lit ? 1 : 0.22}
              strokeWidth={lit ? 1.9 : 1.15}
              strokeLinecap="round"
              className={lit ? "" : "text-foreground"}
            />
          )
        })}

        <circle
          cx={26}
          cy={26}
          r={15.5}
          className="fill-background/80 stroke-foreground/70"
          strokeWidth={1.3}
        />

        <g transform={`rotate(${angleDeg} 26 26)`}>
          <line
            x1={26}
            y1={12.5}
            x2={26}
            y2={21}
            stroke={activeStroke}
            strokeWidth={2.2}
            strokeLinecap="round"
          />
        </g>
      </svg>
    </div>
  )

  return (
    <ContextMenu>
      <ContextMenuTrigger asChild>
        <div>
          <Hint
            content={tx(
              `${label} — 상하 드래그/휠 조절 · 클릭 직접 입력 · 더블클릭 기본값(${defaultVal})`,
              `${label} — Drag/wheel to adjust · Click to type · Double-click reset (${defaultVal})`
            )}
          >
            {knobCell}
          </Hint>
        </div>
      </ContextMenuTrigger>
      <ContextMenuContent>
        <ContextMenuItem onSelect={() => onChange(defaultVal)}>
          <span className="flex items-center gap-2">
            <RotateCcw className="size-3.5" />
            {tx(`기본값 복원 (${defaultVal})`, `Reset to Default (${defaultVal})`)}
          </span>
        </ContextMenuItem>
        {onContextLearn && (
          <>
            <ContextMenuSeparator />
            <ContextMenuItem onSelect={onContextLearn}>
              <span className="flex items-center gap-2">
                <Radio className="size-3.5 text-primary" />
                {midiCc !== null && midiCc !== undefined
                  ? tx(`MIDI CC#${midiCc} 매핑 해제`, `Clear MIDI CC#${midiCc}`)
                  : tx("MIDI CC 학습 시작", "Learn MIDI CC")}
              </span>
            </ContextMenuItem>
          </>
        )}
      </ContextMenuContent>
    </ContextMenu>
  )
}

interface CortexToggleCellProps {
  label: string
  checked: boolean
  onLabel?: string
  offLabel?: string
  accentHex: string
  onChange: (next: boolean) => void
}

export const CortexToggleCell: React.FC<CortexToggleCellProps> = ({
  label,
  checked,
  onLabel = "ON",
  offLabel = "OFF",
  onChange,
}) => {
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      className="min-w-0 overflow-hidden flex flex-col justify-between px-3.5 py-2.5 bg-card hover:bg-secondary/50 transition-colors border-r border-b border-border text-left h-full min-h-[70px] cursor-pointer"
    >
      <span className="font-mono text-[10px] font-medium tracking-[0.08em] text-muted-foreground uppercase w-full truncate">
        {label}
      </span>
      <div className="flex items-center gap-3 mt-1 min-w-0 w-full">
        <div className="w-5 h-9 shrink-0 rounded-[4px] bg-secondary border border-border p-0.5 flex flex-col justify-between items-center relative">
          <div
            className="w-3.5 h-3.5 rounded-[2px] transition-[transform,background-color,box-shadow] duration-300 ease-[cubic-bezier(0.22,1,0.36,1)]"
            style={{
              transform: checked ? "translateY(0px)" : "translateY(16px)",
              backgroundColor: checked ? "var(--primary)" : "var(--muted-foreground)",
              boxShadow: checked ? "0 0 12px rgba(255, 107, 36, 0.55)" : "none",
            }}
          />
        </div>
        <div className="flex flex-col font-mono text-[11px] leading-tight font-semibold min-w-0 flex-1">
          <span className={`truncate transition-colors duration-250 ${checked ? "text-foreground" : "text-muted-foreground/45"}`}>
            {onLabel}
          </span>
          <span className={`truncate transition-colors duration-250 ${!checked ? "text-foreground" : "text-muted-foreground/45"}`}>
            {offLabel}
          </span>
        </div>
      </div>
    </button>
  )
}
