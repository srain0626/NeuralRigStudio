import * as React from "react"
import { Slot } from "@radix-ui/react-slot"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

const buttonVariants = cva(
  "inline-flex items-center justify-center gap-1.5 whitespace-nowrap rounded-[8px] text-xs font-medium transition-[color,background-color,border-color,transform,box-shadow] disabled:pointer-events-none disabled:opacity-45 [&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-3.5 shrink-0 [&_svg]:shrink-0 outline-none focus-visible:border-ring focus-visible:ring-ring/40 focus-visible:ring-2 cursor-pointer",
  {
    variants: {
      variant: {
        default:
          "bg-foreground text-background border border-foreground hover:-translate-y-px hover:shadow-[0_8px_20px_rgba(11,14,16,0.16)] active:translate-y-0",
        destructive:
          "bg-destructive text-destructive-foreground border border-destructive hover:opacity-90 active:translate-y-px",
        outline:
          "border border-border bg-card/85 text-foreground hover:bg-card hover:border-foreground/25",
        secondary:
          "bg-secondary text-secondary-foreground border border-border hover:bg-card active:translate-y-px",
        ghost:
          "text-muted-foreground hover:bg-card/70 hover:text-foreground",
        link: "text-primary underline-offset-4 hover:underline",
        signal:
          "bg-primary text-primary-foreground border border-primary font-medium tracking-[-0.01em] hover:-translate-y-px hover:shadow-[0_8px_20px_rgba(255,107,36,0.3)] active:translate-y-0",
        keycap:
          "border border-border bg-card text-foreground font-mono font-medium hover:bg-secondary active:translate-y-px",
        oled:
          "border border-oled-border bg-oled-elevated text-oled-foreground font-mono hover:border-oled-foreground/40 active:translate-y-px",
      },
      size: {
        default: "h-8 px-3.5 py-1.5",
        sm: "h-7 px-3 text-xs",
        xs: "h-6 px-2 text-[11px] gap-1",
        lg: "h-9 px-4.5 text-[13px]",
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
