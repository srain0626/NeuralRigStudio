import { useEffect, useState } from "react"
import {
  Activity,
  Guitar,
  Music2,
  Play,
  RefreshCw,
  ScrollText,
  SlidersHorizontal,
  Square,
  Usb,
  VolumeX,
  Wrench,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Label } from "@/components/ui/label"
import { ScrollArea } from "@/components/ui/scroll-area"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import { NeuralPulseLogo } from "@/components/CortexIcons"
import { CortexStudio } from "@/components/CortexStudio"
import { GuitarAnalyzerDialog } from "@/components/GuitarAnalyzerDialog"
import { TunerDialog } from "@/components/TunerDialog"
import { FACTORY_PRESETS } from "@/data/cortexCatalog"
import { studioBridge } from "@/lib/bridge"
import type {
  AsioCapabilitiesDto,
  BackingTrackStateDto,
  GuitarAnalysisLiveStateDto,
  GuitarProfileDto,
  MidiSnapshotDto,
  ModelProfileSummary,
  PresetDef,
  SignalChainConfigDto,
  StudioInitSnapshot,
  TelemetryFrame,
  TunerSnapshotDto,
  WasapiDeviceDto,
} from "@/types/rig"

interface LogEntry {
  id: number
  text: string
  level: "info" | "success" | "error"
}

export default function App() {
  const [activeView, setActiveView] = useState<"grid" | "directory">("grid")
  const [logsOpen, setLogsOpen] = useState(false)
  const [audioDialogOpen, setAudioDialogOpen] = useState(false)
  const [guitarDialogOpen, setGuitarDialogOpen] = useState(false)
  const [tunerDialogOpen, setTunerDialogOpen] = useState(false)
  const [tunerMute, setTunerMute] = useState(false)
  const [tunerRefA4Hz, setTunerRefA4Hz] = useState(440)
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [guitarProfiles, setGuitarProfiles] = useState<GuitarProfileDto[]>([])
  const [activeGuitarProfileId, setActiveGuitarProfileId] = useState("")
  const [guitarAnalysis, setGuitarAnalysis] = useState<GuitarAnalysisLiveStateDto | null>(null)

  const [asioDrivers, setAsioDrivers] = useState<string[]>([])
  const [wasapiDevices, setWasapiDevices] = useState<WasapiDeviceDto[]>([
    { id: "", label: "시스템 기본 출력 장치" },
  ])
  const [wasapiInputDevices, setWasapiInputDevices] = useState<WasapiDeviceDto[]>([
    { id: "", label: "시스템 기본 입력 장치 (자동)" },
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
  const [customCabIrName, setCustomCabIrName] = useState("")
  const [initialChain, setInitialChain] = useState<SignalChainConfigDto>(FACTORY_PRESETS[0])
  const [savedPresets, setSavedPresets] = useState<SignalChainConfigDto[]>([])
  const [backingInitState, setBackingInitState] = useState<BackingTrackStateDto | undefined>(undefined)
  const [midiState, setMidiState] = useState<MidiSnapshotDto>({
    devices: [],
    activeDevice: "",
    connected: false,
    bindings: [],
    learning: null,
  })

  const [isAudioRunning, setIsAudioRunning] = useState(false)
  const [isAudioBusy, setIsAudioBusy] = useState(false)
  const [audioStatus, setAudioStatus] = useState("대기 중")
  const [headerLatencyMs, setHeaderLatencyMs] = useState<number | null>(null)
  const [tuner, setTuner] = useState<TunerSnapshotDto>({
    active: false,
    noteName: "--",
    octave: 0,
    cents: 0,
    freqHz: 0,
  })

  const addLog = (text: string, level: "info" | "success" | "error" = "info") => {
    const line = text.startsWith("[") ? text : `[${new Date().toLocaleTimeString()}] ${text}`
    setLogs((prev) => [{ id: Date.now() + Math.random(), text: line, level }, ...prev].slice(0, 120))
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
          setTuner({ active: false, noteName: "--", octave: 0, cents: 0, freqHz: 0, clarity: 0, levelDb: -96 })
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
          setWasapiDevices(snap.audio.wasapiDevices || [{ id: "", label: "시스템 기본 출력 장치" }])
          setWasapiInputDevices(
            snap.audio.wasapiInputDevices || [{ id: "", label: "시스템 기본 입력 장치 (자동)" }]
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
          if (snap.signalChain) setInitialChain(snap.signalChain)
          if (snap.savedPresets) setSavedPresets(snap.savedPresets)
          setGuitarProfiles(snap.guitarProfiles || [])
          setActiveGuitarProfileId(snap.activeGuitarProfileId || "")
          setTunerMute(Boolean(snap.tunerMute))
          if (snap.tunerRefA4Hz) setTunerRefA4Hz(snap.tunerRefA4Hz)
          if (snap.backing) setBackingInitState(snap.backing)
          if (snap.midi) setMidiState(snap.midi)
          setIsAudioRunning(snap.isAudioRunning)
        })
        .catch((e) => addLog(`초기화 오류: ${String(e)}`, "error"))
    }

    return () => {
      unsubLog()
      unsubAudio()
      unsubTones()
      unsubGuitars()
      unsubMidi()
      unsubTunerMute()
      unsubTel()
    }
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

      const gpDriver = drivers.find((d) => /valeton|gp-?100/i.test(d))
      let activeDriver = inputDriver
      if (!activeDriver || !drivers.includes(activeDriver) || (/realtek/i.test(activeDriver) && gpDriver)) {
        activeDriver = gpDriver || res.initialDriver || drivers[0] || ""
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
      if (!isAudioRunning) handleRefreshDevices()
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
      addLog(`MIDI 장치 연결 오류: ${String(e)}`, "error")
    }
  }

  const handleStartMidiLearn = async (req: {
    targetKind: string
    slotIndex?: number
    paramIndex?: number
    sceneIndex?: number
  }) => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<MidiSnapshotDto>("startMidiLearn", req)
      if (res) setMidiState(res)
    } catch (e) {
      addLog(`MIDI Learn 시작 실패: ${String(e)}`, "error")
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
    } catch {
      // ignore
    }
  }

  const handleQuickStartGp100 = async (mode: "WASAPI" | "ASIO_DUPLEX") => {
    if (!studioBridge.isWebView2 || isAudioBusy) return
    const gpDriver = asioDrivers.find((d) => /valeton|gp-?100/i.test(d)) || "Valeton USB Audio Device"
    setInputKind("ASIO")
    setInputDriver(gpDriver)
    setChannelMode(1)
    setInputChannelOffset(0)
    setOutputKind(mode)
    setPresetIndex(1)
    addLog(`[GP-100] Valeton GP-100 원클릭 연결 시작 (${mode === "ASIO_DUPLEX" ? "GP-100 헤드폰 직결" : "PC 스피커/헤드셋"})…`, "info")
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
        inputDriver: gpDriver,
        wasapiInputDeviceId,
        wasapiInputExclusive,
        wasapiInputRaw,
        channelMode: 1,
        inputChannelOffset: 0,
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
      addLog(`[GP-100] 연결 완료 (${gpDriver})`, "success")
    } catch (e) {
      addLog(`GP-100 연결 실패: ${String(e)}`, "error")
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
    addLog("[범용 입력] Windows 표준 오디오 입력(WASAPI) 원클릭 시작…", "info")
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
      addLog("[범용 입력] 연결 완료", "success")
    } catch (e) {
      addLog(`범용 입력 시작 실패: ${String(e)}`, "error")
      setAudioDialogOpen(true)
    }
  }

  const handleToggleAudio = async () => {
    if (!studioBridge.isWebView2) return
    if (isAudioRunning) {
      try {
        await studioBridge.invoke("stopMonitoring")
      } catch (e) {
        addLog(`중지 오류: ${String(e)}`, "error")
      }
      return
    }
    const effectiveInKind = inputKind === "ASIO" && !inputDriver && asioDrivers.length === 0 ? "WASAPI" : inputKind
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
        outputKind: effectiveInKind === "WASAPI" && outputKind === "ASIO_DUPLEX" ? "WASAPI" : outputKind,
        wasapiExclusive,
        wasapiDeviceId,
        asioOutDriver,
        presetIndex,
      })
      if (res?.inputKind === "WASAPI" || res?.inputKind === "ASIO") setInputKind(res.inputKind)
      if (res?.inputDriver) setInputDriver(res.inputDriver)
      if (res?.outputKind) setOutputKind(res.outputKind)
    } catch (e) {
      addLog(`오디오 시작 실패: ${String(e)}`, "error")
      setAudioDialogOpen(true)
    }
  }

  const handleOpenAsioPanel = async () => {
    if (!studioBridge.isWebView2 || !inputDriver) return
    try {
      await studioBridge.invoke("openAsioPanel", { driver: inputDriver })
    } catch (e) {
      addLog(`ASIO 제어판 열기 실패: ${String(e)}`, "error")
    }
  }

  const handleTunerConfigChange = async (opts: { mute?: boolean; refA4Hz?: number }) => {
    if (opts.mute !== undefined) setTunerMute(opts.mute)
    if (opts.refA4Hz !== undefined) setTunerRefA4Hz(opts.refA4Hz)
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ mute: boolean; refA4Hz: number }>("setTunerConfig", opts)
      if (res) {
        setTunerMute(Boolean(res.mute))
        setTunerRefA4Hz(res.refA4Hz || 440)
      }
    } catch (e) {
      addLog(`튜너 설정 실패: ${String(e)}`, "error")
    }
  }

  return (
    <div className="h-screen w-screen overflow-hidden bg-background text-foreground flex flex-col">
      {/* ── Cortex Control Global Top Header ── */}
      <header className="h-11 shrink-0 border-b border-border bg-card/90 px-3 flex items-center justify-between gap-3">
        {/* Left: Neural Pulse Logo */}
        <div className="flex items-center gap-2.5 min-w-[180px]">
          <div className="w-7 h-7 rounded-md bg-secondary border border-border flex items-center justify-center text-foreground">
            <NeuralPulseLogo className="w-4 h-4" />
          </div>
          <span className="text-xs font-bold tracking-wide text-foreground">NEURALRIG</span>
        </div>

        {/* Center: GRID | DIRECTORY Navigation Tabs (Exact Cortex Control Style) */}
        <nav className="flex items-center h-full gap-1">
          {(["grid", "directory"] as const).map((tab) => {
            const active = activeView === tab
            return (
              <button
                key={tab}
                type="button"
                onClick={() => setActiveView(tab)}
                className={`relative h-full px-5 text-[11px] font-bold tracking-widest uppercase transition-colors ${
                  active ? "text-foreground" : "text-muted-foreground hover:text-foreground/80"
                }`}
              >
                {tab}
                {active && (
                  <span className="absolute top-0 left-2 right-2 h-[2px] bg-foreground rounded-b-full" />
                )}
              </button>
            )
          })}
        </nav>

        {/* Right: MIDI Learn Indicator + Tuner + Live Audio Engine Control + Audio Settings + Logs */}
        <div className="flex items-center gap-1.5 justify-end min-w-[180px]">
          {midiState.learning && (
            <button
              type="button"
              onClick={handleCancelMidiLearn}
              className="h-7 px-2 rounded-md border border-amber-400/70 bg-amber-500/20 text-amber-200 text-[10px] font-mono font-bold animate-pulse cursor-pointer"
              title="외부 MIDI 페달/스위치를 조작하세요 (클릭하여 취소)"
            >
              MIDI LEARN… (취소)
            </button>
          )}
          {/* Live MPM Chromatic Tuner Pill (Click to Open Studio Tuner) */}
          <button
            type="button"
            onClick={() => setTunerDialogOpen(true)}
            className={`h-7 px-2.5 rounded-md border flex items-center gap-1.5 text-xs font-mono transition-colors cursor-pointer hover:opacity-90 ${
              tuner.active && Math.abs(tuner.cents) <= 3.5
                ? "border-emerald-500/50 bg-emerald-500/15 text-emerald-300"
                : tuner.active
                  ? "border-amber-500/40 bg-amber-500/10 text-amber-200"
                  : "border-border bg-secondary/50 text-muted-foreground hover:text-foreground"
            }`}
            title={
              tuner.active
                ? `튜너: ${tuner.freqHz.toFixed(1)} Hz (${tuner.cents > 0 ? "+" : ""}${tuner.cents.toFixed(1)} ct) — 클릭하여 정밀 튜너 열기`
                : "클릭하여 정밀 크로매틱 튜너 열기"
            }
          >
            {tunerMute ? (
              <VolumeX className="w-3 h-3 shrink-0 text-destructive" />
            ) : (
              <Music2 className="w-3 h-3 shrink-0" />
            )}
            <span className="font-bold min-w-[24px] text-center">
              {tuner.noteName !== "--" ? `${tuner.noteName}${tuner.octave}` : "--"}
            </span>
            {tuner.noteName !== "--" && (
              <span className="text-[10px] opacity-80">
                {tuner.cents > 0 ? `+${tuner.cents.toFixed(0)}` : tuner.cents.toFixed(0)}ct
              </span>
            )}
          </button>

          {/* My Guitar Analyzer & Saved Profile Button */}
          {(() => {
            const activeGuitar = guitarProfiles.find((g) => g.id === activeGuitarProfileId)
            return (
              <Button
                size="sm"
                variant={activeGuitar ? "secondary" : "ghost"}
                className="h-7 px-2 text-[11px] gap-1 max-w-[140px]"
                onClick={() => setGuitarDialogOpen(true)}
                title={
                  activeGuitar
                    ? `활성 기타: ${activeGuitar.name} (${activeGuitar.estimatedPickupType})`
                    : "기타 음정 가이드 분석 및 프로필 저장"
                }
              >
                <Guitar className="w-3.5 h-3.5 shrink-0" />
                <span className="truncate">
                  {activeGuitar ? activeGuitar.name : "기타 분석"}
                </span>
              </Button>
            )
          })()}

          {/* Valeton GP-100 1-Click Quick Connect */}
          {asioDrivers.some((d) => /valeton|gp-?100/i.test(d)) && (
            <Button
              size="sm"
              variant={
                isAudioRunning && inputKind === "ASIO" && /valeton|gp-?100/i.test(inputDriver)
                  ? "default"
                  : "secondary"
              }
              className="h-7 px-2 text-[11px] font-mono gap-1"
              disabled={isAudioBusy}
              onClick={() => {
                if (isAudioRunning) {
                  setAudioDialogOpen(true)
                } else {
                  handleQuickStartGp100(outputKind === "ASIO_DUPLEX" ? "ASIO_DUPLEX" : "WASAPI")
                }
              }}
              title="Valeton GP-100 원클릭 연결 (모노 In 1 + 저지연)"
            >
              <Usb className="w-3 h-3" />
              GP-100
            </Button>
          )}

          {/* Start / Stop Audio Engine */}
          <Button
            size="sm"
            variant={isAudioRunning ? "destructive" : "secondary"}
            className="h-7 px-2.5 text-[11px] font-mono gap-1.5"
            disabled={isAudioBusy}
            onClick={handleToggleAudio}
            title={audioStatus}
          >
            {isAudioRunning ? <Square className="w-3 h-3 fill-current" /> : <Play className="w-3 h-3 fill-current" />}
            {isAudioRunning
              ? headerLatencyMs !== null
                ? `${headerLatencyMs.toFixed(1)} ms`
                : "LIVE"
              : "AUDIO"}
          </Button>

          {/* Audio I/O Settings Button */}
          <Button
            size="icon"
            variant="ghost"
            className="h-7 w-7"
            onClick={() => setAudioDialogOpen(true)}
            title="오디오 I/O 설정 (ASIO / WASAPI)"
          >
            <SlidersHorizontal className="w-3.5 h-3.5" />
          </Button>

          {/* Logs Button */}
          <Button
            size="icon"
            variant="ghost"
            className="h-7 w-7"
            onClick={() => setLogsOpen(true)}
            title={`시스템 로그 (${logs.length})`}
          >
            <ScrollText className="w-3.5 h-3.5" />
          </Button>
        </div>
      </header>

      {/* ── Main Cortex Studio Workspace ── */}
      <CortexStudio
        activeView={activeView}
        onChangeView={setActiveView}
        initialChain={initialChain}
        initialSavedPresets={savedPresets}
        tones={tones}
        selectedToneId={selectedToneId}
        customCabIrName={customCabIrName}
        initialBacking={backingInitState}
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

      {/* ── Audio I/O Settings Dialog ── */}
      <Dialog open={audioDialogOpen} onOpenChange={setAudioDialogOpen}>
        <DialogContent className="max-w-lg max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>범용 오디오 I/O 설정 (ASIO · WASAPI · GP-100)</DialogTitle>
          </DialogHeader>

          <div className="space-y-3.5 text-xs">
            {/* Input Backend Selector: ASIO vs Universal WASAPI */}
            <div className="grid grid-cols-2 gap-1.5 p-1 rounded-lg bg-secondary/60 border border-border">
              <button
                type="button"
                disabled={isAudioRunning}
                onClick={() => setInputKind("ASIO")}
                className={`h-8 rounded-md text-[11px] font-semibold transition-all disabled:opacity-60 ${
                  inputKind === "ASIO"
                    ? "bg-card text-foreground shadow border border-border"
                    : "text-muted-foreground hover:text-foreground"
                }`}
              >
                ⚡ ASIO 인터페이스 / GP-100
              </button>
              <button
                type="button"
                disabled={isAudioRunning}
                onClick={() => {
                  setInputKind("WASAPI")
                  if (outputKind === "ASIO_DUPLEX") setOutputKind("WASAPI")
                }}
                className={`h-8 rounded-md text-[11px] font-semibold transition-all disabled:opacity-60 ${
                  inputKind === "WASAPI"
                    ? "bg-card text-foreground shadow border border-border"
                    : "text-muted-foreground hover:text-foreground"
                }`}
              >
                🔌 범용 Windows 입력 (WASAPI)
              </button>
            </div>

            {/* Quick Start Banner (GP-100 + Universal Plug-and-Play) */}
            {inputKind === "ASIO" ? (
              <div className="rounded-md border border-border bg-secondary/45 p-3 space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <span className="text-[11px] font-semibold text-foreground flex items-center gap-1.5">
                    <Usb className="w-3.5 h-3.5" />
                    Valeton GP-100 빠른 연결 (범용 ASIO 겸용)
                  </span>
                  <Badge
                    variant={asioDrivers.some((d) => /valeton|gp-?100/i.test(d)) ? "default" : "outline"}
                    className="text-[10px] font-mono"
                  >
                    {asioDrivers.some((d) => /valeton|gp-?100/i.test(d))
                      ? "GP-100 감지됨"
                      : `${asioDrivers.length}개 ASIO 드라이버`}
                  </Badge>
                </div>
                <div className="grid grid-cols-2 gap-2">
                  <Button
                    size="sm"
                    variant="default"
                    className="h-7 text-[11px]"
                    disabled={isAudioBusy}
                    onClick={() => handleQuickStartGp100("WASAPI")}
                  >
                    GP-100 → PC 스피커/헤드셋
                  </Button>
                  <Button
                    size="sm"
                    variant="secondary"
                    className="h-7 text-[11px]"
                    disabled={isAudioBusy}
                    onClick={() => handleQuickStartGp100("ASIO_DUPLEX")}
                  >
                    GP-100 → 본체 헤드폰 직결
                  </Button>
                </div>
                <p className="text-[10px] text-muted-foreground leading-snug">
                  Focusrite, Steinberg, Audient, MOTU 등 모든 표준 ASIO 다채널 인터페이스와 GP-100을 동시에 지원합니다. ASIO 드라이버가 없는 USB 기타 케이블/라인인은 상단의 <b>범용 Windows 입력</b> 탭을 선택하세요.
                </p>
              </div>
            ) : (
              <div className="rounded-md border border-emerald-500/30 bg-emerald-500/10 p-3 space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <span className="text-[11px] font-semibold text-emerald-200 flex items-center gap-1.5">
                    <Usb className="w-3.5 h-3.5" />
                    범용 플러그앤플레이 입력 (무드라이버 WASAPI)
                  </span>
                  <Button
                    size="sm"
                    variant="secondary"
                    className="h-6 px-2.5 text-[10px]"
                    disabled={isAudioBusy}
                    onClick={handleQuickStartUniversalWasapi}
                  >
                    기본 입력으로 즉시 시작
                  </Button>
                </div>
                <p className="text-[10px] text-muted-foreground leading-snug">
                  별도 ASIO 드라이버 없이 USB 기타 케이블(Rocksmith 등), 클래스 컴플라이언트 오디오 인터페이스, PC 라인인(Line-In)·마이크 단자를 즉시 연결합니다.
                </p>
              </div>
            )}

            <div className="flex items-center justify-between gap-2">
              <Badge variant="outline" className="font-mono text-[11px]">
                <Activity className="w-3 h-3 mr-1" />
                {audioStatus}
              </Badge>
              <div className="flex items-center gap-1.5">
                <Button size="sm" variant="ghost" className="h-7 text-xs" onClick={handleRefreshDevices}>
                  <RefreshCw className="w-3 h-3 mr-1" />
                  새로고침
                </Button>
                {inputKind === "ASIO" && (
                  <Button
                    size="sm"
                    variant="secondary"
                    className="h-7 text-xs"
                    disabled={!inputDriver || isAudioRunning}
                    onClick={handleOpenAsioPanel}
                  >
                    <Wrench className="w-3 h-3 mr-1" />
                    ASIO 제어판
                  </Button>
                )}
              </div>
            </div>

            {inputKind === "ASIO" ? (
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1.5">
                  <Label className="text-[11px] text-muted-foreground">ASIO 입력 드라이버</Label>
                  <Select
                    value={inputDriver || "__none__"}
                    disabled={isAudioRunning}
                    onValueChange={(v) => {
                      if (v === "__none__") return
                      setInputDriver(v)
                      setInputChannelOffset(0)
                      probeAsioCaps(v)
                    }}
                  >
                    <SelectTrigger className="h-8 text-xs">
                      <SelectValue placeholder="드라이버 선택" />
                    </SelectTrigger>
                    <SelectContent>
                      {asioDrivers.length === 0 ? (
                        <SelectItem value="__none__">감지된 ASIO 없음 (범용 탭 권장)</SelectItem>
                      ) : (
                        asioDrivers.map((d) => (
                          <SelectItem key={d} value={d}>
                            {d}
                          </SelectItem>
                        ))
                      )}
                    </SelectContent>
                  </Select>
                </div>

                <div className="space-y-1.5">
                  <Label className="text-[11px] text-muted-foreground">입력 단자 / 채널 선택</Label>
                  {(() => {
                    const chNames =
                      caps?.inputChannels && caps.inputChannels.length > 0
                        ? caps.inputChannels
                        : ["In 1", "In 2"]
                    const selKey = `${channelMode === 0 ? "stereo" : "mono"}:${Math.min(
                      inputChannelOffset,
                      Math.max(0, chNames.length - 1)
                    )}`
                    return (
                      <Select
                        value={selKey}
                        disabled={isAudioRunning}
                        onValueChange={(v) => {
                          const [modeStr, offStr] = v.split(":")
                          setChannelMode(modeStr === "stereo" ? 0 : 1)
                          setInputChannelOffset(Math.max(0, Number(offStr) || 0))
                        }}
                      >
                        <SelectTrigger className="h-8 text-xs">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          {chNames.map((nm, idx) => (
                            <SelectItem key={`mono:${idx}`} value={`mono:${idx}`}>
                              모노 Ch {idx + 1} ({nm} → 양쪽)
                            </SelectItem>
                          ))}
                          {Array.from({ length: Math.floor(chNames.length / 2) }, (_, pairIdx) => {
                            const c = pairIdx * 2
                            return (
                              <SelectItem key={`stereo:${c}`} value={`stereo:${c}`}>
                                스테레오 Ch {c + 1}+{c + 2} ({chNames[c]}/{chNames[c + 1]})
                              </SelectItem>
                            )
                          })}
                        </SelectContent>
                      </Select>
                    )
                  })()}
                </div>

                <div className="space-y-1.5">
                  <Label className="text-[11px] text-muted-foreground">출력 엔진</Label>
                  <Select
                    value={outputKind}
                    disabled={isAudioRunning}
                    onValueChange={(v) => setOutputKind(v)}
                  >
                    <SelectTrigger className="h-8 text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="WASAPI">WASAPI (PC 스피커/헤드셋)</SelectItem>
                      <SelectItem value="ASIO_DUPLEX">ASIO 직결 풀듀플렉스 (인터페이스 출력)</SelectItem>
                      <SelectItem value="ASIO">별도 제2 ASIO 출력</SelectItem>
                    </SelectContent>
                  </Select>
                </div>

                <div className="space-y-1.5">
                  <Label className="text-[11px] text-muted-foreground">ASIO 버퍼 크기</Label>
                  <Select
                    value={String(bufferSize)}
                    disabled={isAudioRunning}
                    onValueChange={(v) => setBufferSize(Number(v))}
                  >
                    <SelectTrigger className="h-8 text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {(caps?.bufferSizes?.length ? caps.bufferSizes : [64, 128, 256, 512, 0]).map((sz) => (
                        <SelectItem key={sz} value={String(sz)}>
                          {sz === 0 ? "드라이버 기본값" : `${sz} samples`}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              </div>
            ) : (
              <div className="space-y-3">
                <div className="space-y-1.5">
                  <Label className="text-[11px] text-muted-foreground">Windows 오디오 입력 장치 (WASAPI Capture)</Label>
                  <Select
                    value={wasapiInputDeviceId || "__default__"}
                    disabled={isAudioRunning}
                    onValueChange={(v) => {
                      setWasapiInputDeviceId(v === "__default__" ? "" : v)
                      setInputChannelOffset(0)
                    }}
                  >
                    <SelectTrigger className="h-8 text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {wasapiInputDevices.map((d) => (
                        <SelectItem key={d.id || "__default__"} value={d.id || "__default__"}>
                          {d.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-1.5">
                    <Label className="text-[11px] text-muted-foreground">입력 채널 매핑</Label>
                    {(() => {
                      const selDev = wasapiInputDevices.find((d) => d.id === wasapiInputDeviceId)
                      const maxCh = Math.max(2, selDev?.channels ?? 2)
                      const selKey = `${channelMode === 0 ? "stereo" : "mono"}:${Math.min(
                        inputChannelOffset,
                        maxCh - 1
                      )}`
                      return (
                        <Select
                          value={selKey}
                          disabled={isAudioRunning}
                          onValueChange={(v) => {
                            const [modeStr, offStr] = v.split(":")
                            setChannelMode(modeStr === "stereo" ? 0 : 1)
                            setInputChannelOffset(Math.max(0, Number(offStr) || 0))
                          }}
                        >
                          <SelectTrigger className="h-8 text-xs">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {Array.from({ length: maxCh }, (_, idx) => (
                              <SelectItem key={`mono:${idx}`} value={`mono:${idx}`}>
                                모노 Ch {idx + 1} ({idx === 0 ? "좌→양쪽 권장" : idx === 1 ? "우→양쪽" : `In ${idx + 1}`})
                              </SelectItem>
                            ))}
                            <SelectItem value="stereo:0">스테레오 (Ch 1 + Ch 2)</SelectItem>
                          </SelectContent>
                        </Select>
                      )
                    })()}
                  </div>

                  <div className="space-y-1.5">
                    <Label className="text-[11px] text-muted-foreground">출력 엔진</Label>
                    <Select
                      value={outputKind === "ASIO_DUPLEX" ? "WASAPI" : outputKind}
                      disabled={isAudioRunning}
                      onValueChange={(v) => setOutputKind(v)}
                    >
                      <SelectTrigger className="h-8 text-xs">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="WASAPI">WASAPI (PC 스피커/헤드셋)</SelectItem>
                        <SelectItem value="ASIO">ASIO 출력 드라이버</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-2">
                  <div
                    className="flex items-center justify-between rounded-md border border-border bg-secondary/40 px-2.5 py-2"
                    title="Windows 마이크 소음 억제·자동 게인(AGC)을 우회하여 일렉기타 서스테인이 끊기는 현상을 방지합니다."
                  >
                    <Label className="text-[11px]">순수 악기 Raw 모드</Label>
                    <Switch
                      checked={wasapiInputRaw}
                      disabled={isAudioRunning}
                      onCheckedChange={setWasapiInputRaw}
                    />
                  </div>
                  <div
                    className="flex items-center justify-between rounded-md border border-border bg-secondary/40 px-2.5 py-2"
                    title="입력 장치를 단독 점유하여 캡처 레이턴시를 최소화합니다 (미지원 시 공유 저지연 자동 폴백)."
                  >
                    <Label className="text-[11px]">WASAPI 입력 단독 모드</Label>
                    <Switch
                      checked={wasapiInputExclusive}
                      disabled={isAudioRunning}
                      onCheckedChange={setWasapiInputExclusive}
                    />
                  </div>
                </div>
              </div>
            )}

            {outputKind === "WASAPI" && (
              <div className="space-y-3">
                <div className="space-y-1.5">
                  <Label className="text-[11px] text-muted-foreground">WASAPI 출력 장치</Label>
                  <Select
                    value={wasapiDeviceId || "__default__"}
                    disabled={isAudioRunning}
                    onValueChange={(v) => setWasapiDeviceId(v === "__default__" ? "" : v)}
                  >
                    <SelectTrigger className="h-8 text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {wasapiDevices.map((d) => (
                        <SelectItem key={d.id || "__default__"} value={d.id || "__default__"}>
                          {d.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <div className="flex items-center justify-between rounded-md border border-border bg-secondary/40 px-3 py-2">
                  <Label className="text-xs">WASAPI 단독 모드 (초저지연)</Label>
                  <Switch
                    checked={wasapiExclusive}
                    disabled={isAudioRunning}
                    onCheckedChange={setWasapiExclusive}
                  />
                </div>
              </div>
            )}

            {outputKind === "ASIO" && (
              <div className="space-y-1.5">
                <Label className="text-[11px] text-muted-foreground">ASIO 출력 드라이버</Label>
                <Select
                  value={asioOutDriver || "__none__"}
                  disabled={isAudioRunning}
                  onValueChange={(v) => v !== "__none__" && setAsioOutDriver(v)}
                >
                  <SelectTrigger className="h-8 text-xs">
                    <SelectValue placeholder="출력 드라이버 선택" />
                  </SelectTrigger>
                  <SelectContent>
                    {asioDrivers.map((d) => (
                      <SelectItem key={d} value={d}>
                        {d}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}

            <div className="space-y-1.5">
              <Label className="text-[11px] text-muted-foreground">지연 프리셋</Label>
              <Select
                value={String(presetIndex)}
                disabled={isAudioRunning}
                onValueChange={(v) => setPresetIndex(Number(v))}
              >
                <SelectTrigger className="h-8 text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {presets.map((p) => (
                    <SelectItem key={p.index} value={String(p.index)}>
                      {p.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            {/* External MIDI Controller & Foot Pedal Section */}
            <div className="rounded-md border border-border bg-secondary/35 p-3 space-y-2">
              <div className="flex items-center justify-between">
                <Label className="text-[11px] font-semibold text-foreground">
                  외부 MIDI 컨트롤러 / 익스프레션 페달
                </Label>
                <Badge variant={midiState.connected ? "default" : "outline"} className="text-[10px] font-mono">
                  {midiState.connected ? `연결됨 (${midiState.bindings.length} 매핑)` : "미연결"}
                </Badge>
              </div>
              <div className="flex items-center gap-2">
                <Select
                  value={midiState.activeDevice || "__none__"}
                  onValueChange={(v) => handleSelectMidiDevice(v)}
                >
                  <SelectTrigger className="h-8 text-xs flex-1">
                    <SelectValue placeholder="MIDI 입력 포트 선택" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__none__">사용 안 함</SelectItem>
                    {midiState.devices.map((m) => (
                      <SelectItem key={m} value={m}>
                        {m}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <Button
                  size="sm"
                  variant="secondary"
                  className="h-8 text-[11px]"
                  onClick={() => handleStartMidiLearn({ targetKind: "tunerMute" })}
                >
                  튜너 뮤트 학습
                </Button>
              </div>
              <p className="text-[10px] text-muted-foreground">
                스튜디오 화면에서 아무 노브(와우 페달 등), Scene A–H, 블록 전원, TAP 버튼을 우클릭하면 즉시 MIDI CC를 학습합니다.
              </p>
            </div>

            <div className="pt-2 flex justify-end gap-2">
              <Button variant="ghost" size="sm" onClick={() => setAudioDialogOpen(false)}>
                닫기
              </Button>
              <Button
                variant={isAudioRunning ? "destructive" : "default"}
                size="sm"
                disabled={isAudioBusy}
                onClick={handleToggleAudio}
              >
                {isAudioRunning ? "모니터링 중지" : "모니터링 시작"}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>

      {/* ── Studio Chromatic Tuner Dialog ── */}
      <TunerDialog
        open={tunerDialogOpen}
        onOpenChange={setTunerDialogOpen}
        tuner={tuner}
        isAudioRunning={isAudioRunning}
        isAudioBusy={isAudioBusy}
        hasGp100={asioDrivers.some((d) => /valeton|gp-?100/i.test(d))}
        tunerMute={tunerMute}
        refA4Hz={tunerRefA4Hz}
        onTunerConfigChange={handleTunerConfigChange}
        onStartAudio={handleToggleAudio}
        onQuickStartGp100={() =>
          handleQuickStartGp100(outputKind === "ASIO_DUPLEX" ? "ASIO_DUPLEX" : "WASAPI")
        }
        onOpenAudioSettings={() => setAudioDialogOpen(true)}
      />

      {/* ── Guitar Analyzer & Profile Manager Dialog ── */}
      <GuitarAnalyzerDialog
        open={guitarDialogOpen}
        onOpenChange={setGuitarDialogOpen}
        isAudioRunning={isAudioRunning}
        isAudioBusy={isAudioBusy}
        hasGp100={asioDrivers.some((d) => /valeton|gp-?100/i.test(d))}
        onStartAudio={handleToggleAudio}
        onQuickStartGp100={() =>
          handleQuickStartGp100(outputKind === "ASIO_DUPLEX" ? "ASIO_DUPLEX" : "WASAPI")
        }
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

      {/* ── Logs Dialog ── */}
      <Dialog open={logsOpen} onOpenChange={setLogsOpen}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle>NeuralRig Studio 로그</DialogTitle>
          </DialogHeader>
          <ScrollArea className="h-[360px] rounded-md border border-border bg-muted/40 p-3 font-mono text-xs">
            {logs.length === 0 ? (
              <div className="text-muted-foreground">기록된 로그가 없습니다.</div>
            ) : (
              <div className="space-y-1">
                {logs.map((entry) => (
                  <div
                    key={entry.id}
                    className={
                      entry.level === "error"
                        ? "text-destructive"
                        : entry.level === "success"
                          ? "text-emerald-400"
                          : "text-foreground/85"
                    }
                  >
                    {entry.text}
                  </div>
                ))}
              </div>
            )}
          </ScrollArea>
        </DialogContent>
      </Dialog>
    </div>
  )
}
