import React, { useEffect, useState } from "react"
import {
  Activity,
  ArrowLeft,
  ArrowRight,
  BookOpen,
  CheckCircle2,
  Cpu,
  Gauge,
  SlidersHorizontal,
  Sparkles,
  Volume2,
  Wrench,
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
import { useI18n } from "@/lib/i18n"

interface OnboardingTutorialModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onComplete: () => void
  onOpenAudioSheet: () => void
  onOpenGearGuide: () => void
  onOpenBenchmark: () => void
}

interface TourStep {
  badge: string
  titleKo: string
  titleEn: string
  descKo: string
  descEn: string
  tipsKo: string[]
  tipsEn: string[]
  icon: React.ReactNode
  cta?: { labelKo: string; labelEn: string; action: () => void }
}

export const OnboardingTutorialModal: React.FC<OnboardingTutorialModalProps> = ({
  open,
  onOpenChange,
  onComplete,
  onOpenAudioSheet,
  onOpenGearGuide,
  onOpenBenchmark,
}) => {
  const { tx, locale } = useI18n()
  const [stepIdx, setStepIdx] = useState(0)

  useEffect(() => {
    if (open) setStepIdx(0)
  }, [open])

  const steps: TourStep[] = [
    {
      badge: "STEP 1 / 6",
      titleKo: "범용 ASIO · 오디오 인터페이스 & 라인 인풋 연결",
      titleEn: "Connect Universal ASIO, Audio Interface, or Line-In",
      descKo:
        "NeuralRig Studio는 특정 장비에 종속되지 않습니다. Focusrite, Steinberg, Audient, Behringer, Roland, Line 6, Valeton 등 모든 ASIO 인터페이스와 멀티채널 WASAPI 라인 입력을 자동 인식합니다.",
      descEn:
        "NeuralRig Studio works universally across Focusrite, Steinberg, Audient, Behringer, Roland, Line 6, and multi-channel WASAPI inputs with zero vendor lock-in.",
      tipsKo: [
        "우측 상단 Audio I/O 또는 '자동 연결' 버튼을 누르면 외장 인터페이스를 우선 선택합니다.",
        "ASIO 드라이버가 없는 노트북/PC에서는 WASAPI Exclusive / Line-In 모드로 즉시 연주할 수 있습니다.",
        "스테레오/모노 채널 오프셋(Ch 1, Ch 2, Ch 1/2, Ch 3/4...)을 자유롭게 지정하세요.",
      ],
      tipsEn: [
        "Click Audio I/O or Auto-Connect in the top bar to automatically prioritize your hardware interface.",
        "No dedicated ASIO driver? Switch to WASAPI / Line-In for instant low-latency playback.",
        "Choose Mono Ch 1..N or Stereo input pairs directly from the Input Patchbay.",
      ],
      icon: <Volume2 className="size-4 text-primary" />,
      cta: {
        labelKo: "Audio I/O 설정 열기",
        labelEn: "Open Audio I/O",
        action: () => {
          onOpenChange(false)
          onOpenAudioSheet()
        },
      },
    },
    {
      badge: "STEP 2 / 6",
      titleKo: "첫인상 쇼케이스 프리셋 8종 & 8 씬(A–H) 무끊김 전환",
      titleEn: "8 Showcase Factory Presets & Glitch-Free Scenes (A–H)",
      descKo:
        "첫 실행과 동시에 바로 감탄할 수 있도록 클린 네오소울부터 JCM800, 애니송 팝락, 모던 젠트, 시머 앰비언트, 빈티지 퍼즈까지 8개의 팩토리 프리셋(1A–2D)이 준비되어 있습니다.",
      descEn:
        "Start playing immediately with 8 curated tones (1A–2D) covering Neo-Soul Clean, Edge-of-Breakup, Arena Crunch, Pop-Rock, Modern Djent, Cathedral Shimmer, Brown Lead, and Vintage Fuzz.",
      tipsKo: [
        "상단 프리셋 셀렉터(1A~2D) 또는 Alt+1~8 단축키로 즉시 톤을 변경하세요.",
        "각 프리셋마다 8개 Scene(A~H) 스냅샷이 저장되어 곡의 Intro/Verse/Chorus/Solo를 지연 없이 전환합니다.",
        "Ctrl+S 또는 Save 버튼으로 나만의 프리셋을 로컬과 클라우드에 보관할 수 있습니다.",
      ],
      tipsEn: [
        "Switch between presets 1A–2D at the top bar or press '/' (Ctrl+K) for Command Palette.",
        "Each preset holds 8 glitch-free Scenes (A–H) for Verse, Crunch, Chorus, and Lead solo lifts.",
        "Save custom presets locally and sync them across devices with Google login.",
      ],
      icon: <Sparkles className="size-4 text-signal-warn" />,
    },
    {
      badge: "STEP 3 / 6",
      titleKo: "2×8 모듈러 체인 · 직렬/병렬 라우팅 & 모델명/가칭 검색",
      titleEn: "2×8 Modular Chain, Parallel Split & Dual-Name Search",
      descKo:
        "75종 이상의 페달·앰프·캐비넷(.nam & 16K Partitioned IR)·공간계를 2행 × 8슬롯 보드에 배치하세요. UI에는 저작권 안전 가칭이 표시되지만, 검색창에서는 원래 하드웨어 모델명(TS808, SD-1, JCM800 등)으로도 즉시 검색됩니다.",
      descEn:
        "Arrange 75+ pedals, tube amps, cabinets, and stereo FX across a 2×8 grid. Safe aliases appear on the stompboxes, while search matches both UI names and original hardware model names.",
      tipsKo: [
        "슬롯을 클릭해 하단 아날로그 노브(Drive/Tone/Level/EQ)와 딥 메뉴 파라미터를 조절합니다.",
        "Parallel Split 버튼을 켜면 Row 1 / Row 2 듀얼 앰프·듀얼 캐비넷 병렬 믹스가 활성화됩니다.",
        "모든 노브와 바이패스·씬은 MIDI Learn(우클릭 또는 MIDI 시트)으로 풋컨트롤러에 바인딩할 수 있습니다.",
      ],
      tipsEn: [
        "Click any slot to inspect and tweak tactile knobs and cab dual-miking below the board.",
        "Enable Parallel Split to blend Dual Amps and Dual Cabs cleanly across Row 1 and Row 2.",
        "Bind any parameter, bypass toggle, scene, or Tap Tempo via MIDI Learn.",
      ],
      icon: <SlidersHorizontal className="size-4 text-signal-ok" />,
    },
    {
      badge: "STEP 4 / 6",
      titleKo: "AI 톤 아키텍트 · 기타 DI 프로파일러 · 루퍼 & 실시간 리앰핑",
      titleEn: "AI Tone Architect, Guitar DI Profiler, Looper & Re-Amping",
      descKo:
        "하단 AI 바에 '요루시카풍 영롱한 JC-120 + 블루스카이' 또는 '메이든 스타일 트윈리드'를 입력하면 즉시 16슬롯 체인이 완성됩니다. 기타 프로파일러가 픽업 성향을 측정해 게이트·입력 트림까지 자동 보정합니다.",
      descEn:
        "Type natural language in the AI Tone bar to auto-build complete 16-slot chains. Use the Guitar DI Profiler to measure pickup brightness/noise floor and calibrate input trim automatically.",
      tipsKo: [
        "상단 GUITAR 프로필 아이콘으로 1분 캘리브레이션을 실행해 픽업(싱글/험버커)을 분석해 보세요.",
        "듀얼 스트림 레코더는 Dry DI와 Stereo Wet WAV를 동시에 저장해 1클릭 실시간 리앰핑을 지원합니다.",
        "BPM Sync 스테레오 루퍼(Undo/Redo, 0.5x Half-Speed, Reverse)도 내장되어 있습니다.",
      ],
      tipsEn: [
        "Open GUITAR Profiler to fingerprint single-coils vs high-output humbuckers.",
        "Dual-Stream Recorder captures raw Dry DI + Stereo Wet simultaneously for 1-click live re-amping.",
        "Use the BPM-Synced Stereo Looper with Undo/Redo, Half-Speed, and Reverse.",
      ],
      icon: <Activity className="size-4 text-primary" />,
    },
    {
      badge: "STEP 5 / 6",
      titleKo: "새 페달 · 앰프 · 캐비넷 직접 생성 및 커뮤니티 기어 허브",
      titleEn: "Create New Pedals, Amps & Cabinets via MNA SPICE",
      descKo:
        "원하는 빈티지 페달이나 부티크 앰프가 없다면, Claude · Cursor · Codex · Gemini 등 AI 에이전트(MCP)에게 모델명을 말해 보세요. 웹 회로도·데이터시트를 리서치하고 0 B 실시간 MNA SPICE 회로로 추가해 줍니다.",
      descEn:
        "Need a pedal, tube amp, or cabinet not in the factory catalog? Connect any AI agent via `npx neuralrig-mcp` to research real schematics, verify `.OP/.AC/.TRAN` in MNA SPICE, and hot-load it live.",
      tipsKo: [
        "'새 페달/앰프 생성 가이드'에서 Claude/Cursor/Codex/Gemini용 npx MCP 원클릭 설치 명령과 프롬프트 템플릿을 제공합니다.",
        "제작한 커스텀 SPICE 페달·앰프·캐비넷은 HUB에서 다른 사용자에게 공유하거나 다운로드할 수 있습니다.",
        "외부 .nam 파일, .wav 캐비넷 IR, .nrig 번들은 창 위로 드래그 앤 드롭하면 즉시 로드됩니다.",
      ],
      tipsEn: [
        "Open the Custom Gear Guide for one-command `npx neuralrig-mcp install` setup across 7 AI tools.",
        "Upload your verified SPICE creations to the Community Gear Hub or install others' rigs in 1 click.",
        "Drag-and-drop `.nam`, `.wav` IR, or `.nrig` bundle files directly onto the studio window.",
      ],
      icon: <Wrench className="size-4 text-signal-warn" />,
      cta: {
        labelKo: "페달/앰프 제작 & npx MCP 가이드 열기",
        labelEn: "Open Custom Gear & MCP Guide",
        action: () => {
          onOpenChange(false)
          onOpenGearGuide()
        },
      },
    },
    {
      badge: "STEP 6 / 6",
      titleKo: "하드웨어 유사도 · 레이턴시 · 16슬롯 멀티체인 벤치마크",
      titleEn: "Hardware Fidelity, Latency & 16-Slot Stress Benchmark",
      descKo:
        "내 PC에서 TS808/SD-1/AC128/12AX7 회로 유사도가 얼마나 나오는지, 64-frame 초저지연 RT 데드라인과 16슬롯 직렬/병렬 풀체인이 0 B 할당으로 안정 통과하는지 언제든 원클릭으로 검증하세요.",
      descEn:
        "Verify real-time DSP reliability on your own hardware at any time: check MNA SPICE circuit accuracy, 64/128/256-frame CPU budgets, round-trip latency, and zero-allocation 16-slot chain stability.",
      tipsKo: [
        "상단 BENCH 버튼을 누르면 실제 DSP + MNA SPICE + WaveNet 벤치마크가 실행됩니다.",
        "문제 발생 시 상단 BUG 버튼으로 익명화(LogSanitizer)된 로그와 함께 버그를 즉시 제보할 수 있습니다.",
      ],
      tipsEn: [
        "Click BENCH in the top bar anytime to run the quantitative benchmark suite.",
        "Need help or found an issue? Click BUG to submit a bug report with automatically scrubbed logs.",
      ],
      icon: <Gauge className="size-4 text-signal-ok" />,
      cta: {
        labelKo: "지금 벤치마크 실행하기",
        labelEn: "Open Benchmark Suite",
        action: () => {
          onOpenChange(false)
          onOpenBenchmark()
        },
      },
    },
  ]

  const cur = steps[stepIdx]
  const isLast = stepIdx === steps.length - 1
  const pct = Math.round(((stepIdx + 1) / steps.length) * 100)
  const tips = locale === "ko" ? cur.tipsKo : cur.tipsEn

  const handleFinish = () => {
    onComplete()
    onOpenChange(false)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl p-0 overflow-hidden bg-card text-card-foreground border-border gap-0">
        <DialogHeader className="px-5 py-3.5 border-b border-border bg-secondary/45 shrink-0 space-y-3">
          <div className="flex items-center justify-between gap-3 pr-8">
            <div className="flex items-center gap-3 min-w-0">
              <div className="size-9 rounded-sm bg-oled border border-oled-border flex items-center justify-center shrink-0">
                {cur.icon}
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <Badge variant="oled" size="xs">
                    {cur.badge}
                  </Badge>
                  <span className="text-[11px] font-mono text-muted-foreground flex items-center gap-1">
                    <BookOpen className="size-3" />
                    {tx("NEURALRIG STUDIO v3.0 시작 튜토리얼", "NEURALRIG STUDIO v3.0 QUICK TOUR")}
                  </span>
                </div>
                <DialogTitle className="mt-1 leading-snug">
                  {tx(cur.titleKo, cur.titleEn)}
                </DialogTitle>
              </div>
            </div>
          </div>
          <DialogDescription className="sr-only">
            {tx(
              "NeuralRig Studio UI 및 핵심 기능 인터랙티브 가이드",
              "Interactive walkthrough of NeuralRig Studio UI and features"
            )}
          </DialogDescription>

          <div className="h-1 w-full rounded-[1px] bg-border overflow-hidden">
            <div
              className="h-full bg-primary transition-all duration-300"
              style={{ width: `${pct}%` }}
            />
          </div>
        </DialogHeader>

        <div className="px-5 py-4 space-y-3.5 bg-background">
          <p className="text-xs leading-relaxed text-foreground">
            {tx(cur.descKo, cur.descEn)}
          </p>

          <div className="rounded-sm border border-border bg-card p-3.5 space-y-2">
            <div className="flex items-center gap-1.5 font-mono text-[10px] font-bold uppercase tracking-wider text-primary">
              <Cpu className="size-3" />
              <span>{tx("핵심 팁", "KEY TAKEAWAYS")}</span>
            </div>
            <ul className="space-y-1.5">
              {tips.map((tip, i) => (
                <li key={i} className="flex items-start gap-2 text-xs text-foreground leading-relaxed">
                  <CheckCircle2 className="size-3.5 text-signal-ok shrink-0 mt-0.5" />
                  <span>{tip}</span>
                </li>
              ))}
            </ul>
          </div>

          {cur.cta && (
            <div className="flex justify-start">
              <Button size="sm" variant="keycap" onClick={cur.cta.action}>
                {tx(cur.cta.labelKo, cur.cta.labelEn)}
              </Button>
            </div>
          )}
        </div>

        <div className="px-5 py-3 border-t border-border bg-secondary/45 flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="flex items-center gap-1.5">
              {steps.map((_, idx) => (
                <button
                  key={idx}
                  type="button"
                  aria-label={`Step ${idx + 1}`}
                  onClick={() => setStepIdx(idx)}
                  className={`h-1.5 rounded-[1px] transition-all cursor-pointer ${
                    idx === stepIdx
                      ? "w-6 bg-primary"
                      : "w-2 bg-border hover:bg-muted-foreground"
                  }`}
                />
              ))}
            </div>
            <Button
              size="xs"
              variant="ghost"
              onClick={handleFinish}
              className="text-[11px]"
            >
              {tx("건너뛰기", "SKIP")}
            </Button>
          </div>

          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant="keycap"
              disabled={stepIdx === 0}
              onClick={() => setStepIdx((i) => Math.max(0, i - 1))}
            >
              <ArrowLeft className="size-3.5" />
              <span>{tx("이전", "BACK")}</span>
            </Button>
            {isLast ? (
              <Button size="sm" variant="signal" onClick={handleFinish} className="px-4">
                <CheckCircle2 className="size-3.5" />
                <span>{tx("튜토리얼 완료 · 시작하기", "FINISH & PLAY")}</span>
              </Button>
            ) : (
              <Button
                size="sm"
                variant="signal"
                onClick={() => setStepIdx((i) => Math.min(steps.length - 1, i + 1))}
                className="px-4"
              >
                <span>{tx("다음 단계", "NEXT")}</span>
                <ArrowRight className="size-3.5" />
              </Button>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
