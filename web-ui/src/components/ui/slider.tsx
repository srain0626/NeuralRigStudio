import * as React from "react"
import * as SliderPrimitive from "@radix-ui/react-slider"
import { cn } from "@/lib/utils"

type SliderProps = React.ComponentProps<typeof SliderPrimitive.Root> & {
  trackClassName?: string
  rangeClassName?: string
  thumbClassName?: string
  showCenterTick?: boolean
}

function Slider({
  className,
  trackClassName,
  rangeClassName,
  thumbClassName,
  showCenterTick,
  ...props
}: SliderProps) {
  return (
    <SliderPrimitive.Root
      data-slot="slider"
      className={cn(
        "relative flex w-full touch-none items-center select-none data-[disabled]:opacity-45",
        className
      )}
      {...props}
    >
      <SliderPrimitive.Track
        className={cn(
          "bg-secondary border border-border relative h-1.5 w-full grow overflow-hidden rounded-sm",
          trackClassName
        )}
      >
        {showCenterTick && (
          <span
            aria-hidden="true"
            className="pointer-events-none absolute left-1/2 top-0 bottom-0 w-px -translate-x-1/2 bg-white/30 z-[1]"
          />
        )}
        <SliderPrimitive.Range className={cn("bg-primary absolute h-full", rangeClassName)} />
      </SliderPrimitive.Track>
      <SliderPrimitive.Thumb
        className={cn(
          "border-foreground bg-card ring-ring/40 block size-3.5 shrink-0 rounded-sm border-2 transition-[color,box-shadow] hover:ring-2 focus-visible:ring-2 focus-visible:outline-hidden cursor-pointer",
          thumbClassName
        )}
      />
    </SliderPrimitive.Root>
  )
}

export { Slider }
