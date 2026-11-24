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
import { Label } from "@/components/ui/label"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { studioBridge } from "@/lib/bridge"
import { useI18n } from "@/lib/i18n"
import type { AdminOverviewDto } from "@/types/rig"

interface AdminConsoleDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onReLogin?: () => void
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

type AdminTab = "analytics" | "bugs" | "ota"

export const AdminConsoleDialog: React.FC<AdminConsoleDialogProps> = ({
  open,
  onOpenChange,
  onReLogin,
  onLog,
}) => {
  const { tx } = useI18n()
  const [tab, setTab] = useState<AdminTab>("analytics")
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<AdminOverviewDto | null>(null)
  const [selectedBugId, setSelectedBugId] = useState<string | null>(null)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

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
      <DialogContent className="max-w-5xl p-0 overflow-hidden bg-card text-card-foreground border-border gap-0">
        <DialogHeader className="px-5 py-3.5 border-b border-border bg-secondary/45 shrink-0">
          <div className="flex items-center justify-between gap-3 pr-8">
            <div className="flex items-center gap-2.5">
              <div className="size-8 rounded-sm bg-oled border border-oled-border flex items-center justify-center text-signal-warn shrink-0">
                <Shield className="size-4" />
              </div>
              <div>
                <DialogTitle>
                  {tx("어드민 운영 애널리틱스 · 버그 분석 · OTA 정책 콘솔", "ADMIN ANALYTICS · BUG TRIAGE · OTA CONSOLE")}
                </DialogTitle>
                <DialogDescription>
                  {tx("관리자 Google 계정 인증 세션 전용", "Restricted to authenticated Admin Google accounts")}
                </DialogDescription>
              </div>
            </div>
            <Button size="xs" variant="keycap" onClick={() => void loadOverview()} disabled={loading}>
              <RefreshCw className={`size-3 ${loading ? "animate-spin" : ""}`} />
              <span>{tx("새로고침", "REFRESH")}</span>
            </Button>
          </div>
        </DialogHeader>

        <div className="px-5 py-2.5 border-b border-border bg-card">
          <Tabs value={tab} onValueChange={(v) => setTab(v as AdminTab)}>
            <TabsList>
              <TabsTrigger value="analytics">
                {tx("애널리틱스 대시보드", "SERVICE ANALYTICS")}
              </TabsTrigger>
              <TabsTrigger value="bugs">
                {tx(`버그 리포트 (${data?.kpis.openBugCount ?? 0} Open)`, `BUG REPORTS (${data?.kpis.openBugCount ?? 0})`)}
              </TabsTrigger>
              <TabsTrigger value="ota">
                {tx("OTA 선택/강제 업데이트 제어", "OTA RELEASE POLICY")}
              </TabsTrigger>
            </TabsList>
          </Tabs>
        </div>

        <ScrollArea className="h-[480px] px-5 py-4 bg-background">
          {errorMsg && (
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2 rounded-sm border border-destructive/40 bg-destructive/10 p-3 text-xs text-destructive">
              <span>{errorMsg}</span>
              {onReLogin && /만료|로그인|401|Unauthorized/i.test(errorMsg) && (
                <Button size="xs" variant="signal" onClick={onReLogin}>
                  {tx("다시 로그인", "SIGN IN AGAIN")}
                </Button>
              )}
            </div>
          )}

          {data && tab === "analytics" && (
            <div className="space-y-4">
              <div className="grid grid-cols-2 sm:grid-cols-6 gap-2.5">
                {[
                  { label: tx("총 사용자", "USERS"), val: data.kpis.totalUsers, icon: <Users className="size-3.5 text-primary" /> },
                  { label: tx("24H 활성", "24H ACTIVE"), val: data.kpis.active24hUsers, icon: <Activity className="size-3.5 text-signal-ok" /> },
                  { label: tx("동기화 설정", "SYNCED RIGS"), val: data.kpis.totalSyncedConfigs, icon: <RefreshCw className="size-3.5 text-primary" /> },
                  { label: tx("허브 기어", "HUB GEARS"), val: data.kpis.communityGearCount, icon: <Rocket className="size-3.5 text-signal-warn" /> },
                  { label: tx("허브 다운로드", "HUB DLS"), val: data.kpis.totalHubDownloads, icon: <Download className="size-3.5 text-signal-ok" /> },
                  { label: tx("미해결 버그", "OPEN BUGS"), val: data.kpis.openBugCount, icon: <Bug className="size-3.5 text-destructive" /> },
                ].map((k) => (
                  <div key={k.label} className="rounded-sm border border-oled-border bg-oled p-3">
                    <div className="flex items-center justify-between font-mono text-[10px] text-oled-muted">
                      <span>{k.label}</span>
                      {k.icon}
                    </div>
                    <div className="font-mono text-lg font-bold tabular-nums text-oled-foreground mt-1">
                      {k.val}
                    </div>
                  </div>
                ))}
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                <div className="rounded-sm border border-border bg-card p-3.5">
                  <div className="font-display text-xs font-bold text-foreground mb-2">
                    {tx("인기 커뮤니티 기어", "Top Community Gears")}
                  </div>
                  <div className="space-y-1.5 text-[11px]">
                    {data.topGears.length === 0 ? (
                      <div className="text-muted-foreground">{tx("등록된 허브 기어가 없습니다.", "No Hub items yet.")}</div>
                    ) : (
                      data.topGears.map((g, i) => (
                        <div key={i} className="flex items-center justify-between border-b border-border pb-1.5">
                          <div>
                            <span className="text-foreground font-semibold">{g.name}</span>
                            <span className="text-[10px] text-muted-foreground ml-2 font-mono">by {g.ownerName}</span>
                          </div>
                          <Badge variant="secondary" size="xs">{g.downloads} DLs</Badge>
                        </div>
                      ))
                    )}
                  </div>
                </div>

                <div className="rounded-sm border border-border bg-card p-3.5">
                  <div className="font-display text-xs font-bold text-foreground mb-2">
                    {tx("최근 텔레메트리 이벤트", "Recent Telemetry Events")}
                  </div>
                  <div className="rounded-sm border border-oled-border bg-oled p-2.5 space-y-1.5 text-[10px] font-mono">
                    {data.recentTelemetry.slice(0, 10).map((t, i) => (
                      <div key={i} className="flex items-center justify-between border-b border-oled-border pb-1 last:border-b-0 last:pb-0">
                        <span className="text-signal-ok">{t.eventType}</span>
                        <span className="text-oled-muted">v{t.appVersion} · {t.platform}</span>
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
                    className={`w-full text-left rounded-sm border p-2.5 transition-colors cursor-pointer ${
                      selectedBug?.id === b.id
                        ? "border-primary bg-secondary"
                        : "border-border bg-card hover:bg-accent"
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs font-semibold text-foreground truncate">{b.title}</span>
                      <Badge variant={b.status === "resolved" ? "ok" : b.status === "open" ? "hot" : "secondary"} size="xs" className="uppercase">
                        {b.status}
                      </Badge>
                    </div>
                    <div className="text-[10px] text-muted-foreground mt-1 flex items-center justify-between font-mono">
                      <span>{b.category} · {b.severity}</span>
                      <span>v{b.appVersion}</span>
                    </div>
                  </button>
                ))}
              </div>

              <div className="md:col-span-3 rounded-sm border border-border bg-card p-3.5 space-y-3">
                {selectedBug ? (
                  <>
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div>
                        <div className="font-display text-sm font-bold text-foreground">{selectedBug.title}</div>
                        <div className="text-[10px] font-mono text-muted-foreground">
                          {selectedBug.userEmail} · v{selectedBug.appVersion} · {selectedBug.createdAt}
                        </div>
                      </div>
                      <div className="flex items-center gap-1">
                        {(["open", "triaged", "resolved", "wontfix"] as const).map((st) => (
                          <Button
                            key={st}
                            size="xs"
                            variant={selectedBug.status === st ? "signal" : "keycap"}
                            onClick={() => void handleBugStatus(selectedBug.id, st)}
                            className="uppercase"
                          >
                            {st}
                          </Button>
                        ))}
                      </div>
                    </div>
                    <p className="text-xs text-foreground bg-background rounded-sm p-2.5 border border-border">
                      {selectedBug.description || "(No description)"}
                    </p>
                    <div>
                      <div className="text-[10px] font-mono text-muted-foreground uppercase mb-1">
                        SANITIZED LOGS & DIAGNOSTICS
                      </div>
                      <pre className="text-[10px] font-mono text-oled-foreground bg-oled border border-oled-border rounded-sm p-2.5 max-h-60 overflow-y-auto whitespace-pre-wrap">
                        {selectedBug.sanitizedLogs || "(No logs attached)"}
                      </pre>
                    </div>
                  </>
                ) : (
                  <div className="text-xs text-muted-foreground py-12 text-center">
                    {tx("제보된 버그가 없습니다.", "No bug reports found.")}
                  </div>
                )}
              </div>
            </div>
          )}

          {data && tab === "ota" && (
            <form onSubmit={handleSaveRelease} className="space-y-3 max-w-2xl">
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label>Latest Version</Label>
                  <Input value={relVersion} onChange={(e) => setRelVersion(e.target.value)} className="font-mono text-xs" />
                </div>
                <div className="space-y-1">
                  <Label>Min Supported Version (Force Floor)</Label>
                  <Input value={minVersion} onChange={(e) => setMinVersion(e.target.value)} className="font-mono text-xs" />
                </div>
              </div>

              <label className="flex items-center gap-2 rounded-sm border border-destructive/40 bg-destructive/10 p-3 text-xs cursor-pointer">
                <input
                  type="checkbox"
                  checked={forceUpdate}
                  onChange={(e) => setForceUpdate(e.target.checked)}
                />
                <span className="text-destructive font-semibold">
                  {tx("강제 업데이트 활성화 (실행 시 사용자가 업데이트를 건너뛸 수 없도록 차단)", "Enable Mandatory Force Update (Blocks app use until updated)")}
                </span>
              </label>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label>Setup Installer URL (.exe)</Label>
                  <Input value={installerUrl} onChange={(e) => setInstallerUrl(e.target.value)} placeholder="https://github.com/..." className="font-mono text-xs" />
                </div>
                <div className="space-y-1">
                  <Label>Standalone Single-File URL (.exe)</Label>
                  <Input value={standaloneUrl} onChange={(e) => setStandaloneUrl(e.target.value)} placeholder="https://github.com/..." className="font-mono text-xs" />
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label>Release Notes (KO)</Label>
                  <textarea
                    value={notesKo}
                    onChange={(e) => setNotesKo(e.target.value)}
                    rows={3}
                    className="w-full rounded-sm border border-input bg-background p-2 text-xs text-foreground outline-none focus-visible:border-primary"
                  />
                </div>
                <div className="space-y-1">
                  <Label>Release Notes (EN)</Label>
                  <textarea
                    value={notesEn}
                    onChange={(e) => setNotesEn(e.target.value)}
                    rows={3}
                    className="w-full rounded-sm border border-input bg-background p-2 text-xs text-foreground outline-none focus-visible:border-primary"
                  />
                </div>
              </div>

              <Button type="submit" size="sm" variant="signal" className="px-4">
                {tx("OTA 릴리즈 정책 게시", "PUBLISH OTA POLICY")}
              </Button>
            </form>
          )}
        </ScrollArea>
      </DialogContent>
    </Dialog>
  )
}
