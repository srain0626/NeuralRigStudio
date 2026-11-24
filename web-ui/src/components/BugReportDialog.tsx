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
      <DialogContent className="max-w-xl border-white/10 bg-[#101318] text-studio-ink p-0 overflow-hidden shadow-2xl">
        <DialogHeader className="px-5 pt-4 pb-3 border-b border-white/[0.06] bg-white/[0.02]">
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-2.5">
              <div className="flex h-8 w-8 items-center justify-center rounded-md border border-rose-500/30 bg-rose-500/10 text-rose-300">
                <Bug className="size-4" />
              </div>
              <div>
                <DialogTitle className="text-[14px] font-semibold text-white">
                  {tx("버그 제보 · 자동 익명화 로그 전송", "Bug Report · Automated Scrubbed Log Telemetry")}
                </DialogTitle>
                <DialogDescription className="text-[11px] text-studio-muted">
                  {tx(
                    `v${appVersion} · 최근 로그와 오디오 진단은 LogSanitizer로 개인정보(경로·사용자명·토큰) 마스킹 후 전송됩니다.`,
                    `v${appVersion} · Recent logs & audio diagnostics are automatically scrubbed via LogSanitizer before upload.`
                  )}
                </DialogDescription>
              </div>
            </div>
            {cloudState.pendingBugReportsCount > 0 && (
              <Badge variant="outline" className="text-[10px] border-amber-500/40 text-amber-300">
                {cloudState.pendingBugReportsCount} Queued
              </Badge>
            )}
          </div>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="px-5 py-4 space-y-3.5">
          {resultMsg && (
            <div className="rounded-md border border-emerald-500/30 bg-emerald-500/10 p-3 text-xs text-emerald-200 flex items-center gap-2">
              <CheckCircle2 className="size-4 shrink-0 text-emerald-400" />
              <span>{resultMsg}</span>
            </div>
          )}

          <div className="space-y-1.5">
            <label className="text-[11px] font-medium text-white">
              {tx("문제 요약", "Issue Summary")}
            </label>
            <Input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder={tx("예: 64-frame ASIO 시작 시 출력 크랙클 발생", "e.g., Crackle on 64-frame ASIO startup")}
              className="bg-black/40 border-white/10 text-xs"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <label className="text-[11px] font-medium text-white">{tx("카테고리", "Category")}</label>
              <select
                value={category}
                onChange={(e) => setCategory(e.target.value)}
                className="w-full h-9 rounded-md border border-white/10 bg-black/50 px-2.5 text-xs text-white"
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
              <label className="text-[11px] font-medium text-white">{tx("심각도", "Severity")}</label>
              <select
                value={severity}
                onChange={(e) => setSeverity(e.target.value)}
                className="w-full h-9 rounded-md border border-white/10 bg-black/50 px-2.5 text-xs text-white"
              >
                <option value="low">Low</option>
                <option value="normal">Normal</option>
                <option value="high">High</option>
                <option value="critical">Critical</option>
              </select>
            </div>
          </div>

          <div className="space-y-1.5">
            <label className="text-[11px] font-medium text-white">
              {tx("재현 방법 및 상세 설명", "Reproduction Steps & Details")}
            </label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={4}
              placeholder={tx(
                "어떤 오디오 장치/프리셋/동작에서 문제가 발생했는지 적어주세요.",
                "Describe the audio interface, preset, or actions leading to the issue."
              )}
              className="w-full rounded-md border border-white/10 bg-black/40 p-2.5 text-xs text-white placeholder:text-studio-muted focus:outline-none focus:ring-1 focus:ring-cyan-500"
            />
          </div>

          <div className="rounded-md border border-white/[0.07] bg-black/30 overflow-hidden">
            <button
              type="button"
              onClick={() => setShowLogs((s) => !s)}
              className="w-full px-3 py-2 flex items-center justify-between text-[11px] hover:bg-white/[0.03]"
            >
              <span className="flex items-center gap-1.5 text-emerald-300">
                <ShieldCheck className="size-3.5" />
                {tx("자동 첨부 익명화 로그 미리보기 (PII 마스킹 적용됨)", "Preview Auto-Attached Scrubbed Logs (PII Removed)")}
              </span>
              {showLogs ? <ChevronUp className="size-3.5" /> : <ChevronDown className="size-3.5" />}
            </button>
            {showLogs && (
              <ScrollArea className="h-32 border-t border-white/[0.06] bg-black/60 p-2.5">
                <pre className="text-[10px] font-mono text-studio-ink/80 whitespace-pre-wrap break-all">
                  {logsPreview || tx("(수집된 최근 로그 없음)", "(No recent log entries recorded yet)")}
                </pre>
              </ScrollArea>
            )}
          </div>

          <div className="flex items-center justify-end gap-2 pt-1">
            <Button type="button" size="sm" variant="ghost" onClick={() => onOpenChange(false)} className="text-xs">
              {tx("닫기", "Cancel")}
            </Button>
            <Button
              type="submit"
              size="sm"
              disabled={submitting || !title.trim()}
              className="bg-rose-600 hover:bg-rose-500 text-white text-xs gap-1.5 px-4"
            >
              <Send className="size-3.5" />
              <span>{submitting ? tx("전송 중...", "Sending...") : tx("로그와 함께 버그 제보", "Submit Bug Report")}</span>
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}
