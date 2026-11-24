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
          "z-[80] max-w-[260px] rounded-[10px] border border-oled-border bg-oled px-3 py-1.5 text-[11.5px] leading-relaxed text-oled-foreground shadow-lg animate-in fade-in-0 zoom-in-95",
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

interface HelpHintProps {
  content?: React.ReactNode
  side?: "top" | "bottom" | "left" | "right"
  className?: string
  ariaLabel?: string
}

function HelpHint({ content, side = "top", className, ariaLabel = "Help" }: HelpHintProps) {
  if (!content) return null
  return (
    <Tooltip delayDuration={80}>
      <TooltipTrigger asChild>
        <span
          role="button"
          tabIndex={0}
          data-slot="help-hint"
          aria-label={ariaLabel}
          onClick={(e) => {
            e.preventDefault()
            e.stopPropagation()
          }}
          onPointerDown={(e) => e.stopPropagation()}
          onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === " ") {
              e.preventDefault()
              e.stopPropagation()
            }
          }}
          className={cn(
            "inline-flex size-4 shrink-0 items-center justify-center rounded-full border border-muted-foreground/35 bg-muted-foreground/12 font-mono text-[10px] font-semibold leading-none text-muted-foreground hover:border-primary/60 hover:bg-primary/15 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 transition-colors duration-200 cursor-help select-none align-middle",
            className
          )}
        >
          ?
        </span>
      </TooltipTrigger>
      <TooltipContent side={side}>{content}</TooltipContent>
    </Tooltip>
  )
}

export { Tooltip, TooltipTrigger, TooltipContent, TooltipProvider, Hint, HelpHint }
