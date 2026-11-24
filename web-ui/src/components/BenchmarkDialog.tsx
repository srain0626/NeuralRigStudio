import React, { useEffect, useState } from "react"
import {
  Activity,
  CheckCircle2,
  Cpu,
  Gauge,
  Layers,
  Play,
  ShieldCheck,
  Waves,
  XCircle,
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
import { ScrollArea } from "@/components/ui/scroll-area"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { HelpHint } from "@/components/ui/tooltip"
import { studioBridge } from "@/lib/bridge"
import { useI18n } from "@/lib/i18n"
import type { RigBenchmarkReportDto } from "@/types/rig"

interface BenchmarkDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  isAudioRunning: boolean
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

type BenchTab = "similarity" | "latency" | "multichain"

export const BenchmarkDialog: React.FC<BenchmarkDialogProps> = ({
  open,
  onOpenChange,
  isAudioRunning,
  onLog,
}) => {
  const { tx } = useI18n()
  const [tab, setTab] = useState<BenchTab>("similarity")
  const [running, setRunning] = useState(false)
  const [report, setReport] = useState<RigBenchmarkReportDto | null>(null)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  const handleRun = async () => {
    if (!studioBridge.isWebView2) {
      setErrorMsg(
        tx(
          "네이티브 호스트(WebView2) 환경에서만 실시간 DSP 벤치마크를 실행할 수 있습니다.",
          "Live DSP benchmarks require the native desktop host."
        )
      )
      return
    }
    setRunning(true)
    setErrorMsg(null)
    try {
      const res = await studioBridge.invoke<RigBenchmarkReportDto>("runBenchmarkSuite", {})
      setReport(res)
      onLog(
        tx(
          `벤치마크 완료: 유사도 ${res.overallSimilarityPct.toFixed(1)}% · 64fr ${res.minBuffer64Pass ? "PASS" : "OVER"} · RT 할당 ${res.zeroRtAllocAll ? "0 B" : "NON-ZERO"}`,
          `Benchmark finished: Similarity ${res.overallSimilarityPct.toFixed(1)}% · 64fr ${res.minBuffer64Pass ? "PASS" : "OVER"} · RT Alloc ${res.zeroRtAllocAll ? "0 B" : "NON-ZERO"}`
        ),
        res.overallPassed ? "success" : "error"
      )
    } catch (e) {
      const msg = String(e)
      setErrorMsg(msg)
      onLog(tx(`벤치마크 실행 오류: ${msg}`, `Benchmark error: ${msg}`), "error")
    } finally {
      setRunning(false)
    }
  }

  useEffect(() => {
    if (open && !report && !running && studioBridge.isWebView2) {
      void handleRun()
    }
  }, [open])

  const buf64MaxCpu = report
    ? report.latencyItems
        .filter((x) => x.bufferFrames === 64)
        .reduce((acc, x) => Math.max(acc, x.cpuLoadPct), 0)
    : 0

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl p-0 overflow-hidden bg-card text-card-foreground border-border gap-0">
        <DialogHeader className="px-5 py-3.5 border-b border-border bg-secondary/45 shrink-0">
          <div className="flex flex-wrap items-center justify-between gap-3 pr-8">
            <div className="flex items-center gap-2.5 min-w-0">
              <div className="size-8 rounded-sm bg-oled border border-oled-border flex items-center justify-center text-primary shrink-0">
                <Gauge className="size-4" strokeWidth={1.7} />
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <DialogTitle className="truncate">
                    {tx("DSP 성능 · 유사도 벤치마크", "DSP & FIDELITY BENCHMARK")}
                  </DialogTitle>
                  <HelpHint
                    side="bottom"
                    content={tx(
                      "MNA SPICE 회로 해석, 4x HIIR 비대칭 클리핑, WaveNet ESR, 64/128/256fr RT 데드라인 및 16슬롯 스트레스 검증을 수행합니다.",
                      "MNA SPICE nodal checks, 4x HIIR harmonic checks, WaveNet ESR, 64/128/256fr RT deadlines, and 16-slot stress suite."
                    )}
                  />
                </div>
                <DialogDescription className="sr-only">
                  {tx(
                    "MNA SPICE 물리 회로 해석 · 4x HIIR 대칭/비대칭 클리핑 · WaveNet · 64/128/256fr RT 데드라인 · 16슬롯 스트레스 검증",
                    "MNA SPICE nodal checks · 4x HIIR harmonic verification · WaveNet ESR · 64/128/256fr RT deadlines · 16-slot torture suite"
                  )}
                </DialogDescription>
              </div>
            </div>

            <Button
              size="sm"
              variant="signal"
              disabled={running}
              onClick={() => void handleRun()}
            >
              <Play className="size-3.5" />
              <span>
                {running
                  ? tx("계측 중...", "RUNNING...")
                  : report
                    ? tx("벤치마크 다시 실행", "RE-RUN SUITE")
                    : tx("벤치마크 실행", "RUN SUITE")}
              </span>
            </Button>
          </div>

          {/* KPI Summary Cards */}
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 pt-3">
            <div className="rounded-sm border border-oled-border bg-oled px-3 py-2">
              <div className="flex items-center justify-between font-mono text-[10px] text-oled-muted uppercase tracking-wider">
                <span>{tx("하드웨어 유사도", "HARDWARE FIDELITY")}</span>
                <Waves className="size-3 text-primary" />
              </div>
              <div className="mt-1 flex items-baseline gap-1.5">
                <span className="font-mono text-base font-bold tabular-nums text-oled-foreground">
                  {report ? `${report.overallSimilarityPct.toFixed(1)}%` : "--"}
                </span>
                {report && (
                  <Badge variant="ok" size="xs">
                    {report.similarityItems.filter((x) => x.passed).length}/{report.similarityItems.length}
                  </Badge>
                )}
              </div>
            </div>

            <div className="rounded-sm border border-oled-border bg-oled px-3 py-2">
              <div className="flex items-center justify-between font-mono text-[10px] text-oled-muted uppercase tracking-wider">
                <span>{tx("64-FRAME RT 데드라인", "64FR RT BUDGET")}</span>
                <Cpu className="size-3 text-signal-ok" />
              </div>
              <div className="mt-1 flex items-baseline gap-1.5">
                <span className="font-mono text-base font-bold tabular-nums text-oled-foreground">
                  {report ? (report.minBuffer64Pass ? "PASS" : "OVER") : "--"}
                </span>
                {report && (
                  <span className="font-mono text-[10px] text-oled-muted tabular-nums">
                    Peak {buf64MaxCpu.toFixed(1)}% CPU
                  </span>
                )}
              </div>
            </div>

            <div className="rounded-sm border border-oled-border bg-oled px-3 py-2">
              <div className="flex items-center justify-between font-mono text-[10px] text-oled-muted uppercase tracking-wider">
                <span>
                  {isAudioRunning
                    ? tx("실측 왕복 레이턴시", "LIVE ROUND-TRIP")
                    : tx("알고리즘 + 64FR RT", "ALGO + 64FR RT")}
                </span>
                <Activity className="size-3 text-signal-warn" />
              </div>
              <div className="mt-1 flex items-baseline gap-1.5">
                <span className="font-mono text-base font-bold tabular-nums text-oled-foreground">
                  {report
                    ? report.liveAudioRoundTripMs != null
                      ? `${report.liveAudioRoundTripMs.toFixed(1)} ms`
                      : `${(report.latencyItems[0]?.estimatedRoundTripMs ?? 0).toFixed(2)} ms`
                    : "--"}
                </span>
                {report && (
                  <span className="font-mono text-[10px] text-oled-muted tabular-nums">
                    @ {(report.sampleRate / 1000).toFixed(1)}kHz
                  </span>
                )}
              </div>
            </div>

            <div className="rounded-sm border border-oled-border bg-oled px-3 py-2">
              <div className="flex items-center justify-between font-mono text-[10px] text-oled-muted uppercase tracking-wider">
                <span>{tx("RT 메모리 할당 · 체인", "RT ALLOC · 16-SLOT")}</span>
                <ShieldCheck className="size-3 text-signal-ok" />
              </div>
              <div className="mt-1 flex items-baseline gap-1.5">
                <span className="font-mono text-base font-bold tabular-nums text-oled-foreground">
                  {report ? (report.zeroRtAllocAll ? "0 B" : "ALLOC!") : "--"}
                </span>
                {report && (
                  <span className="font-mono text-[10px] text-oled-muted tabular-nums">
                    {report.multiChainItems.filter((x) => x.passed).length}/{report.multiChainItems.length} · {report.totalDurationMs.toFixed(0)}ms
                  </span>
                )}
              </div>
            </div>
          </div>
        </DialogHeader>

        <div className="px-5 py-2.5 flex flex-wrap items-center justify-between gap-2 border-b border-border bg-card">
          <Tabs value={tab} onValueChange={(v) => setTab(v as BenchTab)}>
            <TabsList>
              <TabsTrigger value="similarity">
                {tx("하드웨어 유사도 검증", "HARDWARE SIMILARITY")}
              </TabsTrigger>
              <TabsTrigger value="latency">
                {tx("지연시간 · RT 데드라인", "LATENCY · RT BUDGET")}
              </TabsTrigger>
              <TabsTrigger value="multichain">
                {tx("16-슬롯 멀티체인 스트레스", "16-SLOT MULTI-CHAIN")}
              </TabsTrigger>
            </TabsList>
          </Tabs>

          {report && (
            <div className="flex items-center gap-1.5 font-mono text-[11px] font-bold">
              {report.overallPassed ? (
                <CheckCircle2 className="size-3.5 text-signal-ok" />
              ) : (
                <XCircle className="size-3.5 text-destructive" />
              )}
              <span className={report.overallPassed ? "text-signal-ok" : "text-destructive"}>
                {report.overallPassed
                  ? tx("전체 벤치마크 통과", "ALL BENCHMARKS PASSED")
                  : tx("일부 항목 실패", "SOME CHECKS FAILED")}
              </span>
            </div>
          )}
        </div>

        <ScrollArea className="h-[420px] px-5 py-3 bg-background">
          {errorMsg && (
            <div className="mb-3 rounded-sm border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-destructive">
              {errorMsg}
            </div>
          )}

          {!report && !running && (
            <div className="flex h-64 flex-col items-center justify-center gap-2 text-muted-foreground text-xs">
              <Layers className="size-8 opacity-40" />
              <span>
                {tx(
                  "'벤치마크 실행' 버튼을 눌러 DSP 및 SPICE 회로 정량 검증을 시작하세요.",
                  "Click 'RUN SUITE' to execute quantitative DSP & SPICE benchmarks."
                )}
              </span>
            </div>
          )}

          {running && !report && (
            <div className="flex h-64 flex-col items-center justify-center gap-2 text-primary font-mono text-xs">
              <Gauge className="size-7 animate-pulse" />
              <span>
                {tx(
                  "MNA SPICE · 4x HIIR · 64/128/256fr RT · 16슬롯 멀티체인 측정 중...",
                  "Benchmarking SPICE, 4x HIIR, RT budgets, and 16-slot torture chains..."
                )}
              </span>
            </div>
          )}

          {report && tab === "similarity" && (
            <div className="space-y-2.5 pb-2">
              {report.similarityItems.map((item) => (
                <div
                  key={item.id}
                  className="rounded-sm border border-border bg-card p-3 space-y-2 min-w-0 overflow-hidden"
                >
                  <div className="flex items-center justify-between gap-2 min-w-0">
                    <div className="flex items-center gap-2 min-w-0 flex-1">
                      <span className="font-display text-xs font-bold text-foreground truncate">
                        {item.originalHardware}
                      </span>
                      <span className="font-mono text-[10px] text-muted-foreground truncate">
                        ({item.deviceName})
                      </span>
                      <HelpHint content={item.topologySummary} />
                    </div>
                    <div className="flex items-center gap-2 shrink-0">
                      <span className="font-mono text-xs font-bold tabular-nums text-primary">
                        {item.similarityPct.toFixed(1)}%
                      </span>
                      <Badge variant={item.passed ? "ok" : "destructive"} size="xs">
                        {item.passed ? "PASS" : "FAIL"}
                      </Badge>
                    </div>
                  </div>

                  <div className="h-1.5 w-full rounded-[1px] bg-secondary overflow-hidden border border-border/50">
                    <div
                      className={`h-full transition-all ${
                        item.passed ? "bg-signal-ok" : "bg-destructive"
                      }`}
                      style={{ width: `${Math.min(100, Math.max(0, item.similarityPct))}%` }}
                    />
                  </div>

                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 text-[10px] font-mono">
                    <div className="rounded-sm border border-border bg-background px-2.5 py-1.5">
                      <div className="text-[9px] text-muted-foreground uppercase">{item.metricLabel}</div>
                      <div className="truncate text-foreground font-semibold">{item.measuredValue}</div>
                    </div>
                    <div className="rounded-sm border border-border bg-background px-2.5 py-1.5">
                      <div className="text-[9px] text-muted-foreground uppercase">Reference Target</div>
                      <div className="truncate text-foreground">{item.referenceTarget}</div>
                    </div>
                    <div className="rounded-sm border border-border bg-background px-2.5 py-1.5">
                      <div className="text-[9px] text-muted-foreground uppercase">Details / Margin</div>
                      <div className="truncate text-foreground">{item.deviationSummary}</div>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}

          {report && tab === "latency" && (
            <div className="rounded-sm border border-border bg-card overflow-hidden">
              <table className="w-full text-left border-collapse text-[11px]">
                <thead>
                  <tr className="border-b border-border bg-secondary/45 font-mono text-[10px] text-muted-foreground uppercase">
                    <th className="py-2 px-3">Profile</th>
                    <th className="py-2 px-2 text-right">Buffer</th>
                    <th className="py-2 px-2 text-right">Budget</th>
                    <th className="py-2 px-2 text-right">Avg / P99</th>
                    <th className="py-2 px-2 text-right">CPU %</th>
                    <th className="py-2 px-2 text-right">Algo / Est RT</th>
                    <th className="py-2 px-2 text-right">RT Alloc</th>
                    <th className="py-2 px-3 text-right">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border font-mono text-[10px]">
                  {report.latencyItems.map((row, idx) => (
                    <tr
                      key={`${row.profileName}-${row.bufferFrames}-${idx}`}
                      className="hover:bg-accent/50"
                    >
                      <td className="py-2 px-3 font-sans text-[11px] font-medium text-foreground max-w-[210px] truncate">
                        {row.profileName}
                      </td>
                      <td className="py-2 px-2 text-right tabular-nums text-foreground">
                        {row.bufferFrames} fr
                      </td>
                      <td className="py-2 px-2 text-right tabular-nums text-muted-foreground">
                        {row.budgetMs.toFixed(2)} ms
                      </td>
                      <td className="py-2 px-2 text-right tabular-nums text-primary font-semibold">
                        {row.avgBlockMs.toFixed(3)} / {row.p99BlockMs.toFixed(3)} ms
                      </td>
                      <td className="py-2 px-2 text-right tabular-nums text-foreground">
                        {row.cpuLoadPct.toFixed(1)}%
                      </td>
                      <td className="py-2 px-2 text-right tabular-nums text-muted-foreground">
                        +{row.algorithmicLatencyMs.toFixed(2)} / {row.estimatedRoundTripMs.toFixed(2)} ms
                      </td>
                      <td className="py-2 px-2 text-right tabular-nums text-signal-ok font-semibold">
                        {row.rtAllocBytes} B
                      </td>
                      <td className="py-2 px-3 text-right">
                        <Badge variant={row.passed ? "ok" : "destructive"} size="xs">
                          {row.passed ? "PASS" : "OVER"}
                        </Badge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {report && tab === "multichain" && (
            <div className="space-y-2.5 pb-2">
              {report.multiChainItems.map((item) => (
                <div
                  key={item.scenarioName}
                  className="rounded-sm border border-border bg-card p-3 space-y-2 min-w-0 overflow-hidden"
                >
                  <div className="flex items-center justify-between gap-2 min-w-0">
                    <div className="flex items-center gap-2 min-w-0 flex-1">
                      <span className="font-display text-xs font-bold text-foreground truncate">
                        {item.scenarioName}
                      </span>
                      <Badge variant="secondary" size="xs">
                        {item.activeBlocks} Blocks · {item.routingMode}
                      </Badge>
                      <HelpHint content={item.detail} />
                    </div>
                    <Badge variant={item.passed ? "ok" : "destructive"} size="xs" className="shrink-0">
                      {item.passed ? "PASS · 0 B RT ALLOC" : "FAIL"}
                    </Badge>
                  </div>

                  <div className="grid grid-cols-2 sm:grid-cols-5 gap-2 text-[10px] font-mono">
                    <div className="rounded-sm border border-border bg-background px-2.5 py-1.5">
                      <div className="text-[9px] text-muted-foreground uppercase">Avg Block (256fr)</div>
                      <div className="text-primary font-semibold tabular-nums">{item.avgBlockMs.toFixed(4)} ms</div>
                    </div>
                    <div className="rounded-sm border border-border bg-background px-2.5 py-1.5">
                      <div className="text-[9px] text-muted-foreground uppercase">Peak Ceiling</div>
                      <div className="text-foreground tabular-nums">{item.peakDbfs.toFixed(2)} dBFS</div>
                    </div>
                    <div className="rounded-sm border border-border bg-background px-2.5 py-1.5">
                      <div className="text-[9px] text-muted-foreground uppercase">Output RMS</div>
                      <div className="text-foreground tabular-nums">{item.rmsDbfs.toFixed(2)} dBFS</div>
                    </div>
                    <div className="rounded-sm border border-border bg-background px-2.5 py-1.5">
                      <div className="text-[9px] text-muted-foreground uppercase">Tail DC Offset</div>
                      <div className="text-foreground tabular-nums">{item.tailDcOffsetDbfs.toFixed(1)} dBFS</div>
                    </div>
                    <div className="rounded-sm border border-border bg-background px-2.5 py-1.5">
                      <div className="text-[9px] text-muted-foreground uppercase">NaN / Inf / RT</div>
                      <div className="text-signal-ok font-semibold tabular-nums">
                        {item.finiteSamplesOnly ? "Clean Finite" : "NaN!"} · {item.rtAllocBytes} B
                      </div>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </ScrollArea>
      </DialogContent>
    </Dialog>
  )
}
