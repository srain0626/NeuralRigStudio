import React, { useEffect, useState } from "react"
import {
  Cloud,
  CloudOff,
  Globe,
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
import { Input } from "@/components/ui/input"
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
}

export const AuthAccountModal: React.FC<AuthAccountModalProps> = ({
  open,
  onOpenChange,
  cloudState,
  onCloudStateChange,
  onCheckUpdates,
  onLog,
}) => {
  const { tx } = useI18n()
  const [endpointInput, setEndpointInput] = useState(cloudState.cloudApiBaseUrl || "")
  const [busy, setBusy] = useState<string | null>(null)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setEndpointInput(cloudState.cloudApiBaseUrl || "")
      setErrorMsg(null)
    }
  }, [open, cloudState.cloudApiBaseUrl])

  const isFirstGate = cloudState.mode === "unselected"

  const handleSaveEndpoint = async () => {
    if (!studioBridge.isWebView2) return
    setBusy("endpoint")
    setErrorMsg(null)
    try {
      const next = await studioBridge.invoke<CloudStateDto>("cloudSetEndpoint", {
        url: endpointInput.trim(),
      })
      onCloudStateChange(next)
      toast.success(tx("클라우드 서버 엔드포인트 저장됨", "Cloud endpoint saved"))
    } catch (e) {
      setErrorMsg(String(e))
    } finally {
      setBusy(null)
    }
  }

  const handleGoogleLogin = async () => {
    if (!studioBridge.isWebView2) {
      setErrorMsg(tx("데스크톱 앱(WebView2) 환경에서만 Google OAuth 루프백 로그인이 동작합니다.", "Google OAuth login requires the desktop runtime."))
      return
    }
    setBusy("google")
    setErrorMsg(null)
    try {
      // Save endpoint first if user typed a new URL before hitting Google Sign-In
      if (endpointInput.trim() !== (cloudState.cloudApiBaseUrl || "")) {
        const epState = await studioBridge.invoke<CloudStateDto>("cloudSetEndpoint", {
          url: endpointInput.trim(),
        })
        onCloudStateChange(epState)
      }
      const next = await studioBridge.invoke<CloudStateDto>("cloudSignInGoogle", {})
      onCloudStateChange(next)
      onLog(tx(`Google 로그인 완료: ${next.user?.displayName || "Player"}`, `Signed in via Google: ${next.user?.displayName || "Player"}`), "success")
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

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg border-white/10 bg-[#101318] text-studio-ink p-0 overflow-hidden shadow-2xl">
        <DialogHeader className="px-6 pt-5 pb-4 border-b border-white/[0.06] bg-gradient-to-r from-cyan-500/[0.08] via-transparent to-violet-500/[0.06]">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg border border-cyan-500/30 bg-cyan-500/10 text-cyan-300">
              {cloudState.isAuthenticated ? <UserCheck className="size-5" /> : <Cloud className="size-5" />}
            </div>
            <div>
              <DialogTitle className="text-[15px] font-semibold text-white">
                {isFirstGate
                  ? tx("NeuralRig Studio 시작하기 · 계정 또는 게스트 선택", "Welcome to NeuralRig Studio · Account or Guest")
                  : tx("Google 클라우드 계정 및 동기화 설정", "Google Cloud Account & Sync")}
              </DialogTitle>
              <DialogDescription className="text-[11px] text-studio-muted">
                {tx(
                  "Google 로그인 시 프리셋·프로필·커스텀 SPICE 기어 클라우드 동기화 및 커뮤니티 허브 게시가 해금됩니다.",
                  "Sign in with Google to sync presets & custom SPICE gear across PCs, or play offline anytime in Guest mode."
                )}
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <div className="px-6 py-5 space-y-4">
          {errorMsg && (
            <div className="rounded-md border border-rose-500/30 bg-rose-500/10 p-2.5 text-[11px] text-rose-200">
              {errorMsg}
            </div>
          )}

          {cloudState.isAuthenticated && cloudState.user ? (
            <div className="rounded-lg border border-emerald-500/25 bg-emerald-500/[0.05] p-4 space-y-3">
              <div className="flex items-center justify-between gap-2">
                <div>
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-semibold text-white">{cloudState.user.displayName}</span>
                    {cloudState.user.isAdmin && (
                      <Badge variant="outline" className="text-[9px] border-amber-500/40 bg-amber-500/15 text-amber-300 gap-1">
                        <Shield className="size-2.5" />
                        ADMIN
                      </Badge>
                    )}
                  </div>
                  <div className="text-xs text-studio-muted font-mono">{cloudState.user.email}</div>
                </div>
                <Badge variant="outline" className="text-[10px] border-emerald-500/40 text-emerald-300">
                  Google Connected
                </Badge>
              </div>

              <div className="text-[11px] text-studio-muted flex items-center justify-between border-t border-white/[0.06] pt-2.5">
                <span>
                  {tx("마지막 동기화: ", "Last synced: ")}
                  <span className="font-mono text-white/85">
                    {cloudState.lastSyncUtc ? new Date(cloudState.lastSyncUtc).toLocaleString() : tx("기록 없음", "Never")}
                  </span>
                </span>
              </div>

              <div className="flex flex-wrap gap-2 pt-1">
                <Button
                  size="sm"
                  disabled={busy !== null}
                  onClick={() => void handleSyncNow()}
                  className="bg-cyan-600 hover:bg-cyan-500 text-white text-xs gap-1.5"
                >
                  <RefreshCw className={`size-3.5 ${busy === "sync" ? "animate-spin" : ""}`} />
                  <span>{busy === "sync" ? tx("동기화 중...", "Syncing...") : tx("지금 클라우드 동기화", "Sync Now")}</span>
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={busy !== null}
                  onClick={() => {
                    onOpenChange(false)
                    onCheckUpdates()
                  }}
                  className="text-xs gap-1.5"
                >
                  <Sparkles className="size-3.5" />
                  <span>{tx("업데이트 확인", "Check Updates")}</span>
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  disabled={busy !== null}
                  onClick={() => void handleSignOut()}
                  className="text-xs text-rose-300 hover:text-rose-200 gap-1.5 ml-auto"
                >
                  <LogOut className="size-3.5" />
                  <span>{tx("로그아웃", "Sign Out")}</span>
                </Button>
              </div>
            </div>
          ) : (
            <div className="space-y-3">
              <div className="grid grid-cols-1 gap-2.5">
                <Button
                  size="lg"
                  disabled={busy !== null}
                  onClick={() => void handleGoogleLogin()}
                  className="w-full bg-white text-black hover:bg-white/90 font-semibold text-xs h-10 gap-2"
                >
                  <LogIn className="size-4" />
                  <span>
                    {busy === "google"
                      ? tx("브라우저에서 Google 로그인 진행 중...", "Waiting for Google sign-in...")
                      : tx("Google 계정으로 로그인 (클라우드 동기화 · 허브)", "Sign in with Google (Cloud Sync & Hub)")}
                  </span>
                </Button>

                <Button
                  size="lg"
                  variant="outline"
                  disabled={busy !== null}
                  onClick={() => void handleGuestMode()}
                  className="w-full border-white/15 bg-white/[0.03] hover:bg-white/[0.07] text-xs h-10 gap-2"
                >
                  <CloudOff className="size-4 text-studio-muted" />
                  <span>{tx("로그인 없이 게스트 / 오프라인 모드로 연주 시작", "Continue as Guest (Offline Mode)")}</span>
                </Button>
              </div>
            </div>
          )}

          {/* Cloud Deployment Endpoint Configuration */}
          <div className="rounded-md border border-white/[0.07] bg-black/30 p-3 space-y-2">
            <div className="flex items-center justify-between text-[11px]">
              <span className="flex items-center gap-1.5 text-studio-muted font-medium">
                <Globe className="size-3.5 text-cyan-400" />
                {tx("클라우드 서버 URL (Vercel Next.js)", "Cloud Server URL (Vercel Next.js)")}
              </span>
              <Badge variant="outline" className="text-[9px] h-4 border-white/15">
                {cloudState.isConfigured ? tx("연결 설정됨", "Configured") : tx("로컬/오프라인", "Offline")}
              </Badge>
            </div>
            <div className="flex items-center gap-2">
              <Input
                value={endpointInput}
                onChange={(e) => setEndpointInput(e.target.value)}
                placeholder="https://your-neuralrig-cloud.vercel.app"
                className="h-8 text-xs font-mono bg-black/40 border-white/10"
              />
              <Button
                size="xs"
                variant="outline"
                disabled={busy !== null}
                onClick={() => void handleSaveEndpoint()}
                className="h-8 px-3 text-[11px]"
              >
                {tx("저장", "Save")}
              </Button>
            </div>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
