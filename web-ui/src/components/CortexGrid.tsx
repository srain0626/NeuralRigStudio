import React, { useState } from "react"
import { Minus, Plus } from "lucide-react"
import { CategoryVectorIcon } from "@/components/CortexIcons"
import { Slider } from "@/components/ui/slider"
import { CATEGORY_MAP, DEVICE_MAP } from "@/data/cortexCatalog"
import type { SignalChainConfigDto } from "@/types/rig"

interface CortexGridProps {
  chain: SignalChainConfigDto
  selectedSlot: number
  onSelectSlot: (idx: number) => void
  onToggleBypass: (idx: number) => void
  onMoveOrSwap: (fromIdx: number, toIdx: number) => void
  onDropModelFromLibrary: (modelId: string, toIdx: number) => void
  onUpdateRouting: (patch: Partial<Pick<SignalChainConfigDto, "splitCol" | "mergeCol" | "parallelMix" | "inputGainDb" | "outputGainDb">>) => void
  meterL: number
  meterR: number
  clip: boolean
}

export const CortexGrid: React.FC<CortexGridProps> = ({
  chain,
  selectedSlot,
  onSelectSlot,
  onToggleBypass,
  onMoveOrSwap,
  onDropModelFromLibrary,
  onUpdateRouting,
  meterL,
  meterR,
  clip,
}) => {
  const [dragOverSlot, setDragOverSlot] = useState<number | null>(null)
  const isParallel = chain.routingMode === 1
  const splitCol = Math.max(0, Math.min(7, chain.splitCol))
  const mergeCol = Math.max(splitCol, Math.min(7, chain.mergeCol))

  const handleDragStart = (e: React.DragEvent, slotIdx: number) => {
    e.dataTransfer.setData("application/x-cortex-slot", String(slotIdx))
    e.dataTransfer.effectAllowed = "move"
  }

  const handleDragOver = (e: React.DragEvent, slotIdx: number) => {
    e.preventDefault()
    e.dataTransfer.dropEffect = "move"
    if (dragOverSlot !== slotIdx) setDragOverSlot(slotIdx)
  }

  const handleDrop = (e: React.DragEvent, toIdx: number) => {
    e.preventDefault()
    setDragOverSlot(null)
    const libModel = e.dataTransfer.getData("application/x-cortex-model")
    if (libModel) {
      onDropModelFromLibrary(libModel, toIdx)
      return
    }
    const fromSlotStr = e.dataTransfer.getData("application/x-cortex-slot")
    if (fromSlotStr !== "") {
      const fromIdx = Number(fromSlotStr)
      if (!Number.isNaN(fromIdx) && fromIdx !== toIdx) {
        onMoveOrSwap(fromIdx, toIdx)
      }
    }
  }

  // Coordinate system for the SVG cable layer (viewBox 1000 x 236)
  // Left I/O pill center: x=42, Right I/O pill center: x=958
  // 8 slots centered evenly between x=120 and x=880 (step = 108.57)
  const colX = (col: number) => 126 + col * 106.85
  const row1Y = 58
  const row2Y = 176
  const splitX = colX(splitCol) + 46
  const mergeX = colX(mergeCol) - 46

  const renderSlot = (slotIdx: number) => {
    const slot = chain.slots[slotIdx]
    const def = slot?.modelId ? DEVICE_MAP[slot.modelId] : undefined
    const cat = def ? CATEGORY_MAP[def.category] : undefined
    const isSelected = selectedSlot === slotIdx
    const isDragOver = dragOverSlot === slotIdx

    if (!def || !cat) {
      return (
        <button
          key={slotIdx}
          type="button"
          onClick={() => onSelectSlot(slotIdx)}
          onDragOver={(e) => handleDragOver(e, slotIdx)}
          onDragLeave={() => setDragOverSlot(null)}
          onDrop={(e) => handleDrop(e, slotIdx)}
          className={`relative z-10 w-[58px] h-[58px] rounded-[14px] flex items-center justify-center transition-all ${
            isDragOver
              ? "bg-accent/70 border-2 border-dashed border-foreground/70 scale-105"
              : isSelected
                ? "bg-card/85 border border-foreground/60 shadow-[0_0_0_2px_rgba(255,255,255,0.12)]"
                : "bg-transparent hover:bg-card/60 border border-transparent hover:border-border"
          }`}
          title="빈 슬롯 — 클릭 후 왼쪽 라이브러리에서 페달/앰프를 선택하거나 드래그하세요"
        >
          <div
            className={`w-6 h-6 rounded-full flex items-center justify-center transition-opacity ${
              isSelected || isDragOver ? "bg-accent text-foreground opacity-100" : "bg-background/90 border border-border text-muted-foreground/70 opacity-65 hover:opacity-100"
            }`}
          >
            <Plus className="w-3.5 h-3.5" />
          </div>
        </button>
      )
    }

    const bypassed = !slot.enabled
    return (
      <div key={slotIdx} className="relative z-10 flex flex-col items-center">
        <button
          type="button"
          draggable
          onDragStart={(e) => handleDragStart(e, slotIdx)}
          onDragOver={(e) => handleDragOver(e, slotIdx)}
          onDragLeave={() => setDragOverSlot(null)}
          onDrop={(e) => handleDrop(e, slotIdx)}
          onClick={() => onSelectSlot(slotIdx)}
          onDoubleClick={() => onToggleBypass(slotIdx)}
          className={`w-[58px] h-[58px] rounded-[14px] flex items-center justify-center transition-all cursor-grab active:cursor-grabbing ${
            isDragOver ? "scale-105 ring-2 ring-foreground/80" : ""
          }`}
          style={{
            backgroundColor: isSelected && !bypassed ? `${cat.hex}26` : "var(--background)",
            border: `${isSelected ? 2.5 : 1.75}px solid ${bypassed ? `${cat.hex}44` : cat.hex}`,
            boxShadow: isSelected
              ? `0 0 18px ${cat.hex}55, inset 0 0 12px ${cat.hex}22`
              : bypassed
                ? "none"
                : `0 0 8px ${cat.hex}22`,
            opacity: bypassed ? 0.45 : 1,
          }}
          title={`${def.name} (${bypassed ? "바이패스됨" : "활성"}) — 더블클릭으로 ON/OFF, 드래그로 이동`}
        >
          <CategoryVectorIcon category={def.category} className="w-7 h-7 text-foreground" />
        </button>
        <span
          className={`mt-1 text-[10px] font-medium tracking-tight truncate max-w-[74px] transition-colors ${
            isSelected ? "text-foreground font-semibold" : bypassed ? "text-muted-foreground/45 line-through" : "text-muted-foreground"
          }`}
        >
          {def.shortName}
        </span>
      </div>
    )
  }

  const meterPctL = Math.min(100, Math.round(meterL * 100))
  const meterPctR = Math.min(100, Math.round(meterR * 100))

  return (
    <div className="relative flex-1 flex flex-col justify-center px-5 py-3 bg-background overflow-hidden">
      {/* Main 2x8 Grid Container */}
      <div className="relative w-full max-w-[1040px] mx-auto select-none">
        {/* SVG Routing Cables Layer */}
        <svg
          viewBox="0 0 1000 236"
          preserveAspectRatio="none"
          className="absolute inset-0 w-full h-[236px] pointer-events-none"
        >
          {/* Row 1 main horizontal signal line */}
          <line
            x1="64"
            y1={row1Y}
            x2="936"
            y2={row1Y}
            stroke="currentColor"
            className="text-foreground/35"
            strokeWidth="1.6"
          />

          {isParallel ? (
            <>
              {/* Split curve: Row 1 (splitX) -> Row 2 left */}
              <path
                d={`M ${splitX} ${row1Y} L ${splitX} ${row1Y + 46} Q ${splitX} ${row1Y + 60} ${splitX - 14} ${row1Y + 60} L 106 ${row1Y + 60} Q 92 ${row1Y + 60} 92 ${row1Y + 74} L 92 ${row2Y - 14} Q 92 ${row2Y} 106 ${row2Y} L 894 ${row2Y} Q 908 ${row2Y} 908 ${row2Y - 14} L 908 ${row1Y + 74} Q 908 ${row1Y + 60} 894 ${row1Y + 60} L ${mergeX + 14} ${row1Y + 60} Q ${mergeX} ${row1Y + 60} ${mergeX} ${row1Y + 46} L ${mergeX} ${row1Y}`}
                fill="none"
                stroke="currentColor"
                className="text-foreground/45"
                strokeWidth="1.6"
              />
              {/* Split & Merge junction dots */}
              <circle cx={splitX} cy={row1Y} r="4" className="fill-foreground/85" />
              <circle cx={mergeX} cy={row1Y} r="4" className="fill-foreground/85" />
            </>
          ) : (
            <>
              {/* Serial return cable from Row 1 end -> Row 2 start -> Out */}
              <path
                d={`M 908 ${row1Y} L 908 ${row1Y + 46} Q 908 ${row1Y + 60} 894 ${row1Y + 60} L 106 ${row1Y + 60} Q 92 ${row1Y + 60} 92 ${row1Y + 74} L 92 ${row2Y - 14} Q 92 ${row2Y} 106 ${row2Y} L 936 ${row2Y}`}
                fill="none"
                stroke="currentColor"
                className="text-foreground/35"
                strokeWidth="1.6"
              />
            </>
          )}
        </svg>

        {/* Row 1: In 1 + Slots 0..7 + Multi Out */}
        <div className="relative h-[118px] grid grid-cols-[68px_repeat(8,1fr)_68px] items-start pt-[29px] gap-1">
          {/* In 1 Hardware Pill */}
          <div className="relative z-10 h-[58px] w-[52px] mx-auto rounded-[12px] bg-secondary/90 border border-border flex flex-col items-center justify-center text-[11px] font-semibold text-foreground/90 leading-tight">
            <span>In</span>
            <span>1</span>
          </div>

          {[0, 1, 2, 3, 4, 5, 6, 7].map((idx) => (
            <div key={idx} className="flex justify-center">
              {renderSlot(idx)}
            </div>
          ))}

          {/* Multi Out Hardware Pill */}
          <div className="relative z-10 h-[58px] w-[52px] mx-auto rounded-[12px] bg-secondary/90 border border-border flex flex-col items-center justify-center text-[11px] font-semibold text-foreground/90 leading-tight">
            <span>Multi</span>
            <span>Out</span>
          </div>
        </div>

        {/* Row 2: + + Slots 8..15 + + */}
        <div className="relative h-[118px] grid grid-cols-[68px_repeat(8,1fr)_68px] items-start pt-[29px] gap-1">
          <div className="relative z-10 h-[58px] w-[52px] mx-auto rounded-[12px] bg-secondary/45 border border-border/60 flex items-center justify-center text-muted-foreground">
            <Plus className="w-4 h-4" />
          </div>

          {[8, 9, 10, 11, 12, 13, 14, 15].map((idx) => (
            <div key={idx} className="flex justify-center">
              {renderSlot(idx)}
            </div>
          ))}

          <div className="relative z-10 h-[58px] w-[52px] mx-auto rounded-[12px] bg-secondary/45 border border-border/60 flex items-center justify-center text-muted-foreground">
            <Plus className="w-4 h-4" />
          </div>
        </div>
      </div>

      {/* Live I/O Trim + Split/Merge Routing Controls + Stereo Meter Strip */}
      <div className="max-w-[1040px] w-full mx-auto mt-2.5 flex flex-wrap items-center justify-between gap-3 px-3 py-1.5 rounded-lg border border-border bg-card/55 text-[10px] font-mono text-muted-foreground">
        {/* Left: Input & Output Trim */}
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-1.5" title="더블클릭으로 0.0 dB 초기화">
            <span className="uppercase text-muted-foreground">IN</span>
            <div className="w-16">
              <Slider
                value={[chain.inputGainDb]}
                min={-12}
                max={12}
                step={0.5}
                onValueChange={([v]) => onUpdateRouting({ inputGainDb: v })}
              />
            </div>
            <button
              type="button"
              onDoubleClick={() => onUpdateRouting({ inputGainDb: 0 })}
              className="w-11 text-right text-foreground tabular-nums"
            >
              {chain.inputGainDb >= 0 ? `+${chain.inputGainDb.toFixed(1)}` : chain.inputGainDb.toFixed(1)}dB
            </button>
          </div>

          <div className="w-px h-3.5 bg-border" />

          <div className="flex items-center gap-1.5" title="더블클릭으로 0.0 dB 초기화">
            <span className="uppercase text-muted-foreground">OUT</span>
            <div className="w-16">
              <Slider
                value={[chain.outputGainDb]}
                min={-12}
                max={12}
                step={0.5}
                onValueChange={([v]) => onUpdateRouting({ outputGainDb: v })}
              />
            </div>
            <button
              type="button"
              onDoubleClick={() => onUpdateRouting({ outputGainDb: 0 })}
              className="w-11 text-right text-foreground tabular-nums"
            >
              {chain.outputGainDb >= 0 ? `+${chain.outputGainDb.toFixed(1)}` : chain.outputGainDb.toFixed(1)}dB
            </button>
          </div>
        </div>

        {/* Center: Parallel Split/Merge & Mix Controls */}
        {isParallel ? (
          <div className="flex items-center gap-3">
            <div className="flex items-center gap-1">
              <span>SPLIT</span>
              <button
                type="button"
                className="w-4 h-4 rounded bg-secondary hover:bg-accent flex items-center justify-center text-foreground disabled:opacity-35"
                disabled={splitCol <= 0}
                onClick={() => onUpdateRouting({ splitCol: Math.max(0, splitCol - 1) })}
              >
                <Minus className="w-2.5 h-2.5" />
              </button>
              <span className="w-3.5 text-center text-foreground font-semibold">{splitCol + 1}</span>
              <button
                type="button"
                className="w-4 h-4 rounded bg-secondary hover:bg-accent flex items-center justify-center text-foreground disabled:opacity-35"
                disabled={splitCol >= mergeCol}
                onClick={() => onUpdateRouting({ splitCol: Math.min(mergeCol, splitCol + 1) })}
              >
                <Plus className="w-2.5 h-2.5" />
              </button>
            </div>

            <div className="flex items-center gap-1">
              <span>MERGE</span>
              <button
                type="button"
                className="w-4 h-4 rounded bg-secondary hover:bg-accent flex items-center justify-center text-foreground disabled:opacity-35"
                disabled={mergeCol <= splitCol}
                onClick={() => onUpdateRouting({ mergeCol: Math.max(splitCol, mergeCol - 1) })}
              >
                <Minus className="w-2.5 h-2.5" />
              </button>
              <span className="w-3.5 text-center text-foreground font-semibold">{mergeCol + 1}</span>
              <button
                type="button"
                className="w-4 h-4 rounded bg-secondary hover:bg-accent flex items-center justify-center text-foreground disabled:opacity-35"
                disabled={mergeCol >= 7}
                onClick={() => onUpdateRouting({ mergeCol: Math.min(7, mergeCol + 1) })}
              >
                <Plus className="w-2.5 h-2.5" />
              </button>
            </div>

            <div className="flex items-center gap-1.5">
              <span>MIX</span>
              <div className="w-16">
                <Slider
                  value={[Math.round(chain.parallelMix * 100)]}
                  min={0}
                  max={100}
                  step={1}
                  onValueChange={([v]) => onUpdateRouting({ parallelMix: v / 100 })}
                />
              </div>
              <span className="w-7 text-right text-foreground tabular-nums">
                {Math.round(chain.parallelMix * 100)}%
              </span>
            </div>
          </div>
        ) : (
          <div className="uppercase tracking-wider text-muted-foreground/80">
            SERIAL CHAIN (ROW 1 → ROW 2)
          </div>
        )}

        {/* Right: Stereo Peak Meter */}
        <div className="flex items-center gap-2">
          <span>LVL</span>
          <div className="flex flex-col gap-0.5 w-24">
            <div className="h-1.5 rounded-full bg-secondary overflow-hidden">
              <div
                className="h-full rounded-full transition-all duration-75"
                style={{
                  width: `${meterPctL}%`,
                  backgroundColor: clip ? "var(--cat-amp)" : meterPctL > 82 ? "var(--cat-scene)" : "var(--cat-mod)",
                }}
              />
            </div>
            <div className="h-1.5 rounded-full bg-secondary overflow-hidden">
              <div
                className="h-full rounded-full transition-all duration-75"
                style={{
                  width: `${meterPctR}%`,
                  backgroundColor: clip ? "var(--cat-amp)" : meterPctR > 82 ? "var(--cat-scene)" : "var(--cat-mod)",
                }}
              />
            </div>
          </div>
          {clip && <span className="text-destructive font-bold">CLIP</span>}
        </div>
      </div>
    </div>
  )
}
