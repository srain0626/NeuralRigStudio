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
        className={`max-w-md p-0 overflow-hidden bg-card text-card-foreground border-border gap-0 ${
          isForced ? "[&>button]:hidden" : ""
        }`}
      >
        <DialogHeader
          className={`px-5 py-3.5 border-b border-border shrink-0 ${
            isForced ? "bg-destructive/15" : "bg-secondary/45"
          }`}
        >
          <div className="flex items-center gap-2.5 pr-8">
            <div
              className={`size-8 rounded-sm bg-oled border border-oled-border flex items-center justify-center shrink-0 ${
                isForced ? "text-destructive" : "text-primary"
              }`}
            >
              {isForced ? <AlertTriangle className="size-4" /> : <Sparkles className="size-4" />}
            </div>
            <div>
              <div className="flex items-center gap-2 flex-wrap">
                <DialogTitle>
                  {isForced
                    ? tx("필수 강제 업데이트 필요", "MANDATORY UPDATE REQUIRED")
                    : ota.updateAvailable
                      ? tx("새 버전 업데이트 가능", "NEW VERSION AVAILABLE")
                      : tx("최신 버전 사용 중", "UP TO DATE")}
                </DialogTitle>
                <Badge variant={isForced ? "destructive" : "ok"} size="xs">
                  v{ota.latestVersion}
                </Badge>
              </div>
              <DialogDescription className="mt-0.5 font-mono text-[11px]">
                {tx(
                  `현재 v${ota.currentVersion} → 최신 v${ota.latestVersion} (최소 지원 v${ota.minSupportedVersion})`,
                  `Current v${ota.currentVersion} → Latest v${ota.latestVersion} (Min v${ota.minSupportedVersion})`
                )}
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <div className="px-5 py-4 space-y-3 bg-background">
          {isForced && (
            <div className="rounded-sm border border-destructive/40 bg-destructive/10 p-3 text-xs text-destructive font-medium leading-relaxed">
              {tx(
                "보안·클라우드 프로토콜 호환성 유지 또는 치명적 오류 패치를 위해 이 업데이트를 설치해야 앱을 계속 사용할 수 있습니다.",
                `This release includes critical protocol/compatibility fixes. Installing v${ota.latestVersion} is required before continuing.`
              )}
            </div>
          )}

          {notes && (
            <div className="rounded-sm border border-oled-border bg-oled p-3 text-[11px] text-oled-foreground whitespace-pre-wrap max-h-44 overflow-y-auto font-mono leading-relaxed">
              {notes}
            </div>
          )}

          {statusMsg && (
            <div className="rounded-sm border border-border bg-card p-2.5 text-[11px] text-primary flex items-center gap-2 font-mono">
              <RefreshCw className={`size-3.5 shrink-0 ${busy ? "animate-spin" : ""}`} />
              <span>{statusMsg}</span>
            </div>
          )}

          <div className="flex items-center justify-end gap-2 pt-1">
            {!isForced && (
              <Button size="sm" variant="keycap" onClick={() => onOpenChange(false)}>
                {ota.updateAvailable ? tx("나중에", "LATER") : tx("닫기", "CLOSE")}
              </Button>
            )}
            {ota.updateAvailable && (
              <Button
                size="sm"
                variant={isForced ? "destructive" : "signal"}
                disabled={busy}
                onClick={() => void handleInstall()}
                className="px-4"
              >
                {ota.installerUrl || ota.standaloneExeUrl ? (
                  <Download className="size-3.5" />
                ) : (
                  <ExternalLink className="size-3.5" />
                )}
                <span>
                  {busy
                    ? tx("다운로드 중...", "DOWNLOADING...")
                    : ota.installerUrl || ota.standaloneExeUrl
                      ? tx("지금 다운로드 및 설치", "DOWNLOAD & INSTALL")
                      : tx("GitHub 릴리즈 열기", "OPEN RELEASE PAGE")}
                </span>
              </Button>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
