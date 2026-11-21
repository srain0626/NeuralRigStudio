import React, { useEffect, useState } from "react"
import {
  Cpu,
  Disc3,
  Download,
  Flame,
  FolderOpen,
  Headphones,
  Music,
  Pause,
  Play,
  Repeat,
  RotateCcw,
  Sliders,
  Square,
  Trash2,
  Upload,
  Volume2,
  Wrench,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Slider } from "@/components/ui/slider"
import { Switch } from "@/components/ui/switch"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { VisualizerCanvas } from "@/components/VisualizerCanvas"
import { studioBridge } from "@/lib/bridge"
import {
  DEFAULT_WHITEBOX_PARAMS,
  type AsioCapabilitiesDto,
  type BackingTrackStateDto,
  type ModelProfileSummary,
  type PresetDef,
  type TelemetryFrame,
  type WasapiDeviceDto,
  type WhiteBoxChannel,
  type WhiteBoxParamsDto,
} from "@/types/rig"

interface LiveRigStudioProps {
  asioDrivers: string[]
  wasapiDevices: WasapiDeviceDto[]
  presets: PresetDef[]
  tones: ModelProfileSummary[]
  selectedToneId: string
  initialCustomCabIrName?: string
  initialWhiteBox?: WhiteBoxParamsDto
  initialBacking?: BackingTrackStateDto
  initialConfig: {
    inputDriver: string
    channelMode: number
    asioInBufferSize: number
    outputKind: string
    wasapiExclusive: boolean
    wasapiDeviceId: string
    asioOutDriver: string
    presetIndex: number
    toneMode: number
    toneDrivePct: number
    toneMixPct: number
    toneGainDb: number
    toneGateDb: number
  }
  onTonesUpdated: (tones: ModelProfileSummary[], selectedId: string) => void
  onSelectToneId: (id: string) => void
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

export const LiveRigStudio: React.FC<LiveRigStudioProps> = ({
  asioDrivers,
  wasapiDevices,
  presets,
  tones,
  selectedToneId,
  initialCustomCabIrName = "",
  initialWhiteBox,
  initialBacking,
  initialConfig,
  onTonesUpdated,
  onSelectToneId,
  onLog,
}) => {
  const [inputDriver, setInputDriver] = useState(initialConfig.inputDriver)
  const [channelMode, setChannelMode] = useState(initialConfig.channelMode)
  const [bufferSize, setBufferSize] = useState(initialConfig.asioInBufferSize)
  const [outputKind, setOutputKind] = useState(initialConfig.outputKind || "WASAPI")
  const [wasapiExclusive, setWasapiExclusive] = useState(initialConfig.wasapiExclusive)
  const [wasapiDeviceId, setWasapiDeviceId] = useState(initialConfig.wasapiDeviceId)
  const [asioOutDriver, setAsioOutDriver] = useState(initialConfig.asioOutDriver)
  const [presetIndex, setPresetIndex] = useState(initialConfig.presetIndex)
  const [caps, setCaps] = useState<AsioCapabilitiesDto | null>(null)

  const [isRunning, setIsRunning] = useState(false)
  const [isBusy, setIsBusy] = useState(false)
  const [statusLabel, setStatusLabel] = useState("대기 중")
  const [latencyInfo, setLatencyInfo] = useState<TelemetryFrame["latency"]>(null)
  const [counters, setCounters] = useState<TelemetryFrame["counters"]>({
    padded: 0,
    dropped: 0,
    lost: 0,
    sampleRate: 44100,
  })

  const [toneMode, setToneMode] = useState(initialConfig.toneMode)
  const [drivePct, setDrivePct] = useState(initialConfig.toneDrivePct)
  const [mixPct, setMixPct] = useState(initialConfig.toneMixPct)
  const [gainDb, setGainDb] = useState(initialConfig.toneGainDb)
  const [gateDb, setGateDb] = useState(initialConfig.toneGateDb)
  const [customCabIrName, setCustomCabIrName] = useState(initialCustomCabIrName)
  const [whiteBox, setWhiteBox] = useState<WhiteBoxParamsDto>(initialWhiteBox || DEFAULT_WHITEBOX_PARAMS)

  const [backing, setBacking] = useState<BackingTrackStateDto>(
    initialBacking || {
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

  useEffect(() => {
    setInputDriver(initialConfig.inputDriver)
    setChannelMode(initialConfig.channelMode)
    setBufferSize(initialConfig.asioInBufferSize)
    setOutputKind(initialConfig.outputKind || "WASAPI")
    setWasapiExclusive(initialConfig.wasapiExclusive)
    setWasapiDeviceId(initialConfig.wasapiDeviceId)
    setAsioOutDriver(initialConfig.asioOutDriver)
    setPresetIndex(initialConfig.presetIndex)
    setToneMode(initialConfig.toneMode)
    setDrivePct(initialConfig.toneDrivePct)
    setMixPct(initialConfig.toneMixPct)
    setGainDb(initialConfig.toneGainDb)
    setGateDb(initialConfig.toneGateDb)
  }, [initialConfig])

  useEffect(() => {
    if (initialCustomCabIrName !== undefined) setCustomCabIrName(initialCustomCabIrName)
  }, [initialCustomCabIrName])

  useEffect(() => {
    if (initialWhiteBox) setWhiteBox(initialWhiteBox)
  }, [initialWhiteBox])

  useEffect(() => {
    if (initialBacking) setBacking(initialBacking)
  }, [initialBacking])

  useEffect(() => {
    const unsubAudio = studioBridge.on<{ isRunning: boolean; busy: boolean; state: string; error?: string }>(
      "audioState",
      (ev) => {
        setIsRunning(ev.isRunning)
        setIsBusy(ev.busy)
        setStatusLabel(ev.state)
        if (ev.error) onLog(`오디오 중지: ${ev.error}`, "error")
      }
    )

    let lastStatsTs = 0
    const unsubTel = studioBridge.on<TelemetryFrame>("telemetry", (f) => {
      const now = performance.now()
      if (now - lastStatsTs > 110) {
        lastStatsTs = now
        setIsRunning(f.running)
        setIsBusy(f.busy)
        setLatencyInfo(f.latency)
        setCounters(f.counters)
        if (f.backing) setBacking(f.backing)
      }
    })

    return () => {
      unsubAudio()
      unsubTel()
    }
  }, [])

  useEffect(() => {
    if (!inputDriver || isRunning || !studioBridge.isWebView2) return
    studioBridge
      .invoke<AsioCapabilitiesDto>("getAsioCaps", { driver: inputDriver })
      .then((c) => setCaps(c))
      .catch(() => {})
  }, [inputDriver, isRunning])

  const activeTone = tones.find((t) => t.id === selectedToneId) || null
  const asioOtherDrivers = asioDrivers.filter((d) => d.toLowerCase() !== inputDriver.toLowerCase())

  const pushWhiteBox = (patch: Partial<WhiteBoxParamsDto>) => {
    const next = { ...whiteBox, ...patch }
    setWhiteBox(next)
    studioBridge.invoke("updateWhiteBoxParams", next).catch(() => {})
  }

  const pushToneParams = (
    next: Partial<{ mode: number; drivePct: number; mixPct: number; gainDb: number; gateDb: number }>
  ) => {
    const payload = {
      mode: next.mode ?? toneMode,
      drivePct: next.drivePct ?? drivePct,
      mixPct: next.mixPct ?? mixPct,
      gainDb: next.gainDb ?? gainDb,
      gateDb: next.gateDb ?? gateDb,
    }
    studioBridge.invoke("updateToneParams", payload).catch(() => {})
  }

  const toggleMonitoring = async () => {
    if (isBusy) return
    if (isRunning) {
      await studioBridge.invoke("stopMonitoring")
      return
    }
    setIsBusy(true)
    try {
      await studioBridge.invoke("startMonitoring", {
        inputDriver,
        channelMode,
        asioInBufferSize: bufferSize,
        outputKind,
        wasapiExclusive,
        wasapiDeviceId,
        asioOutDriver: asioOutDriver || asioOtherDrivers[0] || "",
        presetIndex,
      })
      onLog("초저지연 ASIO 모니터링 시작됨", "success")
    } catch (e: unknown) {
      onLog(`모니터 시작 실패: ${e instanceof Error ? e.message : String(e)}`, "error")
    } finally {
      setIsBusy(false)
    }
  }

  const handleOpenControlPanel = async () => {
    try {
      await studioBridge.invoke("openAsioPanel", { driver: inputDriver })
    } catch (e: unknown) {
      onLog(e instanceof Error ? e.message : String(e), "error")
    }
  }

  const handleSelectTone = async (id: string) => {
    const cleanId = id === "__none__" ? "" : id
    onSelectToneId(cleanId)
    try {
      const res = await studioBridge.invoke<{ toneMode?: number }>("selectTone", { id: cleanId })
      if (typeof res?.toneMode === "number") setToneMode(res.toneMode)
    } catch (e: unknown) {
      onLog(String(e), "error")
    }
  }

  const handleImportNam = async () => {
    try {
      const res = await studioBridge.invoke<{
        cancelled?: boolean
        profile?: ModelProfileSummary
        tones?: ModelProfileSummary[]
        selectedToneId?: string
        toneMode?: number
      }>("importNamFile", {})
      if (!res.cancelled && res.profile) {
        onTonesUpdated(res.tones || [], res.selectedToneId || res.profile.id)
        if (typeof res.toneMode === "number") setToneMode(res.toneMode)
        onLog(`.nam 모델 로드 완료: ${res.profile.name}`, "success")
      }
    } catch (e: unknown) {
      onLog(e instanceof Error ? e.message : String(e), "error")
    }
  }

  const handleExportNam = async () => {
    if (!activeTone) return
    try {
      const res = await studioBridge.invoke<{ cancelled?: boolean; path?: string }>("exportNamFile", {
        toneId: activeTone.id,
      })
      if (!res.cancelled && res.path) onLog(`.nam 저장 완료: ${res.path}`, "success")
    } catch (e: unknown) {
      onLog(e instanceof Error ? e.message : String(e), "error")
    }
  }

  const handleDeleteTone = async () => {
    if (!activeTone) return
    try {
      const res = await studioBridge.invoke<{
        tones: ModelProfileSummary[]
        selectedToneId: string
        toneMode?: number
      }>("deleteTone", { id: activeTone.id })
      onTonesUpdated(res.tones, res.selectedToneId)
      if (typeof res.toneMode === "number") setToneMode(res.toneMode)
      onLog(`모델 '${activeTone.name}' 삭제됨`)
    } catch (e: unknown) {
      onLog(String(e), "error")
    }
  }

  const handleLoadCustomCabIr = async () => {
    try {
      const res = await studioBridge.invoke<{
        cancelled?: boolean
        loaded: boolean
        customCabIrName: string
        taps?: number
      }>("loadCustomCabIr", {})
      if (!res.cancelled) {
        setCustomCabIrName(res.customCabIrName || "")
        onLog(`외부 캐비넷 IR 로드됨: ${res.customCabIrName} (${res.taps} taps)`, "success")
      }
    } catch (e: unknown) {
      onLog(e instanceof Error ? e.message : String(e), "error")
    }
  }

  const handleClearCustomCabIr = async () => {
    try {
      await studioBridge.invoke("clearCustomCabIr")
      setCustomCabIrName("")
      onLog("외부 캐비넷 IR 해제 (내장 4×12 V30 IR 복귀)")
    } catch (e: unknown) {
      onLog(e instanceof Error ? e.message : String(e), "error")
    }
  }

  const handleLoadBacking = async () => {
    try {
      const res = await studioBridge.invoke<{ cancelled?: boolean; backing: BackingTrackStateDto }>(
        "loadBackingTrack",
        {}
      )
      if (!res.cancelled && res.backing) setBacking(res.backing)
    } catch (e: unknown) {
      onLog(`MR 로드 실패: ${e instanceof Error ? e.message : String(e)}`, "error")
    }
  }

  const handleControlBacking = async (patch: {
    playing?: boolean
    loop?: boolean
    volume?: number
    seekSec?: number
  }) => {
    try {
      const res = await studioBridge.invoke<{ backing: BackingTrackStateDto }>("controlBackingTrack", patch)
      if (res?.backing) setBacking(res.backing)
    } catch (e: unknown) {
      onLog(e instanceof Error ? e.message : String(e), "error")
    }
  }

  const handleUnloadBacking = async () => {
    try {
      const res = await studioBridge.invoke<{ backing: BackingTrackStateDto }>("unloadBackingTrack")
      if (res?.backing) setBacking(res.backing)
    } catch (e: unknown) {
      onLog(e instanceof Error ? e.message : String(e), "error")
    }
  }

  const bufferList = caps?.bufferSizes?.length ? caps.bufferSizes : [64, 128, 256, 512, 0]
  const sampleRate = caps?.sampleRate || counters.sampleRate || 44100

  return (
    <div className="grid grid-cols-1 lg:grid-cols-12 gap-4">
      {/* ── Left Column: Audio Router & MR Backing Track Mixer ── */}
      <div className="lg:col-span-5 space-y-4">
        <Card>
          <CardHeader className="pb-3 flex flex-row items-center justify-between space-y-0">
            <CardTitle className="text-base flex items-center gap-2">
              <Headphones className="w-4 h-4 text-primary" />
              오디오 라우터
            </CardTitle>
            <Badge
              variant="outline"
              className={
                isRunning
                  ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-300 text-xs"
                  : "text-muted-foreground text-xs"
              }
            >
              {isRunning ? "ON AIR" : "IDLE"}
            </Badge>
          </CardHeader>
          <CardContent className="space-y-3">
            {/* Input Driver + Control Panel */}
            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <Label className="text-xs">ASIO 입력 드라이버</Label>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-6 px-2 text-[11px] text-primary"
                  disabled={!inputDriver || isRunning}
                  onClick={handleOpenControlPanel}
                >
                  <Wrench className="w-3 h-3 mr-1" />
                  ASIO 제어판
                </Button>
              </div>
              <Select
                disabled={isRunning || isBusy}
                value={inputDriver || "__empty__"}
                onValueChange={(v) => v !== "__empty__" && setInputDriver(v)}
              >
                <SelectTrigger className="h-9">
                  <SelectValue placeholder="ASIO 드라이버 없음" />
                </SelectTrigger>
                <SelectContent>
                  {asioDrivers.length === 0 ? (
                    <SelectItem value="__empty__">ASIO 드라이버 미감지</SelectItem>
                  ) : (
                    asioDrivers.map((d) => (
                      <SelectItem key={d} value={d}>{d}</SelectItem>
                    ))
                  )}
                </SelectContent>
              </Select>
            </div>

            {/* Channel Mode & Buffer Size */}
            <div className="grid grid-cols-2 gap-2.5">
              <div className="space-y-1.5">
                <Label className="text-xs">입력 채널</Label>
                <Tabs
                  value={String(channelMode)}
                  onValueChange={(v) => !isRunning && !isBusy && setChannelMode(Number(v))}
                >
                  <TabsList className="grid w-full grid-cols-3 h-9">
                    <TabsTrigger value="0" disabled={isRunning} className="text-xs">스테레오</TabsTrigger>
                    <TabsTrigger value="1" disabled={isRunning} className="text-xs">모노 L</TabsTrigger>
                    <TabsTrigger value="2" disabled={isRunning} className="text-xs">모노 R</TabsTrigger>
                  </TabsList>
                </Tabs>
              </div>

              <div className="space-y-1.5">
                <Label className="text-xs">ASIO 입력 버퍼</Label>
                <Select
                  disabled={isRunning || isBusy}
                  value={String(bufferSize)}
                  onValueChange={(v) => setBufferSize(Number(v))}
                >
                  <SelectTrigger className="h-9">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {bufferList.map((sz) => (
                      <SelectItem key={sz} value={String(sz)}>
                        {sz <= 0
                          ? "드라이버 기본값"
                          : `${sz} 샘플 (${((sz * 1000) / Math.max(1, sampleRate)).toFixed(1)} ms)`}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>

            {/* Output Type & WASAPI Mode */}
            <div className="grid grid-cols-1 sm:grid-cols-12 gap-2.5">
              <div className="sm:col-span-7 space-y-1.5">
                <Label className="text-xs">출력 엔진</Label>
                <Tabs
                  value={outputKind}
                  onValueChange={(v) => !isRunning && !isBusy && setOutputKind(v)}
                >
                  <TabsList className="grid w-full grid-cols-3 h-9">
                    <TabsTrigger value="WASAPI" disabled={isRunning} className="text-[11px] px-1.5">
                      WASAPI
                    </TabsTrigger>
                    <TabsTrigger
                      value="ASIO_DUPLEX"
                      disabled={isRunning || (caps !== null && (caps.outputChannelsCount ?? 0) === 0)}
                      className="text-[11px] px-1.5"
                    >
                      ASIO 직결
                    </TabsTrigger>
                    <TabsTrigger
                      value="ASIO"
                      disabled={isRunning || asioOtherDrivers.length === 0}
                      className="text-[11px] px-1.5"
                    >
                      제2 ASIO
                    </TabsTrigger>
                  </TabsList>
                </Tabs>
              </div>

              <div className="sm:col-span-5 space-y-1.5">
                <Label className="text-xs">WASAPI 모드</Label>
                <Tabs
                  value={wasapiExclusive ? "exclusive" : "shared"}
                  onValueChange={(v) =>
                    !isRunning && !isBusy && outputKind === "WASAPI" && setWasapiExclusive(v === "exclusive")
                  }
                >
                  <TabsList className="grid w-full grid-cols-2 h-9">
                    <TabsTrigger value="shared" disabled={isRunning || outputKind !== "WASAPI"} className="text-[11px]">
                      공유
                    </TabsTrigger>
                    <TabsTrigger value="exclusive" disabled={isRunning || outputKind !== "WASAPI"} className="text-[11px]">
                      단독
                    </TabsTrigger>
                  </TabsList>
                </Tabs>
              </div>
            </div>

            {/* Output Device */}
            <div className="space-y-1.5">
              <Label className="text-xs">출력 장치</Label>
              {outputKind === "ASIO_DUPLEX" ? (
                <div className="h-9 rounded-md border border-emerald-500/35 bg-emerald-500/10 px-3 flex items-center justify-between text-xs">
                  <span className="text-emerald-300 font-medium truncate">
                    풀듀플렉스 직결: {inputDriver || "ASIO"} Out 1/2
                  </span>
                  <span className="font-mono text-[10px] text-emerald-400 shrink-0">0-Ring</span>
                </div>
              ) : outputKind === "WASAPI" ? (
                <Select
                  disabled={isRunning || isBusy}
                  value={wasapiDeviceId || "__default__"}
                  onValueChange={(v) => setWasapiDeviceId(v === "__default__" ? "" : v)}
                >
                  <SelectTrigger className="h-9">
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
              ) : (
                <Select
                  disabled={isRunning || isBusy}
                  value={asioOutDriver || asioOtherDrivers[0] || "__empty__"}
                  onValueChange={(v) => v !== "__empty__" && setAsioOutDriver(v)}
                >
                  <SelectTrigger className="h-9">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {asioOtherDrivers.length === 0 ? (
                      <SelectItem value="__empty__">다른 ASIO 드라이버 없음</SelectItem>
                    ) : (
                      asioOtherDrivers.map((d) => (
                        <SelectItem key={d} value={d}>{d}</SelectItem>
                      ))
                    )}
                  </SelectContent>
                </Select>
              )}
            </div>

            {/* Latency Preset */}
            <div className="space-y-1.5">
              <Label className="text-xs">지연시간 프리셋</Label>
              <Select
                disabled={isRunning || isBusy || outputKind === "ASIO_DUPLEX"}
                value={String(presetIndex)}
                onValueChange={(v) => setPresetIndex(Number(v))}
              >
                <SelectTrigger className="h-9">
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

            <Button
              className="w-full h-10 font-semibold"
              variant={isRunning ? "destructive" : "default"}
              disabled={isBusy || (!isRunning && asioDrivers.length === 0)}
              onClick={toggleMonitoring}
            >
              {isRunning ? (
                <>
                  <Square className="w-4 h-4 mr-2" />
                  모니터링 중지
                </>
              ) : (
                <>
                  <Play className="w-4 h-4 mr-2" />
                  {isBusy ? "오디오 엔진 초기화 중…" : "초저지연 모니터링 시작"}
                </>
              )}
            </Button>

            {/* Telemetry Bar */}
            <div className="rounded-md bg-muted/50 border border-border px-3 py-2 text-xs space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-muted-foreground">{statusLabel}</span>
                {latencyInfo ? (
                  <span className="font-mono text-emerald-400 font-semibold">
                    총 {latencyInfo.totalMs.toFixed(1)} ms ({latencyInfo.inMs.toFixed(1)} + {latencyInfo.ringMs.toFixed(1)} + {latencyInfo.outMs.toFixed(1)})
                  </span>
                ) : (
                  <span className="font-mono text-muted-foreground">-- ms</span>
                )}
              </div>
              <div className="flex justify-between font-mono text-[10px] text-muted-foreground">
                <span>{counters.sampleRate} Hz</span>
                <span>
                  pad:{counters.padded} drop:{counters.dropped} lost:{counters.lost}
                </span>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Lock-Free MR Backing Track Player */}
        <Card>
          <CardHeader className="pb-2.5 flex flex-row items-center justify-between space-y-0">
            <CardTitle className="text-sm flex items-center gap-2">
              <Music className="w-4 h-4 text-primary" />
              백킹 트랙 (MR) 합주
            </CardTitle>
            <Button
              type="button"
              size="sm"
              variant="outline"
              className="h-7 text-[11px] px-2.5"
              onClick={handleLoadBacking}
            >
              <FolderOpen className="w-3 h-3 mr-1" />
              파일 열기
            </Button>
          </CardHeader>
          <CardContent className="space-y-2.5">
            <div className="flex items-center justify-between text-xs">
              <span className="truncate font-medium text-foreground">
                {backing.loaded ? backing.name : "로드된 오디오 없음"}
              </span>
              <span className="font-mono text-[11px] text-muted-foreground shrink-0">
                {backing.loaded
                  ? `${backing.positionSec.toFixed(1)}s / ${backing.durationSec.toFixed(1)}s`
                  : "--:--"}
              </span>
            </div>

            {backing.loaded && (
              <div className="flex items-center gap-2">
                <Button
                  type="button"
                  size="icon"
                  variant={backing.playing ? "secondary" : "default"}
                  className="h-8 w-8 shrink-0"
                  onClick={() => handleControlBacking({ playing: !backing.playing })}
                >
                  {backing.playing ? <Pause className="w-3.5 h-3.5" /> : <Play className="w-3.5 h-3.5" />}
                </Button>
                <Button
                  type="button"
                  size="icon"
                  variant={backing.loop ? "secondary" : "ghost"}
                  className="h-8 w-8 shrink-0"
                  onClick={() => handleControlBacking({ loop: !backing.loop })}
                  title="반복 재생"
                >
                  <Repeat className="w-3.5 h-3.5" />
                </Button>
                <div className="flex-1 flex items-center gap-2">
                  <Volume2 className="w-3.5 h-3.5 text-muted-foreground shrink-0" />
                  <Slider
                    value={[Math.round(backing.volume * 100)]}
                    min={0}
                    max={120}
                    step={2}
                    onValueChange={([v]) => handleControlBacking({ volume: v / 100 })}
                  />
                  <span className="text-[11px] font-mono text-muted-foreground w-9 text-right">
                    {Math.round(backing.volume * 100)}%
                  </span>
                </div>
                <Button
                  type="button"
                  size="icon"
                  variant="ghost"
                  className="h-8 w-8 shrink-0"
                  onClick={handleUnloadBacking}
                  title="MR 언로드"
                >
                  <Trash2 className="w-3.5 h-3.5 text-muted-foreground" />
                </Button>
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* ── Right Column: Visualizer + Neural Rig Engine + WhiteBox Tube Amp ── */}
      <div className="lg:col-span-7 space-y-4">
        <VisualizerCanvas activeToneLabel={activeTone ? activeTone.name : "WhiteBox 4× Rig"} />

        {/* Neural Rig Engine Card */}
        <Card>
          <CardHeader className="pb-3 flex flex-row items-center justify-between space-y-0">
            <CardTitle className="text-base flex items-center gap-2">
              <Cpu className="w-4 h-4 text-emerald-400" />
              뉴럴 리그 엔진
            </CardTitle>
            <Badge variant="secondary" className="text-[11px] font-mono">
              {activeTone
                ? activeTone.esrDb < 0
                  ? `WaveNet · ESR ${activeTone.esrDb.toFixed(1)} dB · ${activeTone.sampleRate} Hz`
                  : `WaveNet · ${(activeTone.archPreset || "NAM").toUpperCase()} · ${activeTone.sampleRate} Hz`
                : "내장 화이트박스 모드"}
            </Badge>
          </CardHeader>
          <CardContent className="space-y-4">
            {/* Model Select + .nam Import / Export / Delete + DSP Routing */}
            <div className="grid grid-cols-1 md:grid-cols-12 gap-2.5 items-end">
              <div className="md:col-span-5 space-y-1.5">
                <Label className="text-xs">.nam 뉴럴 모델</Label>
                <div className="flex gap-1">
                  <Select
                    value={activeTone?.id || "__none__"}
                    onValueChange={handleSelectTone}
                  >
                    <SelectTrigger className="h-9 flex-1 min-w-0">
                      <SelectValue placeholder=".nam 모델 없음" />
                    </SelectTrigger>
                    <SelectContent>
                      {tones.length === 0 ? (
                        <SelectItem value="__none__">불러온 .nam 모델 없음</SelectItem>
                      ) : (
                        tones.map((t) => (
                          <SelectItem key={t.id} value={t.id}>
                            {t.name} ({(t.archPreset || "NAM").toUpperCase()})
                          </SelectItem>
                        ))
                      )}
                    </SelectContent>
                  </Select>
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    className="h-9 px-2.5 shrink-0 text-xs"
                    onClick={handleImportNam}
                  >
                    <Upload className="w-3.5 h-3.5 mr-1" />
                    .nam 불러오기
                  </Button>
                  <Button
                    type="button"
                    size="icon"
                    variant="outline"
                    className="h-9 w-9 shrink-0"
                    disabled={!activeTone}
                    onClick={handleExportNam}
                    title=".nam 내보내기"
                  >
                    <Download className="w-4 h-4" />
                  </Button>
                  <Button
                    type="button"
                    size="icon"
                    variant="outline"
                    className="h-9 w-9 shrink-0"
                    disabled={!activeTone}
                    onClick={handleDeleteTone}
                    title="선택된 모델 삭제"
                  >
                    <Trash2 className="w-4 h-4 text-destructive" />
                  </Button>
                </div>
              </div>

              <div className="md:col-span-7 space-y-1.5">
                <Label className="text-xs">DSP 처리 경로</Label>
                <Tabs
                  value={String(toneMode)}
                  onValueChange={(v) => {
                    const m = Number(v)
                    setToneMode(m)
                    pushToneParams({ mode: m })
                  }}
                >
                  <TabsList className="grid w-full grid-cols-3 sm:grid-cols-6 h-auto sm:h-9 gap-0.5">
                    <TabsTrigger value="0" className="text-[11px] px-1.5">바이패스</TabsTrigger>
                    <TabsTrigger value="1" disabled={!activeTone} className="text-[11px] px-1.5">
                      NAM 풀리그
                    </TabsTrigger>
                    <TabsTrigger value="2" className="text-[11px] px-1.5">
                      캐비넷 IR
                    </TabsTrigger>
                    <TabsTrigger value="3" disabled={!activeTone} className="text-[11px] px-1.5">
                      NAM+외부IR
                    </TabsTrigger>
                    <TabsTrigger value="4" className="text-[11px] px-1.5">
                      화이트박스
                    </TabsTrigger>
                    <TabsTrigger value="5" className="text-[11px] px-1.5">
                      WB+NAM
                    </TabsTrigger>
                  </TabsList>
                </Tabs>
              </div>
            </div>

            {/* 4 Master Sliders */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-3 pt-1">
              <div className="space-y-1.5">
                <div className="flex justify-between text-xs">
                  <Label className="text-xs flex items-center gap-1">
                    <Sliders className="w-3 h-3" /> 드라이브 배율
                  </Label>
                  <span className="font-mono text-muted-foreground">{Math.round(drivePct)}%</span>
                </div>
                <Slider
                  value={[drivePct]}
                  min={20}
                  max={220}
                  step={1}
                  onValueChange={([v]) => {
                    setDrivePct(v)
                    pushToneParams({ drivePct: v })
                  }}
                />
              </div>

              <div className="space-y-1.5">
                <div className="flex justify-between text-xs">
                  <Label className="text-xs">Wet / Dry 믹스</Label>
                  <span className="font-mono text-muted-foreground">{Math.round(mixPct)}%</span>
                </div>
                <Slider
                  value={[mixPct]}
                  min={0}
                  max={100}
                  step={1}
                  onValueChange={([v]) => {
                    setMixPct(v)
                    pushToneParams({ mixPct: v })
                  }}
                />
              </div>

              <div className="space-y-1.5">
                <div className="flex justify-between text-xs">
                  <Label className="text-xs flex items-center gap-1">
                    <Volume2 className="w-3 h-3" /> 출력 게인
                  </Label>
                  <span className="font-mono text-muted-foreground">
                    {gainDb >= 0 ? `+${gainDb.toFixed(1)}` : gainDb.toFixed(1)} dB
                  </span>
                </div>
                <Slider
                  value={[gainDb]}
                  min={-12}
                  max={12}
                  step={0.5}
                  onValueChange={([v]) => {
                    setGainDb(v)
                    pushToneParams({ gainDb: v })
                  }}
                />
              </div>

              <div className="space-y-1.5">
                <div className="flex justify-between text-xs">
                  <Label className="text-xs">노이즈 게이트</Label>
                  <span className="font-mono text-muted-foreground">
                    {gateDb <= -78 ? "OFF" : `${Math.round(gateDb)} dB`}
                  </span>
                </div>
                <Slider
                  value={[gateDb]}
                  min={-80}
                  max={-28}
                  step={1}
                  onValueChange={([v]) => {
                    setGateDb(v)
                    pushToneParams({ gateDb: v })
                  }}
                />
              </div>
            </div>

            {/* External Cab IR Loader */}
            <div className="rounded-md border border-border bg-muted/40 p-2.5 flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="text-xs font-semibold text-foreground flex items-center gap-1.5">
                  <Disc3 className="w-3.5 h-3.5 text-amber-400 shrink-0" />
                  캐비넷 임펄스 응답 (IR)
                </div>
                <div className="text-[11px] font-mono text-muted-foreground truncate mt-0.5">
                  {customCabIrName || "내장 4×12 V30 (512-tap Minimum Phase)"}
                </div>
              </div>
              <div className="flex items-center gap-1.5 shrink-0">
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  className="h-8 text-xs px-2.5"
                  onClick={handleLoadCustomCabIr}
                >
                  <FolderOpen className="w-3.5 h-3.5 mr-1" />
                  IR 로드
                </Button>
                {customCabIrName && (
                  <Button
                    type="button"
                    size="icon"
                    variant="ghost"
                    className="h-8 w-8"
                    onClick={handleClearCustomCabIr}
                    title="외부 IR 해제"
                  >
                    <RotateCcw className="w-3.5 h-3.5 text-muted-foreground" />
                  </Button>
                )}
              </div>
            </div>
          </CardContent>
        </Card>

        {/* White-Box 4x Oversampled Tube Circuit & FMV Studio */}
        <Card>
          <CardHeader className="pb-3 flex flex-wrap flex-row items-center justify-between gap-2 space-y-0">
            <CardTitle className="text-base flex items-center gap-2">
              <Flame className="w-4 h-4 text-amber-400" />
              화이트박스 진공관 앰프 회로
              <Badge variant="outline" className="text-[10px] font-mono">
                4× HIIR OS
              </Badge>
            </CardTitle>
            <div className="flex flex-wrap items-center gap-3 text-xs">
              <label className="flex items-center gap-1.5 cursor-pointer">
                <Switch
                  checked={whiteBox.tsEnabled}
                  onCheckedChange={(v) => pushWhiteBox({ tsEnabled: v })}
                />
                <span>TS808 Boost</span>
              </label>
              <label className="flex items-center gap-1.5 cursor-pointer">
                <Switch
                  checked={whiteBox.bright}
                  onCheckedChange={(v) => pushWhiteBox({ bright: v })}
                />
                <span>Bright</span>
              </label>
              <label className="flex items-center gap-1.5 cursor-pointer">
                <Switch
                  checked={whiteBox.powerAmpEnabled}
                  onCheckedChange={(v) => pushWhiteBox({ powerAmpEnabled: v })}
                />
                <span>Power Amp</span>
              </label>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <Tabs
              value={String(whiteBox.channel)}
              onValueChange={(v) => pushWhiteBox({ channel: Number(v) as WhiteBoxChannel })}
            >
              <TabsList className="grid w-full grid-cols-3 h-9">
                <TabsTrigger value="0" className="text-xs">Clean (AB763)</TabsTrigger>
                <TabsTrigger value="1" className="text-xs">Plexi (1959SLP)</TabsTrigger>
                <TabsTrigger value="2" className="text-xs">High-Gain (SLO)</TabsTrigger>
              </TabsList>
            </Tabs>

            {whiteBox.tsEnabled && (
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-x-5 gap-y-2 rounded-md border border-border bg-muted/40 p-3">
                <div className="space-y-1.5">
                  <div className="flex justify-between text-xs">
                    <Label className="text-xs">TS808 Drive</Label>
                    <span className="font-mono text-muted-foreground">{whiteBox.tsDrive.toFixed(1)}</span>
                  </div>
                  <Slider
                    value={[whiteBox.tsDrive]}
                    min={0}
                    max={10}
                    step={0.1}
                    onValueChange={([v]) => pushWhiteBox({ tsDrive: v })}
                  />
                </div>
                <div className="space-y-1.5">
                  <div className="flex justify-between text-xs">
                    <Label className="text-xs">TS808 Tone</Label>
                    <span className="font-mono text-muted-foreground">{whiteBox.tsTone.toFixed(1)}</span>
                  </div>
                  <Slider
                    value={[whiteBox.tsTone]}
                    min={0}
                    max={10}
                    step={0.1}
                    onValueChange={([v]) => pushWhiteBox({ tsTone: v })}
                  />
                </div>
                <div className="space-y-1.5">
                  <div className="flex justify-between text-xs">
                    <Label className="text-xs">TS808 Level</Label>
                    <span className="font-mono text-muted-foreground">{whiteBox.tsLevel.toFixed(1)}</span>
                  </div>
                  <Slider
                    value={[whiteBox.tsLevel]}
                    min={0}
                    max={10}
                    step={0.1}
                    onValueChange={([v]) => pushWhiteBox({ tsLevel: v })}
                  />
                </div>
              </div>
            )}

            {/* 12AX7 Preamp + FMV Tone Stack */}
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-x-5 gap-y-3">
              {(
                [
                  ["Pre Gain", "preGain"],
                  ["Bass", "bass"],
                  ["Mid", "mid"],
                  ["Treble", "treble"],
                ] as const
              ).map(([lbl, key]) => (
                <div key={key} className="space-y-1.5">
                  <div className="flex justify-between text-xs">
                    <Label className="text-xs">{lbl}</Label>
                    <span className="font-mono text-muted-foreground">{whiteBox[key].toFixed(1)}</span>
                  </div>
                  <Slider
                    value={[whiteBox[key]]}
                    min={0}
                    max={10}
                    step={0.1}
                    onValueChange={([v]) => pushWhiteBox({ [key]: v })}
                  />
                </div>
              ))}
            </div>

            {/* Push-Pull Power Amp */}
            <div
              className={`grid grid-cols-2 sm:grid-cols-4 gap-x-5 gap-y-3 pt-2 border-t border-border ${
                whiteBox.powerAmpEnabled ? "" : "opacity-50 pointer-events-none"
              }`}
            >
              {(
                [
                  ["Master", "master"],
                  ["Sag", "sag"],
                  ["Presence", "presence"],
                  ["Depth", "depth"],
                ] as const
              ).map(([lbl, key]) => (
                <div key={key} className="space-y-1.5">
                  <div className="flex justify-between text-xs">
                    <Label className="text-xs">{lbl}</Label>
                    <span className="font-mono text-muted-foreground">{whiteBox[key].toFixed(1)}</span>
                  </div>
                  <Slider
                    value={[whiteBox[key]]}
                    min={0}
                    max={10}
                    step={0.1}
                    onValueChange={([v]) => pushWhiteBox({ [key]: v })}
                  />
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
