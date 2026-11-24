import React, { useEffect, useRef, useState } from "react"
import { Activity, BarChart3, Waves } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { studioBridge } from "@/lib/bridge"
import { useI18n } from "@/lib/i18n"
import { cn } from "@/lib/utils"
import type { TelemetryFrame } from "@/types/rig"

type VisMode = "combined" | "spectrum" | "waveform"

const EMPTY_FRAME: TelemetryFrame = {
  running: false,
  busy: false,
  meterL: 0,
  meterR: 0,
  clip: false,
  latency: null,
  counters: { padded: 0, dropped: 0, lost: 0, sampleRate: 44100 },
  vis: {
    running: false,
    hasSignal: false,
    hasIrCurve: false,
    peakDb: -90,
    bands: new Array(36).fill(0),
    peakCaps: new Array(36).fill(0),
    wave: new Array(180).fill(0),
    irCurve: [],
  },
}

function linToNormDb(lin: number): number {
  const db = lin > 1e-6 ? 20 * Math.log10(lin) : -60
  return Math.max(0, Math.min(1, (db + 60) / 60))
}

function dbStr(lin: number): string {
  if (lin <= 1e-5) return "-inf"
  const db = 20 * Math.log10(lin)
  return `${db >= 0 ? "+" : ""}${db.toFixed(1)}`
}

export const VisualizerCanvas: React.FC<{
  activeToneLabel?: string
  className?: string
  canvasClassName?: string
}> = ({ activeToneLabel, className, canvasClassName }) => {
  const { tx } = useI18n()
  const [mode, setMode] = useState<VisMode>("combined")
  const [peakLabel, setPeakLabel] = useState<string>("-inf dB")
  const [signalState, setSignalState] = useState<"idle" | "wait" | "active">("idle")
  const [clipActive, setClipActive] = useState(false)

  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const latestRef = useRef<TelemetryFrame>(EMPTY_FRAME)
  const modeRef = useRef<VisMode>(mode)
  modeRef.current = mode

  useEffect(() => {
    let lastUiSync = 0

    const draw = (f: TelemetryFrame) => {
      const canvas = canvasRef.current
      if (!canvas) return
      const rect = canvas.getBoundingClientRect()
      if (rect.width < 10 || rect.height < 10) return

      const dpr = window.devicePixelRatio || 1
      const w = Math.round(rect.width * dpr)
      const h = Math.round(rect.height * dpr)
      if (canvas.width !== w || canvas.height !== h) {
        canvas.width = w
        canvas.height = h
      }

      const ctx = canvas.getContext("2d")
      if (!ctx) return

      ctx.save()
      ctx.scale(dpr, dpr)

      const width = rect.width
      const height = rect.height
      ctx.clearRect(0, 0, width, height)

      // Dark Obsidian Canvas + Top-Left Tangerine Radial Glow
      ctx.fillStyle = "#0d1113"
      ctx.fillRect(0, 0, width, height)
      const glow = ctx.createRadialGradient(width * 0.16, height * 0.1, 10, width * 0.16, height * 0.1, width * 0.65)
      glow.addColorStop(0, "rgba(255, 118, 42, 0.22)")
      glow.addColorStop(1, "rgba(13, 17, 19, 0)")
      ctx.fillStyle = glow
      ctx.fillRect(0, 0, width, height)

      // Stereo dB Meter Strip
      const meterTop = 12
      const meterLeft = 32
      const meterRight = width - 54
      const meterW = Math.max(80, meterRight - meterLeft)
      const barH = 7

      const drawMeterBar = (y: number, tag: string, val: number) => {
        ctx.fillStyle = "rgba(245, 247, 246, 0.55)"
        ctx.font = '500 10px "IBM Plex Mono", ui-monospace, monospace'
        ctx.fillText(tag, 12, y + 6.5)

        ctx.fillStyle = "rgba(255, 255, 255, 0.08)"
        ctx.fillRect(meterLeft, y, meterW, barH)

        const norm = linToNormDb(val)
        const fillW = Math.round(norm * meterW)
        if (fillW > 0) {
          const grad = ctx.createLinearGradient(meterLeft, 0, meterRight, 0)
          grad.addColorStop(0.0, "#f5f7f6")
          grad.addColorStop(0.7, "#f5f7f6")
          grad.addColorStop(0.84, "#ff8c42")
          grad.addColorStop(1.0, "#ff6b24")
          ctx.fillStyle = grad
          ctx.fillRect(meterLeft, y, fillW, barH)
        }

        ctx.fillStyle = "#f5f7f6"
        ctx.font = '500 10px "IBM Plex Mono", ui-monospace, monospace'
        ctx.textAlign = "right"
        ctx.fillText(dbStr(val), width - 12, y + 6.5)
        ctx.textAlign = "left"
      }

      drawMeterBar(meterTop, "L", f.meterL)
      drawMeterBar(meterTop + 12, "R", f.meterR)

      // Plot Area
      const padL = 14
      const padR = 14
      const plotTop = meterTop + 34
      const plotBot = height - 24
      const plotW = Math.max(40, width - padL - padR)
      const plotH = Math.max(40, plotBot - plotTop)

      ctx.strokeStyle = "rgba(255, 255, 255, 0.08)"
      ctx.lineWidth = 1
      for (let g = 0; g <= 4; g++) {
        const gy = Math.round(plotTop + (plotH * g) / 4) + 0.5
        ctx.beginPath()
        ctx.moveTo(padL, gy)
        ctx.lineTo(padL + plotW, gy)
        ctx.stroke()
      }

      const curMode = modeRef.current
      const { bands, peakCaps, wave, irCurve, hasIrCurve, running } = f.vis

      // 1) Alternating White & Tangerine Harmonic Bars (FeatureTrioStages Stage 1 Homage)
      if (curMode !== "waveform" && bands.length > 0) {
        const n = bands.length
        const slotW = plotW / n
        const barW = Math.max(3, slotW - 3)
        for (let b = 0; b < n; b++) {
          const val = bands[b] || 0
          const bh = Math.round(val * (plotH - 6))
          const bx = Math.round(padL + b * slotW + 1.5)
          const by = plotBot - bh
          if (bh > 1) {
            ctx.fillStyle = val > 0.85 || b % 2 === 1 ? "#ff7730" : "rgba(245, 247, 246, 0.85)"
            ctx.fillRect(bx, by, barW, bh)
          }

          const cap = peakCaps[b] || 0
          if (cap > 0.02) {
            const capY = Math.round(plotBot - cap * (plotH - 6))
            ctx.fillStyle = "#ffffff"
            ctx.fillRect(bx, capY - 2, barW, 2)
          }
        }
      }

      // 2) Cab IR Curve Overlay
      if (curMode !== "waveform" && hasIrCurve && irCurve.length > 1) {
        ctx.save()
        ctx.strokeStyle = "#ff8c42"
        ctx.lineWidth = 1.6
        ctx.setLineDash([4, 3])
        ctx.beginPath()
        for (let b = 0; b < irCurve.length; b++) {
          const x = padL + ((b + 0.5) * plotW) / irCurve.length
          const y = plotBot - irCurve[b] * (plotH - 10)
          if (b === 0) ctx.moveTo(x, y)
          else ctx.lineTo(x, y)
        }
        ctx.stroke()
        ctx.restore()
      }

      // 3) Glowing Tangerine Zero-Crossing Waveform (HeroFloatingCards Homage)
      if (curMode !== "spectrum" && wave.length > 1) {
        const midY = plotTop + plotH * 0.5
        const ampScale = plotH * (curMode === "waveform" ? 0.44 : 0.34)
        ctx.save()
        ctx.beginPath()
        for (let i = 0; i < wave.length; i++) {
          const x = padL + (i / (wave.length - 1)) * plotW
          const y = midY - wave[i] * ampScale
          if (i === 0) ctx.moveTo(x, y)
          else ctx.lineTo(x, y)
        }
        ctx.strokeStyle = !running ? "rgba(255,255,255,0.22)" : "#ff7730"
        ctx.lineWidth = curMode === "waveform" ? 2.1 : 1.5
        ctx.stroke()
        ctx.restore()
      }

      // Footer Axis Labels
      ctx.fillStyle = "rgba(245, 247, 246, 0.45)"
      ctx.font = '500 10px "IBM Plex Mono", ui-monospace, monospace'
      const labels =
        curMode === "waveform"
          ? ["0 ms", "ZERO-CROSSING TRIGGER", "11.6 ms"]
          : ["80Hz", "250Hz", "800Hz", "2.5kHz", "8kHz"]
      labels.forEach((txt, idx) => {
        const x = padL + (idx / Math.max(1, labels.length - 1)) * (plotW - 44)
        ctx.fillText(txt, x, height - 7)
      })

      ctx.restore()
    }

    draw(latestRef.current)

    const unsub = studioBridge.on<TelemetryFrame>("telemetry", (frame) => {
      latestRef.current = frame
      draw(frame)

      const now = performance.now()
      if (now - lastUiSync > 90) {
        lastUiSync = now
        setClipActive(frame.clip)
        setPeakLabel(frame.vis.peakDb <= -85 ? "-inf dB" : `${frame.vis.peakDb.toFixed(1)} dB`)
        setSignalState(!frame.vis.running ? "idle" : frame.vis.hasSignal ? "active" : "wait")
      }
    })

    const onResize = () => draw(latestRef.current)
    const onTheme = () => draw(latestRef.current)
    window.addEventListener("resize", onResize)
    window.addEventListener("studio-theme-change", onTheme)
    return () => {
      unsub()
      window.removeEventListener("resize", onResize)
      window.removeEventListener("studio-theme-change", onTheme)
    }
  }, [mode])

  return (
    <div
      className={cn(
        "flex flex-col rounded-[12px] border border-oled-border nr-obsidian-stage text-oled-foreground p-4 gap-3",
        className
      )}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 min-w-0">
          <Activity className="size-4 text-primary shrink-0" strokeWidth={1.6} />
          <span className="font-mono text-xs font-medium tracking-[0.1em] uppercase truncate">
            HARMONIC SPECTRUM · SCOPE
          </span>
          {activeToneLabel && (
            <Badge variant="warn" size="xs" className="max-w-[180px]">
              <span className="truncate">IR: {activeToneLabel}</span>
            </Badge>
          )}
        </div>

        <div className="flex items-center gap-2">
          <Badge
            variant={clipActive ? "hot" : signalState === "active" ? "ok" : "oled"}
            size="xs"
            className="tabular-nums"
          >
            {clipActive ? "CLIP" : peakLabel}
          </Badge>
          <Tabs value={mode} onValueChange={(v) => setMode(v as VisMode)}>
            <TabsList className="h-7 bg-white/[0.07] border-oled-border text-oled-muted">
              <TabsTrigger
                value="combined"
                className="h-5 px-2 text-[10px] data-[state=active]:bg-oled-foreground data-[state=active]:text-oled"
              >
                {tx("복합", "BOTH")}
              </TabsTrigger>
              <TabsTrigger
                value="spectrum"
                className="h-5 px-2 text-[10px] data-[state=active]:bg-oled-foreground data-[state=active]:text-oled"
              >
                <BarChart3 className="size-3" />
                FFT
              </TabsTrigger>
              <TabsTrigger
                value="waveform"
                className="h-5 px-2 text-[10px] data-[state=active]:bg-oled-foreground data-[state=active]:text-oled"
              >
                <Waves className="size-3" />
                {tx("파형", "WAVE")}
              </TabsTrigger>
            </TabsList>
          </Tabs>
        </div>
      </div>

      <div className="flex-1 min-h-0 rounded-[8px] border border-oled-border overflow-hidden bg-oled">
        <canvas ref={canvasRef} className={cn("block w-full h-[260px]", canvasClassName)} />
      </div>

      <div className="flex items-center justify-between font-mono text-[10px] text-oled-muted">
        <span>
          {signalState === "idle" && tx("오디오 대기 중", "ENGINE STANDBY")}
          {signalState === "wait" && tx("기타 신호 대기 중", "AWAITING SIGNAL")}
          {signalState === "active" && tx("실시간 모니터링 활성", "LIVE MONITORING")}
        </span>
        <span className="tabular-nums">1024-PT FFT · 36-BAND · ZERO-CROSSING</span>
      </div>
    </div>
  )
}
