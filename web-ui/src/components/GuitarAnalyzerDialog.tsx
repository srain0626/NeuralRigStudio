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
  Usb,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Progress } from "@/components/ui/progress"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { studioBridge } from "@/lib/bridge"
import type { GuitarAnalysisLiveStateDto, GuitarProfileDto } from "@/types/rig"

const STEP_LABELS = [
  { id: "silence", short: "1. 무음" },
  { id: "note_e2", short: "2. E2" },
  { id: "note_d3", short: "3. D3" },
  { id: "note_b3", short: "4. B3" },
  { id: "note_e4", short: "5. E4" },
  { id: "strum", short: "6. 스트럼" },
]

interface GuitarAnalyzerDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  isAudioRunning: boolean
  isAudioBusy: boolean
  hasGp100: boolean
  onStartAudio: () => void
  onQuickStartGp100: () => void
  onOpenAudioSettings: () => void
  profiles: GuitarProfileDto[]
  activeProfileId: string
  liveAnalysis: GuitarAnalysisLiveStateDto | null
  onLiveAnalysisChange: (next: GuitarAnalysisLiveStateDto | null) => void
  onProfilesUpdated: (profiles: GuitarProfileDto[], activeId: string) => void
  onLog: (text: string, level?: "info" | "success" | "error") => void
}

function GuitarProfileDetail({ profile }: { profile: GuitarProfileDto }) {
  const bands = [
    { label: "Low", range: "70-200", db: profile.lowBandDb },
    { label: "Low-Mid", range: "200-500", db: profile.lowMidBandDb },
    { label: "Mid", range: "500-1.2k", db: profile.midBandDb },
    { label: "Hi-Mid", range: "1.2k-2.8k", db: profile.highMidBandDb },
    { label: "Treble", range: "2.8k-7.5k", db: profile.trebleBandDb },
  ]

  return (
    <div className="space-y-3 text-xs">
      {/* Pickup Summary */}
      <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-secondary/40 px-3 py-2">
        <div className="space-y-0.5">
          <div className="text-[11px] text-muted-foreground">추정 픽업 / 악기 성향</div>
          <div className="text-sm font-semibold text-foreground">
            {profile.estimatedPickupType}
          </div>
        </div>
        <div className="flex items-center gap-1.5 font-mono text-[11px]">
          <Badge variant="secondary">
            Trim {profile.suggestedInputTrimDb >= 0 ? `+${profile.suggestedInputTrimDb.toFixed(1)}` : profile.suggestedInputTrimDb.toFixed(1)} dB
          </Badge>
          <Badge variant="outline">Gate {profile.suggestedGateDb.toFixed(0)} dB</Badge>
        </div>
      </div>

      {/* 3 Trait Bars */}
      <div className="grid grid-cols-3 gap-2">
        <Card className="bg-card/70">
          <CardContent className="p-2.5 space-y-1.5">
            <div className="flex items-center justify-between text-[11px]">
              <span className="text-muted-foreground">밝기</span>
              <span className="font-mono font-semibold">{profile.brightnessScore}/100</span>
            </div>
            <Progress value={profile.brightnessScore} className="h-1.5" />
          </CardContent>
        </Card>
        <Card className="bg-card/70">
          <CardContent className="p-2.5 space-y-1.5">
            <div className="flex items-center justify-between text-[11px]">
              <span className="text-muted-foreground">따뜻함</span>
              <span className="font-mono font-semibold">{profile.warmthScore}/100</span>
            </div>
            <Progress value={profile.warmthScore} className="h-1.5" />
          </CardContent>
        </Card>
        <Card className="bg-card/70">
          <CardContent className="p-2.5 space-y-1.5">
            <div className="flex items-center justify-between text-[11px]">
              <span className="text-muted-foreground">출력 파워</span>
              <span className="font-mono font-semibold">{profile.outputPowerScore}/100</span>
            </div>
            <Progress value={profile.outputPowerScore} className="h-1.5" />
          </CardContent>
        </Card>
      </div>

      {/* 5-Band Tilt + Metrics */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-2.5">
        <Card className="bg-card/70">
          <CardHeader className="p-2.5 pb-1">
            <CardTitle className="text-[11px] text-muted-foreground font-medium">
              5밴드 주파수 밸런스 (상대 dB)
            </CardTitle>
          </CardHeader>
          <CardContent className="p-2.5 pt-1">
            <div className="grid grid-cols-5 gap-1.5 items-end h-20 pt-2">
              {bands.map((b) => {
                const pct = Math.min(100, Math.max(10, ((b.db + 12) / 24) * 100))
                return (
                  <div key={b.label} className="flex flex-col items-center justify-end h-full gap-1">
                    <span className="text-[10px] font-mono text-muted-foreground">
                      {b.db >= 0 ? `+${b.db.toFixed(1)}` : b.db.toFixed(1)}
                    </span>
                    <div className="w-full flex-1 flex items-end justify-center bg-muted/40 rounded-sm overflow-hidden">
                      <div
                        className="w-full bg-primary/80 rounded-t-sm transition-all"
                        style={{ height: `${pct}%` }}
                      />
                    </div>
                    <span className="text-[10px] font-medium text-foreground/90">{b.label}</span>
                  </div>
                )
              })}
            </div>
          </CardContent>
        </Card>

        <Card className="bg-card/70">
          <CardHeader className="p-2.5 pb-1">
            <CardTitle className="text-[11px] text-muted-foreground font-medium">
              전기적 / 음향 실측 지표
            </CardTitle>
          </CardHeader>
          <CardContent className="p-2.5 pt-1 grid grid-cols-2 gap-x-3 gap-y-1 text-[11px]">
            <div className="flex justify-between">
              <span className="text-muted-foreground">노이즈 플로어</span>
              <span className="font-mono">{profile.noiseFloorDb.toFixed(1)} dB</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">60Hz 험</span>
              <span className="font-mono">{profile.hum60HzDb.toFixed(1)} dB</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">스트럼 피크</span>
              <span className="font-mono">{profile.peakDb.toFixed(1)} dBFS</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">크레스트 팩터</span>
              <span className="font-mono">{profile.crestFactorDb.toFixed(1)} dB</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">서스테인 (-20dB)</span>
              <span className="font-mono">{profile.sustainSec.toFixed(2)} s</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">스펙트럼 중심</span>
              <span className="font-mono">{profile.spectralCentroidHz.toFixed(0)} Hz</span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Per-note measurements */}
      {profile.notes?.length > 0 && (
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-1.5">
          {profile.notes.map((n) => (
            <div
              key={n.stepId}
              className="rounded-md border border-border bg-secondary/25 px-2 py-1.5 space-y-0.5 text-[10px]"
            >
              <div className="flex items-center justify-between font-semibold">
                <span>{n.targetNote}</span>
                {n.skipped ? (
                  <span className="text-muted-foreground">건너뜀</span>
                ) : (
                  <span className="font-mono text-muted-foreground">
                    {n.detectedFreqHz.toFixed(1)}Hz
                  </span>
                )}
              </div>
              {!n.skipped && (
                <div className="flex items-center justify-between font-mono text-muted-foreground">
                  <span>H2 {n.h2RatioDb.toFixed(1)}dB</span>
                  <span>{n.sustainSec.toFixed(1)}s</span>
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {/* AI / Tone Recommendation */}
      {profile.toneRecommendation && (
        <div className="rounded-md border border-border bg-muted/35 px-3 py-2 text-[11px] leading-relaxed text-foreground/90 flex gap-2 items-start">
          <Sparkles className="w-3.5 h-3.5 mt-0.5 shrink-0 text-primary" />
          <span>{profile.toneRecommendation}</span>
        </div>
      )}
    </div>
  )
}

export function GuitarAnalyzerDialog({
  open,
  onOpenChange,
  isAudioRunning,
  isAudioBusy,
  hasGp100,
  onStartAudio,
  onQuickStartGp100,
  onOpenAudioSettings,
  profiles,
  activeProfileId,
  liveAnalysis,
  onLiveAnalysisChange,
  onProfilesUpdated,
  onLog,
}: GuitarAnalyzerDialogProps) {
  const [tab, setTab] = useState<"analyze" | "profiles">("analyze")
  const [guitarName, setGuitarName] = useState("내 기타")
  const [saveName, setSaveName] = useState("내 기타")
  const [selectedProfileId, setSelectedProfileId] = useState(activeProfileId || profiles[0]?.id || "")
  const [busyAction, setBusyAction] = useState(false)

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

  const handleDialogOpenChange = async (nextOpen: boolean) => {
    if (!nextOpen && liveAnalysis?.active && !liveAnalysis.completed && studioBridge.isWebView2) {
      try {
        const st = await studioBridge.invoke<GuitarAnalysisLiveStateDto>("cancelGuitarAnalysis")
        onLiveAnalysisChange(st)
      } catch {
        // ignore
      }
    }
    onOpenChange(nextOpen)
  }

  const handleStart = async () => {
    if (!studioBridge.isWebView2) return
    setBusyAction(true)
    try {
      const st = await studioBridge.invoke<GuitarAnalysisLiveStateDto>("startGuitarAnalysis", {
        name: guitarName.trim() || "내 기타",
      })
      onLiveAnalysisChange(st)
    } catch (e) {
      onLog(`기타 분석 시작 실패: ${String(e)}`, "error")
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
      onLog(`단계 건너뛰기 오류: ${String(e)}`, "error")
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
        onLog(`'${res.profile.name}' 저장 및 Input Trim/Gate 보정 적용 완료`, "success")
      } else {
        onLog(`'${res.profile.name}' 기타 프로필 저장 완료`, "success")
      }
      setTab("profiles")
    } catch (e) {
      onLog(`기타 프로필 저장 실패: ${String(e)}`, "error")
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
      onLog(`기타 선택 오류: ${String(e)}`, "error")
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
      onLog(`기타 삭제 오류: ${String(e)}`, "error")
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
        `시그널 체인 보정 완료: Input ${res.inputGainDb >= 0 ? "+" : ""}${res.inputGainDb.toFixed(1)} dB, Gate ${res.gateDb.toFixed(0)} dB`,
        "success"
      )
    } catch (e) {
      onLog(`체인 보정 실패: ${String(e)}`, "error")
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
    <Dialog open={open} onOpenChange={handleDialogOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-sm">
            <Guitar className="w-4 h-4 text-primary" />
            내 기타 DI 분석 &amp; 프로파일러
          </DialogTitle>
        </DialogHeader>

        <Tabs value={tab} onValueChange={(v) => setTab(v as "analyze" | "profiles")}>
          <TabsList className="grid w-full grid-cols-2 h-8">
            <TabsTrigger value="analyze" className="text-xs">
              <AudioWaveform className="w-3.5 h-3.5" />
              가이드 분석 마법사
            </TabsTrigger>
            <TabsTrigger value="profiles" className="text-xs">
              <Sliders className="w-3.5 h-3.5" />
              저장된 내 기타 ({profiles.length})
            </TabsTrigger>
          </TabsList>

          {/* ── Tab 1: Guided Guitar Analysis Wizard ── */}
          <TabsContent value="analyze" className="space-y-3 mt-1">
            {!isAudioRunning && !isCompletedWizard && (
              <div className="rounded-md border border-border bg-secondary/50 p-3 flex flex-wrap items-center justify-between gap-2 text-xs">
                <div className="space-y-0.5">
                  <div className="font-semibold text-foreground">
                    먼저 오디오 엔진(ASIO/GP-100)을 실행해 주세요
                  </div>
                  <div className="text-[11px] text-muted-foreground">
                    기타 드라이 DI 입력(톤 프로세싱 전 원음)을 실시간 분석하려면 오디오 입력이 켜져 있어야 합니다.
                  </div>
                </div>
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

            {!isRunningWizard && !isCompletedWizard && (
              <Card>
                <CardContent className="p-4 space-y-4">
                  <div className="space-y-1.5">
                    <Label className="text-xs text-muted-foreground">기타 이름 / 픽업 위치 메모</Label>
                    <Input
                      value={guitarName}
                      onChange={(e) => setGuitarName(e.target.value)}
                      placeholder="예: Fender Strat (Neck) / Gibson Les Paul (Bridge)"
                      className="h-8 text-xs"
                    />
                  </div>

                  <div className="space-y-1.5">
                    <div className="text-[11px] text-muted-foreground">진행 순서 (약 10~15초 소요)</div>
                    <div className="grid grid-cols-3 sm:grid-cols-6 gap-1.5">
                      {STEP_LABELS.map((s) => (
                        <div
                          key={s.id}
                          className="rounded border border-border bg-secondary/35 px-2 py-1.5 text-center text-[11px] font-medium text-foreground/85"
                        >
                          {s.short}
                        </div>
                      ))}
                    </div>
                  </div>

                  <div className="rounded-md border border-border bg-muted/35 p-3 text-[11px] text-muted-foreground space-y-1 leading-relaxed">
                    <div>• 기타 볼륨·톤 노브를 평소 기준(최대 권장)으로 맞추고 튜닝을 확인해 주세요.</div>
                    <div>• 앰프/이펙트 통과 전 순수 픽업 출력, 노이즈·60Hz 험, 밝기, 서스테인을 측정해 저장합니다.</div>
                    <div>• 저장된 기타 정보는 Antigravity MCP가 톤을 제작할 때도 자동으로 참조합니다.</div>
                  </div>

                  <div className="flex justify-end">
                    <Button
                      size="sm"
                      disabled={!isAudioRunning || busyAction}
                      onClick={handleStart}
                      className="gap-1.5 text-xs"
                    >
                      <AudioWaveform className="w-3.5 h-3.5" />
                      기타 분석 시작
                    </Button>
                  </div>
                </CardContent>
              </Card>
            )}

            {isRunningWizard && liveAnalysis && (
              <div className="space-y-3">
                {/* Stepper */}
                <div className="grid grid-cols-6 gap-1.5">
                  {STEP_LABELS.map((s, idx) => {
                    const done = idx < liveAnalysis.stepIndex
                    const current = idx === liveAnalysis.stepIndex
                    return (
                      <div
                        key={s.id}
                        className={`rounded border px-1.5 py-1.5 text-center text-[11px] font-medium transition-colors ${
                          done
                            ? "border-emerald-500/40 bg-emerald-500/15 text-emerald-300"
                            : current
                              ? "border-primary bg-primary/15 text-foreground font-semibold"
                              : "border-border bg-secondary/25 text-muted-foreground"
                        }`}
                      >
                        {s.short}
                      </div>
                    )
                  })}
                </div>

                {/* Main Live Capture Card */}
                <Card>
                  <CardContent className="p-4 space-y-4">
                    <div className="flex items-start justify-between gap-3">
                      <div className="space-y-1">
                        <div className="flex items-center gap-2">
                          <Badge variant="outline" className="text-[10px] font-mono">
                            STEP {liveAnalysis.stepIndex + 1} / {liveAnalysis.totalSteps}
                          </Badge>
                          <span className="text-sm font-semibold text-foreground">
                            {liveAnalysis.stepTitle}
                          </span>
                        </div>
                        <p className="text-xs text-muted-foreground">{liveAnalysis.instruction}</p>
                      </div>

                      <div
                        className={`shrink-0 rounded-md border px-3 py-1.5 text-center min-w-[84px] transition-colors ${
                          liveAnalysis.isMatched
                            ? "border-emerald-500/60 bg-emerald-500/15"
                            : "border-border bg-secondary/50"
                        }`}
                      >
                        <div className="text-base font-bold font-mono text-foreground">
                          {liveAnalysis.targetNote}
                        </div>
                        <div className="text-[10px] font-mono text-muted-foreground">
                          {liveAnalysis.targetFreqHz > 0
                            ? `${liveAnalysis.targetFreqHz.toFixed(1)} Hz`
                            : liveAnalysis.stepId === "silence"
                              ? "MUTE"
                              : "OPEN CHORD"}
                        </div>
                      </div>
                    </div>

                    {/* Live Input Pitch + Level */}
                    <div className="grid grid-cols-3 gap-2 rounded-md border border-border bg-muted/35 p-2.5 text-xs">
                      <div>
                        <div className="text-[10px] text-muted-foreground">감지된 음정</div>
                        <div className="font-mono font-bold text-sm mt-0.5 flex items-center gap-1.5">
                          <span>{liveAnalysis.liveNote}</span>
                          {liveAnalysis.liveFreqHz > 0 && (
                            <span className="text-[10px] font-normal text-muted-foreground">
                              {liveAnalysis.liveFreqHz.toFixed(1)}Hz (
                              {liveAnalysis.liveCents > 0 ? "+" : ""}
                              {liveAnalysis.liveCents.toFixed(0)}ct)
                            </span>
                          )}
                        </div>
                      </div>

                      <div>
                        <div className="text-[10px] text-muted-foreground">드라이 입력 레벨</div>
                        <div className="mt-1 space-y-1">
                          <Progress value={levelPct} className="h-1.5" />
                          <div className="font-mono text-[10px] text-muted-foreground">
                            {liveAnalysis.liveLevelDb.toFixed(1)} dBFS · 피크{" "}
                            {(liveAnalysis.livePeakDb ?? liveAnalysis.liveLevelDb).toFixed(1)}
                          </div>
                        </div>
                      </div>

                      <div className="flex flex-col items-end justify-center">
                        <Badge
                          variant={liveAnalysis.isMatched ? "default" : "secondary"}
                          className="text-[11px]"
                        >
                          {liveAnalysis.isMatched ? "인식 일치 · 캡처 중" : "신호 대기 중"}
                        </Badge>
                      </div>
                    </div>

                    {isAudioRunning && (liveAnalysis.livePeakDb ?? -96) <= -85 && (
                      <div className="rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 flex items-center justify-between gap-2 text-[11px]">
                        <span className="text-amber-200">
                          드라이 입력 신호가 들어오지 않고 있습니다 ({(liveAnalysis.livePeakDb ?? -96).toFixed(1)} dBFS). 오디오 I/O의 입력 채널(In 1 / In 2)이나 GP-100 케이블·볼륨을 확인하세요.
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
                          <Sliders className="w-3 h-3" />
                          오디오 설정
                        </Button>
                      </div>
                    )}

                    {/* Hold Progress */}
                    <div className="space-y-1.5">
                      <div className="flex items-center justify-between text-[11px]">
                        <span className="text-foreground/90">{liveAnalysis.statusHint}</span>
                        <span className="font-mono font-semibold">
                          {liveAnalysis.progressPct.toFixed(0)}%
                        </span>
                      </div>
                      <Progress value={liveAnalysis.progressPct} className="h-2.5" />
                    </div>

                    <div className="flex items-center justify-between pt-1">
                      <Button
                        size="sm"
                        variant="ghost"
                        className="h-7 text-xs"
                        onClick={handleCancel}
                      >
                        분석 취소
                      </Button>
                      <Button
                        size="sm"
                        variant="secondary"
                        className="h-7 text-xs gap-1.5"
                        onClick={handleSkipStep}
                      >
                        <SkipForward className="w-3.5 h-3.5" />
                        이 단계 건너뛰기 / 수동 완료
                      </Button>
                    </div>
                  </CardContent>
                </Card>
              </div>
            )}

            {isCompletedWizard && liveAnalysis?.draftProfile && (
              <ScrollArea className="max-h-[64vh] pr-2">
                <div className="space-y-3">
                  <div className="flex items-center gap-2">
                    <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
                    <span className="text-xs font-semibold text-foreground">
                      분석 완료! 프로필 이름을 확인하고 저장하세요.
                    </span>
                  </div>

                  <div className="flex items-center gap-2">
                    <Label className="text-xs text-muted-foreground shrink-0">저장 이름</Label>
                    <Input
                      value={saveName}
                      onChange={(e) => setSaveName(e.target.value)}
                      className="h-8 text-xs"
                    />
                  </div>

                  <GuitarProfileDetail profile={liveAnalysis.draftProfile} />

                  <div className="flex flex-wrap items-center justify-between gap-2 pt-2">
                    <Button
                      size="sm"
                      variant="ghost"
                      className="h-8 text-xs gap-1.5"
                      onClick={handleStart}
                      disabled={!isAudioRunning || busyAction}
                    >
                      <RotateCcw className="w-3.5 h-3.5" />
                      다시 측정
                    </Button>
                    <div className="flex items-center gap-2">
                      <Button
                        size="sm"
                        variant="secondary"
                        className="h-8 text-xs"
                        disabled={busyAction}
                        onClick={() => handleSaveProfile(false)}
                      >
                        프로필만 저장
                      </Button>
                      <Button
                        size="sm"
                        variant="default"
                        className="h-8 text-xs gap-1.5"
                        disabled={busyAction}
                        onClick={() => handleSaveProfile(true)}
                      >
                        <Sliders className="w-3.5 h-3.5" />
                        저장 + 체인 보정 적용
                      </Button>
                    </div>
                  </div>
                </div>
              </ScrollArea>
            )}
          </TabsContent>

          {/* ── Tab 2: Saved Guitar Profiles ── */}
          <TabsContent value="profiles" className="space-y-3 mt-1">
            {profiles.length === 0 ? (
              <Card>
                <CardContent className="p-6 text-center space-y-2">
                  <div className="text-xs text-muted-foreground">
                    아직 저장된 기타 프로필이 없습니다.
                  </div>
                  <Button size="sm" variant="secondary" className="text-xs" onClick={() => setTab("analyze")}>
                    첫 번째 기타 분석하기
                  </Button>
                </CardContent>
              </Card>
            ) : (
              <ScrollArea className="max-h-[64vh] pr-2">
                <div className="space-y-3">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                    {profiles.map((g) => {
                      const isActive = g.id === activeProfileId
                      const isSelected = g.id === selectedProfile?.id
                      return (
                        <div
                          key={g.id}
                          onClick={() => setSelectedProfileId(g.id)}
                          className={`cursor-pointer rounded-md border p-2.5 flex items-start justify-between gap-2 transition-colors ${
                            isSelected
                              ? "border-primary bg-primary/10"
                              : "border-border bg-card/60 hover:bg-secondary/40"
                          }`}
                        >
                          <div className="space-y-0.5 min-w-0">
                            <div className="flex items-center gap-1.5">
                              <span className="text-xs font-semibold truncate">{g.name}</span>
                              {isActive && (
                                <Badge variant="default" className="text-[10px] h-4 px-1.5">
                                  활성
                                </Badge>
                              )}
                            </div>
                            <div className="text-[11px] text-muted-foreground truncate">
                              {g.estimatedPickupType}
                            </div>
                            <div className="text-[10px] font-mono text-muted-foreground">
                              {g.createdAt} · 밝기 {g.brightnessScore} · 출력 {g.outputPowerScore}
                            </div>
                          </div>
                          <Button
                            size="icon"
                            variant="ghost"
                            className="h-6 w-6 shrink-0 text-muted-foreground hover:text-destructive"
                            onClick={(e) => {
                              e.stopPropagation()
                              handleDeleteProfile(g.id)
                            }}
                            title="기타 프로필 삭제"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </Button>
                        </div>
                      )
                    })}
                  </div>

                  {selectedProfile && (
                    <div className="space-y-3 pt-1">
                      <GuitarProfileDetail profile={selectedProfile} />

                      <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-muted/30 px-3 py-2">
                        <span className="text-[11px] text-muted-foreground">
                          활성 기타는 Antigravity MCP가 톤을 설계할 때 자동으로 참조됩니다.
                        </span>
                        <div className="flex items-center gap-2">
                          {selectedProfile.id !== activeProfileId && (
                            <Button
                              size="sm"
                              variant="secondary"
                              className="h-7 text-xs"
                              onClick={() => handleSelectProfile(selectedProfile.id)}
                            >
                              활성 기타로 선택
                            </Button>
                          )}
                          <Button
                            size="sm"
                            variant="default"
                            className="h-7 text-xs gap-1.5"
                            disabled={busyAction}
                            onClick={() => handleApplyCalibration(selectedProfile.id)}
                          >
                            <Sliders className="w-3.5 h-3.5" />
                            현재 체인에 Trim &amp; Gate 적용
                          </Button>
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              </ScrollArea>
            )}
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  )
}
