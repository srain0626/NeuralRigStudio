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
      setErrorMsg(tx("네이티브 호스트(WebView2) 환경에서만 실시간 DSP 벤치마크를 실행할 수 있습니다.", "Live DSP benchmarks require the native desktop host."))
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
      <DialogContent className="max-w-4xl border-white/10 bg-[#101216] text-studio-ink p-0 overflow-hidden shadow-2xl">
        <DialogHeader className="px-5 pt-4 pb-3 border-b border-white/[0.06] bg-white/[0.02]">
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-2.5">
              <div className="flex h-8 w-8 items-center justify-center rounded-md border border-cyan-400/30 bg-cyan-500/10 text-cyan-300">
                <Gauge className="size-4" strokeWidth={1.7} />
              </div>
              <div>
                <DialogTitle className="text-[14px] font-semibold tracking-tight text-white">
                  {tx("하드웨어 유사도 · 지연시간 · 멀티체인 벤치마크", "Hardware Similarity · Latency · Multi-Chain Benchmark")}
                </DialogTitle>
                <DialogDescription className="text-[11px] text-studio-muted">
                  {tx(
                    "MNA SPICE 물리 회로 해석 · 4x HIIR 대칭/비대칭 클리핑 · WaveNet · 64/128/256fr RT 데드라인 · 16슬롯 스트레스 검증",
                    "MNA SPICE nodal checks · 4x HIIR harmonic verification · WaveNet ESR · 64/128/256fr RT deadlines · 16-slot torture suite"
                  )}
                </DialogDescription>
              </div>
            </div>

            <Button
              size="sm"
              variant="default"
              disabled={running}
              onClick={() => void handleRun()}
              className="h-8 gap-1.5 bg-cyan-600 hover:bg-cyan-500 text-white text-[11px] font-medium px-3"
            >
              <Play className="size-3.5" />
              <span>{running ? tx("계측 중...", "Running...") : report ? tx("벤치마크 다시 실행", "Re-Run Suite") : tx("벤치마크 실행", "Run Suite")}</span>
            </Button>
          </div>

          {/* KPI Summary Cards */}
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 pt-3">
            <div className="rounded-md border border-white/[0.07] bg-black/30 px-3 py-2">
              <div className="flex items-center justify-between text-[10px] text-studio-muted uppercase tracking-wider">
                <span>{tx("하드웨어 유사도", "Hardware Fidelity")}</span>
                <Waves className="size-3 text-cyan-400" />
              </div>
              <div className="mt-1 flex items-baseline gap-1.5">
                <span className="text-base font-semibold tabular-nums text-white">
                  {report ? `${report.overallSimilarityPct.toFixed(1)}%` : "--"}
                </span>
                {report && (
                  <Badge variant="outline" className="text-[9px] h-4 px-1 border-cyan-500/30 text-cyan-300">
                    {report.similarityItems.filter((x) => x.passed).length}/{report.similarityItems.length}
                  </Badge>
                )}
              </div>
            </div>

            <div className="rounded-md border border-white/[0.07] bg-black/30 px-3 py-2">
              <div className="flex items-center justify-between text-[10px] text-studio-muted uppercase tracking-wider">
                <span>{tx("64-Frame RT 데드라인", "64fr RT Budget")}</span>
                <Cpu className="size-3 text-emerald-400" />
              </div>
              <div className="mt-1 flex items-baseline gap-1.5">
                <span className="text-base font-semibold tabular-nums text-white">
                  {report ? (report.minBuffer64Pass ? "PASS" : "OVER") : "--"}
                </span>
                {report && (
                  <span className="text-[10px] text-studio-muted tabular-nums">
                    Peak {buf64MaxCpu.toFixed(1)}% CPU
                  </span>
                )}
              </div>
            </div>

            <div className="rounded-md border border-white/[0.07] bg-black/30 px-3 py-2">
              <div className="flex items-center justify-between text-[10px] text-studio-muted uppercase tracking-wider">
                <span>{isAudioRunning ? tx("실측 왕복 레이턴시", "Live Round-Trip") : tx("알고리즘 + 64fr RT", "Algo + 64fr RT")}</span>
                <Activity className="size-3 text-amber-400" />
              </div>
              <div className="mt-1 flex items-baseline gap-1.5">
                <span className="text-base font-semibold tabular-nums text-white">
                  {report
                    ? report.liveAudioRoundTripMs != null
                      ? `${report.liveAudioRoundTripMs.toFixed(1)} ms`
                      : `${(report.latencyItems[0]?.estimatedRoundTripMs ?? 0).toFixed(2)} ms`
                    : "--"}
                </span>
                {report && (
                  <span className="text-[10px] text-studio-muted tabular-nums">
                    @ {(report.sampleRate / 1000).toFixed(1)}kHz
                  </span>
                )}
              </div>
            </div>

            <div className="rounded-md border border-white/[0.07] bg-black/30 px-3 py-2">
              <div className="flex items-center justify-between text-[10px] text-studio-muted uppercase tracking-wider">
                <span>{tx("RT 메모리 할당 · 체인", "RT Alloc · 16-Slot")}</span>
                <ShieldCheck className="size-3 text-emerald-400" />
              </div>
              <div className="mt-1 flex items-baseline gap-1.5">
                <span className="text-base font-semibold tabular-nums text-white">
                  {report ? (report.zeroRtAllocAll ? "0 B" : "ALLOC!") : "--"}
                </span>
                {report && (
                  <span className="text-[10px] text-studio-muted tabular-nums">
                    {report.multiChainItems.filter((x) => x.passed).length}/{report.multiChainItems.length} Chains · {report.totalDurationMs.toFixed(0)}ms
                  </span>
                )}
              </div>
            </div>
          </div>
        </DialogHeader>

        <div className="px-5 py-3 flex items-center justify-between border-b border-white/[0.05] bg-black/20">
          <Tabs value={tab} onValueChange={(v) => setTab(v as BenchTab)}>
            <TabsList className="h-8 bg-black/40 border border-white/[0.06] p-0.5">
              <TabsTrigger value="similarity" className="text-[11px] h-7 px-3">
                {tx("하드웨어 유사도 검증", "Hardware Similarity")}
              </TabsTrigger>
              <TabsTrigger value="latency" className="text-[11px] h-7 px-3">
                {tx("지연시간 · RT 데드라인", "Latency · RT Budget")}
              </TabsTrigger>
              <TabsTrigger value="multichain" className="text-[11px] h-7 px-3">
                {tx("16-슬롯 멀티체인 스트레스", "16-Slot Multi-Chain")}
              </TabsTrigger>
            </TabsList>
          </Tabs>

          {report && (
            <div className="flex items-center gap-1.5 text-[11px]">
              {report.overallPassed ? (
                <CheckCircle2 className="size-3.5 text-emerald-400" />
              ) : (
                <XCircle className="size-3.5 text-rose-400" />
              )}
              <span className={report.overallPassed ? "text-emerald-300 font-medium" : "text-rose-300 font-medium"}>
                {report.overallPassed ? tx("전체 벤치마크 통과", "ALL BENCHMARKS PASSED") : tx("일부 항목 실패", "SOME CHECKS FAILED")}
              </span>
            </div>
          )}
        </div>

        <ScrollArea className="h-[420px] px-5 py-3">
          {errorMsg && (
            <div className="mb-3 rounded-md border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-[11px] text-rose-200">
              {errorMsg}
            </div>
          )}

          {!report && !running && (
            <div className="flex h-64 flex-col items-center justify-center gap-2 text-studio-muted text-xs">
              <Layers className="size-8 opacity-40" />
              <span>{tx("'벤치마크 실행' 버튼을 눌러 DSP 및 SPICE 회로 정량 검증을 시작하세요.", "Click 'Run Suite' to execute quantitative DSP & SPICE benchmarks.")}</span>
            </div>
          )}

          {running && !report && (
            <div className="flex h-64 flex-col items-center justify-center gap-2 text-cyan-300 text-xs">
              <Gauge className="size-7 animate-pulse" />
              <span>{tx("MNA SPICE · 4x HIIR · 64/128/256fr RT · 16슬롯 멀티체인 측정 중...", "Benchmarking SPICE, 4x HIIR, RT budgets, and 16-slot torture chains...")}</span>
            </div>
          )}

          {report && tab === "similarity" && (
            <div className="space-y-2.5 pb-2">
              {report.similarityItems.map((item) => (
                <div
                  key={item.id}
                  className="rounded-md border border-white/[0.07] bg-white/[0.02] p-3"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-semibold text-white">{item.originalHardware}</span>
                        <span className="text-[10px] text-studio-muted">({item.deviceName})</span>
                      </div>
                      <div className="text-[10px] text-studio-muted mt-0.5">{item.topologySummary}</div>
                    </div>
                    <div className="flex items-center gap-2">
                      <span className="text-xs font-semibold tabular-nums text-cyan-300">
                        {item.similarityPct.toFixed(1)}%
                      </span>
                      <Badge
                        variant="outline"
                        className={
                          item.passed
                            ? "text-[9px] h-5 border-emerald-500/30 bg-emerald-500/10 text-emerald-300"
                            : "text-[9px] h-5 border-rose-500/30 bg-rose-500/10 text-rose-300"
                        }
                      >
                        {item.passed ? "PASS" : "FAIL"}
                      </Badge>
                    </div>
                  </div>

                  <div className="mt-2 h-1.5 w-full rounded-full bg-black/50 overflow-hidden">
                    <div
                      className={`h-full rounded-full transition-all ${item.passed ? "bg-gradient-to-r from-cyan-500 to-emerald-400" : "bg-rose-500"}`}
                      style={{ width: `${Math.min(100, Math.max(0, item.similarityPct))}%` }}
                    />
                  </div>

                  <div className="mt-2 grid grid-cols-1 sm:grid-cols-3 gap-2 text-[10px] font-mono">
                    <div className="rounded bg-black/30 px-2 py-1 text-studio-ink">
                      <div className="text-[9px] text-studio-muted">{item.metricLabel}</div>
                      <div className="truncate">{item.measuredValue}</div>
                    </div>
                    <div className="rounded bg-black/30 px-2 py-1 text-studio-muted">
                      <div className="text-[9px]">Reference Target</div>
                      <div className="truncate text-white/80">{item.referenceTarget}</div>
                    </div>
                    <div className="rounded bg-black/30 px-2 py-1 text-studio-muted">
                      <div className="text-[9px]">Details / Margin</div>
                      <div className="truncate text-white/80">{item.deviationSummary}</div>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}

          {report && tab === "latency" && (
            <div className="rounded-md border border-white/[0.07] bg-white/[0.02] overflow-hidden">
              <table className="w-full text-left border-collapse text-[11px]">
                <thead>
                  <tr className="border-b border-white/[0.06] bg-black/40 text-[10px] text-studio-muted uppercase">
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
                <tbody className="divide-y divide-white/[0.05] font-mono text-[10px]">
                  {report.latencyItems.map((row, idx) => (
                    <tr key={`${row.profileName}-${row.bufferFrames}-${idx}`} className="hover:bg-white/[0.02]">
                      <td className="py-2 px-3 font-sans text-[11px] text-white">{row.profileName}</td>
                      <td className="py-2 px-2 text-right tabular-nums">{row.bufferFrames} fr</td>
                      <td className="py-2 px-2 text-right tabular-nums text-studio-muted">{row.budgetMs.toFixed(2)} ms</td>
                      <td className="py-2 px-2 text-right tabular-nums text-cyan-200">
                        {row.avgBlockMs.toFixed(3)} / {row.p99BlockMs.toFixed(3)} ms
                      </td>
                      <td className="py-2 px-2 text-right tabular-nums">{row.cpuLoadPct.toFixed(1)}%</td>
                      <td className="py-2 px-2 text-right tabular-nums text-studio-muted">
                        +{row.algorithmicLatencyMs.toFixed(2)} / {row.estimatedRoundTripMs.toFixed(2)} ms
                      </td>
                      <td className="py-2 px-2 text-right tabular-nums text-emerald-300">{row.rtAllocBytes} B</td>
                      <td className="py-2 px-3 text-right">
                        <Badge
                          variant="outline"
                          className={
                            row.passed
                              ? "text-[9px] h-4 border-emerald-500/30 text-emerald-300"
                              : "text-[9px] h-4 border-rose-500/30 text-rose-300"
                          }
                        >
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
                  className="rounded-md border border-white/[0.07] bg-white/[0.02] p-3"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-semibold text-white">{item.scenarioName}</span>
                        <Badge variant="outline" className="text-[9px] h-4 border-white/15 text-studio-muted">
                          {item.activeBlocks} Blocks · {item.routingMode}
                        </Badge>
                      </div>
                      <div className="text-[10px] text-studio-muted mt-0.5">{item.detail}</div>
                    </div>
                    <Badge
                      variant="outline"
                      className={
                        item.passed
                          ? "text-[9px] h-5 border-emerald-500/30 bg-emerald-500/10 text-emerald-300"
                          : "text-[9px] h-5 border-rose-500/30 bg-rose-500/10 text-rose-300"
                      }
                    >
                      {item.passed ? "PASS · 0 B RT ALLOC" : "FAIL"}
                    </Badge>
                  </div>

                  <div className="mt-2 grid grid-cols-2 sm:grid-cols-5 gap-2 text-[10px] font-mono">
                    <div className="rounded bg-black/30 px-2 py-1">
                      <div className="text-[9px] text-studio-muted">Avg Block (256fr)</div>
                      <div className="text-cyan-200">{item.avgBlockMs.toFixed(4)} ms</div>
                    </div>
                    <div className="rounded bg-black/30 px-2 py-1">
                      <div className="text-[9px] text-studio-muted">Peak Ceiling</div>
                      <div className="text-white">{item.peakDbfs.toFixed(2)} dBFS</div>
                    </div>
                    <div className="rounded bg-black/30 px-2 py-1">
                      <div className="text-[9px] text-studio-muted">Output RMS</div>
                      <div className="text-white">{item.rmsDbfs.toFixed(2)} dBFS</div>
                    </div>
                    <div className="rounded bg-black/30 px-2 py-1">
                      <div className="text-[9px] text-studio-muted">Tail DC Offset</div>
                      <div className="text-white">{item.tailDcOffsetDbfs.toFixed(1)} dBFS</div>
                    </div>
                    <div className="rounded bg-black/30 px-2 py-1">
                      <div className="text-[9px] text-studio-muted">NaN / Inf / RT</div>
                      <div className="text-emerald-300">
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
