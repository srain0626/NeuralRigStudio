import React, { useState } from "react"
import {
  ArrowLeft,
  ArrowRight,
  Plus,
  Power,
  Radio,
  Repeat,
  RotateCcw,
  SlidersHorizontal,
  Trash2,
} from "lucide-react"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { Hint } from "@/components/ui/tooltip"
import { CATEGORY_MAP, DEVICE_MAP, TEMPO_SYNC_DIVISIONS, supportsTempoSync } from "@/data/cortexCatalog"
import { useI18n } from "@/lib/i18n"
import type { MidiSnapshotDto, ModelProfileSummary, SignalChainConfigDto } from "@/types/rig"

interface CortexGridProps {
  chain: SignalChainConfigDto
  tones?: ModelProfileSummary[]
  selectedSlot: number
  midiState?: MidiSnapshotDto
  onSelectSlot: (idx: number, openCatalogIfEmpty?: boolean) => void
  onToggleBypass: (idx: number) => void
  onResetSlot: (idx: number) => void
  onRemoveSlot: (idx: number) => void
  onMoveOrSwap: (fromIdx: number, toIdx: number) => void
  onDropModelFromLibrary: (modelId: string, toIdx: number) => void
  onUpdateRouting?: (
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

  // Orthogonal routing coordinates aligned with 1000x272 SVG viewBox
  const colX = (col: number) => 56 + (col + 0.5) * 111
  const row1Y = 68
  const row2Y = 204
  const splitX = colX(splitCol) + 46
  const mergeX = colX(mergeCol) - 46

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
            `빈 슬롯 #${slotNum} — 클릭하여 장비 추가`,
            `Empty Slot #${slotNum} — Click to add gear`
          )}
        >
          <button
            type="button"
            onClick={() => onSelectSlot(slotIdx, true)}
            onContextMenu={(e) => {
              e.preventDefault()
              onSelectSlot(slotIdx, true)
            }}
            onDragOver={(e) => handleDragOver(e, slotIdx)}
            onDragLeave={() => setDragOverSlot(null)}
            onDrop={(e) => handleDrop(e, slotIdx)}
            className={`relative z-10 w-full max-w-[126px] h-[86px] rounded-[14px] flex items-center justify-center font-mono transition-all duration-200 cursor-pointer ${
              isDragOver
                ? "bg-white/15 border-2 border-dashed border-primary text-primary scale-[1.03]"
                : isSelected
                  ? "bg-white/10 border border-primary/80 text-oled-foreground shadow-[0_0_0_1px_var(--signal-hot)]"
                  : "bg-white/[0.03] border border-dashed border-white/14 text-white/35 hover:border-white/35 hover:text-white/80 hover:bg-white/[0.06]"
            }`}
          >
            <Plus className="size-5 opacity-80" strokeWidth={1.6} />
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

    // Primary parameter readout hairline (0..100%)
    const firstParam = def.params[0]
    const primaryPct =
      firstParam && firstParam.max > firstParam.min
        ? Math.max(
            6,
            Math.min(
              100,
              Math.round(
                (((slot.params[firstParam.index] ?? firstParam.defaultVal) - firstParam.min) /
                  (firstParam.max - firstParam.min)) *
                  100
              )
            )
          )
        : 50

    const catTag = def.category.toUpperCase()

    // Exact Landing Stage 2 Elevated Porcelain-White Card Language
    const cardStateClasses = bypassed
      ? isSelected
        ? "bg-white/[0.11] text-white/75 border-2 border-primary -translate-y-0.5 shadow-[0_10px_24px_rgba(255,107,36,0.25)]"
        : "bg-white/[0.06] text-white/50 border border-white/14 hover:bg-white/[0.09]"
      : isSelected
        ? "bg-[#ffffff] text-[#0b0e10] -translate-y-1 scale-[1.03] shadow-[0_14px_28px_rgba(255,107,36,0.36),0_0_0_2px_#ff6b24]"
        : "bg-[#f4f6f5] text-[#0b0e10] shadow-[0_8px_18px_rgba(0,0,0,0.28)] hover:-translate-y-0.5 hover:bg-[#ffffff]"

    return (
      <ContextMenu key={slotIdx}>
        <ContextMenuTrigger asChild>
          <div className="w-full max-w-[126px] min-w-0">
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
                onClick={() => onSelectSlot(slotIdx, false)}
                onDoubleClick={() => onToggleBypass(slotIdx)}
                className={`relative z-10 w-full max-w-[126px] min-w-0 overflow-hidden h-[86px] rounded-[14px] px-3 py-2.5 flex flex-col justify-between text-left transition-all duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] cursor-grab active:cursor-grabbing ${cardStateClasses} ${
                  isDragOver ? "ring-2 ring-primary scale-105" : ""
                }`}
              >
                {/* Top Micro Row: Category Stamp + Status Dot */}
                <div className="flex items-center justify-between gap-1 font-mono text-[10px] leading-none min-w-0">
                  <span
                    className={`font-semibold tracking-[0.05em] truncate ${
                      isSelected && !bypassed ? "text-[#ff6b24]" : "opacity-55"
                    }`}
                  >
                    {catTag}
                  </span>
                  <span
                    className="size-2 rounded-full shrink-0 transition-all duration-300"
                    style={{
                      backgroundColor: bypassed
                        ? "rgba(255,255,255,0.25)"
                        : isSelected
                          ? "#ff6b24"
                          : cat.hex,
                      boxShadow: bypassed
                        ? "none"
                        : `0 0 8px ${isSelected ? "#ff6b24" : cat.hex}`,
                    }}
                  />
                </div>

                {/* Center Model Title */}
                <div
                  className={`font-sans text-[13px] font-semibold tracking-[-0.015em] w-full min-w-0 truncate leading-tight ${
                    bypassed ? "line-through opacity-75" : ""
                  }`}
                >
                  {displayLabel}
                </div>

                {/* Bottom Parameter Hairline + Optional Sync/Mic Badge */}
                <div className="flex items-center justify-between gap-1.5 min-w-0">
                  <div
                    className={`h-[2px] flex-1 min-w-0 rounded-full overflow-hidden ${
                      bypassed ? "bg-white/15" : "bg-black/10"
                    }`}
                  >
                    <div
                      className="h-full transition-all duration-150"
                      style={{
                        width: `${primaryPct}%`,
                        backgroundColor: bypassed
                          ? "rgba(255,255,255,0.4)"
                          : isSelected
                            ? "#ff6b24"
                            : "#0b0e10",
                      }}
                    />
                  </div>
                  {(syncBadge || dualMicBadge || bypassBinding) && (
                    <span
                      className={`font-mono text-[8px] font-bold leading-none shrink-0 ${
                        isSelected && !bypassed ? "text-[#ff6b24]" : "opacity-65"
                      }`}
                    >
                      {syncBadge ?? (dualMicBadge ? "2×" : `CC${bypassBinding?.cc}`)}
                    </span>
                  )}
                </div>
              </button>
            </Hint>
          </div>
        </ContextMenuTrigger>
        <ContextMenuContent>
          <ContextMenuItem onSelect={() => onSelectSlot(slotIdx, true)}>
            <span className="flex items-center gap-2 min-w-0 font-medium">
              <Repeat className="size-3.5 text-primary shrink-0" />
              <span className="truncate">{tx("장비 변경…", "Change Gear…")}</span>
            </span>
          </ContextMenuItem>
          <ContextMenuItem onSelect={() => onSelectSlot(slotIdx, false)}>
            <span className="flex items-center gap-2 min-w-0">
              <SlidersHorizontal className="size-3.5 shrink-0" />
              <span className="truncate">{tx("세부 노브 열기", "Edit Parameters")}</span>
            </span>
          </ContextMenuItem>
          <ContextMenuSeparator />
          <ContextMenuItem onSelect={() => onToggleBypass(slotIdx)}>
            <span className="flex items-center gap-2 min-w-0">
              <Power className="size-3.5 shrink-0" />
              <span className="truncate">{slot.enabled ? tx("블록 바이패스", "Bypass Block") : tx("블록 활성화", "Enable Block")}</span>
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
              <span className="flex items-center gap-2 min-w-0">
                <Radio className="size-3.5 text-primary shrink-0" />
                <span className="truncate">
                  {bypassBinding
                    ? tx(`바이패스 MIDI CC#${bypassBinding.cc} 해제`, `Clear Bypass MIDI CC#${bypassBinding.cc}`)
                    : tx("바이패스 MIDI CC 학습", "Learn Bypass MIDI CC")}
                </span>
              </span>
            </ContextMenuItem>
          )}
          <ContextMenuSeparator />
          <ContextMenuItem disabled={slotIdx === 0} onSelect={() => onMoveOrSwap(slotIdx, slotIdx - 1)}>
            <span className="flex items-center gap-2 min-w-0">
              <ArrowLeft className="size-3.5 shrink-0" />
              <span className="truncate">{tx("왼쪽 슬롯과 교환", "Swap Left")}</span>
            </span>
          </ContextMenuItem>
          <ContextMenuItem disabled={slotIdx === 15} onSelect={() => onMoveOrSwap(slotIdx, slotIdx + 1)}>
            <span className="flex items-center gap-2 min-w-0">
              <ArrowRight className="size-3.5 shrink-0" />
              <span className="truncate">{tx("오른쪽 슬롯과 교환", "Swap Right")}</span>
            </span>
          </ContextMenuItem>
          <ContextMenuItem onSelect={() => onResetSlot(slotIdx)}>
            <span className="flex items-center gap-2 min-w-0">
              <RotateCcw className="size-3.5 shrink-0" />
              <span className="truncate">{tx("파라미터 기본값 복원", "Reset Parameters")}</span>
            </span>
          </ContextMenuItem>
          <ContextMenuSeparator />
          <ContextMenuItem variant="destructive" onSelect={() => onRemoveSlot(slotIdx)}>
            <span className="flex items-center gap-2 min-w-0">
              <Trash2 className="size-3.5 shrink-0" />
              <span className="truncate">{tx("슬롯에서 제거", "Remove Block")}</span>
            </span>
          </ContextMenuItem>
        </ContextMenuContent>
      </ContextMenu>
    )
  }

  const meterPctL = Math.min(100, Math.round(meterL * 100))
  const meterPctR = Math.min(100, Math.round(meterR * 100))

  return (
    <div
      data-tour="signal-stage"
      className="w-full max-w-[1300px] mx-auto rounded-[24px] border border-oled-border nr-obsidian-stage px-7 py-7 overflow-hidden"
    >
      {/* 2x8 Elevated Porcelain Module Matrix + Orthogonal Signal Cables */}
      <div className="relative select-none">
        <svg
          viewBox="0 0 1000 272"
          preserveAspectRatio="none"
          className="absolute inset-0 w-full h-[272px] pointer-events-none"
        >
          {/* Row 1 Bus */}
          <line
            x1="31"
            y1={row1Y}
            x2="969"
            y2={row1Y}
            stroke="rgba(255,255,255,0.14)"
            strokeWidth="1.6"
          />
          {signalHot && (
            <line
              x1="31"
              y1={row1Y}
              x2="969"
              y2={row1Y}
              stroke="var(--signal-hot)"
              strokeWidth="2"
              className="cable-signal-pulse"
            />
          )}

          {isParallel ? (
            <>
              <path
                d={`M ${splitX} ${row1Y} L ${splitX} ${row1Y + 54} Q ${splitX} ${row1Y + 68} ${splitX - 14} ${row1Y + 68} L 68 ${row1Y + 68} Q 54 ${row1Y + 68} 54 ${row1Y + 82} L 54 ${row2Y - 14} Q 54 ${row2Y} 68 ${row2Y} L 932 ${row2Y} Q 946 ${row2Y} 946 ${row2Y - 14} L 946 ${row1Y + 82} Q 946 ${row1Y + 68} 932 ${row1Y + 68} L ${mergeX + 14} ${row1Y + 68} Q ${mergeX} ${row1Y + 68} ${mergeX} ${row1Y + 54} L ${mergeX} ${row1Y}`}
                fill="none"
                stroke={hasRow2Active ? "var(--signal-warn)" : "rgba(255,255,255,0.12)"}
                strokeDasharray={hasRow2Active ? undefined : "5 4"}
                strokeWidth={hasRow2Active ? "1.9" : "1.4"}
                className={hasRow2Active && signalHot ? "cable-signal-pulse" : undefined}
              />
              <circle cx={splitX} cy={row1Y} r="4.5" fill="var(--signal-warn)" />
              <circle cx={mergeX} cy={row1Y} r="4.5" fill="var(--signal-warn)" />
            </>
          ) : (
            <>
              <path
                d={`M 946 ${row1Y} L 946 ${row1Y + 54} Q 946 ${row1Y + 68} 932 ${row1Y + 68} L 68 ${row1Y + 68} Q 54 ${row1Y + 68} 54 ${row1Y + 82} L 54 ${row2Y - 14} Q 54 ${row2Y} 68 ${row2Y} L 969 ${row2Y}`}
                fill="none"
                stroke="rgba(255,255,255,0.14)"
                strokeWidth="1.6"
              />
              {signalHot && (
                <path
                  d={`M 946 ${row1Y} L 946 ${row1Y + 54} Q 946 ${row1Y + 68} 932 ${row1Y + 68} L 68 ${row1Y + 68} Q 54 ${row1Y + 68} 54 ${row1Y + 82} L 54 ${row2Y - 14} Q 54 ${row2Y} 68 ${row2Y} L 969 ${row2Y}`}
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
        <div className="relative h-[136px] grid grid-cols-[62px_repeat(8,minmax(0,1fr))_62px] items-center gap-3">
          <div className="relative z-10 h-[86px] w-full rounded-[14px] bg-white/[0.06] border border-white/12 flex items-center justify-center font-mono text-xs font-medium text-oled-foreground/85">
            IN
          </div>

          {[0, 1, 2, 3, 4, 5, 6, 7].map((idx) => (
            <div key={idx} className="flex justify-center min-w-0">
              {renderSlot(idx)}
            </div>
          ))}

          <div className="h-[86px] w-full" aria-hidden="true" />
        </div>

        {/* Row 2 */}
        <div className="relative h-[136px] grid grid-cols-[62px_repeat(8,minmax(0,1fr))_62px] items-center gap-3">
          <div className="h-[86px] w-full" aria-hidden="true" />

          {[8, 9, 10, 11, 12, 13, 14, 15].map((idx) => (
            <div key={idx} className="flex justify-center min-w-0">
              {renderSlot(idx)}
            </div>
          ))}

          <div className="relative z-10 h-[86px] w-full rounded-[14px] bg-white/[0.06] border border-white/12 flex flex-col items-center justify-center gap-2 font-mono text-[11px] font-medium text-oled-foreground/85">
            <div className="flex items-center gap-1">
              <span>OUT</span>
              {clip && <span className="size-1.5 rounded-full bg-signal-hot" />}
            </div>
            <div className="flex flex-col gap-1 w-8">
              <div className="h-1 rounded-full bg-white/12 overflow-hidden">
                <div
                  className="h-full transition-all duration-75"
                  style={{
                    width: `${meterPctL}%`,
                    backgroundColor: clip ? "#ff6b24" : meterPctL > 82 ? "#ff8c42" : "#34d399",
                  }}
                />
              </div>
              <div className="h-1 rounded-full bg-white/12 overflow-hidden">
                <div
                  className="h-full transition-all duration-75"
                  style={{
                    width: `${meterPctR}%`,
                    backgroundColor: clip ? "#ff6b24" : meterPctR > 82 ? "#ff8c42" : "#34d399",
                  }}
                />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
