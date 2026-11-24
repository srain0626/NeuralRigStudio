import * as React from "react"
import * as TooltipPrimitive from "@radix-ui/react-tooltip"
import { cn } from "@/lib/utils"

function TooltipProvider({
  delayDuration = 160,
  ...props
}: React.ComponentProps<typeof TooltipPrimitive.Provider>) {
  return (
    <TooltipPrimitive.Provider
      data-slot="tooltip-provider"
      delayDuration={delayDuration}
      {...props}
    />
  )
}

function Tooltip(props: React.ComponentProps<typeof TooltipPrimitive.Root>) {
  return <TooltipPrimitive.Root data-slot="tooltip" {...props} />
}

function TooltipTrigger(
  props: React.ComponentProps<typeof TooltipPrimitive.Trigger>
) {
  return <TooltipPrimitive.Trigger data-slot="tooltip-trigger" {...props} />
}

function TooltipContent({
  className,
  sideOffset = 6,
  children,
  ...props
}: React.ComponentProps<typeof TooltipPrimitive.Content>) {
  return (
    <TooltipPrimitive.Portal>
      <TooltipPrimitive.Content
        data-slot="tooltip-content"
        sideOffset={sideOffset}
        className={cn(
          "z-50 max-w-xs rounded-sm border border-oled-border bg-oled px-2.5 py-1.5 text-[11px] text-oled-foreground shadow-md animate-in fade-in-0 zoom-in-95",
          className
        )}
        {...props}
      >
        {children}
      </TooltipPrimitive.Content>
    </TooltipPrimitive.Portal>
  )
}

interface HintProps {
  content: React.ReactNode
  kbd?: string
  side?: "top" | "bottom" | "left" | "right"
  children: React.ReactElement
}

function Hint({ content, kbd, side = "bottom", children }: HintProps) {
  if (!content) return children
  return (
    <Tooltip>
      <TooltipTrigger asChild>{children}</TooltipTrigger>
      <TooltipContent side={side} className="flex items-center gap-2">
        <span className="leading-snug">{content}</span>
        {kbd && (
          <kbd className="rounded-sm border border-oled-border bg-oled-elevated px-1 py-0.5 font-mono text-[10px] font-semibold text-oled-muted">
            {kbd}
          </kbd>
        )}
      </TooltipContent>
    </Tooltip>
  )
}

export { Tooltip, TooltipTrigger, TooltipContent, TooltipProvider, Hint }
