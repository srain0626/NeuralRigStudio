import React from "react"
import { Terminal, Trash2 } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ScrollArea } from "@/components/ui/scroll-area"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet"
import { HelpHint } from "@/components/ui/tooltip"
import { useI18n } from "@/lib/i18n"

export interface StudioLogEntry {
  id: number
  text: string
  level: "info" | "success" | "error"
}

interface ConsoleLogSheetProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  logs: StudioLogEntry[]
  onClearLogs: () => void
}

export const ConsoleLogSheet: React.FC<ConsoleLogSheetProps> = ({
  open,
  onOpenChange,
  logs,
  onClearLogs,
}) => {
  const { tx } = useI18n()

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="max-w-lg p-0">
        <SheetHeader>
          <div className="flex items-center justify-between gap-2 pr-8">
            <div className="min-w-0">
              <SheetTitle className="flex items-center gap-2">
                <Terminal className="size-4 text-primary shrink-0" />
                <span className="truncate">SYSTEM TELEMETRY CONSOLE</span>
                <HelpHint
                  side="bottom"
                  content={tx(
                    "오디오 엔진, MIDI, 프리셋, MCP 회로도 이벤트 기록입니다.",
                    "Real-time audio engine, MIDI, preset, and MCP schematic event log."
                  )}
                />
              </SheetTitle>
              <SheetDescription className="sr-only">
                {tx(
                  "오디오 엔진, MIDI, 프리셋, MCP 회로도 이벤트 기록",
                  "Real-time audio engine, MIDI, preset, and MCP schematic event log."
                )}
              </SheetDescription>
            </div>
            <Badge variant="outline" size="xs">
              {logs.length}
            </Badge>
          </div>
        </SheetHeader>

        <div className="flex-1 min-h-0 p-4 bg-background">
          <ScrollArea className="h-full rounded-sm border border-border bg-oled p-3 font-mono text-[11px]">
            {logs.length === 0 ? (
              <div className="text-oled-muted py-6 text-center">
                {tx("기록된 이벤트가 없습니다.", "No events recorded yet.")}
              </div>
            ) : (
              <div className="space-y-1.5 min-w-0">
                {logs.map((entry) => (
                  <div
                    key={entry.id}
                    className={`break-words overflow-hidden ${
                      entry.level === "error"
                        ? "text-signal-hot"
                        : entry.level === "success"
                          ? "text-signal-ok"
                          : "text-oled-foreground/85"
                    }`}
                  >
                    {entry.text}
                  </div>
                ))}
              </div>
            )}
          </ScrollArea>
        </div>

        <SheetFooter>
          <Button variant="ghost" size="sm" disabled={logs.length === 0} onClick={onClearLogs}>
            <Trash2 className="size-3.5" />
            {tx("기록 비우기", "Clear Logs")}
          </Button>
          <Button variant="keycap" size="sm" onClick={() => onOpenChange(false)}>
            {tx("닫기", "Close")}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  )
}
