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
  Usb,
  Volume2,
  VolumeX,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import {
  Dialog,
  DialogContent,
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

const LED_STEPS = Array.from({ length: 21 }, (_, idx) => (idx - 10) * 5) // -50 .. 0 .. +50 ct

interface TunerDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tuner: TunerSnapshotDto
  isAudioRunning: boolean
  isAudioBusy: boolean
  hasGp100: boolean
  tunerMute: boolean
  refA4Hz: number
  onTunerConfigChange: (opts: { mute?: boolean; refA4Hz?: number }) => void
  onStartAudio: () => void
  onQuickStartGp100: () => void
  onOpenAudioSettings: () => void
}

export function TunerDialog({
  open,
  onOpenChange,
  tuner,
  isAudioRunning,
  isAudioBusy,
  hasGp100,
  tunerMute,
  refA4Hz,
  onTunerConfigChange,
  onStartAudio,
  onQuickStartGp100,
  onOpenAudioSettings,
}: TunerDialogProps) {
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

  // 가장 가까운 목표 줄 계산
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
          <DialogTitle className="flex items-center justify-between gap-2 pr-6 text-sm">
            <span className="flex items-center gap-2">
              <Music2 className="w-4 h-4 text-primary" />
              정밀 크로매틱 기타 튜너
            </span>
            <Badge variant={isAudioRunning ? "secondary" : "outline"} className="text-[10px] font-mono">
              A4 = {refA4Hz.toFixed(0)} Hz
            </Badge>
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-3 text-xs">
          {!isAudioRunning && (
            <div className="rounded-md border border-border bg-secondary/50 p-2.5 flex flex-wrap items-center justify-between gap-2">
              <span className="text-[11px] text-muted-foreground">
                튜너를 사용하려면 먼저 오디오 입력(ASIO/GP-100)을 켜주세요.
              </span>
              <div className="flex items-center gap-1.5">
                {hasGp100 && (
                  <Button
                    size="sm"
                    variant="default"
                    className="h-7 text-[11px] gap-1"
                    disabled={isAudioBusy}
                    onClick={onQuickStartGp100}
                  >
                    <Usb className="w-3 h-3" />
                    GP-100 연결
                  </Button>
                )}
                <Button
                  size="sm"
                  variant="secondary"
                  className="h-7 text-[11px] gap-1"
                  disabled={isAudioBusy}
                  onClick={onStartAudio}
                >
                  <Play className="w-3 h-3" />
                  오디오 시작
                </Button>
              </div>
            </div>
          )}

          {/* Preset selector */}
          <div className="flex items-center justify-between gap-2">
            <Label className="text-[11px] text-muted-foreground shrink-0">튜닝 프리셋</Label>
            <Select value={presetId} onValueChange={setPresetId}>
              <SelectTrigger className="h-8 text-xs w-[270px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TUNING_PRESETS.map((p) => (
                  <SelectItem key={p.id} value={p.id} className="text-xs">
                    {p.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {/* Main Pitch Display Card */}
          <Card
            className={`transition-colors ${
              inTune
                ? "border-emerald-500/60 bg-emerald-500/10"
                : tuner.active
                  ? "border-amber-500/40 bg-card"
                  : "border-border bg-card/70"
            }`}
          >
            <CardContent className="p-4 space-y-4">
              {/* Big Note + Cents + Direction */}
              <div className="flex items-center justify-between gap-3">
                <div className="space-y-0.5 min-w-[90px]">
                  <div className="text-[10px] text-muted-foreground">감지 주파수</div>
                  <div className="font-mono text-sm font-semibold">
                    {tuner.active && tuner.freqHz > 0 ? `${tuner.freqHz.toFixed(1)} Hz` : "-- Hz"}
                  </div>
                  {tuner.active && (tuner.clarity ?? 0) > 0 && (
                    <div className="text-[10px] font-mono text-muted-foreground">
                      신뢰도 {Math.round((tuner.clarity ?? 0) * 100)}%
                    </div>
                  )}
                </div>

                <div className="text-center">
                  <div
                    className={`text-5xl font-black font-mono tracking-tight leading-none ${
                      inTune
                        ? "text-emerald-300"
                        : tuner.active
                          ? "text-foreground"
                          : "text-muted-foreground"
                    }`}
                  >
                    {tuner.noteName !== "--" ? `${tuner.noteName}${tuner.octave}` : "--"}
                  </div>
                </div>

                <div className="text-right space-y-1 min-w-[96px]">
                  <Badge
                    variant={inTune ? "default" : tuner.active ? "secondary" : "outline"}
                    className="font-mono text-[11px]"
                  >
                    {!tuner.active
                      ? "신호 대기"
                      : inTune
                        ? "IN TUNE"
                        : `${tuner.cents > 0 ? "+" : ""}${tuner.cents.toFixed(1)} ct`}
                  </Badge>
                  <div className="text-[11px] font-medium flex items-center justify-end gap-1 text-muted-foreground">
                    {!tuner.active ? (
                      <span>줄을 1회 튕기세요</span>
                    ) : inTune ? (
                      <span className="text-emerald-300 flex items-center gap-1">
                        <Check className="w-3.5 h-3.5" />
                        정확히 조율됨
                      </span>
                    ) : tuner.cents < 0 ? (
                      <span className="text-amber-300 flex items-center gap-1">
                        <ArrowUp className="w-3.5 h-3.5" />
                        줄을 감으세요
                      </span>
                    ) : (
                      <span className="text-amber-300 flex items-center gap-1">
                        <ArrowDown className="w-3.5 h-3.5" />
                        줄을 푸세요
                      </span>
                    )}
                  </div>
                </div>
              </div>

              {/* 21-LED Scale + Moving Needle */}
              <div className="space-y-1.5">
                <div className="relative flex items-center justify-between gap-1 h-7 px-1 rounded-md border border-border bg-background/60">
                  {LED_STEPS.map((stepCt) => {
                    const isCenter = stepCt === 0
                    const dist = tuner.active ? Math.abs(clampedCents - stepCt) : 999
                    const lit = tuner.active && dist <= 3.5
                    const activeColor = isCenter
                      ? inTune
                        ? "bg-emerald-400"
                        : "bg-primary"
                      : Math.abs(stepCt) <= 10
                        ? "bg-amber-400"
                        : "bg-destructive"
                    return (
                      <div
                        key={stepCt}
                        className={`flex-1 rounded-sm transition-all ${
                          isCenter ? "h-5" : "h-3"
                        } ${lit ? activeColor : isCenter ? "bg-muted-foreground/40" : "bg-muted/50"}`}
                      />
                    )
                  })}
                  {tuner.active && (
                    <div
                      className={`absolute top-0 bottom-0 w-0.5 transition-all duration-75 ${
                        inTune ? "bg-emerald-300" : "bg-foreground"
                      }`}
                      style={{ left: `calc(${needleLeftPct}% - 1px)` }}
                    />
                  )}
                </div>
                <div className="flex justify-between text-[10px] font-mono text-muted-foreground px-1">
                  <span>-50 ct (Flat)</span>
                  <span>0 ct</span>
                  <span>+50 ct (Sharp)</span>
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
                      className={`rounded-md border px-1.5 py-1.5 text-center transition-colors ${
                        isStringLocked
                          ? "border-emerald-500/60 bg-emerald-500/20 text-emerald-300"
                          : isTarget
                            ? "border-primary bg-primary/15 text-foreground"
                            : "border-border bg-secondary/35 text-muted-foreground"
                      }`}
                    >
                      <div className="text-[10px]">{s.num}번줄</div>
                      <div className="font-mono font-bold text-xs">{s.name}</div>
                      <div className="text-[9px] font-mono opacity-80">{targetFreq.toFixed(1)}Hz</div>
                    </div>
                  )
                })}
              </div>
            </CardContent>
          </Card>

          {/* Controls: Tuner Mute + A4 Reference + Dry Signal Level */}
          <div className="rounded-md border border-border bg-muted/35 p-3 space-y-2.5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                {tunerMute ? (
                  <VolumeX className="w-4 h-4 text-destructive" />
                ) : (
                  <Volume2 className="w-4 h-4 text-muted-foreground" />
                )}
                <Label htmlFor="tuner-mute-switch" className="text-xs cursor-pointer">
                  튜너 뮤트 (조율 중 출력 무음)
                </Label>
                <Switch
                  id="tuner-mute-switch"
                  checked={tunerMute}
                  onCheckedChange={(checked) => onTunerConfigChange({ mute: checked })}
                />
              </div>

              <div className="flex items-center gap-1">
                <span className="text-[11px] text-muted-foreground mr-1">A4 기준</span>
                <Button
                  size="icon"
                  variant="outline"
                  className="h-6 w-6"
                  onClick={() => onTunerConfigChange({ refA4Hz: Math.max(430, refA4Hz - 1) })}
                  title="A4 기준 -1 Hz"
                >
                  <Minus className="w-3 h-3" />
                </Button>
                <button
                  type="button"
                  onClick={() => onTunerConfigChange({ refA4Hz: 440 })}
                  className="h-6 px-2 rounded border border-border bg-secondary/50 font-mono text-[11px] hover:bg-secondary cursor-pointer"
                  title="클릭하여 표준 440 Hz로 초기화"
                >
                  {refA4Hz.toFixed(0)} Hz
                </button>
                <Button
                  size="icon"
                  variant="outline"
                  className="h-6 w-6"
                  onClick={() => onTunerConfigChange({ refA4Hz: Math.min(450, refA4Hz + 1) })}
                  title="A4 기준 +1 Hz"
                >
                  <Plus className="w-3 h-3" />
                </Button>
              </div>
            </div>

            <div className="space-y-1">
              <div className="flex items-center justify-between text-[10px] text-muted-foreground">
                <span>드라이 DI 입력 신호</span>
                <span className="font-mono">{levelDb.toFixed(1)} dBFS</span>
              </div>
              <Progress value={levelPct} className="h-1.5" />
            </div>

            {isAudioRunning && levelDb <= -85 && (
              <div className="rounded border border-amber-500/40 bg-amber-500/10 px-2.5 py-2 flex items-center justify-between gap-2 text-[11px]">
                <span className="text-amber-200">
                  입력 신호가 -85 dBFS 이하입니다. 오디오 I/O 입력 채널(In 1/In 2) 또는 기타 볼륨을 확인하세요.
                </span>
                <Button
                  size="sm"
                  variant="secondary"
                  className="h-6 text-[10px] shrink-0 gap-1"
                  onClick={() => {
                    onOpenChange(false)
                    onOpenAudioSettings()
                  }}
                >
                  <SlidersHorizontal className="w-3 h-3" />
                  오디오 I/O
                </Button>
              </div>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
