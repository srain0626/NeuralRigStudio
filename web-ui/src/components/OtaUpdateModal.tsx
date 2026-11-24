import React, { useState } from "react"
import {
  AlertTriangle,
  Download,
  ExternalLink,
  RefreshCw,
  Sparkles,
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
import { studioBridge } from "@/lib/bridge"
import { useI18n } from "@/lib/i18n"
import type { OtaCheckResultDto } from "@/types/rig"

interface OtaUpdateModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  ota: OtaCheckResultDto | null
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

export const OtaUpdateModal: React.FC<OtaUpdateModalProps> = ({
  open,
  onOpenChange,
  ota,
  onLog,
}) => {
  const { tx, locale } = useI18n()
  const [busy, setBusy] = useState(false)
  const [statusMsg, setStatusMsg] = useState<string | null>(null)

  if (!ota) return null
  const isForced = Boolean(ota.updateAvailable && ota.forceUpdate)
  const notes = locale === "ko" ? ota.releaseNotesKo || ota.releaseNotesEn : ota.releaseNotesEn || ota.releaseNotesKo

  const handleInstall = async () => {
    if (!studioBridge.isWebView2) return
    setBusy(true)
    setStatusMsg(tx("업데이트 패키지 다운로드 및 검증 중...", "Downloading and verifying release package..."))
    try {
      const res = await studioBridge.invoke<{ ok: boolean; message: string }>("applyOtaUpdate", ota)
      setStatusMsg(res.message)
      onLog(`[OTA] ${res.message}`, res.ok ? "success" : "error")
    } catch (e) {
      const msg = String(e)
      setStatusMsg(msg)
      onLog(`[OTA Error] ${msg}`, "error")
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (isForced && !next) return
        onOpenChange(next)
      }}
    >
      <DialogContent
        onEscapeKeyDown={(e) => {
          if (isForced) e.preventDefault()
        }}
        onPointerDownOutside={(e) => {
          if (isForced) e.preventDefault()
        }}
        className={`max-w-md border-white/15 bg-[#101318] text-studio-ink p-0 overflow-hidden shadow-2xl ${
          isForced ? "[&>button]:hidden" : ""
        }`}
      >
        <DialogHeader
          className={`px-5 pt-4 pb-3 border-b border-white/[0.06] ${
            isForced ? "bg-rose-500/15" : "bg-cyan-500/10"
          }`}
        >
          <div className="flex items-center gap-2.5">
            <div
              className={`flex h-9 w-9 items-center justify-center rounded-md border ${
                isForced
                  ? "border-rose-500/40 bg-rose-500/20 text-rose-300"
                  : "border-cyan-500/40 bg-cyan-500/20 text-cyan-300"
              }`}
            >
              {isForced ? <AlertTriangle className="size-5" /> : <Sparkles className="size-5" />}
            </div>
            <div>
              <div className="flex items-center gap-2">
                <DialogTitle className="text-[14px] font-semibold text-white">
                  {isForced
                    ? tx("필수 강제 업데이트 필요", "Mandatory Security & Protocol Update")
                    : ota.updateAvailable
                      ? tx("새 버전 업데이트 가능", "New Version Available")
                      : tx("최신 버전 사용 중", "Up to Date")}
                </DialogTitle>
                <Badge
                  variant="outline"
                  className={
                    isForced
                      ? "text-[9px] border-rose-500/50 text-rose-200"
                      : "text-[9px] border-cyan-500/40 text-cyan-200"
                  }
                >
                  v{ota.latestVersion}
                </Badge>
              </div>
              <DialogDescription className="text-[11px] text-studio-muted">
                {tx(
                  `현재 v${ota.currentVersion} → 최신 v${ota.latestVersion} (최소 지원 v${ota.minSupportedVersion})`,
                  `Current v${ota.currentVersion} → Latest v${ota.latestVersion} (Min supported v${ota.minSupportedVersion})`
                )}
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <div className="px-5 py-4 space-y-3">
          {isForced && (
            <div className="rounded-md border border-rose-500/40 bg-rose-500/10 p-3 text-xs text-rose-100">
              {tx(
                "보안·클라우드 프로토콜 호환성 유지 또는 치명적 오류 패치를 위해 이 업데이트를 설치해야 앱을 계속 사용할 수 있습니다.",
                "This release includes critical protocol/compatibility fixes. Installing v" +
                  ota.latestVersion +
                  " is required before continuing."
              )}
            </div>
          )}

          {notes && (
            <div className="rounded-md border border-white/[0.07] bg-black/40 p-3 text-[11px] text-studio-ink/90 whitespace-pre-wrap max-h-44 overflow-y-auto font-mono">
              {notes}
            </div>
          )}

          {statusMsg && (
            <div className="rounded-md border border-cyan-500/30 bg-cyan-500/10 p-2.5 text-[11px] text-cyan-200 flex items-center gap-2">
              <RefreshCw className={`size-3.5 shrink-0 ${busy ? "animate-spin" : ""}`} />
              <span>{statusMsg}</span>
            </div>
          )}

          <div className="flex items-center justify-end gap-2 pt-1">
            {!isForced && (
              <Button size="sm" variant="ghost" onClick={() => onOpenChange(false)} className="text-xs">
                {ota.updateAvailable ? tx("나중에", "Later") : tx("닫기", "Close")}
              </Button>
            )}
            {ota.updateAvailable && (
              <Button
                size="sm"
                disabled={busy}
                onClick={() => void handleInstall()}
                className={`text-white text-xs gap-1.5 px-4 ${
                  isForced ? "bg-rose-600 hover:bg-rose-500" : "bg-cyan-600 hover:bg-cyan-500"
                }`}
              >
                {ota.installerUrl || ota.standaloneExeUrl ? <Download className="size-3.5" /> : <ExternalLink className="size-3.5" />}
                <span>
                  {busy
                    ? tx("다운로드 중...", "Downloading...")
                    : ota.installerUrl || ota.standaloneExeUrl
                      ? tx("지금 다운로드 및 설치", "Download & Install")
                      : tx("GitHub 릴리즈 열기", "Open Release Page")}
                </span>
              </Button>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
