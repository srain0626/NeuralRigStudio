import * as React from "react"
import { Slot } from "@radix-ui/react-slot"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-1.5 whitespace-nowrap rounded-md text-xs font-medium transition-[color,background-color,border-color,transform] disabled:pointer-events-none disabled:opacity-45 [&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-3.5 shrink-0 [&_svg]:shrink-0 outline-none focus-visible:border-ring focus-visible:ring-ring/40 focus-visible:ring-2 cursor-pointer",
  {
    variants: {
      variant: {
        default:
          "bg-primary text-primary-foreground border border-primary hover:opacity-90 active:translate-y-px",
        destructive:
          "bg-destructive text-destructive-foreground border border-destructive hover:opacity-90 active:translate-y-px",
        outline:
          "border border-border bg-card text-foreground hover:bg-accent hover:text-accent-foreground",
        secondary:
          "bg-secondary text-secondary-foreground border border-border hover:bg-accent active:translate-y-px",
        ghost:
          "text-muted-foreground hover:bg-accent hover:text-accent-foreground",
        link: "text-primary underline-offset-4 hover:underline",
        signal:
          "bg-signal-hot text-primary-foreground border border-signal-hot font-mono font-bold tracking-wide hover:opacity-90 active:translate-y-px",
        keycap:
          "border border-border bg-secondary text-secondary-foreground font-mono font-semibold shadow-[0_1px_0_var(--border)] hover:bg-accent active:translate-y-px active:shadow-none",
        oled:
          "border border-oled-border bg-oled-elevated text-oled-foreground font-mono hover:border-oled-foreground/45 active:translate-y-px",
      },
      size: {
        default: "h-8 px-3 py-1.5",
        sm: "h-7 px-2.5 text-xs",
        xs: "h-6 px-2 text-[11px] gap-1",
        lg: "h-9 px-4 text-sm",
        icon: "size-8",
        "icon-sm": "size-7",
        "icon-xs": "size-6",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

function Button({
  className,
  variant,
  size,
  asChild = false,
  ...props
}: React.ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean
  }) {
  const Comp = asChild ? Slot : "button"
  return (
    <Comp
      data-slot="button"
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    />
  )
}

export { Button, buttonVariants }
