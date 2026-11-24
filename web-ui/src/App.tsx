import { useEffect, useMemo, useRef, useState } from "react"
import {
  BookOpen,
  Bug,
  Cloud,
  Gauge,
  Globe,
  Guitar,
  Music2,
  Package,
  Palette,
  Play,
  Search,
  Shield,
  SlidersHorizontal,
  Square,
  Terminal,
  VolumeX,
  Wrench,
} from "lucide-react"
import { motion } from "motion/react"
import { toast } from "sonner"
import { AdminConsoleDialog } from "@/components/AdminConsoleDialog"
import { AudioMidiSheet } from "@/components/AudioMidiSheet"
import { AuthAccountModal } from "@/components/AuthAccountModal"
import { BenchmarkDialog } from "@/components/BenchmarkDialog"
import { BugReportDialog } from "@/components/BugReportDialog"
import { CommandPalette } from "@/components/CommandPalette"
import { ConsoleLogSheet, type StudioLogEntry } from "@/components/ConsoleLogSheet"
import { CortexStudio } from "@/components/CortexStudio"
import { CustomGearGuideDialog } from "@/components/CustomGearGuideDialog"
import { GuitarAnalyzerDialog } from "@/components/GuitarAnalyzerDialog"
import { OnboardingTutorialModal } from "@/components/OnboardingTutorialModal"
import { OtaUpdateModal } from "@/components/OtaUpdateModal"
import { ToneHubModal } from "@/components/ToneHubModal"
import { TunerDialog } from "@/components/TunerDialog"
import { Button } from "@/components/ui/button"
import { Kbd } from "@/components/ui/kbd"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Separator } from "@/components/ui/separator"
import { Toaster } from "@/components/ui/sonner"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { Hint } from "@/components/ui/tooltip"
import {
  FACTORY_PRESETS,
  commitActiveScene,
  createDefaultParams,
  ensurePresetScenes,
  registerCustomDevices,
  switchSceneOnChain,
  syncScenesOnAssignSlot,
} from "@/data/cortexCatalog"
import { studioBridge } from "@/lib/bridge"
import { useI18n, type StudioLocale } from "@/lib/i18n"
import { STUDIO_THEMES, useStudioTheme } from "@/lib/theme"
import type {
  AsioCapabilitiesDto,
  BackingTrackStateDto,
  CloudStateDto,
  CustomDeviceSpecDto,
  GuitarAnalysisLiveStateDto,
  GuitarProfileDto,
  LooperSnapshotDto,
  MidiSnapshotDto,
  ModelProfileSummary,
  OtaCheckResultDto,
  PresetDef,
  RecorderReampSnapshotDto,
  SignalChainConfigDto,
  StudioInitSnapshot,
  TelemetryFrame,
  TunerSnapshotDto,
  WasapiDeviceDto,
} from "@/types/rig"

export type StudioViewId = "grid" | "directory" | "scope"

export default function App() {
  const { locale, setLocale, tx } = useI18n()
  const { themeId, theme, setThemeId } = useStudioTheme()

  const [activeView, setActiveView] = useState<StudioViewId>("grid")
  const [logsOpen, setLogsOpen] = useState(false)
  const [audioDialogOpen, setAudioDialogOpen] = useState(false)
  const [guitarDialogOpen, setGuitarDialogOpen] = useState(false)
  const [tunerDialogOpen, setTunerDialogOpen] = useState(false)
  const [toneHubOpen, setToneHubOpen] = useState(false)
  const [benchOpen, setBenchOpen] = useState(false)
  const [tutorialOpen, setTutorialOpen] = useState(false)
  const [gearGuideOpen, setGearGuideOpen] = useState(false)
  const [authOpen, setAuthOpen] = useState(false)
  const [bugOpen, setBugOpen] = useState(false)
  const [otaOpen, setOtaOpen] = useState(false)
  const [otaInfo, setOtaInfo] = useState<OtaCheckResultDto | null>(null)
  const [adminOpen, setAdminOpen] = useState(false)
  const [appVersion, setAppVersion] = useState("3.0.0")
  const [hasCompletedTutorial, setHasCompletedTutorial] = useState(false)
  const [cloudState, setCloudState] = useState<CloudStateDto>({
    mode: "unselected",
    isConfigured: true,
    isAuthenticated: false,
    user: null,
    lastSyncUtc: null,
    pendingBugReportsCount: 0,
  })
  const [customDevices, setCustomDevices] = useState<CustomDeviceSpecDto[]>([])
  const [commandOpen, setCommandOpen] = useState(false)
  const [fileDropActive, setFileDropActive] = useState(false)

  const [tunerMute, setTunerMute] = useState(false)
  const [tunerRefA4Hz, setTunerRefA4Hz] = useState(440)
  const [logs, setLogs] = useState<StudioLogEntry[]>([])
  const [guitarProfiles, setGuitarProfiles] = useState<GuitarProfileDto[]>([])
  const [activeGuitarProfileId, setActiveGuitarProfileId] = useState("")
  const [guitarAnalysis, setGuitarAnalysis] = useState<GuitarAnalysisLiveStateDto | null>(null)

  const [asioDrivers, setAsioDrivers] = useState<string[]>([])
  const [wasapiDevices, setWasapiDevices] = useState<WasapiDeviceDto[]>([
    { id: "", label: "System Default Output" },
  ])
  const [wasapiInputDevices, setWasapiInputDevices] = useState<WasapiDeviceDto[]>([
    { id: "", label: "System Default Input (Auto)" },
  ])
  const [presets, setPresets] = useState<PresetDef[]>([
    { index: 0, name: "극한 (링 4ms / WASAPI 8~12ms)", ringMs: 4, sharedMs: 12, exclMs: 8 },
    { index: 1, name: "최소 (링 8ms / WASAPI 10~15ms)", ringMs: 8, sharedMs: 15, exclMs: 10 },
    { index: 2, name: "균형 (링 16ms / WASAPI 15~25ms)", ringMs: 16, sharedMs: 25, exclMs: 15 },
    { index: 3, name: "안정 (링 32ms / WASAPI 25~50ms)", ringMs: 32, sharedMs: 50, exclMs: 25 },
  ])

  const [inputKind, setInputKind] = useState<"ASIO" | "WASAPI">("ASIO")
  const [inputDriver, setInputDriver] = useState("")
  const [wasapiInputDeviceId, setWasapiInputDeviceId] = useState("")
  const [wasapiInputExclusive, setWasapiInputExclusive] = useState(false)
  const [wasapiInputRaw, setWasapiInputRaw] = useState(true)
  const [channelMode, setChannelMode] = useState(1)
  const [inputChannelOffset, setInputChannelOffset] = useState(0)
  const [bufferSize, setBufferSize] = useState(0)
  const [outputKind, setOutputKind] = useState("WASAPI")
  const [wasapiExclusive, setWasapiExclusive] = useState(false)
  const [wasapiDeviceId, setWasapiDeviceId] = useState("")
  const [asioOutDriver, setAsioOutDriver] = useState("")
  const [presetIndex, setPresetIndex] = useState(1)
  const [caps, setCaps] = useState<AsioCapabilitiesDto | null>(null)

  const [tones, setTones] = useState<ModelProfileSummary[]>([])
  const [selectedToneId, setSelectedToneId] = useState("")
  const [catalogVersion, setCatalogVersion] = useState(0)
  const [customCabIrName, setCustomCabIrName] = useState("")

  const [chain, setChain] = useState<SignalChainConfigDto>(() =>
    ensurePresetScenes(FACTORY_PRESETS[0])
  )
  const [savedPresets, setSavedPresets] = useState<SignalChainConfigDto[]>([])
  const [selectedSlot, setSelectedSlot] = useState<number>(2)

  const [backingInitState, setBackingInitState] = useState<BackingTrackStateDto | undefined>(
    undefined
  )
  const [looperInitState, setLooperInitState] = useState<LooperSnapshotDto | undefined>(undefined)
  const [recorderInitState, setRecorderInitState] = useState<
    RecorderReampSnapshotDto | undefined
  >(undefined)
  const [midiState, setMidiState] = useState<MidiSnapshotDto>({
    devices: [],
    activeDevice: "",
    connected: false,
    bindings: [],
    learning: null,
  })

  const [isAudioRunning, setIsAudioRunning] = useState(false)
  const [isAudioBusy, setIsAudioBusy] = useState(false)
  const [audioStatus, setAudioStatus] = useState("STANDBY")
  const [headerLatencyMs, setHeaderLatencyMs] = useState<number | null>(null)
  const [tuner, setTuner] = useState<TunerSnapshotDto>({
    active: false,
    noteName: "--",
    octave: 0,
    cents: 0,
    freqHz: 0,
  })

  const syncTimerRef = useRef<number | null>(null)

  const addLog = (text: string, level: "info" | "success" | "error" = "info") => {
    const line = text.startsWith("[") ? text : `[${new Date().toLocaleTimeString()}] ${text}`
    setLogs((prev) =>
      [{ id: Date.now() + Math.random(), text: line, level }, ...prev].slice(0, 140)
    )
    if (level === "success") toast.success(text)
    else if (level === "error") toast.error(text)
  }

  const pushChainToEngine = (
    next: SignalChainConfigDto,
    immediate = false,
    preserveScenes = false
  ) => {
    const finalized = preserveScenes ? ensurePresetScenes(next) : commitActiveScene(next)
    setChain(finalized)
    if (!studioBridge.isWebView2) return
    if (syncTimerRef.current !== null) {
      window.clearTimeout(syncTimerRef.current)
      syncTimerRef.current = null
    }
    const send = () => {
      studioBridge
        .invoke("updateSignalChain", finalized)
        .catch((e) => addLog(`체인 업데이트 실패: ${String(e)}`, "error"))
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

  const loadPreset = (preset: SignalChainConfigDto) => {
    const clone: SignalChainConfigDto = ensurePresetScenes({
      ...preset,
      slots: preset.slots.map((s) => ({
        ...s,
        params: [...s.params],
        cabMic: s.cabMic ? { ...s.cabMic } : undefined,
      })),
      scenes: preset.scenes?.map((sc) => ({
        parallelMix: sc.parallelMix,
        slots: sc.slots.map((sl) => ({
          enabled: sl.enabled,
          syncDivision: sl.syncDivision,
          params: [...sl.params],
        })),
      })),
    })
    pushChainToEngine(clone, true, true)
    const firstAmp = clone.slots.findIndex(
      (s) =>
        s.modelId.startsWith("amp_") ||
        s.modelId.startsWith("custom_amp_") ||
        s.modelId === "nam_capture"
    )
    if (firstAmp >= 0) setSelectedSlot(firstAmp)
    addLog(`${clone.presetId} · ${clone.presetName}`, "info")
    toast(`${clone.presetId} · ${clone.presetName}`)
  }

  const assignModelToSlot = (modelId: string, slotIdx: number) => {
    const nextSlots = chain.slots.map((s) => ({ ...s, params: [...s.params] }))
    const nextParams = createDefaultParams(modelId)
    nextSlots[slotIdx] = {
      index: slotIdx,
      modelId,
      namModelId: modelId === "nam_capture" ? selectedToneId || null : undefined,
      enabled: true,
      params: nextParams,
    }
    setSelectedSlot(slotIdx)
    pushChainToEngine(
      syncScenesOnAssignSlot({ ...chain, slots: nextSlots }, slotIdx, modelId, nextParams),
      true,
      true
    )
  }

  useEffect(() => {
    const unsubLog = studioBridge.on<{ line: string }>("log", (ev) => {
      if (ev?.line) addLog(ev.line, "info")
    })
    const unsubAudio = studioBridge.on<{ isRunning: boolean; busy: boolean; state: string }>(
      "audioState",
      (ev) => {
        setIsAudioRunning(Boolean(ev.isRunning))
        setIsAudioBusy(Boolean(ev.busy))
        if (ev.state) setAudioStatus(ev.state)
      }
    )
    const unsubTones = studioBridge.on<{ tones: ModelProfileSummary[]; selectedToneId: string }>(
      "tonesChanged",
      (ev) => {
        setTones(ev.tones || [])
        setSelectedToneId(ev.selectedToneId || "")
      }
    )
    const unsubCabIr = studioBridge.on<{ customCabIrName?: string }>("cabIrChanged", (ev) => {
      if (ev?.customCabIrName !== undefined) setCustomCabIrName(ev.customCabIrName)
    })
    const unsubGuitars = studioBridge.on<{
      guitarProfiles: GuitarProfileDto[]
      activeGuitarProfileId: string
    }>("guitarProfilesChanged", (ev) => {
      setGuitarProfiles(ev.guitarProfiles || [])
      setActiveGuitarProfileId(ev.activeGuitarProfileId || "")
    })
    const unsubMidi = studioBridge.on<MidiSnapshotDto>("midiStateChanged", (ev) => {
      if (ev) setMidiState(ev)
    })
    const unsubTunerMute = studioBridge.on<{ mute: boolean }>("tunerMuteChanged", (ev) => {
      if (ev !== undefined) setTunerMute(Boolean(ev.mute))
    })
    const unsubCustomDevices = studioBridge.on<{
      customDevices?: CustomDeviceSpecDto[]
      addedDevice?: CustomDeviceSpecDto
    }>("customDevicesChanged", (ev) => {
      if (ev?.customDevices) {
        setCustomDevices(ev.customDevices)
        registerCustomDevices(ev.customDevices)
        setCatalogVersion((v) => v + 1)
      }
      if (ev?.addedDevice?.name) {
        addLog(`[MCP] ${ev.addedDevice.name} loaded.`, "success")
      }
    })
    const unsubCloud = studioBridge.on<CloudStateDto>("cloudStateChanged", (st) => {
      if (st) setCloudState(st)
    })
    const unsubChain = studioBridge.on<{
      signalChain?: SignalChainConfigDto
      savedPresets?: SignalChainConfigDto[]
      summary?: string
    }>("signalChainChanged", (ev) => {
      if (ev?.signalChain) {
        const nextChain = ensurePresetScenes(ev.signalChain)
        setChain(nextChain)
        const firstAmp = nextChain.slots.findIndex(
          (s) =>
            s.modelId.startsWith("amp_") ||
            s.modelId.startsWith("custom_amp_") ||
            s.modelId === "nam_capture"
        )
        if (firstAmp >= 0) setSelectedSlot(firstAmp)
        addLog(ev.summary ? `[MCP] ${ev.summary}` : "[MCP] Signal chain updated.", "success")
      }
      if (ev?.savedPresets) setSavedPresets(ev.savedPresets)
    })

    let lastHdrSync = 0
    let lastGuitarSync = 0
    let lastTunerSync = 0
    const unsubTel = studioBridge.on<TelemetryFrame>("telemetry", (f) => {
      const now = performance.now()
      if (f.guitarAnalysis !== undefined && now - lastGuitarSync > 35) {
        lastGuitarSync = now
        setGuitarAnalysis(f.guitarAnalysis ?? null)
      }
      if (now - lastTunerSync > 40) {
        lastTunerSync = now
        if (f.vis?.tuner) {
          setTuner(f.vis.tuner)
        } else if (!f.running) {
          setTuner({
            active: false,
            noteName: "--",
            octave: 0,
            cents: 0,
            freqHz: 0,
            clarity: 0,
            levelDb: -96,
          })
        }
      }
      if (now - lastHdrSync > 120) {
        lastHdrSync = now
        setIsAudioRunning(f.running)
        setIsAudioBusy(f.busy)
        setHeaderLatencyMs(f.running && f.latency ? f.latency.totalMs : null)
      }
    })

    if (studioBridge.isWebView2) {
      studioBridge
        .invoke<StudioInitSnapshot>("init")
        .then((snap) => {
          const loadedAsio = snap.audio.asioDrivers || []
          setAsioDrivers(loadedAsio)
          setWasapiDevices(snap.audio.wasapiDevices || [{ id: "", label: "System Default Output" }])
          setWasapiInputDevices(
            snap.audio.wasapiInputDevices || [{ id: "", label: "System Default Input (Auto)" }]
          )
          if (snap.presets?.length) setPresets(snap.presets)
          if (snap.audio.initialCaps) setCaps(snap.audio.initialCaps)

          const s = snap.settings
          const resolvedInKind =
            s.inputKind?.toUpperCase() === "WASAPI" || loadedAsio.length === 0 ? "WASAPI" : "ASIO"
          setInputKind(resolvedInKind)
          setInputDriver(snap.audio.initialDriver || s.inputDriver || "")
          setWasapiInputDeviceId(s.wasapiInputDeviceId || "")
          setWasapiInputExclusive(Boolean(s.wasapiInputExclusive))
          setWasapiInputRaw(s.wasapiInputRaw ?? true)
          if (s.channelMode === 2) {
            setChannelMode(1)
            setInputChannelOffset(1)
          } else {
            setChannelMode(s.channelMode ?? 1)
            setInputChannelOffset(s.inputChannelOffset ?? 0)
          }
          setBufferSize(s.asioInBufferSize ?? 0)
          setOutputKind(s.outputKind || "WASAPI")
          setWasapiExclusive(Boolean(s.wasapiExclusive))
          setWasapiDeviceId(s.wasapiDeviceId || "")
          setAsioOutDriver(s.asioOutDriver || "")
          setPresetIndex(s.presetIndex ?? 1)

          setTones(snap.tones || [])
          setSelectedToneId(snap.selectedToneId || "")
          setCustomCabIrName(snap.customCabIrName || "")
          if (snap.customDevices) {
            setCustomDevices(snap.customDevices)
            registerCustomDevices(snap.customDevices)
            setCatalogVersion((v) => v + 1)
          }
          if (snap.appVersion) setAppVersion(snap.appVersion)
          if (snap.cloudState) setCloudState(snap.cloudState)
          const tutDone = Boolean(s.hasCompletedTutorial)
          setHasCompletedTutorial(tutDone)
          if (!s.hasChosenAuthMode && snap.cloudState?.mode === "unselected") {
            setAuthOpen(true)
          } else if (!tutDone) {
            setTutorialOpen(true)
          }
          studioBridge
            .invoke<OtaCheckResultDto>("checkOtaUpdate")
            .then((rel) => {
              if (rel) {
                setOtaInfo(rel)
                if (rel.updateAvailable) setOtaOpen(true)
              }
            })
            .catch(() => {})
          if (snap.signalChain) setChain(ensurePresetScenes(snap.signalChain))
          if (snap.savedPresets) setSavedPresets(snap.savedPresets)
          setGuitarProfiles(snap.guitarProfiles || [])
          setActiveGuitarProfileId(snap.activeGuitarProfileId || "")
          setTunerMute(Boolean(snap.tunerMute))
          if (snap.tunerRefA4Hz) setTunerRefA4Hz(snap.tunerRefA4Hz)
          if (snap.backing) setBackingInitState(snap.backing)
          if (snap.looper) setLooperInitState(snap.looper)
          if (snap.recorder) setRecorderInitState(snap.recorder)
          if (snap.midi) setMidiState(snap.midi)
          setIsAudioRunning(snap.isAudioRunning)
        })
        .catch((e) => addLog(`Init error: ${String(e)}`, "error"))
    }

    return () => {
      unsubLog()
      unsubAudio()
      unsubTones()
      unsubCabIr()
      unsubGuitars()
      unsubMidi()
      unsubTunerMute()
      unsubCustomDevices()
      unsubCloud()
      unsubChain()
      unsubTel()
    }
  }, [])

  const handleManualCheckUpdates = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<OtaCheckResultDto>("checkOtaUpdate")
      setOtaInfo(res)
      if (!res.updateAvailable) {
        toast.success(tx(`현재 최신 버전(v${res.currentVersion})입니다.`, `You are running the latest version (v${res.currentVersion}).`))
      }
      setOtaOpen(true)
    } catch (e) {
      toast.error(String(e))
    }
  }

  // Global Command Palette Shortcuts: '/' or 'Ctrl+K' / 'Cmd+K'
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName?.toLowerCase()
      const isInput =
        tag === "input" ||
        tag === "textarea" ||
        (e.target as HTMLElement | null)?.isContentEditable
      if ((e.key === "k" || e.key === "K") && (e.ctrlKey || e.metaKey)) {
        e.preventDefault()
        setCommandOpen((o) => !o)
        return
      }
      if (!isInput && e.key === "/" && !e.ctrlKey && !e.metaKey && !e.altKey) {
        e.preventDefault()
        setCommandOpen(true)
      }
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [])

  const probeAsioCaps = async (driver: string) => {
    if (!studioBridge.isWebView2 || !driver) return
    try {
      const res = await studioBridge.invoke<AsioCapabilitiesDto>("getAsioCaps", { driver })
      setCaps(res)
    } catch {
      // ignore
    }
  }

  const handleRefreshDevices = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{
        asioDrivers: string[]
        wasapiDevices: WasapiDeviceDto[]
        wasapiInputDevices?: WasapiDeviceDto[]
        initialDriver?: string
        initialCaps?: AsioCapabilitiesDto | null
      }>("refreshAudioDevices")
      const drivers = res.asioDrivers || []
      setAsioDrivers(drivers)
      setWasapiDevices(res.wasapiDevices || [])
      if (res.wasapiInputDevices) setWasapiInputDevices(res.wasapiInputDevices)

      const preferredPro = drivers.find((d) => !/realtek/i.test(d))
      let activeDriver = inputDriver
      if (
        !activeDriver ||
        !drivers.includes(activeDriver) ||
        (/realtek/i.test(activeDriver) && preferredPro)
      ) {
        activeDriver = preferredPro || res.initialDriver || drivers[0] || ""
        setInputDriver(activeDriver)
      }
      if (!isAudioRunning && activeDriver) {
        if (res.initialCaps && res.initialCaps.driverName === activeDriver) {
          setCaps(res.initialCaps)
        } else {
          await probeAsioCaps(activeDriver)
        }
      }
    } catch {
      // ignore
    }
  }

  useEffect(() => {
    if (audioDialogOpen && studioBridge.isWebView2) {
      if (!isAudioRunning) void handleRefreshDevices()
      studioBridge
        .invoke<MidiSnapshotDto>("refreshMidiDevices")
        .then((m) => m && setMidiState(m))
        .catch(() => {})
    }
  }, [audioDialogOpen])

  const handleSelectMidiDevice = async (deviceName: string) => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<MidiSnapshotDto>("selectMidiDevice", { deviceName })
      if (res) setMidiState(res)
    } catch (e) {
      addLog(tx(`MIDI 연결 오류: ${String(e)}`, `MIDI connect error: ${String(e)}`), "error")
    }
  }

  const handleStartMidiLearn = async (req: {
    targetKind: string
    slotIndex?: number
    paramIndex?: number
    sceneIndex?: number
  }) => {
    if (!studioBridge.isWebView2) {
      toast.info(tx("WebView2 오디오 호스트에서 MIDI 학습이 동작합니다.", "MIDI Learn runs inside the desktop host."))
      return
    }
    try {
      const res = await studioBridge.invoke<MidiSnapshotDto>("startMidiLearn", req)
      if (res) setMidiState(res)
      toast.info(tx("MIDI 컨트롤러 페달이나 노브를 움직여 주세요…", "Move a MIDI pedal or knob to bind…"))
    } catch (e) {
      addLog(tx(`MIDI Learn 실패: ${String(e)}`, `MIDI Learn failed: ${String(e)}`), "error")
    }
  }

  const handleCancelMidiLearn = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<MidiSnapshotDto>("cancelMidiLearn")
      if (res) setMidiState(res)
    } catch {
      // ignore
    }
  }

  const handleClearMidiBinding = async (req: {
    targetKind: string
    slotIndex?: number
    paramIndex?: number
    sceneIndex?: number
  }) => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<MidiSnapshotDto>("clearMidiBinding", req)
      if (res) setMidiState(res)
      toast.success(tx("MIDI 매핑 해제됨", "MIDI binding cleared"))
    } catch {
      // ignore
    }
  }

  const handleQuickStartAutoAudio = async (mode: "WASAPI" | "ASIO_DUPLEX") => {
    if (!studioBridge.isWebView2 || isAudioBusy) return
    const preferredDriver =
      (inputDriver && asioDrivers.includes(inputDriver) ? inputDriver : null) ||
      asioDrivers.find((d) => !/realtek/i.test(d)) ||
      asioDrivers[0]

    if (!preferredDriver) {
      await handleQuickStartUniversalWasapi()
      return
    }

    setInputKind("ASIO")
    setInputDriver(preferredDriver)
    setOutputKind(mode)
    setPresetIndex(1)
    try {
      if (isAudioRunning) {
        await studioBridge.invoke("stopMonitoring")
      }
      const res = await studioBridge.invoke<{
        isRunning: boolean
        inputKind?: string
        inputDriver?: string
        outputKind?: string
      }>("startMonitoring", {
        inputKind: "ASIO",
        inputDriver: preferredDriver,
        wasapiInputDeviceId,
        wasapiInputExclusive,
        wasapiInputRaw,
        channelMode,
        inputChannelOffset,
        asioInBufferSize: bufferSize,
        outputKind: mode,
        wasapiExclusive,
        wasapiDeviceId,
        asioOutDriver,
        presetIndex: 1,
      })
      if (res?.inputDriver) setInputDriver(res.inputDriver)
      if (res?.inputKind === "WASAPI" || res?.inputKind === "ASIO") setInputKind(res.inputKind)
      if (res?.outputKind) setOutputKind(res.outputKind)
      setAudioDialogOpen(false)
      addLog(tx(`ASIO 오디오 연결 완료 (${preferredDriver})`, `ASIO active (${preferredDriver})`), "success")
    } catch (e) {
      addLog(tx(`ASIO 연결 실패: ${String(e)}`, `ASIO connect failed: ${String(e)}`), "error")
      setAudioDialogOpen(true)
    }
  }

  const handleQuickStartUniversalWasapi = async () => {
    if (!studioBridge.isWebView2 || isAudioBusy) return
    setInputKind("WASAPI")
    setChannelMode(1)
    setInputChannelOffset(0)
    const safeOut = outputKind === "ASIO_DUPLEX" ? "WASAPI" : outputKind
    setOutputKind(safeOut)
    try {
      if (isAudioRunning) {
        await studioBridge.invoke("stopMonitoring")
      }
      const res = await studioBridge.invoke<{
        isRunning: boolean
        inputKind?: string
        outputKind?: string
      }>("startMonitoring", {
        inputKind: "WASAPI",
        inputDriver,
        wasapiInputDeviceId,
        wasapiInputExclusive,
        wasapiInputRaw,
        channelMode: 1,
        inputChannelOffset: 0,
        asioInBufferSize: bufferSize,
        outputKind: safeOut,
        wasapiExclusive,
        wasapiDeviceId,
        asioOutDriver,
        presetIndex,
      })
      if (res?.outputKind) setOutputKind(res.outputKind)
      setAudioDialogOpen(false)
      addLog(tx("WASAPI 범용 입력 연결 완료", "Universal WASAPI connected"), "success")
    } catch (e) {
      addLog(tx(`WASAPI 시작 실패: ${String(e)}`, `WASAPI start failed: ${String(e)}`), "error")
      setAudioDialogOpen(true)
    }
  }

  const handleToggleAudio = async () => {
    if (!studioBridge.isWebView2) {
      toast.info(tx("웹 프리뷰 모드에서는 시뮬레이션 상태로 표시됩니다.", "Running in browser UI preview mode."))
      return
    }
    if (isAudioRunning) {
      try {
        await studioBridge.invoke("stopMonitoring")
      } catch (e) {
        addLog(tx(`중지 오류: ${String(e)}`, `Stop error: ${String(e)}`), "error")
      }
      return
    }
    const effectiveInKind =
      inputKind === "ASIO" && !inputDriver && asioDrivers.length === 0 ? "WASAPI" : inputKind
    if (effectiveInKind === "ASIO" && !inputDriver) {
      setAudioDialogOpen(true)
      return
    }
    try {
      const res = await studioBridge.invoke<{
        isRunning: boolean
        inputKind?: string
        inputDriver?: string
        outputKind?: string
      }>("startMonitoring", {
        inputKind: effectiveInKind,
        inputDriver,
        wasapiInputDeviceId,
        wasapiInputExclusive,
        wasapiInputRaw,
        channelMode,
        inputChannelOffset,
        asioInBufferSize: bufferSize,
        outputKind:
          effectiveInKind === "WASAPI" && outputKind === "ASIO_DUPLEX" ? "WASAPI" : outputKind,
        wasapiExclusive,
        wasapiDeviceId,
        asioOutDriver,
        presetIndex,
      })
      if (res?.inputKind === "WASAPI" || res?.inputKind === "ASIO") setInputKind(res.inputKind)
      if (res?.inputDriver) setInputDriver(res.inputDriver)
      if (res?.outputKind) setOutputKind(res.outputKind)
    } catch (e) {
      addLog(tx(`오디오 시작 실패: ${String(e)}`, `Audio start failed: ${String(e)}`), "error")
      setAudioDialogOpen(true)
    }
  }

  const handleOpenAsioPanel = async () => {
    if (!studioBridge.isWebView2 || !inputDriver) return
    try {
      await studioBridge.invoke("openAsioPanel", { driver: inputDriver })
    } catch (e) {
      addLog(tx(`ASIO 제어판 오류: ${String(e)}`, `ASIO CPL error: ${String(e)}`), "error")
    }
  }

  const handleTunerConfigChange = async (opts: { mute?: boolean; refA4Hz?: number }) => {
    if (opts.mute !== undefined) setTunerMute(opts.mute)
    if (opts.refA4Hz !== undefined) setTunerRefA4Hz(opts.refA4Hz)
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ mute: boolean; refA4Hz: number }>(
        "setTunerConfig",
        opts
      )
      if (res) {
        setTunerMute(Boolean(res.mute))
        setTunerRefA4Hz(res.refA4Hz || 440)
      }
    } catch (e) {
      addLog(tx(`튜너 설정 실패: ${String(e)}`, `Tuner config error: ${String(e)}`), "error")
    }
  }

  const handleGlobalFileDrop = async (files: FileList | null) => {
    setFileDropActive(false)
    if (!files || files.length === 0 || !studioBridge.isWebView2) return
    for (let i = 0; i < files.length; i++) {
      const f = files[i]
      if (!/\.(nrig|nam|wav|json)$/i.test(f.name)) {
        addLog(
          tx(
            `지원하지 않는 드롭 형식: ${f.name} (.nrig, .nam, .wav)`,
            `Unsupported file drop: ${f.name} (.nrig, .nam, .wav)`
          ),
          "error"
        )
        continue
      }
      try {
        const buf = await f.arrayBuffer()
        const bytes = new Uint8Array(buf)
        let binary = ""
        const chunk = 0x8000
        for (let off = 0; off < bytes.length; off += chunk) {
          binary += String.fromCharCode(
            ...bytes.subarray(off, Math.min(off + chunk, bytes.length))
          )
        }
        const base64Data = btoa(binary)
        const res = await studioBridge.invoke<{ summary?: string }>("importDroppedFile", {
          fileName: f.name,
          base64Data,
        })
        addLog(res?.summary || `${f.name} loaded`, "success")
      } catch (e) {
        addLog(`${f.name}: ${String(e)}`, "error")
      }
    }
  }

  const activeGuitar = guitarProfiles.find((g) => g.id === activeGuitarProfileId)
  const inTuneHeader = tuner.active && Math.abs(tuner.cents) <= 3.5

  return (
    <div
      className="relative h-screen w-screen overflow-hidden bg-background text-foreground flex flex-col"
      onDragOver={(e) => {
        if (e.dataTransfer?.types?.includes("Files")) {
          e.preventDefault()
          if (!fileDropActive) setFileDropActive(true)
        }
      }}
      onDragLeave={(e) => {
        if (e.currentTarget === e.target) setFileDropActive(false)
      }}
      onDrop={(e) => {
        if (e.dataTransfer?.files?.length) {
          e.preventDefault()
          void handleGlobalFileDrop(e.dataTransfer.files)
        } else {
          setFileDropActive(false)
        }
      }}
    >
      <Toaster position="bottom-right" />

      {/* Architectural Full-Bleed Drop Overlay */}
      {fileDropActive && (
        <div className="pointer-events-none fixed inset-0 z-50 bg-oled/90 backdrop-blur-xs flex items-center justify-center p-8">
          <div className="w-full max-w-lg rounded-md border-2 border-dashed border-primary bg-oled-elevated p-8 text-center space-y-2">
            <Package className="size-10 text-primary mx-auto" strokeWidth={1.5} />
            <div className="font-display text-sm font-bold tracking-wider uppercase text-oled-foreground">
              {tx("DROP TO MOUNT // 즉시 파일 로드", "DROP TO MOUNT // INSTANT LOAD")}
            </div>
            <div className="font-mono text-xs text-oled-muted">
              .NRIG BUNDLE · .NAM NEURAL CAPTURE · .WAV CABINET IR
            </div>
          </div>
        </div>
      )}

      {/* ── ZONE 1: TOP INSTRUMENT CROWN (Single 48px Hardware Bezel) ── */}
      <header className="h-12 shrink-0 border-b border-border bg-card px-3.5 flex items-center justify-between gap-3">
        {/* Left: NR//01 Brand Mark + 3-Workspace Navigation + Command Palette Trigger */}
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2">
            <span className="size-2.5 rounded-[1px] bg-primary shrink-0" />
            <span className="font-display text-sm font-bold tracking-tight text-foreground">
              NR//01
            </span>
          </div>

          <Separator orientation="vertical" />

          <nav className="flex items-center gap-0.5 rounded-sm border border-border bg-secondary p-0.5">
            {(
              [
                { id: "grid", label: "RIG" },
                { id: "directory", label: "VAULT" },
                { id: "scope", label: "SCOPE" },
              ] as const
            ).map((tab) => {
              const active = activeView === tab.id
              return (
                <button
                  key={tab.id}
                  type="button"
                  onClick={() => setActiveView(tab.id)}
                  className={`relative px-3 py-1 rounded-[2px] font-mono text-[11px] font-bold tracking-wider transition-colors cursor-pointer ${
                    active
                      ? "text-background"
                      : "text-muted-foreground hover:text-foreground"
                  }`}
                >
                  {active && (
                    <motion.span
                      layoutId="crown-nav-pill"
                      transition={{ type: "spring", stiffness: 520, damping: 40 }}
                      className="absolute inset-0 rounded-[2px] bg-foreground"
                    />
                  )}
                  <span className="relative z-10">{tab.label}</span>
                </button>
              )
            })}
          </nav>

          <Hint
            content={tx("장비·프리셋·명령어 즉시 검색", "Quick Command Palette")}
            kbd="/"
          >
            <Button
              size="xs"
              variant="outline"
              onClick={() => setCommandOpen(true)}
            >
              <Search className="size-3" strokeWidth={1.5} />
              <span className="hidden md:inline font-mono">{tx("검색", "SEARCH")}</span>
              <Kbd>/</Kbd>
            </Button>
          </Hint>
        </div>

        {/* Right: MIDI Learn Banner · Tuner OLED · Guitar · Hub · Theme/Lang · Engine · Patchbay · Logs */}
        <div className="flex items-center gap-1.5">
          {midiState.learning && (
            <Hint content={tx("클릭해 MIDI Learn 대기 취소", "Cancel MIDI Learn")}>
              <Button
                size="xs"
                variant="signal"
                onClick={handleCancelMidiLearn}
              >
                MIDI LEARN…
              </Button>
            </Hint>
          )}

          {/* Recessed OLED Inline Chromatic Tuner */}
          <Hint
            content={
              tuner.active
                ? `${tuner.freqHz.toFixed(1)} Hz (${tuner.cents > 0 ? "+" : ""}${tuner.cents.toFixed(1)} ct) — ${tx("정밀 튜너 열기", "Open Precision Tuner")}`
                : tx("정밀 크로매틱 튜너 열기", "Open Chromatic Tuner")
            }
          >
            <Button
              size="xs"
              variant="oled"
              onClick={() => setTunerDialogOpen(true)}
            >
              {tunerMute ? (
                <VolumeX className="size-3 text-signal-hot" strokeWidth={1.5} />
              ) : (
                <Music2
                  className={
                    inTuneHeader
                      ? "size-3 text-signal-ok"
                      : tuner.active
                        ? "size-3 text-signal-warn"
                        : "size-3 text-oled-muted"
                  }
                  strokeWidth={1.5}
                />
              )}
              <span
                className={`font-bold tabular-nums min-w-[22px] text-center ${
                  inTuneHeader
                    ? "text-signal-ok"
                    : tuner.active
                      ? "text-oled-foreground"
                      : "text-oled-muted"
                }`}
              >
                {tuner.noteName !== "--" ? `${tuner.noteName}${tuner.octave}` : "--"}
              </span>
              {tuner.noteName !== "--" && (
                <span className="text-[10px] tabular-nums text-oled-muted">
                  {tuner.cents > 0 ? `+${tuner.cents.toFixed(0)}` : tuner.cents.toFixed(0)}ct
                </span>
              )}
            </Button>
          </Hint>

          <Hint
            content={
              activeGuitar
                ? `${tx("활성 기타", "Active Guitar")}: ${activeGuitar.name} (${activeGuitar.estimatedPickupType})`
                : tx("기타 DI 음향 분석 및 프로필 관리", "Guitar DI Profiler & Calibration")
            }
          >
            <Button
              size="xs"
              variant={activeGuitar ? "secondary" : "ghost"}
              onClick={() => setGuitarDialogOpen(true)}
            >
              <Guitar className="size-3.5" strokeWidth={1.5} />
              <span className="max-w-[110px] truncate hidden lg:inline">
                {activeGuitar ? activeGuitar.name : tx("기타 프로필", "GUITAR")}
              </span>
            </Button>
          </Hint>

          <Hint
            content={tx(
              "Tone Hub · .nrig 번들 · VST3/CLAP DAW 플러그인",
              "Tone Hub · .nrig Bundles · VST3/CLAP Export"
            )}
          >
            <Button
              size="xs"
              variant="keycap"
              onClick={() => setToneHubOpen(true)}
            >
              <Globe className="size-3" strokeWidth={1.5} />
              <span>HUB · VST3</span>
            </Button>
          </Hint>

          <Hint content={tx("하드웨어 유사도 · 지연시간 · 16슬롯 멀티체인 벤치마크", "Hardware Similarity · Latency · 16-Slot Benchmark")}>
            <Button
              size="xs"
              variant="keycap"
              onClick={() => setBenchOpen(true)}
            >
              <Gauge className="size-3" strokeWidth={1.5} />
              <span>BENCH</span>
            </Button>
          </Hint>

          <Hint content={tx("페달·앰프·캐비넷 생성 & npx MCP 가이드", "Custom Pedal/Amp/Cab Creation & npx MCP Guide")}>
            <Button
              size="xs"
              variant="keycap"
              onClick={() => setGearGuideOpen(true)}
            >
              <Wrench className="size-3" strokeWidth={1.5} />
              <span className="hidden xl:inline">CRAFT</span>
            </Button>
          </Hint>

          <Hint content={tx("스튜디오 튜토리얼 (UI & 기능 가이드)", "Studio UI & Feature Tutorial")}>
            <Button
              size="xs"
              variant="keycap"
              onClick={() => setTutorialOpen(true)}
            >
              <BookOpen className="size-3" strokeWidth={1.5} />
              <span className="hidden xl:inline">GUIDE</span>
            </Button>
          </Hint>

          <Hint content={tx("버그 제보 (자동 익명화 로그 전송)", "Report Bug (Auto-Attached Scrubbed Logs)")}>
            <Button
              size="xs"
              variant="keycap"
              onClick={() => setBugOpen(true)}
            >
              <Bug className="size-3" strokeWidth={1.5} />
              <span className="hidden xl:inline">BUG</span>
            </Button>
          </Hint>

          <Hint
            content={
              cloudState.isAuthenticated && cloudState.user
                ? `${cloudState.user.displayName} (${cloudState.user.email})`
                : tx("Google 클라우드 로그인 · 동기화 · 게스트 설정", "Google Cloud Sign-In & Sync")
            }
          >
            <Button
              size="xs"
              variant={cloudState.isAuthenticated ? "secondary" : "keycap"}
              onClick={() => setAuthOpen(true)}
            >
              <Cloud className="size-3" strokeWidth={1.5} />
              <span className="max-w-[84px] truncate hidden md:inline">
                {cloudState.isAuthenticated && cloudState.user
                  ? cloudState.user.displayName
                  : tx("로그인", "SIGN IN")}
              </span>
            </Button>
          </Hint>

          {cloudState.user?.isAdmin && (
            <Hint content={tx("어드민 운영 애널리틱스 · OTA 정책 콘솔", "Admin Analytics & OTA Console")}>
              <Button
                size="xs"
                variant="keycap"
                onClick={() => setAdminOpen(true)}
              >
                <Shield className="size-3 text-signal-warn" strokeWidth={1.5} />
                <span>ADMIN</span>
              </Button>
            </Hint>
          )}

          {/* Theme Finish & Language Switcher Popover */}
          <Popover>
            <Hint content={tx("섀시 테마 (5종) 및 언어 설정 (KO/EN)", "Chassis Theme & Language (KO/EN)")}>
              <PopoverTrigger asChild>
                <Button size="xs" variant="outline">
                  <span
                    className="size-2.5 rounded-[1px] border border-border"
                    style={{ backgroundColor: theme.swatchBg }}
                  />
                  <span className="font-mono font-bold">{theme.shortName}</span>
                  <Separator orientation="vertical" className="h-3" />
                  <span className="font-mono font-bold">{locale.toUpperCase()}</span>
                </Button>
              </PopoverTrigger>
            </Hint>
            <PopoverContent align="end" className="w-72 space-y-3">
              <div className="space-y-1.5">
                <div className="flex items-center justify-between">
                  <span className="font-mono text-[10px] font-bold uppercase tracking-wider text-muted-foreground flex items-center gap-1.5">
                    <Palette className="size-3" />
                    {tx("섀시 마감 테마", "CHASSIS FINISH")}
                  </span>
                  <span className="font-mono text-[10px] text-muted-foreground">5 FINISHES</span>
                </div>
                <div className="space-y-1">
                  {STUDIO_THEMES.map((t) => {
                    const active = t.id === themeId
                    return (
                      <button
                        key={t.id}
                        type="button"
                        onClick={() => setThemeId(t.id)}
                        className={`w-full flex items-center justify-between px-2.5 py-1.5 rounded-sm border text-left transition-colors cursor-pointer ${
                          active
                            ? "border-primary bg-secondary"
                            : "border-border bg-background hover:bg-accent"
                        }`}
                      >
                        <div className="flex items-center gap-2 min-w-0">
                          <div className="flex items-center rounded-[2px] overflow-hidden border border-border shrink-0">
                            <span className="w-3 h-4" style={{ backgroundColor: t.swatchBg }} />
                            <span className="w-2 h-4" style={{ backgroundColor: t.swatchPrimary }} />
                          </div>
                          <div className="min-w-0">
                            <div className="font-display text-xs font-bold truncate">{t.name}</div>
                            <div className="text-[10px] text-muted-foreground truncate">
                              {locale === "ko" ? t.taglineKo : t.taglineEn}
                            </div>
                          </div>
                        </div>
                        {active && (
                          <span className="font-mono text-[10px] font-bold text-primary shrink-0">
                            ON
                          </span>
                        )}
                      </button>
                    )
                  })}
                </div>
              </div>

              <Separator />

              <div className="flex items-center justify-between">
                <span className="font-mono text-[10px] font-bold uppercase tracking-wider text-muted-foreground">
                  {tx("인터페이스 언어", "INTERFACE LANG")}
                </span>
                <ToggleGroup
                  type="single"
                  size="xs"
                  variant="keycap"
                  value={locale}
                  onValueChange={(v) => {
                    if (v === "ko" || v === "en") setLocale(v as StudioLocale)
                  }}
                >
                  <ToggleGroupItem value="ko">KO</ToggleGroupItem>
                  <ToggleGroupItem value="en">EN</ToggleGroupItem>
                </ToggleGroup>
              </div>
            </PopoverContent>
          </Popover>

          <Hint content={audioStatus}>
            <Button
              size="xs"
              variant={isAudioRunning ? "signal" : "keycap"}
              disabled={isAudioBusy}
              onClick={handleToggleAudio}
            >
              {isAudioRunning ? (
                <Square className="size-2.5 fill-current" />
              ) : (
                <Play className="size-2.5 fill-current" />
              )}
              <span className="tabular-nums">
                {isAudioRunning
                  ? headerLatencyMs !== null
                    ? `LIVE ${headerLatencyMs.toFixed(1)}ms`
                    : "LIVE"
                  : "AUDIO"}
              </span>
            </Button>
          </Hint>

          <Hint content={tx("오디오 I/O & MIDI 패치베이 설정", "Audio I/O & MIDI Patchbay")}>
            <Button
              size="icon-sm"
              variant="ghost"
              onClick={() => setAudioDialogOpen(true)}
            >
              <SlidersHorizontal className="size-3.5" strokeWidth={1.5} />
            </Button>
          </Hint>

          <Hint content={`${tx("시스템 콘솔 로그", "Console Logs")} (${logs.length})`}>
            <Button
              size="icon-sm"
              variant="ghost"
              onClick={() => setLogsOpen(true)}
            >
              <Terminal className="size-3.5" strokeWidth={1.5} />
            </Button>
          </Hint>
        </div>
      </header>

      {/* ── Main Studio Workspace (RIG | VAULT | SCOPE) ── */}
      <CortexStudio
        activeView={activeView}
        catalogVersion={catalogVersion}
        onChangeView={setActiveView}
        chain={chain}
        savedPresets={savedPresets}
        allPresets={allPresets}
        selectedSlot={selectedSlot}
        onSelectSlot={setSelectedSlot}
        onPushChain={pushChainToEngine}
        onLoadPreset={loadPreset}
        onAssignModel={assignModelToSlot}
        onSavedPresetsChange={setSavedPresets}
        tones={tones}
        selectedToneId={selectedToneId}
        customCabIrName={customCabIrName}
        initialBacking={backingInitState}
        initialLooper={looperInitState}
        initialRecorder={recorderInitState}
        isAudioRunning={isAudioRunning}
        isAudioBusy={isAudioBusy}
        guitarProfiles={guitarProfiles}
        activeGuitarProfileId={activeGuitarProfileId}
        liveGuitarAnalysis={guitarAnalysis}
        onLiveGuitarAnalysisChange={setGuitarAnalysis}
        onGuitarProfilesUpdated={(nextProfiles, nextActiveId) => {
          setGuitarProfiles(nextProfiles)
          setActiveGuitarProfileId(nextActiveId)
        }}
        onStartAudio={handleToggleAudio}
        onOpenAudioSettings={() => setAudioDialogOpen(true)}
        onOpenToneHub={() => setToneHubOpen(true)}
        midiState={midiState}
        onStartMidiLearn={handleStartMidiLearn}
        onClearMidiBinding={handleClearMidiBinding}
        onTonesUpdated={(nextTones, nextId) => {
          setTones(nextTones)
          setSelectedToneId(nextId)
        }}
        onSelectToneId={setSelectedToneId}
        onCustomCabIrChange={setCustomCabIrName}
        onLog={addLog}
      />

      {/* ── Global Command Palette ('/' or Ctrl+K) ── */}
      <CommandPalette
        open={commandOpen}
        onOpenChange={setCommandOpen}
        presets={allPresets}
        activePresetId={chain.presetId}
        activeScene={chain.activeScene}
        selectedSlot={selectedSlot}
        isAudioRunning={isAudioRunning}
        onSelectView={setActiveView}
        onLoadPreset={loadPreset}
        onSelectScene={(sceneIdx) =>
          pushChainToEngine(switchSceneOnChain(chain, sceneIdx), true, true)
        }
        onAssignModel={assignModelToSlot}
        onToggleAudio={handleToggleAudio}
        onOpenTuner={() => setTunerDialogOpen(true)}
        onOpenGuitar={() => setGuitarDialogOpen(true)}
        onOpenToneHub={() => setToneHubOpen(true)}
        onOpenBenchmark={() => setBenchOpen(true)}
        onOpenTutorial={() => setTutorialOpen(true)}
        onOpenGearGuide={() => setGearGuideOpen(true)}
        onOpenAuth={() => setAuthOpen(true)}
        onOpenBugReport={() => setBugOpen(true)}
        onOpenUpdates={() => void handleManualCheckUpdates()}
        onOpenAdmin={() => setAdminOpen(true)}
        isAdmin={Boolean(cloudState.user?.isAdmin)}
        onOpenAudioSheet={() => setAudioDialogOpen(true)}
        onOpenLogsSheet={() => setLogsOpen(true)}
      />

      {/* ── Slide-Over Audio I/O & MIDI Patchbay Sheet ── */}
      <AudioMidiSheet
        open={audioDialogOpen}
        onOpenChange={setAudioDialogOpen}
        isAudioRunning={isAudioRunning}
        isAudioBusy={isAudioBusy}
        audioStatus={audioStatus}
        inputKind={inputKind}
        onInputKindChange={setInputKind}
        inputDriver={inputDriver}
        onInputDriverChange={setInputDriver}
        asioDrivers={asioDrivers}
        wasapiInputDevices={wasapiInputDevices}
        wasapiInputDeviceId={wasapiInputDeviceId}
        onWasapiInputDeviceIdChange={setWasapiInputDeviceId}
        wasapiInputExclusive={wasapiInputExclusive}
        onWasapiInputExclusiveChange={setWasapiInputExclusive}
        wasapiInputRaw={wasapiInputRaw}
        onWasapiInputRawChange={setWasapiInputRaw}
        channelMode={channelMode}
        inputChannelOffset={inputChannelOffset}
        onChannelMappingChange={(mode, offset) => {
          setChannelMode(mode)
          setInputChannelOffset(offset)
        }}
        bufferSize={bufferSize}
        onBufferSizeChange={setBufferSize}
        outputKind={outputKind}
        onOutputKindChange={setOutputKind}
        wasapiDevices={wasapiDevices}
        wasapiDeviceId={wasapiDeviceId}
        onWasapiDeviceIdChange={setWasapiDeviceId}
        wasapiExclusive={wasapiExclusive}
        onWasapiExclusiveChange={setWasapiExclusive}
        asioOutDriver={asioOutDriver}
        onAsioOutDriverChange={setAsioOutDriver}
        presetIndex={presetIndex}
        onPresetIndexChange={setPresetIndex}
        presets={presets}
        caps={caps}
        midiState={midiState}
        onProbeAsioCaps={probeAsioCaps}
        onRefreshDevices={handleRefreshDevices}
        onOpenAsioPanel={handleOpenAsioPanel}
        onQuickStartAutoAudio={handleQuickStartAutoAudio}
        onQuickStartUniversalWasapi={handleQuickStartUniversalWasapi}
        onToggleAudio={handleToggleAudio}
        onSelectMidiDevice={handleSelectMidiDevice}
        onStartMidiLearn={handleStartMidiLearn}
        onClearMidiBinding={handleClearMidiBinding}
      />

      {/* ── Slide-Over System Console Sheet ── */}
      <ConsoleLogSheet
        open={logsOpen}
        onOpenChange={setLogsOpen}
        logs={logs}
        onClearLogs={() => setLogs([])}
      />

      {/* ── Precision Strobe Chromatic Tuner Dialog ── */}
      <TunerDialog
        open={tunerDialogOpen}
        onOpenChange={setTunerDialogOpen}
        tuner={tuner}
        isAudioRunning={isAudioRunning}
        isAudioBusy={isAudioBusy}
        tunerMute={tunerMute}
        refA4Hz={tunerRefA4Hz}
        onTunerConfigChange={handleTunerConfigChange}
        onStartAudio={handleToggleAudio}
        onOpenAudioSettings={() => setAudioDialogOpen(true)}
      />

      {/* ── Guitar Analyzer & Profile Manager Dialog ── */}
      <GuitarAnalyzerDialog
        open={guitarDialogOpen}
        onOpenChange={setGuitarDialogOpen}
        isAudioRunning={isAudioRunning}
        isAudioBusy={isAudioBusy}
        onStartAudio={handleToggleAudio}
        onOpenAudioSettings={() => setAudioDialogOpen(true)}
        profiles={guitarProfiles}
        activeProfileId={activeGuitarProfileId}
        liveAnalysis={guitarAnalysis}
        onLiveAnalysisChange={setGuitarAnalysis}
        onProfilesUpdated={(nextProfiles, nextActiveId) => {
          setGuitarProfiles(nextProfiles)
          setActiveGuitarProfileId(nextActiveId)
        }}
        onLog={addLog}
      />

      {/* ── Tone Hub & .nrig Bundle & VST3/CLAP Plugin Center ── */}
      <ToneHubModal
        open={toneHubOpen}
        onOpenChange={setToneHubOpen}
        cloudState={cloudState}
        customDevices={customDevices}
        onRequestLogin={() => setAuthOpen(true)}
        onLog={addLog}
      />

      {/* ── Hardware Similarity / Latency / 16-Slot Multi-Chain Benchmark ── */}
      <BenchmarkDialog
        open={benchOpen}
        onOpenChange={setBenchOpen}
        isAudioRunning={isAudioRunning}
        onLog={addLog}
      />

      {/* ── Interactive Onboarding Tutorial (Replayable) ── */}
      <OnboardingTutorialModal
        open={tutorialOpen}
        onOpenChange={setTutorialOpen}
        onComplete={() => {
          setHasCompletedTutorial(true)
          if (studioBridge.isWebView2) {
            void studioBridge.invoke("updateOnboardingState", { hasCompletedTutorial: true }).catch(() => {})
          }
        }}
        onOpenAudioSheet={() => setAudioDialogOpen(true)}
        onOpenGearGuide={() => setGearGuideOpen(true)}
        onOpenBenchmark={() => setBenchOpen(true)}
      />

      {/* ── Custom Pedal / Amp / Cab Creation & npx MCP Guide ── */}
      <CustomGearGuideDialog
        open={gearGuideOpen}
        onOpenChange={setGearGuideOpen}
        customDevices={customDevices}
        onOpenCommunityHub={() => setToneHubOpen(true)}
      />

      {/* ── Google Cloud Sign-In / Guest Mode / Settings Sync ── */}
      <AuthAccountModal
        open={authOpen}
        onOpenChange={(nextOpen) => {
          setAuthOpen(nextOpen)
          if (!nextOpen && !hasCompletedTutorial) {
            setTutorialOpen(true)
          }
        }}
        cloudState={cloudState}
        onCloudStateChange={(nextState) => {
          setCloudState(nextState)
          if (nextState.mode !== "unselected" && !hasCompletedTutorial) {
            setTutorialOpen(true)
          }
        }}
        onCheckUpdates={() => void handleManualCheckUpdates()}
        onLog={addLog}
      />

      {/* ── Automated Scrubbed-Log Bug Reporter ── */}
      <BugReportDialog
        open={bugOpen}
        onOpenChange={setBugOpen}
        appVersion={appVersion}
        cloudState={cloudState}
        onLog={addLog}
      />

      {/* ── OTA Software Update (Choice or Mandatory Blocking) ── */}
      <OtaUpdateModal
        open={otaOpen}
        onOpenChange={setOtaOpen}
        ota={otaInfo}
        onLog={addLog}
      />

      {/* ── Admin Analytics & OTA Release Console (Admin Only) ── */}
      {cloudState.user?.isAdmin && (
        <AdminConsoleDialog
          open={adminOpen}
          onOpenChange={setAdminOpen}
          onLog={addLog}
        />
      )}
    </div>
  )
}
