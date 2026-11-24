import * as React from "react"
import { cn } from "@/lib/utils"

function Kbd({ className, ...props }: React.ComponentProps<"kbd">) {
  return (
    <kbd
      data-slot="kbd"
      className={cn(
        "inline-flex h-4 min-w-4 items-center justify-center rounded-sm border border-border bg-secondary px-1 font-mono text-[10px] font-semibold text-muted-foreground",
        className
      )}
      {...props}
    />
  )
}

export { Kbd }
