import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

const badgeVariants = cva(
  "inline-flex items-center justify-center rounded-[6px] border font-medium w-fit whitespace-nowrap shrink-0 gap-1 transition-colors",
  {
    variants: {
      variant: {
        default: "border-foreground bg-foreground text-background",
        secondary: "border-border bg-secondary text-secondary-foreground",
        destructive: "border-destructive bg-destructive text-destructive-foreground",
        outline: "border-border bg-transparent text-foreground",
        ok: "border-signal-ok/35 bg-signal-ok/12 text-signal-ok font-mono",
        warn: "border-signal-warn/45 bg-signal-warn/15 text-signal-warn font-mono",
        hot: "border-signal-hot/40 bg-signal-hot/15 text-signal-hot font-mono",
        oled: "border-oled-border bg-oled text-oled-foreground font-mono",
      },
      size: {
        default: "px-2 py-0.5 text-[11px]",
        xs: "px-1.5 py-0 text-[10px] leading-4 font-mono",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

function Badge({
  className,
  variant,
  size,
  ...props
}: React.ComponentProps<"span"> & VariantProps<typeof badgeVariants>) {
  return (
    <span
      data-slot="badge"
      className={cn(badgeVariants({ variant, size }), className)}
      {...props}
    />
  )
}

export { Badge, badgeVariants }
