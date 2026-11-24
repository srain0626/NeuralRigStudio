import React, { useState } from "react"
import { ArrowLeft, ArrowRight, Minus, Plus, Power, Radio, RotateCcw, Trash2 } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { Separator } from "@/components/ui/separator"
import { Slider } from "@/components/ui/slider"
import { Hint } from "@/components/ui/tooltip"
import { CATEGORY_MAP, DEVICE_MAP, TEMPO_SYNC_DIVISIONS, supportsTempoSync } from "@/data/cortexCatalog"
import { useI18n } from "@/lib/i18n"
import type { MidiSnapshotDto, ModelProfileSummary, SignalChainConfigDto } from "@/types/rig"

interface CortexGridProps {
  chain: SignalChainConfigDto
  tones?: ModelProfileSummary[]
  selectedSlot: number
  midiState?: MidiSnapshotDto
  onSelectSlot: (idx: number) => void
  onToggleBypass: (idx: number) => void
  onResetSlot: (idx: number) => void
  onRemoveSlot: (idx: number) => void
  onMoveOrSwap: (fromIdx: number, toIdx: number) => void
  onDropModelFromLibrary: (modelId: string, toIdx: number) => void
  onUpdateRouting: (
    patch: Partial<
      Pick<SignalChainConfigDto, "splitCol" | "mergeCol" | "parallelMix" | "inputGainDb" | "outputGainDb">
    >
  ) => void
  onStartMidiLearn?: (req: { targetKind: string; slotIndex?: number }) => void
  onClearMidiBinding?: (req: { targetKind: string; slotIndex?: number }) => void
  meterL: number
  meterR: number
  clip: boolean
}

export const CortexGrid: React.FC<CortexGridProps> = ({
  chain,
  tones,
  selectedSlot,
  midiState,
  onSelectSlot,
  onToggleBypass,
  onResetSlot,
  onRemoveSlot,
  onMoveOrSwap,
  onDropModelFromLibrary,
  onUpdateRouting,
  onStartMidiLearn,
  onClearMidiBinding,
  meterL,
  meterR,
  clip,
}) => {
  const { tx } = useI18n()
  const [dragOverSlot, setDragOverSlot] = useState<number | null>(null)
  const isParallel = chain.routingMode === 1
  const splitCol = Math.max(0, Math.min(7, chain.splitCol))
  const mergeCol = Math.max(splitCol, Math.min(7, chain.mergeCol))
  const hasRow2Active = chain.slots.slice(8, 16).some((s) => Boolean(s.modelId) && s.enabled)
  const signalHot = meterL > 0.012 || meterR > 0.012

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

  // Orthogonal routing cable coordinates aligned with 1000x224 SVG viewBox
  const colX = (col: number) => 60 + (col + 0.5) * 110
  const row1Y = 56
  const row2Y = 168
  const splitX = colX(splitCol) + 42
  const mergeX = colX(mergeCol) - 42

  const renderSlot = (slotIdx: number) => {
    const slot = chain.slots[slotIdx]
    const def = slot?.modelId ? DEVICE_MAP[slot.modelId] : undefined
    const cat = def ? CATEGORY_MAP[def.category] : undefined
    const isSelected = selectedSlot === slotIdx
    const isDragOver = dragOverSlot === slotIdx
    const slotNum = String(slotIdx + 1).padStart(2, "0")

    if (!def || !cat) {
      return (
        <Hint
          key={slotIdx}
          content={tx(
            `빈 슬롯 #${slotNum} — 클릭 후 왼쪽 카탈로그에서 장비를 배치하거나 드래그하세요`,
            `Empty Slot #${slotNum} — Click or drop gear from catalog`
          )}
        >
          <button
            type="button"
            onClick={() => onSelectSlot(slotIdx)}
            onDragOver={(e) => handleDragOver(e, slotIdx)}
            onDragLeave={() => setDragOverSlot(null)}
            onDrop={(e) => handleDrop(e, slotIdx)}
            className={`relative z-10 w-full max-w-[94px] h-[64px] rounded-[3px] flex flex-col items-center justify-center gap-1 font-mono transition-colors cursor-pointer ${
              isDragOver
                ? "bg-oled-elevated border-2 border-dashed border-primary text-primary"
                : isSelected
                  ? "bg-oled-elevated border-2 border-primary text-oled-foreground"
                  : "bg-oled/85 border border-oled-border/80 text-oled-muted hover:border-oled-foreground/45 hover:text-oled-foreground"
            }`}
          >
            <Plus className="size-3.5 opacity-75" strokeWidth={1.5} />
            <span className="text-[9px] tabular-nums opacity-70">{slotNum}</span>
          </button>
        </Hint>
      )
    }

    const bypassed = !slot.enabled
    const syncDiv = slot.syncDivision ?? 0
    const syncBadge =
      supportsTempoSync(slot.modelId) && syncDiv > 0
        ? TEMPO_SYNC_DIVISIONS.find((d) => d.value === syncDiv)?.short
        : null
    const dualMicBadge = def.category === "cab" && slot.cabMic?.dualEnabled
    const assignedNamName =
      slot.modelId === "nam_capture" && slot.namModelId
        ? tones?.find((t) => t.id === slot.namModelId)?.name
        : undefined
    const displayLabel = assignedNamName || def.shortName

    const bypassBinding = midiState?.bindings?.find(
      (b) => b.targetKind === "bypass" && b.slotIndex === slotIdx
    )

    // Mini dial indicator rotations from first 3 params
    const dialAngles = [0, 1, 2].map((pi) => {
      const pDef = def.params[pi]
      if (!pDef || pDef.max <= pDef.min) return -135
      const val = slot.params[pDef.index] ?? pDef.defaultVal
      const norm = Math.max(0, Math.min(1, (val - pDef.min) / (pDef.max - pDef.min)))
      return -135 + norm * 270
    })

    const tileBorderClass = isDragOver
      ? "border-2 border-dashed border-primary scale-[1.03]"
      : isSelected
        ? "border-2 border-primary bg-oled-elevated"
        : "border border-oled-border bg-oled-elevated/90 hover:border-oled-foreground/45"

    // Option 2: Category-specific Boutique Hardware Silhouette
    let silhouetteContent: React.ReactNode
    if (def.category === "amp") {
      silhouetteContent = (
        <div className="flex flex-col justify-between h-full w-full p-1.5">
          {/* Amp Grille Window */}
          <div
            className="h-6 rounded-[2px] border px-1.5 flex items-center justify-between"
            style={{
              borderColor: cat.hex,
              backgroundImage:
                "repeating-linear-gradient(90deg, rgba(242,239,233,0.12) 0px, rgba(242,239,233,0.12) 2px, transparent 2px, transparent 5px)",
            }}
          >
            <span className="bg-oled px-1 font-mono text-[9px] font-bold text-oled-foreground truncate max-w-[58px]">
              {displayLabel}
            </span>
            <span
              className={`size-1.5 rounded-full shrink-0 ${
                bypassed ? "bg-oled-muted/40" : "bg-signal-hot"
              }`}
            />
          </div>
          {/* Amp Faceplate Dials + Slot Index */}
          <div className="flex items-center justify-between px-0.5 pt-1">
            <div className="flex items-center gap-1">
              {dialAngles.map((deg, di) => (
                <svg key={di} width="12" height="12" viewBox="0 0 12 12">
                  <circle cx="6" cy="6" r="4.8" fill="none" stroke="rgba(242,239,233,0.45)" strokeWidth="1" />
                  <g transform={`rotate(${deg} 6 6)`}>
                    <line x1="6" y1="2.2" x2="6" y2="5.5" stroke={cat.hex} strokeWidth="1.4" strokeLinecap="round" />
                  </g>
                </svg>
              ))}
            </div>
            <span className="font-mono text-[9px] tabular-nums text-oled-muted">{slotNum}</span>
          </div>
        </div>
      )
    } else if (def.category === "cab") {
      silhouetteContent = (
        <div className="flex items-center justify-between h-full w-full px-2 py-1.5 gap-1.5">
          {/* Speaker Cone Acoustic Silhouette */}
          <svg width="34" height="34" viewBox="0 0 34 34" className="shrink-0">
            <rect x="1.5" y="1.5" width="31" height="31" rx="2" fill="none" stroke="rgba(242,239,233,0.2)" strokeWidth="1" />
            <circle cx="17" cy="17" r="12" fill="none" stroke={cat.hex} strokeWidth="1.5" />
            <circle cx="17" cy="17" r="4.5" fill="none" stroke="rgba(242,239,233,0.75)" strokeWidth="1.2" />
            <circle cx="22" cy="13" r="1.8" fill={dualMicBadge ? "var(--signal-ok)" : "var(--signal-warn)"} />
          </svg>
          <div className="flex flex-col justify-between items-end h-full min-w-0 py-0.5">
            <span className="font-mono text-[9px] tabular-nums text-oled-muted">
              {dualMicBadge ? "2×MIC" : slotNum}
            </span>
            <span className="font-mono text-[9px] font-bold text-oled-foreground truncate max-w-[44px]">
              {displayLabel}
            </span>
          </div>
        </div>
      )
    } else if (def.category === "nam") {
      silhouetteContent = (
        <div className="flex flex-col justify-between h-full w-full p-1.5">
          <div className="flex items-center justify-between">
            <svg width="20" height="14" viewBox="0 0 20 14" className="shrink-0">
              <circle cx="10" cy="7" r="5.5" fill="none" stroke={cat.hex} strokeWidth="1.2" />
              <line x1="5" y1="5" x2="15" y2="5" stroke="currentColor" strokeWidth="1" />
              <line x1="4" y1="7" x2="16" y2="7" stroke="currentColor" strokeWidth="1.2" />
              <line x1="5" y1="9" x2="15" y2="9" stroke="currentColor" strokeWidth="1" />
            </svg>
            <span className="font-mono text-[9px] tabular-nums text-oled-muted">{slotNum}</span>
          </div>
          <div className="font-mono text-[10px] font-bold text-oled-foreground truncate">
            {displayLabel}
          </div>
          <div
            className="h-2.5 w-full rounded-[1px] flex items-center justify-between px-1 font-mono text-[8px] font-bold text-oled"
            style={{ backgroundColor: cat.hex }}
          >
            <span>NAM</span>
            <span>{bypassed ? "BYP" : "ON"}</span>
          </div>
        </div>
      )
    } else {
      // Stompbox Enclosure Silhouette (Drive, Delay, Reverb, Mod, EQ, Dyn, Pitch)
      silhouetteContent = (
        <div className="flex flex-col justify-between h-full w-full p-1.5">
          {/* Top Mini Knobs + Jewel LED */}
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-1">
              {dialAngles.slice(0, 2).map((deg, di) => (
                <svg key={di} width="11" height="11" viewBox="0 0 12 12">
                  <circle cx="6" cy="6" r="4.5" fill="none" stroke="rgba(242,239,233,0.45)" strokeWidth="1" />
                  <g transform={`rotate(${deg} 6 6)`}>
                    <line x1="6" y1="2.2" x2="6" y2="5.5" stroke="#F2EFE9" strokeWidth="1.4" strokeLinecap="round" />
                  </g>
                </svg>
              ))}
            </div>
            <span
              className={`size-1.5 rounded-full ${
                bypassed ? "bg-oled-muted/35" : "bg-signal-ok"
              }`}
            />
          </div>

          {/* Model Nameplate */}
          <div
            className={`font-mono text-[10px] font-bold truncate leading-tight ${
              bypassed ? "line-through text-oled-muted" : "text-oled-foreground"
            }`}
          >
            {displayLabel}
          </div>

          {/* Colored Stomp Pad with Slot Index & Tempo Sync */}
          <div
            className="h-3.5 w-full rounded-[1px] px-1 flex items-center justify-between font-mono text-[8px] font-bold text-white"
            style={{ backgroundColor: cat.hex }}
          >
            <span className="tabular-nums">{slotNum}</span>
            <span>{syncBadge ?? (bypassBinding ? `CC${bypassBinding.cc}` : def.category.toUpperCase())}</span>
          </div>
        </div>
      )
    }

    return (
      <ContextMenu key={slotIdx}>
        <ContextMenuTrigger asChild>
          <div>
            <Hint
              content={tx(
                `${def.name} (${bypassed ? "바이패스됨" : "활성"}) — 더블클릭 ON/OFF · 우클릭 메뉴 · 드래그 이동`,
                `${def.name} (${bypassed ? "Bypassed" : "Active"}) — Double-click bypass · Right-click menu · Drag move`
              )}
            >
              <button
                type="button"
                draggable
                onDragStart={(e) => handleDragStart(e, slotIdx)}
                onDragOver={(e) => handleDragOver(e, slotIdx)}
                onDragLeave={() => setDragOverSlot(null)}
                onDrop={(e) => handleDrop(e, slotIdx)}
                onClick={() => onSelectSlot(slotIdx)}
                onDoubleClick={() => onToggleBypass(slotIdx)}
                className={`relative z-10 w-full max-w-[94px] h-[64px] rounded-[3px] text-left transition-all cursor-grab active:cursor-grabbing ${tileBorderClass} ${
                  bypassed ? "opacity-45" : "opacity-100"
                }`}
              >
                {silhouetteContent}
              </button>
            </Hint>
          </div>
        </ContextMenuTrigger>
        <ContextMenuContent>
          <ContextMenuItem onSelect={() => onToggleBypass(slotIdx)}>
            <span className="flex items-center gap-2">
              <Power className="size-3.5" />
              {slot.enabled ? tx("블록 바이패스", "Bypass Block") : tx("블록 활성화", "Enable Block")}
            </span>
          </ContextMenuItem>
          {(onStartMidiLearn || onClearMidiBinding) && (
            <ContextMenuItem
              onSelect={() => {
                if (bypassBinding && onClearMidiBinding) {
                  onClearMidiBinding({ targetKind: "bypass", slotIndex: slotIdx })
                } else if (onStartMidiLearn) {
                  onStartMidiLearn({ targetKind: "bypass", slotIndex: slotIdx })
                }
              }}
            >
              <span className="flex items-center gap-2">
                <Radio className="size-3.5 text-primary" />
                {bypassBinding
                  ? tx(`바이패스 MIDI CC#${bypassBinding.cc} 해제`, `Clear Bypass MIDI CC#${bypassBinding.cc}`)
                  : tx("바이패스 MIDI CC 학습", "Learn Bypass MIDI CC")}
              </span>
            </ContextMenuItem>
          )}
          <ContextMenuSeparator />
          <ContextMenuItem disabled={slotIdx === 0} onSelect={() => onMoveOrSwap(slotIdx, slotIdx - 1)}>
            <span className="flex items-center gap-2">
              <ArrowLeft className="size-3.5" />
              {tx("왼쪽 슬롯과 교환", "Swap Left")}
            </span>
          </ContextMenuItem>
          <ContextMenuItem disabled={slotIdx === 15} onSelect={() => onMoveOrSwap(slotIdx, slotIdx + 1)}>
            <span className="flex items-center gap-2">
              <ArrowRight className="size-3.5" />
              {tx("오른쪽 슬롯과 교환", "Swap Right")}
            </span>
          </ContextMenuItem>
          <ContextMenuItem onSelect={() => onResetSlot(slotIdx)}>
            <span className="flex items-center gap-2">
              <RotateCcw className="size-3.5" />
              {tx("파라미터 기본값 복원", "Reset Parameters")}
            </span>
          </ContextMenuItem>
          <ContextMenuSeparator />
          <ContextMenuItem variant="destructive" onSelect={() => onRemoveSlot(slotIdx)}>
            <span className="flex items-center gap-2">
              <Trash2 className="size-3.5" />
              {tx("슬롯에서 제거", "Remove Block")}
            </span>
          </ContextMenuItem>
        </ContextMenuContent>
      </ContextMenu>
    )
  }

  const meterPctL = Math.min(100, Math.round(meterL * 100))
  const meterPctR = Math.min(100, Math.round(meterR * 100))

  return (
    <div className="flex-1 min-h-0 flex flex-col justify-center p-3.5 bg-background overflow-hidden">
      {/* Recessed Matte-Black OLED Patchboard Container */}
      <div className="w-full max-w-[1060px] mx-auto rounded-md border border-oled-border oled-dot-grid overflow-hidden flex flex-col">
        {/* Top OLED Technical Coordinate Strip */}
        <div className="h-7 px-3.5 border-b border-oled-border bg-oled-elevated/80 flex items-center justify-between font-mono text-[10px] text-oled-muted">
          <div className="flex items-center gap-2">
            <span
              className={`size-1.5 rounded-full ${
                signalHot ? "bg-signal-hot" : "bg-oled-muted/40"
              }`}
            />
            <span>SIGNAL BUS // {isParallel ? `PARALLEL SPLIT C${splitCol + 1}→C${mergeCol + 1}` : "SERIAL R1 → R2"}</span>
          </div>
          <span className="tabular-nums">
            {signalHot ? tx("SIGNAL FLOW ACTIVE", "SIGNAL FLOW ACTIVE") : "0B RT ALLOC · 48.0 KHZ"}
          </span>
        </div>

        {/* 2x8 Hardware Silhouette Grid + Orthogonal Signal Cables */}
        <div className="relative px-3 py-3 select-none">
          <svg
            viewBox="0 0 1000 224"
            preserveAspectRatio="none"
            className="absolute inset-x-3 top-3 w-[calc(100%-1.5rem)] h-[224px] pointer-events-none"
          >
            {/* Base Row 1 Cable */}
            <line
              x1="34"
              y1={row1Y}
              x2="966"
              y2={row1Y}
              stroke="var(--border-oled)"
              strokeWidth="2.2"
            />
            {signalHot && (
              <line
                x1="34"
                y1={row1Y}
                x2="966"
                y2={row1Y}
                stroke="var(--signal-hot)"
                strokeWidth="2"
                className="cable-signal-pulse"
              />
            )}

            {isParallel ? (
              <>
                <path
                  d={`M ${splitX} ${row1Y} L ${splitX} ${row1Y + 42} Q ${splitX} ${row1Y + 56} ${splitX - 14} ${row1Y + 56} L 68 ${row1Y + 56} Q 54 ${row1Y + 56} 54 ${row1Y + 70} L 54 ${row2Y - 14} Q 54 ${row2Y} 68 ${row2Y} L 932 ${row2Y} Q 946 ${row2Y} 946 ${row2Y - 14} L 946 ${row1Y + 70} Q 946 ${row1Y + 56} 932 ${row1Y + 56} L ${mergeX + 14} ${row1Y + 56} Q ${mergeX} ${row1Y + 56} ${mergeX} ${row1Y + 42} L ${mergeX} ${row1Y}`}
                  fill="none"
                  stroke={hasRow2Active ? "var(--signal-warn)" : "var(--border-oled)"}
                  strokeDasharray={hasRow2Active ? undefined : "5 4"}
                  strokeWidth={hasRow2Active ? "2" : "1.5"}
                  className={hasRow2Active && signalHot ? "cable-signal-pulse" : undefined}
                />
                <circle cx={splitX} cy={row1Y} r="4.5" fill="var(--signal-warn)" />
                <circle cx={mergeX} cy={row1Y} r="4.5" fill="var(--signal-warn)" />
              </>
            ) : (
              <>
                <path
                  d={`M 946 ${row1Y} L 946 ${row1Y + 42} Q 946 ${row1Y + 56} 932 ${row1Y + 56} L 68 ${row1Y + 56} Q 54 ${row1Y + 56} 54 ${row1Y + 70} L 54 ${row2Y - 14} Q 54 ${row2Y} 68 ${row2Y} L 966 ${row2Y}`}
                  fill="none"
                  stroke="var(--border-oled)"
                  strokeWidth="2.2"
                />
                {signalHot && (
                  <path
                    d={`M 946 ${row1Y} L 946 ${row1Y + 42} Q 946 ${row1Y + 56} 932 ${row1Y + 56} L 68 ${row1Y + 56} Q 54 ${row1Y + 56} 54 ${row1Y + 70} L 54 ${row2Y - 14} Q 54 ${row2Y} 68 ${row2Y} L 966 ${row2Y}`}
                    fill="none"
                    stroke="var(--signal-hot)"
                    strokeWidth="2"
                    className="cable-signal-pulse"
                  />
                )}
              </>
            )}
          </svg>

          {/* Row 1 */}
          <div className="relative h-[112px] grid grid-cols-[54px_repeat(8,minmax(0,1fr))_54px] items-center gap-2">
            <div className="relative z-10 h-[64px] w-full rounded-[3px] bg-oled-elevated border border-oled-border flex flex-col items-center justify-center font-mono text-[10px] font-bold text-oled-foreground">
              <span>IN.1</span>
              <span className="text-[8px] text-oled-muted">DI</span>
            </div>

            {[0, 1, 2, 3, 4, 5, 6, 7].map((idx) => (
              <div key={idx} className="flex justify-center">
                {renderSlot(idx)}
              </div>
            ))}

            <div className="relative z-10 h-[64px] w-full rounded-[3px] bg-oled-elevated border border-oled-border flex flex-col items-center justify-center font-mono text-[10px] font-bold text-oled-foreground">
              <span>{isParallel ? "SUM" : "R1→"}</span>
              <span className="text-[8px] text-oled-muted">BUS</span>
            </div>
          </div>

          {/* Row 2 */}
          <div className="relative h-[112px] grid grid-cols-[54px_repeat(8,minmax(0,1fr))_54px] items-center gap-2">
            <div className="relative z-10 h-[64px] w-full rounded-[3px] bg-oled-elevated/65 border border-oled-border flex flex-col items-center justify-center font-mono text-[10px] font-bold text-oled-muted">
              <span>{isParallel ? "B" : "→R2"}</span>
              <span className="text-[8px]">RET</span>
            </div>

            {[8, 9, 10, 11, 12, 13, 14, 15].map((idx) => (
              <div key={idx} className="flex justify-center">
                {renderSlot(idx)}
              </div>
            ))}

            <div className="relative z-10 h-[64px] w-full rounded-[3px] bg-oled-elevated border border-oled-border flex flex-col items-center justify-center font-mono text-[10px] font-bold text-oled-foreground">
              <span>OUT</span>
              <span className="text-[8px] text-oled-muted tabular-nums">
                {isParallel ? `${Math.round(chain.parallelMix * 100)}%` : "L/R"}
              </span>
            </div>
          </div>
        </div>

        {/* Bottom OLED Trim + Split/Merge + Stereo Peak Strip */}
        <div className="h-9 px-3.5 border-t border-oled-border bg-oled-elevated/90 flex flex-wrap items-center justify-between gap-3 font-mono text-[10px] text-oled-muted">
          {/* Input & Output Trim */}
          <div className="flex items-center gap-3">
            <Hint content={tx("입력 게인 트림 (더블클릭 0.0dB 초기화)", "Input trim (double-click 0.0dB)")}>
              <div className="flex items-center gap-1.5">
                <span className="font-bold">IN</span>
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
                  className="w-11 text-right text-oled-foreground tabular-nums cursor-pointer"
                >
                  {chain.inputGainDb >= 0 ? `+${chain.inputGainDb.toFixed(1)}` : chain.inputGainDb.toFixed(1)}dB
                </button>
              </div>
            </Hint>

            <Separator orientation="vertical" className="bg-oled-border" />

            <Hint content={tx("마스터 출력 게인 (더블클릭 0.0dB 초기화)", "Output gain (double-click 0.0dB)")}>
              <div className="flex items-center gap-1.5">
                <span className="font-bold">OUT</span>
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
                  className="w-11 text-right text-oled-foreground tabular-nums cursor-pointer"
                >
                  {chain.outputGainDb >= 0 ? `+${chain.outputGainDb.toFixed(1)}` : chain.outputGainDb.toFixed(1)}dB
                </button>
              </div>
            </Hint>
          </div>

          {/* Center: Parallel Controls when active */}
          {isParallel ? (
            <div className="flex items-center gap-2.5">
              <div className="flex items-center gap-1">
                <span>SPLIT</span>
                <Button
                  size="icon-xs"
                  variant="oled"
                  disabled={splitCol <= 0}
                  onClick={() => onUpdateRouting({ splitCol: Math.max(0, splitCol - 1) })}
                >
                  <Minus className="size-2.5" />
                </Button>
                <span className="w-4 text-center text-oled-foreground font-bold tabular-nums">
                  {splitCol + 1}
                </span>
                <Button
                  size="icon-xs"
                  variant="oled"
                  disabled={splitCol >= mergeCol}
                  onClick={() => onUpdateRouting({ splitCol: Math.min(mergeCol, splitCol + 1) })}
                >
                  <Plus className="size-2.5" />
                </Button>
              </div>

              <div className="flex items-center gap-1">
                <span>MERGE</span>
                <Button
                  size="icon-xs"
                  variant="oled"
                  disabled={mergeCol <= splitCol}
                  onClick={() => onUpdateRouting({ mergeCol: Math.max(splitCol, mergeCol - 1) })}
                >
                  <Minus className="size-2.5" />
                </Button>
                <span className="w-4 text-center text-oled-foreground font-bold tabular-nums">
                  {mergeCol + 1}
                </span>
                <Button
                  size="icon-xs"
                  variant="oled"
                  disabled={mergeCol >= 7}
                  onClick={() => onUpdateRouting({ mergeCol: Math.min(7, mergeCol + 1) })}
                >
                  <Plus className="size-2.5" />
                </Button>
              </div>

              <div className="flex items-center gap-1.5">
                <span>MIX</span>
                <div className="w-14">
                  <Slider
                    value={[Math.round(chain.parallelMix * 100)]}
                    min={0}
                    max={100}
                    step={1}
                    onValueChange={([v]) => onUpdateRouting({ parallelMix: v / 100 })}
                  />
                </div>
                <span className="w-8 text-right text-oled-foreground tabular-nums">
                  {Math.round(chain.parallelMix * 100)}%
                </span>
              </div>
            </div>
          ) : (
            <span className="text-[10px] tracking-wider text-oled-muted">
              {tx("DRAG MODULES TO REORDER · DOUBLE-CLICK BYPASS", "DRAG MODULES TO REORDER · DOUBLE-CLICK BYPASS")}
            </span>
          )}

          {/* Stereo Output Peak Meter */}
          <div className="flex items-center gap-2">
            <span className="font-bold">L/R</span>
            <div className="flex flex-col gap-1 w-24">
              <div className="h-1.5 rounded-[1px] bg-oled overflow-hidden border border-oled-border">
                <div
                  className="h-full transition-all duration-75"
                  style={{
                    width: `${meterPctL}%`,
                    backgroundColor: clip
                      ? "var(--signal-hot)"
                      : meterPctL > 82
                        ? "var(--signal-warn)"
                        : "var(--signal-ok)",
                  }}
                />
              </div>
              <div className="h-1.5 rounded-[1px] bg-oled overflow-hidden border border-oled-border">
                <div
                  className="h-full transition-all duration-75"
                  style={{
                    width: `${meterPctR}%`,
                    backgroundColor: clip
                      ? "var(--signal-hot)"
                      : meterPctR > 82
                        ? "var(--signal-warn)"
                        : "var(--signal-ok)",
                  }}
                />
              </div>
            </div>
            {clip && (
              <Badge variant="hot" size="xs">
                CLIP
              </Badge>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
