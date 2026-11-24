import React, { useEffect, useState } from "react"
import {
  Cloud,
  CloudOff,
  LogIn,
  LogOut,
  RefreshCw,
  Shield,
  Sparkles,
  UserCheck,
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
import { studioBridge } from "@/lib/bridge"
import { useI18n } from "@/lib/i18n"
import type { CloudStateDto } from "@/types/rig"

interface AuthAccountModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  cloudState: CloudStateDto
  onCloudStateChange: (next: CloudStateDto) => void
  onCheckUpdates: () => void
  onLog: (msg: string, level?: "info" | "success" | "error") => void
  dailyPrompt?: boolean
}

export const AuthAccountModal: React.FC<AuthAccountModalProps> = ({
  open,
  onOpenChange,
  cloudState,
  onCloudStateChange,
  onCheckUpdates,
  onLog,
  dailyPrompt = false,
}) => {
  const { tx } = useI18n()
  const [busy, setBusy] = useState<string | null>(null)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setErrorMsg(null)
    }
  }, [open])

  const isFirstGate = cloudState.mode === "unselected"
  const isDailyReminder = Boolean(dailyPrompt && !cloudState.isAuthenticated && !isFirstGate)

  const handleGoogleLogin = async () => {
    if (!studioBridge.isWebView2) {
      setErrorMsg(
        tx(
          "데스크톱 앱(WebView2) 환경에서만 Google OAuth 루프백 로그인이 동작합니다.",
          "Google OAuth login requires the desktop runtime."
        )
      )
      return
    }
    setBusy("google")
    setErrorMsg(null)
    try {
      const next = await studioBridge.invoke<CloudStateDto>("cloudSignInGoogle", {})
      onCloudStateChange(next)
      onLog(
        tx(
          `Google 로그인 완료: ${next.user?.displayName || "Player"}`,
          `Signed in via Google: ${next.user?.displayName || "Player"}`
        ),
        "success"
      )
      onOpenChange(false)
    } catch (e) {
      const msg = String(e)
      setErrorMsg(msg)
      onLog(tx(`Google 로그인 오류: ${msg}`, `Google login failed: ${msg}`), "error")
    } finally {
      setBusy(null)
    }
  }

  const handleGuestMode = async () => {
    if (studioBridge.isWebView2) {
      setBusy("guest")
      try {
        const next = await studioBridge.invoke<CloudStateDto>("cloudContinueGuest", {})
        onCloudStateChange(next)
      } catch (e) {
        onLog(String(e), "error")
      } finally {
        setBusy(null)
      }
    } else {
      onCloudStateChange({ ...cloudState, mode: "guest" })
    }
    onOpenChange(false)
  }

  const handleSyncNow = async () => {
    if (!studioBridge.isWebView2) return
    setBusy("sync")
    setErrorMsg(null)
    try {
      const next = await studioBridge.invoke<CloudStateDto>("cloudSyncNow", {})
      onCloudStateChange(next)
      toast.success(tx("클라우드 동기화 완료", "Cloud sync complete"))
    } catch (e) {
      setErrorMsg(String(e))
    } finally {
      setBusy(null)
    }
  }

  const handleSignOut = async () => {
    if (!studioBridge.isWebView2) return
    setBusy("signout")
    try {
      const next = await studioBridge.invoke<CloudStateDto>("cloudSignOut", {})
      onCloudStateChange(next)
      toast(tx("로그아웃되었습니다 (게스트 모드)", "Signed out (Guest mode)"))
    } catch (e) {
      setErrorMsg(String(e))
    } finally {
      setBusy(null)
    }
  }

  const initials = (cloudState.user?.displayName || "NR")
    .trim()
    .slice(0, 2)
    .toUpperCase()

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg p-0 overflow-hidden bg-card text-card-foreground border-border gap-0">
        <DialogHeader className="px-5 py-3.5 border-b border-border bg-secondary/45 shrink-0">
          <div className="flex items-center gap-2.5 pr-8">
            <div className="size-8 rounded-sm bg-oled border border-oled-border flex items-center justify-center text-primary shrink-0">
              {cloudState.isAuthenticated ? (
                <UserCheck className="size-4" />
              ) : (
                <Cloud className="size-4" />
              )}
            </div>
            <div>
              <DialogTitle>
                {isFirstGate
                  ? tx("NEURALRIG STUDIO · 계정 또는 게스트 선택", "NEURALRIG STUDIO · ACCOUNT OR GUEST")
                  : isDailyReminder
                    ? tx("오늘의 로그인 안내 · 클라우드 백업", "DAILY REMINDER · SIGN IN WITH GOOGLE")
                    : tx("GOOGLE 클라우드 계정 & 동기화", "GOOGLE CLOUD ACCOUNT & SYNC")}
              </DialogTitle>
              <DialogDescription>
                {isDailyReminder
                  ? tx(
                      "게스트 모드로 사용 중입니다. Google 계정으로 로그인하면 프리셋·기타 프로필·SPICE 커스텀 기어가 자동 동기화됩니다.",
                      "You are in Guest mode. Sign in with Google to sync presets, guitar profiles, and custom SPICE gear across PCs."
                    )
                  : tx(
                      "Google 로그인 시 프리셋·프로필·커스텀 SPICE 기어 동기화 및 커뮤니티 허브 게시가 해금됩니다.",
                      "Sign in with Google to sync presets & custom SPICE gear across PCs, or play offline anytime in Guest mode."
                    )}
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <div className="px-5 py-4 space-y-3.5 bg-background">
          {errorMsg && (
            <div className="rounded-sm border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-destructive">
              {errorMsg}
            </div>
          )}

          {cloudState.isAuthenticated && cloudState.user ? (
            <div className="rounded-sm border border-border bg-card p-4 space-y-3.5">
              <div className="flex items-center justify-between gap-3">
                <div className="flex items-center gap-3 min-w-0">
                  <div className="size-10 rounded-sm border border-oled-border bg-oled text-oled-foreground flex items-center justify-center shrink-0 overflow-hidden font-display text-xs font-bold">
                    {cloudState.user.avatarUrl ? (
                      <img
                        src={cloudState.user.avatarUrl}
                        alt={cloudState.user.displayName}
                        className="size-full object-cover"
                      />
                    ) : (
                      <span>{initials}</span>
                    )}
                  </div>
                  <div className="min-w-0">
                    <div className="flex items-center gap-1.5 flex-wrap">
                      <span className="font-display text-sm font-bold text-foreground truncate">
                        {cloudState.user.displayName}
                      </span>
                      {cloudState.user.isAdmin && (
                        <Badge variant="warn" size="xs">
                          <Shield className="size-2.5" />
                          ADMIN
                        </Badge>
                      )}
                    </div>
                    <div className="text-[11px] font-mono text-muted-foreground truncate">
                      {cloudState.user.email}
                    </div>
                  </div>
                </div>
                <Badge variant="ok" size="xs" className="shrink-0">
                  GOOGLE CONNECTED
                </Badge>
              </div>

              <div className="rounded-sm border border-oled-border bg-oled px-3 py-2 flex items-center justify-between text-[11px] font-mono">
                <span className="text-oled-muted uppercase tracking-wider">
                  {tx("마지막 클라우드 동기화", "LAST CLOUD SYNC")}
                </span>
                <span className="text-oled-foreground tabular-nums">
                  {cloudState.lastSyncUtc
                    ? new Date(cloudState.lastSyncUtc).toLocaleString()
                    : tx("기록 없음", "NEVER")}
                </span>
              </div>

              <div className="flex flex-wrap items-center gap-2 pt-0.5">
                <Button
                  size="sm"
                  variant="signal"
                  disabled={busy !== null}
                  onClick={() => void handleSyncNow()}
                >
                  <RefreshCw className={`size-3.5 ${busy === "sync" ? "animate-spin" : ""}`} />
                  <span>
                    {busy === "sync"
                      ? tx("동기화 중...", "SYNCING...")
                      : tx("지금 클라우드 동기화", "SYNC NOW")}
                  </span>
                </Button>
                <Button
                  size="sm"
                  variant="keycap"
                  disabled={busy !== null}
                  onClick={() => {
                    onOpenChange(false)
                    onCheckUpdates()
                  }}
                >
                  <Sparkles className="size-3.5" />
                  <span>{tx("업데이트 확인", "CHECK UPDATES")}</span>
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  disabled={busy !== null}
                  onClick={() => void handleSignOut()}
                  className="text-destructive hover:text-destructive ml-auto"
                >
                  <LogOut className="size-3.5" />
                  <span>{tx("로그아웃", "SIGN OUT")}</span>
                </Button>
              </div>
            </div>
          ) : (
            <div className="space-y-3">
              <div className="rounded-sm border border-border bg-card p-3.5 space-y-1.5 text-xs text-muted-foreground">
                <div className="font-mono text-[10px] font-bold uppercase tracking-wider text-primary">
                  {tx("클라우드 세션 안내", "CLOUD SESSION")}
                </div>
                <p className="leading-relaxed text-foreground/90">
                  {tx(
                    "Google 계정으로 로그인하면 프리셋·기타 프로필·SPICE 커스텀 기어가 자동 동기화되며, 게스트 모드에서도 모든 오프라인 연주/DSP/로컬 기능을 제한 없이 사용할 수 있습니다.",
                    "Sign in with Google to synchronize presets, guitar profiles, and custom SPICE gear across PCs, or play offline with zero restrictions in Guest mode."
                  )}
                </p>
              </div>

              <div className="grid grid-cols-1 gap-2">
                <Button
                  size="lg"
                  variant="signal"
                  disabled={busy !== null}
                  onClick={() => void handleGoogleLogin()}
                  className="w-full h-10"
                >
                  <LogIn className="size-4" />
                  <span>
                    {busy === "google"
                      ? tx("브라우저에서 Google 로그인 진행 중...", "WAITING FOR GOOGLE SIGN-IN...")
                      : tx("Google 계정으로 로그인 (클라우드 동기화 · 허브)", "SIGN IN WITH GOOGLE (SYNC & HUB)")}
                  </span>
                </Button>

                <Button
                  size="lg"
                  variant="keycap"
                  disabled={busy !== null}
                  onClick={() => void handleGuestMode()}
                  className="w-full h-10"
                >
                  <CloudOff className="size-4 text-muted-foreground" />
                  <span>
                    {isDailyReminder
                      ? tx("오늘은 게스트로 계속하기", "CONTINUE AS GUEST TODAY")
                      : tx("로그인 없이 게스트 / 오프라인 모드로 연주 시작", "CONTINUE AS GUEST (OFFLINE MODE)")}
                  </span>
                </Button>
              </div>
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}
