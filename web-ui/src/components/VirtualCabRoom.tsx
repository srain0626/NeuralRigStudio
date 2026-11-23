import React, { useRef, useState } from "react"
import { Mic, RotateCcw } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Slider } from "@/components/ui/slider"
import { Switch } from "@/components/ui/switch"
import type { CabMicConfigDto } from "@/types/rig"

export const DEFAULT_CAB_MIC_CONFIG: CabMicConfigDto = {
  dualEnabled: false,
  micAType: 0,
  micAX: 0.25,
  micADist: 0.2,
  micALevelDb: 0,
  micAPan: -0.35,
  micAPhaseInv: false,
  micBType: 1,
  micBX: 0.55,
  micBDist: 0.35,
  micBLevelDb: -1.5,
  micBPan: 0.35,
  micBPhaseInv: false,
  micBDelayMs: 0,
  blend: 0.45,
}

const MIC_MODELS = [
  { id: 0, name: "Shure SM57 (Dynamic)", short: "SM57" },
  { id: 1, name: "Royer R-121 (Ribbon)", short: "R-121" },
  { id: 2, name: "AKG C414 (Condenser)", short: "C414" },
  { id: 3, name: "Sennheiser MD421", short: "MD421" },
  { id: 4, name: "Beyerdynamic M160", short: "M160" },
  { id: 5, name: "Shure SM7B (Dynamic)", short: "SM7B" },
]

interface VirtualCabRoomProps {
  config?: CabMicConfigDto | null
  onChange: (next: CabMicConfigDto) => void
}

export const VirtualCabRoom: React.FC<VirtualCabRoomProps> = ({ config, onChange }) => {
  const cfg: CabMicConfigDto = config ?? DEFAULT_CAB_MIC_CONFIG
  const svgRef = useRef<SVGSVGElement | null>(null)
  const [draggingMic, setDraggingMic] = useState<"A" | "B" | null>(null)

  const patch = (partial: Partial<CabMicConfigDto>) => {
    onChange({ ...cfg, ...partial })
  }

  // Speaker cone coordinates in 240x150 viewBox:
  // X: 36 (Cap Center, x=0) .. 216 (Cone Edge, x=1)
  // Y: 22 (Close on grille, dist=0) .. 132 (Room distance, dist=1)
  const toSvgX = (normX: number) => 36 + Math.max(0, Math.min(1, normX)) * 180
  const toSvgY = (normDist: number) => 22 + Math.max(0, Math.min(1, normDist)) * 110

  const updateMicFromPointer = (clientX: number, clientY: number, which: "A" | "B") => {
    const svg = svgRef.current
    if (!svg) return
    const rect = svg.getBoundingClientRect()
    if (rect.width <= 0 || rect.height <= 0) return
    const vx = ((clientX - rect.left) / rect.width) * 240
    const vy = ((clientY - rect.top) / rect.height) * 150
    const nx = Math.max(0, Math.min(1, (vx - 36) / 180))
    const ny = Math.max(0, Math.min(1, (vy - 22) / 110))
    if (which === "A") {
      patch({ dualEnabled: true, micAX: Number(nx.toFixed(2)), micADist: Number(ny.toFixed(2)) })
    } else {
      patch({ dualEnabled: true, micBX: Number(nx.toFixed(2)), micBDist: Number(ny.toFixed(2)) })
    }
  }

  const micAXSvg = toSvgX(cfg.micAX)
  const micAYSvg = toSvgY(cfg.micADist)
  const micBXSvg = toSvgX(cfg.micBX)
  const micBYSvg = toSvgY(cfg.micBDist)

  return (
    <div className="flex items-stretch gap-3 p-2.5 bg-background/55 border-l border-border min-w-[660px] flex-1">
      {/* Left: 2D Interactive Speaker Cone & Dual Mic Placement Canvas */}
      <div className="w-[238px] shrink-0 flex flex-col justify-between gap-1.5">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-1.5">
            <Mic className="w-3.5 h-3.5 text-amber-400" />
            <span className="text-[11px] font-bold tracking-wide text-foreground">VIRTUAL CAB ROOM</span>
          </div>
          <div className="flex items-center gap-1.5">
            <span className="text-[10px] font-mono text-muted-foreground">
              {cfg.dualEnabled ? "DUAL ON" : "BYPASS"}
            </span>
            <Switch
              checked={cfg.dualEnabled}
              onCheckedChange={(checked) => patch({ dualEnabled: checked })}
            />
          </div>
        </div>

        <svg
          ref={svgRef}
          viewBox="0 0 240 150"
          className="w-full h-[142px] rounded-md bg-[#0d1017] border border-border/80 select-none touch-none cursor-crosshair"
          onPointerMove={(e) => {
            if (draggingMic) updateMicFromPointer(e.clientX, e.clientY, draggingMic)
          }}
          onPointerUp={() => setDraggingMic(null)}
          onPointerCancel={() => setDraggingMic(null)}
        >
          {/* Distance Grid Lines */}
          {[0.25, 0.5, 0.75].map((r) => (
            <line
              key={`h-${r}`}
              x1="20"
              y1={toSvgY(r)}
              x2="228"
              y2={toSvgY(r)}
              stroke="rgba(255,255,255,0.06)"
              strokeDasharray="3 3"
            />
          ))}
          {[0, 0.33, 0.66, 1].map((c) => (
            <line
              key={`v-${c}`}
              x1={toSvgX(c)}
              y1="14"
              x2={toSvgX(c)}
              y2="138"
              stroke="rgba(255,255,255,0.06)"
              strokeDasharray="3 3"
            />
          ))}

          {/* Speaker Cone Acoustic Reference */}
          <ellipse cx="36" cy="77" rx="184" ry="62" fill="rgba(255,255,255,0.025)" stroke="rgba(255,255,255,0.14)" />
          <ellipse cx="36" cy="77" rx="122" ry="44" fill="none" stroke="rgba(255,255,255,0.08)" />
          <ellipse cx="36" cy="77" rx="42" ry="22" fill="rgba(255,255,255,0.04)" stroke="rgba(255,255,255,0.22)" />

          {/* Axis Labels */}
          <text x="26" y="13" fill="rgba(255,255,255,0.45)" fontSize="8" fontFamily="monospace">
            CAP
          </text>
          <text x="196" y="13" fill="rgba(255,255,255,0.45)" fontSize="8" fontFamily="monospace">
            EDGE
          </text>
          <text x="4" y="25" fill="rgba(255,255,255,0.4)" fontSize="7" fontFamily="monospace">
            0"
          </text>
          <text x="4" y="134" fill="rgba(255,255,255,0.4)" fontSize="7" fontFamily="monospace">
            12"
          </text>

          {/* Mic A Draggable Handle */}
          <g
            transform={`translate(${micAXSvg}, ${micAYSvg})`}
            className="cursor-grab active:cursor-grabbing"
            onPointerDown={(e) => {
              e.stopPropagation()
              e.currentTarget.setPointerCapture(e.pointerId)
              setDraggingMic("A")
            }}
            onPointerMove={(e) => {
              if (draggingMic === "A") updateMicFromPointer(e.clientX, e.clientY, "A")
            }}
            onPointerUp={(e) => {
              if (e.currentTarget.hasPointerCapture(e.pointerId))
                e.currentTarget.releasePointerCapture(e.pointerId)
              setDraggingMic(null)
            }}
          >
            <circle r="13" fill="rgba(245, 158, 11, 0.18)" stroke="#f59e0b" strokeWidth="1.6" />
            <circle r="4" fill="#fbbf24" />
            <text y="-16" textAnchor="middle" fill="#fbbf24" fontSize="8.5" fontWeight="bold" fontFamily="monospace">
              A·{MIC_MODELS[cfg.micAType]?.short ?? "57"}
            </text>
          </g>

          {/* Mic B Draggable Handle */}
          <g
            transform={`translate(${micBXSvg}, ${micBYSvg})`}
            className="cursor-grab active:cursor-grabbing"
            onPointerDown={(e) => {
              e.stopPropagation()
              e.currentTarget.setPointerCapture(e.pointerId)
              setDraggingMic("B")
            }}
            onPointerMove={(e) => {
              if (draggingMic === "B") updateMicFromPointer(e.clientX, e.clientY, "B")
            }}
            onPointerUp={(e) => {
              if (e.currentTarget.hasPointerCapture(e.pointerId))
                e.currentTarget.releasePointerCapture(e.pointerId)
              setDraggingMic(null)
            }}
          >
            <circle r="13" fill="rgba(52, 211, 153, 0.18)" stroke="#34d399" strokeWidth="1.6" />
            <circle r="4" fill="#6ee7b7" />
            <text y="22" textAnchor="middle" fill="#6ee7b7" fontSize="8.5" fontWeight="bold" fontFamily="monospace">
              B·{MIC_MODELS[cfg.micBType]?.short ?? "121"}
            </text>
          </g>
        </svg>

        <div className="flex items-center justify-between gap-1">
          {[
            {
              label: "SM57+R121",
              val: {
                ...DEFAULT_CAB_MIC_CONFIG,
                dualEnabled: true,
              },
            },
            {
              label: "Haas Wide",
              val: {
                ...DEFAULT_CAB_MIC_CONFIG,
                dualEnabled: true,
                micAType: 0,
                micAX: 0.2,
                micADist: 0.15,
                micAPan: -1,
                micBType: 2,
                micBX: 0.45,
                micBDist: 0.3,
                micBPan: 1,
                micBDelayMs: 8.5,
                blend: 0.5,
              },
            },
            {
              label: "Tight Edge",
              val: {
                ...DEFAULT_CAB_MIC_CONFIG,
                dualEnabled: true,
                micAType: 3,
                micAX: 0.15,
                micADist: 0.1,
                micAPan: -0.2,
                micBType: 4,
                micBX: 0.72,
                micBDist: 0.22,
                micBPan: 0.2,
                blend: 0.38,
              },
            },
          ].map((preset) => (
            <button
              key={preset.label}
              type="button"
              onClick={() => onChange(preset.val)}
              className="flex-1 h-5 rounded bg-secondary/75 hover:bg-accent border border-border text-[9px] font-mono text-muted-foreground hover:text-foreground transition-colors"
            >
              {preset.label}
            </button>
          ))}
        </div>
      </div>

      {/* Right: Mic A, Mic B & Blend/Haas Controls */}
      <div className="flex-1 grid grid-cols-3 gap-2.5 text-[11px]">
        {/* Mic A Column */}
        <div className="rounded-md border border-amber-500/25 bg-card/60 p-2 flex flex-col justify-between gap-1.5">
          <div className="flex items-center justify-between">
            <span className="font-bold text-amber-300 text-[11px]">MIC A</span>
            <div className="flex items-center gap-1">
              <Button
                type="button"
                size="sm"
                variant={cfg.micAPhaseInv ? "default" : "secondary"}
                className="h-5 px-1.5 text-[10px] font-mono"
                onClick={() => patch({ dualEnabled: true, micAPhaseInv: !cfg.micAPhaseInv })}
                title="Mic A 위상 반전 (Phase Invert Ø)"
              >
                Ø
              </Button>
            </div>
          </div>
          <Select
            value={String(cfg.micAType)}
            onValueChange={(v) => patch({ dualEnabled: true, micAType: Number(v) })}
          >
            <SelectTrigger className="h-6 text-[10px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {MIC_MODELS.map((m) => (
                <SelectItem key={m.id} value={String(m.id)} className="text-xs">
                  {m.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <div className="space-y-1">
            <div className="flex justify-between text-[10px] font-mono text-muted-foreground">
              <span>POS {Math.round(cfg.micAX * 100)}%</span>
              <span>DIST {Math.round(cfg.micADist * 12)}"</span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-[9px] font-mono text-muted-foreground w-7">PAN</span>
              <Slider
                value={[Math.round(cfg.micAPan * 100)]}
                min={-100}
                max={100}
                step={5}
                onValueChange={([v]) => patch({ dualEnabled: true, micAPan: v / 100 })}
              />
              <span className="text-[9px] font-mono w-7 text-right">
                {cfg.micAPan === 0 ? "C" : cfg.micAPan < 0 ? `L${Math.round(-cfg.micAPan * 100)}` : `R${Math.round(cfg.micAPan * 100)}`}
              </span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-[9px] font-mono text-muted-foreground w-7">LVL</span>
              <Slider
                value={[Math.round(cfg.micALevelDb * 10)]}
                min={-180}
                max={60}
                step={5}
                onValueChange={([v]) => patch({ dualEnabled: true, micALevelDb: v / 10 })}
              />
              <span className="text-[9px] font-mono w-7 text-right">
                {cfg.micALevelDb > 0 ? `+${cfg.micALevelDb.toFixed(1)}` : cfg.micALevelDb.toFixed(1)}
              </span>
            </div>
          </div>
        </div>

        {/* Mic B Column */}
        <div className="rounded-md border border-emerald-500/25 bg-card/60 p-2 flex flex-col justify-between gap-1.5">
          <div className="flex items-center justify-between">
            <span className="font-bold text-emerald-300 text-[11px]">MIC B</span>
            <Button
              type="button"
              size="sm"
              variant={cfg.micBPhaseInv ? "default" : "secondary"}
              className="h-5 px-1.5 text-[10px] font-mono"
              onClick={() => patch({ dualEnabled: true, micBPhaseInv: !cfg.micBPhaseInv })}
              title="Mic B 위상 반전 (Phase Invert Ø)"
            >
              Ø
            </Button>
          </div>
          <Select
            value={String(cfg.micBType)}
            onValueChange={(v) => patch({ dualEnabled: true, micBType: Number(v) })}
          >
            <SelectTrigger className="h-6 text-[10px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {MIC_MODELS.map((m) => (
                <SelectItem key={m.id} value={String(m.id)} className="text-xs">
                  {m.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <div className="space-y-1">
            <div className="flex justify-between text-[10px] font-mono text-muted-foreground">
              <span>POS {Math.round(cfg.micBX * 100)}%</span>
              <span>DIST {Math.round(cfg.micBDist * 12)}"</span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-[9px] font-mono text-muted-foreground w-7">PAN</span>
              <Slider
                value={[Math.round(cfg.micBPan * 100)]}
                min={-100}
                max={100}
                step={5}
                onValueChange={([v]) => patch({ dualEnabled: true, micBPan: v / 100 })}
              />
              <span className="text-[9px] font-mono w-7 text-right">
                {cfg.micBPan === 0 ? "C" : cfg.micBPan < 0 ? `L${Math.round(-cfg.micBPan * 100)}` : `R${Math.round(cfg.micBPan * 100)}`}
              </span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-[9px] font-mono text-muted-foreground w-7">LVL</span>
              <Slider
                value={[Math.round(cfg.micBLevelDb * 10)]}
                min={-180}
                max={60}
                step={5}
                onValueChange={([v]) => patch({ dualEnabled: true, micBLevelDb: v / 10 })}
              />
              <span className="text-[9px] font-mono w-7 text-right">
                {cfg.micBLevelDb > 0 ? `+${cfg.micBLevelDb.toFixed(1)}` : cfg.micBLevelDb.toFixed(1)}
              </span>
            </div>
          </div>
        </div>

        {/* Blend & Haas Delay Column */}
        <div className="rounded-md border border-border bg-card/60 p-2 flex flex-col justify-between gap-2">
          <div className="flex items-center justify-between">
            <span className="font-bold text-foreground text-[11px]">MIX · HAAS</span>
            <Button
              type="button"
              size="icon"
              variant="ghost"
              className="h-5 w-5"
              onClick={() => onChange({ ...DEFAULT_CAB_MIC_CONFIG, dualEnabled: cfg.dualEnabled })}
              title="마이킹 기본값 초기화"
            >
              <RotateCcw className="w-3 h-3" />
            </Button>
          </div>

          <div className="space-y-1">
            <div className="flex justify-between text-[10px] font-mono">
              <span className="text-amber-300">A {Math.round((1 - cfg.blend) * 100)}%</span>
              <span className="text-emerald-300">B {Math.round(cfg.blend * 100)}%</span>
            </div>
            <Slider
              value={[Math.round(cfg.blend * 100)]}
              min={0}
              max={100}
              step={1}
              onValueChange={([v]) => patch({ dualEnabled: true, blend: v / 100 })}
            />
          </div>

          <div className="space-y-1">
            <div className="flex justify-between text-[10px] font-mono">
              <span className="text-muted-foreground">MIC B HAAS</span>
              <span className="text-cyan-300">{cfg.micBDelayMs.toFixed(1)} ms</span>
            </div>
            <Slider
              value={[Math.round(cfg.micBDelayMs * 10)]}
              min={0}
              max={150}
              step={1}
              onValueChange={([v]) => patch({ dualEnabled: true, micBDelayMs: v / 10 })}
            />
          </div>

          <div className="text-[9px] text-muted-foreground leading-tight">
            콘 위 마이크를 드래그해 Cap(밝음)↔Edge(부드러움)·거리를 조절하세요.
          </div>
        </div>
      </div>
    </div>
  )
}
