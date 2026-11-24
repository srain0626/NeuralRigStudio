import * as React from "react"
import * as TogglePrimitive from "@radix-ui/react-toggle"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

const toggleVariants = cva(
  "inline-flex items-center justify-center gap-1 rounded-sm text-xs font-mono font-semibold whitespace-nowrap transition-[color,background-color,border-color] disabled:pointer-events-none disabled:opacity-40 [&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-3.5 shrink-0 outline-none focus-visible:ring-2 focus-visible:ring-ring/40 cursor-pointer",
  {
    variants: {
      variant: {
        default:
          "border border-border bg-secondary/70 text-muted-foreground hover:bg-accent hover:text-foreground data-[state=on]:bg-primary data-[state=on]:border-primary data-[state=on]:text-primary-foreground",
        outline:
          "border border-border bg-transparent text-muted-foreground hover:bg-accent hover:text-foreground data-[state=on]:bg-foreground data-[state=on]:border-foreground data-[state=on]:text-background",
        keycap:
          "border border-border bg-secondary text-muted-foreground shadow-[0_1px_0_var(--border)] hover:text-foreground data-[state=on]:bg-primary data-[state=on]:border-primary data-[state=on]:text-primary-foreground data-[state=on]:shadow-none",
        oled:
          "border border-oled-border bg-oled-elevated text-oled-muted hover:text-oled-foreground data-[state=on]:bg-primary data-[state=on]:border-primary data-[state=on]:text-primary-foreground",
      },
      size: {
        default: "h-7 px-2.5 min-w-7",
        sm: "h-6 px-2 text-[11px] min-w-6",
        xs: "h-5 px-1.5 text-[10px] min-w-5",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

function Toggle({
  className,
  variant,
  size,
  ...props
}: React.ComponentProps<typeof TogglePrimitive.Root> &
  VariantProps<typeof toggleVariants>) {
  return (
    <TogglePrimitive.Root
      data-slot="toggle"
      className={cn(toggleVariants({ variant, size, className }))}
      {...props}
    />
  )
}

export { Toggle, toggleVariants }
