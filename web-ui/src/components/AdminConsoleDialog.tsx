import React, { useEffect, useState } from "react"
import {
  Activity,
  Bug,
  Download,
  RefreshCw,
  Rocket,
  Shield,
  Users,
} from "lucide-react"
import { toast } from "sonner"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { studioBridge } from "@/lib/bridge"
import { useI18n } from "@/lib/i18n"
import type { AdminOverviewDto } from "@/types/rig"

interface AdminConsoleDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

type AdminTab = "analytics" | "bugs" | "ota"

export const AdminConsoleDialog: React.FC<AdminConsoleDialogProps> = ({
  open,
  onOpenChange,
  onLog,
}) => {
  const { tx } = useI18n()
  const [tab, setTab] = useState<AdminTab>("analytics")
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<AdminOverviewDto | null>(null)
  const [selectedBugId, setSelectedBugId] = useState<string | null>(null)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  // OTA policy form fields
  const [relVersion, setRelVersion] = useState("3.0.0")
  const [minVersion, setMinVersion] = useState("3.0.0")
  const [forceUpdate, setForceUpdate] = useState(false)
  const [notesKo, setNotesKo] = useState("")
  const [notesEn, setNotesEn] = useState("")
  const [installerUrl, setInstallerUrl] = useState("")
  const [standaloneUrl, setStandaloneUrl] = useState("")

  const loadOverview = async () => {
    if (!studioBridge.isWebView2) return
    setLoading(true)
    setErrorMsg(null)
    try {
      const res = await studioBridge.invoke<AdminOverviewDto>("getAdminOverview", {})
      setData(res)
      if (res.bugs?.length && !selectedBugId) setSelectedBugId(res.bugs[0].id)
      if (res.releasePolicy) {
        setRelVersion(res.releasePolicy.version || "3.0.0")
        setMinVersion(res.releasePolicy.minSupportedVersion || "3.0.0")
        setForceUpdate(Boolean(res.releasePolicy.forceUpdate))
        setNotesKo(res.releasePolicy.releaseNotesKo || "")
        setNotesEn(res.releasePolicy.releaseNotesEn || "")
        setInstallerUrl(res.releasePolicy.installerUrl || "")
        setStandaloneUrl(res.releasePolicy.standaloneExeUrl || "")
      }
    } catch (e) {
      setErrorMsg(String(e))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (open) void loadOverview()
  }, [open])

  const handleBugStatus = async (id: string, status: string) => {
    if (!studioBridge.isWebView2) return
    try {
      await studioBridge.invoke("adminUpdateBugStatus", { id, status })
      toast.success(tx(`버그 상태 변경: ${status}`, `Bug status -> ${status}`))
      await loadOverview()
    } catch (e) {
      toast.error(String(e))
    }
  }

  const handleSaveRelease = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!studioBridge.isWebView2) return
    try {
      await studioBridge.invoke("adminUpdateReleasePolicy", {
        version: relVersion,
        minSupportedVersion: minVersion,
        forceUpdate,
        releaseNotesKo: notesKo,
        releaseNotesEn: notesEn,
        installerUrl,
        standaloneExeUrl: standaloneUrl,
      })
      toast.success(tx(`OTA 정책 저장됨 (v${relVersion}, 강제=${forceUpdate})`, `Saved OTA policy v${relVersion}`))
      onLog(`[Admin] Published OTA policy v${relVersion} (force=${forceUpdate})`, "success")
      await loadOverview()
    } catch (err) {
      toast.error(String(err))
    }
  }

  const selectedBug = data?.bugs.find((b) => b.id === selectedBugId) || data?.bugs[0] || null

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-5xl border-amber-500/20 bg-[#101318] text-studio-ink p-0 overflow-hidden shadow-2xl">
        <DialogHeader className="px-5 pt-4 pb-3 border-b border-white/[0.06] bg-amber-500/[0.06]">
          <div className="flex items-center justify-between gap-3 pr-6">
            <div className="flex items-center gap-2.5">
              <div className="flex h-8 w-8 items-center justify-center rounded-md border border-amber-500/40 bg-amber-500/15 text-amber-300">
                <Shield className="size-4" />
              </div>
              <div>
                <DialogTitle className="text-[14px] font-semibold text-white">
                  {tx("어드민 운영 애널리틱스 · 버그 분석 · OTA 정책 콘솔", "Admin Analytics · Bug Log Triage · OTA Policy Console")}
                </DialogTitle>
                <DialogDescription className="text-[11px] text-studio-muted">
                  {tx("관리자 Google 계정 인증 세션 전용", "Restricted to authenticated Admin Google accounts")}
                </DialogDescription>
              </div>
            </div>
            <Button size="xs" variant="outline" onClick={() => void loadOverview()} disabled={loading} className="gap-1.5">
              <RefreshCw className={`size-3 ${loading ? "animate-spin" : ""}`} />
              <span>{tx("새로고침", "Refresh")}</span>
            </Button>
          </div>
        </DialogHeader>

        <div className="px-5 py-2.5 border-b border-white/[0.05] bg-black/20">
          <Tabs value={tab} onValueChange={(v) => setTab(v as AdminTab)}>
            <TabsList className="h-8 bg-black/40 border border-white/[0.06] p-0.5">
              <TabsTrigger value="analytics" className="text-[11px] h-7 px-3">
                {tx("애널리틱스 대시보드", "Service Analytics")}
              </TabsTrigger>
              <TabsTrigger value="bugs" className="text-[11px] h-7 px-3">
                {tx(`버그 리포트 (${data?.kpis.openBugCount ?? 0} Open)`, `Bug Reports (${data?.kpis.openBugCount ?? 0} Open)`)}
              </TabsTrigger>
              <TabsTrigger value="ota" className="text-[11px] h-7 px-3">
                {tx("OTA 선택/강제 업데이트 제어", "OTA Release & Force Policy")}
              </TabsTrigger>
            </TabsList>
          </Tabs>
        </div>

        <ScrollArea className="h-[480px] px-5 py-4">
          {errorMsg && (
            <div className="mb-3 rounded-md border border-rose-500/30 bg-rose-500/10 p-3 text-xs text-rose-200">
              {errorMsg}
            </div>
          )}

          {data && tab === "analytics" && (
            <div className="space-y-4">
              <div className="grid grid-cols-2 sm:grid-cols-6 gap-2.5">
                {[
                  { label: tx("총 사용자", "Users"), val: data.kpis.totalUsers, icon: <Users className="size-3.5 text-cyan-400" /> },
                  { label: tx("24h 활성", "24h Active"), val: data.kpis.active24hUsers, icon: <Activity className="size-3.5 text-emerald-400" /> },
                  { label: tx("동기화 설정", "Synced Rigs"), val: data.kpis.totalSyncedConfigs, icon: <RefreshCw className="size-3.5 text-violet-400" /> },
                  { label: tx("허브 기어", "Hub Gears"), val: data.kpis.communityGearCount, icon: <Rocket className="size-3.5 text-amber-400" /> },
                  { label: tx("허브 다운로드", "Hub DLs"), val: data.kpis.totalHubDownloads, icon: <Download className="size-3.5 text-cyan-400" /> },
                  { label: tx("미해결 버그", "Open Bugs"), val: data.kpis.openBugCount, icon: <Bug className="size-3.5 text-rose-400" /> },
                ].map((k) => (
                  <div key={k.label} className="rounded-md border border-white/[0.07] bg-black/30 p-3">
                    <div className="flex items-center justify-between text-[10px] text-studio-muted">
                      <span>{k.label}</span>
                      {k.icon}
                    </div>
                    <div className="text-lg font-semibold tabular-nums text-white mt-1">{k.val}</div>
                  </div>
                ))}
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                <div className="rounded-md border border-white/[0.07] bg-white/[0.02] p-3">
                  <div className="text-xs font-semibold text-white mb-2">{tx("인기 커뮤니티 기어", "Top Community Gears")}</div>
                  <div className="space-y-1.5 text-[11px]">
                    {data.topGears.length === 0 ? (
                      <div className="text-studio-muted">{tx("등록된 허브 기어가 없습니다.", "No Hub items yet.")}</div>
                    ) : (
                      data.topGears.map((g, i) => (
                        <div key={i} className="flex items-center justify-between border-b border-white/[0.05] pb-1">
                          <div>
                            <span className="text-white font-medium">{g.name}</span>
                            <span className="text-[10px] text-studio-muted ml-2">by {g.ownerName}</span>
                          </div>
                          <Badge variant="outline" className="text-[9px]">{g.downloads} DLs</Badge>
                        </div>
                      ))
                    )}
                  </div>
                </div>

                <div className="rounded-md border border-white/[0.07] bg-white/[0.02] p-3">
                  <div className="text-xs font-semibold text-white mb-2">{tx("최근 텔레메트리 이벤트", "Recent Telemetry Events")}</div>
                  <div className="space-y-1.5 text-[10px] font-mono">
                    {data.recentTelemetry.slice(0, 10).map((t, i) => (
                      <div key={i} className="flex items-center justify-between border-b border-white/[0.04] pb-1">
                        <span className="text-cyan-300">{t.eventType}</span>
                        <span className="text-studio-muted">v{t.appVersion} · {t.platform}</span>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            </div>
          )}

          {data && tab === "bugs" && (
            <div className="grid grid-cols-1 md:grid-cols-5 gap-3">
              <div className="md:col-span-2 space-y-2">
                {data.bugs.map((b) => (
                  <button
                    key={b.id}
                    type="button"
                    onClick={() => setSelectedBugId(b.id)}
                    className={`w-full text-left rounded-md border p-2.5 transition-colors ${
                      selectedBug?.id === b.id
                        ? "border-cyan-500/50 bg-cyan-500/10"
                        : "border-white/[0.07] bg-white/[0.02] hover:bg-white/[0.04]"
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs font-semibold text-white truncate">{b.title}</span>
                      <Badge variant="outline" className="text-[9px] h-4 uppercase">{b.status}</Badge>
                    </div>
                    <div className="text-[10px] text-studio-muted mt-1 flex items-center justify-between font-mono">
                      <span>{b.category} · {b.severity}</span>
                      <span>v{b.appVersion}</span>
                    </div>
                  </button>
                ))}
              </div>

              <div className="md:col-span-3 rounded-md border border-white/[0.07] bg-black/30 p-3.5 space-y-3">
                {selectedBug ? (
                  <>
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div>
                        <div className="text-sm font-semibold text-white">{selectedBug.title}</div>
                        <div className="text-[10px] font-mono text-studio-muted">
                          {selectedBug.userEmail} · v{selectedBug.appVersion} · {selectedBug.createdAt}
                        </div>
                      </div>
                      <div className="flex items-center gap-1">
                        {(["open", "triaged", "resolved", "wontfix"] as const).map((st) => (
                          <Button
                            key={st}
                            size="xs"
                            variant={selectedBug.status === st ? "default" : "outline"}
                            onClick={() => void handleBugStatus(selectedBug.id, st)}
                            className="text-[10px] h-6 px-2 uppercase"
                          >
                            {st}
                          </Button>
                        ))}
                      </div>
                    </div>
                    <p className="text-xs text-white/90 bg-black/40 rounded p-2.5 border border-white/[0.05]">
                      {selectedBug.description || "(No description)"}
                    </p>
                    <div>
                      <div className="text-[10px] font-mono text-studio-muted mb-1">SANITIZED LOGS & DIAGNOSTICS</div>
                      <pre className="text-[10px] font-mono text-emerald-200 bg-black/60 rounded p-2.5 max-h-60 overflow-y-auto whitespace-pre-wrap">
                        {selectedBug.sanitizedLogs || "(No logs attached)"}
                      </pre>
                    </div>
                  </>
                ) : (
                  <div className="text-xs text-studio-muted py-12 text-center">
                    {tx("제보된 버그가 없습니다.", "No bug reports found.")}
                  </div>
                )}
              </div>
            </div>
          )}

          {data && tab === "ota" && (
            <form onSubmit={handleSaveRelease} className="space-y-3 max-w-2xl">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-[11px] font-medium text-white">Latest Version</label>
                  <Input value={relVersion} onChange={(e) => setRelVersion(e.target.value)} className="mt-1 bg-black/40 font-mono text-xs" />
                </div>
                <div>
                  <label className="text-[11px] font-medium text-white">Min Supported Version (Force Floor)</label>
                  <Input value={minVersion} onChange={(e) => setMinVersion(e.target.value)} className="mt-1 bg-black/40 font-mono text-xs" />
                </div>
              </div>

              <label className="flex items-center gap-2 rounded-md border border-rose-500/30 bg-rose-500/[0.06] p-3 text-xs cursor-pointer">
                <input
                  type="checkbox"
                  checked={forceUpdate}
                  onChange={(e) => setForceUpdate(e.target.checked)}
                />
                <span className="text-rose-200 font-medium">
                  {tx("강제 업데이트 활성화 (실행 시 사용자가 업데이트를 건너뛸 수 없도록 차단)", "Enable Mandatory Force Update (Blocks app use until updated)")}
                </span>
              </label>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div>
                  <label className="text-[11px] font-medium text-white">Setup Installer URL (.exe)</label>
                  <Input value={installerUrl} onChange={(e) => setInstallerUrl(e.target.value)} placeholder="https://github.com/..." className="mt-1 bg-black/40 font-mono text-xs" />
                </div>
                <div>
                  <label className="text-[11px] font-medium text-white">Standalone Single-File URL (.exe)</label>
                  <Input value={standaloneUrl} onChange={(e) => setStandaloneUrl(e.target.value)} placeholder="https://github.com/..." className="mt-1 bg-black/40 font-mono text-xs" />
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div>
                  <label className="text-[11px] font-medium text-white">Release Notes (KO)</label>
                  <textarea value={notesKo} onChange={(e) => setNotesKo(e.target.value)} rows={3} className="mt-1 w-full rounded-md border border-white/10 bg-black/40 p-2 text-xs text-white" />
                </div>
                <div>
                  <label className="text-[11px] font-medium text-white">Release Notes (EN)</label>
                  <textarea value={notesEn} onChange={(e) => setNotesEn(e.target.value)} rows={3} className="mt-1 w-full rounded-md border border-white/10 bg-black/40 p-2 text-xs text-white" />
                </div>
              </div>

              <Button type="submit" size="sm" className="bg-amber-600 hover:bg-amber-500 text-white text-xs px-4">
                {tx("OTA 릴리즈 정책 게시", "Publish OTA Release Policy")}
              </Button>
            </form>
          )}
        </ScrollArea>
      </DialogContent>
    </Dialog>
  )
}
