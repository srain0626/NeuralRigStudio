import React, { useState } from "react"
import { ExternalLink, Heart, Sparkles } from "lucide-react"
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

export const PATREON_MEMBERSHIP_URL = "https://www.patreon.com/cw/srain0626/membership"

interface SponsorPromptModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

export const SponsorPromptModal: React.FC<SponsorPromptModalProps> = ({
  open,
  onOpenChange,
  onLog,
}) => {
  const { tx } = useI18n()
  const [opening, setOpening] = useState(false)

  const handleSupportClick = async () => {
    setOpening(true)
    try {
      if (studioBridge.isWebView2) {
        await studioBridge.invoke("openExternalUrl", { url: PATREON_MEMBERSHIP_URL })
      } else {
        window.open(PATREON_MEMBERSHIP_URL, "_blank", "noopener,noreferrer")
      }
      const msg = tx(
        "Patreon 멤버십 페이지를 열었습니다. 후원해 주셔서 감사합니다!",
        "Opened Patreon membership page. Thank you for supporting NeuralRig Studio!"
      )
      toast.success(msg)
      onLog(`[Sponsor] ${msg}`, "success")
      onOpenChange(false)
    } catch (e) {
      const err = String(e)
      toast.error(err)
      onLog(`[Sponsor] ${err}`, "error")
    } finally {
      setOpening(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg p-0 overflow-hidden bg-card text-card-foreground border-border gap-0">
        <DialogHeader className="px-5 py-3.5 border-b border-border bg-secondary/45 shrink-0">
          <div className="flex items-center gap-2.5 pr-8">
            <div className="size-8 rounded-sm bg-oled border border-oled-border flex items-center justify-center text-primary shrink-0">
              <Heart className="size-4" />
            </div>
            <div>
              <div className="flex items-center gap-2 flex-wrap">
                <DialogTitle>
                  {tx(
                    "NEURALRIG STUDIO와 함께해 주셔서 감사합니다",
                    "THANK YOU FOR PLAYING WITH NEURALRIG STUDIO"
                  )}
                </DialogTitle>
                <Badge variant="ok" size="xs">
                  <Sparkles className="size-2.5" />
                  7+ DAYS
                </Badge>
              </div>
              <DialogDescription className="mt-0.5">
                {tx(
                  "일주일 이상 함께 연주해 주신 크리에이터분께 드리는 안내 (1회만 표시)",
                  "A one-time thank-you note after your first week with NeuralRig Studio"
                )}
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <div className="px-5 py-4 space-y-3.5 bg-background">
          <div className="rounded-sm border border-border bg-card p-4 space-y-2.5 text-xs leading-relaxed">
            <div className="font-mono text-[10px] font-bold uppercase tracking-wider text-primary">
              {tx("인디프렌들리 프리 소프트웨어 후원 안내", "INDEPENDENT OPEN PROJECT SUPPORT")}
            </div>
            <p className="text-foreground/95">
              {tx(
                "NeuralRig Studio는 실시간 MNA SPICE 회로 해석, 네이티브 .nam SIMD 추론 엔진, 커뮤니티 기어 허브와 클라우드 동기화를 무료로 제공하는 1인 개발 프로젝트입니다.",
                "NeuralRig Studio is built and maintained independently for free—delivering real-time MNA SPICE physics, native .nam SIMD inference, Community Hub, and Cloud Sync."
              )}
            </p>
            <p className="text-muted-foreground">
              {tx(
                "유지보수와 신규 앰프·페달 회로 모델링이 꾸준히 이어질 수 있도록, 여유가 되신다면 Patreon 멤버십으로 응원해 주세요. (이 창은 최초 1회만 표시됩니다.)",
                "If NeuralRig Studio helps your tone and practice, optional Patreon membership support helps sustain server costs and new circuit models. (Shown only once.)"
              )}
            </p>
          </div>

          <div className="rounded-sm border border-oled-border bg-oled px-3 py-2 flex items-center justify-between gap-2 text-[11px] font-mono">
            <span className="text-oled-muted uppercase tracking-wider shrink-0">PATREON</span>
            <span className="text-oled-foreground truncate">{PATREON_MEMBERSHIP_URL}</span>
          </div>

          <div className="flex flex-wrap items-center gap-2 pt-0.5">
            <Button
              size="lg"
              variant="signal"
              disabled={opening}
              onClick={() => void handleSupportClick()}
              className="flex-1 h-10"
            >
              <ExternalLink className="size-4" />
              <span>
                {opening
                  ? tx("브라우저에서 여는 중...", "OPENING BROWSER...")
                  : tx("Patreon 멤버십으로 후원하기", "SUPPORT ON PATREON")}
              </span>
            </Button>
            <Button
              size="lg"
              variant="keycap"
              disabled={opening}
              onClick={() => onOpenChange(false)}
              className="h-10"
            >
              <span>{tx("괜찮습니다, 계속 연주하기", "NO THANKS, BACK TO RIG")}</span>
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
