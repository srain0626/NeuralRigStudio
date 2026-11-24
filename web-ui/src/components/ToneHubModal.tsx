import React, { useEffect, useMemo, useState } from "react"
import {
  CheckCircle2,
  Cloud,
  Cpu,
  Download,
  ExternalLink,
  FolderUp,
  Globe,
  HardDriveDownload,
  Layers,
  Package,
  Search,
  Sparkles,
  Star,
  Upload,
  Waves,
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
import type {
  CloudStateDto,
  CommunityGearItemDto,
  CustomDeviceSpecDto,
  DawPluginInspectorDto,
  ToneHubItemDto,
} from "@/types/rig"

interface ToneHubModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  cloudState?: CloudStateDto
  customDevices?: CustomDeviceSpecDto[]
  onRequestLogin?: () => void
  onLog: (msg: string, level?: "info" | "success" | "error") => void
}

type HubTab = "all" | "nam" | "ir" | "nrig" | "community" | "daw"

export const ToneHubModal: React.FC<ToneHubModalProps> = ({
  open,
  onOpenChange,
  cloudState,
  customDevices = [],
  onRequestLogin,
  onLog,
}) => {
  const { tx } = useI18n()
  const [tab, setTab] = useState<HubTab>("all")
  const [search, setSearch] = useState("")
  const [urlInput, setUrlInput] = useState("")
  const [busyId, setBusyId] = useState<string | null>(null)
  const [items, setItems] = useState<ToneHubItemDto[]>([])
  const [dawInfo, setDawInfo] = useState<DawPluginInspectorDto | null>(null)
  const [exportStatus, setExportStatus] = useState<string | null>(null)
  const [communityItems, setCommunityItems] = useState<CommunityGearItemDto[]>([])
  const [communitySource, setCommunitySource] = useState("local-offline")
  const [communityCat, setCommunityCat] = useState("all")
  const [uploadDeviceId, setUploadDeviceId] = useState("")
  const [uploadSummary, setUploadSummary] = useState("")

  const refreshCommunityHub = async (cat = communityCat, q = search) => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{ items: CommunityGearItemDto[]; source: string }>("getCommunityHub", {
        category: cat,
        query: q,
        sort: "downloads",
      })
      setCommunityItems(res?.items || [])
      setCommunitySource(res?.source || "local-offline")
    } catch {
      // Ignore transient offline errors
    }
  }

  const refreshCatalog = async () => {
    if (!studioBridge.isWebView2) return
    try {
      const res = await studioBridge.invoke<{
        items: ToneHubItemDto[]
        dawPlugin: DawPluginInspectorDto
      }>("getToneHubCatalog")
      if (res?.items) setItems(res.items)
      if (res?.dawPlugin) setDawInfo(res.dawPlugin)
    } catch (e) {
      onLog(tx(`Tone Hub 조회 실패: ${String(e)}`, `Tone Hub error: ${String(e)}`), "error")
    }
  }

  useEffect(() => {
    if (open) {
      setExportStatus(null)
      void refreshCatalog()
      void refreshCommunityHub()
      if (!uploadDeviceId && customDevices.length > 0) {
        setUploadDeviceId(customDevices[0].id)
      }
    }
  }, [open, customDevices.length])

  useEffect(() => {
    if (open && tab === "community") {
      void refreshCommunityHub(communityCat, search)
    }
  }, [tab, communityCat, search])

  const handleInstallCommunityGear = async (item: CommunityGearItemDto) => {
    if (!studioBridge.isWebView2 || busyId) return
    setBusyId(`hub_${item.id}`)
    try {
      await studioBridge.invoke("installCommunityGear", item)
      toast.success(tx(`'${item.originalName || item.name}' 설치 및 SPICE 검증 완료`, `Installed '${item.originalName || item.name}'`))
      onLog(`[Community Hub] Installed '${item.originalName || item.name}'`, "success")
    } catch (e) {
      const msg = String(e)
      onLog(msg, "error")
      toast.error(msg)
    } finally {
      setBusyId(null)
    }
  }

  const handleUploadCommunityGear = async () => {
    if (!studioBridge.isWebView2 || !uploadDeviceId || busyId) return
    setBusyId("__upload_hub__")
    try {
      const res = await studioBridge.invoke<{ message: string }>("uploadCommunityGear", {
        deviceId: uploadDeviceId,
        summary: uploadSummary.trim(),
        tags: ["SPICE", "Community"],
      })
      toast.success(res.message)
      onLog(res.message, "success")
      setUploadSummary("")
      await refreshCommunityHub()
    } catch (e) {
      const msg = String(e)
      toast.error(msg)
      onLog(msg, "error")
    } finally {
      setBusyId(null)
    }
  }

  const filteredItems = useMemo(() => {
    const q = search.trim().toLowerCase()
    return items.filter((it) => {
      if (tab !== "all" && tab !== "daw" && it.category !== tab) return false
      if (!q) return true
      return (
        it.title.toLowerCase().includes(q) ||
        it.architecture.toLowerCase().includes(q) ||
        it.genre.toLowerCase().includes(q) ||
        it.description.toLowerCase().includes(q) ||
        it.tags.some((t) => t.toLowerCase().includes(q))
      )
    })
  }, [items, tab, search])

  const handleInstallItem = async (id: string) => {
    if (!studioBridge.isWebView2 || busyId) return
    setBusyId(id)
    try {
      const res = await studioBridge.invoke<{
        summary?: string
        catalog?: ToneHubItemDto[]
      }>("installToneHubItem", { id })
      if (res?.catalog) setItems(res.catalog)
      if (res?.summary) onLog(res.summary, "success")
    } catch (e) {
      onLog(tx(`설치 실패: ${String(e)}`, `Install failed: ${String(e)}`), "error")
    } finally {
      setBusyId(null)
    }
  }

  const handleInstallUrl = async () => {
    const url = urlInput.trim()
    if (!url || !studioBridge.isWebView2 || busyId) return
    setBusyId("__url__")
    try {
      const res = await studioBridge.invoke<{ summary?: string }>("installToneHubUrl", { url })
      onLog(res?.summary || `Installed: ${url}`, "success")
      setUrlInput("")
      await refreshCatalog()
    } catch (e) {
      onLog(tx(`URL 다운로드 실패: ${String(e)}`, `URL download failed: ${String(e)}`), "error")
    } finally {
      setBusyId(null)
    }
  }

  const handleExportNrig = async () => {
    if (!studioBridge.isWebView2 || busyId) return
    setBusyId("__export_nrig__")
    try {
      const res = await studioBridge.invoke<{ cancelled?: boolean; path?: string; name?: string }>(
        "exportNrigBundle"
      )
      if (!res?.cancelled && res?.path) {
        onLog(`[.nrig] '${res.name}' → ${res.path}`, "success")
      }
    } catch (e) {
      onLog(`.nrig export failed: ${String(e)}`, "error")
    } finally {
      setBusyId(null)
    }
  }

  const handleImportNrig = async () => {
    if (!studioBridge.isWebView2 || busyId) return
    setBusyId("__import_nrig__")
    try {
      const res = await studioBridge.invoke<{ cancelled?: boolean; summary?: string }>(
        "importNrigBundle"
      )
      if (!res?.cancelled && res?.summary) {
        onLog(res.summary, "success")
        await refreshCatalog()
      }
    } catch (e) {
      onLog(`.nrig import failed: ${String(e)}`, "error")
    } finally {
      setBusyId(null)
    }
  }

  const handleExportDawPlugin = async () => {
    if (!studioBridge.isWebView2 || busyId) return
    setBusyId("__export_daw__")
    try {
      const res = await studioBridge.invoke<{
        cancelled?: boolean
        outputDirectory?: string
        parameterCount?: number
      }>("exportDawPlugin")
      if (!res?.cancelled && res?.outputDirectory) {
        const msg = `VST3 & CLAP (${res.parameterCount} Params) → ${res.outputDirectory}`
        setExportStatus(msg)
        onLog(`[DAW] ${msg}`, "success")
      }
    } catch (e) {
      onLog(tx(`DAW 플러그인 내보내기 실패: ${String(e)}`, `DAW export failed: ${String(e)}`), "error")
    } finally {
      setBusyId(null)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl h-[82vh] flex flex-col p-0 overflow-hidden bg-card border-border">
        <DialogHeader className="px-5 py-3.5 border-b border-border bg-secondary/45 shrink-0">
          <div className="flex flex-wrap items-center justify-between gap-2 pr-8">
            <div className="flex items-center gap-2.5">
              <div className="size-8 rounded-sm bg-oled border border-oled-border flex items-center justify-center text-primary">
                <Globe className="size-4" />
              </div>
              <div>
                <DialogTitle>TONE HUB · .NRIG BUNDLES · VST3 / CLAP</DialogTitle>
                <DialogDescription>
                  {tx(
                    "WaveNet/LSTM/ConvNet 모델, 8K IR, .nrig 아티스트 번들 및 DAW 플러그인 패키징",
                    "Curated WaveNet/LSTM/ConvNet models, 8K IRs, .nrig bundles, and NativeAOT DAW export."
                  )}
                </DialogDescription>
              </div>
            </div>

            <div className="flex items-center gap-1.5">
              <Button
                size="xs"
                variant="keycap"
                disabled={busyId !== null}
                onClick={handleImportNrig}
              >
                <FolderUp className="size-3.5" />
                {tx(".nrig 불러오기", "IMPORT .NRIG")}
              </Button>
              <Button
                size="xs"
                variant="signal"
                disabled={busyId !== null}
                onClick={handleExportNrig}
              >
                <Package className="size-3.5" />
                {tx("현재 리그 .nrig 저장", "EXPORT .NRIG")}
              </Button>
            </div>
          </div>
        </DialogHeader>

        {/* Sub-nav + Search */}
        <div className="px-5 py-2.5 border-b border-border bg-card flex flex-wrap items-center justify-between gap-2 shrink-0">
          <Tabs value={tab} onValueChange={(v) => setTab(v as HubTab)}>
            <TabsList>
              <TabsTrigger value="all">ALL ({items.length})</TabsTrigger>
              <TabsTrigger value="nam">NAM</TabsTrigger>
              <TabsTrigger value="ir">8K IR</TabsTrigger>
              <TabsTrigger value="nrig">.NRIG</TabsTrigger>
              <TabsTrigger value="community">{tx("SPICE 기어 허브", "GEAR HUB")}</TabsTrigger>
              <TabsTrigger value="daw">VST3 / CLAP</TabsTrigger>
            </TabsList>
          </Tabs>

          {tab !== "daw" && (
            <div className="relative w-64">
              <Search className="size-3.5 text-muted-foreground absolute left-2.5 top-1/2 -translate-y-1/2 pointer-events-none" />
              <Input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={tx("모델, 장르, WaveNet/LSTM 검색…", "Search models, genre, arch…")}
                className="pl-8"
              />
            </div>
          )}
        </div>

        {/* Direct URL Installer */}
        {tab !== "daw" && tab !== "community" && (
          <div className="px-5 py-2 border-b border-border bg-background flex items-center gap-2 shrink-0">
            <ExternalLink className="size-3.5 text-muted-foreground shrink-0" />
            <Input
              value={urlInput}
              onChange={(e) => setUrlInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") void handleInstallUrl()
              }}
              placeholder={tx(
                "직접 링크 설치 (https://.../*.nam · *.wav · *.nrig)",
                "Direct URL install (https://.../*.nam · *.wav · *.nrig)"
              )}
              className="flex-1"
            />
            <Button
              size="sm"
              variant="keycap"
              disabled={!urlInput.trim() || busyId !== null}
              onClick={() => void handleInstallUrl()}
            >
              <Download className="size-3.5" />
              {busyId === "__url__" ? tx("설치 중…", "FETCHING…") : tx("URL 설치", "INSTALL")}
            </Button>
          </div>
        )}

        <ScrollArea className="flex-1 min-h-0 p-5 bg-background">
          {tab === "community" ? (
            <div className="space-y-4">
              {/* Upload User Created Custom SPICE/Cab Gear */}
              <div className="rounded-sm border border-border bg-card p-3.5 space-y-2.5">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex items-center gap-2 text-xs font-semibold">
                    <Cloud className="size-4 text-primary" />
                    <span>{tx("내가 생성한 페달 · 앰프 · 캐비넷 커뮤니티 업로드", "Publish Your Custom SPICE / Cab Creation")}</span>
                    <Badge variant="secondary" size="xs">{communitySource === "cloud" ? "CLOUD LIVE" : "LOCAL CATALOG"}</Badge>
                  </div>
                  {!cloudState?.isAuthenticated && onRequestLogin && (
                    <Button size="xs" variant="keycap" onClick={() => { onOpenChange(false); onRequestLogin() }}>
                      {tx("Google 로그인하여 게시", "Sign in to Publish")}
                    </Button>
                  )}
                </div>

                {customDevices.length > 0 ? (
                  <div className="flex flex-wrap items-center gap-2">
                    <select
                      value={uploadDeviceId}
                      onChange={(e) => setUploadDeviceId(e.target.value)}
                      className="h-8 rounded-sm border border-border bg-background px-2 text-xs"
                    >
                      {customDevices.map((d) => (
                        <option key={d.id} value={d.id}>
                          [{d.category.toUpperCase()}] {d.originalName || d.name}
                        </option>
                      ))}
                    </select>
                    <Input
                      value={uploadSummary}
                      onChange={(e) => setUploadSummary(e.target.value)}
                      placeholder={tx("회로 설명 / 톤 팁 (선택)", "Circuit notes / tone summary (optional)")}
                      className="flex-1 min-w-[200px] h-8 text-xs"
                    />
                    <Button
                      size="xs"
                      variant="signal"
                      disabled={!cloudState?.isAuthenticated || !uploadDeviceId || busyId !== null}
                      onClick={() => void handleUploadCommunityGear()}
                      className="h-8 px-3 gap-1.5"
                    >
                      <Upload className="size-3.5" />
                      {tx("허브 업로드", "PUBLISH")}
                    </Button>
                  </div>
                ) : (
                  <div className="text-[11px] text-muted-foreground">
                    {tx(
                      "아직 로컬에 생성된 커스텀 SPICE/캐비넷 기어가 없습니다. 상단 CRAFT 가이드로 새 페달·앰프를 추가한 뒤 공유하세요.",
                      "Create a custom SPICE pedal, amp, or cab via the CRAFT MCP guide first, then share it here."
                    )}
                  </div>
                )}
              </div>

              {/* Filter */}
              <div className="flex items-center gap-1.5">
                {(["all", "drive", "amp", "cab"] as const).map((cat) => (
                  <Button
                    key={cat}
                    size="xs"
                    variant={communityCat === cat ? "signal" : "keycap"}
                    onClick={() => setCommunityCat(cat)}
                  >
                    {cat.toUpperCase()}
                  </Button>
                ))}
              </div>

              {/* Community Gear List */}
              {communityItems.length === 0 ? (
                <div className="rounded-sm border border-dashed border-border p-8 text-center text-xs text-muted-foreground">
                  {tx("표시할 커뮤니티 기어가 없습니다. 검색어를 비우거나 Google 로그인 후 새 기어를 올려보세요.", "No matching community gear found.")}
                </div>
              ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  {communityItems.map((it) => (
                    <div key={it.id} className="rounded-sm border border-border bg-card p-3.5 flex flex-col justify-between gap-2.5">
                      <div className="space-y-1">
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-display text-xs font-bold truncate">{it.originalName || it.name}</span>
                          <Badge variant="secondary" size="xs">{it.category.toUpperCase()}</Badge>
                        </div>
                        <div className="text-[10px] font-mono text-muted-foreground">
                          by {it.ownerName} · {it.downloads} downloads
                        </div>
                        <p className="text-[11px] text-muted-foreground line-clamp-2">{it.summary}</p>
                      </div>
                      <div className="flex items-center justify-between pt-2 border-t border-border">
                        <span className="text-[10px] font-mono text-muted-foreground truncate max-w-[180px]">
                          {it.schematicSourceUrl || "MNA SPICE Verified"}
                        </span>
                        <Button
                          size="xs"
                          variant="signal"
                          disabled={busyId !== null}
                          onClick={() => void handleInstallCommunityGear(it)}
                        >
                          <Download className="size-3" />
                          {busyId === `hub_${it.id}` ? tx("검증 중…", "VERIFYING…") : tx("설치 · SPICE 검증", "INSTALL")}
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          ) : tab === "daw" ? (
            <div className="space-y-4">
              <div className="rounded-sm border border-border bg-card p-4 flex flex-wrap items-center justify-between gap-4">
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <Cpu className="size-4 text-primary" />
                    <span className="font-display text-sm font-bold">
                      {dawInfo?.pluginName || "NeuralRig Studio"} DAW Plugin (NativeAOT C-ABI)
                    </span>
                    <Badge variant="ok" size="xs">
                      CLAP v1.2 + VST3 v3.7
                    </Badge>
                  </div>
                  <p className="text-xs text-muted-foreground">
                    {tx(
                      "무할당 DSP + .nrig 직렬화 + 32개 샘플 정확도 오토메이션 + 호스트 BPM 동기화",
                      "0-allocation DSP + .nrig state + 32 sample-accurate parameters + host BPM sync."
                    )}
                  </p>
                  <div className="text-[10px] font-mono text-muted-foreground pt-1 space-y-0.5">
                    <div>CLAP ID: {dawInfo?.clapId || "com.neuralrig.studio.vstclap"}</div>
                    <div>VST3 CID: {dawInfo?.vst3ProcessorCid || "A73E91C452D84B6E91F0C38E4B72D101"}</div>
                    <div>
                      NativeAOT: {dawInfo?.nativeAotCommand || "dotnet publish src/NeuralRig.Plugin/NeuralRig.Plugin.csproj -c Release -r win-x64 -p:NativeLib=Shared"}
                    </div>
                  </div>
                </div>

                <Button
                  size="lg"
                  variant="signal"
                  disabled={busyId !== null}
                  onClick={handleExportDawPlugin}
                >
                  <HardDriveDownload className="size-4" />
                  {busyId === "__export_daw__"
                    ? tx("패키징 중…", "PACKAGING…")
                    : tx("VST3 & CLAP 내보내기", "EXPORT VST3 & CLAP")}
                </Button>
              </div>

              {exportStatus && (
                <div className="px-3.5 py-2.5 rounded-sm border border-signal-ok/50 bg-signal-ok/10 text-xs text-signal-ok flex items-center gap-2 font-mono">
                  <CheckCircle2 className="size-4 shrink-0" />
                  <span>{exportStatus}</span>
                </div>
              )}

              <div className="rounded-sm border border-border bg-card overflow-hidden">
                <div className="px-4 py-2 border-b border-border bg-secondary/45 flex items-center justify-between">
                  <span className="font-mono text-[11px] font-bold uppercase tracking-wider">
                    AUTOMATION PARAMETER MAP ({dawInfo?.parameters?.length ?? 32})
                  </span>
                  <span className="font-mono text-[10px] text-muted-foreground">
                    0 B RT HEAP ALLOC
                  </span>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 divide-y md:divide-y-0 md:gap-px bg-border text-xs">
                  {(dawInfo?.parameters ?? []).map((p) => (
                    <div
                      key={p.id}
                      className="px-3.5 py-1.5 bg-card flex items-center justify-between font-mono text-[11px]"
                    >
                      <div className="flex items-center gap-2 min-w-0">
                        <span className="text-muted-foreground w-6 tabular-nums">#{p.id}</span>
                        <span className="font-sans font-semibold truncate">{p.name}</span>
                        <Badge variant="secondary" size="xs">
                          {p.module}
                        </Badge>
                      </div>
                      <span className="text-[10px] text-muted-foreground shrink-0 tabular-nums">
                        {p.min}..{p.max} {p.unit}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
              {filteredItems.map((item) => {
                const isNam = item.category === "nam"
                const isIr = item.category === "ir"
                return (
                  <div
                    key={item.id}
                    className="rounded-sm border border-border bg-card p-3.5 flex flex-col justify-between gap-3"
                  >
                    <div className="space-y-1.5">
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex items-center gap-2 min-w-0">
                          {isNam ? (
                            <Sparkles className="size-4 text-signal-warn shrink-0" />
                          ) : isIr ? (
                            <Waves className="size-4 text-signal-ok shrink-0" />
                          ) : (
                            <Layers className="size-4 text-primary shrink-0" />
                          )}
                          <span className="font-display text-xs font-bold truncate">
                            {item.title}
                          </span>
                        </div>
                        <Badge variant={isNam ? "warn" : isIr ? "ok" : "hot"} size="xs">
                          {item.architecture}
                        </Badge>
                      </div>

                      <div className="text-[10px] text-muted-foreground flex items-center gap-2 font-mono tabular-nums">
                        <span>{item.genre}</span>
                        <span>·</span>
                        <span>{item.sampleRate / 1000}kHz</span>
                        {item.esrDb < 0 && (
                          <>
                            <span>·</span>
                            <span>ESR {item.esrDb.toFixed(1)}dB</span>
                          </>
                        )}
                        {item.taps > 0 && (
                          <>
                            <span>·</span>
                            <span>{item.taps} taps</span>
                          </>
                        )}
                      </div>

                      <p className="text-[11px] text-muted-foreground leading-relaxed">
                        {item.description}
                      </p>

                      <div className="flex flex-wrap gap-1 pt-0.5">
                        {item.tags.map((tg) => (
                          <Badge key={tg} variant="secondary" size="xs">
                            {tg}
                          </Badge>
                        ))}
                      </div>
                    </div>

                    <div className="flex items-center justify-between pt-2 border-t border-border">
                      <div className="flex items-center gap-2 text-[10px] font-mono tabular-nums text-muted-foreground">
                        <span className="flex items-center gap-0.5 text-signal-warn">
                          <Star className="size-3 fill-current" />
                          {item.rating.toFixed(1)}
                        </span>
                        <span>·</span>
                        <span>{item.downloads.toLocaleString()}</span>
                      </div>

                      <Button
                        size="xs"
                        variant={item.isInstalled ? "keycap" : "signal"}
                        disabled={busyId !== null}
                        onClick={() => void handleInstallItem(item.id)}
                      >
                        {item.isInstalled ? (
                          <>
                            <CheckCircle2 className="size-3 text-signal-ok" />
                            {tx("다시 적용", "RE-APPLY")}
                          </>
                        ) : (
                          <>
                            <Download className="size-3" />
                            {busyId === item.id ? tx("설치 중…", "INSTALLING…") : tx("설치 및 적용", "INSTALL")}
                          </>
                        )}
                      </Button>
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </ScrollArea>
      </DialogContent>
    </Dialog>
  )
}
