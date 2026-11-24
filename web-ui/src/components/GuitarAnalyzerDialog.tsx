import { useEffect, useState } from "react"
import {
  AudioWaveform,
  CheckCircle2,
  Guitar,
  Play,
  RotateCcw,
  SkipForward,
  Sliders,
  Sparkles,
  Trash2,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Progress } from "@/components/ui/progress"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Hint } from "@/components/ui/tooltip"
import { studioBridge } from "@/lib/bridge"
import { useI18n } from "@/lib/i18n"
import type { GuitarAnalysisLiveStateDto, GuitarProfileDto } from "@/types/rig"

interface GuitarAnalyzerContentProps {
  isAudioRunning: boolean
  isAudioBusy: boolean
  onStartAudio: () => void
  onOpenAudioSettings: () => void
  onCloseParent?: () => void
  profiles: GuitarProfileDto[]
  activeProfileId: string
  liveAnalysis: GuitarAnalysisLiveStateDto | null
  onLiveAnalysisChange: (next: GuitarAnalysisLiveStateDto | null) => void
  onProfilesUpdated: (profiles: GuitarProfileDto[], activeId: string) => void
  onLog: (text: string, level?: "info" | "success" | "error") => void
}

function GuitarProfileDetail({ profile }: { profile: GuitarProfileDto }) {
  const { tx } = useI18n()
  const bands = [
    { label: "LOW", range: "70-200", db: profile.lowBandDb },
    { label: "LO-MID", range: "200-500", db: profile.lowMidBandDb },
    { label: "MID", range: "500-1.2k", db: profile.midBandDb },
    { label: "HI-MID", range: "1.2k-2.8k", db: profile.highMidBandDb },
    { label: "TREBLE", range: "2.8k-7.5k", db: profile.trebleBandDb },
  ]

  return (
    <div className="space-y-3 text-xs">
      <div className="flex flex-wrap items-center justify-between gap-2 rounded-sm border border-border bg-secondary/45 px-3 py-2">
        <div className="space-y-0.5">
          <div className="font-mono text-[10px] uppercase text-muted-foreground">
            {tx("추정 픽업 / 악기 성향", "ESTIMATED PICKUP PROFILE")}
          </div>
          <div className="font-display text-sm font-bold text-foreground">
            {profile.estimatedPickupType}
          </div>
        </div>
        <div className="flex items-center gap-1.5 font-mono tabular-nums">
          <Badge variant="secondary" size="xs">
            TRIM {profile.suggestedInputTrimDb >= 0 ? `+${profile.suggestedInputTrimDb.toFixed(1)}` : profile.suggestedInputTrimDb.toFixed(1)} dB
          </Badge>
          <Badge variant="outline" size="xs">
            GATE {profile.suggestedGateDb.toFixed(0)} dB
          </Badge>
        </div>
      </div>

      <div className="grid grid-cols-3 gap-2">
        {[
          { label: tx("밝기", "BRIGHTNESS"), val: profile.brightnessScore },
          { label: tx("따뜻함", "WARMTH"), val: profile.warmthScore },
          { label: tx("출력 파워", "OUTPUT"), val: profile.outputPowerScore },
        ].map((trait) => (
          <Card key={trait.label} className="py-2.5 gap-1.5">
            <CardContent className="px-3 space-y-1.5">
              <div className="flex items-center justify-between font-mono text-[10px]">
                <span className="text-muted-foreground">{trait.label}</span>
                <span className="font-bold tabular-nums">{trait.val}/100</span>
              </div>
              <Progress value={trait.val} className="h-1.5" />
            </CardContent>
          </Card>
        ))}
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-2.5">
        <Card className="py-2.5 gap-2">
          <CardHeader className="px-3 pb-0">
            <CardTitle className="font-mono text-[10px] uppercase text-muted-foreground">
              {tx("5밴드 주파수 밸런스 (상대 dB)", "5-BAND TILT BALANCE (REL dB)")}
            </CardTitle>
          </CardHeader>
          <CardContent className="px-3">
            <div className="grid grid-cols-5 gap-1.5 items-end h-20 pt-2">
              {bands.map((b) => {
                const pct = Math.min(100, Math.max(10, ((b.db + 12) / 24) * 100))
                return (
                  <div key={b.label} className="flex flex-col items-center justify-end h-full gap-1">
                    <span className="text-[10px] font-mono tabular-nums text-muted-foreground">
                      {b.db >= 0 ? `+${b.db.toFixed(1)}` : b.db.toFixed(1)}
                    </span>
                    <div className="w-full flex-1 flex items-end justify-center bg-secondary rounded-[2px] overflow-hidden border border-border">
                      <div
                        className="w-full bg-primary transition-all"
                        style={{ height: `${pct}%` }}
                      />
                    </div>
                    <span className="text-[9px] font-mono font-bold text-foreground">{b.label}</span>
                  </div>
                )
              })}
            </div>
          </CardContent>
        </Card>

        <Card className="py-2.5 gap-2">
          <CardHeader className="px-3 pb-0">
            <CardTitle className="font-mono text-[10px] uppercase text-muted-foreground">
              {tx("전기적 / 음향 실측 지표", "ELECTRICAL & ACOUSTIC METRICS")}
            </CardTitle>
          </CardHeader>
          <CardContent className="px-3 grid grid-cols-2 gap-x-3 gap-y-1 text-[11px] font-mono tabular-nums">
            <div className="flex justify-between">
              <span className="text-muted-foreground">{tx("노이즈", "NOISE")}</span>
              <span>{profile.noiseFloorDb.toFixed(1)}dB</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">60Hz HUM</span>
              <span>{profile.hum60HzDb.toFixed(1)}dB</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">{tx("피크", "PEAK")}</span>
              <span>{profile.peakDb.toFixed(1)}dB</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">CREST</span>
              <span>{profile.crestFactorDb.toFixed(1)}dB</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">{tx("서스테인", "SUSTAIN")}</span>
              <span>{profile.sustainSec.toFixed(2)}s</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">CENTROID</span>
              <span>{profile.spectralCentroidHz.toFixed(0)}Hz</span>
            </div>
          </CardContent>
        </Card>
      </div>

      {profile.notes?.length > 0 && (
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-1.5">
          {profile.notes.map((n) => (
            <div
              key={n.stepId}
              className="rounded-sm border border-border bg-secondary/30 px-2 py-1.5 space-y-0.5 text-[10px] font-mono tabular-nums"
            >
              <div className="flex items-center justify-between font-bold">
                <span>{n.targetNote}</span>
                {n.skipped ? (
                  <span className="text-muted-foreground">SKIP</span>
                ) : (
                  <span className="text-muted-foreground">{n.detectedFreqHz.toFixed(1)}Hz</span>
                )}
              </div>
              {!n.skipped && (
                <div className="flex items-center justify-between text-muted-foreground">
                  <span>H2 {n.h2RatioDb.toFixed(1)}dB</span>
                  <span>{n.sustainSec.toFixed(1)}s</span>
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {profile.toneRecommendation && (
        <div className="rounded-sm border border-border bg-secondary/45 px-3 py-2 text-[11px] leading-relaxed text-foreground flex gap-2 items-start">
          <Sparkles className="size-3.5 mt-0.5 shrink-0 text-primary" />
          <span>{profile.toneRecommendation}</span>
        </div>
      )}
    </div>
  )
}

export function GuitarAnalyzerContent({
  isAudioRunning,
  isAudioBusy,
  onStartAudio,
  onOpenAudioSettings,
  onCloseParent,
  profiles,
  activeProfileId,
  liveAnalysis,
  onLiveAnalysisChange,
  onProfilesUpdated,
  onLog,
}: GuitarAnalyzerContentProps) {
  const { tx } = useI18n()
  const [tab, setTab] = useState<"analyze" | "profiles">("analyze")
  const [guitarName, setGuitarName] = useState("My Guitar")
  const [saveName, setSaveName] = useState("My Guitar")
  const [selectedProfileId, setSelectedProfileId] = useState(
    activeProfileId || profiles[0]?.id || ""
  )
  const [busyAction, setBusyAction] = useState(false)

  const stepLabels = [
    { id: "silence", short: tx("1. 무음", "1. MUTE") },
    { id: "note_e2", short: "2. E2" },
    { id: "note_d3", short: "3. D3" },
    { id: "note_b3", short: "4. B3" },
    { id: "note_e4", short: "5. E4" },
    { id: "strum", short: tx("6. 스트럼", "6. STRUM") },
  ]

  useEffect(() => {
    if (liveAnalysis?.completed && liveAnalysis.draftProfile) {
      setSaveName(liveAnalysis.draftProfile.name || guitarName)
    }
  }, [liveAnalysis?.completed, liveAnalysis?.draftProfile?.id])

  useEffect(() => {
    if (activeProfileId && profiles.some((p) => p.id === activeProfileId)) {
      setSelectedProfileId(activeProfileId)
    } else if (profiles.length > 0 && !profiles.some((p) => p.id === selectedProfileId)) {
      setSelectedProfileId(profiles[0].id)
    } else if (profiles.length === 0) {
      setSelectedProfileId("")
    }
  }, [activeProfileId, profiles])

  const handleStart = async () => {
    if (!studioBridge.isWebView2) return
    setBusyAction(true)
    try {
      const st = await studioBridge.invoke<GuitarAnalysisLiveStateDto>("startGuitarAnalysis", {
        name: guitarName.trim() || "My Guitar",
      })
      onLiveAnalysisChange(st)
    } catch (e) {
      onLog(tx(`기타 분석 시작 실패: ${String(e)}`, `Analysis start failed: ${String(e)}`), "error")
    } finally {
      setBusyAction(false)
    }
  }

  const handleCancel = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const st = await studioBridge.invoke<GuitarAnalysisLiveStateDto>("cancelGuitarAnalysis")
      onLiveAnalysisChange(st)
    } catch {
      // ignore
    }
  }

  const handleSkipStep = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const st = await studioBridge.invoke<GuitarAnalysisLiveStateDto>("skipGuitarAnalysisStep")
      onLiveAnalysisChange(st)
    } catch (e) {
      onLog(tx(`단계 건너뛰기 오류: ${String(e)}`, `Skip step error: ${String(e)}`), "error")
    }
  }

  const handleSaveProfile = async (alsoCalibrate: boolean) => {
    if (!studioBridge.isWebView2 || !liveAnalysis?.draftProfile) return
    setBusyAction(true)
    try {
      const res = await studioBridge.invoke<{
        profile: GuitarProfileDto
        guitarProfiles: GuitarProfileDto[]
        activeGuitarProfileId: string
      }>("saveGuitarProfile", {
        name: saveName.trim() || liveAnalysis.draftProfile.name,
        profile: liveAnalysis.draftProfile,
      })
      onProfilesUpdated(res.guitarProfiles || [], res.activeGuitarProfileId || "")
      onLiveAnalysisChange(null)
      setSelectedProfileId(res.profile.id)

      if (alsoCalibrate) {
        await studioBridge.invoke("applyGuitarCalibration", { id: res.profile.id })
        onLog(
          tx(
            `'${res.profile.name}' 저장 및 Input Trim/Gate 보정 적용 완료`,
            `Saved '${res.profile.name}' and applied Input Trim/Gate calibration`
          ),
          "success"
        )
      } else {
        onLog(
          tx(`'${res.profile.name}' 기타 프로필 저장 완료`, `Saved '${res.profile.name}' profile`),
          "success"
        )
      }
      setTab("profiles")
    } catch (e) {
      onLog(tx(`프로필 저장 실패: ${String(e)}`, `Save failed: ${String(e)}`), "error")
    } finally {
      setBusyAction(false)
    }
  }

  const handleSelectProfile = async (id: string) => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{
        guitarProfiles: GuitarProfileDto[]
        activeGuitarProfileId: string
      }>("selectGuitarProfile", { id })
      onProfilesUpdated(res.guitarProfiles || [], res.activeGuitarProfileId || "")
      setSelectedProfileId(id)
    } catch (e) {
      onLog(tx(`기타 선택 오류: ${String(e)}`, `Select error: ${String(e)}`), "error")
    }
  }

  const handleDeleteProfile = async (id: string) => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{
        guitarProfiles: GuitarProfileDto[]
        activeGuitarProfileId: string
      }>("deleteGuitarProfile", { id })
      onProfilesUpdated(res.guitarProfiles || [], res.activeGuitarProfileId || "")
    } catch (e) {
      onLog(tx(`기타 삭제 오류: ${String(e)}`, `Delete error: ${String(e)}`), "error")
    }
  }

  const handleApplyCalibration = async (id: string) => {
    if (!studioBridge.isWebView2 || !id) return
    setBusyAction(true)
    try {
      const res = await studioBridge.invoke<{ inputGainDb: number; gateDb: number }>(
        "applyGuitarCalibration",
        { id }
      )
      onLog(
        tx(
          `보정 적용 완료: Input ${res.inputGainDb >= 0 ? "+" : ""}${res.inputGainDb.toFixed(1)} dB, Gate ${res.gateDb.toFixed(0)} dB`,
          `Calibration applied: Input ${res.inputGainDb >= 0 ? "+" : ""}${res.inputGainDb.toFixed(1)} dB, Gate ${res.gateDb.toFixed(0)} dB`
        ),
        "success"
      )
    } catch (e) {
      onLog(tx(`체인 보정 실패: ${String(e)}`, `Calibration failed: ${String(e)}`), "error")
    } finally {
      setBusyAction(false)
    }
  }

  const selectedProfile =
    profiles.find((p) => p.id === selectedProfileId) ||
    profiles.find((p) => p.id === activeProfileId) ||
    profiles[0] ||
    null

  const isRunningWizard = Boolean(liveAnalysis?.active && !liveAnalysis.completed)
  const isCompletedWizard = Boolean(liveAnalysis?.completed && liveAnalysis.draftProfile)
  const levelPct = liveAnalysis
    ? Math.min(100, Math.max(0, ((liveAnalysis.liveLevelDb + 82) / 82) * 100))
    : 0

  return (
    <Tabs value={tab} onValueChange={(v) => setTab(v as "analyze" | "profiles")} className="flex-1 min-h-0 flex flex-col">
      <TabsList className="grid w-full grid-cols-2 shrink-0">
        <TabsTrigger value="analyze">
          <AudioWaveform className="size-3.5" />
          {tx("DI 가이드 분석", "DI ANALYZER")}
        </TabsTrigger>
        <TabsTrigger value="profiles">
          <Sliders className="size-3.5" />
          {tx(`저장된 기타 (${profiles.length})`, `PROFILES (${profiles.length})`)}
        </TabsTrigger>
      </TabsList>

      <TabsContent value="analyze" className="space-y-3 mt-1 min-h-0 overflow-y-auto">
        {!isAudioRunning && !isCompletedWizard && (
          <div className="rounded-sm border border-border bg-secondary/50 p-3 flex flex-wrap items-center justify-between gap-2 text-xs">
            <div className="space-y-0.5">
              <div className="font-semibold text-foreground">
                {tx("먼저 오디오 엔진을 시작하세요", "Start Audio Engine First")}
              </div>
              <div className="text-[11px] text-muted-foreground">
                {tx(
                  "드라이 DI 원음을 분석하려면 오디오 입력이 켜져 있어야 합니다.",
                  "Dry DI analysis requires an active audio input stream."
                )}
              </div>
            </div>
            <div className="flex items-center gap-1.5">
              <Button size="xs" variant="ghost" onClick={onOpenAudioSettings}>
                {tx("오디오 설정", "AUDIO I/O")}
              </Button>
              <Button size="xs" variant="keycap" disabled={isAudioBusy} onClick={onStartAudio}>
                <Play className="size-3" />
                {tx("오디오 시작", "START AUDIO")}
              </Button>
            </div>
          </div>
        )}

        {!isRunningWizard && !isCompletedWizard && (
          <Card className="py-3 gap-3">
            <CardContent className="px-4 space-y-3.5">
              <div className="space-y-1.5">
                <Label>{tx("기타 이름 / 픽업 위치 메모", "Guitar Name / Pickup Position")}</Label>
                <Input
                  value={guitarName}
                  onChange={(e) => setGuitarName(e.target.value)}
                  placeholder="Fender Strat (Neck) / Gibson Les Paul (Bridge)"
                />
              </div>

              <div className="space-y-1.5">
                <div className="font-mono text-[10px] uppercase text-muted-foreground">
                  {tx("6단계 측정 시퀀스 (약 12초)", "6-STEP SEQUENCE (~12s)")}
                </div>
                <div className="grid grid-cols-3 sm:grid-cols-6 gap-1.5">
                  {stepLabels.map((s) => (
                    <div
                      key={s.id}
                      className="rounded-sm border border-border bg-secondary/50 px-2 py-1.5 text-center font-mono text-[10px] font-bold text-foreground"
                    >
                      {s.short}
                    </div>
                  ))}
                </div>
              </div>

              <div className="rounded-sm border border-border bg-background p-3 text-[11px] text-muted-foreground space-y-1 leading-relaxed">
                <div>
                  • {tx("기타 볼륨·톤 노브를 최대로 맞추고 튜닝을 확인하세요.", "Set guitar volume & tone to max and verify tuning.")}
                </div>
                <div>
                  • {tx("순수 픽업 출력, 60Hz 험, 밝기, 서스테인을 정밀 계측해 저장합니다.", "Measures raw pickup output, 60Hz hum, brightness, and sustain.")}
                </div>
              </div>

              <div className="flex justify-end">
                <Button
                  size="sm"
                  variant="signal"
                  disabled={!isAudioRunning || busyAction}
                  onClick={handleStart}
                >
                  <AudioWaveform className="size-3.5" />
                  {tx("기타 분석 시작", "START PROFILING")}
                </Button>
              </div>
            </CardContent>
          </Card>
        )}

        {isRunningWizard && liveAnalysis && (
          <div className="space-y-3">
            <div className="grid grid-cols-6 gap-1.5">
              {stepLabels.map((s, idx) => {
                const done = idx < liveAnalysis.stepIndex
                const current = idx === liveAnalysis.stepIndex
                return (
                  <div
                    key={s.id}
                    className={`rounded-sm border px-1.5 py-1.5 text-center font-mono text-[10px] font-bold ${
                      done
                        ? "border-signal-ok/50 bg-signal-ok/15 text-signal-ok"
                        : current
                          ? "border-primary bg-primary text-primary-foreground"
                          : "border-border bg-secondary/40 text-muted-foreground"
                    }`}
                  >
                    {s.short}
                  </div>
                )
              })}
            </div>

            <Card className="py-3 gap-3">
              <CardContent className="px-4 space-y-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <Badge variant="outline" size="xs">
                        STEP {liveAnalysis.stepIndex + 1} / {liveAnalysis.totalSteps}
                      </Badge>
                      <span className="font-display text-sm font-bold">{liveAnalysis.stepTitle}</span>
                    </div>
                    <p className="text-xs text-muted-foreground">{liveAnalysis.instruction}</p>
                  </div>

                  <div
                    className={`shrink-0 rounded-sm border px-3 py-1.5 text-center min-w-[84px] bg-oled ${
                      liveAnalysis.isMatched ? "border-signal-ok" : "border-oled-border"
                    }`}
                  >
                    <div className="text-base font-bold font-mono text-oled-foreground">
                      {liveAnalysis.targetNote}
                    </div>
                    <div className="text-[10px] font-mono tabular-nums text-oled-muted">
                      {liveAnalysis.targetFreqHz > 0
                        ? `${liveAnalysis.targetFreqHz.toFixed(1)} Hz`
                        : liveAnalysis.stepId === "silence"
                          ? "MUTE"
                          : "STRUM"}
                    </div>
                  </div>
                </div>

                <div className="grid grid-cols-3 gap-2 rounded-sm border border-border bg-background p-2.5 text-xs">
                  <div>
                    <div className="font-mono text-[10px] text-muted-foreground">
                      {tx("감지 음정", "DETECTED")}
                    </div>
                    <div className="font-mono font-bold text-sm mt-0.5 tabular-nums">
                      {liveAnalysis.liveNote}
                    </div>
                  </div>
                  <div>
                    <div className="font-mono text-[10px] text-muted-foreground">
                      {tx("드라이 DI 레벨", "DRY DI LEVEL")}
                    </div>
                    <div className="mt-1 space-y-1">
                      <Progress value={levelPct} className="h-1.5" />
                      <div className="font-mono text-[10px] tabular-nums text-muted-foreground">
                        {liveAnalysis.liveLevelDb.toFixed(1)} dBFS
                      </div>
                    </div>
                  </div>
                  <div className="flex items-center justify-end">
                    <Badge variant={liveAnalysis.isMatched ? "ok" : "secondary"} size="xs">
                      {liveAnalysis.isMatched ? tx("캡처 중", "CAPTURING") : tx("대기 중", "WAITING")}
                    </Badge>
                  </div>
                </div>

                {isAudioRunning && (liveAnalysis.livePeakDb ?? -96) <= -85 && (
                  <div className="rounded-sm border border-signal-warn/50 bg-signal-warn/10 px-3 py-2 flex items-center justify-between gap-2 text-[11px]">
                    <span className="text-signal-warn">
                      {tx("입력 신호가 매우 낮습니다. 오디오 I/O 채널을 확인하세요.", "Input signal too low. Check Audio I/O channels.")}
                    </span>
                    <Button
                      size="xs"
                      variant="keycap"
                      onClick={() => {
                        onCloseParent?.()
                        onOpenAudioSettings()
                      }}
                    >
                      <Sliders className="size-3" />
                      AUDIO I/O
                    </Button>
                  </div>
                )}

                <div className="space-y-1">
                  <div className="flex items-center justify-between text-[11px]">
                    <span>{liveAnalysis.statusHint}</span>
                    <span className="font-mono font-bold tabular-nums">
                      {liveAnalysis.progressPct.toFixed(0)}%
                    </span>
                  </div>
                  <Progress value={liveAnalysis.progressPct} className="h-2" />
                </div>

                <div className="flex items-center justify-between pt-1">
                  <Button size="xs" variant="ghost" onClick={handleCancel}>
                    {tx("분석 취소", "Cancel")}
                  </Button>
                  <Button size="xs" variant="keycap" onClick={handleSkipStep}>
                    <SkipForward className="size-3" />
                    {tx("건너뛰기", "Skip Step")}
                  </Button>
                </div>
              </CardContent>
            </Card>
          </div>
        )}

        {isCompletedWizard && liveAnalysis?.draftProfile && (
          <div className="space-y-3">
            <div className="flex items-center gap-2">
              <CheckCircle2 className="size-4 text-signal-ok shrink-0" />
              <span className="text-xs font-bold">
                {tx("측정 완료! 프로필 이름을 확인하고 저장하세요.", "Measurement complete! Name and save profile.")}
              </span>
            </div>

            <div className="flex items-center gap-2">
              <Label className="shrink-0">{tx("프로필 이름", "Name")}</Label>
              <Input value={saveName} onChange={(e) => setSaveName(e.target.value)} />
            </div>

            <GuitarProfileDetail profile={liveAnalysis.draftProfile} />

            <div className="flex flex-wrap items-center justify-between gap-2 pt-1">
              <Button
                size="sm"
                variant="ghost"
                onClick={handleStart}
                disabled={!isAudioRunning || busyAction}
              >
                <RotateCcw className="size-3.5" />
                {tx("다시 측정", "Re-measure")}
              </Button>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="keycap"
                  disabled={busyAction}
                  onClick={() => handleSaveProfile(false)}
                >
                  {tx("프로필만 저장", "Save Profile")}
                </Button>
                <Button
                  size="sm"
                  variant="signal"
                  disabled={busyAction}
                  onClick={() => handleSaveProfile(true)}
                >
                  <Sliders className="size-3.5" />
                  {tx("저장 + 체인 보정", "Save + Calibrate")}
                </Button>
              </div>
            </div>
          </div>
        )}
      </TabsContent>

      <TabsContent value="profiles" className="space-y-3 mt-1 min-h-0 overflow-y-auto">
        {profiles.length === 0 ? (
          <Card className="py-6">
            <CardContent className="text-center space-y-2">
              <div className="text-xs text-muted-foreground">
                {tx("저장된 기타 프로필이 없습니다.", "No saved guitar profiles yet.")}
              </div>
              <Button size="sm" variant="keycap" onClick={() => setTab("analyze")}>
                {tx("첫 번째 기타 분석하기", "Analyze First Guitar")}
              </Button>
            </CardContent>
          </Card>
        ) : (
          <div className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
              {profiles.map((g) => {
                const isActive = g.id === activeProfileId
                const isSelected = g.id === selectedProfile?.id
                return (
                  <div
                    key={g.id}
                    onClick={() => setSelectedProfileId(g.id)}
                    className={`cursor-pointer rounded-sm border p-2.5 flex items-start justify-between gap-2 transition-colors ${
                      isSelected ? "border-primary bg-secondary/70" : "border-border bg-card hover:bg-secondary/40"
                    }`}
                  >
                    <div className="space-y-0.5 min-w-0">
                      <div className="flex items-center gap-1.5">
                        <span className="text-xs font-bold truncate">{g.name}</span>
                        {isActive && (
                          <Badge variant="ok" size="xs">
                            ACTIVE
                          </Badge>
                        )}
                      </div>
                      <div className="text-[11px] text-muted-foreground truncate">
                        {g.estimatedPickupType}
                      </div>
                      <div className="text-[10px] font-mono tabular-nums text-muted-foreground">
                        BRT {g.brightnessScore} · OUT {g.outputPowerScore}
                      </div>
                    </div>
                    <Hint content={tx("기타 프로필 삭제", "Delete profile")}>
                      <Button
                        size="icon-xs"
                        variant="ghost"
                        onClick={(e) => {
                          e.stopPropagation()
                          void handleDeleteProfile(g.id)
                        }}
                      >
                        <Trash2 className="size-3.5" />
                      </Button>
                    </Hint>
                  </div>
                )
              })}
            </div>

            {selectedProfile && (
              <div className="space-y-3">
                <GuitarProfileDetail profile={selectedProfile} />
                <div className="flex flex-wrap items-center justify-between gap-2 rounded-sm border border-border bg-secondary/35 px-3 py-2">
                  <span className="text-[11px] text-muted-foreground">
                    {tx(
                      "활성 기타 스펙은 AI 톤 설계 시 자동 참조됩니다.",
                      "Active guitar profile informs AI tone synthesis."
                    )}
                  </span>
                  <div className="flex items-center gap-2">
                    {selectedProfile.id !== activeProfileId && (
                      <Button
                        size="xs"
                        variant="keycap"
                        onClick={() => handleSelectProfile(selectedProfile.id)}
                      >
                        {tx("활성 기타로 선택", "Set Active")}
                      </Button>
                    )}
                    <Button
                      size="xs"
                      variant="signal"
                      disabled={busyAction}
                      onClick={() => handleApplyCalibration(selectedProfile.id)}
                    >
                      <Sliders className="size-3" />
                      {tx("Trim & Gate 적용", "Apply Trim & Gate")}
                    </Button>
                  </div>
                </div>
              </div>
            )}
          </div>
        )}
      </TabsContent>
    </Tabs>
  )
}

interface GuitarAnalyzerDialogProps extends GuitarAnalyzerContentProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function GuitarAnalyzerDialog({
  open,
  onOpenChange,
  ...props
}: GuitarAnalyzerDialogProps) {
  const { tx } = useI18n()

  const handleDialogOpenChange = async (nextOpen: boolean) => {
    if (
      !nextOpen &&
      props.liveAnalysis?.active &&
      !props.liveAnalysis.completed &&
      studioBridge.isWebView2
    ) {
      try {
        const st = await studioBridge.invoke<GuitarAnalysisLiveStateDto>("cancelGuitarAnalysis")
        props.onLiveAnalysisChange(st)
      } catch {
        // ignore
      }
    }
    onOpenChange(nextOpen)
  }

  return (
    <Dialog open={open} onOpenChange={handleDialogOpenChange}>
      <DialogContent className="max-w-2xl max-h-[86vh] flex flex-col">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Guitar className="size-4 text-primary" />
            GUITAR DI PROFILER
          </DialogTitle>
          <DialogDescription>
            {tx(
              "6단계 실측으로 픽업 출력·주파수 밸런스·60Hz 험을 분석하고 입력 트림을 자동 보정합니다.",
              "Analyze pickup output, spectral tilt, and 60Hz hum across 6 steps to calibrate input trim."
            )}
          </DialogDescription>
        </DialogHeader>
        <ScrollArea className="flex-1 min-h-0 pr-2">
          <GuitarAnalyzerContent {...props} onCloseParent={() => onOpenChange(false)} />
        </ScrollArea>
      </DialogContent>
    </Dialog>
  )
}
