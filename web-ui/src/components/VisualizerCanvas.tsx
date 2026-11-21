import React, { useEffect, useRef, useState } from "react"
import { Activity, Waves, BarChart3 } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { studioBridge } from "@/lib/bridge"
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

export const VisualizerCanvas: React.FC<{ activeToneLabel?: string }> = ({ activeToneLabel }) => {
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

      // Background card well
      ctx.fillStyle = "#09090b"
      ctx.fillRect(0, 0, width, height)

      // ── Stereo dB Meter strip at top ──
      const meterTop = 8
      const meterLeft = 30
      const meterRight = width - 48
      const meterW = Math.max(80, meterRight - meterLeft)
      const barH = 9

      const drawMeterBar = (y: number, tag: string, val: number) => {
        ctx.fillStyle = "#a1a1aa"
        ctx.font = "600 10px ui-monospace, SFMono-Regular, Menlo, monospace"
        ctx.fillText(tag, 8, y + 8)

        ctx.fillStyle = "#18181b"
        ctx.fillRect(meterLeft, y, meterW, barH)

        const norm = linToNormDb(val)
        const fillW = Math.round(norm * meterW)
        if (fillW > 0) {
          const grad = ctx.createLinearGradient(meterLeft, 0, meterRight, 0)
          grad.addColorStop(0.0, "#22c55e")
          grad.addColorStop(0.76, "#22c55e")
          grad.addColorStop(0.84, "#eab308")
          grad.addColorStop(0.95, "#ef4444")
          grad.addColorStop(1.0, "#ef4444")
          ctx.fillStyle = grad
          ctx.fillRect(meterLeft, y, fillW, barH)
        }

        // -12 dB and -6 dB ticks
        ctx.fillStyle = "#3f3f46"
        ctx.fillRect(meterLeft + meterW * 0.8, y, 1, barH)
        ctx.fillRect(meterLeft + meterW * 0.9, y, 1, barH)

        ctx.fillStyle = "#d4d4d8"
        ctx.font = "10px ui-monospace, SFMono-Regular, Menlo, monospace"
        ctx.textAlign = "right"
        ctx.fillText(dbStr(val), width - 8, y + 8)
        ctx.textAlign = "left"
      }

      drawMeterBar(meterTop, "L", f.meterL)
      drawMeterBar(meterTop + 14, "R", f.meterR)

      // ── Plot area below meter ──
      const padL = 12
      const padR = 12
      const plotTop = meterTop + 34
      const plotBot = height - 22
      const plotW = Math.max(40, width - padL - padR)
      const plotH = Math.max(40, plotBot - plotTop)

      // Grid lines (-12dB quartiles)
      ctx.strokeStyle = "rgba(63, 63, 70, 0.35)"
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

      // 1) Spectrum bars + peak caps
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
            const barGrad = ctx.createLinearGradient(0, by, 0, plotBot)
            if (val > 0.85) {
              barGrad.addColorStop(0, "rgba(248, 113, 113, 0.95)")
              barGrad.addColorStop(0.45, "rgba(250, 204, 21, 0.85)")
              barGrad.addColorStop(1, "rgba(34, 197, 94, 0.35)")
            } else {
              barGrad.addColorStop(0, "rgba(74, 222, 128, 0.92)")
              barGrad.addColorStop(1, "rgba(16, 185, 129, 0.25)")
            }
            ctx.fillStyle = barGrad
            ctx.fillRect(bx, by, barW, bh)
          }

          const cap = peakCaps[b] || 0
          if (cap > 0.02) {
            const capY = Math.round(plotBot - cap * (plotH - 6))
            ctx.fillStyle = "rgba(250, 250, 250, 0.78)"
            ctx.fillRect(bx, capY - 2, barW, 2)
          }
        }
      }

      // 2) Cab IR Curve overlay
      if (curMode !== "waveform" && hasIrCurve && irCurve.length > 1) {
        ctx.save()
        ctx.strokeStyle = "rgba(245, 158, 11, 0.9)"
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

      // 3) Zero-crossing Oscilloscope Waveform
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
        ctx.strokeStyle = !running
          ? "rgba(82, 82, 91, 0.45)"
          : curMode === "combined"
            ? "rgba(56, 189, 248, 0.85)"
            : "rgba(74, 222, 128, 0.95)"
        ctx.lineWidth = curMode === "waveform" ? 1.9 : 1.35
        ctx.stroke()
        ctx.restore()
      }

      // Frequency / axis footer labels
      ctx.fillStyle = "#71717a"
      ctx.font = "10px ui-monospace, SFMono-Regular, Menlo, monospace"
      const labels =
        curMode === "waveform"
          ? ["0 ms", "Waveform (Zero-Crossing Trigger)", "11.6 ms"]
          : ["80Hz", "250Hz", "800Hz", "2.5kHz", "8kHz"]
      labels.forEach((txt, idx) => {
        const x = padL + (idx / Math.max(1, labels.length - 1)) * (plotW - 36)
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
    window.addEventListener("resize", onResize)
    return () => {
      unsub()
      window.removeEventListener("resize", onResize)
    }
  }, [])

  return (
    <Card>
      <CardHeader className="pb-2 flex flex-row items-center justify-between gap-2 space-y-0">
        <div className="flex items-center gap-2">
          <Activity className="w-4 h-4 text-emerald-400" />
          <CardTitle className="text-sm font-semibold">실시간 스펙트럼 · 스테레오 dB 미터</CardTitle>
          {activeToneLabel && (
            <Badge variant="outline" className="text-[10px] border-amber-500/40 text-amber-300">
              IR: {activeToneLabel}
            </Badge>
          )}
        </div>
        <div className="flex items-center gap-2">
          <Badge
            variant="outline"
            className={
              clipActive
                ? "border-red-500 bg-red-500/20 text-red-300 font-mono text-[10px]"
                : signalState === "active"
                  ? "border-emerald-500/40 text-emerald-300 font-mono text-[10px]"
                  : "border-border text-muted-foreground font-mono text-[10px]"
            }
          >
            {clipActive ? "CLIP" : peakLabel}
          </Badge>
          <Tabs value={mode} onValueChange={(v) => setMode(v as VisMode)}>
            <TabsList className="h-7 bg-muted">
              <TabsTrigger value="combined" className="text-xs px-2 h-5">복합</TabsTrigger>
              <TabsTrigger value="spectrum" className="text-xs px-2 h-5">
                <BarChart3 className="w-3 h-3 mr-1" />FFT
              </TabsTrigger>
              <TabsTrigger value="waveform" className="text-xs px-2 h-5">
                <Waves className="w-3 h-3 mr-1" />파형
              </TabsTrigger>
            </TabsList>
          </Tabs>
        </div>
      </CardHeader>
      <CardContent className="pt-1">
        <div className="rounded-md border border-border overflow-hidden bg-background">
          <canvas ref={canvasRef} className="block w-full h-[255px]" />
        </div>
        <div className="mt-2 flex items-center justify-between text-[11px] text-muted-foreground">
          <span>
            {signalState === "idle" && "모니터 대기 중"}
            {signalState === "wait" && "신호 대기 중"}
            {signalState === "active" && "모니터링 활성"}
          </span>
          <span className="font-mono">1024-pt · 36-band</span>
        </div>
      </CardContent>
    </Card>
  )
}
