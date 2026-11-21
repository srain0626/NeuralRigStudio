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
  const [presets, setPresets] = useState<PresetDef[]>([
    { index: 0, name: "극한 (링 4ms / WASAPI 8~12ms)", ringMs: 4, sharedMs: 12, exclMs: 8 },
    { index: 1, name: "최소 (링 8ms / WASAPI 10~15ms)", ringMs: 8, sharedMs: 15, exclMs: 10 },
    { index: 2, name: "균형 (링 16ms / WASAPI 15~25ms)", ringMs: 16, sharedMs: 25, exclMs: 15 },
    { index: 3, name: "안정 (링 32ms / WASAPI 25~50ms)", ringMs: 32, sharedMs: 50, exclMs: 25 },
  ])

  const [inputDriver, setInputDriver] = useState("")
  const [channelMode, setChannelMode] = useState(1)
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
          setAsioDrivers(snap.audio.asioDrivers || [])
          setWasapiDevices(snap.audio.wasapiDevices || [{ id: "", label: "시스템 기본 출력 장치" }])
          if (snap.presets?.length) setPresets(snap.presets)
          if (snap.audio.initialCaps) setCaps(snap.audio.initialCaps)

          const s = snap.settings
          setInputDriver(snap.audio.initialDriver || s.inputDriver || "")
          setChannelMode(s.channelMode ?? 1)
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
          setIsAudioRunning(snap.isAudioRunning)
        })
        .catch((e) => addLog(`초기화 오류: ${String(e)}`, "error"))
    }

    return () => {
      unsubLog()
      unsubAudio()
      unsubTones()
      unsubGuitars()
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
        initialDriver?: string
        initialCaps?: AsioCapabilitiesDto | null
      }>("refreshAudioDevices")
      const drivers = res.asioDrivers || []
      setAsioDrivers(drivers)
      setWasapiDevices(res.wasapiDevices || [])

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
    if (audioDialogOpen && !isAudioRunning && studioBridge.isWebView2) {
      handleRefreshDevices()
    }
  }, [audioDialogOpen])

  const handleQuickStartGp100 = async (mode: "WASAPI" | "ASIO_DUPLEX") => {
    if (!studioBridge.isWebView2 || isAudioBusy) return
    const gpDriver = asioDrivers.find((d) => /valeton|gp-?100/i.test(d)) || "Valeton USB Audio Device"
    setInputDriver(gpDriver)
    setChannelMode(1)
    setOutputKind(mode)
    setPresetIndex(1)
    addLog(`[GP-100] Valeton GP-100 원클릭 연결 시작 (${mode === "ASIO_DUPLEX" ? "GP-100 헤드폰 직결" : "PC 스피커/헤드셋"})…`, "info")
    try {
      if (isAudioRunning) {
        await studioBridge.invoke("stopMonitoring")
      }
      const res = await studioBridge.invoke<{ isRunning: boolean; inputDriver?: string }>("startMonitoring", {
        inputDriver: gpDriver,
        channelMode: 1,
        asioInBufferSize: bufferSize,
        outputKind: mode,
        wasapiExclusive,
        wasapiDeviceId,
        asioOutDriver,
        presetIndex: 1,
      })
      if (res?.inputDriver) setInputDriver(res.inputDriver)
      setAudioDialogOpen(false)
      addLog(`[GP-100] 연결 완료 (${gpDriver})`, "success")
    } catch (e) {
      addLog(`GP-100 연결 실패: ${String(e)}`, "error")
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
    if (!inputDriver) {
      setAudioDialogOpen(true)
      return
    }
    try {
      const res = await studioBridge.invoke<{ isRunning: boolean; inputDriver?: string }>("startMonitoring", {
        inputDriver,
        channelMode,
        asioInBufferSize: bufferSize,
        outputKind,
        wasapiExclusive,
        wasapiDeviceId,
        asioOutDriver,
        presetIndex,
      })
      if (res?.inputDriver) setInputDriver(res.inputDriver)
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

        {/* Right: Tuner + Live Audio Engine Control + Audio Settings + Logs */}
        <div className="flex items-center gap-1.5 justify-end min-w-[180px]">
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
              variant={isAudioRunning && /valeton|gp-?100/i.test(inputDriver) ? "default" : "secondary"}
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
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>오디오 I/O 설정 (ASIO / WASAPI)</DialogTitle>
          </DialogHeader>

          <div className="space-y-4 text-xs">
            {/* Valeton GP-100 Quick Setup Banner */}
            <div className="rounded-md border border-border bg-secondary/45 p-3 space-y-2">
              <div className="flex items-center justify-between gap-2">
                <span className="text-[11px] font-semibold text-foreground flex items-center gap-1.5">
                  <Usb className="w-3.5 h-3.5" />
                  Valeton GP-100 빠른 연결
                </span>
                <Badge variant={asioDrivers.some((d) => /valeton|gp-?100/i.test(d)) ? "default" : "outline"} className="text-[10px] font-mono">
                  {asioDrivers.some((d) => /valeton|gp-?100/i.test(d)) ? "GP-100 감지됨" : "USB 연결 대기"}
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
                  GP-100 → PC 스피커/헤드셋 (권장)
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  className="h-7 text-[11px]"
                  disabled={isAudioBusy}
                  onClick={() => handleQuickStartGp100("ASIO_DUPLEX")}
                >
                  GP-100 → GP-100 헤드폰 단자 직결
                </Button>
              </div>
              <p className="text-[11px] text-muted-foreground leading-snug">
                기타 입력을 위해 모노 In 1 (좌→양쪽)이 자동 선택됩니다. 순수 앰프·이펙터 톤을 위해 GP-100 본체는 빈 패치(바이패스) 또는 USB Dry 입력을 권장합니다.
              </p>
            </div>

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
              </div>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label className="text-[11px] text-muted-foreground">ASIO 입력 드라이버</Label>
                <Select
                  value={inputDriver || "__none__"}
                  disabled={isAudioRunning}
                  onValueChange={(v) => {
                    if (v === "__none__") return
                    setInputDriver(v)
                    probeAsioCaps(v)
                  }}
                >
                  <SelectTrigger className="h-8 text-xs">
                    <SelectValue placeholder="드라이버 선택" />
                  </SelectTrigger>
                  <SelectContent>
                    {asioDrivers.length === 0 ? (
                      <SelectItem value="__none__">감지된 ASIO 없음</SelectItem>
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
                <Label className="text-[11px] text-muted-foreground">입력 채널 모드</Label>
                <Select
                  value={String(channelMode)}
                  disabled={isAudioRunning}
                  onValueChange={(v) => setChannelMode(Number(v))}
                >
                  <SelectTrigger className="h-8 text-xs">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="0">스테레오 (In 1 + In 2)</SelectItem>
                    <SelectItem value="1">모노 In 1 (좌→양쪽)</SelectItem>
                    <SelectItem value="2">모노 In 2 (우→양쪽)</SelectItem>
                  </SelectContent>
                </Select>
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
                    <SelectItem value="ASIO_DUPLEX">ASIO 직결 풀듀플렉스</SelectItem>
                    <SelectItem value="ASIO">별도 ASIO 출력</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="space-y-1.5">
                <Label className="text-[11px] text-muted-foreground">버퍼 크기</Label>
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
