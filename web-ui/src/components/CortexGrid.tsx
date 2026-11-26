import React, { useEffect, useState } from "react"
import {
  ArrowLeft,
  ArrowRight,
  Lock,
  Plus,
  Power,
  Radio,
  Repeat,
  RotateCcw,
  SlidersHorizontal,
  Trash2,
  Unlock,
} from "lucide-react"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Slider } from "@/components/ui/slider"
import { Hint } from "@/components/ui/tooltip"
import { CATEGORY_MAP, DEVICE_MAP, TEMPO_SYNC_DIVISIONS, supportsTempoSync } from "@/data/cortexCatalog"
import { useI18n } from "@/lib/i18n"
import type { MidiSnapshotDto, ModelProfileSummary, SignalChainConfigDto } from "@/types/rig"

const IO_HEADROOM_PADS = [-12, -6, 0, 6]

function linearToDb(amp: number): number {
  if (!Number.isFinite(amp) || amp <= 1e-5) return -96
  return Math.max(-96, 20 * Math.log10(amp))
}

function dbToMeterPct(db: number): number {
  if (db <= -60) return 0
  return Math.max(0, Math.min(100, ((db + 60) / 60) * 100))
}

function formatDbReadout(db: number): string {
  if (db <= -85) return "-inf dB"
  return `${db >= 0 ? "+" : ""}${db.toFixed(1)} dB`
}

function formatGainDb(db: number): string {
  const rounded = Math.abs(db) < 0.05 ? 0 : db
  return `${rounded >= 0 ? "+" : ""}${rounded.toFixed(1)} dB`
}

function getMeterColor(db: number, clipped: boolean): string {
  if (clipped || db >= -3) return "#ff6b24"
  if (db >= -12) return "#f59e0b"
  return "#34d399"
}

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
  inMeterL?: number
  inMeterR?: number
  inClip?: boolean
  ioGainLock?: boolean
  onToggleIoGainLock?: () => void
  onIoGainBegin?: (kind: "inputGain" | "outputGain", valueDb: number) => void
  onIoGainChange?: (
    kind: "inputGain" | "outputGain",
    valueDb: number,
    opts?: { commit?: boolean; discrete?: boolean }
  ) => void
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
  inMeterL = 0,
  inMeterR = 0,
  inClip = false,
  ioGainLock = false,
  onToggleIoGainLock,
  onIoGainBegin,
  onIoGainChange,
}) => {
  const { tx } = useI18n()
  const [dragOverSlot, setDragOverSlot] = useState<number | null>(null)
  const [inClipLatched, setInClipLatched] = useState(false)
  const [outClipLatched, setOutClipLatched] = useState(false)

  useEffect(() => {
    if (inClip) setInClipLatched(true)
  }, [inClip])

  useEffect(() => {
    if (clip) setOutClipLatched(true)
  }, [clip])
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

  const inDbL = linearToDb(inMeterL)
  const inDbR = linearToDb(inMeterR)
  const inDbMax = Math.max(inDbL, inDbR)
  const inPctL = dbToMeterPct(inDbL)
  const inPctR = dbToMeterPct(inDbR)
  const inPeaking = inClip || inClipLatched

  const outDbL = linearToDb(meterL)
  const outDbR = linearToDb(meterR)
  const outDbMax = Math.max(outDbL, outDbR)
  const outPctL = dbToMeterPct(outDbL)
  const outPctR = dbToMeterPct(outDbR)
  const outPeaking = clip || outClipLatched

  const applyIoGain = (
    kind: "inputGain" | "outputGain",
    valueDb: number,
    opts?: { commit?: boolean; discrete?: boolean }
  ) => {
    const clamped = Math.round(Math.max(-24, Math.min(24, valueDb)) * 10) / 10
    onIoGainChange?.(kind, clamped, opts)
  }

  const nudgeIoGainByWheel = (kind: "inputGain" | "outputGain", e: React.WheelEvent) => {
    e.preventDefault()
    const current = kind === "inputGain" ? chain.inputGainDb : chain.outputGainDb
    const delta = e.deltaY < 0 ? 0.5 : -0.5
    applyIoGain(kind, current + delta, { commit: true, discrete: true })
  }

  const renderIoMeterBars = (
    pctL: number,
    pctR: number,
    dbL: number,
    dbR: number,
    peaking: boolean,
    widthClass = "w-14"
  ) => (
    <div className={`flex flex-col gap-1 ${widthClass}`}>
      <div className="h-1.5 rounded-full bg-white/10 overflow-hidden">
        <div
          className="h-full transition-all duration-75"
          style={{
            width: `${pctL}%`,
            backgroundColor: getMeterColor(dbL, peaking),
          }}
        />
      </div>
      <div className="h-1.5 rounded-full bg-white/10 overflow-hidden">
        <div
          className="h-full transition-all duration-75"
          style={{
            width: `${pctR}%`,
            backgroundColor: getMeterColor(dbR, peaking),
          }}
        />
      </div>
    </div>
  )

  const renderIoPopover = (kind: "inputGain" | "outputGain") => {
    const isInput = kind === "inputGain"
    const gainDb = isInput ? chain.inputGainDb : chain.outputGainDb
    const pctL = isInput ? inPctL : outPctL
    const pctR = isInput ? inPctR : outPctR
    const dbL = isInput ? inDbL : outDbL
    const dbR = isInput ? inDbR : outDbR
    const dbMax = isInput ? inDbMax : outDbMax
    const peaking = isInput ? inPeaking : outPeaking
    const clearLatch = () => (isInput ? setInClipLatched(false) : setOutClipLatched(false))

    return (
      <PopoverContent
        align={isInput ? "start" : "end"}
        side={isInput ? "right" : "left"}
        className="w-64 rounded-[16px] border border-white/15 bg-oled-elevated text-oled-foreground p-3.5 space-y-3 shadow-2xl"
      >
        <div className="flex items-center justify-between gap-2">
          <div className="min-w-0">
            <div className="text-xs font-medium text-oled-foreground">
              {isInput ? tx("입력 트림 (Input Trim)", "Input Trim") : tx("마스터 출력 (Output Gain)", "Output Gain")}
            </div>
            <div className="text-[10px] font-mono text-oled-muted tabular-nums">
              {tx("피크", "Peak")} {formatDbReadout(dbMax)}
            </div>
          </div>
          <button
            type="button"
            onClick={() => applyIoGain(kind, 0, { commit: true, discrete: true })}
            className="h-6 px-2 rounded-full border border-white/15 bg-white/5 hover:bg-white/10 text-[10px] font-mono text-oled-foreground/90 inline-flex items-center gap-1 cursor-pointer"
          >
            <RotateCcw className="size-2.5" />
            <span>0 dB</span>
          </button>
        </div>

        <div className="flex items-center gap-2.5 rounded-[10px] border border-white/10 bg-black/30 px-2.5 py-2">
          <div className="flex-1">{renderIoMeterBars(pctL, pctR, dbL, dbR, peaking, "w-full")}</div>
          <button
            type="button"
            onClick={clearLatch}
            title={tx("클릭하여 PEAK 표시 초기화", "Click to clear PEAK latch")}
            className={`px-1.5 py-0.5 rounded text-[9px] font-mono font-semibold uppercase cursor-pointer transition-colors ${
              peaking
                ? "bg-signal-hot text-white shadow-[0_0_10px_rgba(255,107,36,0.55)]"
                : "bg-white/8 text-oled-muted hover:text-oled-foreground"
            }`}
          >
            PEAK
          </button>
        </div>

        <div className="space-y-1.5">
          <div className="flex items-center justify-between text-[11px] font-mono">
            <span className="text-oled-muted">-24 dB</span>
            <span className="font-semibold text-oled-foreground tabular-nums">{formatGainDb(gainDb)}</span>
            <span className="text-oled-muted">+24 dB</span>
          </div>
          <Slider
            value={[gainDb]}
            min={-24}
            max={24}
            step={0.5}
            showCenterTick
            trackClassName="h-1.5 bg-white/10 border-white/15"
            rangeClassName="bg-primary"
            thumbClassName="border-white bg-oled text-white"
            onPointerDown={() => onIoGainBegin?.(kind, gainDb)}
            onValueChange={([v]) => applyIoGain(kind, v)}
            onValueCommit={([v]) => applyIoGain(kind, v, { commit: true })}
          />
        </div>

        <div className="grid grid-cols-4 gap-1.5 pt-0.5">
          {IO_HEADROOM_PADS.map((pad) => {
            const active = Math.abs(gainDb - pad) < 0.05
            return (
              <button
                key={pad}
                type="button"
                onClick={() => applyIoGain(kind, pad, { commit: true, discrete: true })}
                className={`h-6 rounded-md border text-[10px] font-mono font-medium cursor-pointer transition-colors ${
                  active
                    ? "border-primary bg-primary text-white"
                    : "border-white/12 bg-white/[0.04] text-oled-foreground/85 hover:bg-white/10"
                }`}
              >
                {pad > 0 ? `+${pad}` : pad} dB
              </button>
            )
          })}
        </div>
      </PopoverContent>
    )
  }

  return (
    <div
      data-tour="signal-stage"
      className="w-full max-w-[1300px] mx-auto rounded-[24px] border border-oled-border nr-obsidian-stage px-7 pt-4 pb-7 overflow-hidden"
    >
      {/* Always-Visible Input / Output Gain Trim & Dual Peak Meter Strip */}
      <div className="flex flex-wrap items-center justify-between gap-3 pb-3.5 mb-4 border-b border-white/[0.08]">
        {/* IN Channel: Badge + Logarithmic Meter + Trim Slider + Readout */}
        <div className="flex items-center gap-2.5 min-w-0 flex-1 basis-[280px]">
          <Hint
            content={
              inPeaking
                ? tx("입력 피크 감지됨 · 클릭해 초기화", "Input peak detected · click to clear")
                : tx("입력 레벨 미터 (-60 ~ 0 dBFS)", "Input level meter (-60 to 0 dBFS)")
            }
          >
            <button
              type="button"
              onClick={() => setInClipLatched(false)}
              className={`h-6 px-2 rounded-full border font-mono text-[10px] font-semibold tracking-wider uppercase flex items-center gap-1.5 shrink-0 cursor-pointer transition-colors ${
                inPeaking
                  ? "border-signal-hot bg-signal-hot/20 text-signal-hot shadow-[0_0_12px_rgba(255,107,36,0.35)]"
                  : "border-white/12 bg-white/[0.05] text-oled-foreground/85 hover:bg-white/10"
              }`}
            >
              <span
                className={`size-1.5 rounded-full ${inPeaking ? "bg-signal-hot animate-pulse" : "bg-emerald-400/80"}`}
              />
              <span>{inPeaking ? "IN PEAK" : "IN"}</span>
            </button>
          </Hint>

          {renderIoMeterBars(inPctL, inPctR, inDbL, inDbR, inPeaking, "w-14 shrink-0")}

          <span className="font-mono text-[10px] text-oled-muted tabular-nums w-14 shrink-0">
            {formatDbReadout(inDbMax)}
          </span>

          <div className="min-w-[96px] max-w-[175px] flex-1">
            <Slider
              aria-label={tx("입력 게인", "Input Gain")}
              value={[chain.inputGainDb]}
              min={-24}
              max={24}
              step={0.5}
              showCenterTick
              trackClassName="h-1.5 bg-white/10 border-white/15"
              rangeClassName="bg-primary"
              thumbClassName="border-white bg-oled text-white"
              onPointerDown={() => onIoGainBegin?.("inputGain", chain.inputGainDb)}
              onValueChange={([v]) => applyIoGain("inputGain", v)}
              onValueCommit={([v]) => applyIoGain("inputGain", v, { commit: true })}
            />
          </div>

          <Hint content={tx("더블클릭해 0.0 dB로 초기화", "Double-click to reset to 0.0 dB")}>
            <button
              type="button"
              onDoubleClick={() => applyIoGain("inputGain", 0, { commit: true, discrete: true })}
              className="h-6 px-2 rounded-md border border-white/10 bg-white/[0.04] hover:bg-white/10 font-mono text-[11px] font-medium text-oled-foreground tabular-nums shrink-0 cursor-pointer"
            >
              {formatGainDb(chain.inputGainDb)}
            </button>
          </Hint>
        </div>

        {/* Center: Lock I/O Gain Across Presets */}
        <Hint
          content={tx(
            "활성화하면 프리셋을 바꿔도 현재 Input / Output 게인을 그대로 유지합니다.",
            "Lock Input / Output gain trim across preset changes."
          )}
        >
          <button
            type="button"
            onClick={onToggleIoGainLock}
            className={`h-6 px-2.5 rounded-full border font-mono text-[10px] font-medium inline-flex items-center gap-1.5 shrink-0 cursor-pointer transition-colors ${
              ioGainLock
                ? "border-primary/60 bg-primary/20 text-primary"
                : "border-white/10 bg-white/[0.04] text-oled-muted hover:text-oled-foreground hover:bg-white/[0.08]"
            }`}
          >
            {ioGainLock ? <Lock className="size-3" /> : <Unlock className="size-3" />}
            <span>{tx("I/O 고정", "I/O Lock")}</span>
          </button>
        </Hint>

        {/* OUT Channel: Readout + Trim Slider + Logarithmic Meter + Badge */}
        <div className="flex items-center justify-end gap-2.5 min-w-0 flex-1 basis-[280px]">
          <Hint content={tx("더블클릭해 0.0 dB로 초기화", "Double-click to reset to 0.0 dB")}>
            <button
              type="button"
              onDoubleClick={() => applyIoGain("outputGain", 0, { commit: true, discrete: true })}
              className="h-6 px-2 rounded-md border border-white/10 bg-white/[0.04] hover:bg-white/10 font-mono text-[11px] font-medium text-oled-foreground tabular-nums shrink-0 cursor-pointer"
            >
              {formatGainDb(chain.outputGainDb)}
            </button>
          </Hint>

          <div className="min-w-[96px] max-w-[175px] flex-1">
            <Slider
              aria-label={tx("출력 게인", "Output Gain")}
              value={[chain.outputGainDb]}
              min={-24}
              max={24}
              step={0.5}
              showCenterTick
              trackClassName="h-1.5 bg-white/10 border-white/15"
              rangeClassName="bg-primary"
              thumbClassName="border-white bg-oled text-white"
              onPointerDown={() => onIoGainBegin?.("outputGain", chain.outputGainDb)}
              onValueChange={([v]) => applyIoGain("outputGain", v)}
              onValueCommit={([v]) => applyIoGain("outputGain", v, { commit: true })}
            />
          </div>

          <span className="font-mono text-[10px] text-oled-muted tabular-nums w-14 text-right shrink-0">
            {formatDbReadout(outDbMax)}
          </span>

          {renderIoMeterBars(outPctL, outPctR, outDbL, outDbR, outPeaking, "w-14 shrink-0")}

          <Hint
            content={
              outPeaking
                ? tx("출력 피크 감지됨 · 클릭해 초기화", "Output peak detected · click to clear")
                : tx("출력 레벨 미터 (-60 ~ 0 dBFS)", "Output level meter (-60 to 0 dBFS)")
            }
          >
            <button
              type="button"
              onClick={() => setOutClipLatched(false)}
              className={`h-6 px-2 rounded-full border font-mono text-[10px] font-semibold tracking-wider uppercase flex items-center gap-1.5 shrink-0 cursor-pointer transition-colors ${
                outPeaking
                  ? "border-signal-hot bg-signal-hot/20 text-signal-hot shadow-[0_0_12px_rgba(255,107,36,0.35)]"
                  : "border-white/12 bg-white/[0.05] text-oled-foreground/85 hover:bg-white/10"
              }`}
            >
              <span
                className={`size-1.5 rounded-full ${outPeaking ? "bg-signal-hot animate-pulse" : "bg-emerald-400/80"}`}
              />
              <span>{outPeaking ? "OUT PEAK" : "OUT"}</span>
            </button>
          </Hint>
        </div>
      </div>

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
          <Popover>
            <PopoverTrigger asChild>
              <button
                type="button"
                onWheel={(e) => nudgeIoGainByWheel("inputGain", e)}
                title={tx("클릭: 입력 게인 조절 · 휠: ±0.5 dB", "Click: Input gain · Wheel: ±0.5 dB")}
                className={`relative z-10 h-[86px] w-full rounded-[14px] border flex flex-col items-center justify-center gap-1.5 font-mono text-[11px] font-medium text-oled-foreground/90 cursor-pointer transition-colors ${
                  inPeaking
                    ? "bg-signal-hot/15 border-signal-hot/60"
                    : "bg-white/[0.06] border-white/12 hover:bg-white/10"
                }`}
              >
                <div className="flex items-center gap-1">
                  <span>IN</span>
                  {inPeaking && <span className="size-1.5 rounded-full bg-signal-hot animate-pulse" />}
                </div>
                {renderIoMeterBars(inPctL, inPctR, inDbL, inDbR, inPeaking, "w-8")}
                <span className="text-[9px] text-oled-muted tabular-nums">
                  {chain.inputGainDb >= 0 ? `+${chain.inputGainDb.toFixed(1)}` : chain.inputGainDb.toFixed(1)}
                </span>
              </button>
            </PopoverTrigger>
            {renderIoPopover("inputGain")}
          </Popover>

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

          <Popover>
            <PopoverTrigger asChild>
              <button
                type="button"
                onWheel={(e) => nudgeIoGainByWheel("outputGain", e)}
                title={tx("클릭: 출력 게인 조절 · 휠: ±0.5 dB", "Click: Output gain · Wheel: ±0.5 dB")}
                className={`relative z-10 h-[86px] w-full rounded-[14px] border flex flex-col items-center justify-center gap-1.5 font-mono text-[11px] font-medium text-oled-foreground/90 cursor-pointer transition-colors ${
                  outPeaking
                    ? "bg-signal-hot/15 border-signal-hot/60"
                    : "bg-white/[0.06] border-white/12 hover:bg-white/10"
                }`}
              >
                <div className="flex items-center gap-1">
                  <span>OUT</span>
                  {outPeaking && <span className="size-1.5 rounded-full bg-signal-hot animate-pulse" />}
                </div>
                {renderIoMeterBars(outPctL, outPctR, outDbL, outDbR, outPeaking, "w-8")}
                <span className="text-[9px] text-oled-muted tabular-nums">
                  {chain.outputGainDb >= 0 ? `+${chain.outputGainDb.toFixed(1)}` : chain.outputGainDb.toFixed(1)}
                </span>
              </button>
            </PopoverTrigger>
            {renderIoPopover("outputGain")}
          </Popover>
        </div>
      </div>
    </div>
  )
}
