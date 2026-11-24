import React, { useEffect, useMemo, useRef, useState } from "react"
import {
  ArrowLeft,
  ArrowRight,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Clock,
  Cpu,
  Download,
  FolderOpen,
  GitBranch,
  Globe,
  Mic,
  Minus,
  Package,
  Plus,
  Power,
  Radio,
  RotateCcw,
  Save,
  Search,
  SlidersHorizontal,
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
import { Slider } from "@/components/ui/slider"
import { Toggle } from "@/components/ui/toggle"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { HelpHint, Hint } from "@/components/ui/tooltip"
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
  isTourActive?: boolean
  looperOpen?: boolean
  onLooperOpenChange?: (open: boolean) => void
  onChangeView: (view: "grid" | "directory" | "scope") => void
  chain: SignalChainConfigDto
  savedPresets: SignalChainConfigDto[]
  allPresets: SignalChainConfigDto[]
  selectedSlot: number
  onSelectSlot: (idx: number) => void
  onPushChain: (
    next: SignalChainConfigDto,
    immediate?: boolean,
    recordHistory?: boolean,
    skipEngineSync?: boolean
  ) => void
  isVst3Host?: boolean
  onParamGesture?: (req: {
    phase: "begin" | "change" | "end"
    kind:
      | "slotBypass"
      | "slotParam"
      | "scene"
      | "tempo"
      | "routing"
      | "parallelMix"
      | "inputGain"
      | "outputGain"
    slotIndex?: number
    paramIndex?: number
    value: number
  }) => void
  onLoadPreset: (preset: SignalChainConfigDto) => void
  onAssignModel: (modelId: string, slotIdx: number) => void
  onSavedPresetsChange: (presets: SignalChainConfigDto[]) => void
  tones: ModelProfileSummary[]
  selectedToneId: string
  customCabIrName: string
  initialBacking?: BackingTrackStateDto
  initialLooper?: LooperSnapshotDto
  initialRecorder?: RecorderReampSnapshotDto
  isAudioRunning: boolean
  isAudioBusy: boolean
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
  onTonesUpdated: (tones: ModelProfileSummary[], selectedToneId: string) => void
  onSelectToneId: (id: string) => void
  onCustomCabIrChange: (name: string) => void
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

export const CortexStudio: React.FC<CortexStudioProps> = ({
  activeView,
  catalogVersion,
  isTourActive,
  looperOpen = false,
  onLooperOpenChange,
  onChangeView,
  chain,
  savedPresets,
  allPresets,
  selectedSlot,
  onSelectSlot,
  onPushChain,
  isVst3Host = false,
  onParamGesture,
  onLoadPreset,
  onAssignModel,
  onSavedPresetsChange,
  tones,
  selectedToneId,
  customCabIrName,
  initialBacking,
  initialLooper,
  initialRecorder,
  isAudioRunning,
  isAudioBusy,
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

  const emitGesture = (
    phase: "begin" | "change" | "end",
    kind:
      | "slotBypass"
      | "slotParam"
      | "scene"
      | "tempo"
      | "routing"
      | "parallelMix"
      | "inputGain"
      | "outputGain",
    value: number,
    slotIndex?: number,
    paramIndex?: number
  ) => {
    if (isVst3Host && onParamGesture) {
      onParamGesture({ phase, kind, value, slotIndex, paramIndex })
    }
  }

  const [activeCategory, setActiveCategory] = useState<DeviceCategoryId>("amp")
  const [inspectorOpen, setInspectorOpen] = useState(false)
  const [catalogOpen, setCatalogOpen] = useState(false)
  const [presetPickerOpen, setPresetPickerOpen] = useState(false)
  const [searchQuery, setSearchQuery] = useState("")
  const [draftName, setDraftName] = useState(chain.presetName)
  const [editingBpm, setEditingBpm] = useState(false)
  const [draftBpm, setDraftBpm] = useState("120")
  const [cabRoomOpen, setCabRoomOpen] = useState(false)

  useEffect(() => {
    if (isTourActive) {
      setInspectorOpen(false)
      setCatalogOpen(false)
      setPresetPickerOpen(false)
      onLooperOpenChange?.(false)
    }
  }, [isTourActive, onLooperOpenChange])

  useEffect(() => {
    if (looperOpen) {
      setInspectorOpen(false)
    }
  }, [looperOpen])

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

  const handleSavePreset = async (nextChain: SignalChainConfigDto = chain) => {
    if (!studioBridge.isWebView2) {
      const filtered = savedPresets.filter((p) => p.presetId !== nextChain.presetId)
      onSavedPresetsChange([...filtered, nextChain])
      onLog(
        tx(`${nextChain.presetId} ${nextChain.presetName} 저장됨`, `Saved ${nextChain.presetId} ${nextChain.presetName}`),
        "success"
      )
      return
    }
    try {
      const res = await studioBridge.invoke<{ savedPresets: SignalChainConfigDto[] }>(
        "saveUserPreset",
        nextChain
      )
      if (res?.savedPresets) onSavedPresetsChange(res.savedPresets)
      onLog(
        tx(`${nextChain.presetId} ${nextChain.presetName} 저장됨`, `Saved ${nextChain.presetId} ${nextChain.presetName}`),
        "success"
      )
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

  const handleSelectSlot = (idx: number, openCatalogIfEmpty?: boolean) => {
    const modelId = chain.slots[idx]?.modelId
    if (modelId && DEVICE_MAP[modelId]) {
      setActiveCategory(DEVICE_MAP[modelId].category)
    }
    if (openCatalogIfEmpty || !modelId) {
      onSelectSlot(idx)
      setInspectorOpen(false)
      onLooperOpenChange?.(false)
      setCatalogOpen(true)
      return
    }
    if (inspectorOpen && selectedSlot === idx) {
      setInspectorOpen(false)
      return
    }
    onSelectSlot(idx)
    setCatalogOpen(false)
    onLooperOpenChange?.(false)
    setInspectorOpen(true)
  }

  const handlePickModel = (modelId: string) => {
    onAssignModel(modelId, selectedSlot)
    setCatalogOpen(false)
    setInspectorOpen(true)
  }

  const toggleSlotBypass = (slotIdx: number) => {
    const target = chain.slots[slotIdx]
    if (!target?.modelId) return
    const nextEnabled = !target.enabled
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx
        ? { ...s, enabled: nextEnabled, params: [...s.params] }
        : { ...s, params: [...s.params] }
    )
    if (isVst3Host && onParamGesture) {
      const val = nextEnabled ? 1 : 0
      emitGesture("begin", "slotBypass", val, slotIdx)
      emitGesture("change", "slotBypass", val, slotIdx)
      emitGesture("end", "slotBypass", val, slotIdx)
      onPushChain({ ...chain, slots: nextSlots }, false, false, true)
    } else {
      onPushChain({ ...chain, slots: nextSlots }, true)
    }
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
    if (slotIdx === selectedSlot) {
      setInspectorOpen(false)
    }
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
    if (isVst3Host && onParamGesture) {
      emitGesture("change", "slotParam", val, slotIdx, paramIdx)
      onPushChain({ ...chain, slots: nextSlots }, false, false, true)
    } else {
      onPushChain({ ...chain, slots: nextSlots }, false)
    }
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
      if (isVst3Host && onParamGesture) {
        emitGesture("begin", "tempo", bpm)
        emitGesture("change", "tempo", bpm)
        emitGesture("end", "tempo", bpm)
        onPushChain({ ...chain, tempoBpm: bpm }, false, true, true)
      } else {
        onPushChain({ ...chain, tempoBpm: bpm }, true, true)
      }
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

  // Global Rig Keyboard Shortcuts (1–8 Scenes, G Catalog, L Looper, B Bypass, Del Remove, T Tap, Esc Close, Space MR)
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName?.toLowerCase()
      const isInput =
        tag === "input" ||
        tag === "textarea" ||
        (e.target as HTMLElement | null)?.isContentEditable
      if (isInput || activeView !== "grid") return

      if (e.key === "Escape") {
        if (catalogOpen) {
          e.preventDefault()
          setCatalogOpen(false)
        } else if (inspectorOpen) {
          e.preventDefault()
          setInspectorOpen(false)
        } else if (looperOpen) {
          e.preventDefault()
          onLooperOpenChange?.(false)
        }
      } else if (e.key.toLowerCase() === "g" && !e.ctrlKey && !e.altKey && !e.metaKey) {
        e.preventDefault()
        setCatalogOpen((v) => !v)
      } else if (e.key.toLowerCase() === "l" && !e.ctrlKey && !e.altKey && !e.metaKey) {
        e.preventDefault()
        onLooperOpenChange?.(!looperOpen)
      } else if (e.key >= "1" && e.key <= "8" && !e.ctrlKey && !e.altKey && !e.metaKey) {
        e.preventDefault()
        onPushChain(switchSceneOnChain(chain, Number(e.key) - 1), true, true)
      } else if (e.key.toLowerCase() === "b" && !e.ctrlKey && !e.metaKey) {
        e.preventDefault()
        toggleSlotBypass(selectedSlot)
      } else if ((e.key === "Delete" || e.key === "Backspace") && !e.ctrlKey && inspectorOpen) {
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
  }, [
    activeView,
    catalogOpen,
    inspectorOpen,
    looperOpen,
    onLooperOpenChange,
    chain,
    selectedSlot,
    backing.loaded,
    backing.playing,
  ])

  // ───────────────────────────────────────────────────────────────────────────
  // VIEW 2: VAULT & HUB (Accessed from Menu)
  // ───────────────────────────────────────────────────────────────────────────
  if (activeView === "directory") {
    return (
      <div className="flex-1 min-h-0 flex flex-col overflow-hidden bg-transparent">
        <div className="max-w-[1180px] mx-auto w-full px-6 pt-5 pb-3 shrink-0 flex items-center justify-between gap-3">
          <div className="flex items-center gap-2.5 min-w-0">
            <h1 className="font-display font-light text-[26px] leading-tight tracking-[-0.025em] text-foreground truncate">
              {tx("톤 보관함 · .NAM · VST3", "Rig Vault & Neural Hub")}
            </h1>
            <HelpHint
              side="bottom"
              content={tx(
                ".nrig 번들, .nam 캡처, 커스텀 WAV IR 및 DAW 플러그인을 관리합니다.",
                "Manage .nrig bundles, .nam captures, custom WAV IRs, and DAW plugins."
              )}
            />
          </div>
          <div className="flex items-center gap-1.5">
            <Button
              size="sm"
              variant="outline"
              className="rounded-full px-3.5"
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
              <Upload className="size-3.5" />
              {tx(".NRIG 가져오기", "Import .NRIG")}
            </Button>
            <Button
              size="sm"
              variant="keycap"
              className="rounded-full px-3.5"
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
              <Download className="size-3.5" />
              {tx(".NRIG 내보내기", "Export .NRIG")}
            </Button>
          </div>
        </div>

        <div className="max-w-[1180px] mx-auto w-full px-6 pb-24 pt-1 flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-12 gap-4">
          {/* Col 1: Presets Bank */}
          <div className="lg:col-span-4 rounded-[16px] border border-border bg-card shadow-[0_18px_42px_rgba(11,14,16,0.08)] flex flex-col min-h-0 overflow-hidden">
            <div className="h-11 px-4 border-b border-border flex items-center justify-between gap-2 bg-secondary/40">
              <div className="flex items-center gap-2 min-w-0">
                <span className="font-mono text-[11px] font-bold tracking-[0.08em] uppercase truncate">
                  {tx("프리셋 뱅크", "PRESET BANK")}
                </span>
                <Badge variant="outline" size="xs">
                  {allPresets.length}
                </Badge>
              </div>
            </div>

            <ScrollArea className="flex-1 p-2.5">
              <div className="space-y-1.5 w-full min-w-0">
                {allPresets.map((p) => {
                  const isActive = p.presetId === chain.presetId
                  const isUser = savedPresets.some((u) => u.presetId === p.presetId)
                  return (
                    <div
                      key={p.presetId}
                      className={`w-full min-w-0 overflow-hidden flex items-center justify-between gap-2 px-3 py-2.5 rounded-[10px] border transition-all cursor-pointer ${
                        isActive
                          ? "bg-foreground text-background border-foreground shadow-[0_10px_24px_rgba(11,14,16,0.16)]"
                          : "bg-background/45 border-border hover:bg-card text-foreground"
                      }`}
                      onClick={() => {
                        onLoadPreset(p)
                        onChangeView("grid")
                      }}
                    >
                      <div className="flex items-center gap-2.5 min-w-0 flex-1">
                        <span
                          className={`font-mono text-[11px] font-bold w-8 shrink-0 ${
                            isActive ? "text-primary" : "opacity-65"
                          }`}
                        >
                          {p.presetId}
                        </span>
                        <span className="font-display font-normal text-[13px] truncate">
                          {p.presetName}
                        </span>
                      </div>
                      {isUser && (
                        <Button
                          size="icon-xs"
                          variant="ghost"
                          className="shrink-0"
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
          <div className="lg:col-span-4 rounded-[16px] border border-border bg-card shadow-[0_18px_42px_rgba(11,14,16,0.08)] flex flex-col min-h-0 overflow-hidden">
            <div className="h-11 px-4 border-b border-border flex items-center justify-between gap-2 bg-secondary/40">
              <div className="flex items-center gap-1.5 min-w-0">
                <span className="font-mono text-[11px] font-bold tracking-[0.08em] uppercase truncate">
                  NEURAL CAPTURES (.NAM)
                </span>
                <HelpHint
                  content={tx(
                    "WaveNet · LSTM · ConvNet 지원 (체인 내 최대 3슬롯 동시 구동)",
                    "Supports WaveNet, LSTM & ConvNet (up to 3 concurrent slots)."
                  )}
                />
              </div>
              <div className="flex items-center gap-1 shrink-0">
                <Button size="xs" variant="keycap" onClick={handleImportNam}>
                  <Upload className="size-3" />
                  {tx("가져오기", "Import")}
                </Button>
                <Hint content={tx(".nam 파일 내보내기", "Export selected .nam file")}>
                  <Button
                    size="icon-xs"
                    variant="ghost"
                    disabled={!selectedToneId}
                    onClick={handleExportNam}
                  >
                    <Download className="size-3" />
                  </Button>
                </Hint>
              </div>
            </div>

            <ScrollArea className="flex-1 p-2.5">
              {tones.length === 0 ? (
                <div className="p-8 text-center text-xs text-muted-foreground">
                  {tx("로드된 .nam 모델 없음", "No .nam models loaded")}
                </div>
              ) : (
                <div className="space-y-1.5 w-full min-w-0">
                  {tones.map((t) => {
                    const sel = t.id === selectedToneId
                    return (
                      <div
                        key={t.id}
                        onClick={() => void handleSelectNamModel(t.id)}
                        className={`w-full min-w-0 overflow-hidden flex items-center justify-between gap-2 px-3 py-2 rounded-[10px] border cursor-pointer transition-all ${
                          sel
                            ? "bg-card border-primary shadow-[0_0_0_1px_var(--signal-hot)] text-foreground"
                            : "border-border bg-background/45 hover:bg-card text-foreground"
                        }`}
                      >
                        <div className="min-w-0 flex-1">
                          <div className="font-display font-normal text-xs truncate">{t.name}</div>
                          <div className="text-[10px] font-mono text-muted-foreground tabular-nums truncate">
                            {t.archPreset} · {t.sampleRate} Hz
                          </div>
                        </div>
                        <Button
                          size="icon-xs"
                          variant="ghost"
                          className="shrink-0"
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

            <div className="border-t border-border p-3.5 bg-secondary/35 space-y-2">
              <div className="flex items-center justify-between gap-2">
                <span className="flex items-center gap-1.5 min-w-0 font-mono text-[10px] font-bold uppercase tracking-wider text-muted-foreground">
                  <span className="truncate">CUSTOM CAB IR (.WAV)</span>
                  <HelpHint
                    content={tx(
                      "최대 16,384-tap Zero-Latency 분할 FFT 컨볼버",
                      "Up to 16,384-tap zero-latency partitioned FFT convolver."
                    )}
                  />
                </span>
                <div className="flex items-center gap-1 shrink-0">
                  <Button size="xs" variant="keycap" onClick={handleLoadCabIr}>
                    <FolderOpen className="size-3" />
                    {tx("IR 열기", "Load IR")}
                  </Button>
                  {customCabIrName && (
                    <Button size="icon-xs" variant="ghost" onClick={handleClearCabIr}>
                      <RotateCcw className="size-3" />
                    </Button>
                  )}
                </div>
              </div>
              <div className="text-xs font-mono truncate text-foreground bg-background/70 px-2.5 py-2 rounded-[8px] border border-border">
                {customCabIrName || tx("내장 512-tap 4x12 V30 Modern IR", "Built-in 512-tap 4x12 V30 Modern IR")}
              </div>
            </div>
          </div>

          {/* Col 3: Tone Hub & VST3/CLAP Packaging Cards + Dark Obsidian Snapshot */}
          <div className="lg:col-span-4 flex flex-col min-h-0 gap-4">
            <Card className="py-4 gap-3 bg-card shadow-[0_18px_42px_rgba(11,14,16,0.08)]">
              <CardHeader>
                <div className="flex items-center justify-between gap-2">
                  <CardTitle className="flex items-center gap-2 text-xs font-mono uppercase tracking-wider min-w-0">
                    <Globe className="size-4 text-primary shrink-0" />
                    <span className="truncate">TONE HUB &amp; COMMUNITY</span>
                    <HelpHint
                      content={tx(
                        "검증된 WaveNet/LSTM/ConvNet 앰프 캡처, 초저지연 캐비넷 IR, 커뮤니티 .nrig 번들을 원클릭 설치합니다.",
                        "Install curated WaveNet/LSTM/ConvNet models, 8K IRs, and community .nrig bundles."
                      )}
                    />
                  </CardTitle>
                  <Badge variant="hot" size="xs">
                    CURATED
                  </Badge>
                </div>
              </CardHeader>
              <CardContent className="text-xs">
                <Button size="sm" variant="signal" className="w-full rounded-full" onClick={onOpenToneHub}>
                  <Package className="size-3.5" />
                  {tx("Tone Hub 카탈로그 열기", "Open Tone Hub Catalog")}
                </Button>
              </CardContent>
            </Card>

            <Card className="py-4 gap-3 bg-card shadow-[0_18px_42px_rgba(11,14,16,0.08)]">
              <CardHeader>
                <div className="flex items-center justify-between gap-2">
                  <CardTitle className="flex items-center gap-2 text-xs font-mono uppercase tracking-wider min-w-0">
                    <Cpu className="size-4 text-primary shrink-0" />
                    <span className="truncate">NATIVEAOT DAW PLUGIN</span>
                    <HelpHint
                      content={tx(
                        "현재 시그널 체인과 .nam 모델을 32개 샘플 정확도 오토메이션을 지원하는 VST3/CLAP으로 패키징합니다.",
                        "Package the current rig into a 0-alloc VST3 & CLAP DAW plugin with 32 parameters."
                      )}
                    />
                  </CardTitle>
                  <Badge variant="ok" size="xs">
                    VST3 + CLAP
                  </Badge>
                </div>
              </CardHeader>
              <CardContent className="text-xs">
                <Button size="sm" variant="keycap" className="w-full rounded-full" onClick={onOpenToneHub}>
                  {tx("VST3 / CLAP 내보내기 센터", "Open VST3 / CLAP Exporter")}
                </Button>
              </CardContent>
            </Card>

            <div className="mt-auto rounded-[16px] border border-oled-border nr-obsidian-pod p-4 font-mono text-[11px] text-oled-foreground space-y-1.5 min-w-0 overflow-hidden">
              <div className="flex items-center justify-between text-oled-muted text-[10px] tracking-[0.12em]">
                <span>ACTIVE RIG</span>
                <span className="size-1.5 rounded-full bg-primary shrink-0" />
              </div>
              <div className="font-display font-light text-lg tracking-tight text-oled-foreground truncate">
                {chain.presetId} · {chain.presetName}
              </div>
              <div className="text-oled-muted tabular-nums text-[10px] truncate">
                SCENE {SCENE_LABELS[chain.activeScene]} · {Math.round(chain.tempoBpm ?? 120)} BPM ·{" "}
                {chain.routingMode === 1 ? "PARALLEL" : "SERIAL"}
              </div>
            </div>
          </div>
        </div>
      </div>
    )
  }

  // ───────────────────────────────────────────────────────────────────────────
  // VIEW 3: HARMONIC LAB (Accessed from Menu)
  // ───────────────────────────────────────────────────────────────────────────
  if (activeView === "scope") {
    return (
      <div className="flex-1 min-h-0 flex flex-col overflow-hidden bg-transparent">
        <div className="max-w-[1180px] mx-auto w-full px-6 pt-5 pb-3 shrink-0 flex items-center justify-between gap-3">
          <div className="flex items-center gap-2.5 min-w-0">
            <h1 className="font-display font-light text-[26px] leading-tight tracking-[-0.025em] text-foreground truncate">
              {tx("스펙트럼 · 기타 DI 분석", "Harmonic Spectrum & Guitar DI Lab")}
            </h1>
            <HelpHint
              side="bottom"
              content={tx(
                "실시간 FFT 배음 스펙트럼과 기타 픽업 DI 특성을 계측합니다.",
                "Real-time FFT harmonic spectrum and guitar pickup DI profiler."
              )}
            />
          </div>
          <Badge variant={isAudioRunning ? "ok" : "outline"} size="xs">
            {isAudioRunning ? tx("LIVE ENGINE", "LIVE ENGINE") : tx("STANDBY", "STANDBY")}
          </Badge>
        </div>

        <div className="max-w-[1180px] mx-auto w-full px-6 pb-24 pt-1 flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-12 gap-4">
          <div className="lg:col-span-7 flex flex-col min-h-0">
            <VisualizerCanvas
              activeToneLabel={customCabIrName || undefined}
              className="flex-1 min-h-0 rounded-[16px]"
              canvasClassName="h-full min-h-[340px]"
            />
          </div>
          <div className="lg:col-span-5 flex flex-col min-h-0 rounded-[16px] border border-border bg-card shadow-[0_18px_42px_rgba(11,14,16,0.08)] p-4 overflow-hidden">
            <div className="pb-2.5 mb-2.5 border-b border-border flex items-center justify-between gap-2">
              <span className="flex items-center gap-1.5 min-w-0 font-mono text-[11px] font-bold tracking-[0.08em] uppercase">
                <span className="truncate">GUITAR DI PROFILER</span>
                <HelpHint
                  content={tx(
                    "6단계 측정으로 픽업 출력·험·밝기를 분석해 Input Trim과 Gate를 자동 보정합니다.",
                    "Measures pickup output, hum, and tilt across 6 steps to calibrate Input Trim & Gate."
                  )}
                />
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
      </div>
    )
  }

  // ───────────────────────────────────────────────────────────────────────────
  // VIEW 1: TOSS-INSPIRED STUDIO (ONLY Preset + Pure Signal Chain at Rest)
  // ───────────────────────────────────────────────────────────────────────────
  const tapBinding = midiState?.bindings?.find((b) => b.targetKind === "tapTempo")
  const splitCol = Math.max(0, Math.min(7, chain.splitCol))
  const mergeCol = Math.max(splitCol, Math.min(7, chain.mergeCol))

  return (
    <div className="relative flex-1 min-h-0 flex flex-col overflow-hidden bg-transparent">
      {/* Centered Hero Canvas: Preset Bar + Pure 2x8 Signal Chain */}
      <div
        className={`flex-1 min-h-0 flex flex-col justify-center max-w-[1300px] w-full mx-auto px-6 gap-6 transition-[padding] duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] ${
          (inspectorOpen && currentModel) || looperOpen ? "pb-72" : "pb-16"
        }`}
      >
        {/* Clean Toss-Style Preset + Scene Bar */}
        <header
          data-tour="preset-scene-bar"
          className="w-full flex items-center justify-between gap-3 flex-wrap px-1"
        >
          {/* Left: Preset Stepper + Clickable Title Popover */}
          <div className="flex items-center gap-2 min-w-0 flex-1">
            <Hint content={tx("이전 프리셋", "Previous Preset")}>
              <Button
                size="icon-sm"
                variant="outline"
                onClick={() => stepPreset(-1)}
                className="rounded-full shrink-0"
              >
                <ChevronLeft className="size-4" />
              </Button>
            </Hint>
            <Hint content={tx("다음 프리셋", "Next Preset")}>
              <Button
                size="icon-sm"
                variant="outline"
                onClick={() => stepPreset(1)}
                className="rounded-full shrink-0"
              >
                <ChevronRight className="size-4" />
              </Button>
            </Hint>

            <Popover open={presetPickerOpen} onOpenChange={setPresetPickerOpen}>
              <PopoverTrigger asChild>
                <button
                  type="button"
                  className="inline-flex items-baseline gap-2.5 px-3 py-1.5 rounded-[12px] hover:bg-card/70 transition-colors cursor-pointer min-w-0 max-w-full overflow-hidden"
                >
                  <span className="font-mono text-xs font-bold text-primary shrink-0 tabular-nums">
                    {chain.presetId}
                  </span>
                  <span className="font-display font-light text-[25px] md:text-[28px] leading-tight tracking-[-0.025em] text-foreground truncate max-w-[220px] sm:max-w-[340px] md:max-w-[420px]">
                    {chain.presetName}
                  </span>
                  <ChevronDown className="size-4 text-muted-foreground shrink-0 self-center" />
                </button>
              </PopoverTrigger>
              <PopoverContent align="start" className="w-80 rounded-[16px] p-3 space-y-2.5">
                <div className="flex items-center gap-1.5">
                  <Input
                    value={draftName}
                    onChange={(e) => setDraftName(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        const updated = { ...chain, presetName: draftName }
                        onPushChain(updated, true)
                        void handleSavePreset(updated)
                      }
                    }}
                    placeholder={tx("프리셋 이름", "Preset name")}
                    className="h-8 text-xs"
                  />
                  <Button
                    size="xs"
                    variant="signal"
                    onClick={() => {
                      const updated = { ...chain, presetName: draftName }
                      onPushChain(updated, true)
                      void handleSavePreset(updated)
                    }}
                    className="shrink-0"
                  >
                    <Save className="size-3" />
                    {tx("저장", "Save")}
                  </Button>
                </div>

                <Separator />

                <ScrollArea className="h-64">
                  <div className="space-y-1 pr-1 w-full min-w-0">
                    {allPresets.map((p) => {
                      const isActive = p.presetId === chain.presetId
                      const isUser = savedPresets.some((u) => u.presetId === p.presetId)
                      return (
                        <div
                          key={p.presetId}
                          onClick={() => {
                            onLoadPreset(p)
                            setPresetPickerOpen(false)
                          }}
                          className={`w-full min-w-0 overflow-hidden flex items-center justify-between gap-2 px-2.5 py-2 rounded-[10px] text-xs cursor-pointer transition-colors ${
                            isActive
                              ? "bg-foreground text-background font-medium"
                              : "hover:bg-secondary text-foreground"
                          }`}
                        >
                          <div className="flex items-center gap-2 min-w-0 flex-1">
                            <span
                              className={`font-mono text-[11px] font-bold w-7 shrink-0 ${
                                isActive ? "text-primary" : "text-muted-foreground"
                              }`}
                            >
                              {p.presetId}
                            </span>
                            <span className="truncate">{p.presetName}</span>
                          </div>
                          {isUser && (
                            <Button
                              size="icon-xs"
                              variant="ghost"
                              className="shrink-0"
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
              </PopoverContent>
            </Popover>
          </div>

          {/* Right: Scene A..H Segmented Pill + Chain Settings Popover */}
          <div className="flex items-center gap-2 shrink-0">
            <div className="rounded-full bg-card/85 backdrop-blur-sm border border-border p-1 flex items-center gap-0.5 shadow-[0_6px_18px_rgba(11,14,16,0.06)]">
              {SCENE_LABELS.map((label, idx) => {
                const active = chain.activeScene === idx
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
                          <button
                            type="button"
                            onClick={() => {
                              const nextChain = switchSceneOnChain(chain, idx)
                              if (isVst3Host && onParamGesture) {
                                emitGesture("begin", "scene", idx)
                                emitGesture("change", "scene", idx)
                                emitGesture("end", "scene", idx)
                                onPushChain(nextChain, false, true, true)
                              } else {
                                onPushChain(nextChain, true, true)
                              }
                            }}
                            className={`relative h-6 min-w-6 px-2 rounded-full font-mono text-[11px] font-medium transition-all duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] cursor-pointer ${
                              active
                                ? "bg-foreground text-background shadow-[0_4px_14px_rgba(11,14,16,0.22)] scale-[1.03]"
                                : "text-muted-foreground hover:text-foreground hover:bg-secondary/70"
                            }`}
                          >
                            {label}
                            {sceneBinding && (
                              <span className="absolute top-0.5 right-0.5 size-1 rounded-full bg-signal-ok" />
                            )}
                          </button>
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
            </div>

            {/* Chain Routing / BPM / Master Trim Popover */}
            <Popover>
              <PopoverTrigger asChild>
                <Button
                  size="sm"
                  variant="outline"
                  className="rounded-full px-3.5 gap-1.5 font-mono text-[11px] bg-card/85 shadow-[0_6px_18px_rgba(11,14,16,0.06)]"
                >
                  <SlidersHorizontal className="size-3 text-muted-foreground" />
                  <span className="tabular-nums">
                    {Math.round(chain.tempoBpm ?? 120)} BPM ·{" "}
                    {chain.routingMode === 1 ? tx("병렬", "Parallel") : tx("직렬", "Serial")}
                  </span>
                </Button>
              </PopoverTrigger>
              <PopoverContent align="end" className="w-80 rounded-[16px] p-4 space-y-4">
                {/* Tempo Row */}
                <div className="space-y-1.5">
                  <div className="text-[11px] font-medium text-muted-foreground flex items-center gap-1.5">
                    <Clock className="size-3.5" />
                    <span>{tx("템포 (40–280 BPM)", "Tempo (40–280 BPM)")}</span>
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
                          const clampedBpm = Math.max(40, Math.min(280, v))
                          if (isVst3Host && onParamGesture) {
                            emitGesture("begin", "tempo", clampedBpm)
                            emitGesture("change", "tempo", clampedBpm)
                            emitGesture("end", "tempo", clampedBpm)
                            onPushChain({ ...chain, tempoBpm: clampedBpm }, false, true, true)
                          } else {
                            onPushChain({ ...chain, tempoBpm: clampedBpm }, true, true)
                          }
                        }
                      }}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") e.currentTarget.blur()
                      }}
                      className="h-8 font-mono tabular-nums"
                    />
                    <ContextMenu>
                      <ContextMenuTrigger asChild>
                        <span>
                          <Hint content={tx("연속 클릭 또는 T 키로 탭 템포", "Tap tempo")} kbd="T">
                            <Button size="sm" variant="signal" onClick={handleTapTempo}>
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
                </div>

                {/* Serial / Parallel Routing Toggle */}
                <div className="space-y-2">
                  <div className="text-[11px] font-medium text-muted-foreground flex items-center gap-1.5">
                    <GitBranch className="size-3.5" />
                    <span>{tx("신호 경로", "Signal Routing")}</span>
                    <HelpHint
                      side="top"
                      content={tx(
                        "병렬 분기 시 Row 1·Row 2로 신호를 나누어 듀얼 앰프·캐비넷을 블렌딩할 수 있습니다.",
                        "Parallel routing splits the chain across Row 1 & Row 2 for dual-amp/cab blending."
                      )}
                    />
                  </div>
                  <div className="grid grid-cols-2 gap-1.5">
                    <Button
                      size="sm"
                      variant={chain.routingMode !== 1 ? "default" : "outline"}
                      onClick={() => {
                        if (isVst3Host && onParamGesture) {
                          emitGesture("begin", "routing", 0)
                          emitGesture("change", "routing", 0)
                          emitGesture("end", "routing", 0)
                          onPushChain({ ...chain, routingMode: 0 }, false, false, true)
                        } else {
                          onPushChain({ ...chain, routingMode: 0 }, true)
                        }
                      }}
                    >
                      {tx("직렬 (Serial)", "Serial")}
                    </Button>
                    <Button
                      size="sm"
                      variant={chain.routingMode === 1 ? "signal" : "outline"}
                      onClick={() => {
                        if (isVst3Host && onParamGesture) {
                          emitGesture("begin", "routing", 1)
                          emitGesture("change", "routing", 1)
                          emitGesture("end", "routing", 1)
                          onPushChain({ ...chain, routingMode: 1 }, false, false, true)
                        } else {
                          onPushChain({ ...chain, routingMode: 1 }, true)
                        }
                      }}
                    >
                      {tx("병렬 분기 (Parallel)", "Parallel")}
                    </Button>
                  </div>

                  {chain.routingMode === 1 && (
                    <div className="rounded-[12px] border border-border bg-secondary/45 p-3 space-y-2.5 text-xs">
                      <div className="flex items-center justify-between">
                        <span className="text-muted-foreground">{tx("분기 지점 (Split)", "Split Col")}</span>
                        <div className="flex items-center gap-1.5">
                          <Button
                            size="icon-xs"
                            variant="outline"
                            disabled={splitCol <= 0}
                            onClick={() => onPushChain({ ...chain, splitCol: Math.max(0, splitCol - 1) }, false)}
                          >
                            <Minus className="size-3" />
                          </Button>
                          <span className="w-5 text-center font-mono font-bold tabular-nums">
                            {splitCol + 1}
                          </span>
                          <Button
                            size="icon-xs"
                            variant="outline"
                            disabled={splitCol >= mergeCol}
                            onClick={() =>
                              onPushChain({ ...chain, splitCol: Math.min(mergeCol, splitCol + 1) }, false)
                            }
                          >
                            <Plus className="size-3" />
                          </Button>
                        </div>
                      </div>

                      <div className="flex items-center justify-between">
                        <span className="text-muted-foreground">{tx("합류 지점 (Merge)", "Merge Col")}</span>
                        <div className="flex items-center gap-1.5">
                          <Button
                            size="icon-xs"
                            variant="outline"
                            disabled={mergeCol <= splitCol}
                            onClick={() =>
                              onPushChain({ ...chain, mergeCol: Math.max(splitCol, mergeCol - 1) }, false)
                            }
                          >
                            <Minus className="size-3" />
                          </Button>
                          <span className="w-5 text-center font-mono font-bold tabular-nums">
                            {mergeCol + 1}
                          </span>
                          <Button
                            size="icon-xs"
                            variant="outline"
                            disabled={mergeCol >= 7}
                            onClick={() => onPushChain({ ...chain, mergeCol: Math.min(7, mergeCol + 1) }, false)}
                          >
                            <Plus className="size-3" />
                          </Button>
                        </div>
                      </div>

                      <div className="space-y-1">
                        <div className="flex items-center justify-between">
                          <span className="text-muted-foreground">{tx("병렬 믹스", "Parallel Mix")}</span>
                          <span className="font-mono font-semibold tabular-nums">
                            {Math.round(chain.parallelMix * 100)}%
                          </span>
                        </div>
                        <Slider
                          value={[Math.round(chain.parallelMix * 100)]}
                          min={0}
                          max={100}
                          step={1}
                          onPointerDown={() => emitGesture("begin", "parallelMix", chain.parallelMix * 100)}
                          onValueChange={([v]) => {
                            if (isVst3Host && onParamGesture) {
                              emitGesture("change", "parallelMix", v)
                              onPushChain({ ...chain, parallelMix: v / 100 }, false, false, true)
                            } else {
                              onPushChain({ ...chain, parallelMix: v / 100 }, false)
                            }
                          }}
                          onValueCommit={([v]) => emitGesture("end", "parallelMix", v)}
                        />
                      </div>
                    </div>
                  )}
                </div>

                {/* Master Input / Output Gain Trim */}
                <div className="space-y-2.5 pt-2 border-t border-border text-xs">
                  <div className="space-y-1">
                    <div className="flex items-center justify-between">
                      <span className="inline-flex items-center gap-1.5 text-muted-foreground">
                        <span>{tx("입력 트림 (Input)", "Input Trim")}</span>
                        <HelpHint
                          side="top"
                          content={tx(
                            "수치를 더블클릭하면 0.0 dB로 초기화됩니다.",
                            "Double-click any dB value to reset to 0.0 dB."
                          )}
                        />
                      </span>
                      <button
                        type="button"
                        onDoubleClick={() => {
                          if (isVst3Host && onParamGesture) {
                            emitGesture("begin", "inputGain", 0)
                            emitGesture("change", "inputGain", 0)
                            emitGesture("end", "inputGain", 0)
                            onPushChain({ ...chain, inputGainDb: 0 }, false, false, true)
                          } else {
                            onPushChain({ ...chain, inputGainDb: 0 }, false)
                          }
                        }}
                        className="font-mono font-semibold tabular-nums cursor-pointer"
                      >
                        {chain.inputGainDb >= 0
                          ? `+${chain.inputGainDb.toFixed(1)}`
                          : chain.inputGainDb.toFixed(1)}
                        dB
                      </button>
                    </div>
                    <Slider
                      value={[chain.inputGainDb]}
                      min={-12}
                      max={12}
                      step={0.5}
                      onPointerDown={() => emitGesture("begin", "inputGain", chain.inputGainDb)}
                      onValueChange={([v]) => {
                        if (isVst3Host && onParamGesture) {
                          emitGesture("change", "inputGain", v)
                          onPushChain({ ...chain, inputGainDb: v }, false, false, true)
                        } else {
                          onPushChain({ ...chain, inputGainDb: v }, false)
                        }
                      }}
                      onValueCommit={([v]) => emitGesture("end", "inputGain", v)}
                    />
                  </div>

                  <div className="space-y-1">
                    <div className="flex items-center justify-between">
                      <span className="text-muted-foreground">{tx("마스터 출력 (Output)", "Output Gain")}</span>
                      <button
                        type="button"
                        onDoubleClick={() => {
                          if (isVst3Host && onParamGesture) {
                            emitGesture("begin", "outputGain", 0)
                            emitGesture("change", "outputGain", 0)
                            emitGesture("end", "outputGain", 0)
                            onPushChain({ ...chain, outputGainDb: 0 }, false, false, true)
                          } else {
                            onPushChain({ ...chain, outputGainDb: 0 }, false)
                          }
                        }}
                        className="font-mono font-semibold tabular-nums cursor-pointer"
                      >
                        {chain.outputGainDb >= 0
                          ? `+${chain.outputGainDb.toFixed(1)}`
                          : chain.outputGainDb.toFixed(1)}
                        dB
                      </button>
                    </div>
                    <Slider
                      value={[chain.outputGainDb]}
                      min={-12}
                      max={12}
                      step={0.5}
                      onPointerDown={() => emitGesture("begin", "outputGain", chain.outputGainDb)}
                      onValueChange={([v]) => {
                        if (isVst3Host && onParamGesture) {
                          emitGesture("change", "outputGain", v)
                          onPushChain({ ...chain, outputGainDb: v }, false, false, true)
                        } else {
                          onPushChain({ ...chain, outputGainDb: v }, false)
                        }
                      }}
                      onValueCommit={([v]) => emitGesture("end", "outputGain", v)}
                    />
                  </div>
                </div>
              </PopoverContent>
            </Popover>
          </div>
        </header>

        {/* Centerpiece: Pure 2x8 Dark Obsidian Signal Chain */}
        <CortexGrid
          chain={chain}
          tones={tones}
          selectedSlot={inspectorOpen || catalogOpen ? selectedSlot : -1}
          midiState={midiState}
          onSelectSlot={handleSelectSlot}
          onToggleBypass={toggleSlotBypass}
          onResetSlot={resetSlotDefaults}
          onRemoveSlot={removeSlotBlock}
          onMoveOrSwap={moveOrSwapSlot}
          onDropModelFromLibrary={onAssignModel}
          onStartMidiLearn={onStartMidiLearn}
          onClearMidiBinding={onClearMidiBinding}
          meterL={meterL}
          meterR={meterR}
          clip={clip}
        />
      </div>

      {/* Floating Bottom Slot Parameter Sheet (Shown ONLY when a gear tile is clicked) */}
      {inspectorOpen && currentModel && currentCategory && (
        <section
          data-tour="inspector-dock"
          className="fixed inset-x-0 bottom-[80px] z-30 mx-auto w-[min(960px,calc(100vw-48px))] rounded-[18px] border border-border bg-card/96 backdrop-blur-md shadow-[0_24px_64px_rgba(11,14,16,0.22)] overflow-hidden nr-float-up"
        >
          <div className="h-11 shrink-0 px-4 border-b border-border bg-secondary/40 flex items-center justify-between gap-2">
            <div className="flex items-center gap-2 min-w-0 flex-1">
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
                {currentModel.categoryLabel}
              </Badge>
              <span className="font-display font-normal text-sm truncate">{currentModel.name}</span>
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
                    <SelectTrigger className="h-7 w-40 text-[11px]">
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
                  <Button size="xs" variant="keycap" onClick={() => void importNamWithSlot(selectedSlot)}>
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

              <Button size="xs" variant="outline" onClick={() => setCatalogOpen(true)}>
                {tx("장비 변경", "Swap")}
              </Button>

              <Separator orientation="vertical" className="h-4" />

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
              <Hint content={tx("닫기", "Close")} kbd="Esc">
                <Button size="icon-xs" variant="ghost" onClick={() => setInspectorOpen(false)}>
                  <X className="size-3.5" />
                </Button>
              </Hint>
            </div>
          </div>

          <ScrollArea className={currentModel.category === "cab" && cabRoomOpen ? "h-[248px]" : "max-h-[208px]"}>
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
                    : "grid grid-cols-2 sm:grid-cols-4"
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
                        onChange={(checked) => {
                          const v = checked ? 1 : 0
                          if (isVst3Host && onParamGesture) {
                            emitGesture("begin", "slotParam", v, selectedSlot, p.index)
                            updateSlotParam(selectedSlot, p.index, v)
                            emitGesture("end", "slotParam", v, selectedSlot, p.index)
                          } else {
                            updateSlotParam(selectedSlot, p.index, v)
                          }
                        }}
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
                      onGestureStart={() =>
                        emitGesture("begin", "slotParam", val, selectedSlot, p.index)
                      }
                      onChange={(nextVal) => updateSlotParam(selectedSlot, p.index, nextVal)}
                      onGestureEnd={() =>
                        emitGesture(
                          "end",
                          "slotParam",
                          chain.slots[selectedSlot]?.params[p.index] ?? val,
                          selectedSlot,
                          p.index
                        )
                      }
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
          </ScrollArea>
        </section>
      )}

      {/* Centered Toss-Style Gear Catalog Picker (Shown ONLY when an empty slot or Swap is clicked) */}
      {catalogOpen && (
        <div
          onClick={() => setCatalogOpen(false)}
          className="fixed inset-0 z-40 flex items-center justify-center bg-black/25 backdrop-blur-[2px] p-4"
        >
          <div
            data-tour-catalog="true"
            onClick={(e) => e.stopPropagation()}
            className="w-[460px] max-w-full h-[500px] max-h-[82vh] rounded-[18px] border border-border bg-popover p-4 shadow-[0_32px_80px_rgba(11,14,16,0.32)] flex flex-col gap-3"
          >
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <div className="font-mono text-[10.5px] font-semibold text-primary">
                  SLOT #{String(selectedSlot + 1).padStart(2, "0")}
                </div>
                <div className="flex items-center gap-1.5">
                  <h2 className="font-display font-light text-lg leading-snug tracking-tight text-foreground truncate">
                    {tx("장비 선택", "Choose Gear")}
                  </h2>
                  <HelpHint
                    content={tx(
                      "원본 하드웨어 모델명(TS808, Plexi 등)으로도 검색할 수 있습니다.",
                      "Search by studio name or original hardware (TS808, Plexi)."
                    )}
                  />
                </div>
              </div>
              <Button size="icon-xs" variant="ghost" className="shrink-0" onClick={() => setCatalogOpen(false)}>
                <X className="size-4" />
              </Button>
            </div>

            <div className="relative flex items-center">
              <Search className="size-3.5 text-muted-foreground absolute left-3 pointer-events-none" />
              <Input
                autoFocus
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Escape") {
                    if (searchQuery) {
                      setSearchQuery("")
                    } else {
                      setCatalogOpen(false)
                    }
                  }
                }}
                placeholder={tx("장비 검색…", "Search gear…")}
                className="pl-8 pr-7 h-9 rounded-[10px]"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery("")}
                  className="absolute right-2.5 text-muted-foreground hover:text-foreground cursor-pointer"
                >
                  <X className="size-3.5" />
                </button>
              )}
            </div>

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
                      className={`relative h-8 rounded-[8px] border flex items-center justify-center transition-colors cursor-pointer ${
                        active
                          ? "bg-foreground text-background border-foreground"
                          : "bg-background/60 text-muted-foreground border-border hover:text-foreground hover:bg-card"
                      }`}
                    >
                      <span
                        className="absolute top-0 inset-x-1.5 h-[2px] rounded-full"
                        style={{ backgroundColor: cat.hex }}
                      />
                      <CategoryVectorIcon category={cat.id} className="size-3.5 mt-0.5" />
                    </button>
                  </Hint>
                )
              })}
            </div>

            <ScrollArea className="flex-1 min-h-0">
              <div className="space-y-1 pr-1 w-full min-w-0">
                {categoryModels.length === 0 ? (
                  <div className="py-10 text-center text-xs text-muted-foreground">
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
                        onClick={() => handlePickModel(model.id)}
                        className={`w-full min-w-0 overflow-hidden text-left px-3 py-2 rounded-[10px] text-xs transition-colors flex items-center justify-between gap-2 border cursor-pointer ${
                          isInSelectedSlot
                            ? "bg-primary/12 text-foreground font-semibold border-primary"
                            : "text-foreground hover:bg-secondary/75 border-transparent"
                        }`}
                      >
                        <div className="flex items-center gap-2.5 min-w-0 flex-1">
                          <span
                            className="size-2 rounded-full shrink-0"
                            style={{ backgroundColor: catColor }}
                          />
                          <span className="truncate">{model.name}</span>
                        </div>
                        <span className="text-[10px] font-mono text-muted-foreground uppercase shrink-0">
                          {model.category}
                        </span>
                      </button>
                    )
                  })
                )}
              </div>
            </ScrollArea>
          </div>
        </div>
      )}

      {/* Floating Looper & Recording Pod (Centered cleanly above Liquid Glass Dock) */}
      {looperOpen && (
        <div className="fixed inset-x-0 bottom-[80px] z-30 flex justify-center pointer-events-none px-4">
          <div
            data-tour="looper-pod-frame"
            className="pointer-events-auto relative w-[420px] max-w-full h-[256px] shadow-[0_24px_64px_rgba(11,14,16,0.32)] rounded-[16px] nr-float-up"
          >
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
            <button
              type="button"
              aria-label={tx("루퍼 닫기", "Close Looper")}
              onClick={() => onLooperOpenChange?.(false)}
              className="absolute -top-2.5 -right-2.5 size-6 rounded-full bg-card border border-border text-foreground hover:bg-secondary flex items-center justify-center shadow-md cursor-pointer"
            >
              <X className="size-3.5" />
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
