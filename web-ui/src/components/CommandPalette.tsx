import React from "react"
import {
  Activity,
  AudioWaveform,
  BookOpen,
  Bug,
  Cloud,
  Disc3,
  Gauge,
  Globe,
  Guitar,
  Languages,
  Music2,
  Palette,
  Play,
  Shield,
  SlidersHorizontal,
  Sparkles,
  Square,
  Terminal,
  Wrench,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
  CommandShortcut,
} from "@/components/ui/command"
import { CATEGORY_MAP, DEVICE_CATALOG, SCENE_LABELS, getDeviceSearchKeywords } from "@/data/cortexCatalog"
import { useI18n } from "@/lib/i18n"
import { STUDIO_THEMES, useStudioTheme } from "@/lib/theme"
import type { SignalChainConfigDto } from "@/types/rig"

interface CommandPaletteProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  presets: SignalChainConfigDto[]
  activePresetId: string
  activeScene: number
  selectedSlot: number
  isAudioRunning: boolean
  onSelectView: (view: "grid" | "directory" | "scope") => void
  onLoadPreset: (preset: SignalChainConfigDto) => void
  onSelectScene: (sceneIndex: number) => void
  onAssignModel: (modelId: string, slotIdx: number) => void
  onToggleAudio: () => void
  onOpenTuner: () => void
  onOpenGuitar: () => void
  onOpenToneHub: () => void
  onOpenBenchmark: () => void
  onOpenTutorial: () => void
  onOpenGearGuide: () => void
  onOpenAuth: () => void
  onOpenBugReport: () => void
  onOpenUpdates: () => void
  onOpenAdmin?: () => void
  isAdmin?: boolean
  isVst3Host?: boolean
  onOpenAudioSheet: () => void
  onOpenLogsSheet: () => void
}

export const CommandPalette: React.FC<CommandPaletteProps> = ({
  open,
  onOpenChange,
  presets,
  activePresetId,
  activeScene,
  selectedSlot,
  isAudioRunning,
  onSelectView,
  onLoadPreset,
  onSelectScene,
  onAssignModel,
  onToggleAudio,
  onOpenTuner,
  onOpenGuitar,
  onOpenToneHub,
  onOpenBenchmark,
  onOpenTutorial,
  onOpenGearGuide,
  onOpenAuth,
  onOpenBugReport,
  onOpenUpdates,
  onOpenAdmin,
  isAdmin,
  isVst3Host = false,
  onOpenAudioSheet,
  onOpenLogsSheet,
}) => {
  const { tx, toggleLocale, locale } = useI18n()
  const { themeId, setThemeId } = useStudioTheme()

  const runAndClose = (fn: () => void) => {
    fn()
    onOpenChange(false)
  }

  return (
    <CommandDialog
      open={open}
      onOpenChange={onOpenChange}
      title={tx("스튜디오 커맨드 팔레트", "Studio Command Palette")}
      description={tx(
        "장비 모델, 프리셋, 씬, 테마 및 스튜디오 기능을 검색해 즉시 실행합니다.",
        "Search gear models, presets, scenes, themes, and studio tools."
      )}
    >
      <CommandInput
        placeholder={tx(
          "장비(TS808, Plexi), 프리셋, 테마, 기능 검색…",
          "Search gear (TS808, Plexi), presets, themes, actions…"
        )}
      />
      <CommandList>
        <CommandEmpty>{tx("일치하는 항목이 없습니다.", "No matching items found.")}</CommandEmpty>

        <CommandGroup heading={tx("스튜디오 액션", "Studio Actions")}>
          {!isVst3Host && (
            <CommandItem onSelect={() => runAndClose(onToggleAudio)}>
              <span className="flex items-center gap-2 min-w-0 flex-1">
                {isAudioRunning ? <Square className="size-3.5 text-signal-hot shrink-0" /> : <Play className="size-3.5 text-signal-ok shrink-0" />}
                <span className="truncate">{isAudioRunning ? tx("오디오 엔진 중지", "Stop Audio Engine") : tx("오디오 엔진 시작", "Start Audio Engine")}</span>
              </span>
              <CommandShortcut>AUDIO</CommandShortcut>
            </CommandItem>
          )}
          <CommandItem onSelect={() => runAndClose(() => onSelectView("grid"))}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <AudioWaveform className="size-3.5 shrink-0" />
              <span className="truncate">{tx("패치보드 작업실 (RIG)", "Open Patchboard Workspace (RIG)")}</span>
            </span>
            <CommandShortcut>RIG</CommandShortcut>
          </CommandItem>
          <CommandItem onSelect={() => runAndClose(() => onSelectView("directory"))}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Disc3 className="size-3.5 shrink-0" />
              <span className="truncate">{tx("프리셋·NAM 보관함 (VAULT)", "Open Tone Vault (VAULT)")}</span>
            </span>
            <CommandShortcut>VAULT</CommandShortcut>
          </CommandItem>
          <CommandItem onSelect={() => runAndClose(() => onSelectView("scope"))}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Activity className="size-3.5 shrink-0" />
              <span className="truncate">{tx("실시간 스펙트럼 · 기타 분석 (SCOPE)", "Open Live Scope & Analyzer (SCOPE)")}</span>
            </span>
            <CommandShortcut>SCOPE</CommandShortcut>
          </CommandItem>
          <CommandItem onSelect={() => runAndClose(onOpenTuner)}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Music2 className="size-3.5 shrink-0" />
              <span className="truncate">{tx("정밀 스트로브 크로매틱 튜너", "Precision Strobe Tuner")}</span>
            </span>
          </CommandItem>
          <CommandItem onSelect={() => runAndClose(onOpenGuitar)}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Guitar className="size-3.5 shrink-0" />
              <span className="truncate">{tx("내 기타 DI 프로파일러", "Guitar DI Profiler")}</span>
            </span>
          </CommandItem>
          <CommandItem onSelect={() => runAndClose(onOpenToneHub)}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Globe className="size-3.5 shrink-0" />
              <span className="truncate">
                {isVst3Host
                  ? tx("Tone Hub · .nrig 번들 · 커스텀 IR", "Tone Hub · .nrig Bundles · Custom IR")
                  : tx("Tone Hub · .nrig 번들 · VST3/CLAP 내보내기", "Tone Hub · .nrig Bundles · VST3/CLAP")}
              </span>
            </span>
          </CommandItem>
          <CommandItem onSelect={() => runAndClose(onOpenBenchmark)}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Gauge className="size-3.5 shrink-0" />
              <span className="truncate">{tx("하드웨어 유사도 · 지연시간 · 멀티체인 벤치마크", "Hardware Similarity · Latency · Chain Benchmark")}</span>
            </span>
          </CommandItem>
          <CommandItem onSelect={() => runAndClose(onOpenGearGuide)}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Wrench className="size-3.5 shrink-0" />
              <span className="truncate">{tx("새 페달·앰프·캐비넷 제작 & npx MCP 가이드", "Create Custom Gear & npx MCP Guide")}</span>
            </span>
          </CommandItem>
          <CommandItem onSelect={() => runAndClose(onOpenTutorial)}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <BookOpen className="size-3.5 shrink-0" />
              <span className="truncate">{tx("스튜디오 UI 튜토리얼 다시 보기", "Replay Studio Quick Tour Tutorial")}</span>
            </span>
          </CommandItem>
          {!isVst3Host && (
            <CommandItem onSelect={() => runAndClose(onOpenAuth)}>
              <span className="flex items-center gap-2 min-w-0 flex-1">
                <Cloud className="size-3.5 shrink-0" />
                <span className="truncate">{tx("Google 계정 · 클라우드 동기화", "Google Account · Cloud Sync")}</span>
              </span>
            </CommandItem>
          )}
          {!isVst3Host && (
            <CommandItem onSelect={() => runAndClose(onOpenUpdates)}>
              <span className="flex items-center gap-2 min-w-0 flex-1">
                <Sparkles className="size-3.5 shrink-0" />
                <span className="truncate">{tx("OTA 업데이트 확인", "Check OTA Software Updates")}</span>
              </span>
            </CommandItem>
          )}
          <CommandItem onSelect={() => runAndClose(onOpenBugReport)}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Bug className="size-3.5 shrink-0" />
              <span className="truncate">{tx("버그 제보 (자동 익명화 로그 전송)", "Submit Bug Report (Scrubbed Logs)")}</span>
            </span>
          </CommandItem>
          {!isVst3Host && isAdmin && onOpenAdmin && (
            <CommandItem onSelect={() => runAndClose(onOpenAdmin)}>
              <span className="flex items-center gap-2 min-w-0 flex-1">
                <Shield className="size-3.5 text-signal-warn shrink-0" />
                <span className="truncate">{tx("어드민 애널리틱스 · OTA 정책 콘솔", "Admin Analytics · OTA Console")}</span>
              </span>
            </CommandItem>
          )}
          {!isVst3Host && (
            <CommandItem onSelect={() => runAndClose(onOpenAudioSheet)}>
              <span className="flex items-center gap-2 min-w-0 flex-1">
                <SlidersHorizontal className="size-3.5 shrink-0" />
                <span className="truncate">{tx("오디오 I/O & MIDI 패치베이", "Audio I/O & MIDI Patchbay")}</span>
              </span>
            </CommandItem>
          )}
          <CommandItem onSelect={() => runAndClose(onOpenLogsSheet)}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Terminal className="size-3.5 shrink-0" />
              <span className="truncate">{tx("시스템 콘솔 로그", "System Console Logs")}</span>
            </span>
          </CommandItem>
          <CommandItem onSelect={() => runAndClose(toggleLocale)}>
            <span className="flex items-center gap-2 min-w-0 flex-1">
              <Languages className="size-3.5 shrink-0" />
              <span className="truncate">{tx("언어 전환 (한국어 ↔ English)", "Switch Language (KO ↔ EN)")}</span>
            </span>
            <CommandShortcut>{locale.toUpperCase()}</CommandShortcut>
          </CommandItem>
        </CommandGroup>

        <CommandSeparator />

        <CommandGroup heading={tx("스튜디오 섀시 테마", "Studio Chassis Finish")}>
          {STUDIO_THEMES.map((t) => (
            <CommandItem
              key={t.id}
              value={`theme ${t.name} ${t.id}`}
              onSelect={() => runAndClose(() => setThemeId(t.id))}
            >
              <span className="flex items-center gap-2 min-w-0 flex-1">
                <Palette className="size-3.5 shrink-0" />
                <span
                  className="size-3 rounded-sm border border-border shrink-0"
                  style={{ backgroundColor: t.swatchBg }}
                />
                <span className="truncate">{t.name}</span>
              </span>
              {themeId === t.id && (
                <Badge variant="hot" size="xs">
                  ACTIVE
                </Badge>
              )}
            </CommandItem>
          ))}
        </CommandGroup>

        <CommandSeparator />

        <CommandGroup heading={tx("씬 전환 (SCENES A–H)", "Switch Scene (A–H)")}>
          {SCENE_LABELS.map((label, idx) => (
            <CommandItem
              key={label}
              value={`scene ${label}`}
              onSelect={() =>
                runAndClose(() => {
                  onSelectView("grid")
                  onSelectScene(idx)
                })
              }
            >
              <span className="font-mono truncate">SCENE {label}</span>
              <span className="flex items-center gap-2 shrink-0">
                {activeScene === idx && (
                  <Badge variant="ok" size="xs">
                    ON
                  </Badge>
                )}
                <CommandShortcut>{idx + 1}</CommandShortcut>
              </span>
            </CommandItem>
          ))}
        </CommandGroup>

        <CommandSeparator />

        <CommandGroup heading={tx("프리셋 뱅크", "Preset Bank")}>
          {presets.map((p) => (
            <CommandItem
              key={p.presetId}
              value={`preset ${p.presetId} ${p.presetName}`}
              onSelect={() =>
                runAndClose(() => {
                  onLoadPreset(p)
                  onSelectView("grid")
                })
              }
            >
              <span className="flex items-center gap-2 min-w-0 flex-1">
                <span className="font-mono text-muted-foreground shrink-0">{p.presetId}</span>
                <span className="truncate font-medium">{p.presetName}</span>
              </span>
              {activePresetId === p.presetId && (
                <Badge variant="secondary" size="xs">
                  CURRENT
                </Badge>
              )}
            </CommandItem>
          ))}
        </CommandGroup>

        <CommandSeparator />

        <CommandGroup
          heading={tx(
            `장비 카탈로그 → 슬롯 #${String(selectedSlot + 1).padStart(2, "0")}에 장착`,
            `Gear Catalog → Mount to Slot #${String(selectedSlot + 1).padStart(2, "0")}`
          )}
        >
          {DEVICE_CATALOG.map((dev) => {
            const cat = CATEGORY_MAP[dev.category]
            return (
              <CommandItem
                key={dev.id}
                value={getDeviceSearchKeywords(dev)}
                onSelect={() =>
                  runAndClose(() => {
                    onAssignModel(dev.id, selectedSlot)
                    onSelectView("grid")
                  })
                }
              >
                <span className="flex items-center gap-2 min-w-0 flex-1">
                  <span
                    className="w-2 h-2 rounded-xs shrink-0"
                    style={{ backgroundColor: cat?.hex }}
                  />
                  <span className="truncate">{dev.name}</span>
                </span>
                <CommandShortcut>{dev.categoryLabel}</CommandShortcut>
              </CommandItem>
            )
          })}
        </CommandGroup>
      </CommandList>
    </CommandDialog>
  )
}
