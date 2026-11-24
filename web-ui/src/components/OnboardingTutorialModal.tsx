import React, { useEffect, useId, useLayoutEffect, useRef, useState } from "react"
import { createPortal } from "react-dom"
import {
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  Gauge,
  Menu,
  SlidersHorizontal,
  Sparkles,
  Volume2,
  Wrench,
  X,
} from "lucide-react"
import { Button } from "@/components/ui/button"
import { Hint } from "@/components/ui/tooltip"
import { useI18n } from "@/lib/i18n"

interface OnboardingTutorialModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  activeView?: "grid" | "directory" | "scope"
  onEnsureStudioView?: () => void
  onComplete: () => void
  onOpenAudioSheet: () => void
  onOpenGearGuide: () => void
  onOpenBenchmark: () => void
}

type PreferredSide = "top" | "bottom" | "left" | "right"

interface TourAction {
  key: "audio" | "mcp" | "bench"
  labelKo: string
  labelEn: string
  action: () => void
}

interface TourStep {
  badge: string
  target: string
  preferredSide: PreferredSide
  pad: number
  rx: number
  titleKo: string
  titleEn: string
  descKo: string
  descEn: string
  tipsKo: string[]
  tipsEn: string[]
  icon: React.ReactNode
  actions?: TourAction[]
}

interface SpotRect {
  x: number
  y: number
  w: number
  h: number
  rx: number
}

interface CardPos {
  left: number
  top: number
}

function rectsOverlapArea(
  ax: number,
  ay: number,
  aw: number,
  ah: number,
  bx: number,
  by: number,
  bw: number,
  bh: number
): number {
  const ox = Math.max(0, Math.min(ax + aw, bx + bw) - Math.max(ax, bx))
  const oy = Math.max(0, Math.min(ay + ah, by + bh) - Math.max(ay, by))
  return ox * oy
}

function computeCoachmarkPos(
  spot: SpotRect | null,
  cardW: number,
  cardH: number,
  preferred: PreferredSide,
  vw: number,
  vh: number
): CardPos {
  const margin = 16
  const gap = 14
  const maxL = Math.max(margin, vw - cardW - margin)
  const maxT = Math.max(margin, vh - cardH - margin)

  if (!spot) {
    return {
      left: Math.round((vw - cardW) / 2),
      top: Math.round((vh - cardH) / 2),
    }
  }

  const clampL = (v: number) => Math.max(margin, Math.min(maxL, v))
  const clampT = (v: number) => Math.max(margin, Math.min(maxT, v))

  const spotCenterX = spot.x + spot.w / 2
  const alignRight = spotCenterX > vw * 0.52
  const horizLeft = clampL(alignRight ? spot.x + spot.w - cardW : spot.x)
  const vertTop = clampT(spot.y)

  const candidates: Array<{ left: number; top: number; fits: boolean }> = []
  const order: PreferredSide[] = [
    preferred,
    preferred === "bottom" ? "top" : preferred === "top" ? "bottom" : preferred === "left" ? "right" : "left",
    "bottom",
    "top",
    "right",
    "left",
  ]

  for (const side of order) {
    let rawL = horizLeft
    let rawT = vertTop
    if (side === "bottom") {
      rawT = spot.y + spot.h + gap
      rawL = horizLeft
    } else if (side === "top") {
      rawT = spot.y - cardH - gap
      rawL = horizLeft
    } else if (side === "right") {
      rawL = spot.x + spot.w + gap
      rawT = vertTop
    } else {
      rawL = spot.x - cardW - gap
      rawT = vertTop
    }

    const fitsViewport =
      rawL >= margin &&
      rawL + cardW <= vw - margin &&
      rawT >= margin &&
      rawT + cardH <= vh - margin

    candidates.push({ left: Math.round(clampL(rawL)), top: Math.round(clampT(rawT)), fits: fitsViewport })
  }

  for (const c of candidates) {
    if (!c.fits) continue
    if (rectsOverlapArea(c.left, c.top, cardW, cardH, spot.x, spot.y, spot.w, spot.h) === 0) {
      return { left: c.left, top: c.top }
    }
  }

  let best = candidates[0]
  let bestOverlap = Infinity
  for (const c of candidates) {
    const ov = rectsOverlapArea(c.left, c.top, cardW, cardH, spot.x, spot.y, spot.w, spot.h)
    if (ov < bestOverlap) {
      bestOverlap = ov
      best = c
    }
  }
  return { left: best.left, top: best.top }
}

export const OnboardingTutorialModal: React.FC<OnboardingTutorialModalProps> = ({
  open,
  onOpenChange,
  activeView,
  onEnsureStudioView,
  onComplete,
  onOpenAudioSheet,
  onOpenGearGuide,
  onOpenBenchmark,
}) => {
  const { tx, locale } = useI18n()
  const maskId = useId()
  const headingId = useId()
  const cardRef = useRef<HTMLDivElement | null>(null)
  const [stepIdx, setStepIdx] = useState(0)
  const [spot, setSpot] = useState<SpotRect | null>(null)
  const [cardPos, setCardPos] = useState<CardPos>({ left: 32, top: 80 })
  const [cardDims, setCardDims] = useState<{ w: number; h: number }>({ w: 430, h: 246 })

  useEffect(() => {
    if (!open) return
    setStepIdx(0)
    ;(document.activeElement as HTMLElement | null)?.blur?.()
    if (activeView && activeView !== "grid") {
      onEnsureStudioView?.()
    }
  }, [open])

  const steps: TourStep[] = [
    {
      badge: "STEP 01 / 04",
      target: "preset-scene-bar",
      preferredSide: "bottom",
      pad: 8,
      rx: 14,
      titleKo: "프리셋 & A–H 씬 전환",
      titleEn: "Presets & Scenes (A–H)",
      descKo:
        "프리셋 이름을 눌러 톤을 고르고, A–H 버튼으로 곡 파트별 사운드를 끊김 없이 바꿔보세요. 템포와 병렬 설정도 우측 버튼에 모여 있어요.",
      descEn:
        "Click the preset name to switch or save rigs, and use A–H for dropout-free scene changes. Tempo and Serial/Parallel settings live cleanly in the right pill.",
      tipsKo: [
        "프리셋 제목 클릭 시 이름 수정·저장과 전체 프리셋 선택이 열립니다.",
        "숫자키 1–8로 A–H 씬을 전환하고, 우측 설정에서 BPM과 병렬 믹스를 조절하세요.",
      ],
      tipsEn: [
        "Click the preset title to rename, save, or browse all presets.",
        "Press keys 1–8 to jump between Scenes A–H anytime.",
      ],
      icon: <Sparkles className="size-4 text-primary" />,
    },
    {
      badge: "STEP 02 / 04",
      target: "signal-stage",
      preferredSide: "bottom",
      pad: 6,
      rx: 22,
      titleKo: "시그널 체인 편집",
      titleEn: "Signal Chain Editing",
      descKo:
        "페달·앰프 카드를 누르면 아래에 세부 노브 창이 열리고, 빈 '+' 칸을 누르면 75종 이상의 장비를 바로 추가할 수 있어요.",
      descEn:
        "Tap any gear card to dial in parameters below, or click an empty '+' socket to mount amps, pedals, and cabs from 75+ models.",
      tipsKo: [
        "드래그로 순서를 바꾸고, 더블클릭으로 바이패스(B), 우클릭으로 MIDI 학습을 실행합니다.",
        "외부 .nam, .wav 캐비넷 IR, .nrig 번들은 창에 끌어다 놓으면 바로 장착됩니다.",
      ],
      tipsEn: [
        "Drag to swap blocks, double-click to bypass (B), or right-click for MIDI Learn.",
        "Drag-and-drop .nam captures, .wav IRs, or .nrig bundles anywhere to mount.",
      ],
      icon: <SlidersHorizontal className="size-4 text-primary" />,
    },
    {
      badge: "STEP 03 / 04",
      target: "audio-io",
      preferredSide: "bottom",
      pad: 6,
      rx: 20,
      titleKo: "원클릭 오디오 시작",
      titleEn: "One-Click Audio Start",
      descKo:
        "별도 설정 없이 연결된 오디오 인터페이스나 라인 입력을 자동 감지해 바로 연주를 시작합니다.",
      descEn:
        "Auto-detects connected ASIO interfaces and multi-channel WASAPI inputs so you can start playing in a single click.",
      tipsKo: [
        "ASIO와 WASAPI 라인 인풋을 모두 지원해 어떤 PC 환경에서도 즉시 연주할 수 있어요.",
        "세밀한 채널·버퍼 레이턴시 조정이 필요하면 메뉴 안 오디오 설정을 열어보세요.",
      ],
      tipsEn: [
        "Supports universal ASIO drivers and low-latency WASAPI inputs.",
        "Open Audio I/O inside Menu anytime for channel routing and buffer tuning.",
      ],
      icon: <Volume2 className="size-4 text-primary" />,
    },
    {
      badge: "STEP 04 / 04",
      target: "studio-menu",
      preferredSide: "bottom",
      pad: 6,
      rx: 20,
      titleKo: "튜너 · 루퍼 · 보관함 · 더보기",
      titleEn: "Tuner, Looper, Vault & More",
      descKo:
        "평소엔 연주에만 집중할 수 있도록 튜너, 루퍼·리앰핑, 오디오 세부 설정, Tone Hub, AI 기어 제작(MCP)을 모두 메뉴 안에 깔끔하게 담아 두었습니다.",
      descEn:
        "To keep the workspace calm, Tuner, Looper & Re-Amp, Audio I/O, Vault, Tone Hub, and AI Gear Crafting (MCP) stay neatly inside Menu.",
      tipsKo: [
        "빠른 도구 그리드로 오디오 설정·정밀 튜너·루퍼(L)·검색(/)에 1클릭으로 이동하세요.",
        "AI 기어 제작(npx neuralrig-mcp)과 DSP 벤치마크도 메뉴에서 실행할 수 있습니다.",
      ],
      tipsEn: [
        "Jump straight to Audio I/O, Tuner, Looper (L), and Search (/) from the Menu grid.",
        "Craft new SPICE gear via `npx neuralrig-mcp` or verify DSP accuracy in Benchmark.",
      ],
      icon: <Menu className="size-4 text-primary" />,
      actions: [
        {
          key: "audio",
          labelKo: "오디오 설정 열기",
          labelEn: "Audio I/O",
          action: () => {
            onOpenChange(false)
            onOpenAudioSheet()
          },
        },
        {
          key: "mcp",
          labelKo: "AI 기어 제작 가이드",
          labelEn: "AI Gear Guide",
          action: () => {
            onOpenChange(false)
            onOpenGearGuide()
          },
        },
        {
          key: "bench",
          labelKo: "DSP 벤치마크",
          labelEn: "DSP Benchmark",
          action: () => {
            onOpenChange(false)
            onOpenBenchmark()
          },
        },
      ],
    },
  ]

  const safeStepIdx = Math.min(stepIdx, steps.length - 1)
  const cur = steps[safeStepIdx]
  const isLast = safeStepIdx === steps.length - 1
  const pct = Math.round(((safeStepIdx + 1) / steps.length) * 100)
  const tips = locale === "ko" ? cur.tipsKo : cur.tipsEn

  const handleFinish = () => {
    onComplete()
    onOpenChange(false)
  }

  useLayoutEffect(() => {
    if (!open) return

    const measure = () => {
      const vw = window.innerWidth
      const vh = window.innerHeight
      const el = document.querySelector(`[data-tour="${cur.target}"]`) as HTMLElement | null
      let nextSpot: SpotRect | null = null
      if (el) {
        const r = el.getBoundingClientRect()
        if (r.width > 0 && r.height > 0) {
          const x = Math.max(4, Math.floor(r.left - cur.pad))
          const y = Math.max(4, Math.floor(r.top - cur.pad))
          const right = Math.min(vw - 4, Math.ceil(r.right + cur.pad))
          const bottom = Math.min(vh - 4, Math.ceil(r.bottom + cur.pad))
          nextSpot = {
            x,
            y,
            w: Math.max(0, right - x),
            h: Math.max(0, bottom - y),
            rx: cur.rx,
          }
        }
      }
      setSpot(nextSpot)

      const cw = Math.min(430, vw - 32)
      const ch = cardRef.current?.offsetHeight || cardDims.h
      setCardPos(computeCoachmarkPos(nextSpot, cw, ch, cur.preferredSide, vw, vh))
    }

    measure()
    const raf = requestAnimationFrame(measure)
    const timer = window.setTimeout(measure, 75)
    window.addEventListener("resize", measure)
    return () => {
      cancelAnimationFrame(raf)
      window.clearTimeout(timer)
      window.removeEventListener("resize", measure)
    }
  }, [open, safeStepIdx, cur.target, cur.pad, cur.rx, cur.preferredSide, activeView, cardDims.w, cardDims.h])

  useLayoutEffect(() => {
    if (!open || !cardRef.current) return
    const node = cardRef.current
    const updateDims = () => {
      const r = node.getBoundingClientRect()
      if (r.width > 0 && r.height > 0) {
        setCardDims((prev) =>
          Math.abs(prev.w - r.width) < 1 && Math.abs(prev.h - r.height) < 1
            ? prev
            : { w: Math.round(r.width), h: Math.round(r.height) }
        )
      }
    }
    updateDims()
    const ro = new ResizeObserver(updateDims)
    ro.observe(node)
    return () => ro.disconnect()
  }, [open, safeStepIdx, locale])

  useEffect(() => {
    if (!open) return
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault()
        onOpenChange(false)
      } else if (e.key === "ArrowRight") {
        e.preventDefault()
        setStepIdx((i) => Math.min(steps.length - 1, i + 1))
      } else if (e.key === "ArrowLeft") {
        e.preventDefault()
        setStepIdx((i) => Math.max(0, i - 1))
      }
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [open, steps.length, onOpenChange])

  if (!open || typeof document === "undefined") return null

  const isDark = document.documentElement.classList.contains("dark")
  const backdropFill = isDark ? "rgba(4, 7, 9, 0.72)" : "rgba(11, 14, 16, 0.54)"

  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={headingId}
      className="fixed inset-0 z-[60]"
      data-tour-overlay="true"
    >
      {/* Semi-Transparent SVG Backdrop with Sharp Cutout */}
      <svg
        className="fixed inset-0 w-full h-full cursor-pointer"
        onClick={() => onOpenChange(false)}
        aria-hidden="true"
      >
        <defs>
          <mask id={maskId}>
            <rect width="100%" height="100%" fill="white" />
            {spot && (
              <rect
                x={spot.x}
                y={spot.y}
                width={spot.w}
                height={spot.h}
                rx={spot.rx}
                fill="black"
                className="transition-all duration-200 ease-out"
              />
            )}
          </mask>
        </defs>
        <rect width="100%" height="100%" fill={backdropFill} mask={`url(#${maskId})`} />
      </svg>

      {/* Highlight Frame Around Target */}
      {spot && (
        <div
          aria-hidden="true"
          data-tour-highlight={cur.target}
          style={{
            left: spot.x,
            top: spot.y,
            width: spot.w,
            height: spot.h,
            borderRadius: spot.rx,
          }}
          className="fixed pointer-events-none border-2 border-primary shadow-[0_0_0_4px_rgba(255,107,36,0.20),0_12px_36px_rgba(255,107,36,0.30)] transition-all duration-200 ease-out"
        />
      )}

      {/* Floating Toss-Style Coachmark Callout Card */}
      <div
        ref={cardRef}
        data-tour-card={cur.target}
        onClick={(e) => e.stopPropagation()}
        style={{
          left: cardPos.left,
          top: cardPos.top,
          width: "min(430px, calc(100vw - 32px))",
        }}
        className="fixed z-10 rounded-[16px] border border-border bg-popover/96 backdrop-blur-md text-popover-foreground shadow-[0_28px_72px_rgba(11,14,16,0.34)] overflow-hidden transition-[left,top] duration-200 ease-out"
      >
        <div className="px-4 pt-3 pb-2.5 border-b border-border bg-secondary/40 space-y-2">
          <div className="flex items-start justify-between gap-2.5">
            <div className="flex items-start gap-2.5 min-w-0">
              <div className="size-8 rounded-[10px] bg-oled border border-oled-border flex items-center justify-center shrink-0 mt-0.5">
                {cur.icon}
              </div>
              <div className="min-w-0">
                <div className="font-mono text-[10.5px] font-semibold tracking-wider text-primary">
                  {cur.badge}
                </div>
                <h2
                  id={headingId}
                  className="font-display font-light text-[17px] leading-snug tracking-[-0.015em] text-foreground"
                >
                  {tx(cur.titleKo, cur.titleEn)}
                </h2>
              </div>
            </div>

            <Hint content={tx("투어 닫기", "Close Tour")} kbd="Esc">
              <Button
                size="icon-xs"
                variant="ghost"
                onClick={() => onOpenChange(false)}
                className="shrink-0"
              >
                <X className="size-3.5" />
              </Button>
            </Hint>
          </div>

          <div className="h-[2px] w-full rounded-full bg-border overflow-hidden">
            <div
              className="h-full bg-primary transition-all duration-200"
              style={{ width: `${pct}%` }}
            />
          </div>
        </div>

        <div className="px-4 py-3 space-y-2.5">
          <p className="text-[12px] leading-relaxed text-foreground">
            {tx(cur.descKo, cur.descEn)}
          </p>

          <ul className="space-y-1.5 rounded-[12px] border border-border bg-card/85 px-3 py-2">
            {tips.map((tip, i) => (
              <li
                key={i}
                className="flex items-start gap-2 text-[11px] leading-snug text-muted-foreground"
              >
                <CheckCircle2 className="size-3.5 text-signal-ok shrink-0 mt-0.5" />
                <span className="text-foreground/90">{tip}</span>
              </li>
            ))}
          </ul>

          {cur.actions && cur.actions.length > 0 && (
            <div className="flex flex-wrap items-center gap-1.5 pt-0.5">
              {cur.actions.map((act) => (
                <Button key={act.key} size="xs" variant="keycap" onClick={act.action}>
                  {act.key === "audio" && <SlidersHorizontal className="size-3 text-primary" />}
                  {act.key === "mcp" && <Wrench className="size-3 text-primary" />}
                  {act.key === "bench" && <Gauge className="size-3 text-signal-ok" />}
                  {tx(act.labelKo, act.labelEn)}
                </Button>
              ))}
            </div>
          )}
        </div>

        <div className="px-4 py-2.5 border-t border-border bg-secondary/45 flex items-center justify-between gap-2">
          <div className="flex items-center gap-2.5">
            <div className="flex items-center gap-1">
              {steps.map((_, idx) => (
                <button
                  key={idx}
                  type="button"
                  aria-label={`Step ${idx + 1}`}
                  onClick={() => setStepIdx(idx)}
                  className={`h-1.5 rounded-full transition-all cursor-pointer ${
                    idx === safeStepIdx
                      ? "w-5 bg-primary"
                      : "w-1.5 bg-border hover:bg-muted-foreground"
                  }`}
                />
              ))}
            </div>
            <Button
              size="xs"
              variant="ghost"
              onClick={handleFinish}
              className="text-[11px] px-1.5"
            >
              {tx("건너뛰기", "SKIP")}
            </Button>
          </div>

          <div className="flex items-center gap-1.5">
            <Button
              size="xs"
              variant="keycap"
              disabled={safeStepIdx === 0}
              onClick={() => setStepIdx((i) => Math.max(0, i - 1))}
            >
              <ArrowLeft className="size-3" />
              <span>{tx("이전", "BACK")}</span>
            </Button>
            {isLast ? (
              <Button size="xs" variant="signal" onClick={handleFinish} className="px-3">
                <CheckCircle2 className="size-3" />
                <span>{tx("완료", "FINISH")}</span>
              </Button>
            ) : (
              <Button
                size="xs"
                variant="signal"
                onClick={() => setStepIdx((i) => Math.min(steps.length - 1, i + 1))}
                className="px-3"
              >
                <span>{tx("다음", "NEXT")}</span>
                <ArrowRight className="size-3" />
              </Button>
            )}
          </div>
        </div>
      </div>
    </div>,
    document.body
  )
}
