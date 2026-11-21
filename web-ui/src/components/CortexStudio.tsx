import React, { useEffect, useMemo, useRef, useState } from "react"
import {
  ArrowLeft,
  ArrowRight,
  ChevronLeft,
  ChevronRight,
  Download,
  FolderOpen,
  GitBranch,
  Music,
  Pause,
  Play,
  Power,
  Repeat,
  RotateCcw,
  Save,
  Trash2,
  Upload,
  Volume2,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { ScrollArea } from "@/components/ui/scroll-area"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Slider } from "@/components/ui/slider"
import { CategoryVectorIcon } from "@/components/CortexIcons"
import { CortexGrid } from "@/components/CortexGrid"
import { CortexKnob, CortexToggleCell } from "@/components/CortexKnob"
import { VisualizerCanvas } from "@/components/VisualizerCanvas"
import {
  CATEGORIES,
  CATEGORY_MAP,
  DEVICE_CATALOG,
  DEVICE_MAP,
  FACTORY_PRESETS,
  SCENE_LABELS,
  commitActiveScene,
  createDefaultParams,
  ensurePresetScenes,
  switchSceneOnChain,
  syncScenesOnAssignSlot,
  syncScenesOnSwapSlots,
} from "@/data/cortexCatalog"
import { studioBridge } from "@/lib/bridge"
import type {
  BackingTrackStateDto,
  DeviceCategoryId,
  ModelProfileSummary,
  SignalChainConfigDto,
  TelemetryFrame,
} from "@/types/rig"

interface CortexStudioProps {
  activeView: "grid" | "directory"
  onChangeView: (view: "grid" | "directory") => void
  initialChain?: SignalChainConfigDto
  initialSavedPresets?: SignalChainConfigDto[]
  tones: ModelProfileSummary[]
  selectedToneId: string
  customCabIrName: string
  initialBacking?: BackingTrackStateDto
  onTonesUpdated: (tones: ModelProfileSummary[], selectedId: string) => void
  onSelectToneId: (id: string) => void
  onCustomCabIrChange: (name: string) => void
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

export const CortexStudio: React.FC<CortexStudioProps> = ({
  activeView,
  onChangeView,
  initialChain,
  initialSavedPresets = [],
  tones,
  selectedToneId,
  customCabIrName,
  initialBacking,
  onTonesUpdated,
  onSelectToneId,
  onCustomCabIrChange,
  onLog,
}) => {
  const [chain, setChain] = useState<SignalChainConfigDto>(() => ensurePresetScenes(initialChain ?? FACTORY_PRESETS[0]))
  const [savedPresets, setSavedPresets] = useState<SignalChainConfigDto[]>(initialSavedPresets)
  const [selectedSlot, setSelectedSlot] = useState<number>(2) // Amp slot selected by default
  const [activeCategory, setActiveCategory] = useState<DeviceCategoryId>("amp")
  const [editingName, setEditingName] = useState(false)

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

  const syncTimerRef = useRef<number | null>(null)

  useEffect(() => {
    if (initialChain) setChain(ensurePresetScenes(initialChain))
  }, [initialChain])

  useEffect(() => {
    setSavedPresets(initialSavedPresets)
  }, [initialSavedPresets])

  useEffect(() => {
    if (initialBacking) setBacking(initialBacking)
  }, [initialBacking])

  useEffect(() => {
    const unsubChain = studioBridge.on<{
      signalChain?: SignalChainConfigDto
      savedPresets?: SignalChainConfigDto[]
      summary?: string
    }>("signalChainChanged", (ev) => {
      if (ev?.signalChain) {
        const nextChain = ensurePresetScenes(ev.signalChain)
        setChain(nextChain)
        const firstAmp = nextChain.slots.findIndex((s) => s.modelId.startsWith("amp_") || s.modelId === "nam_capture")
        if (firstAmp >= 0) setSelectedSlot(firstAmp)
        onLog(ev.summary ? `[MCP] ${ev.summary}` : "[MCP] Antigravity에서 새 프리셋이 적용되었습니다.", "success")
      }
      if (ev?.savedPresets) setSavedPresets(ev.savedPresets)
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
    })
    return () => {
      unsubChain()
      unsub()
    }
  }, [])

  // Push signal chain changes to C# real-time engine (auto-commits current Scene A..H snapshot)
  const pushChainToEngine = (next: SignalChainConfigDto, immediate = false, preserveScenes = false) => {
    const finalized = preserveScenes ? ensurePresetScenes(next) : commitActiveScene(next)
    setChain(finalized)
    if (!studioBridge.isWebView2) return
    if (syncTimerRef.current !== null) {
      window.clearTimeout(syncTimerRef.current)
      syncTimerRef.current = null
    }
    const send = () => {
      studioBridge.invoke("updateSignalChain", finalized).catch((e) => onLog(`체인 업데이트 실패: ${String(e)}`, "error"))
    }
    if (immediate) send()
    else syncTimerRef.current = window.setTimeout(send, 16)
  }

  const allPresets = useMemo(() => {
    const map = new Map<string, SignalChainConfigDto>()
    for (const p of FACTORY_PRESETS) map.set(p.presetId, p)
    for (const p of savedPresets) map.set(p.presetId, p)
    return Array.from(map.values())
  }, [savedPresets])

  const currentPresetIdx = useMemo(() => {
    const idx = allPresets.findIndex((p) => p.presetId === chain.presetId)
    return idx >= 0 ? idx : 0
  }, [allPresets, chain.presetId])

  const loadPreset = (preset: SignalChainConfigDto) => {
    const clone: SignalChainConfigDto = ensurePresetScenes({
      ...preset,
      slots: preset.slots.map((s) => ({ ...s, params: [...s.params] })),
      scenes: preset.scenes?.map((sc) => ({
        parallelMix: sc.parallelMix,
        slots: sc.slots.map((sl) => ({ enabled: sl.enabled, params: [...sl.params] })),
      })),
    })
    pushChainToEngine(clone, true, true)
    const firstAmp = clone.slots.findIndex((s) => s.modelId.startsWith("amp_") || s.modelId === "nam_capture")
    if (firstAmp >= 0) setSelectedSlot(firstAmp)
    onLog(`[프리셋] ${clone.presetId} ${clone.presetName} 로드`, "info")
  }

  const stepPreset = (delta: number) => {
    if (allPresets.length === 0) return
    const nextIdx = (currentPresetIdx + delta + allPresets.length) % allPresets.length
    loadPreset(allPresets[nextIdx])
  }

  const handleSavePreset = async () => {
    if (!studioBridge.isWebView2) {
      setSavedPresets((prev) => {
        const filtered = prev.filter((p) => p.presetId !== chain.presetId)
        return [...filtered, chain]
      })
      onLog(`[프리셋] ${chain.presetId} ${chain.presetName} 저장됨`, "success")
      return
    }
    try {
      const res = await studioBridge.invoke<{ savedPresets: SignalChainConfigDto[] }>("saveUserPreset", chain)
      if (res?.savedPresets) setSavedPresets(res.savedPresets)
      onLog(`[프리셋] ${chain.presetId} ${chain.presetName} 저장됨`, "success")
    } catch (e) {
      onLog(`프리셋 저장 실패: ${String(e)}`, "error")
    }
  }

  const handleDeleteUserPreset = async (presetId: string) => {
    if (!studioBridge.isWebView2) {
      setSavedPresets((prev) => prev.filter((p) => p.presetId !== presetId))
      return
    }
    try {
      const res = await studioBridge.invoke<{ savedPresets: SignalChainConfigDto[] }>("deleteUserPreset", { presetId })
      if (res?.savedPresets) setSavedPresets(res.savedPresets)
    } catch (e) {
      onLog(`프리셋 삭제 실패: ${String(e)}`, "error")
    }
  }

  // Slot operations
  const currentSlot = chain.slots[selectedSlot]
  const currentModel = currentSlot?.modelId ? DEVICE_MAP[currentSlot.modelId] : undefined
  const currentCategory = currentModel ? CATEGORY_MAP[currentModel.category] : undefined

  const handleSelectSlot = (idx: number) => {
    setSelectedSlot(idx)
    const modelId = chain.slots[idx]?.modelId
    if (modelId && DEVICE_MAP[modelId]) {
      setActiveCategory(DEVICE_MAP[modelId].category)
    }
  }

  const assignModelToSlot = (modelId: string, slotIdx: number) => {
    const nextSlots = chain.slots.map((s) => ({ ...s, params: [...s.params] }))
    const nextParams = createDefaultParams(modelId)
    nextSlots[slotIdx] = {
      index: slotIdx,
      modelId,
      enabled: true,
      params: nextParams,
    }
    setSelectedSlot(slotIdx)
    pushChainToEngine(syncScenesOnAssignSlot({ ...chain, slots: nextSlots }, slotIdx, modelId, nextParams), true, true)
  }

  const toggleSlotBypass = (slotIdx: number) => {
    const target = chain.slots[slotIdx]
    if (!target?.modelId) return
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx ? { ...s, enabled: !s.enabled, params: [...s.params] } : { ...s, params: [...s.params] }
    )
    pushChainToEngine({ ...chain, slots: nextSlots }, true)
  }

  const removeSlotBlock = (slotIdx: number) => {
    const emptyParams = [0, 0, 0, 0, 0, 0, 0, 0]
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx
        ? { index: i, modelId: "", enabled: true, params: [...emptyParams] }
        : { ...s, params: [...s.params] }
    )
    pushChainToEngine(syncScenesOnAssignSlot({ ...chain, slots: nextSlots }, slotIdx, "", emptyParams), true, true)
  }

  const moveOrSwapSlot = (fromIdx: number, toIdx: number) => {
    if (fromIdx === toIdx || toIdx < 0 || toIdx >= 16) return
    const nextSlots = chain.slots.map((s) => ({ ...s, params: [...s.params] }))
    const a = { ...nextSlots[fromIdx], index: toIdx }
    const b = { ...nextSlots[toIdx], index: fromIdx }
    nextSlots[toIdx] = a
    nextSlots[fromIdx] = b
    setSelectedSlot(toIdx)
    pushChainToEngine(syncScenesOnSwapSlots({ ...chain, slots: nextSlots }, fromIdx, toIdx), true, true)
  }

  const updateSlotParam = (slotIdx: number, paramIdx: number, val: number) => {
    const nextSlots = chain.slots.map((s, i) => {
      if (i !== slotIdx) return s
      const p = [...s.params]
      p[paramIdx] = val
      return { ...s, params: p }
    })
    pushChainToEngine({ ...chain, slots: nextSlots }, false)
  }

  const resetSlotDefaults = (slotIdx: number) => {
    const slot = chain.slots[slotIdx]
    if (!slot?.modelId) return
    const nextSlots = chain.slots.map((s, i) =>
      i === slotIdx ? { ...s, params: createDefaultParams(slot.modelId) } : s
    )
    pushChainToEngine({ ...chain, slots: nextSlots }, true)
  }

  // Native file dialogs (.nam, Custom WAV IR, Backing track)
  const handleSelectNamModel = async (id: string) => {
    onSelectToneId(id)
    if (!studioBridge.isWebView2) return
    try {
      await studioBridge.invoke("selectTone", { id })
    } catch (e) {
      onLog(`NAM 모델 선택 실패: ${String(e)}`, "error")
    }
  }

  const handleImportNam = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{
        cancelled?: boolean
        tones?: ModelProfileSummary[]
        selectedToneId?: string
      }>("importNamFile", {})
      if (!res?.cancelled && res?.tones) {
        onTonesUpdated(res.tones, res.selectedToneId || "")
      }
    } catch (e) {
      onLog(`NAM 가져오기 실패: ${String(e)}`, "error")
    }
  }

  const handleExportNam = async () => {
    if (!studioBridge.isWebView2) return
    try {
      await studioBridge.invoke("exportNamFile", { toneId: selectedToneId })
    } catch (e) {
      onLog(`NAM 내보내기 실패: ${String(e)}`, "error")
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
      onLog(`NAM 삭제 실패: ${String(e)}`, "error")
    }
  }

  const handleLoadCabIr = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ cancelled?: boolean; customCabIrName?: string }>("loadCustomCabIr", {})
      if (!res?.cancelled && res?.customCabIrName !== undefined) {
        onCustomCabIrChange(res.customCabIrName)
      }
    } catch (e) {
      onLog(`Cab IR 로드 실패: ${String(e)}`, "error")
    }
  }

  const handleClearCabIr = async () => {
    if (!studioBridge.isWebView2) return
    try {
      await studioBridge.invoke("clearCustomCabIr")
      onCustomCabIrChange("")
    } catch (e) {
      onLog(`Cab IR 초기화 실패: ${String(e)}`, "error")
    }
  }

  const handleLoadBacking = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ cancelled?: boolean; backing?: BackingTrackStateDto }>("loadBackingTrack", {})
      if (!res?.cancelled && res?.backing) setBacking(res.backing)
    } catch (e) {
      onLog(`백킹 트랙 로드 실패: ${String(e)}`, "error")
    }
  }

  const controlBacking = async (patch: { playing?: boolean; loop?: boolean; volume?: number; seekSec?: number }) => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ backing: BackingTrackStateDto }>("controlBackingTrack", patch)
      if (res?.backing) setBacking(res.backing)
    } catch (e) {
      onLog(`백킹 트랙 제어 실패: ${String(e)}`, "error")
    }
  }

  const unloadBacking = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ backing: BackingTrackStateDto }>("unloadBackingTrack")
      if (res?.backing) setBacking(res.backing)
    } catch (e) {
      onLog(`백킹 트랙 해제 실패: ${String(e)}`, "error")
    }
  }

  const categoryModels = useMemo(
    () => DEVICE_CATALOG.filter((d) => d.category === activeCategory),
    [activeCategory]
  )

  const formatTime = (sec: number) => {
    const s = Math.max(0, Math.floor(sec))
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`
  }

  // ───────────────────────────────────────────────────────────────────────────
  // DIRECTORY VIEW (Presets, .nam Neural Captures, Cab IR, MR Mixer, Visualizer)
  // ───────────────────────────────────────────────────────────────────────────
  if (activeView === "directory") {
    return (
      <div className="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-12 bg-background overflow-hidden">
        {/* Left: Presets Bank */}
        <div className="lg:col-span-4 border-r border-border flex flex-col min-h-0 bg-card/40">
          <div className="h-10 px-4 border-b border-border flex items-center justify-between">
            <span className="text-xs font-semibold tracking-wider uppercase text-foreground">PRESET BANK</span>
            <Badge variant="outline" className="text-[10px] font-mono">
              {allPresets.length} RIGS
            </Badge>
          </div>
          <ScrollArea className="flex-1 p-2">
            <div className="space-y-1">
              {allPresets.map((p) => {
                const isActive = p.presetId === chain.presetId
                const isUser = savedPresets.some((u) => u.presetId === p.presetId)
                return (
                  <div
                    key={p.presetId}
                    className={`flex items-center justify-between px-3 py-2 rounded-md border transition-colors cursor-pointer ${
                      isActive
                        ? "bg-accent border-foreground/30 text-foreground"
                        : "bg-transparent border-transparent hover:bg-card text-muted-foreground hover:text-foreground"
                    }`}
                    onClick={() => {
                      loadPreset(p)
                      onChangeView("grid")
                    }}
                  >
                    <div className="flex items-center gap-2.5 min-w-0">
                      <span className="font-mono text-[11px] font-bold text-muted-foreground w-7 shrink-0">
                        {p.presetId}
                      </span>
                      <span className="text-xs font-medium truncate">{p.presetName}</span>
                    </div>
                    <div className="flex items-center gap-1.5 shrink-0">
                      {isUser && (
                        <Button
                          size="icon"
                          variant="ghost"
                          className="h-6 w-6 text-muted-foreground hover:text-destructive"
                          onClick={(e) => {
                            e.stopPropagation()
                            handleDeleteUserPreset(p.presetId)
                          }}
                        >
                          <Trash2 className="w-3 h-3" />
                        </Button>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          </ScrollArea>
        </div>

        {/* Middle: Neural Captures (.nam) & Cab IR */}
        <div className="lg:col-span-4 border-r border-border flex flex-col min-h-0 bg-card/20">
          <div className="h-10 px-4 border-b border-border flex items-center justify-between">
            <span className="text-xs font-semibold tracking-wider uppercase text-foreground">NEURAL CAPTURES (.NAM)</span>
            <div className="flex items-center gap-1">
              <Button size="sm" variant="secondary" className="h-6 text-[11px] px-2" onClick={handleImportNam}>
                <Upload className="w-3 h-3 mr-1" />
                가져오기
              </Button>
              <Button
                size="sm"
                variant="ghost"
                className="h-6 text-[11px] px-2"
                disabled={!selectedToneId}
                onClick={handleExportNam}
              >
                <Download className="w-3 h-3" />
              </Button>
            </div>
          </div>
          <ScrollArea className="flex-1 p-2">
            {tones.length === 0 ? (
              <div className="p-6 text-center text-xs text-muted-foreground">
                로드된 `.nam` 파일이 없습니다. 상단 가져오기 버튼으로 공식 Neural Amp Modeler 파일을 추가하세요.
              </div>
            ) : (
              <div className="space-y-1">
                {tones.map((t) => {
                  const sel = t.id === selectedToneId
                  return (
                    <div
                      key={t.id}
                      onClick={() => handleSelectNamModel(t.id)}
                      className={`flex items-center justify-between px-3 py-2 rounded-md border cursor-pointer transition-colors ${
                        sel
                          ? "bg-accent border-foreground/30 text-foreground"
                          : "border-transparent hover:bg-card text-muted-foreground hover:text-foreground"
                      }`}
                    >
                      <div className="min-w-0">
                        <div className="text-xs font-medium truncate">{t.name}</div>
                        <div className="text-[10px] font-mono text-muted-foreground">
                          {t.archPreset} · {t.sampleRate} Hz
                        </div>
                      </div>
                      <Button
                        size="icon"
                        variant="ghost"
                        className="h-6 w-6 text-muted-foreground hover:text-destructive shrink-0"
                        onClick={(e) => {
                          e.stopPropagation()
                          handleDeleteNam(t.id)
                        }}
                      >
                        <Trash2 className="w-3 h-3" />
                      </Button>
                    </div>
                  )
                })}
              </div>
            )}
          </ScrollArea>

          {/* External Cab IR Section */}
          <div className="border-t border-border p-3 bg-card/50 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
                CUSTOM CAB IR (.WAV)
              </span>
              <div className="flex items-center gap-1">
                <Button size="sm" variant="secondary" className="h-6 text-[11px] px-2" onClick={handleLoadCabIr}>
                  <FolderOpen className="w-3 h-3 mr-1" />
                  IR 열기
                </Button>
                {customCabIrName && (
                  <Button size="sm" variant="ghost" className="h-6 text-[11px] px-2" onClick={handleClearCabIr}>
                    <RotateCcw className="w-3 h-3" />
                  </Button>
                )}
              </div>
            </div>
            <div className="text-xs font-mono truncate text-foreground/90 bg-background/60 px-2.5 py-1.5 rounded border border-border">
              {customCabIrName || "내장 512-tap 4x12 V30 Modern IR"}
            </div>
          </div>
        </div>

        {/* Right: Live Visualizer + MR Backing Track */}
        <div className="lg:col-span-4 flex flex-col min-h-0 p-3 gap-3 bg-background">
          <div className="flex-1 min-h-[180px] rounded-lg border border-border bg-card/40 overflow-hidden p-2 flex flex-col">
            <div className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground px-1 pb-1.5">
              LIVE SPECTRUM &amp; WAVEFORM
            </div>
            <div className="flex-1 min-h-0">
              <VisualizerCanvas />
            </div>
          </div>

          <div className="rounded-lg border border-border bg-card/50 p-3 space-y-2.5">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
                <Music className="w-3.5 h-3.5" />
                BACKING TRACK (MR)
              </div>
              <div className="flex items-center gap-1">
                <Button size="sm" variant="secondary" className="h-6 text-[11px] px-2" onClick={handleLoadBacking}>
                  <FolderOpen className="w-3 h-3 mr-1" />
                  MR 열기
                </Button>
                {backing.loaded && (
                  <Button size="icon" variant="ghost" className="h-6 w-6" onClick={unloadBacking}>
                    <Trash2 className="w-3 h-3" />
                  </Button>
                )}
              </div>
            </div>

            <div className="text-xs truncate text-foreground/90 font-medium">
              {backing.loaded ? backing.name : "MR 파일이 로드되지 않았습니다"}
            </div>

            {backing.loaded && (
              <>
                <div className="flex items-center gap-2">
                  <Button
                    size="icon"
                    variant="secondary"
                    className="h-7 w-7 shrink-0"
                    onClick={() => controlBacking({ playing: !backing.playing })}
                  >
                    {backing.playing ? <Pause className="w-3.5 h-3.5" /> : <Play className="w-3.5 h-3.5" />}
                  </Button>
                  <Button
                    size="icon"
                    variant={backing.loop ? "default" : "ghost"}
                    className="h-7 w-7 shrink-0"
                    onClick={() => controlBacking({ loop: !backing.loop })}
                  >
                    <Repeat className="w-3.5 h-3.5" />
                  </Button>
                  <div className="flex-1">
                    <Slider
                      value={[backing.positionSec]}
                      min={0}
                      max={Math.max(1, backing.durationSec)}
                      step={0.5}
                      onValueChange={([v]) => controlBacking({ seekSec: v })}
                    />
                  </div>
                  <span className="text-[10px] font-mono text-muted-foreground tabular-nums">
                    {formatTime(backing.positionSec)} / {formatTime(backing.durationSec)}
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <Volume2 className="w-3.5 h-3.5 text-muted-foreground shrink-0" />
                  <Slider
                    value={[Math.round(backing.volume * 100)]}
                    min={0}
                    max={100}
                    step={1}
                    onValueChange={([v]) => controlBacking({ volume: v / 100 })}
                  />
                  <span className="text-[10px] font-mono text-muted-foreground w-8 text-right">
                    {Math.round(backing.volume * 100)}%
                  </span>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    )
  }

  // ───────────────────────────────────────────────────────────────────────────
  // MAIN GRID VIEW (Exact Neural DSP Quad Cortex / Cortex Control Workspace)
  // ───────────────────────────────────────────────────────────────────────────
  return (
    <div className="flex-1 min-h-0 flex bg-background overflow-hidden">
      {/* ── Far-Left Vertical Category Icon Rail ── */}
      <aside className="w-12 shrink-0 border-r border-border bg-card/80 flex flex-col items-center py-2 gap-1">
        {CATEGORIES.map((cat) => {
          const active = activeCategory === cat.id
          return (
            <button
              key={cat.id}
              type="button"
              onClick={() => setActiveCategory(cat.id)}
              className={`relative w-10 h-10 rounded-md flex items-center justify-center transition-colors ${
                active ? "bg-accent text-foreground" : "text-muted-foreground/65 hover:text-foreground hover:bg-accent/50"
              }`}
              title={cat.label}
            >
              {active && (
                <span
                  className="absolute left-0 top-1.5 bottom-1.5 w-[3px] rounded-r-full"
                  style={{ backgroundColor: cat.hex }}
                />
              )}
              <CategoryVectorIcon category={cat.id} className="w-5 h-5" />
            </button>
          )
        })}
      </aside>

      {/* ── Left Device List Sidebar ── */}
      <aside className="w-56 shrink-0 border-r border-border bg-card/55 flex flex-col min-h-0">
        <div className="px-3.5 py-2.5 border-b border-border flex items-center justify-between">
          <span className="text-[11px] font-bold tracking-wider uppercase text-foreground">
            {CATEGORY_MAP[activeCategory].label}
          </span>
          <span
            className="w-2 h-2 rounded-full"
            style={{ backgroundColor: CATEGORY_MAP[activeCategory].hex }}
          />
        </div>

        <ScrollArea className="flex-1">
          <div className="p-2 space-y-0.5">
            {categoryModels.map((model) => {
              const isInSelectedSlot = currentSlot?.modelId === model.id
              return (
                <button
                  key={model.id}
                  type="button"
                  draggable
                  onDragStart={(e) => {
                    e.dataTransfer.setData("application/x-cortex-model", model.id)
                    e.dataTransfer.effectAllowed = "copy"
                  }}
                  onClick={() => assignModelToSlot(model.id, selectedSlot)}
                  className={`w-full text-left px-3 py-2 rounded-md text-xs transition-colors flex items-center justify-between gap-2 ${
                    isInSelectedSlot
                      ? "bg-emerald-500/15 text-emerald-300 font-semibold border border-emerald-500/30"
                      : "text-foreground/80 hover:bg-accent hover:text-foreground border border-transparent"
                  }`}
                  title="클릭하여 선택된 슬롯에 배치 / 드래그하여 원하는 슬롯에 배치"
                >
                  <span className="truncate">{model.name}</span>
                </button>
              )
            })}
          </div>
        </ScrollArea>

        <div className="px-3 py-2 border-t border-border text-[10px] text-muted-foreground">
          슬롯 #{selectedSlot + 1} ({selectedSlot < 8 ? "Row 1" : "Row 2"}) 선택됨
        </div>
      </aside>

      {/* ── Center + Bottom Main Area ── */}
      <div className="flex-1 min-w-0 flex flex-col min-h-0">
        {/* ── Preset & Scene Bar (Matches Cortex Control top strip) ── */}
        <div className="h-12 shrink-0 px-4 border-b border-border bg-card/45 flex items-center justify-between gap-3">
          {/* Left: Preset Switcher + Name + Save */}
          <div className="flex items-center gap-1.5 min-w-0">
            <Button
              size="icon"
              variant="secondary"
              className="h-7 w-7"
              onClick={() => stepPreset(-1)}
              title="이전 프리셋"
            >
              <ChevronLeft className="w-4 h-4" />
            </Button>
            <Button
              size="icon"
              variant="secondary"
              className="h-7 w-7"
              onClick={() => stepPreset(1)}
              title="다음 프리셋"
            >
              <ChevronRight className="w-4 h-4" />
            </Button>

            <div className="h-7 px-3 rounded-md bg-secondary/80 border border-border flex items-center gap-2 min-w-[200px] max-w-[280px]">
              <span className="text-xs font-mono font-bold text-muted-foreground">{chain.presetId}</span>
              {editingName ? (
                <Input
                  autoFocus
                  value={chain.presetName}
                  onChange={(e) => setChain({ ...chain, presetName: e.target.value })}
                  onBlur={() => {
                    setEditingName(false)
                    pushChainToEngine(chain, true)
                  }}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      setEditingName(false)
                      pushChainToEngine(chain, true)
                    }
                  }}
                  className="h-5 px-1 text-xs border-0 bg-transparent focus-visible:ring-0"
                />
              ) : (
                <button
                  type="button"
                  onClick={() => setEditingName(true)}
                  className="text-xs font-semibold text-foreground truncate text-left flex-1"
                  title="클릭하여 프리셋 이름 변경"
                >
                  {chain.presetName}
                </button>
              )}
            </div>

            <Button
              size="icon"
              variant="secondary"
              className="h-7 w-7"
              onClick={handleSavePreset}
              title="현재 프리셋 저장"
            >
              <Save className="w-3.5 h-3.5" />
            </Button>
          </div>

          {/* Right: Scene A..H Buttons + Routing Toggle */}
          <div className="flex items-center gap-2">
            <div className="flex items-center gap-1">
              {SCENE_LABELS.map((label, idx) => {
                const isActive = chain.activeScene === idx
                return (
                  <button
                    key={label}
                    type="button"
                    onClick={() => pushChainToEngine(switchSceneOnChain(chain, idx), true, true)}
                    className={`w-7 h-7 rounded-[6px] text-xs font-bold transition-all ${
                      isActive
                        ? "text-background shadow-sm"
                        : "bg-secondary/80 text-muted-foreground hover:text-foreground hover:bg-accent border border-border"
                    }`}
                    style={
                      isActive
                        ? {
                            backgroundColor: "var(--cat-scene)",
                            boxShadow: "0 0 10px rgba(250, 204, 21, 0.35)",
                          }
                        : undefined
                    }
                    title={`Scene ${label}`}
                  >
                    {label}
                  </button>
                )
              })}
            </div>

            <div className="w-px h-5 bg-border mx-0.5" />

            {/* Routing Mode Toggle (Parallel Split vs Serial) */}
            <Button
              size="sm"
              variant={chain.routingMode === 1 ? "default" : "secondary"}
              className="h-7 px-2.5 text-[11px] font-semibold gap-1.5"
              onClick={() =>
                pushChainToEngine(
                  { ...chain, routingMode: chain.routingMode === 1 ? 0 : 1 },
                  true
                )
              }
              title="직렬(Serial) / 병렬 분기(Parallel Split) 라우팅 전환"
            >
              <GitBranch className="w-3.5 h-3.5" />
              {chain.routingMode === 1 ? "Parallel" : "Serial"}
            </Button>
          </div>
        </div>

        {/* ── Center 2x8 Cortex Signal Chain Grid ── */}
        <CortexGrid
          chain={chain}
          selectedSlot={selectedSlot}
          onSelectSlot={handleSelectSlot}
          onToggleBypass={toggleSlotBypass}
          onMoveOrSwap={moveOrSwapSlot}
          onDropModelFromLibrary={assignModelToSlot}
          onUpdateRouting={(patch) => pushChainToEngine({ ...chain, ...patch }, false)}
          meterL={meterL}
          meterR={meterR}
          clip={clip}
        />

        {/* ── Bottom Parameter Inspector Panel (Matches Cortex Control bottom deck) ── */}
        <section className="h-[224px] shrink-0 border-t border-border bg-card/85 flex flex-col">
          {/* Inspector Header Bar */}
          <div className="h-10 shrink-0 px-4 border-b border-border bg-card flex items-center justify-between gap-3">
            {currentModel && currentCategory ? (
              <>
                <div className="flex items-center gap-2.5 min-w-0">
                  <button
                    type="button"
                    onClick={() => toggleSlotBypass(selectedSlot)}
                    className="w-6 h-6 rounded-md flex items-center justify-center border transition-colors"
                    style={{
                      borderColor: currentSlot.enabled ? currentCategory.hex : "var(--border)",
                      backgroundColor: currentSlot.enabled ? `${currentCategory.hex}24` : "transparent",
                      color: currentSlot.enabled ? currentCategory.hex : "var(--muted-foreground)",
                    }}
                    title={currentSlot.enabled ? "블록 활성 (클릭하여 바이패스)" : "바이패스됨 (클릭하여 활성화)"}
                  >
                    <Power className="w-3.5 h-3.5" />
                  </button>
                  <span className="text-xs text-muted-foreground font-medium">{currentModel.categoryLabel}</span>
                  <span className="text-xs font-bold text-foreground truncate">{currentModel.name}</span>
                  {!currentSlot.enabled && (
                    <Badge variant="outline" className="text-[10px] h-5 text-muted-foreground">
                      BYPASSED
                    </Badge>
                  )}
                </div>

                {/* Contextual NAM / Cab IR Controls */}
                <div className="flex items-center gap-2">
                  {currentModel.id === "nam_capture" && (
                    <div className="flex items-center gap-1.5">
                      <Select value={selectedToneId || "__none__"} onValueChange={(v) => v !== "__none__" && handleSelectNamModel(v)}>
                        <SelectTrigger className="h-7 w-44 text-xs">
                          <SelectValue placeholder=".nam 모델 선택" />
                        </SelectTrigger>
                        <SelectContent>
                          {tones.length === 0 ? (
                            <SelectItem value="__none__">불러온 .nam 없음</SelectItem>
                          ) : (
                            tones.map((t) => (
                              <SelectItem key={t.id} value={t.id}>
                                {t.name}
                              </SelectItem>
                            ))
                          )}
                        </SelectContent>
                      </Select>
                      <Button size="sm" variant="secondary" className="h-7 text-[11px] px-2" onClick={handleImportNam}>
                        <Upload className="w-3 h-3 mr-1" />
                        .nam
                      </Button>
                    </div>
                  )}

                  {(currentModel.id === "cab_custom" || currentModel.id === "cab_v30") && (
                    <div className="flex items-center gap-1.5">
                      {customCabIrName && (
                        <span className="text-[11px] font-mono text-muted-foreground max-w-[140px] truncate">
                          {customCabIrName}
                        </span>
                      )}
                      <Button size="sm" variant="secondary" className="h-7 text-[11px] px-2" onClick={handleLoadCabIr}>
                        <FolderOpen className="w-3 h-3 mr-1" />
                        WAV IR
                      </Button>
                    </div>
                  )}

                  <div className="flex items-center gap-0.5 ml-1">
                    <Button
                      size="icon"
                      variant="ghost"
                      className="h-7 w-7"
                      disabled={selectedSlot === 0}
                      onClick={() => moveOrSwapSlot(selectedSlot, selectedSlot - 1)}
                      title="왼쪽으로 이동"
                    >
                      <ArrowLeft className="w-3.5 h-3.5" />
                    </Button>
                    <Button
                      size="icon"
                      variant="ghost"
                      className="h-7 w-7"
                      disabled={selectedSlot === 15}
                      onClick={() => moveOrSwapSlot(selectedSlot, selectedSlot + 1)}
                      title="오른쪽으로 이동"
                    >
                      <ArrowRight className="w-3.5 h-3.5" />
                    </Button>
                    <Button
                      size="icon"
                      variant="ghost"
                      className="h-7 w-7"
                      onClick={() => resetSlotDefaults(selectedSlot)}
                      title="기본값으로 복원"
                    >
                      <RotateCcw className="w-3.5 h-3.5" />
                    </Button>
                    <Button
                      size="icon"
                      variant="ghost"
                      className="h-7 w-7 text-muted-foreground hover:text-destructive"
                      onClick={() => removeSlotBlock(selectedSlot)}
                      title="체인에서 블록 제거"
                    >
                      <Trash2 className="w-3.5 h-3.5" />
                    </Button>
                  </div>
                </div>
              </>
            ) : (
              <div className="flex items-center justify-between w-full">
                <span className="text-xs font-medium text-muted-foreground">
                  빈 슬롯 #{selectedSlot + 1} — 왼쪽 라이브러리에서 페달/앰프를 선택하거나 아래 빠른 추가를 클릭하세요
                </span>
              </div>
            )}
          </div>

          {/* Parameter Cells Grid */}
          <ScrollArea className="flex-1">
            {currentModel && currentCategory ? (
              <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
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
                      onChange={(nextVal) => updateSlotParam(selectedSlot, p.index, nextVal)}
                    />
                  )
                })}
              </div>
            ) : (
              <div className="p-4 flex flex-wrap items-center gap-2">
                {[
                  "od_ts808",
                  "od_klon",
                  "dist_rat",
                  "fuzz_bigmuff",
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
                    <button
                      key={id}
                      type="button"
                      onClick={() => assignModelToSlot(id, selectedSlot)}
                      className="px-3 py-1.5 rounded-md bg-secondary/70 hover:bg-accent border text-xs font-medium flex items-center gap-2 transition-colors"
                      style={{ borderColor: `${c.hex}55` }}
                    >
                      <span className="w-2 h-2 rounded-full" style={{ backgroundColor: c.hex }} />
                      {m.name}
                    </button>
                  )
                })}
              </div>
            )}
          </ScrollArea>
        </section>
      </div>
    </div>
  )
}
