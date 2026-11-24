import React, { useEffect, useRef, useState } from "react"
import { Activity, BarChart3, Waves } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
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

    const readPalette = () => {
      const cs = getComputedStyle(document.documentElement)
      return {
        bg: cs.getPropertyValue("--surface-oled").trim() || "#0D0E0D",
        elevated: cs.getPropertyValue("--surface-oled-elevated").trim() || "#171917",
        fg: cs.getPropertyValue("--ink-oled").trim() || "#F2EFE9",
        muted: cs.getPropertyValue("--ink-oled-muted").trim() || "#8E8C84",
        border: cs.getPropertyValue("--border-oled").trim() || "#2E312D",
        hot: cs.getPropertyValue("--signal-hot").trim() || "#FF4400",
        ok: cs.getPropertyValue("--signal-ok").trim() || "#2CE674",
        warn: cs.getPropertyValue("--signal-warn").trim() || "#FF9F0A",
      }
    }

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
      const pal = readPalette()

      ctx.save()
      ctx.scale(dpr, dpr)

      const width = rect.width
      const height = rect.height
      ctx.clearRect(0, 0, width, height)

      // Recessed Matte OLED Surface + Subtle Dot Grid
      ctx.fillStyle = pal.bg
      ctx.fillRect(0, 0, width, height)
      ctx.fillStyle = "rgba(242, 239, 233, 0.08)"
      for (let gx = 12; gx < width; gx += 16) {
        for (let gy = 48; gy < height - 20; gy += 16) {
          ctx.fillRect(gx, gy, 1, 1)
        }
      }

      // Stereo dB Meter Strip
      const meterTop = 10
      const meterLeft = 30
      const meterRight = width - 52
      const meterW = Math.max(80, meterRight - meterLeft)
      const barH = 8

      const drawMeterBar = (y: number, tag: string, val: number) => {
        ctx.fillStyle = pal.muted
        ctx.font = '600 10px "IBM Plex Mono", ui-monospace, monospace'
        ctx.fillText(tag, 10, y + 7)

        ctx.fillStyle = pal.elevated
        ctx.fillRect(meterLeft, y, meterW, barH)

        const norm = linToNormDb(val)
        const fillW = Math.round(norm * meterW)
        if (fillW > 0) {
          const grad = ctx.createLinearGradient(meterLeft, 0, meterRight, 0)
          grad.addColorStop(0.0, pal.ok)
          grad.addColorStop(0.76, pal.ok)
          grad.addColorStop(0.86, pal.warn)
          grad.addColorStop(0.95, pal.hot)
          grad.addColorStop(1.0, pal.hot)
          ctx.fillStyle = grad
          ctx.fillRect(meterLeft, y, fillW, barH)
        }

        ctx.fillStyle = pal.border
        ctx.fillRect(meterLeft + meterW * 0.8, y, 1, barH)
        ctx.fillRect(meterLeft + meterW * 0.9, y, 1, barH)

        ctx.fillStyle = pal.fg
        ctx.font = '500 10px "IBM Plex Mono", ui-monospace, monospace'
        ctx.textAlign = "right"
        ctx.fillText(dbStr(val), width - 10, y + 7)
        ctx.textAlign = "left"
      }

      drawMeterBar(meterTop, "L", f.meterL)
      drawMeterBar(meterTop + 13, "R", f.meterR)

      // Plot Area
      const padL = 12
      const padR = 12
      const plotTop = meterTop + 34
      const plotBot = height - 22
      const plotW = Math.max(40, width - padL - padR)
      const plotH = Math.max(40, plotBot - plotTop)

      ctx.strokeStyle = pal.border
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

      // 1) Spectrum Bars + Peak Caps
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
            ctx.fillStyle = val > 0.85 ? pal.hot : val > 0.65 ? pal.warn : pal.ok
            ctx.globalAlpha = 0.82
            ctx.fillRect(bx, by, barW, bh)
            ctx.globalAlpha = 1
          }

          const cap = peakCaps[b] || 0
          if (cap > 0.02) {
            const capY = Math.round(plotBot - cap * (plotH - 6))
            ctx.fillStyle = pal.fg
            ctx.fillRect(bx, capY - 2, barW, 2)
          }
        }
      }

      // 2) Cab IR Curve Overlay
      if (curMode !== "waveform" && hasIrCurve && irCurve.length > 1) {
        ctx.save()
        ctx.strokeStyle = pal.warn
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

      // 3) Zero-Crossing Oscilloscope Waveform
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
        ctx.strokeStyle = !running ? pal.muted : curMode === "combined" ? pal.fg : pal.hot
        ctx.lineWidth = curMode === "waveform" ? 1.9 : 1.35
        ctx.stroke()
        ctx.restore()
      }

      // Footer Axis Labels
      ctx.fillStyle = pal.muted
      ctx.font = '500 10px "IBM Plex Mono", ui-monospace, monospace'
      const labels =
        curMode === "waveform"
          ? ["0 ms", "ZERO-CROSSING TRIGGER", "11.6 ms"]
          : ["80Hz", "250Hz", "800Hz", "2.5kHz", "8kHz"]
      labels.forEach((txt, idx) => {
        const x = padL + (idx / Math.max(1, labels.length - 1)) * (plotW - 44)
        ctx.fillText(txt, x, height - 6)
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
    <Card className={cn("flex flex-col py-3 gap-2.5", className)}>
      <CardHeader className="flex flex-row items-center justify-between gap-2 space-y-0 pb-0">
        <div className="flex items-center gap-2">
          <Activity className="size-4 text-primary" strokeWidth={1.5} />
          <CardTitle className="text-xs font-mono uppercase tracking-wider">
            SPECTRUM · OSCILLOSCOPE
          </CardTitle>
          {activeToneLabel && (
            <Badge variant="warn" size="xs">
              IR: {activeToneLabel}
            </Badge>
          )}
        </div>
        <div className="flex items-center gap-2">
          <Badge
            variant={clipActive ? "hot" : signalState === "active" ? "ok" : "outline"}
            size="xs"
            className="tabular-nums"
          >
            {clipActive ? "CLIP" : peakLabel}
          </Badge>
          <Tabs value={mode} onValueChange={(v) => setMode(v as VisMode)}>
            <TabsList className="h-7">
              <TabsTrigger value="combined" className="h-5 px-2 text-[10px]">
                {tx("복합", "BOTH")}
              </TabsTrigger>
              <TabsTrigger value="spectrum" className="h-5 px-2 text-[10px]">
                <BarChart3 className="size-3" />
                FFT
              </TabsTrigger>
              <TabsTrigger value="waveform" className="h-5 px-2 text-[10px]">
                <Waves className="size-3" />
                {tx("파형", "WAVE")}
              </TabsTrigger>
            </TabsList>
          </Tabs>
        </div>
      </CardHeader>
      <CardContent className="flex-1 min-h-0 flex flex-col gap-2">
        <div className="flex-1 min-h-0 rounded-sm border border-oled-border overflow-hidden bg-oled">
          <canvas ref={canvasRef} className={cn("block w-full h-[260px]", canvasClassName)} />
        </div>
        <div className="flex items-center justify-between font-mono text-[10px] text-muted-foreground">
          <span>
            {signalState === "idle" && tx("오디오 대기 중", "ENGINE STANDBY")}
            {signalState === "wait" && tx("기타 신호 대기 중", "AWAITING SIGNAL")}
            {signalState === "active" && tx("실시간 모니터링 활성", "LIVE MONITORING")}
          </span>
          <span className="tabular-nums">1024-PT FFT · 36-BAND · ZERO-CROSSING</span>
        </div>
      </CardContent>
    </Card>
  )
}
