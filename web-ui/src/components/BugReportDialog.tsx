import React, { useEffect, useState } from "react"
import {
  Bug,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Send,
  ShieldCheck,
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
import { studioBridge } from "@/lib/bridge"
import { useI18n } from "@/lib/i18n"
import type { CloudStateDto } from "@/types/rig"

interface BugReportDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  appVersion: string
  cloudState: CloudStateDto
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

export const BugReportDialog: React.FC<BugReportDialogProps> = ({
  open,
  onOpenChange,
  appVersion,
  cloudState,
  onLog,
}) => {
  const { tx } = useI18n()
  const [title, setTitle] = useState("")
  const [category, setCategory] = useState("audio")
  const [severity, setSeverity] = useState("normal")
  const [description, setDescription] = useState("")
  const [logsPreview, setLogsPreview] = useState("")
  const [showLogs, setShowLogs] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [resultMsg, setResultMsg] = useState<string | null>(null)

  useEffect(() => {
    if (open && studioBridge.isWebView2) {
      setResultMsg(null)
      studioBridge
        .invoke<{ logs: string; pendingCount: number }>("getSanitizedLogsPreview")
        .then((res) => setLogsPreview(res?.logs || ""))
        .catch(() => {})
    }
  }, [open])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!title.trim()) {
      toast.error(tx("버그 제목을 입력해 주세요.", "Please enter a bug title."))
      return
    }
    if (!studioBridge.isWebView2) return

    setSubmitting(true)
    setResultMsg(null)
    try {
      const res = await studioBridge.invoke<{
        ok: boolean
        uploaded: boolean
        queuedOffline: boolean
        message: string
        reportId: string
      }>("submitBugReport", {
        title: title.trim(),
        category,
        severity,
        description: description.trim(),
      })
      setResultMsg(res.message)
      onLog(`[BugReport #${res.reportId.slice(0, 6)}] ${res.message}`, "success")
      toast.success(tx("버그 리포트 접수됨", "Bug report submitted"))
      setTitle("")
      setDescription("")
    } catch (err) {
      const msg = String(err)
      onLog(msg, "error")
      toast.error(msg)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-xl p-0 overflow-hidden bg-card text-card-foreground border-border gap-0">
        <DialogHeader className="px-5 py-3.5 border-b border-border bg-secondary/45 shrink-0">
          <div className="flex items-center justify-between gap-2 pr-8">
            <div className="flex items-center gap-2.5">
              <div className="size-8 rounded-sm bg-oled border border-oled-border flex items-center justify-center text-signal-hot shrink-0">
                <Bug className="size-4" />
              </div>
              <div>
                <DialogTitle>
                  {tx("버그 제보 · 자동 익명화 로그 전송", "BUG REPORT · SCRUBBED LOG TELEMETRY")}
                </DialogTitle>
                <DialogDescription>
                  {tx(
                    `v${appVersion} · 최근 로그와 오디오 진단은 LogSanitizer로 개인정보(경로·사용자명·토큰) 마스킹 후 전송됩니다.`,
                    `v${appVersion} · Recent logs & audio diagnostics are automatically scrubbed via LogSanitizer before upload.`
                  )}
                </DialogDescription>
              </div>
            </div>
            {cloudState.pendingBugReportsCount > 0 && (
              <Badge variant="warn" size="xs">
                {cloudState.pendingBugReportsCount} Queued
              </Badge>
            )}
          </div>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="px-5 py-4 space-y-3.5 bg-background">
          {resultMsg && (
            <div className="rounded-sm border border-signal-ok/40 bg-signal-ok/10 p-3 text-xs text-signal-ok flex items-center gap-2 font-medium">
              <CheckCircle2 className="size-4 shrink-0" />
              <span>{resultMsg}</span>
            </div>
          )}

          <div className="space-y-1.5">
            <Label>{tx("문제 요약", "ISSUE SUMMARY")}</Label>
            <Input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder={tx("예: 64-frame ASIO 시작 시 출력 크랙클 발생", "e.g., Crackle on 64-frame ASIO startup")}
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>{tx("카테고리", "CATEGORY")}</Label>
              <select
                value={category}
                onChange={(e) => setCategory(e.target.value)}
                className="w-full h-8 rounded-sm border border-input bg-background px-2.5 text-xs text-foreground outline-none focus-visible:border-primary"
              >
                <option value="audio">Audio I/O (ASIO/WASAPI)</option>
                <option value="spice">SPICE / DSP Engine</option>
                <option value="ui">UI / Signal Chain</option>
                <option value="hub">Cloud Sync / Gear Hub</option>
                <option value="ota">OTA / Installer</option>
                <option value="crash">Crash / Hang</option>
              </select>
            </div>
            <div className="space-y-1.5">
              <Label>{tx("심각도", "SEVERITY")}</Label>
              <select
                value={severity}
                onChange={(e) => setSeverity(e.target.value)}
                className="w-full h-8 rounded-sm border border-input bg-background px-2.5 text-xs text-foreground outline-none focus-visible:border-primary"
              >
                <option value="low">Low</option>
                <option value="normal">Normal</option>
                <option value="high">High</option>
                <option value="critical">Critical</option>
              </select>
            </div>
          </div>

          <div className="space-y-1.5">
            <Label>{tx("재현 방법 및 상세 설명", "REPRODUCTION STEPS & DETAILS")}</Label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={4}
              placeholder={tx(
                "어떤 오디오 장치/프리셋/동작에서 문제가 발생했는지 적어주세요.",
                "Describe the audio interface, preset, or actions leading to the issue."
              )}
              className="w-full rounded-sm border border-input bg-background p-2.5 text-xs text-foreground placeholder:text-muted-foreground outline-none focus-visible:border-primary"
            />
          </div>

          <div className="rounded-sm border border-border bg-card overflow-hidden">
            <button
              type="button"
              onClick={() => setShowLogs((s) => !s)}
              className="w-full px-3 py-2 flex items-center justify-between text-[11px] hover:bg-accent cursor-pointer"
            >
              <span className="flex items-center gap-1.5 font-mono text-signal-ok font-semibold">
                <ShieldCheck className="size-3.5" />
                {tx("자동 첨부 익명화 로그 미리보기 (PII 마스킹 적용됨)", "Preview Auto-Attached Scrubbed Logs (PII Removed)")}
              </span>
              {showLogs ? <ChevronUp className="size-3.5 text-muted-foreground" /> : <ChevronDown className="size-3.5 text-muted-foreground" />}
            </button>
            {showLogs && (
              <ScrollArea className="h-32 border-t border-oled-border bg-oled p-2.5">
                <pre className="text-[10px] font-mono text-oled-foreground whitespace-pre-wrap break-all">
                  {logsPreview || tx("(수집된 최근 로그 없음)", "(No recent log entries recorded yet)")}
                </pre>
              </ScrollArea>
            )}
          </div>

          <div className="flex items-center justify-end gap-2 pt-1">
            <Button type="button" size="sm" variant="keycap" onClick={() => onOpenChange(false)}>
              {tx("닫기", "CANCEL")}
            </Button>
            <Button
              type="submit"
              size="sm"
              variant="signal"
              disabled={submitting || !title.trim()}
              className="px-4"
            >
              <Send className="size-3.5" />
              <span>{submitting ? tx("전송 중...", "SENDING...") : tx("로그와 함께 버그 제보", "SUBMIT BUG REPORT")}</span>
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}
