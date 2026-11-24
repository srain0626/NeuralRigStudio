import * as React from "react"
import * as SwitchPrimitive from "@radix-ui/react-switch"
import { cn } from "@/lib/utils"

function Switch({
  className,
  ...props
}: React.ComponentProps<typeof SwitchPrimitive.Root>) {
  return (
    <SwitchPrimitive.Root
      data-slot="switch"
      className={cn(
        "peer data-[state=checked]:bg-primary data-[state=unchecked]:bg-secondary inline-flex h-5 w-9 shrink-0 items-center rounded-sm border border-border transition-colors outline-none focus-visible:ring-ring/40 focus-visible:ring-2 disabled:cursor-not-allowed disabled:opacity-45 cursor-pointer px-0.5",
        className
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb
        data-slot="switch-thumb"
        className={cn(
          "bg-card border border-border pointer-events-none block size-3.5 rounded-[2px] ring-0 transition-transform data-[state=checked]:translate-x-4 data-[state=checked]:border-primary-foreground data-[state=unchecked]:translate-x-0"
        )}
      />
    </SwitchPrimitive.Root>
  )
}

export { Switch }
