import React, { useRef } from "react"

interface CortexKnobProps {
  label: string
  value: number
  min: number
  max: number
  step: number
  defaultVal: number
  unit?: string
  accentHex: string
  onChange: (next: number) => void
}

export const CortexKnob: React.FC<CortexKnobProps> = ({
  label,
  value,
  min,
  max,
  step,
  defaultVal,
  unit,
  accentHex,
  onChange,
}) => {
  const dragRef = useRef<{ startY: number; startVal: number } | null>(null)

  const clamped = Math.min(max, Math.max(min, value))
  const norm = max > min ? (clamped - min) / (max - min) : 0

  // 270 deg sweep: -135 deg to +135 deg
  const startAngle = -135
  const sweep = 270
  const angleDeg = startAngle + norm * sweep

  const r = 20
  const cx = 26
  const cy = 26
  const circ = 2 * Math.PI * r
  const trackLen = (sweep / 360) * circ
  const activeLen = norm * trackLen

  const quantize = (v: number) => {
    const q = Math.round(v / step) * step
    const decimals = step < 0.1 ? 2 : step < 1 ? 1 : 0
    return Number(Math.min(max, Math.max(min, q)).toFixed(decimals))
  }

  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    e.preventDefault()
    e.currentTarget.setPointerCapture(e.pointerId)
    dragRef.current = { startY: e.clientY, startVal: clamped }
  }

  const handlePointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!dragRef.current) return
    const dy = dragRef.current.startY - e.clientY
    const sens = e.shiftKey ? 450 : 150
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
    e.preventDefault()
    const dir = e.deltaY < 0 ? 1 : -1
    const inc = step * (e.shiftKey ? 1 : max - min > 100 ? 5 : 2)
    onChange(quantize(clamped + dir * inc))
  }

  const formattedVal = step >= 1 ? clamped.toFixed(0) : step >= 0.1 ? clamped.toFixed(1) : clamped.toFixed(2)

  return (
    <div
      className="relative flex items-center justify-between px-3.5 py-2.5 bg-card/70 hover:bg-card transition-colors border-r border-b border-border cursor-ns-resize group"
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onWheel={handleWheel}
      onDoubleClick={() => onChange(defaultVal)}
      title={`${label}: 드래그 또는 휠로 조절 / 더블클릭으로 기본값 복원`}
    >
      <div className="flex flex-col justify-between h-12 min-w-0 pr-2">
        <span className="text-[10px] font-semibold tracking-wider text-muted-foreground uppercase truncate">
          {label}
        </span>
        <div className="flex items-baseline gap-1 font-mono text-xs text-foreground">
          <span className="font-semibold tabular-nums">{formattedVal}</span>
          {unit && <span className="text-[10px] text-muted-foreground">{unit}</span>}
        </div>
      </div>

      <svg width={52} height={52} viewBox="0 0 52 52" className="shrink-0 overflow-visible">
        {/* Background track arc */}
        <circle
          cx={cx}
          cy={cy}
          r={r}
          fill="none"
          stroke="currentColor"
          className="text-secondary"
          strokeWidth={3.2}
          strokeLinecap="round"
          strokeDasharray={`${trackLen} ${circ}`}
          transform={`rotate(135 ${cx} ${cy})`}
        />
        {/* Active value arc */}
        <circle
          cx={cx}
          cy={cy}
          r={r}
          fill="none"
          stroke={accentHex}
          strokeWidth={3.2}
          strokeLinecap="round"
          strokeDasharray={`${Math.max(0.5, activeLen)} ${circ}`}
          transform={`rotate(135 ${cx} ${cy})`}
        />
        {/* Inner hardware dial */}
        <circle cx={cx} cy={cy} r={14.5} className="fill-background stroke-border" strokeWidth={1} />
        {/* Indicator needle */}
        <g transform={`rotate(${angleDeg} ${cx} ${cy})`}>
          <line
            x1={cx}
            y1={cy - 4}
            x2={cx}
            y2={cy - 13.5}
            stroke="currentColor"
            className="text-foreground"
            strokeWidth={2}
            strokeLinecap="round"
          />
        </g>
      </svg>
    </div>
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
  onLabel = "On",
  offLabel = "Off",
  accentHex,
  onChange,
}) => {
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      className="flex flex-col justify-between px-3.5 py-2.5 bg-card/70 hover:bg-card transition-colors border-r border-b border-border text-left h-full min-h-[72px]"
    >
      <span className="text-[10px] font-semibold tracking-wider text-muted-foreground uppercase">
        {label}
      </span>
      <div className="flex items-center gap-3 mt-1">
        {/* Vertical hardware toggle pill matching Cortex Control screenshot */}
        <div className="w-5 h-9 rounded-full bg-background border border-border p-0.5 flex flex-col justify-between items-center relative">
          <div
            className="w-3.5 h-3.5 rounded-full transition-all duration-150"
            style={{
              transform: checked ? "translateY(0px)" : "translateY(16px)",
              border: `2px solid ${accentHex}`,
              backgroundColor: checked ? accentHex : "transparent",
              boxShadow: checked ? `0 0 8px ${accentHex}88` : "none",
            }}
          />
        </div>
        <div className="flex flex-col text-[11px] leading-tight font-medium">
          <span className={checked ? "text-foreground" : "text-muted-foreground/50"}>{onLabel}</span>
          <span className={!checked ? "text-foreground" : "text-muted-foreground/50"}>{offLabel}</span>
        </div>
      </div>
    </button>
  )
}
