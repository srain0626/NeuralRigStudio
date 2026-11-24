import React, { useState } from "react"
import {
  Bot,
  Check,
  Copy,
  Cpu,
  FileCode2,
  Globe,
  ShieldAlert,
  Sparkles,
  Terminal,
  Wrench,
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
import { ScrollArea } from "@/components/ui/scroll-area"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { HelpHint } from "@/components/ui/tooltip"
import { useI18n } from "@/lib/i18n"
import type { CustomDeviceSpecDto } from "@/types/rig"

interface CustomGearGuideDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  customDevices: CustomDeviceSpecDto[]
  onOpenCommunityHub?: () => void
}

type GuideTab = "workflow" | "mcp" | "library"

const AI_PROVIDERS = [
  { id: "all", label: "All Providers (Auto)", path: "All detected configs" },
  { id: "claude-desktop", label: "Claude Desktop", path: "%APPDATA%\\Claude\\claude_desktop_config.json" },
  { id: "claude-code", label: "Claude Code", path: "~/.claude.json" },
  { id: "cursor", label: "Cursor", path: "~/.cursor/mcp.json" },
  { id: "windsurf", label: "Windsurf", path: "~/.codeium/windsurf/mcp_config.json" },
  { id: "codex", label: "OpenAI Codex CLI", path: "~/.codex/config.toml" },
  { id: "gemini", label: "Gemini CLI", path: "~/.gemini/settings.json" },
  { id: "antigravity", label: "Antigravity", path: "~/.gemini/antigravity/mcp_config.json" },
] as const

export const CustomGearGuideDialog: React.FC<CustomGearGuideDialogProps> = ({
  open,
  onOpenChange,
  customDevices,
  onOpenCommunityHub,
}) => {
  const { tx } = useI18n()
  const [tab, setTab] = useState<GuideTab>("workflow")
  const [selectedClient, setSelectedClient] = useState<string>("all")
  const [copiedId, setCopiedId] = useState<string | null>(null)

  const copyText = async (id: string, text: string, label: string) => {
    try {
      await navigator.clipboard.writeText(text)
      setCopiedId(id)
      toast.success(tx(`클립보드 복사됨: ${label}`, `Copied: ${label}`))
      window.setTimeout(() => setCopiedId((prev) => (prev === id ? null : prev)), 1800)
    } catch {
      toast.error(tx("클립보드 복사 실패", "Clipboard copy failed"))
    }
  }

  const installCmd = `npx -y neuralrig-mcp install --client ${selectedClient}`
  const snippetText =
    selectedClient === "codex"
      ? `[mcp_servers.neuralrig]\ncommand = "npx"\nargs = ["-y", "neuralrig-mcp"]`
      : JSON.stringify(
          {
            mcpServers: {
              neuralrig: {
                command: "npx",
                args: ["-y", "neuralrig-mcp"],
              },
            },
          },
          null,
          2
        )

  const prompts = [
    {
      id: "pedal",
      tag: "DRIVE / FUZZ PEDAL",
      titleKo: "빈티지/부티크 페달 추가 프롬프트",
      titleEn: "Add Custom Pedal via MNA SPICE",
      prompt:
        'NeuralRig MCP를 사용해 "Boss HM-2w" 또는 "ProCo You Dirty RAT" 페달을 추가해줘. 웹에서 실제 회로도(R/C/트랜지스터/다이오드·옵앰프 데이터시트)를 찾아 검증하고, 없으면 없다고 답해줘. 검증된 회로도를 neuralrig_simulate_spice(.OP/.AC/.TRAN, RT 0B)로 확인한 뒤 neuralrig_commit_spice_device로 등록해줘.',
    },
    {
      id: "amp",
      tag: "TUBE AMP HEAD",
      titleKo: "진공관 앰프 프리앰프 추가 프롬프트",
      titleEn: "Add Tube Amp via 12AX7 Triode SPICE",
      prompt:
        'NeuralRig MCP로 "Dumble Overdrive Special Clean/OD" 앰프 모델을 추가해줘. 실제 12AX7 다단 플레이트/캐소드 저항·커플링 캡과 B+ 전압 회로도를 웹에서 찾아보고, 없을 경우 추측하지 말고 없다고 보고해줘. 시뮬레이션 검증 통과 후 체인 슬롯에 넣어줘.',
    },
    {
      id: "cab",
      tag: "CABINET IR",
      titleKo: "스피커 캐비넷 음향 커브 IR 생성 프롬프트",
      titleEn: "Create Acoustic Cabinet IR via MCP",
      prompt:
        'NeuralRig MCP의 neuralrig_simulate_spice + cabAcoustic 파라미터를 사용해서 "Celestion Creamback M-65 2x12 Open Back" 실제 주파수 응답 커브 기반 캐비넷 IR을 검증하고 스튜디오에 등록해줘.',
    },
  ]

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl p-0 overflow-hidden bg-card text-card-foreground border-border gap-0">
        <DialogHeader className="px-5 py-3.5 border-b border-border bg-secondary/45 shrink-0">
          <div className="flex flex-wrap items-center justify-between gap-3 pr-8">
            <div className="flex items-center gap-2.5 min-w-0">
              <div className="size-8 rounded-sm bg-oled border border-oled-border flex items-center justify-center text-primary shrink-0">
                <Wrench className="size-4" />
              </div>
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <DialogTitle className="truncate">
                    {tx("커스텀 페달 · 앰프 · MCP", "CUSTOM GEAR & MCP")}
                  </DialogTitle>
                  <HelpHint
                    side="bottom"
                    content={tx(
                      "실제 회로도 리서치 → .OP/.AC/.TRAN 물리 검증 → 0 B 실시간 MNA SPICE 및 IR 핫로드를 지원합니다.",
                      "Web Schematic Research → .OP/.AC/.TRAN Physics Verification → Zero-Alloc MNA SPICE & Cab IR Hot-Load."
                    )}
                  />
                </div>
                <DialogDescription className="sr-only">
                  {tx(
                    "실제 회로도 리서치 → .OP/.AC/.TRAN 물리 검증 → 0 B 실시간 MNA SPICE / Partitioned IR 핫로드",
                    "Web Schematic Research → .OP/.AC/.TRAN Physics Verification → Zero-Alloc MNA SPICE & Cab IR Hot-Load"
                  )}
                </DialogDescription>
              </div>
            </div>
            {onOpenCommunityHub && (
              <Button
                size="xs"
                variant="keycap"
                onClick={() => {
                  onOpenChange(false)
                  onOpenCommunityHub()
                }}
              >
                <Globe className="size-3.5 text-primary" />
                <span>{tx("커뮤니티 기어 허브", "COMMUNITY HUB")}</span>
              </Button>
            )}
          </div>
        </DialogHeader>

        <div className="px-5 py-2.5 border-b border-border bg-card flex items-center justify-between">
          <Tabs value={tab} onValueChange={(v) => setTab(v as GuideTab)}>
            <TabsList>
              <TabsTrigger value="workflow">
                {tx("1. AI 회로도 생성 파이프라인", "1. AI SPICE PIPELINE")}
              </TabsTrigger>
              <TabsTrigger value="mcp">
                {tx("2. npx 다중 AI MCP 설치", "2. NPX MULTI-AI MCP")}
              </TabsTrigger>
              <TabsTrigger value="library">
                {tx(`3. 커스텀 기어 (${customDevices.length}) · WAV IR`, `3. CUSTOM GEAR (${customDevices.length}) & IR`)}
              </TabsTrigger>
            </TabsList>
          </Tabs>
        </div>

        <ScrollArea className="h-[440px] px-5 py-4 bg-background">
          {tab === "workflow" && (
            <div className="space-y-4 pb-2">
              <div className="rounded-sm border border-signal-warn/45 bg-signal-warn/10 px-3 py-2 flex items-center gap-2 text-xs text-foreground">
                <ShieldAlert className="size-4 text-signal-warn shrink-0" />
                <span className="font-semibold truncate">
                  {tx("실제 회로도 검증 원칙 (추측 생성 금지)", "Verified Schematics Only (No Fabrication)")}
                </span>
                <HelpHint
                  content={tx(
                    "웹에서 실제 페달/앰프 회로도나 공식 데이터시트를 찾지 못하면 임의로 추측하지 않고 '찾을 수 없음'으로 보고합니다.",
                    "If no verifiable schematic or datasheet exists online, the MCP pipeline refuses to fabricate values and reports clearly."
                  )}
                />
              </div>

              <div className="grid grid-cols-1 md:grid-cols-5 gap-2">
                {[
                  { n: "01", title: "MCP Guide", sub: "neuralrig_get_spice_guide" },
                  { n: "02", title: "Schematic Search", sub: "ElectroSmash / Datasheet" },
                  { n: "03", title: "Model Reg", sub: "neuralrig_register_spice_model" },
                  { n: "04", title: ".OP/.AC/.TRAN", sub: "neuralrig_simulate_spice (0B)" },
                  { n: "05", title: "Hot-Load & Sync", sub: "neuralrig_commit_spice_device" },
                ].map((s) => (
                  <div key={s.n} className="rounded-sm border border-border bg-card p-2.5 min-w-0 overflow-hidden">
                    <div className="text-[10px] font-mono font-bold text-primary">STEP {s.n}</div>
                    <div className="font-display text-xs font-bold text-foreground mt-0.5 truncate">{s.title}</div>
                    <div className="text-[10px] font-mono text-muted-foreground mt-1 truncate">{s.sub}</div>
                  </div>
                ))}
              </div>

              <div className="space-y-2.5">
                <div className="flex items-center gap-1.5 font-display text-xs font-bold text-foreground">
                  <Sparkles className="size-3.5 text-primary shrink-0" />
                  <span>{tx("AI 에이전트 원클릭 프롬프트", "Copyable AI Prompts")}</span>
                </div>
                {prompts.map((p) => (
                  <div key={p.id} className="rounded-sm border border-border bg-card p-3 space-y-2 min-w-0 overflow-hidden">
                    <div className="flex items-center justify-between gap-2 min-w-0">
                      <div className="flex items-center gap-2 min-w-0 flex-1">
                        <Badge variant="hot" size="xs">
                          {p.tag}
                        </Badge>
                        <span className="text-xs font-semibold text-foreground truncate">{tx(p.titleKo, p.titleEn)}</span>
                      </div>
                      <Button
                        size="xs"
                        variant="keycap"
                        className="shrink-0"
                        onClick={() => void copyText(p.id, p.prompt, tx(p.titleKo, p.titleEn))}
                      >
                        {copiedId === p.id ? <Check className="size-3 text-signal-ok" /> : <Copy className="size-3" />}
                        <span>{copiedId === p.id ? tx("복사됨", "COPIED") : tx("복사", "COPY")}</span>
                      </Button>
                    </div>
                    <p className="text-[11px] font-mono text-oled-foreground bg-oled rounded-sm p-2.5 border border-oled-border leading-relaxed break-words">
                      {p.prompt}
                    </p>
                  </div>
                ))}
              </div>
            </div>
          )}

          {tab === "mcp" && (
            <div className="space-y-4 pb-2">
              <div className="rounded-sm border border-border bg-card px-3.5 py-2.5 text-xs flex items-center gap-2">
                <Bot className="size-4 text-primary shrink-0" />
                <span className="font-display font-bold text-foreground truncate">
                  {tx("npx neuralrig-mcp 다중 AI 연동", "Connect Any AI via npx neuralrig-mcp")}
                </span>
                <HelpHint
                  content={tx(
                    "아래에서 AI 에이전트를 선택하면 설정 파일 자동 등록 명령어와 JSON/TOML 스니펫을 복사할 수 있습니다.",
                    "Select an AI client below to copy the auto-install CLI command or manual JSON/TOML config."
                  )}
                />
              </div>

              <div className="flex flex-wrap gap-1.5">
                {AI_PROVIDERS.map((c) => (
                  <Button
                    key={c.id}
                    size="xs"
                    variant={selectedClient === c.id ? "signal" : "keycap"}
                    onClick={() => setSelectedClient(c.id)}
                  >
                    {c.label}
                  </Button>
                ))}
              </div>

              <div className="rounded-sm border border-border bg-card p-3 space-y-2 min-w-0">
                <div className="flex items-center justify-between gap-2 text-[11px]">
                  <span className="flex items-center gap-1.5 font-mono font-bold text-primary min-w-0">
                    <Terminal className="size-3.5 shrink-0" />
                    <span className="truncate">{tx("자동 설치 명령어", "AUTO-CONFIGURE COMMAND")}</span>
                  </span>
                  <Button
                    size="xs"
                    variant="keycap"
                    className="shrink-0"
                    onClick={() => void copyText("mcp-cmd", installCmd, "npx command")}
                  >
                    {copiedId === "mcp-cmd" ? <Check className="size-3 text-signal-ok" /> : <Copy className="size-3" />}
                    <span>{tx("복사", "COPY")}</span>
                  </Button>
                </div>
                <pre className="text-xs font-mono text-oled-foreground bg-oled border border-oled-border rounded-sm p-2.5 overflow-x-auto">
                  {installCmd}
                </pre>
                <div className="text-[10px] text-muted-foreground font-mono truncate">
                  Target: {AI_PROVIDERS.find((x) => x.id === selectedClient)?.path}
                </div>
              </div>

              <div className="rounded-sm border border-border bg-card p-3 space-y-2 min-w-0">
                <div className="flex items-center justify-between gap-2 text-[11px]">
                  <span className="flex items-center gap-1.5 font-mono font-bold text-foreground min-w-0">
                    <FileCode2 className="size-3.5 text-signal-ok shrink-0" />
                    <span className="truncate">{tx("수동 설정 스니펫", "MANUAL CONFIG SNIPPET")}</span>
                  </span>
                  <Button
                    size="xs"
                    variant="keycap"
                    className="shrink-0"
                    onClick={() => void copyText("mcp-json", snippetText, "config snippet")}
                  >
                    {copiedId === "mcp-json" ? <Check className="size-3 text-signal-ok" /> : <Copy className="size-3" />}
                    <span>{tx("복사", "COPY")}</span>
                  </Button>
                </div>
                <pre className="text-[11px] font-mono text-signal-ok bg-oled border border-oled-border rounded-sm p-2.5 overflow-x-auto">
                  {snippetText}
                </pre>
              </div>

              <div className="rounded-sm border border-oled-border bg-oled p-3 text-[11px] font-mono text-oled-muted">
                <div># {tx("연동 상태 진단 / 확인", "Diagnostics")}</div>
                <div className="text-oled-foreground mt-0.5">npx -y neuralrig-mcp doctor</div>
              </div>
            </div>
          )}

          {tab === "library" && (
            <div className="space-y-4 pb-2">
              <div className="rounded-sm border border-border bg-card px-3.5 py-2.5 text-xs flex items-center gap-2">
                <Cpu className="size-3.5 text-primary shrink-0" />
                <span className="font-display font-bold text-foreground truncate">
                  {tx(".wav IR · .nam · .nrig 드래그 앤 드롭 가져오기", "Drag & Drop .wav IR / .nam / .nrig")}
                </span>
                <HelpHint
                  content={tx(
                    "서드파티 .wav(최대 16,384-tap), .nam 모델, .nrig 번들을 스튜디오 창 위로 끌어다 놓으면 즉시 로드됩니다.",
                    "Drop any .wav cab IR (up to 16,384 taps), .nam model, or .nrig bundle onto the Studio window."
                  )}
                />
              </div>

              <div>
                <div className="font-display text-xs font-bold text-foreground mb-2">
                  {tx(`등록된 커스텀 기어 (${customDevices.length})`, `Registered Custom Gear (${customDevices.length})`)}
                </div>
                {customDevices.length === 0 ? (
                  <div className="rounded-sm border border-dashed border-border bg-card p-6 text-center text-xs text-muted-foreground">
                    {tx("등록된 커스텀 기어가 없습니다.", "No custom SPICE/Cab gear registered yet.")}
                  </div>
                ) : (
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                    {customDevices.map((d) => (
                      <div key={d.id} className="rounded-sm border border-border bg-card p-3 space-y-1 min-w-0 overflow-hidden">
                        <div className="flex items-center justify-between gap-2 min-w-0">
                          <div className="flex items-center gap-1.5 min-w-0 flex-1">
                            <span className="font-display text-xs font-bold text-foreground truncate">
                              {d.originalName || d.name}
                            </span>
                            <HelpHint content={d.schematicSummary} />
                          </div>
                          <Badge variant="hot" size="xs" className="uppercase">
                            {d.category}
                          </Badge>
                        </div>
                        <div className="text-[10px] text-muted-foreground font-mono truncate">{d.id}</div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          )}
        </ScrollArea>
      </DialogContent>
    </Dialog>
  )
}
