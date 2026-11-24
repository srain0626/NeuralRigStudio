import React, { useEffect, useMemo, useRef, useState } from "react"
import {
  ArrowLeft,
  ArrowRight,
  ChevronLeft,
  ChevronRight,
  Clock,
  Cpu,
  Download,
  FolderOpen,
  GitBranch,
  Globe,
  Mic,
  Package,
  PanelLeftClose,
  PanelLeftOpen,
  Power,
  Radio,
  RotateCcw,
  Save,
  Search,
  Trash2,
  Upload,
  X,
} from "lucide-react"
import { CategoryVectorIcon } from "@/components/CortexIcons"
import { CortexGrid } from "@/components/CortexGrid"
import { CortexKnob, CortexToggleCell } from "@/components/CortexKnob"
import { GuitarAnalyzerContent } from "@/components/GuitarAnalyzerDialog"
import { LooperRecorderDeck } from "@/components/LooperRecorderDeck"
import { VirtualCabRoom } from "@/components/VirtualCabRoom"
import { VisualizerCanvas } from "@/components/VisualizerCanvas"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { Input } from "@/components/ui/input"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { ScrollArea } from "@/components/ui/scroll-area"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Separator } from "@/components/ui/separator"
import { Toggle } from "@/components/ui/toggle"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { Hint } from "@/components/ui/tooltip"
import {
  CATEGORIES,
  CATEGORY_MAP,
  DEVICE_CATALOG,
  DEVICE_MAP,
  SCENE_LABELS,
  TEMPO_SYNC_DIVISIONS,
  computeSyncedValueLabel,
  createDefaultParams,
  matchesDeviceSearch,
  supportsTempoSync,
  switchSceneOnChain,
  syncScenesOnAssignSlot,
  syncScenesOnSwapSlots,
} from "@/data/cortexCatalog"
import { studioBridge } from "@/lib/bridge"
import { useI18n } from "@/lib/i18n"
import type {
  BackingTrackStateDto,
  CabMicConfigDto,
  DeviceCategoryId,
  GuitarAnalysisLiveStateDto,
  GuitarProfileDto,
  LooperSnapshotDto,
  MidiSnapshotDto,
  ModelProfileSummary,
  RecorderReampSnapshotDto,
  SignalChainConfigDto,
  TelemetryFrame,
} from "@/types/rig"

interface CortexStudioProps {
  activeView: "grid" | "directory" | "scope"
  catalogVersion?: number
  onChangeView: (view: "grid" | "directory" | "scope") => void
  chain: SignalChainConfigDto
  savedPresets: SignalChainConfigDto[]
  allPresets: SignalChainConfigDto[]
  selectedSlot: number
  onSelectSlot: (idx: number) => void
  onPushChain: (next: SignalChainConfigDto, immediate?: boolean, preserveScenes?: boolean) => void
  onLoadPreset: (preset: SignalChainConfigDto) => void
  onAssignModel: (modelId: string, slotIdx: number) => void
  onSavedPresetsChange: (presets: SignalChainConfigDto[]) => void
  tones: ModelProfileSummary[]
  selectedToneId: string
  customCabIrName: string
  initialBacking?: BackingTrackStateDto
  initialLooper?: LooperSnapshotDto
  initialRecorder?: RecorderReampSnapshotDto
  isAudioRunning?: boolean
  isAudioBusy?: boolean
  guitarProfiles: GuitarProfileDto[]
  activeGuitarProfileId: string
  liveGuitarAnalysis: GuitarAnalysisLiveStateDto | null
  onLiveGuitarAnalysisChange: (next: GuitarAnalysisLiveStateDto | null) => void
  onGuitarProfilesUpdated: (profiles: GuitarProfileDto[], activeId: string) => void
  onStartAudio: () => void
  onOpenAudioSettings: () => void
  onOpenToneHub: () => void
  midiState?: MidiSnapshotDto
  onStartMidiLearn?: (req: {
    targetKind: string
    slotIndex?: number
    paramIndex?: number
    sceneIndex?: number
  }) => void
  onClearMidiBinding?: (req: {
    targetKind: string
    slotIndex?: number
    paramIndex?: number
    sceneIndex?: number
  }) => void
  onTonesUpdated: (tones: ModelProfileSummary[], selectedId: string) => void
  onSelectToneId: (id: string) => void
  onCustomCabIrChange: (name: string) => void
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

export const CortexStudio: React.FC<CortexStudioProps> = ({
  activeView,
  catalogVersion = 0,
  onChangeView,
  chain,
  savedPresets,
  allPresets,
  selectedSlot,
  onSelectSlot,
  onPushChain,
  onLoadPreset,
  onAssignModel,
  onSavedPresetsChange,
  tones,
  selectedToneId,
  customCabIrName,
  initialBacking,
  initialLooper,
  initialRecorder,
  isAudioRunning = false,
  isAudioBusy = false,
  guitarProfiles,
  activeGuitarProfileId,
  liveGuitarAnalysis,
  onLiveGuitarAnalysisChange,
  onGuitarProfilesUpdated,
  onStartAudio,
  onOpenAudioSettings,
  onOpenToneHub,
  midiState,
  onStartMidiLearn,
  onClearMidiBinding,
  onTonesUpdated,
  onSelectToneId,
  onCustomCabIrChange,
  onLog,
}) => {
  const { tx } = useI18n()

  const [activeCategory, setActiveCategory] = useState<DeviceCategoryId>("amp")
  const [catalogCollapsed, setCatalogCollapsed] = useState(false)
  const [searchQuery, setSearchQuery] = useState("")
  const [editingName, setEditingName] = useState(false)
  const [draftName, setDraftName] = useState(chain.presetName)
  const [editingBpm, setEditingBpm] = useState(false)
  const [draftBpm, setDraftBpm] = useState("120")
  const [cabRoomOpen, setCabRoomOpen] = useState(false)

  const tapTimesRef = useRef<number[]>([])

  const [meterL, setMeterL] = useState(0)
  const [meterR, setMeterR] = useState(0)
  const [clip, setClip] = useState(false)

  const [backing, setBacking] = useState<BackingTrackStateDto>(
    initialBacking ?? {
      loaded: false,
      playing: false,
      loop: true,
      volume: 0.7,
      positionSec: 0,
      durationSec: 0,
      name: "",
      path: "",
    }
  )

  const [looper, setLooper] = useState<LooperSnapshotDto>(
    initialLooper ?? {
      state: "empty",
      loopLengthFrames: 0,
      playheadFrames: 0,
      durationSec: 0,
      positionSec: 0,
      progressPct: 0,
      overdubLayers: 0,
      canUndo: false,
      canRedo: false,
      halfSpeed: false,
      reverse: false,
      quantizeSync: true,
      level: 1.0,
      sampleRate: 48000,
      maxDurationSec: 60,
    }
  )

  const [recorder, setRecorder] = useState<RecorderReampSnapshotDto>(
    initialRecorder ?? {
      recording: false,
      recordedSec: 0,
      droppedFrames: 0,
      lastDryPath: "",
      lastWetPath: "",
      lastDryName: "",
      recordingsDir: "",
      reampAvailable: false,
      reampEnabled: false,
      reampPlaying: false,
      reampLoop: true,
      reampPositionSec: 0,
      reampDurationSec: 0,
      reampName: "",
      reampPath: "",
    }
  )

  useEffect(() => {
    setDraftName(chain.presetName)
  }, [chain.presetName])

  useEffect(() => {
    if (initialBacking) setBacking(initialBacking)
  }, [initialBacking])

  useEffect(() => {
    if (initialLooper) setLooper(initialLooper)
  }, [initialLooper])

  useEffect(() => {
    if (initialRecorder) setRecorder(initialRecorder)
  }, [initialRecorder])

  useEffect(() => {
    const unsubLooper = studioBridge.on<LooperSnapshotDto>("looperStateChanged", (ev) => {
      if (ev) setLooper(ev)
    })
    const unsubRecorder = studioBridge.on<RecorderReampSnapshotDto>("recorderStateChanged", (ev) => {
      if (ev) setRecorder(ev)
    })

    let lastMeter = 0
    const unsub = studioBridge.on<TelemetryFrame>("telemetry", (f) => {
      const now = performance.now()
      if (now - lastMeter > 55) {
        lastMeter = now
        setMeterL(f.meterL || 0)
        setMeterR(f.meterR || 0)
        setClip(Boolean(f.clip))
      }
      if (f.backing) setBacking(f.backing)
      if (f.looper) setLooper(f.looper)
      if (f.recorder) setRecorder(f.recorder)
    })
    return () => {
      unsubLooper()
      unsubRecorder()
      unsub()
    }
  }, [])

  const handleLooperCommand = async (cmd: string, value?: boolean | number) => {
    if (cmd === "level" && typeof value === "number") {
      setLooper((prev) => ({ ...prev, level: value }))
    }
    if (!studioBridge.isWebView2) return
    try {
      const snap = await studioBridge.invoke<LooperSnapshotDto>("looperCommand", { cmd, value })
      if (snap) setLooper(snap)
    } catch (e) {
      onLog(tx(`루퍼 제어 실패: ${String(e)}`, `Looper error: ${String(e)}`), "error")
    }
  }

  const handleRecorderCommand = async (
    cmd: string,
    payload?: { value?: boolean | number; path?: string }
  ) => {
    if (!studioBridge.isWebView2) return
    try {
      const snap = await studioBridge.invoke<RecorderReampSnapshotDto>("recorderCommand", {
        cmd,
        ...payload,
      })
      if (snap) setRecorder(snap)
    } catch (e) {
      onLog(tx(`레코더/리앰핑 오류: ${String(e)}`, `Recorder error: ${String(e)}`), "error")
    }
  }

  const currentPresetIdx = useMemo(() => {
    const idx = allPresets.findIndex((p) => p.presetId === chain.presetId)
    return idx >= 0 ? idx : 0
  }, [allPresets, chain.presetId])

  const stepPreset = (delta: number) => {
    if (allPresets.length === 0) return
    const nextIdx = (currentPresetIdx + delta + allPresets.length) % allPresets.length
    onLoadPreset(allPresets[nextIdx])
  }

  const handleSavePreset = async () => {
    if (!studioBridge.isWebView2) {
      const filtered = savedPresets.filter((p) => p.presetId !== chain.presetId)
      onSavedPresetsChange([...filtered, chain])
      onLog(tx(`${chain.presetId} ${chain.presetName} 저장됨`, `Saved ${chain.presetId} ${chain.presetName}`), "success")
      return
    }
    try {
      const res = await studioBridge.invoke<{ savedPresets: SignalChainConfigDto[] }>(
        "saveUserPreset",
        chain
      )
      if (res?.savedPresets) onSavedPresetsChange(res.savedPresets)
      onLog(tx(`${chain.presetId} ${chain.presetName} 저장됨`, `Saved ${chain.presetId} ${chain.presetName}`), "success")
    } catch (e) {
      onLog(tx(`프리셋 저장 실패: ${String(e)}`, `Save preset failed: ${String(e)}`), "error")
    }
  }

  const handleDeleteUserPreset = async (presetId: string) => {
    if (!studioBridge.isWebView2) {
      onSavedPresetsChange(savedPresets.filter((p) => p.presetId !== presetId))
      return
    }
    try {
      const res = await studioBridge.invoke<{ savedPresets: SignalChainConfigDto[] }>(
        "deleteUserPreset",
        { presetId }
      )
      if (res?.savedPresets) onSavedPresetsChange(res.savedPresets)
    } catch (e) {
      onLog(tx(`프리셋 삭제 실패: ${String(e)}`, `Delete preset failed: ${String(e)}`), "error")
    }
  }

  const currentSlot = chain.slots[selectedSlot]
  const currentModel = currentSlot?.modelId ? DEVICE_MAP[currentSlot.modelId] : undefined
  const currentCategory = currentModel ? CATEGORY_MAP[currentModel.category] : undefined

  const handleSelectSlot = (idx: number) => {
    onSelectSlot(idx)
    const modelId = chain.slots[idx]?.modelId
    if (modelId && DEVICE_MAP[modelId]) {
      setActiveCategory(DEVICE_MAP[modelId].category)
    }
  }

  const toggleSlotBypass = (slotIdx: number) => {
    const target = chain.slots[slotIdx]
    if (!target?.modelId) return
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx
        ? { ...s, enabled: !s.enabled, params: [...s.params] }
        : { ...s, params: [...s.params] }
    )
    onPushChain({ ...chain, slots: nextSlots }, true)
  }

  const removeSlotBlock = (slotIdx: number) => {
    const emptyParams = [0, 0, 0, 0, 0, 0, 0, 0]
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx
        ? { index: i, modelId: "", enabled: true, params: [...emptyParams] }
        : { ...s, params: [...s.params] }
    )
    onPushChain(
      syncScenesOnAssignSlot({ ...chain, slots: nextSlots }, slotIdx, "", emptyParams),
      true,
      true
    )
  }

  const moveOrSwapSlot = (fromIdx: number, toIdx: number) => {
    if (fromIdx === toIdx || toIdx < 0 || toIdx >= 16) return
    const nextSlots = chain.slots.map((s) => ({ ...s, params: [...s.params] }))
    const a = { ...nextSlots[fromIdx], index: toIdx }
    const b = { ...nextSlots[toIdx], index: fromIdx }
    nextSlots[toIdx] = a
    nextSlots[fromIdx] = b
    onSelectSlot(toIdx)
    onPushChain(syncScenesOnSwapSlots({ ...chain, slots: nextSlots }, fromIdx, toIdx), true, true)
  }

  const updateSlotParam = (slotIdx: number, paramIdx: number, val: number) => {
    const nextSlots = chain.slots.map((s, i) => {
      if (i !== slotIdx) return s
      const p = [...s.params]
      p[paramIdx] = val
      return { ...s, params: p }
    })
    onPushChain({ ...chain, slots: nextSlots }, false)
  }

  const updateSlotSyncDivision = (slotIdx: number, syncDivision: number) => {
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx ? { ...s, syncDivision, params: [...s.params] } : s
    )
    onPushChain({ ...chain, slots: nextSlots }, true)
  }

  const updateSlotNamModel = (slotIdx: number, namModelId: string) => {
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx ? { ...s, namModelId, params: [...s.params] } : s
    )
    onPushChain({ ...chain, slots: nextSlots }, true)
    if (!selectedToneId) {
      void handleSelectNamModel(namModelId)
    }
  }

  const updateSlotCabMic = (slotIdx: number, cabMic: CabMicConfigDto) => {
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx ? { ...s, cabMic, params: [...s.params] } : s
    )
    onPushChain({ ...chain, slots: nextSlots }, false)
  }

  const handleTapTempo = () => {
    const now = performance.now()
    const prev = tapTimesRef.current.filter((t) => now - t < 2200)
    prev.push(now)
    if (prev.length > 4) prev.shift()
    tapTimesRef.current = prev
    if (prev.length >= 2) {
      const avgMs = (prev[prev.length - 1] - prev[0]) / (prev.length - 1)
      const bpm = Math.max(40, Math.min(280, Math.round((60000 / avgMs) * 2) / 2))
      onPushChain({ ...chain, tempoBpm: bpm }, true, true)
    }
  }

  const resetSlotDefaults = (slotIdx: number) => {
    const slot = chain.slots[slotIdx]
    if (!slot?.modelId) return
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx ? { ...s, params: createDefaultParams(slot.modelId) } : s
    )
    onPushChain({ ...chain, slots: nextSlots }, true)
  }

  const handleSelectNamModel = async (id: string) => {
    onSelectToneId(id)
    if (!studioBridge.isWebView2) return
    try {
      await studioBridge.invoke("selectTone", { id })
    } catch (e) {
      onLog(tx(`NAM 선택 실패: ${String(e)}`, `NAM select failed: ${String(e)}`), "error")
    }
  }

  const importNamWithSlot = async (slotIndex?: number) => {
    if (!studioBridge.isWebView2) return
    try {
      const payload = typeof slotIndex === "number" ? { slotIndex } : {}
      const res = await studioBridge.invoke<{
        cancelled?: boolean
        tones?: ModelProfileSummary[]
        selectedToneId?: string
        signalChain?: SignalChainConfigDto
      }>("importNamFile", payload)
      if (!res?.cancelled && res?.tones) {
        onTonesUpdated(res.tones, res.selectedToneId || "")
        if (res.signalChain) {
          onPushChain(res.signalChain, true, true)
        } else if (typeof slotIndex === "number" && res.selectedToneId) {
          updateSlotNamModel(slotIndex, res.selectedToneId)
        }
      }
    } catch (e) {
      onLog(tx(`NAM 가져오기 실패: ${String(e)}`, `NAM import failed: ${String(e)}`), "error")
    }
  }

  const handleImportNam = () => void importNamWithSlot()

  const handleExportNam = async () => {
    if (!studioBridge.isWebView2) return
    try {
      await studioBridge.invoke("exportNamFile", { toneId: selectedToneId })
    } catch (e) {
      onLog(tx(`NAM 내보내기 실패: ${String(e)}`, `NAM export failed: ${String(e)}`), "error")
    }
  }

  const handleDeleteNam = async (id: string) => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{
        tones?: ModelProfileSummary[]
        selectedToneId?: string
      }>("deleteTone", { id })
      if (res?.tones) onTonesUpdated(res.tones, res.selectedToneId || "")
    } catch (e) {
      onLog(tx(`NAM 삭제 실패: ${String(e)}`, `NAM delete failed: ${String(e)}`), "error")
    }
  }

  const handleLoadCabIr = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ cancelled?: boolean; customCabIrName?: string }>(
        "loadCustomCabIr",
        {}
      )
      if (!res?.cancelled && res?.customCabIrName !== undefined) {
        onCustomCabIrChange(res.customCabIrName)
      }
    } catch (e) {
      onLog(tx(`Cab IR 로드 실패: ${String(e)}`, `Cab IR load failed: ${String(e)}`), "error")
    }
  }

  const handleClearCabIr = async () => {
    if (!studioBridge.isWebView2) return
    try {
      await studioBridge.invoke("clearCustomCabIr")
      onCustomCabIrChange("")
    } catch (e) {
      onLog(tx(`Cab IR 초기화 실패: ${String(e)}`, `Cab IR clear failed: ${String(e)}`), "error")
    }
  }

  const handleLoadBacking = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ cancelled?: boolean; backing?: BackingTrackStateDto }>(
        "loadBackingTrack",
        {}
      )
      if (!res?.cancelled && res?.backing) setBacking(res.backing)
    } catch (e) {
      onLog(tx(`백킹 트랙 로드 실패: ${String(e)}`, `Backing load failed: ${String(e)}`), "error")
    }
  }

  const controlBacking = async (patch: {
    playing?: boolean
    loop?: boolean
    volume?: number
    seekSec?: number
  }) => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ backing: BackingTrackStateDto }>(
        "controlBackingTrack",
        patch
      )
      if (res?.backing) setBacking(res.backing)
    } catch (e) {
      onLog(tx(`백킹 트랙 제어 실패: ${String(e)}`, `Backing control failed: ${String(e)}`), "error")
    }
  }

  const unloadBacking = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ backing: BackingTrackStateDto }>("unloadBackingTrack")
      if (res?.backing) setBacking(res.backing)
    } catch (e) {
      onLog(tx(`백킹 트랙 해제 실패: ${String(e)}`, `Backing unload failed: ${String(e)}`), "error")
    }
  }

  const categoryModels = useMemo(() => {
    const q = searchQuery.trim()
    if (!q) return DEVICE_CATALOG.filter((d) => d.category === activeCategory)
    return DEVICE_CATALOG.filter((d) => matchesDeviceSearch(d, q))
  }, [activeCategory, searchQuery, catalogVersion])

  // Global Rig Keyboard Shortcuts (1–8 Scenes, B Bypass, Del Remove, T Tap, Space MR)
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName?.toLowerCase()
      const isInput =
        tag === "input" ||
        tag === "textarea" ||
        (e.target as HTMLElement | null)?.isContentEditable
      if (isInput || activeView !== "grid") return

      if (e.key >= "1" && e.key <= "8" && !e.ctrlKey && !e.altKey && !e.metaKey) {
        e.preventDefault()
        onPushChain(switchSceneOnChain(chain, Number(e.key) - 1), true, true)
      } else if (e.key.toLowerCase() === "b" && !e.ctrlKey && !e.metaKey) {
        e.preventDefault()
        toggleSlotBypass(selectedSlot)
      } else if ((e.key === "Delete" || e.key === "Backspace") && !e.ctrlKey) {
        e.preventDefault()
        removeSlotBlock(selectedSlot)
      } else if (e.key.toLowerCase() === "t" && !e.ctrlKey && !e.metaKey) {
        e.preventDefault()
        handleTapTempo()
      } else if (e.key === " " && backing.loaded) {
        e.preventDefault()
        void controlBacking({ playing: !backing.playing })
      }
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [activeView, chain, selectedSlot, backing.loaded, backing.playing])

  // ───────────────────────────────────────────────────────────────────────────
  // VIEW 2: VAULT (Presets, .nam Neural Captures, Cab IR, Tone Hub & VST3/CLAP)
  // ───────────────────────────────────────────────────────────────────────────
  if (activeView === "directory") {
    return (
      <div className="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-12 bg-background overflow-hidden">
        {/* Col 1: Presets Bank */}
        <div className="lg:col-span-4 border-r border-border flex flex-col min-h-0 bg-card">
          <div className="h-11 px-4 border-b border-border flex items-center justify-between gap-2 bg-secondary/40">
            <div className="flex items-center gap-2">
              <span className="font-mono text-xs font-bold tracking-wider uppercase">PRESET BANK</span>
              <Badge variant="outline" size="xs">
                {allPresets.length}
              </Badge>
            </div>
            <div className="flex items-center gap-1">
              <Button
                size="xs"
                variant="keycap"
                onClick={async () => {
                  if (!studioBridge.isWebView2) return
                  try {
                    const r = await studioBridge.invoke<{ cancelled?: boolean; summary?: string }>(
                      "importNrigBundle"
                    )
                    if (!r?.cancelled && r?.summary) onLog(r.summary, "success")
                  } catch (e) {
                    onLog(`.nrig import failed: ${String(e)}`, "error")
                  }
                }}
              >
                <Upload className="size-3" />
                .NRIG
              </Button>
              <Hint
                content={tx(
                  "현재 리그 + .nam + 커스텀 IR을 .nrig 번들로 내보내기",
                  "Export active rig + .nam + custom IR as .nrig"
                )}
              >
                <Button
                  size="xs"
                  variant="ghost"
                  onClick={async () => {
                    if (!studioBridge.isWebView2) return
                    try {
                      const r = await studioBridge.invoke<{ cancelled?: boolean; name?: string }>(
                        "exportNrigBundle"
                      )
                      if (!r?.cancelled && r?.name) onLog(`[.nrig] ${r.name} exported`, "success")
                    } catch (e) {
                      onLog(`.nrig export failed: ${String(e)}`, "error")
                    }
                  }}
                >
                  <Download className="size-3" />
                  {tx("내보내기", "EXPORT")}
                </Button>
              </Hint>
            </div>
          </div>

          <ScrollArea className="flex-1 p-2">
            <div className="space-y-1">
              {allPresets.map((p) => {
                const isActive = p.presetId === chain.presetId
                const isUser = savedPresets.some((u) => u.presetId === p.presetId)
                return (
                  <div
                    key={p.presetId}
                    className={`flex items-center justify-between px-3 py-2 rounded-sm border transition-colors cursor-pointer ${
                      isActive
                        ? "bg-primary text-primary-foreground border-primary"
                        : "bg-background border-border hover:bg-secondary/60 text-foreground"
                    }`}
                    onClick={() => {
                      onLoadPreset(p)
                      onChangeView("grid")
                    }}
                  >
                    <div className="flex items-center gap-2.5 min-w-0">
                      <span className="font-mono text-[11px] font-bold w-8 shrink-0 opacity-80">
                        {p.presetId}
                      </span>
                      <span className="font-display text-xs font-bold truncate">{p.presetName}</span>
                    </div>
                    {isUser && (
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        onClick={(e) => {
                          e.stopPropagation()
                          void handleDeleteUserPreset(p.presetId)
                        }}
                      >
                        <Trash2 className="size-3" />
                      </Button>
                    )}
                  </div>
                )
              })}
            </div>
          </ScrollArea>
        </div>

        {/* Col 2: Neural Captures (.nam) & Cab IR */}
        <div className="lg:col-span-4 border-r border-border flex flex-col min-h-0 bg-background">
          <div className="h-11 px-4 border-b border-border flex items-center justify-between bg-secondary/35">
            <span className="font-mono text-xs font-bold tracking-wider uppercase">
              NEURAL CAPTURES (.NAM)
            </span>
            <div className="flex items-center gap-1">
              <Button size="xs" variant="keycap" onClick={handleImportNam}>
                <Upload className="size-3" />
                {tx("가져오기", "IMPORT")}
              </Button>
              <Button
                size="icon-xs"
                variant="ghost"
                disabled={!selectedToneId}
                onClick={handleExportNam}
              >
                <Download className="size-3" />
              </Button>
            </div>
          </div>

          <ScrollArea className="flex-1 p-2">
            {tones.length === 0 ? (
              <div className="p-6 text-center text-xs text-muted-foreground">
                {tx(
                  "로드된 .nam 파일이 없습니다. 가져오기 버튼으로 Neural Amp Modeler 캡처를 추가하세요.",
                  "No .nam models loaded. Import official Neural Amp Modeler files above."
                )}
              </div>
            ) : (
              <div className="space-y-1">
                {tones.map((t) => {
                  const sel = t.id === selectedToneId
                  return (
                    <div
                      key={t.id}
                      onClick={() => void handleSelectNamModel(t.id)}
                      className={`flex items-center justify-between px-3 py-2 rounded-sm border cursor-pointer transition-colors ${
                        sel
                          ? "bg-secondary border-primary text-foreground"
                          : "border-border bg-card hover:bg-secondary/50 text-foreground"
                      }`}
                    >
                      <div className="min-w-0">
                        <div className="text-xs font-bold truncate">{t.name}</div>
                        <div className="text-[10px] font-mono text-muted-foreground tabular-nums">
                          {t.archPreset} · {t.sampleRate} Hz
                        </div>
                      </div>
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        onClick={(e) => {
                          e.stopPropagation()
                          void handleDeleteNam(t.id)
                        }}
                      >
                        <Trash2 className="size-3" />
                      </Button>
                    </div>
                  )
                })}
              </div>
            )}
          </ScrollArea>

          <div className="border-t border-border p-3.5 bg-card space-y-2">
            <div className="flex items-center justify-between">
              <span className="font-mono text-[10px] font-bold uppercase tracking-wider text-muted-foreground">
                CUSTOM CAB IR (.WAV)
              </span>
              <div className="flex items-center gap-1">
                <Button size="xs" variant="keycap" onClick={handleLoadCabIr}>
                  <FolderOpen className="size-3" />
                  {tx("IR 열기", "LOAD IR")}
                </Button>
                {customCabIrName && (
                  <Button size="icon-xs" variant="ghost" onClick={handleClearCabIr}>
                    <RotateCcw className="size-3" />
                  </Button>
                )}
              </div>
            </div>
            <div className="text-xs font-mono truncate text-foreground bg-background px-2.5 py-2 rounded-sm border border-border">
              {customCabIrName || tx("내장 512-tap 4x12 V30 Modern IR", "Built-in 512-tap 4x12 V30 Modern IR")}
            </div>
          </div>
        </div>

        {/* Col 3: Tone Hub & VST3/CLAP Packaging Card */}
        <div className="lg:col-span-4 flex flex-col min-h-0 p-4 gap-4 bg-background">
          <Card className="py-4 gap-3">
            <CardHeader>
              <div className="flex items-center justify-between">
                <CardTitle className="flex items-center gap-2 text-xs font-mono uppercase tracking-wider">
                  <Globe className="size-4 text-primary" />
                  TONE HUB &amp; .NRIG EXCHANGE
                </CardTitle>
                <Badge variant="hot" size="xs">
                  CURATED
                </Badge>
              </div>
            </CardHeader>
            <CardContent className="space-y-3 text-xs">
              <p className="text-muted-foreground leading-relaxed">
                {tx(
                  "검증된 WaveNet/LSTM/ConvNet 앰프 캡처, 8K 초저지연 캐비넷 IR, 아티스트 .nrig 번들을 원클릭으로 설치하고 적용할 수 있습니다.",
                  "Browse and install curated WaveNet/LSTM/ConvNet neural models, 8K IRs, and artist .nrig bundles."
                )}
              </p>
              <Button size="sm" variant="signal" className="w-full" onClick={onOpenToneHub}>
                <Package className="size-3.5" />
                {tx("Tone Hub 카탈로그 열기", "OPEN TONE HUB CATALOG")}
              </Button>
            </CardContent>
          </Card>

          <Card className="py-4 gap-3">
            <CardHeader>
              <div className="flex items-center justify-between">
                <CardTitle className="flex items-center gap-2 text-xs font-mono uppercase tracking-wider">
                  <Cpu className="size-4 text-primary" />
                  NATIVEAOT DAW PLUGIN
                </CardTitle>
                <Badge variant="ok" size="xs">
                  VST3 + CLAP
                </Badge>
              </div>
            </CardHeader>
            <CardContent className="space-y-3 text-xs">
              <p className="text-muted-foreground leading-relaxed">
                {tx(
                  "현재 구축된 시그널 체인과 .nam 모델을 32개 샘플 정확도 오토메이션을 지원하는 네이티브 VST3/CLAP 플러그인으로 패키징합니다.",
                  "Package the current rig into a zero-allocation NativeAOT VST3 & CLAP DAW plugin with 32 parameters."
                )}
              </p>
              <Button size="sm" variant="keycap" className="w-full" onClick={onOpenToneHub}>
                {tx("VST3 / CLAP 내보내기 센터", "OPEN VST3 / CLAP EXPORTER")}
              </Button>
            </CardContent>
          </Card>

          <div className="mt-auto rounded-sm border border-border bg-oled p-3.5 font-mono text-[11px] text-oled-foreground space-y-1.5">
            <div className="text-oled-muted text-[10px]">ACTIVE RIG SNAPSHOT</div>
            <div className="font-bold text-sm">
              {chain.presetId} · {chain.presetName}
            </div>
            <div className="text-oled-muted tabular-nums">
              SCENE {SCENE_LABELS[chain.activeScene]} · {Math.round(chain.tempoBpm ?? 120)} BPM ·{" "}
              {chain.routingMode === 1 ? "PARALLEL" : "SERIAL"}
            </div>
          </div>
        </div>
      </div>
    )
  }

  // ───────────────────────────────────────────────────────────────────────────
  // VIEW 3: SCOPE (Full-Size Acoustic Spectrum Lab + Guitar DI Profiler)
  // ───────────────────────────────────────────────────────────────────────────
  if (activeView === "scope") {
    return (
      <div className="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-12 gap-3 p-3.5 bg-background overflow-hidden">
        <div className="lg:col-span-7 flex flex-col min-h-0">
          <VisualizerCanvas
            activeToneLabel={customCabIrName || undefined}
            className="flex-1 min-h-0"
            canvasClassName="h-full min-h-[340px]"
          />
        </div>
        <div className="lg:col-span-5 flex flex-col min-h-0 rounded-md border border-border bg-card p-3.5 overflow-hidden">
          <div className="pb-2 mb-2 border-b border-border flex items-center justify-between">
            <span className="font-mono text-xs font-bold tracking-wider uppercase">
              GUITAR DI PROFILER
            </span>
            <Badge variant="outline" size="xs">
              {guitarProfiles.length} SAVED
            </Badge>
          </div>
          <GuitarAnalyzerContent
            isAudioRunning={isAudioRunning}
            isAudioBusy={isAudioBusy}
            onStartAudio={onStartAudio}
            onOpenAudioSettings={onOpenAudioSettings}
            profiles={guitarProfiles}
            activeProfileId={activeGuitarProfileId}
            liveAnalysis={liveGuitarAnalysis}
            onLiveAnalysisChange={onLiveGuitarAnalysisChange}
            onProfilesUpdated={onGuitarProfilesUpdated}
            onLog={onLog}
          />
        </div>
      </div>
    )
  }

  // ───────────────────────────────────────────────────────────────────────────
  // VIEW 1: RIG (Collapsible Catalog · Boutique Patchboard · Split Bottom Console)
  // ───────────────────────────────────────────────────────────────────────────
  const tapBinding = midiState?.bindings?.find((b) => b.targetKind === "tapTempo")

  return (
    <div className="flex-1 min-h-0 flex bg-background overflow-hidden">
      {/* Left Collapsible Editorial Gear Catalog */}
      {catalogCollapsed ? (
        <aside className="w-11 shrink-0 border-r border-border bg-card flex flex-col items-center py-2 gap-1.5">
          <Hint content={tx("장비 카탈로그 펼치기", "Expand Gear Catalog")} side="right">
            <Button
              size="icon-xs"
              variant="ghost"
              onClick={() => setCatalogCollapsed(false)}
            >
              <PanelLeftOpen className="size-3.5" />
            </Button>
          </Hint>
          <Separator className="w-6" />
          <ScrollArea className="flex-1 w-full">
            <div className="flex flex-col items-center gap-1 py-1">
              {CATEGORIES.map((cat) => {
                const active = activeCategory === cat.id
                return (
                  <Hint key={cat.id} content={cat.label} side="right">
                    <button
                      type="button"
                      onClick={() => {
                        setActiveCategory(cat.id)
                        setCatalogCollapsed(false)
                      }}
                      className={`relative w-8 h-8 rounded-sm flex items-center justify-center transition-colors cursor-pointer ${
                        active
                          ? "bg-secondary text-foreground border border-border"
                          : "text-muted-foreground hover:text-foreground hover:bg-secondary/50"
                      }`}
                    >
                      {active && (
                        <span
                          className="absolute left-0 top-1 bottom-1 w-[2.5px]"
                          style={{ backgroundColor: cat.hex }}
                        />
                      )}
                      <CategoryVectorIcon category={cat.id} className="size-4" />
                    </button>
                  </Hint>
                )
              })}
            </div>
          </ScrollArea>
        </aside>
      ) : (
        <aside className="w-64 shrink-0 border-r border-border bg-card flex flex-col min-h-0">
          {/* Catalog Top Bar */}
          <div className="px-3 py-2 border-b border-border space-y-2">
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-1.5 min-w-0">
                <span
                  className="size-2 rounded-[1px] shrink-0"
                  style={{ backgroundColor: CATEGORY_MAP[activeCategory].hex }}
                />
                <span className="font-mono text-[11px] font-bold tracking-wider uppercase truncate">
                  {searchQuery.trim()
                    ? tx(`검색 (${categoryModels.length})`, `MATCH (${categoryModels.length})`)
                    : CATEGORY_MAP[activeCategory].label}
                </span>
              </div>
              <Hint content={tx("카탈로그 접기", "Collapse Catalog")} side="right">
                <Button
                  size="icon-xs"
                  variant="ghost"
                  onClick={() => setCatalogCollapsed(true)}
                >
                  <PanelLeftClose className="size-3.5" />
                </Button>
              </Hint>
            </div>

            {/* Category Micro Chip Grid (5x2) */}
            <div className="grid grid-cols-5 gap-1">
              {CATEGORIES.map((cat) => {
                const active = activeCategory === cat.id && !searchQuery.trim()
                return (
                  <Hint key={cat.id} content={cat.label}>
                    <button
                      type="button"
                      onClick={() => {
                        setSearchQuery("")
                        setActiveCategory(cat.id)
                      }}
                      className={`relative h-7 rounded-sm border flex items-center justify-center transition-colors cursor-pointer ${
                        active
                          ? "bg-foreground text-background border-foreground"
                          : "bg-background text-muted-foreground border-border hover:text-foreground hover:bg-secondary/60"
                      }`}
                    >
                      <span
                        className="absolute top-0 inset-x-0 h-[2px]"
                        style={{ backgroundColor: cat.hex }}
                      />
                      <CategoryVectorIcon category={cat.id} className="size-3.5 mt-0.5" />
                    </button>
                  </Hint>
                )
              })}
            </div>

            <div className="relative flex items-center">
              <Search className="size-3.5 text-muted-foreground absolute left-2.5 pointer-events-none" />
              <Input
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Escape") {
                    setSearchQuery("")
                    e.currentTarget.blur()
                  }
                }}
                placeholder={tx("모델 필터 (예: TS808, Plexi)", "Filter gear (TS808, Plexi)")}
                className="pl-7 pr-6"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery("")}
                  className="absolute right-2 text-muted-foreground hover:text-foreground cursor-pointer"
                >
                  <X className="size-3" />
                </button>
              )}
            </div>
          </div>

          {/* Device List */}
          <ScrollArea className="flex-1 min-h-0">
            <div className="p-2 space-y-0.5">
              {categoryModels.length === 0 ? (
                <div className="py-8 text-center text-xs text-muted-foreground">
                  {tx("일치하는 장비가 없습니다", "No matching gear")}
                </div>
              ) : (
                categoryModels.map((model) => {
                  const isInSelectedSlot = currentSlot?.modelId === model.id
                  const catColor = CATEGORY_MAP[model.category].hex
                  return (
                    <button
                      key={model.id}
                      type="button"
                      draggable
                      onDragStart={(e) => {
                        e.dataTransfer.setData("application/x-cortex-model", model.id)
                        e.dataTransfer.effectAllowed = "copy"
                      }}
                      onClick={() => onAssignModel(model.id, selectedSlot)}
                      className={`w-full text-left px-2.5 py-1.5 rounded-sm text-xs transition-colors flex items-center justify-between gap-2 border cursor-pointer ${
                        isInSelectedSlot
                          ? "bg-primary/15 text-foreground font-bold border-primary"
                          : "text-foreground/90 hover:bg-secondary border-transparent"
                      }`}
                    >
                      <div className="flex items-center gap-2 min-w-0">
                        <span
                          className="w-1.5 h-3 rounded-[1px] shrink-0"
                          style={{ backgroundColor: catColor }}
                        />
                        <span className="truncate">{model.name}</span>
                      </div>
                      {searchQuery.trim() && (
                        <span className="text-[9px] font-mono text-muted-foreground uppercase shrink-0">
                          {model.category}
                        </span>
                      )}
                    </button>
                  )
                })
              )}
            </div>
          </ScrollArea>

          <div className="px-3 py-1.5 border-t border-border flex items-center justify-between text-[10px] font-mono tabular-nums text-muted-foreground bg-secondary/35">
            <span>SLOT #{String(selectedSlot + 1).padStart(2, "0")}</span>
            <span>1-8 · B · T · /</span>
          </div>
        </aside>
      )}

      {/* Center Stage + Split Hardware Console */}
      <div className="flex-1 min-w-0 flex flex-col min-h-0">
        {/* Preset Cartridge + BPM + Scene A–H + Routing Bar */}
        <div className="h-11 shrink-0 px-3.5 border-b border-border bg-card flex items-center justify-between gap-3">
          {/* Left: Preset Cartridge */}
          <div className="flex items-center gap-1.5 min-w-0">
            <Hint content={tx("이전 프리셋", "Previous Preset")}>
              <Button size="icon-xs" variant="keycap" onClick={() => stepPreset(-1)}>
                <ChevronLeft className="size-3.5" />
              </Button>
            </Hint>
            <Hint content={tx("다음 프리셋", "Next Preset")}>
              <Button size="icon-xs" variant="keycap" onClick={() => stepPreset(1)}>
                <ChevronRight className="size-3.5" />
              </Button>
            </Hint>

            <div className="h-7 px-2.5 rounded-sm bg-background border border-border flex items-center gap-2 min-w-[200px] max-w-[280px]">
              <span className="font-mono text-[11px] font-bold text-primary shrink-0">
                {chain.presetId}
              </span>
              {editingName ? (
                <Input
                  autoFocus
                  value={draftName}
                  onChange={(e) => setDraftName(e.target.value)}
                  onBlur={() => {
                    setEditingName(false)
                    onPushChain({ ...chain, presetName: draftName }, true)
                  }}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      setEditingName(false)
                      onPushChain({ ...chain, presetName: draftName }, true)
                    }
                    if (e.key === "Escape") {
                      setDraftName(chain.presetName)
                      setEditingName(false)
                    }
                  }}
                  className="h-5 px-1 border-0 bg-transparent focus-visible:ring-0"
                />
              ) : (
                <Hint content={tx("클릭하여 프리셋 이름 변경", "Click to rename preset")}>
                  <button
                    type="button"
                    onClick={() => setEditingName(true)}
                    className="font-display text-xs font-bold text-foreground truncate text-left flex-1 cursor-text"
                  >
                    {chain.presetName}
                  </button>
                </Hint>
              )}
            </div>

            <Hint content={tx("현재 프리셋 저장", "Save User Preset")}>
              <Button size="icon-xs" variant="keycap" onClick={handleSavePreset}>
                <Save className="size-3" />
              </Button>
            </Hint>
          </div>

          {/* Right: BPM Popover + Scene A–H + Routing Toggle */}
          <div className="flex items-center gap-2">
            {/* Global Tempo Popover + TAP */}
            <div className="flex items-center gap-1">
              <Popover>
                <Hint content={tx("글로벌 BPM 설정", "Global Tempo BPM")}>
                  <PopoverTrigger asChild>
                    <Button size="xs" variant="outline" className="tabular-nums">
                      <Clock className="size-3 text-muted-foreground" />
                      <span>{Math.round(chain.tempoBpm ?? 120)} BPM</span>
                    </Button>
                  </PopoverTrigger>
                </Hint>
                <PopoverContent className="w-56 space-y-2.5">
                  <div className="font-mono text-[10px] font-bold uppercase tracking-wider text-muted-foreground">
                    GLOBAL TEMPO (40–280 BPM)
                  </div>
                  <div className="flex items-center gap-2">
                    <Input
                      value={editingBpm ? draftBpm : String(Math.round(chain.tempoBpm ?? 120))}
                      onFocus={() => {
                        setDraftBpm(String(Math.round(chain.tempoBpm ?? 120)))
                        setEditingBpm(true)
                      }}
                      onChange={(e) => setDraftBpm(e.target.value)}
                      onBlur={() => {
                        setEditingBpm(false)
                        const v = Number.parseFloat(draftBpm)
                        if (!Number.isNaN(v) && Number.isFinite(v)) {
                          onPushChain({ ...chain, tempoBpm: Math.max(40, Math.min(280, v)) }, true, true)
                        }
                      }}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") e.currentTarget.blur()
                      }}
                      className="font-mono tabular-nums"
                    />
                    <Button size="sm" variant="signal" onClick={handleTapTempo}>
                      TAP
                    </Button>
                  </div>
                </PopoverContent>
              </Popover>

              <ContextMenu>
                <ContextMenuTrigger asChild>
                  <span>
                    <Hint content={tx("연속 클릭 또는 T 키로 탭 템포", "Tap tempo")} kbd="T">
                      <Button size="xs" variant="keycap" onClick={handleTapTempo}>
                        TAP
                      </Button>
                    </Hint>
                  </span>
                </ContextMenuTrigger>
                <ContextMenuContent>
                  <ContextMenuItem
                    onSelect={() => {
                      if (tapBinding && onClearMidiBinding)
                        onClearMidiBinding({ targetKind: "tapTempo" })
                      else if (onStartMidiLearn) onStartMidiLearn({ targetKind: "tapTempo" })
                    }}
                  >
                    <Radio className="size-3.5 text-primary" />
                    {tapBinding
                      ? tx(`MIDI CC#${tapBinding.cc} 해제`, `Clear MIDI CC#${tapBinding.cc}`)
                      : tx("TAP MIDI CC 학습", "Learn TAP MIDI CC")}
                  </ContextMenuItem>
                </ContextMenuContent>
              </ContextMenu>
            </div>

            <Separator orientation="vertical" />

            {/* Scenes A..H */}
            <ToggleGroup
              type="single"
              size="xs"
              variant="keycap"
              value={String(chain.activeScene)}
              onValueChange={(v) => {
                if (v !== "") onPushChain(switchSceneOnChain(chain, Number(v)), true, true)
              }}
            >
              {SCENE_LABELS.map((label, idx) => {
                const sceneBinding = midiState?.bindings?.find(
                  (b) => b.targetKind === "scene" && b.sceneIndex === idx
                )
                return (
                  <ContextMenu key={label}>
                    <ContextMenuTrigger asChild>
                      <span>
                        <Hint
                          content={tx(`Scene ${label} 전환`, `Switch Scene ${label}`)}
                          kbd={String(idx + 1)}
                        >
                          <ToggleGroupItem value={String(idx)} className="relative">
                            {label}
                            {sceneBinding && (
                              <span className="absolute -bottom-0.5 right-0.5 size-1 rounded-full bg-signal-ok" />
                            )}
                          </ToggleGroupItem>
                        </Hint>
                      </span>
                    </ContextMenuTrigger>
                    <ContextMenuContent>
                      <ContextMenuItem
                        onSelect={() => {
                          if (sceneBinding && onClearMidiBinding) {
                            onClearMidiBinding({ targetKind: "scene", sceneIndex: idx })
                          } else if (onStartMidiLearn) {
                            onStartMidiLearn({ targetKind: "scene", sceneIndex: idx })
                          }
                        }}
                      >
                        <Radio className="size-3.5 text-primary" />
                        {sceneBinding
                          ? tx(`Scene ${label} MIDI CC#${sceneBinding.cc} 해제`, `Clear Scene ${label} CC#${sceneBinding.cc}`)
                          : tx(`Scene ${label} MIDI CC 학습`, `Learn Scene ${label} MIDI CC`)}
                      </ContextMenuItem>
                    </ContextMenuContent>
                  </ContextMenu>
                )
              })}
            </ToggleGroup>

            <Separator orientation="vertical" />

            {/* Serial vs Parallel Routing Toggle */}
            <Hint
              content={tx(
                "직렬(Serial) / 병렬 분기(Parallel) 라우팅 전환",
                "Toggle Serial / Parallel Split Routing"
              )}
            >
              <Toggle
                size="sm"
                variant="keycap"
                pressed={chain.routingMode === 1}
                onPressedChange={(on) => onPushChain({ ...chain, routingMode: on ? 1 : 0 }, true)}
              >
                <GitBranch className="size-3" />
                {chain.routingMode === 1 ? "PARALLEL" : "SERIAL"}
              </Toggle>
            </Hint>
          </div>
        </div>

        {/* Center: Recessed OLED Boutique Silhouette Patchboard */}
        <CortexGrid
          chain={chain}
          tones={tones}
          selectedSlot={selectedSlot}
          midiState={midiState}
          onSelectSlot={handleSelectSlot}
          onToggleBypass={toggleSlotBypass}
          onResetSlot={resetSlotDefaults}
          onRemoveSlot={removeSlotBlock}
          onMoveOrSwap={moveOrSwapSlot}
          onDropModelFromLibrary={onAssignModel}
          onUpdateRouting={(patch) => onPushChain({ ...chain, ...patch }, false)}
          onStartMidiLearn={onStartMidiLearn}
          onClearMidiBinding={onClearMidiBinding}
          meterL={meterL}
          meterR={meterR}
          clip={clip}
        />

        {/* Zone 3: Bottom Split Hardware Console (Faceplate 8 cols + TP-7 Micro Tape Deck 4 cols) */}
        <section
          className={`${
            currentModel?.category === "cab" && cabRoomOpen ? "h-[280px]" : "h-[238px]"
          } shrink-0 border-t border-border bg-card grid grid-cols-1 md:grid-cols-12 transition-[height] duration-150`}
        >
          {/* Left: Module Parameter Faceplate */}
          <div className="md:col-span-8 flex flex-col min-w-0 min-h-0">
            <div className="h-10 shrink-0 px-3.5 border-b border-border bg-secondary/40 flex items-center justify-between gap-2">
              {currentModel && currentCategory ? (
                <>
                  <div className="flex items-center gap-2 min-w-0">
                    <Hint
                      content={
                        currentSlot.enabled
                          ? tx("블록 바이패스", "Bypass Block")
                          : tx("블록 활성화", "Enable Block")
                      }
                      kbd="B"
                    >
                      <Button
                        size="icon-xs"
                        variant={currentSlot.enabled ? "signal" : "outline"}
                        onClick={() => toggleSlotBypass(selectedSlot)}
                      >
                        <Power className="size-3" />
                      </Button>
                    </Hint>
                    <Badge variant="outline" size="xs">
                      #{String(selectedSlot + 1).padStart(2, "0")}
                    </Badge>
                    <span className="font-mono text-[10px] font-bold uppercase text-muted-foreground shrink-0">
                      {currentModel.categoryLabel}
                    </span>
                    <span className="font-display text-xs font-bold truncate">
                      {currentModel.name}
                    </span>
                    {!currentSlot.enabled && (
                      <Badge variant="warn" size="xs">
                        BYPASSED
                      </Badge>
                    )}
                  </div>

                  <div className="flex items-center gap-1.5 shrink-0">
                    {supportsTempoSync(currentModel.id) && (
                      <ToggleGroup
                        type="single"
                        size="xs"
                        variant="keycap"
                        value={String(currentSlot.syncDivision ?? 0)}
                        onValueChange={(v) => {
                          if (v !== "") updateSlotSyncDivision(selectedSlot, Number(v))
                        }}
                      >
                        {TEMPO_SYNC_DIVISIONS.map((div) => (
                          <ToggleGroupItem key={div.value} value={String(div.value)}>
                            {div.label}
                          </ToggleGroupItem>
                        ))}
                      </ToggleGroup>
                    )}

                    {currentModel.id === "nam_capture" && (
                      <div className="flex items-center gap-1">
                        <Select
                          value={currentSlot.namModelId || selectedToneId || "__none__"}
                          onValueChange={(v) => v !== "__none__" && updateSlotNamModel(selectedSlot, v)}
                        >
                          <SelectTrigger className="h-6 w-44 text-[11px]">
                            <SelectValue placeholder={tx(".nam 선택", "Select .nam")} />
                          </SelectTrigger>
                          <SelectContent>
                            {tones.length === 0 ? (
                              <SelectItem value="__none__">
                                {tx("불러온 .nam 없음", "No .nam loaded")}
                              </SelectItem>
                            ) : (
                              tones.map((t) => (
                                <SelectItem key={t.id} value={t.id}>
                                  {t.name} [{t.architecture}]
                                </SelectItem>
                              ))
                            )}
                          </SelectContent>
                        </Select>
                        <Button
                          size="xs"
                          variant="keycap"
                          onClick={() => void importNamWithSlot(selectedSlot)}
                        >
                          <Upload className="size-3" />
                          .NAM
                        </Button>
                      </div>
                    )}

                    {currentModel.category === "cab" && (
                      <div className="flex items-center gap-1">
                        <Toggle
                          size="xs"
                          variant="keycap"
                          pressed={cabRoomOpen}
                          onPressedChange={setCabRoomOpen}
                        >
                          <Mic className="size-3" />
                          CAB ROOM{currentSlot.cabMic?.dualEnabled ? " 2×" : ""}
                        </Toggle>
                        {(currentModel.id === "cab_custom" || currentModel.id === "cab_v30") && (
                          <Button size="xs" variant="keycap" onClick={handleLoadCabIr}>
                            <FolderOpen className="size-3" />
                            WAV IR
                          </Button>
                        )}
                      </div>
                    )}

                    <Separator orientation="vertical" />

                    <Hint content={tx("왼쪽 이동", "Move Left")}>
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        disabled={selectedSlot === 0}
                        onClick={() => moveOrSwapSlot(selectedSlot, selectedSlot - 1)}
                      >
                        <ArrowLeft className="size-3.5" />
                      </Button>
                    </Hint>
                    <Hint content={tx("오른쪽 이동", "Move Right")}>
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        disabled={selectedSlot === 15}
                        onClick={() => moveOrSwapSlot(selectedSlot, selectedSlot + 1)}
                      >
                        <ArrowRight className="size-3.5" />
                      </Button>
                    </Hint>
                    <Hint content={tx("기본값 복원", "Reset Defaults")}>
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        onClick={() => resetSlotDefaults(selectedSlot)}
                      >
                        <RotateCcw className="size-3.5" />
                      </Button>
                    </Hint>
                    <Hint content={tx("블록 제거", "Remove Block")} kbd="Del">
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        onClick={() => removeSlotBlock(selectedSlot)}
                      >
                        <Trash2 className="size-3.5" />
                      </Button>
                    </Hint>
                  </div>
                </>
              ) : (
                <div className="flex items-center justify-between w-full font-mono text-[11px] text-muted-foreground">
                  <span>
                    {tx(
                      `EMPTY SLOT #${String(selectedSlot + 1).padStart(2, "0")} — 카탈로그 또는 빠른 장착에서 장비를 선택하세요`,
                      `EMPTY SLOT #${String(selectedSlot + 1).padStart(2, "0")} — Pick gear from catalog or quick mount`
                    )}
                  </span>
                </div>
              )}
            </div>

            <ScrollArea className="flex-1 min-h-0 bg-card">
              {currentModel && currentCategory ? (
                <div
                  className={
                    currentModel.category === "cab" && cabRoomOpen
                      ? "flex items-stretch min-h-[218px]"
                      : ""
                  }
                >
                  <div
                    className={
                      currentModel.category === "cab" && cabRoomOpen
                        ? "w-[310px] shrink-0 grid grid-cols-2"
                        : "grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4"
                    }
                  >
                    {currentModel.params.map((p) => {
                      const val = currentSlot.params[p.index] ?? p.defaultVal
                      if (p.kind === "toggle") {
                        return (
                          <CortexToggleCell
                            key={p.index}
                            label={p.label}
                            checked={val > 0.5}
                            onLabel={p.onLabel}
                            offLabel={p.offLabel}
                            accentHex={currentCategory.hex}
                            onChange={(checked) => updateSlotParam(selectedSlot, p.index, checked ? 1 : 0)}
                          />
                        )
                      }
                      return (
                        <CortexKnob
                          key={p.index}
                          label={p.label}
                          value={val}
                          min={p.min}
                          max={p.max}
                          step={p.step}
                          defaultVal={p.defaultVal}
                          unit={p.unit}
                          accentHex={currentCategory.hex}
                          syncedText={
                            supportsTempoSync(currentModel.id) &&
                            (currentSlot.syncDivision ?? 0) > 0 &&
                            (p.unit === "ms" || p.unit === "Hz")
                              ? computeSyncedValueLabel(
                                  currentModel.id,
                                  currentSlot.syncDivision ?? 0,
                                  chain.tempoBpm ?? 120
                                )
                              : null
                          }
                          midiCc={
                            midiState?.bindings?.find(
                              (b) =>
                                b.targetKind === "param" &&
                                b.slotIndex === selectedSlot &&
                                b.paramIndex === p.index
                            )?.cc ?? null
                          }
                          isMidiLearning={
                            midiState?.learning?.targetKind === "param" &&
                            midiState?.learning?.slotIndex === selectedSlot &&
                            midiState?.learning?.paramIndex === p.index
                          }
                          onChange={(nextVal) => updateSlotParam(selectedSlot, p.index, nextVal)}
                          onContextLearn={() => {
                            const existing = midiState?.bindings?.find(
                              (b) =>
                                b.targetKind === "param" &&
                                b.slotIndex === selectedSlot &&
                                b.paramIndex === p.index
                            )
                            if (existing && onClearMidiBinding) {
                              onClearMidiBinding({
                                targetKind: "param",
                                slotIndex: selectedSlot,
                                paramIndex: p.index,
                              })
                            } else if (onStartMidiLearn) {
                              onStartMidiLearn({
                                targetKind: "param",
                                slotIndex: selectedSlot,
                                paramIndex: p.index,
                              })
                            }
                          }}
                        />
                      )
                    })}
                  </div>
                  {currentModel.category === "cab" && cabRoomOpen && (
                    <VirtualCabRoom
                      config={currentSlot.cabMic}
                      onChange={(next) => updateSlotCabMic(selectedSlot, next)}
                    />
                  )}
                </div>
              ) : (
                <div className="p-4 flex flex-wrap items-center gap-1.5">
                  {[
                    "od_ts808",
                    "od_klon",
                    "dist_rat",
                    "fuzz_bigmuff",
                    "pitch_capo",
                    "amp_plexi",
                    "amp_slo100",
                    "nam_capture",
                    "cab_v30",
                    "eq_graphic5",
                    "mod_ce2",
                    "dly_digital",
                    "rev_hall",
                  ].map((id) => {
                    const m = DEVICE_MAP[id]
                    const c = CATEGORY_MAP[m.category]
                    return (
                      <Button
                        key={id}
                        size="xs"
                        variant="keycap"
                        onClick={() => onAssignModel(id, selectedSlot)}
                      >
                        <span className="size-2 rounded-[1px]" style={{ backgroundColor: c.hex }} />
                        {m.name}
                      </Button>
                    )
                  })}
                </div>
              )}
            </ScrollArea>
          </div>

          {/* Right: TP-7 Micro Tape & Loop + MR Deck */}
          <div className="md:col-span-4 min-h-0">
            <LooperRecorderDeck
              looper={looper}
              recorder={recorder}
              backing={backing}
              tempoBpm={chain.tempoBpm ?? 120}
              isAudioRunning={isAudioRunning}
              midiState={midiState}
              onLooperCmd={handleLooperCommand}
              onRecorderCmd={handleRecorderCommand}
              onLoadBacking={handleLoadBacking}
              onControlBacking={controlBacking}
              onUnloadBacking={unloadBacking}
              onStartMidiLearn={onStartMidiLearn}
              onClearMidiBinding={onClearMidiBinding}
            />
          </div>
        </section>
      </div>
    </div>
  )
}
