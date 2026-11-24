import React from "react"
import {
  Circle,
  FolderOpen,
  Gauge,
  Music,
  Pause,
  Play,
  Radio,
  Redo2,
  Repeat,
  RotateCcw,
  Square,
  Trash2,
  Undo2,
  Upload,
  Volume2,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { Slider } from "@/components/ui/slider"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Toggle } from "@/components/ui/toggle"
import { Hint } from "@/components/ui/tooltip"
import { useI18n } from "@/lib/i18n"
import type {
  BackingTrackStateDto,
  LooperSnapshotDto,
  MidiSnapshotDto,
  RecorderReampSnapshotDto,
} from "@/types/rig"

interface LooperRecorderDeckProps {
  looper: LooperSnapshotDto
  recorder: RecorderReampSnapshotDto
  backing: BackingTrackStateDto
  tempoBpm: number
  isAudioRunning: boolean
  midiState?: MidiSnapshotDto
  onLooperCmd: (cmd: string, value?: boolean | number) => void
  onRecorderCmd: (cmd: string, payload?: { value?: boolean | number; path?: string }) => void
  onLoadBacking: () => void
  onControlBacking: (patch: { playing?: boolean; loop?: boolean; volume?: number; seekSec?: number }) => void
  onUnloadBacking: () => void
  onStartMidiLearn?: (req: { targetKind: string }) => void
  onClearMidiBinding?: (req: { targetKind: string }) => void
}

function formatSec(sec: number): string {
  const s = Math.max(0, sec)
  const m = Math.floor(s / 60)
  const rem = s - m * 60
  return m > 0 ? `${m}:${rem.toFixed(1).padStart(4, "0")}` : `${rem.toFixed(1)}s`
}

export const LooperRecorderDeck: React.FC<LooperRecorderDeckProps> = ({
  looper,
  recorder,
  backing,
  tempoBpm,
  isAudioRunning,
  midiState,
  onLooperCmd,
  onRecorderCmd,
  onLoadBacking,
  onControlBacking,
  onUnloadBacking,
  onStartMidiLearn,
  onClearMidiBinding,
}) => {
  const { tx } = useI18n()

  const lpState = looper.state
  const isEmpty = lpState === "empty"
  const isRec = lpState === "recording"
  const isPlay = lpState === "playing"
  const isDub = lpState === "overdubbing"
  const isStop = lpState === "stopped"
  const isReelSpinning = isRec || isPlay || isDub

  const recDubLabel = isEmpty
    ? "REC"
    : isRec
      ? "PLAY"
      : isPlay || isStop
        ? isStop
          ? "RESUME"
          : "DUB"
        : "PLAY"

  const recBinding = midiState?.bindings?.find((b) => b.targetKind === "looperRecDub")
  const playStopBinding = midiState?.bindings?.find((b) => b.targetKind === "looperPlayStop")
  const undoBinding = midiState?.bindings?.find((b) => b.targetKind === "looperUndo")

  const reelRadius = 21
  const reelCirc = 2 * Math.PI * reelRadius
  const progressLen = (Math.min(100, Math.max(0, looper.progressPct || 0)) / 100) * reelCirc
  const reelRingColor = isRec
    ? "#ff6b24"
    : isDub
      ? "#ff8c42"
      : isPlay
        ? "#34d399"
        : "rgba(255,255,255,0.28)"

  return (
    <div
      data-tour="looper-pod"
      className="h-full flex flex-col nr-obsidian-pod rounded-[10px] border border-oled-border text-oled-foreground overflow-hidden min-w-0"
    >
      <Tabs defaultValue="tape" className="flex-1 flex flex-col min-h-0 gap-0">
        {/* Top Deck Switch Header */}
        <div className="h-10 shrink-0 px-3.5 border-b border-oled-border flex items-center justify-between gap-2">
          <TabsList className="h-7 bg-white/[0.06] border-oled-border text-oled-muted">
            <TabsTrigger
              value="tape"
              className="h-5 px-2 text-[10px] data-[state=active]:bg-oled-foreground data-[state=active]:text-oled"
            >
              TAPE · LOOP
            </TabsTrigger>
            <TabsTrigger
              value="mr"
              className="h-5 px-2 text-[10px] data-[state=active]:bg-oled-foreground data-[state=active]:text-oled"
            >
              MR DECK
            </TabsTrigger>
          </TabsList>

          <div className="flex items-center gap-1.5 font-mono text-[10px] tabular-nums text-oled-muted">
            {looper.overdubLayers > 0 && (
              <Badge variant="warn" size="xs">
                L{looper.overdubLayers + 1}
              </Badge>
            )}
            <span>{Math.round(tempoBpm)} BPM</span>
          </div>
        </div>

        {/* Tab 1: TP-7 Micro Tape Looper + Dual-Stream Recorder & Re-Amp */}
        <TabsContent value="tape" className="flex-1 min-h-0 p-3 flex flex-col justify-between gap-2">
          <div className="flex items-center gap-3 rounded-[8px] border border-oled-border bg-white/[0.04] p-2.5">
            {/* Concentric Rotating Tape Reel */}
            <Hint
              content={tx(
                `루퍼 상태: ${lpState.toUpperCase()} (${formatSec(looper.durationSec)})`,
                `Looper: ${lpState.toUpperCase()} (${formatSec(looper.durationSec)})`
              )}
            >
              <div className="relative shrink-0">
                <svg width="56" height="56" viewBox="0 0 58 58">
                  <circle
                    cx="29"
                    cy="29"
                    r="26"
                    fill="rgba(0,0,0,0.45)"
                    stroke="rgba(255,255,255,0.12)"
                    strokeWidth="1.4"
                  />
                  <circle
                    cx="29"
                    cy="29"
                    r={reelRadius}
                    fill="none"
                    stroke="rgba(255,255,255,0.12)"
                    strokeWidth="2.5"
                  />
                  <circle
                    cx="29"
                    cy="29"
                    r={reelRadius}
                    fill="none"
                    stroke={reelRingColor}
                    strokeWidth="2.5"
                    strokeDasharray={`${Math.max(1.5, progressLen)} ${reelCirc}`}
                    transform="rotate(-90 29 29)"
                  />
                  <g
                    style={{ transformOrigin: "29px 29px" }}
                    className={
                      isReelSpinning
                        ? looper.reverse
                          ? "tp7-reel-spin-reverse"
                          : "tp7-reel-spin"
                        : ""
                    }
                  >
                    <circle
                      cx="29"
                      cy="29"
                      r="7.5"
                      fill="none"
                      stroke="#f5f7f6"
                      strokeWidth="1.3"
                    />
                    <circle cx="29" cy="29" r="2.1" fill="#ff6b24" />
                    <line x1="29" y1="15" x2="29" y2="21.5" stroke="#f5f7f6" strokeWidth="1.4" />
                    <line x1="17" y1="36" x2="22.5" y2="32.5" stroke="#f5f7f6" strokeWidth="1.4" />
                    <line x1="41" y1="36" x2="35.5" y2="32.5" stroke="#f5f7f6" strokeWidth="1.4" />
                  </g>
                </svg>
              </div>
            </Hint>

            {/* Transport & FX Controls */}
            <div className="flex-1 min-w-0 space-y-1.5">
              <div className="flex items-center justify-between gap-1">
                <span className="font-display text-sm font-light tabular-nums text-oled-foreground">
                  {looper.durationSec > 0 ? formatSec(looper.durationSec) : "0.0s"}
                </span>
                <Hint
                  content={`${tx("루퍼 출력 레벨", "Looper Level")}: ${Math.round((looper.level ?? 1) * 100)}%`}
                >
                  <div className="w-16">
                    <Slider
                      value={[Math.round((looper.level ?? 1) * 100)]}
                      min={0}
                      max={150}
                      step={5}
                      onValueChange={([v]) => onLooperCmd("level", v / 100)}
                    />
                  </div>
                </Hint>
              </div>

              <div className="flex flex-wrap items-center gap-1">
                <ContextMenu>
                  <ContextMenuTrigger asChild>
                    <Button
                      size="xs"
                      variant={isRec || isDub || isPlay ? "signal" : "oled"}
                      onClick={() => onLooperCmd("recDub")}
                    >
                      <Circle className="size-2.5 fill-current" />
                      {recDubLabel}
                    </Button>
                  </ContextMenuTrigger>
                  <ContextMenuContent>
                    <ContextMenuItem
                      onSelect={() => {
                        if (recBinding && onClearMidiBinding)
                          onClearMidiBinding({ targetKind: "looperRecDub" })
                        else if (onStartMidiLearn)
                          onStartMidiLearn({ targetKind: "looperRecDub" })
                      }}
                    >
                      <Radio className="size-3.5 text-primary" />
                      {recBinding
                        ? tx(`MIDI CC#${recBinding.cc} 해제`, `Clear MIDI CC#${recBinding.cc}`)
                        : tx("REC/DUB MIDI CC 학습", "Learn REC/DUB MIDI CC")}
                    </ContextMenuItem>
                  </ContextMenuContent>
                </ContextMenu>

                <ContextMenu>
                  <ContextMenuTrigger asChild>
                    <Button
                      size="icon-xs"
                      variant="oled"
                      disabled={isEmpty}
                      onClick={() => onLooperCmd("playStop")}
                    >
                      {isPlay || isDub || isRec ? (
                        <Square className="size-2.5 fill-current" />
                      ) : (
                        <Play className="size-2.5 fill-current" />
                      )}
                    </Button>
                  </ContextMenuTrigger>
                  <ContextMenuContent>
                    <ContextMenuItem
                      onSelect={() => {
                        if (playStopBinding && onClearMidiBinding)
                          onClearMidiBinding({ targetKind: "looperPlayStop" })
                        else if (onStartMidiLearn)
                          onStartMidiLearn({ targetKind: "looperPlayStop" })
                      }}
                    >
                      <Radio className="size-3.5 text-primary" />
                      {playStopBinding
                        ? tx(`MIDI CC#${playStopBinding.cc} 해제`, `Clear MIDI CC#${playStopBinding.cc}`)
                        : tx("PLAY/STOP MIDI CC 학습", "Learn PLAY/STOP MIDI CC")}
                    </ContextMenuItem>
                  </ContextMenuContent>
                </ContextMenu>

                <ContextMenu>
                  <ContextMenuTrigger asChild>
                    <Button
                      size="icon-xs"
                      variant="oled"
                      disabled={!looper.canUndo && !looper.canRedo}
                      onClick={() => onLooperCmd(looper.canUndo ? "undo" : "redo")}
                    >
                      {looper.canRedo ? <Redo2 className="size-3" /> : <Undo2 className="size-3" />}
                    </Button>
                  </ContextMenuTrigger>
                  <ContextMenuContent>
                    <ContextMenuItem
                      onSelect={() => {
                        if (undoBinding && onClearMidiBinding)
                          onClearMidiBinding({ targetKind: "looperUndo" })
                        else if (onStartMidiLearn)
                          onStartMidiLearn({ targetKind: "looperUndo" })
                      }}
                    >
                      <Radio className="size-3.5 text-primary" />
                      {undoBinding
                        ? tx(`MIDI CC#${undoBinding.cc} 해제`, `Clear MIDI CC#${undoBinding.cc}`)
                        : tx("UNDO MIDI CC 학습", "Learn UNDO MIDI CC")}
                    </ContextMenuItem>
                  </ContextMenuContent>
                </ContextMenu>

                <Hint content={tx("루퍼 버퍼 지우기", "Clear Looper Buffer")}>
                  <Button
                    size="icon-xs"
                    variant="oled"
                    disabled={isEmpty}
                    onClick={() => onLooperCmd("clear")}
                  >
                    <Trash2 className="size-3" />
                  </Button>
                </Hint>
              </div>

              <div className="flex items-center gap-1">
                <Hint content={tx("0.5배속 재생", "Half-Speed (0.5x)")}>
                  <Toggle
                    size="xs"
                    variant="oled"
                    pressed={looper.halfSpeed}
                    onPressedChange={(v) => onLooperCmd("halfSpeed", v)}
                  >
                    ½X
                  </Toggle>
                </Hint>
                <Hint content={tx("역방향 재생", "Reverse Playback")}>
                  <Toggle
                    size="xs"
                    variant="oled"
                    pressed={looper.reverse}
                    onPressedChange={(v) => onLooperCmd("reverse", v)}
                  >
                    <RotateCcw className="size-2.5" />
                    REV
                  </Toggle>
                </Hint>
                <Hint content={tx("BPM 박자 퀀타이즈 동기화", "BPM Quantize Snap")}>
                  <Toggle
                    size="xs"
                    variant="oled"
                    pressed={looper.quantizeSync}
                    onPressedChange={(v) => onLooperCmd("quantize", v)}
                  >
                    <Gauge className="size-2.5" />
                    SYNC
                  </Toggle>
                </Hint>
              </div>
            </div>
          </div>

          {/* Lower Bay: Dual-Stream Dry/Wet Recorder & 1-Click Re-Amp */}
          <div className="rounded-[8px] border border-oled-border bg-white/[0.04] px-2.5 py-2 space-y-1.5">
            <div className="flex items-center justify-between gap-1.5">
              <span className="font-mono text-[9.5px] tracking-[0.12em] text-oled-muted">
                REC · RE-AMP
              </span>
              <div className="flex items-center gap-1">
                <Hint
                  content={tx(
                    "Dry DI WAV 파일 불러와 실시간 리앰핑",
                    "Load Dry DI .wav for live re-amping"
                  )}
                >
                  <Button
                    size="icon-xs"
                    variant="oled"
                    onClick={() => onRecorderCmd("loadReampFile")}
                  >
                    <Upload className="size-3" />
                  </Button>
                </Hint>
                <Hint
                  content={tx(
                    `녹음 폴더 열기 (${recorder.recordingsDir || "Recordings"})`,
                    `Open recordings folder (${recorder.recordingsDir || "Recordings"})`
                  )}
                >
                  <Button
                    size="icon-xs"
                    variant="oled"
                    onClick={() => onRecorderCmd("openRecordingsFolder")}
                  >
                    <FolderOpen className="size-3" />
                  </Button>
                </Hint>
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-1.5">
              <Button
                size="xs"
                variant={recorder.recording ? "signal" : "oled"}
                disabled={!isAudioRunning && !recorder.recording}
                onClick={() => onRecorderCmd(recorder.recording ? "stopRec" : "startRec")}
              >
                <Circle className="size-2.5 fill-current" />
                {recorder.recording ? `STOP ${formatSec(recorder.recordedSec)}` : "REC DI+WET"}
              </Button>

              <Button
                size="xs"
                variant={recorder.reampEnabled ? "signal" : "oled"}
                disabled={!recorder.reampAvailable}
                onClick={() => onRecorderCmd("toggleReamp")}
              >
                <Radio className="size-3" />
                {recorder.reampEnabled ? "RE-AMP ON" : "RE-AMP"}
              </Button>

              {recorder.reampAvailable && (
                <>
                  <Button
                    size="icon-xs"
                    variant="oled"
                    onClick={() => onRecorderCmd("playPauseReamp")}
                  >
                    {recorder.reampPlaying ? (
                      <Pause className="size-2.5" />
                    ) : (
                      <Play className="size-2.5" />
                    )}
                  </Button>
                  <Toggle
                    size="xs"
                    variant="oled"
                    pressed={recorder.reampLoop}
                    onPressedChange={(v) => onRecorderCmd("loopReamp", { value: v })}
                  >
                    <Repeat className="size-2.5" />
                  </Toggle>
                  <span className="font-mono text-[10px] tabular-nums text-oled-muted truncate max-w-[78px]">
                    {recorder.reampDurationSec > 0
                      ? `${formatSec(recorder.reampPositionSec)}/${formatSec(recorder.reampDurationSec)}`
                      : recorder.reampName}
                  </span>
                </>
              )}
            </div>
          </div>
        </TabsContent>

        {/* Tab 2: Backing Track (MR) Transport */}
        <TabsContent value="mr" className="flex-1 min-h-0 p-3 flex flex-col justify-between gap-2">
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-1.5 min-w-0">
              <Music className="size-3.5 text-primary shrink-0" />
              <span className="text-xs font-medium truncate text-oled-foreground">
                {backing.loaded
                  ? backing.name
                  : tx("MR 백킹 트랙 없음", "No backing track loaded")}
              </span>
            </div>
            <div className="flex items-center gap-1 shrink-0">
              <Button size="xs" variant="oled" onClick={onLoadBacking}>
                <FolderOpen className="size-3" />
                {tx("MR 열기", "LOAD")}
              </Button>
              {backing.loaded && (
                <Button size="icon-xs" variant="oled" onClick={onUnloadBacking}>
                  <Trash2 className="size-3" />
                </Button>
              )}
            </div>
          </div>

          {backing.loaded ? (
            <div className="space-y-3 rounded-[8px] border border-oled-border bg-white/[0.04] p-2.5">
              <div className="flex items-center gap-2">
                <Button
                  size="icon-xs"
                  variant={backing.playing ? "signal" : "oled"}
                  onClick={() => onControlBacking({ playing: !backing.playing })}
                >
                  {backing.playing ? <Pause className="size-3" /> : <Play className="size-3" />}
                </Button>
                <Toggle
                  size="xs"
                  variant="oled"
                  pressed={backing.loop}
                  onPressedChange={(v) => onControlBacking({ loop: v })}
                >
                  <Repeat className="size-3" />
                </Toggle>
                <div className="flex-1">
                  <Slider
                    value={[backing.positionSec]}
                    min={0}
                    max={Math.max(1, backing.durationSec)}
                    step={0.5}
                    onValueChange={([v]) => onControlBacking({ seekSec: v })}
                  />
                </div>
                <span className="font-mono text-[10px] tabular-nums text-oled-muted">
                  {formatSec(backing.positionSec)} / {formatSec(backing.durationSec)}
                </span>
              </div>

              <div className="h-px bg-oled-border" />

              <div className="flex items-center gap-2">
                <Volume2 className="size-3.5 text-oled-muted shrink-0" />
                <Slider
                  value={[Math.round(backing.volume * 100)]}
                  min={0}
                  max={100}
                  step={1}
                  onValueChange={([v]) => onControlBacking({ volume: v / 100 })}
                />
                <span className="font-mono text-[10px] tabular-nums w-9 text-right text-oled-foreground">
                  {Math.round(backing.volume * 100)}%
                </span>
              </div>
            </div>
          ) : (
            <div className="flex-1 rounded-[8px] border border-dashed border-oled-border bg-white/[0.02] flex flex-col items-center justify-center p-4 text-center">
              <span className="text-[11px] text-oled-muted leading-relaxed">
                {tx(
                  "WAV / MP3 반주 파일을 불러와 톤에 맞춰 합주하세요 (단축키 Space)",
                  "Load a WAV / MP3 backing track to jam along (Shortcut: Space)"
                )}
              </span>
            </div>
          )}
        </TabsContent>
      </Tabs>
    </div>
  )
}
