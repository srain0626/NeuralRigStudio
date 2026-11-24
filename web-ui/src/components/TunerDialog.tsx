import { useMemo, useState } from "react"
import {
  ArrowDown,
  ArrowUp,
  Check,
  Minus,
  Music2,
  Play,
  Plus,
  SlidersHorizontal,
  Volume2,
  VolumeX,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Label } from "@/components/ui/label"
import { Progress } from "@/components/ui/progress"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import { Hint } from "@/components/ui/tooltip"
import { useI18n } from "@/lib/i18n"
import type { TunerSnapshotDto } from "@/types/rig"

interface StringTarget {
  num: number
  name: string
  midi: number
}

interface TuningPreset {
  id: string
  label: string
  strings: StringTarget[]
}

const TUNING_PRESETS: TuningPreset[] = [
  {
    id: "standard",
    label: "Standard (E A D G B E)",
    strings: [
      { num: 6, name: "E2", midi: 40 },
      { num: 5, name: "A2", midi: 45 },
      { num: 4, name: "D3", midi: 50 },
      { num: 3, name: "G3", midi: 55 },
      { num: 2, name: "B3", midi: 59 },
      { num: 1, name: "E4", midi: 64 },
    ],
  },
  {
    id: "eb",
    label: "Half-Step Down (Eb Ab Db Gb Bb Eb)",
    strings: [
      { num: 6, name: "D#2", midi: 39 },
      { num: 5, name: "G#2", midi: 44 },
      { num: 4, name: "C#3", midi: 49 },
      { num: 3, name: "F#3", midi: 54 },
      { num: 2, name: "A#3", midi: 58 },
      { num: 1, name: "D#4", midi: 63 },
    ],
  },
  {
    id: "dropd",
    label: "Drop D (D A D G B E)",
    strings: [
      { num: 6, name: "D2", midi: 38 },
      { num: 5, name: "A2", midi: 45 },
      { num: 4, name: "D3", midi: 50 },
      { num: 3, name: "G3", midi: 55 },
      { num: 2, name: "B3", midi: 59 },
      { num: 1, name: "E4", midi: 64 },
    ],
  },
  {
    id: "dstd",
    label: "D Standard (D G C F A D)",
    strings: [
      { num: 6, name: "D2", midi: 38 },
      { num: 5, name: "G2", midi: 43 },
      { num: 4, name: "C3", midi: 48 },
      { num: 3, name: "F3", midi: 53 },
      { num: 2, name: "A3", midi: 57 },
      { num: 1, name: "D4", midi: 62 },
    ],
  },
]

const LED_STEPS = Array.from({ length: 21 }, (_, idx) => (idx - 10) * 5)

interface TunerDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tuner: TunerSnapshotDto
  isAudioRunning: boolean
  isAudioBusy: boolean
  tunerMute: boolean
  refA4Hz: number
  onTunerConfigChange: (opts: { mute?: boolean; refA4Hz?: number }) => void
  onStartAudio: () => void
  onOpenAudioSettings: () => void
}

export function TunerDialog({
  open,
  onOpenChange,
  tuner,
  isAudioRunning,
  isAudioBusy,
  tunerMute,
  refA4Hz,
  onTunerConfigChange,
  onStartAudio,
  onOpenAudioSettings,
}: TunerDialogProps) {
  const { tx } = useI18n()
  const [presetId, setPresetId] = useState("standard")
  const activePreset = useMemo(
    () => TUNING_PRESETS.find((p) => p.id === presetId) ?? TUNING_PRESETS[0],
    [presetId]
  )

  const handleOpenChange = (nextOpen: boolean) => {
    if (!nextOpen && tunerMute) {
      onTunerConfigChange({ mute: false })
    }
    onOpenChange(nextOpen)
  }

  const levelDb = tuner.levelDb ?? -96
  const levelPct = Math.min(100, Math.max(0, ((levelDb + 82) / 82) * 100))
  const inTune = tuner.active && Math.abs(tuner.cents) <= 3.5
  const clampedCents = tuner.active ? Math.max(-50, Math.min(50, tuner.cents)) : 0
  const needleLeftPct = 50 + clampedCents

  const nearestString = useMemo(() => {
    if (!tuner.active || tuner.freqHz <= 20) return null
    const curMidi = 69 + 12 * Math.log2(tuner.freqHz / Math.max(420, refA4Hz))
    let best: { target: StringTarget; targetFreq: number; centsDiff: number } | null = null
    for (const s of activePreset.strings) {
      const targetFreq = refA4Hz * Math.pow(2, (s.midi - 69) / 12)
      const centsDiff = (curMidi - s.midi) * 100
      if (!best || Math.abs(centsDiff) < Math.abs(best.centsDiff)) {
        best = { target: s, targetFreq, centsDiff }
      }
    }
    return best
  }, [tuner.active, tuner.freqHz, activePreset, refA4Hz])

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <div className="flex items-center justify-between gap-2 pr-8">
            <DialogTitle className="flex items-center gap-2">
              <Music2 className="size-4 text-primary" />
              PRECISION STROBE TUNER
            </DialogTitle>
            <Badge variant={isAudioRunning ? "ok" : "outline"} size="xs">
              A4 = {refA4Hz.toFixed(0)} Hz
            </Badge>
          </div>
          <DialogDescription>
            {tx(
              "MPM 기반 고정밀 크로매틱 튜너 · 조율 시 출력 무음 전환 지원",
              "High-precision MPM chromatic tuner with silent bypass option."
            )}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 text-xs">
          {!isAudioRunning && (
            <div className="rounded-sm border border-border bg-secondary/50 p-2.5 flex flex-wrap items-center justify-between gap-2">
              <span className="text-[11px] text-muted-foreground">
                {tx("튜너를 사용하려면 먼저 오디오 입력을 켜주세요.", "Start audio input to activate tuner.")}
              </span>
              <div className="flex items-center gap-1.5">
                <Button size="xs" variant="ghost" onClick={onOpenAudioSettings}>
                  {tx("장치 설정", "Audio I/O")}
                </Button>
                <Button size="xs" variant="keycap" disabled={isAudioBusy} onClick={onStartAudio}>
                  <Play className="size-3" />
                  {tx("오디오 시작", "START")}
                </Button>
              </div>
            </div>
          )}

          <div className="flex items-center justify-between gap-2">
            <Label>{tx("튜닝 프리셋", "TUNING PRESET")}</Label>
            <Select value={presetId} onValueChange={setPresetId}>
              <SelectTrigger className="w-[270px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TUNING_PRESETS.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {p.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {/* Recessed Matte OLED Strobe Instrument Display */}
          <div className="rounded-md border border-oled-border oled-dot-grid p-4 space-y-4">
            <div className="flex items-center justify-between gap-3">
              <div className="space-y-0.5 min-w-[92px] font-mono tabular-nums">
                <div className="text-[10px] text-oled-muted">FREQ</div>
                <div className="text-sm font-bold text-oled-foreground">
                  {tuner.active && tuner.freqHz > 0 ? `${tuner.freqHz.toFixed(1)} Hz` : "-- Hz"}
                </div>
                {tuner.active && (tuner.clarity ?? 0) > 0 && (
                  <div className="text-[10px] text-oled-muted">
                    CONF {Math.round((tuner.clarity ?? 0) * 100)}%
                  </div>
                )}
              </div>

              <div className="text-center">
                <div
                  className={`font-display text-5xl font-bold tracking-tight leading-none tabular-nums ${
                    inTune
                      ? "text-signal-ok"
                      : tuner.active
                        ? "text-oled-foreground"
                        : "text-oled-muted"
                  }`}
                >
                  {tuner.noteName !== "--" ? `${tuner.noteName}${tuner.octave}` : "--"}
                </div>
              </div>

              <div className="text-right space-y-1 min-w-[100px]">
                <Badge
                  variant={inTune ? "ok" : tuner.active ? "warn" : "oled"}
                  size="xs"
                  className="tabular-nums"
                >
                  {!tuner.active
                    ? "IDLE"
                    : inTune
                      ? "IN TUNE"
                      : `${tuner.cents > 0 ? "+" : ""}${tuner.cents.toFixed(1)} ct`}
                </Badge>
                <div className="text-[11px] font-medium flex items-center justify-end gap-1 text-oled-muted">
                  {!tuner.active ? (
                    <span>{tx("줄을 1회 튕기세요", "Pluck a string")}</span>
                  ) : inTune ? (
                    <span className="text-signal-ok flex items-center gap-1">
                      <Check className="size-3.5" />
                      {tx("조율 완료", "Locked")}
                    </span>
                  ) : tuner.cents < 0 ? (
                    <span className="text-signal-warn flex items-center gap-1">
                      <ArrowUp className="size-3.5" />
                      {tx("줄을 감으세요", "Tune Up")}
                    </span>
                  ) : (
                    <span className="text-signal-hot flex items-center gap-1">
                      <ArrowDown className="size-3.5" />
                      {tx("줄을 푸세요", "Tune Down")}
                    </span>
                  )}
                </div>
              </div>
            </div>

            {/* 21-LED Strobe Scale + Vernier Needle */}
            <div className="space-y-1.5">
              <div className="relative flex items-center justify-between gap-1 h-8 px-1.5 rounded-sm border border-oled-border bg-oled-elevated">
                {LED_STEPS.map((stepCt) => {
                  const isCenter = stepCt === 0
                  const dist = tuner.active ? Math.abs(clampedCents - stepCt) : 999
                  const lit = tuner.active && dist <= 3.5
                  const activeColor = isCenter
                    ? inTune
                      ? "bg-signal-ok"
                      : "bg-primary"
                    : Math.abs(stepCt) <= 10
                      ? "bg-signal-warn"
                      : "bg-signal-hot"
                  return (
                    <div
                      key={stepCt}
                      className={`flex-1 rounded-[1px] transition-colors ${
                        isCenter ? "h-5" : "h-3"
                      } ${lit ? activeColor : isCenter ? "bg-oled-muted/50" : "bg-oled-border"}`}
                    />
                  )
                })}
                {tuner.active && (
                  <div
                    className={`absolute top-0 bottom-0 w-0.5 transition-all duration-75 ${
                      inTune ? "bg-signal-ok" : "bg-oled-foreground"
                    }`}
                    style={{ left: `calc(${needleLeftPct}% - 1px)` }}
                  />
                )}
              </div>
              <div className="flex justify-between text-[10px] font-mono tabular-nums text-oled-muted px-1">
                <span>-50 ct</span>
                <span>0 ct</span>
                <span>+50 ct</span>
              </div>
            </div>

            {/* 6-String Target Strip */}
            <div className="grid grid-cols-6 gap-1.5">
              {activePreset.strings.map((s) => {
                const targetFreq = refA4Hz * Math.pow(2, (s.midi - 69) / 12)
                const isTarget = nearestString?.target.num === s.num
                const isStringLocked =
                  isTarget && Math.abs(nearestString?.centsDiff ?? 99) <= 4
                return (
                  <div
                    key={s.num}
                    className={`rounded-sm border px-1.5 py-1.5 text-center font-mono tabular-nums transition-colors ${
                      isStringLocked
                        ? "border-signal-ok bg-signal-ok/15 text-signal-ok"
                        : isTarget
                          ? "border-primary bg-primary/20 text-oled-foreground"
                          : "border-oled-border bg-oled-elevated text-oled-muted"
                    }`}
                  >
                    <div className="text-[9px]">S{s.num}</div>
                    <div className="font-bold text-xs">{s.name}</div>
                    <div className="text-[9px] opacity-80">{targetFreq.toFixed(0)}</div>
                  </div>
                )
              })}
            </div>
          </div>

          {/* Controls */}
          <div className="rounded-sm border border-border bg-background p-3 space-y-2.5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                {tunerMute ? (
                  <VolumeX className="size-4 text-signal-hot" />
                ) : (
                  <Volume2 className="size-4 text-muted-foreground" />
                )}
                <Label htmlFor="tuner-mute-switch" className="cursor-pointer">
                  {tx("튜너 뮤트 (조율 중 무음)", "Tuner Mute (Silent Tuning)")}
                </Label>
                <Switch
                  id="tuner-mute-switch"
                  checked={tunerMute}
                  onCheckedChange={(checked) => onTunerConfigChange({ mute: checked })}
                />
              </div>

              <div className="flex items-center gap-1 font-mono">
                <span className="text-[11px] text-muted-foreground mr-1">A4</span>
                <Button
                  size="icon-xs"
                  variant="keycap"
                  onClick={() => onTunerConfigChange({ refA4Hz: Math.max(430, refA4Hz - 1) })}
                >
                  <Minus className="size-3" />
                </Button>
                <Hint content={tx("클릭하여 표준 440 Hz 초기화", "Reset to 440 Hz")}>
                  <Button
                    size="xs"
                    variant="outline"
                    className="tabular-nums"
                    onClick={() => onTunerConfigChange({ refA4Hz: 440 })}
                  >
                    {refA4Hz.toFixed(0)} Hz
                  </Button>
                </Hint>
                <Button
                  size="icon-xs"
                  variant="keycap"
                  onClick={() => onTunerConfigChange({ refA4Hz: Math.min(450, refA4Hz + 1) })}
                >
                  <Plus className="size-3" />
                </Button>
              </div>
            </div>

            <div className="space-y-1">
              <div className="flex items-center justify-between font-mono text-[10px] text-muted-foreground tabular-nums">
                <span>{tx("드라이 DI 입력", "DRY DI SIGNAL")}</span>
                <span>{levelDb.toFixed(1)} dBFS</span>
              </div>
              <Progress value={levelPct} className="h-1.5" />
            </div>

            {isAudioRunning && levelDb <= -85 && (
              <div className="rounded-sm border border-signal-warn/50 bg-signal-warn/10 px-2.5 py-2 flex items-center justify-between gap-2 text-[11px]">
                <span className="text-signal-warn">
                  {tx("입력 신호가 -85 dBFS 이하입니다.", "Signal below -85 dBFS. Check input channel.")}
                </span>
                <Button
                  size="xs"
                  variant="keycap"
                  onClick={() => {
                    onOpenChange(false)
                    onOpenAudioSettings()
                  }}
                >
                  <SlidersHorizontal className="size-3" />
                  AUDIO I/O
                </Button>
              </div>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
