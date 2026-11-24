import { Toaster as Sonner, type ToasterProps } from "sonner"
import { useStudioTheme } from "@/lib/theme"

function Toaster({ ...props }: ToasterProps) {
  const { theme } = useStudioTheme()
  return (
    <Sonner
      theme={theme.isDark ? "dark" : "light"}
      className="toaster group"
      toastOptions={{
        classNames: {
          toast:
            "rounded-md border border-border bg-popover text-popover-foreground font-sans text-xs shadow-lg",
          description: "text-muted-foreground text-[11px]",
          actionButton: "bg-primary text-primary-foreground rounded-sm font-mono text-[11px]",
          cancelButton: "bg-secondary text-secondary-foreground rounded-sm font-mono text-[11px]",
        },
      }}
      {...props}
    />
  )
}

export { Toaster }
