import React, { createContext, useContext, useLayoutEffect, useMemo, useState } from "react"
import { studioBridge } from "@/lib/bridge"

export type StudioThemeId = "mist" | "obsidian"

export interface StudioThemeMeta {
  id: StudioThemeId
  name: string
  shortName: string
  taglineKo: string
  taglineEn: string
  isDark: boolean
  swatchBg: string
  swatchCard: string
  swatchPrimary: string
  dwmBgHex: string
  dwmBorderHex: string
  dwmTextHex: string
}

export const STUDIO_THEMES: StudioThemeMeta[] = [
  {
    id: "mist",
    name: "Silver-Mist",
    shortName: "MIST",
    taglineKo: "실버 미스트 캔버스 · 다크 옵시디언 스테이지",
    taglineEn: "Silver-Mist Canvas · Obsidian Stage",
    isDark: false,
    swatchBg: "#d5dcd9",
    swatchCard: "#f4f6f5",
    swatchPrimary: "#ff6b24",
    dwmBgHex: "#d5dcd9",
    dwmBorderHex: "#bbc4c0",
    dwmTextHex: "#0b0e10",
  },
  {
    id: "obsidian",
    name: "Obsidian Night",
    shortName: "DARK",
    taglineKo: "딥 옵시디언 스튜디오 · 탠저린 오라",
    taglineEn: "Deep Obsidian Studio · Tangerine Aura",
    isDark: true,
    swatchBg: "#0a0f12",
    swatchCard: "#11171b",
    swatchPrimary: "#ff6b24",
    dwmBgHex: "#0a0f12",
    dwmBorderHex: "#1e282e",
    dwmTextHex: "#f5f7f6",
  },
]

const STORAGE_KEY = "neuralrig.theme.v2"

interface StudioThemeContextValue {
  themeId: StudioThemeId
  theme: StudioThemeMeta
  setThemeId: (id: StudioThemeId) => void
  toggleTheme: () => void
  cycleTheme: () => void
}

const StudioThemeContext = createContext<StudioThemeContextValue | null>(null)

export const StudioThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [themeId, setThemeIdState] = useState<StudioThemeId>(() => {
    try {
      const saved = localStorage.getItem(STORAGE_KEY) as StudioThemeId | null
      if (saved === "mist" || saved === "obsidian") return saved
    } catch {
      // ignore
    }
    return "mist"
  })

  const theme = useMemo(
    () => STUDIO_THEMES.find((t) => t.id === themeId) ?? STUDIO_THEMES[0],
    [themeId]
  )

  useLayoutEffect(() => {
    const root = document.documentElement
    root.setAttribute("data-theme-switching", "1")
    root.setAttribute("data-theme", theme.id)
    root.classList.toggle("dark", theme.isDark)
    void root.offsetWidth
    const frame = requestAnimationFrame(() => {
      root.removeAttribute("data-theme-switching")
    })
    try {
      localStorage.setItem(STORAGE_KEY, theme.id)
    } catch {
      // ignore
    }
    window.dispatchEvent(new CustomEvent("studio-theme-change", { detail: theme }))

    if (studioBridge.isWebView2) {
      studioBridge
        .invoke("setWindowTheme", {
          bgHex: theme.dwmBgHex,
          borderHex: theme.dwmBorderHex,
          textHex: theme.dwmTextHex,
          isDark: theme.isDark,
        })
        .catch(() => {
          // ignore on unsupported builds
        })
    }
    return () => {
      cancelAnimationFrame(frame)
      root.removeAttribute("data-theme-switching")
    }
  }, [theme])

  const setThemeId = (next: StudioThemeId) => setThemeIdState(next)
  const toggleTheme = () => setThemeIdState((prev) => (prev === "mist" ? "obsidian" : "mist"))
  const cycleTheme = toggleTheme

  return (
    <StudioThemeContext.Provider value={{ themeId, theme, setThemeId, toggleTheme, cycleTheme }}>
      {children}
    </StudioThemeContext.Provider>
  )
}

export function useStudioTheme(): StudioThemeContextValue {
  const ctx = useContext(StudioThemeContext)
  if (!ctx) {
    throw new Error("useStudioTheme must be used inside StudioThemeProvider")
  }
  return ctx
}
