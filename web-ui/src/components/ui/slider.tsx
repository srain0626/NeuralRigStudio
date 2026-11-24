import * as React from "react"
import * as SliderPrimitive from "@radix-ui/react-slider"
import { cn } from "@/lib/utils"

function Slider({
  className,
  ...props
}: React.ComponentProps<typeof SliderPrimitive.Root>) {
  return (
    <SliderPrimitive.Root
      data-slot="slider"
      className={cn(
        "relative flex w-full touch-none items-center select-none data-[disabled]:opacity-45",
        className
      )}
      {...props}
    >
      <SliderPrimitive.Track className="bg-secondary border border-border relative h-1.5 w-full grow overflow-hidden rounded-sm">
        <SliderPrimitive.Range className="bg-primary absolute h-full" />
      </SliderPrimitive.Track>
      <SliderPrimitive.Thumb className="border-foreground bg-card ring-ring/40 block size-3.5 shrink-0 rounded-sm border-2 transition-[color,box-shadow] hover:ring-2 focus-visible:ring-2 focus-visible:outline-hidden cursor-pointer" />
    </SliderPrimitive.Root>
  )
}

export { Slider }
