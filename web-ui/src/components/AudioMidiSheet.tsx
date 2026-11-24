import React from "react"
import { Activity, Cpu, Play, RefreshCw, Square, Usb, Wrench } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { ScrollArea } from "@/components/ui/scroll-area"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Separator } from "@/components/ui/separator"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet"
import { Switch } from "@/components/ui/switch"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { HelpHint } from "@/components/ui/tooltip"
import { useI18n } from "@/lib/i18n"
import type {
  AsioCapabilitiesDto,
  MidiSnapshotDto,
  PresetDef,
  WasapiDeviceDto,
} from "@/types/rig"

interface AudioMidiSheetProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  isAudioRunning: boolean
  isAudioBusy: boolean
  audioStatus: string
  inputKind: "ASIO" | "WASAPI"
  onInputKindChange: (kind: "ASIO" | "WASAPI") => void
  inputDriver: string
  onInputDriverChange: (driver: string) => void
  asioDrivers: string[]
  wasapiInputDevices: WasapiDeviceDto[]
  wasapiInputDeviceId: string
  onWasapiInputDeviceIdChange: (id: string) => void
  wasapiInputExclusive: boolean
  onWasapiInputExclusiveChange: (val: boolean) => void
  wasapiInputRaw: boolean
  onWasapiInputRawChange: (val: boolean) => void
  channelMode: number
  inputChannelOffset: number
  onChannelMappingChange: (channelMode: number, inputChannelOffset: number) => void
  bufferSize: number
  onBufferSizeChange: (sz: number) => void
  outputKind: string
  onOutputKindChange: (kind: string) => void
  wasapiDevices: WasapiDeviceDto[]
  wasapiDeviceId: string
  onWasapiDeviceIdChange: (id: string) => void
  wasapiExclusive: boolean
  onWasapiExclusiveChange: (val: boolean) => void
  asioOutDriver: string
  onAsioOutDriverChange: (driver: string) => void
  presetIndex: number
  onPresetIndexChange: (idx: number) => void
  presets: PresetDef[]
  caps: AsioCapabilitiesDto | null
  midiState: MidiSnapshotDto
  onProbeAsioCaps: (driver: string) => void
  onRefreshDevices: () => void
  onOpenAsioPanel: () => void
  onQuickStartAutoAudio: (mode: "WASAPI" | "ASIO_DUPLEX") => void
  onQuickStartUniversalWasapi: () => void
  onToggleAudio: () => void
  onSelectMidiDevice: (deviceName: string) => void
  onStartMidiLearn: (req: {
    targetKind: string
    slotIndex?: number
    paramIndex?: number
    sceneIndex?: number
  }) => void
  onClearMidiBinding: (req: {
    targetKind: string
    slotIndex?: number
    paramIndex?: number
    sceneIndex?: number
  }) => void
}

export const AudioMidiSheet: React.FC<AudioMidiSheetProps> = ({
  open,
  onOpenChange,
  isAudioRunning,
  isAudioBusy,
  audioStatus,
  inputKind,
  onInputKindChange,
  inputDriver,
  onInputDriverChange,
  asioDrivers,
  wasapiInputDevices,
  wasapiInputDeviceId,
  onWasapiInputDeviceIdChange,
  wasapiInputExclusive,
  onWasapiInputExclusiveChange,
  wasapiInputRaw,
  onWasapiInputRawChange,
  channelMode,
  inputChannelOffset,
  onChannelMappingChange,
  bufferSize,
  onBufferSizeChange,
  outputKind,
  onOutputKindChange,
  wasapiDevices,
  wasapiDeviceId,
  onWasapiDeviceIdChange,
  wasapiExclusive,
  onWasapiExclusiveChange,
  asioOutDriver,
  onAsioOutDriverChange,
  presetIndex,
  onPresetIndexChange,
  presets,
  caps,
  midiState,
  onProbeAsioCaps,
  onRefreshDevices,
  onOpenAsioPanel,
  onQuickStartAutoAudio,
  onQuickStartUniversalWasapi,
  onToggleAudio,
  onSelectMidiDevice,
  onStartMidiLearn,
  onClearMidiBinding,
}) => {
  const { tx } = useI18n()
  const nonRealtekAsioCount = asioDrivers.filter((d) => !/realtek/i.test(d)).length

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="max-w-xl p-0">
        <SheetHeader>
          <div className="flex items-center justify-between gap-2 pr-8">
            <div className="flex items-center gap-2 min-w-0">
              <SheetTitle className="truncate">AUDIO I/O &amp; MIDI PATCHBAY</SheetTitle>
              <HelpHint
                side="bottom"
                content={tx(
                  "ASIO 인터페이스, WASAPI 라인 인풋 라우팅 및 MIDI 컨트롤러를 설정합니다.",
                  "Configure ASIO interfaces, WASAPI Line-In routing, and MIDI controllers."
                )}
              />
              <SheetDescription className="sr-only">
                {tx(
                  "범용 ASIO 오디오 인터페이스 · 멀티이펙터 · WASAPI 라인 인풋 라우팅 및 MIDI 컨트롤러 설정",
                  "Configure Universal ASIO Interfaces, Multi-FX USB Audio, WASAPI Line-In routing, and MIDI controllers."
                )}
              </SheetDescription>
            </div>
            <Badge variant={isAudioRunning ? "ok" : "outline"} size="xs" className="max-w-[190px]">
              <Activity className="size-3 shrink-0" />
              <span className="truncate">{audioStatus}</span>
            </Badge>
          </div>
        </SheetHeader>

        <ScrollArea className="flex-1 min-h-0">
          <div className="p-5 space-y-4 text-xs">
            {/* Engine Status & Refresh Bar */}
            <div className="flex items-center justify-between gap-2 rounded-sm border border-border bg-secondary/40 px-3 py-2">
              <span className="flex items-center gap-1.5 min-w-0 font-mono text-[11px] text-muted-foreground">
                <span className="truncate">
                  {isAudioRunning ? tx("오디오 실행 중", "Engine live") : tx("오디오 대기 중", "Standby")}
                </span>
                {isAudioRunning && (
                  <HelpHint
                    side="bottom"
                    content={tx(
                      "장치를 변경하려면 모니터링을 먼저 중지하세요.",
                      "Stop audio monitoring first to change hardware settings."
                    )}
                  />
                )}
              </span>
              <div className="flex items-center gap-1.5 shrink-0">
                <Button size="xs" variant="ghost" onClick={onRefreshDevices}>
                  <RefreshCw className="size-3" />
                  {tx("새로고침", "Refresh")}
                </Button>
                {inputKind === "ASIO" && (
                  <Button
                    size="xs"
                    variant="keycap"
                    disabled={!inputDriver || isAudioRunning}
                    onClick={onOpenAsioPanel}
                  >
                    <Wrench className="size-3" />
                    ASIO CPL
                  </Button>
                )}
              </div>
            </div>

            <Tabs
              value={inputKind}
              onValueChange={(v) => {
                if (isAudioRunning) return
                const next = v as "ASIO" | "WASAPI"
                onInputKindChange(next)
                if (next === "WASAPI" && outputKind === "ASIO_DUPLEX") {
                  onOutputKindChange("WASAPI")
                }
              }}
            >
              <TabsList className="grid w-full grid-cols-2">
                <TabsTrigger value="ASIO" disabled={isAudioRunning}>
                  ASIO INTERFACE
                </TabsTrigger>
                <TabsTrigger value="WASAPI" disabled={isAudioRunning}>
                  WASAPI / LINE-IN
                </TabsTrigger>
              </TabsList>

              <TabsContent value="ASIO" className="space-y-4 mt-3">
                {/* Universal Audio Interface Quick Connect Card */}
                <div className="rounded-sm border border-border bg-background p-3 space-y-2.5">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-mono text-[11px] font-bold flex items-center gap-1.5 min-w-0 text-foreground">
                      <Usb className="size-3.5 text-primary shrink-0" />
                      <span className="truncate">ASIO QUICK CONNECT</span>
                      <HelpHint
                        content={tx(
                          "Focusrite, UAD, RME, Audient, MOTU, Line 6, BOSS 등 모든 ASIO 장치를 자동 감지합니다. 드라이버 없는 USB 케이블은 WASAPI 탭을 이용하세요.",
                          "Auto-detects Focusrite, UAD, RME, Audient, MOTU, Line 6, BOSS & ASIO4ALL. For driverless USB cables, switch to WASAPI."
                        )}
                      />
                    </span>
                    <Badge variant={nonRealtekAsioCount > 0 ? "ok" : "outline"} size="xs">
                      {asioDrivers.length > 0
                        ? `${asioDrivers.length} ASIO`
                        : tx("WASAPI 모드", "WASAPI READY")}
                    </Badge>
                  </div>
                  <div className="grid grid-cols-2 gap-2">
                    <Button
                      size="sm"
                      variant="signal"
                      disabled={isAudioBusy}
                      onClick={() => onQuickStartAutoAudio("WASAPI")}
                    >
                      <span className="truncate">{tx("인터페이스 입력 → PC 출력", "Interface In → PC Output")}</span>
                    </Button>
                    <Button
                      size="sm"
                      variant="keycap"
                      disabled={isAudioBusy}
                      onClick={() => onQuickStartAutoAudio("ASIO_DUPLEX")}
                    >
                      <span className="truncate">{tx("인터페이스 직결 (Duplex)", "Direct Duplex")}</span>
                    </Button>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-1.5">
                    <Label>{tx("ASIO 입력 드라이버", "ASIO Input Driver")}</Label>
                    <Select
                      value={inputDriver || "__none__"}
                      disabled={isAudioRunning}
                      onValueChange={(v) => {
                        if (v === "__none__") return
                        onInputDriverChange(v)
                        onChannelMappingChange(channelMode, 0)
                        onProbeAsioCaps(v)
                      }}
                    >
                      <SelectTrigger>
                        <SelectValue placeholder={tx("드라이버 선택", "Select driver")} />
                      </SelectTrigger>
                      <SelectContent>
                        {asioDrivers.length === 0 ? (
                          <SelectItem value="__none__">
                            {tx("감지된 ASIO 없음 (WASAPI 권장)", "No ASIO detected (use WASAPI)")}
                          </SelectItem>
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
                    <Label>{tx("입력 단자 / 채널", "Input Channel Map")}</Label>
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
                            onChannelMappingChange(
                              modeStr === "stereo" ? 0 : 1,
                              Math.max(0, Number(offStr) || 0)
                            )
                          }}
                        >
                          <SelectTrigger>
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {chNames.map((nm, idx) => (
                              <SelectItem key={`mono:${idx}`} value={`mono:${idx}`}>
                                Mono Ch {idx + 1} ({nm})
                              </SelectItem>
                            ))}
                            {Array.from({ length: Math.floor(chNames.length / 2) }, (_, pairIdx) => {
                              const c = pairIdx * 2
                              return (
                                <SelectItem key={`stereo:${c}`} value={`stereo:${c}`}>
                                  Stereo Ch {c + 1}+{c + 2} ({chNames[c]}/{chNames[c + 1]})
                                </SelectItem>
                              )
                            })}
                          </SelectContent>
                        </Select>
                      )
                    })()}
                  </div>

                  <div className="space-y-1.5">
                    <Label>{tx("출력 엔진", "Output Engine")}</Label>
                    <Select
                      value={outputKind}
                      disabled={isAudioRunning}
                      onValueChange={onOutputKindChange}
                    >
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="WASAPI">
                          {tx("WASAPI (PC 스피커/헤드셋)", "WASAPI (System Output)")}
                        </SelectItem>
                        <SelectItem value="ASIO_DUPLEX">
                          {tx("ASIO 직결 풀듀플렉스", "ASIO Direct Duplex")}
                        </SelectItem>
                        <SelectItem value="ASIO">
                          {tx("별도 제2 ASIO 출력", "Secondary ASIO Output")}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  <div className="space-y-1.5">
                    <Label>{tx("ASIO 버퍼 크기", "ASIO Buffer Size")}</Label>
                    <Select
                      value={String(bufferSize)}
                      disabled={isAudioRunning}
                      onValueChange={(v) => onBufferSizeChange(Number(v))}
                    >
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {(caps?.bufferSizes?.length ? caps.bufferSizes : [64, 128, 256, 512, 0]).map((sz) => (
                          <SelectItem key={sz} value={String(sz)}>
                            {sz === 0 ? tx("드라이버 기본값", "Driver Default") : `${sz} samples`}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                </div>
              </TabsContent>

              <TabsContent value="WASAPI" className="space-y-4 mt-3">
                <div className="rounded-sm border border-signal-ok/40 bg-signal-ok/10 px-3 py-2.5">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-mono text-[11px] font-bold flex items-center gap-1.5 min-w-0 text-signal-ok">
                      <Usb className="size-3.5 shrink-0" />
                      <span className="truncate">PLUG-AND-PLAY WASAPI</span>
                      <HelpHint
                        content={tx(
                          "별도 ASIO 드라이버 없이 USB 기타 케이블, 클래스 컴플라이언트 장치 및 PC 라인인을 바로 연결합니다.",
                          "Connect USB guitar cables, class-compliant interfaces, or PC Line-In directly via WASAPI."
                        )}
                      />
                    </span>
                    <Button
                      size="xs"
                      variant="keycap"
                      disabled={isAudioBusy}
                      onClick={onQuickStartUniversalWasapi}
                      className="shrink-0"
                    >
                      {tx("기본 입력으로 즉시 시작", "Quick Start Default")}
                    </Button>
                  </div>
                </div>

                <div className="space-y-1.5">
                  <Label>{tx("Windows 오디오 입력 장치", "WASAPI Capture Device")}</Label>
                  <Select
                    value={wasapiInputDeviceId || "__default__"}
                    disabled={isAudioRunning}
                    onValueChange={(v) => {
                      onWasapiInputDeviceIdChange(v === "__default__" ? "" : v)
                      onChannelMappingChange(channelMode, 0)
                    }}
                  >
                    <SelectTrigger>
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
                    <Label>{tx("입력 채널 매핑", "Channel Mapping")}</Label>
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
                            onChannelMappingChange(
                              modeStr === "stereo" ? 0 : 1,
                              Math.max(0, Number(offStr) || 0)
                            )
                          }}
                        >
                          <SelectTrigger>
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {Array.from({ length: maxCh }, (_, idx) => (
                              <SelectItem key={`mono:${idx}`} value={`mono:${idx}`}>
                                Mono Ch {idx + 1} ({idx === 0 ? "Hi-Z / L" : idx === 1 ? "Line / R" : `In ${idx + 1}`})
                              </SelectItem>
                            ))}
                            {Array.from({ length: Math.floor(maxCh / 2) }, (_, pairIdx) => {
                              const c = pairIdx * 2
                              return (
                                <SelectItem key={`stereo:${c}`} value={`stereo:${c}`}>
                                  Stereo Ch {c + 1}+{c + 2} (Line In)
                                </SelectItem>
                              )
                            })}
                          </SelectContent>
                        </Select>
                      )
                    })()}
                  </div>

                  <div className="space-y-1.5">
                    <Label>{tx("출력 엔진", "Output Engine")}</Label>
                    <Select
                      value={outputKind === "ASIO_DUPLEX" ? "WASAPI" : outputKind}
                      disabled={isAudioRunning}
                      onValueChange={onOutputKindChange}
                    >
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="WASAPI">WASAPI (PC Output)</SelectItem>
                        <SelectItem value="ASIO">ASIO Output Driver</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-2.5">
                  <div className="flex items-center justify-between gap-2 rounded-sm border border-border bg-background px-3 py-2 min-w-0">
                    <span className="flex items-center gap-1.5 min-w-0">
                      <Label className="text-[11px] truncate">{tx("순수 악기 Raw 모드", "Instrument Raw Mode")}</Label>
                      <HelpHint
                        content={tx(
                          "Windows 소음 억제 및 자동 게인(AGC)을 우회해 기타 서스테인 절단을 방지합니다.",
                          "Bypass Windows AGC & noise suppression to preserve natural guitar sustain."
                        )}
                      />
                    </span>
                    <Switch
                      checked={wasapiInputRaw}
                      disabled={isAudioRunning}
                      onCheckedChange={onWasapiInputRawChange}
                    />
                  </div>
                  <div className="flex items-center justify-between gap-2 rounded-sm border border-border bg-background px-3 py-2 min-w-0">
                    <span className="flex items-center gap-1.5 min-w-0">
                      <Label className="text-[11px] truncate">{tx("WASAPI 입력 단독", "Exclusive Input")}</Label>
                      <HelpHint
                        content={tx(
                          "입력 장치를 단독 점유해 캡처 레이턴시를 최소화합니다.",
                          "Claim exclusive capture access for minimum input latency."
                        )}
                      />
                    </span>
                    <Switch
                      checked={wasapiInputExclusive}
                      disabled={isAudioRunning}
                      onCheckedChange={onWasapiInputExclusiveChange}
                    />
                  </div>
                </div>
              </TabsContent>
            </Tabs>

            <Separator />

            {/* Output Device & Latency Preset */}
            <div className="space-y-3">
              {outputKind === "WASAPI" && (
                <>
                  <div className="space-y-1.5">
                    <Label>{tx("WASAPI 출력 장치", "WASAPI Output Device")}</Label>
                    <Select
                      value={wasapiDeviceId || "__default__"}
                      disabled={isAudioRunning}
                      onValueChange={(v) => onWasapiDeviceIdChange(v === "__default__" ? "" : v)}
                    >
                      <SelectTrigger>
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
                  <div className="flex items-center justify-between gap-2 rounded-sm border border-border bg-background px-3 py-2 min-w-0">
                    <span className="flex items-center gap-1.5 min-w-0">
                      <Label className="truncate">{tx("WASAPI 출력 단독 모드", "WASAPI Exclusive Output")}</Label>
                      <HelpHint
                        content={tx(
                          "Windows 오디오 믹서를 우회해 초저지연 출력을 제공합니다.",
                          "Bypass Windows audio mixer for ultra-low latency playback."
                        )}
                      />
                    </span>
                    <Switch
                      checked={wasapiExclusive}
                      disabled={isAudioRunning}
                      onCheckedChange={onWasapiExclusiveChange}
                    />
                  </div>
                </>
              )}

              {outputKind === "ASIO" && (
                <div className="space-y-1.5">
                  <Label>{tx("ASIO 출력 드라이버", "ASIO Output Driver")}</Label>
                  <Select
                    value={asioOutDriver || "__none__"}
                    disabled={isAudioRunning}
                    onValueChange={(v) => v !== "__none__" && onAsioOutDriverChange(v)}
                  >
                    <SelectTrigger>
                      <SelectValue placeholder={tx("출력 드라이버 선택", "Select output driver")} />
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
                <Label>{tx("지연시간 프리셋", "Latency Profile")}</Label>
                <Select
                  value={String(presetIndex)}
                  disabled={isAudioRunning}
                  onValueChange={(v) => onPresetIndexChange(Number(v))}
                >
                  <SelectTrigger>
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
            </div>

            <Separator />

            {/* External MIDI Controller & Foot Switch Section */}
            <div className="rounded-sm border border-border bg-background p-3 space-y-2.5">
              <div className="flex items-center justify-between gap-2">
                <span className="font-mono text-[11px] font-bold flex items-center gap-1.5 min-w-0 text-foreground">
                  <Cpu className="size-3.5 text-primary shrink-0" />
                  <span className="truncate">EXTERNAL MIDI &amp; EXPRESSION</span>
                  <HelpHint
                    content={tx(
                      "시그널 체인 슬롯, 노브, 씬(A–H), 루퍼 버튼을 우클릭하면 즉시 MIDI CC 학습 메뉴가 열립니다.",
                      "Right-click any slot, knob, Scene (A–H), or looper control to open MIDI CC Learn."
                    )}
                  />
                </span>
                <Badge variant={midiState.connected ? "ok" : "outline"} size="xs">
                  {midiState.connected
                    ? `${midiState.bindings.length} CC`
                    : tx("미연결", "OFFLINE")}
                </Badge>
              </div>

              <div className="flex items-center gap-2 min-w-0">
                <Select
                  value={midiState.activeDevice || "__none__"}
                  onValueChange={onSelectMidiDevice}
                >
                  <SelectTrigger className="flex-1 min-w-0">
                    <SelectValue placeholder={tx("MIDI 입력 포트 선택", "Select MIDI input port")} />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__none__">{tx("사용 안 함", "Disabled")}</SelectItem>
                    {midiState.devices.map((m) => (
                      <SelectItem key={m} value={m}>
                        {m}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>

                <Button
                  size="sm"
                  variant="keycap"
                  className="shrink-0"
                  onClick={() => {
                    const existing = midiState.bindings.find((b) => b.targetKind === "tunerMute")
                    if (existing) onClearMidiBinding({ targetKind: "tunerMute" })
                    else onStartMidiLearn({ targetKind: "tunerMute" })
                  }}
                >
                  {midiState.bindings.some((b) => b.targetKind === "tunerMute")
                    ? tx("튜너 뮤트 해제", "Clear Mute CC")
                    : tx("튜너 뮤트 학습", "Learn Mute CC")}
                </Button>
              </div>

              {midiState.bindings.length > 0 && (
                <div className="flex flex-wrap gap-1 pt-0.5">
                  {midiState.bindings.map((b, idx) => (
                    <Badge key={`${b.targetKind}-${idx}`} variant="secondary" size="xs" className="max-w-full">
                      <span className="truncate">CC{b.cc} → {b.targetKind}</span>
                    </Badge>
                  ))}
                </div>
              )}
            </div>
          </div>
        </ScrollArea>

        <SheetFooter>
          <Button variant="ghost" size="sm" onClick={() => onOpenChange(false)}>
            {tx("닫기", "Close")}
          </Button>
          <Button
            variant={isAudioRunning ? "destructive" : "signal"}
            size="sm"
            disabled={isAudioBusy}
            onClick={onToggleAudio}
          >
            {isAudioRunning ? <Square className="size-3 fill-current" /> : <Play className="size-3 fill-current" />}
            {isAudioRunning ? tx("모니터링 중지", "Stop Monitoring") : tx("모니터링 시작", "Start Monitoring")}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  )
}
