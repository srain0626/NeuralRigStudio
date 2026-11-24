import { useEffect, useMemo, useRef, useState } from "react"
import {
  Activity,
  ArrowLeft,
  BookOpen,
  Cloud,
  Gauge,
  Globe,
  Guitar,
  Layers,
  Menu,
  Music2,
  Package,
  Play,
  Search,
  Shield,
  SlidersHorizontal,
  Square,
  Wrench,
} from "lucide-react"
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
import { SponsorPromptModal } from "@/components/SponsorPromptModal"
import { ToneHubModal } from "@/components/ToneHubModal"
import { TunerDialog } from "@/components/TunerDialog"
import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Separator } from "@/components/ui/separator"
import { Toaster } from "@/components/ui/sonner"
import { HelpHint, Hint } from "@/components/ui/tooltip"
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
import { useI18n } from "@/lib/i18n"
import { useStudioTheme } from "@/lib/theme"
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
  const { locale, toggleLocale, tx } = useI18n()
  const { themeId, toggleTheme } = useStudioTheme()
  const [introDone, setIntroDone] = useState(false)
  const [studioMenuOpen, setStudioMenuOpen] = useState(false)

  useEffect(() => {
    const id = window.setTimeout(() => setIntroDone(true), 760)
    return () => window.clearTimeout(id)
  }, [])

  const [activeView, setActiveView] = useState<StudioViewId>("grid")
  const [looperOpen, setLooperOpen] = useState(false)
  const [hasRecordActivity, setHasRecordActivity] = useState(false)
  const [logsOpen, setLogsOpen] = useState(false)
  const [audioDialogOpen, setAudioDialogOpen] = useState(false)
  const [guitarDialogOpen, setGuitarDialogOpen] = useState(false)
  const [tunerDialogOpen, setTunerDialogOpen] = useState(false)
  const [toneHubOpen, setToneHubOpen] = useState(false)
  const [benchOpen, setBenchOpen] = useState(false)
  const [tutorialOpen, setTutorialOpen] = useState(false)
  const [gearGuideOpen, setGearGuideOpen] = useState(false)
  const [authOpen, setAuthOpen] = useState(false)
  const [authDailyPrompt, setAuthDailyPrompt] = useState(false)
  const [sponsorOpen, setSponsorOpen] = useState(false)
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
  const lastGuestPromptDateRef = useRef<string | null>(null)
  const firstSignInUtcRef = useRef<string | null>(null)
  const hasShownSponsorRef = useRef(false)

  const getLocalYmd = () => {
    const d = new Date()
    const y = d.getFullYear()
    const m = String(d.getMonth() + 1).padStart(2, "0")
    const day = String(d.getDate()).padStart(2, "0")
    return `${y}-${m}-${day}`
  }

  const syncPromptRefsFromCloudState = (st?: CloudStateDto | null) => {
    if (!st) return
    if (st.lastGuestLoginPromptDate !== undefined) {
      lastGuestPromptDateRef.current = st.lastGuestLoginPromptDate ?? null
    }
    if (st.firstGoogleSignInUtc !== undefined) {
      firstSignInUtcRef.current = st.firstGoogleSignInUtc ?? null
    }
    if (st.hasShownSponsorPrompt !== undefined) {
      hasShownSponsorRef.current = Boolean(st.hasShownSponsorPrompt)
    }
  }

  const markGuestDailyPromptShown = (todayYmd: string) => {
    lastGuestPromptDateRef.current = todayYmd
    if (studioBridge.isWebView2) {
      void studioBridge
        .invoke("updateOnboardingState", { lastGuestLoginPromptDate: todayYmd })
        .catch(() => {})
    }
  }

  const markSponsorPromptShown = () => {
    hasShownSponsorRef.current = true
    if (studioBridge.isWebView2) {
      void studioBridge
        .invoke("updateOnboardingState", { hasShownSponsorPrompt: true })
        .catch(() => {})
    }
  }

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
      if (st) {
        setCloudState(st)
        syncPromptRefsFromCloudState(st)
      }
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

    const unsubLooperActivity = studioBridge.on<LooperSnapshotDto>("looperStateChanged", (ev) => {
      if (ev) {
        setHasRecordActivity((prev) => Boolean(ev.state !== "empty" || prev))
      }
    })
    const unsubRecorderActivity = studioBridge.on<RecorderReampSnapshotDto>("recorderStateChanged", (ev) => {
      if (ev) {
        setHasRecordActivity(Boolean(ev.recording || ev.reampPlaying))
      }
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
        setHasRecordActivity(
          Boolean(
            f.recorder?.recording ||
              f.recorder?.reampPlaying ||
              f.backing?.playing ||
              (f.looper?.state && f.looper.state !== "empty")
          )
        )
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
          if (snap.cloudState) {
            setCloudState(snap.cloudState)
            syncPromptRefsFromCloudState(snap.cloudState)
          } else {
            lastGuestPromptDateRef.current = s.lastGuestLoginPromptDate ?? null
            firstSignInUtcRef.current = s.firstGoogleSignInUtc ?? null
            hasShownSponsorRef.current = Boolean(s.hasShownSponsorPrompt)
          }
          const tutDone = Boolean(s.hasCompletedTutorial)
          setHasCompletedTutorial(tutDone)
          if (!s.hasChosenAuthMode && snap.cloudState?.mode === "unselected") {
            setAuthDailyPrompt(false)
            setAuthOpen(true)
          } else if (!tutDone) {
            setTutorialOpen(true)
          } else if (snap.cloudState?.shouldShowGuestDailyLoginPrompt) {
            markGuestDailyPromptShown(getLocalYmd())
            setAuthDailyPrompt(true)
            setAuthOpen(true)
          } else if (snap.cloudState?.shouldShowSponsorPrompt) {
            markSponsorPromptShown()
            setSponsorOpen(true)
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
          setHasRecordActivity(
            Boolean(
              snap.recorder?.recording ||
                snap.recorder?.reampPlaying ||
                snap.backing?.playing ||
                (snap.looper?.state && snap.looper.state !== "empty")
            )
          )
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
      unsubLooperActivity()
      unsubRecorderActivity()
      unsubTel()
    }
  }, [])

  useEffect(() => {
    if (!studioBridge.isWebView2) return

    const evaluateActivePrompts = () => {
      const blockingOta = Boolean(otaOpen && otaInfo?.updateAvailable && otaInfo?.forceUpdate)
      if (!hasCompletedTutorial || authOpen || tutorialOpen || sponsorOpen || blockingOta) {
        return
      }

      const today = getLocalYmd()
      if (
        cloudState.mode === "guest" &&
        !cloudState.isAuthenticated &&
        lastGuestPromptDateRef.current !== today
      ) {
        markGuestDailyPromptShown(today)
        setAuthDailyPrompt(true)
        setAuthOpen(true)
        return
      }

      if (
        cloudState.isAuthenticated &&
        !hasShownSponsorRef.current &&
        firstSignInUtcRef.current
      ) {
        const firstMs = Date.parse(firstSignInUtcRef.current)
        if (!Number.isNaN(firstMs) && Date.now() - firstMs >= 7 * 24 * 60 * 60 * 1000) {
          markSponsorPromptShown()
          setSponsorOpen(true)
        }
      }
    }

    window.addEventListener("focus", evaluateActivePrompts)
    const timer = window.setInterval(evaluateActivePrompts, 15 * 60 * 1000)
    return () => {
      window.removeEventListener("focus", evaluateActivePrompts)
      window.clearInterval(timer)
    }
  }, [
    cloudState.mode,
    cloudState.isAuthenticated,
    hasCompletedTutorial,
    authOpen,
    tutorialOpen,
    sponsorOpen,
    otaOpen,
    otaInfo?.updateAvailable,
    otaInfo?.forceUpdate,
  ])

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

      {/* Organic Tangerine-Coral Radial Aura (Matching Landing Page Hero) */}
      <div className="nr-ambient-aura" aria-hidden="true" />

      {/* 0.75s Signature Pulse Opening Veil (Matching Landing Page Act 0) */}
      <div
        aria-hidden="true"
        className={`pointer-events-none fixed inset-0 z-50 bg-background flex items-center justify-center transition-all duration-500 ${
          introDone ? "opacity-0 invisible" : "opacity-100 visible"
        }`}
      >
        <div className="nr-intro-mark inline-flex items-center gap-3 font-sans text-xl font-medium tracking-[-0.03em] text-foreground">
          <svg width={26} height={26} viewBox="0 0 24 24" fill="none">
            <path
              d="M2 12h4l2.8-8 3.6 16 2.8-8H22"
              stroke="currentColor"
              strokeWidth={2.1}
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
          <span>NeuralRig</span>
        </div>
      </div>

      {/* Architectural Full-Bleed Drop Overlay */}
      {fileDropActive && (
        <div className="pointer-events-none fixed inset-0 z-50 bg-oled/90 backdrop-blur-xs flex items-center justify-center p-8">
          <div className="w-full max-w-lg rounded-[12px] border border-dashed border-primary bg-oled-elevated p-8 text-center space-y-2 shadow-2xl">
            <Package className="size-10 text-primary mx-auto" strokeWidth={1.5} />
            <div className="font-display text-base font-light tracking-[-0.02em] text-oled-foreground">
              {tx("여기에 놓아 즉시 장착", "Drop to Mount Immediately")}
            </div>
            <div className="font-mono text-[11px] text-oled-muted">
              .NRIG BUNDLE · .NAM NEURAL CAPTURE · .WAV CABINET IR
            </div>
          </div>
        </div>
      )}

      {/* ── TOP MINIMALIST HEADER (Brand + Conditional Back Pill) ── */}
      <header className="relative z-10 h-12 shrink-0 px-7 pt-2 flex items-center justify-between gap-4">
        {/* Left: Waveform Pulse Brand + Conditional Back-to-Studio Pill */}
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={() => setActiveView("grid")}
            className="inline-flex items-center gap-2.5 text-[16px] font-medium tracking-[-0.03em] text-foreground cursor-pointer"
          >
            <svg width={20} height={20} viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <path
                d="M2 12h4l2.8-7.5 3.6 15 2.8-7.5H22"
                stroke="currentColor"
                strokeWidth={2.05}
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
            <span>NeuralRig</span>
          </button>

          {activeView !== "grid" && (
            <Button
              size="xs"
              variant="secondary"
              onClick={() => setActiveView("grid")}
              className="rounded-full px-3"
            >
              <ArrowLeft className="size-3" />
              <span>{tx("스튜디오로 돌아가기", "Back to Studio")}</span>
            </Button>
          )}
        </div>

        {/* Right: Conditional MIDI Learn Cancel Only */}
        {midiState.learning && (
          <Hint content={tx("클릭해 MIDI Learn 대기 취소", "Cancel MIDI Learn")}>
            <Button size="xs" variant="signal" onClick={handleCancelMidiLearn} className="rounded-full">
              MIDI LEARN…
            </Button>
          </Hint>
        )}
      </header>

      {/* ── Main Studio Workspace (RIG | VAULT | SCOPE) ── */}
      <CortexStudio
        activeView={activeView}
        catalogVersion={catalogVersion}
        isTourActive={tutorialOpen}
        looperOpen={looperOpen}
        onLooperOpenChange={setLooperOpen}
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

      {/* ── BOTTOM LIQUID GLASS FLOATING DOCK (Audio · Record · Menu) ── */}
      <nav
        data-tour="liquid-dock"
        aria-label={tx("스튜디오 빠른 제어 독", "Studio Floating Dock")}
        className="fixed bottom-5 left-1/2 -translate-x-1/2 z-40 nr-liquid-dock p-1.5 flex items-center gap-1.5"
      >
        <div data-tour="audio-io">
          <Hint content={audioStatus} side="top">
            <button
              type="button"
              data-audio={isAudioRunning ? "live" : "idle"}
              disabled={isAudioBusy}
              onClick={handleToggleAudio}
              className="nr-liquid-btn h-9 px-4 inline-flex items-center gap-2 text-[12.5px] font-medium cursor-pointer"
            >
              {isAudioRunning ? (
                <Square className="size-2.5 fill-current" />
              ) : (
                <Play className="size-2.5 fill-current text-primary" />
              )}
              <span className="tabular-nums">
                {isAudioRunning
                  ? headerLatencyMs !== null
                    ? `${tx("연주 중", "LIVE")} · ${headerLatencyMs.toFixed(1)}ms`
                    : tx("연주 중", "LIVE")
                  : tx("오디오 시작", "Start Audio")}
              </span>
            </button>
          </Hint>
        </div>

        <span className="h-4 w-px bg-foreground/12" aria-hidden="true" />

        <Hint
          content={tx("루퍼 · 듀얼 녹음 & 1클릭 리앰핑", "Looper, Dry+Wet Recording & Re-Amping")}
          kbd="L"
          side="top"
        >
          <button
            type="button"
            data-active={looperOpen ? "true" : "false"}
            data-recording={hasRecordActivity ? "true" : "false"}
            onClick={() => {
              setStudioMenuOpen(false)
              setActiveView("grid")
              setLooperOpen((v) => !v)
            }}
            className="nr-liquid-btn h-9 px-4 inline-flex items-center gap-2 text-[12.5px] font-medium text-foreground cursor-pointer"
          >
            <span
              className={`size-2 rounded-full transition-all duration-300 ${
                hasRecordActivity
                  ? "bg-white animate-pulse shadow-[0_0_8px_rgba(255,255,255,0.9)]"
                  : looperOpen
                    ? "bg-primary shadow-[0_0_8px_rgba(255,107,36,0.75)]"
                    : "bg-primary/85"
              }`}
            />
            <span>{tx("녹음", "Record")}</span>
          </button>
        </Hint>

        <span className="h-4 w-px bg-foreground/12" aria-hidden="true" />

        <Popover open={studioMenuOpen} onOpenChange={setStudioMenuOpen}>
          <PopoverTrigger asChild>
            <button
              type="button"
              data-active={studioMenuOpen ? "true" : "false"}
              className="nr-liquid-btn h-9 px-4 inline-flex items-center gap-2 text-[12.5px] font-medium text-foreground cursor-pointer"
            >
              <Menu className="size-3.5" strokeWidth={1.8} />
              <span>{tx("메뉴", "Menu")}</span>
            </button>
          </PopoverTrigger>
          <PopoverContent
            side="top"
            align="center"
            sideOffset={12}
            className="w-80 rounded-[18px] p-3 space-y-2.5"
          >
            {/* Account Sync Card */}
            <button
              type="button"
              onClick={() => {
                setStudioMenuOpen(false)
                setAuthDailyPrompt(false)
                setAuthOpen(true)
              }}
              className="w-full min-w-0 overflow-hidden flex items-center justify-between gap-2 px-3 py-2.5 rounded-[12px] bg-secondary/70 hover:bg-secondary border border-border text-left transition-colors cursor-pointer"
            >
              <div className="flex items-center gap-2 min-w-0 flex-1">
                <Cloud className="size-4 text-primary shrink-0" />
                <span className="text-xs font-semibold truncate">
                  {cloudState.isAuthenticated && cloudState.user
                    ? cloudState.user.displayName
                    : tx("Google 클라우드 동기화", "Cloud Account & Sync")}
                </span>
                <HelpHint
                  side="top"
                  content={
                    cloudState.isAuthenticated && cloudState.user
                      ? cloudState.user.email
                      : tx(
                          "프리셋·기타 프로필 클라우드 백업 및 커뮤니티 기어 게시",
                          "Back up presets & guitar profiles or publish custom gear."
                        )
                  }
                />
              </div>
              <span className="font-mono text-xs text-primary shrink-0">→</span>
            </button>

            {/* Compact Segmented Mode Chips: Theme & Language */}
            <div className="grid grid-cols-2 gap-1.5">
              <button
                type="button"
                onClick={toggleTheme}
                className="h-8 min-w-0 overflow-hidden px-2.5 rounded-[10px] border border-border bg-card hover:bg-secondary text-xs font-medium flex items-center justify-between gap-1.5 transition-colors cursor-pointer"
              >
                <span className="text-muted-foreground truncate">{tx("화면 테마", "Theme")}</span>
                <span className="font-mono text-[11px] font-semibold text-foreground shrink-0">
                  {themeId === "mist" ? tx("라이트", "Mist") : tx("다크", "Dark")}
                </span>
              </button>
              <button
                type="button"
                onClick={toggleLocale}
                className="h-8 min-w-0 overflow-hidden px-2.5 rounded-[10px] border border-border bg-card hover:bg-secondary text-xs font-medium flex items-center justify-between gap-1.5 transition-colors cursor-pointer"
              >
                <span className="text-muted-foreground truncate">{tx("언어", "Lang")}</span>
                <span className="font-mono text-[11px] font-semibold text-foreground shrink-0">
                  {locale === "ko" ? "한국어" : "English"}
                </span>
              </button>
            </div>

            {/* 2x2 Quick Studio Tools Grid */}
            <div className="grid grid-cols-2 gap-1.5">
              <button
                type="button"
                onClick={() => {
                  setStudioMenuOpen(false)
                  setAudioDialogOpen(true)
                }}
                className="min-w-0 overflow-hidden flex items-center gap-2 px-2.5 py-2 rounded-[10px] border border-border bg-card hover:bg-secondary text-left transition-colors cursor-pointer"
              >
                <SlidersHorizontal className="size-3.5 text-primary shrink-0" />
                <span className="text-xs font-medium truncate">
                  {tx("오디오 설정", "Audio I/O")}
                </span>
              </button>

              <button
                type="button"
                onClick={() => {
                  setStudioMenuOpen(false)
                  setTunerDialogOpen(true)
                }}
                className="min-w-0 overflow-hidden flex items-center justify-between gap-1.5 px-2.5 py-2 rounded-[10px] border border-border bg-card hover:bg-secondary text-left transition-colors cursor-pointer"
              >
                <div className="flex items-center gap-2 min-w-0">
                  <Music2 className="size-3.5 text-primary shrink-0" />
                  <span className="text-xs font-medium truncate">
                    {tx("정밀 튜너", "Tuner")}
                  </span>
                </div>
                {tuner.noteName !== "--" && (
                  <span className="font-mono text-[10px] font-bold text-signal-ok tabular-nums shrink-0">
                    {tuner.noteName}
                    {tuner.octave}
                  </span>
                )}
              </button>

              <button
                type="button"
                onClick={() => {
                  setStudioMenuOpen(false)
                  setGuitarDialogOpen(true)
                }}
                className="min-w-0 overflow-hidden flex items-center gap-2 px-2.5 py-2 rounded-[10px] border border-border bg-card hover:bg-secondary text-left transition-colors cursor-pointer"
              >
                <Guitar className="size-3.5 text-primary shrink-0" />
                <span className="text-xs font-medium truncate">
                  {tx("기타 DI 분석", "Guitar DI")}
                </span>
              </button>

              <button
                type="button"
                onClick={() => {
                  setStudioMenuOpen(false)
                  setCommandOpen(true)
                }}
                className="min-w-0 overflow-hidden flex items-center justify-between gap-1.5 px-2.5 py-2 rounded-[10px] border border-border bg-card hover:bg-secondary text-left transition-colors cursor-pointer"
              >
                <div className="flex items-center gap-2 min-w-0">
                  <Search className="size-3.5 text-primary shrink-0" />
                  <span className="text-xs font-medium truncate">
                    {tx("빠른 검색", "Search")}
                  </span>
                </div>
                <span className="font-mono text-[10px] text-muted-foreground shrink-0">/</span>
              </button>
            </div>

            <Separator />

            {/* Workspace & Advanced Feature List */}
            <div className="space-y-0.5 text-xs min-w-0">
              {[
                {
                  icon: Layers,
                  label: tx("톤 보관함 · .NAM · VST3", "Rig Vault & VST3"),
                  action: () => setActiveView("directory"),
                },
                {
                  icon: Activity,
                  label: activeGuitar
                    ? `${tx("스펙트럼 · 내 기타", "Lab")}: ${activeGuitar.name}`
                    : tx("스펙트럼 · 기타 분석", "Harmonic & DI Lab"),
                  action: () => setActiveView("scope"),
                },
                {
                  icon: Globe,
                  label: tx("Tone Hub · 커뮤니티", "Tone Hub & Community"),
                  action: () => setToneHubOpen(true),
                },
                {
                  icon: Wrench,
                  label: tx("AI 페달·앰프 제작 (MCP)", "Craft Gear with AI (MCP)"),
                  action: () => setGearGuideOpen(true),
                },
                {
                  icon: Gauge,
                  label: tx("DSP 성능 벤치마크", "DSP Benchmark"),
                  action: () => setBenchOpen(true),
                },
                {
                  icon: BookOpen,
                  label: tx("가이드 투어 보기", "Guided Spotlight Tour"),
                  action: () => setTutorialOpen(true),
                },
              ].map((item) => {
                const Icon = item.icon
                return (
                  <button
                    key={item.label}
                    type="button"
                    onClick={() => {
                      setStudioMenuOpen(false)
                      item.action()
                    }}
                    className="w-full min-w-0 overflow-hidden flex items-center gap-2.5 px-2.5 py-1.5 rounded-[8px] hover:bg-secondary text-left text-foreground transition-colors cursor-pointer"
                  >
                    <Icon className="size-3.5 text-muted-foreground shrink-0" strokeWidth={1.6} />
                    <span className="truncate min-w-0 flex-1">{item.label}</span>
                  </button>
                )
              })}

              {cloudState.user?.isAdmin && (
                <button
                  type="button"
                  onClick={() => {
                    setStudioMenuOpen(false)
                    setAdminOpen(true)
                  }}
                  className="w-full min-w-0 overflow-hidden flex items-center gap-2.5 px-2.5 py-1.5 rounded-[8px] hover:bg-secondary text-left text-signal-warn transition-colors cursor-pointer"
                >
                  <Shield className="size-3.5 shrink-0" strokeWidth={1.6} />
                  <span className="font-medium truncate min-w-0 flex-1">
                    {tx("어드민 애널리틱스 · OTA 콘솔", "Admin Analytics & OTA Console")}
                  </span>
                </button>
              )}
            </div>

            {/* Compact Diagnostics Footer */}
            <div className="pt-2 border-t border-border flex items-center justify-between text-[11px] text-muted-foreground">
              <button
                type="button"
                onClick={() => {
                  setStudioMenuOpen(false)
                  setLogsOpen(true)
                }}
                className="hover:text-foreground transition-colors cursor-pointer"
              >
                {tx(`콘솔 로그 (${logs.length})`, `Logs (${logs.length})`)}
              </button>
              <button
                type="button"
                onClick={() => {
                  setStudioMenuOpen(false)
                  setBugOpen(true)
                }}
                className="hover:text-foreground transition-colors cursor-pointer"
              >
                {tx("버그 제보", "Bug Report")}
              </button>
              <button
                type="button"
                onClick={() => {
                  setStudioMenuOpen(false)
                  void handleManualCheckUpdates()
                }}
                className="hover:text-foreground font-mono tabular-nums transition-colors cursor-pointer"
              >
                v{appVersion}
              </button>
            </div>
          </PopoverContent>
        </Popover>
      </nav>

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
        onOpenAuth={() => {
          setAuthDailyPrompt(false)
          setAuthOpen(true)
        }}
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
        onRequestLogin={() => {
          setAuthDailyPrompt(false)
          setAuthOpen(true)
        }}
        onLog={addLog}
      />

      {/* ── Hardware Similarity / Latency / 16-Slot Multi-Chain Benchmark ── */}
      <BenchmarkDialog
        open={benchOpen}
        onOpenChange={setBenchOpen}
        isAudioRunning={isAudioRunning}
        onLog={addLog}
      />

      {/* ── Interactive Onboarding Tutorial (Spotlight Coachmark Tour) ── */}
      <OnboardingTutorialModal
        open={tutorialOpen}
        onOpenChange={setTutorialOpen}
        activeView={activeView}
        onEnsureStudioView={() => setActiveView("grid")}
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
        dailyPrompt={authDailyPrompt}
        onOpenChange={(nextOpen) => {
          setAuthOpen(nextOpen)
          if (!nextOpen) {
            setAuthDailyPrompt(false)
            if (!hasCompletedTutorial) {
              setTutorialOpen(true)
            }
          }
        }}
        cloudState={cloudState}
        onCloudStateChange={(nextState) => {
          setCloudState(nextState)
          syncPromptRefsFromCloudState(nextState)
          if (nextState.mode !== "unselected" && !hasCompletedTutorial) {
            setTutorialOpen(true)
          }
        }}
        onCheckUpdates={() => void handleManualCheckUpdates()}
        onLog={addLog}
      />

      {/* ── One-Time Patreon Support Prompt (7+ Days Signed-In Creators) ── */}
      <SponsorPromptModal
        open={sponsorOpen}
        onOpenChange={setSponsorOpen}
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
      {(cloudState.user?.isAdmin || adminOpen) && (
        <AdminConsoleDialog
          open={adminOpen}
          onOpenChange={setAdminOpen}
          onReLogin={() => {
            setAdminOpen(false)
            setAuthDailyPrompt(false)
            setAuthOpen(true)
          }}
          onLog={addLog}
        />
      )}
    </div>
  )
}
