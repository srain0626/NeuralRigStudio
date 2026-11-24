import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import "@fontsource-variable/space-grotesk"
import "pretendard/dist/web/variable/pretendardvariable.css"
import "@fontsource/ibm-plex-mono/latin-400.css"
import "@fontsource/ibm-plex-mono/latin-500.css"
import "@fontsource/ibm-plex-mono/latin-600.css"
import "@fontsource/ibm-plex-mono/latin-700.css"
import "./index.css"
import { StudioThemeProvider } from "@/lib/theme"
import { I18nProvider } from "@/lib/i18n"
import { TooltipProvider } from "@/components/ui/tooltip"
import App from "./App.tsx"

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <StudioThemeProvider>
      <I18nProvider>
        <TooltipProvider delayDuration={160}>
          <App />
        </TooltipProvider>
      </I18nProvider>
    </StudioThemeProvider>
  </StrictMode>
)
