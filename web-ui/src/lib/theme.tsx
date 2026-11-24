import React, { createContext, useContext, useEffect, useMemo, useState } from "react"
import { studioBridge } from "@/lib/bridge"

export type StudioThemeId = "putty" | "carbon" | "zine" | "field" | "cobalt"

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
    id: "putty",
    name: "Putty Aluminum",
    shortName: "PUTTY",
    taglineKo: "무광 웜 알루미늄 · 버밀리온",
    taglineEn: "Warm Anodized Aluminum · Vermilion",
    isDark: false,
    swatchBg: "#E8E5DE",
    swatchCard: "#F3F1EC",
    swatchPrimary: "#FF4400",
    dwmBgHex: "#F3F1EC",
    dwmBorderHex: "#C5C0B6",
    dwmTextHex: "#151514",
  },
  {
    id: "carbon",
    name: "Carbon Darkroom",
    shortName: "CARBON",
    taglineKo: "무광 카본 블랙 · 본 화이트",
    taglineEn: "Matte Carbon · Bone Chalk Ink",
    isDark: true,
    swatchBg: "#121211",
    swatchCard: "#191917",
    swatchPrimary: "#FF4400",
    dwmBgHex: "#191917",
    dwmBorderHex: "#2D2C29",
    dwmTextHex: "#F0EDE6",
  },
  {
    id: "zine",
    name: "Editorial Zine",
    shortName: "ZINE",
    taglineKo: "미색 리소그래프 지면 · 먹선 그리드",
    taglineEn: "Uncoated Risograph Paper · Ink Rules",
    isDark: false,
    swatchBg: "#F6F3EB",
    swatchCard: "#FFFFFF",
    swatchPrimary: "#1D4ED8",
    dwmBgHex: "#F6F3EB",
    dwmBorderHex: "#1C1C1A",
    dwmTextHex: "#111111",
  },
  {
    id: "field",
    name: "Field Olive",
    shortName: "FIELD",
    taglineKo: "세이지 필드 기어 · 포스퍼 앰버",
    taglineEn: "Sage Field Chassis · Phosphor Amber",
    isDark: true,
    swatchBg: "#1B201A",
    swatchCard: "#232921",
    swatchPrimary: "#FF9F0A",
    dwmBgHex: "#232921",
    dwmBorderHex: "#384136",
    dwmTextHex: "#E5ECE1",
  },
  {
    id: "cobalt",
    name: "Cobalt Lab",
    shortName: "COBALT",
    taglineKo: "브러시드 실버 · 인터내셔널 코발트",
    taglineEn: "Brushed Silver Mist · Klein Cobalt",
    isDark: false,
    swatchBg: "#E4E8F0",
    swatchCard: "#F4F6FA",
    swatchPrimary: "#0047FF",
    dwmBgHex: "#F4F6FA",
    dwmBorderHex: "#BCC5D6",
    dwmTextHex: "#0F172A",
  },
]

const STORAGE_KEY = "neuralrig.theme"

interface StudioThemeContextValue {
  themeId: StudioThemeId
  theme: StudioThemeMeta
  setThemeId: (id: StudioThemeId) => void
  cycleTheme: () => void
}

const StudioThemeContext = createContext<StudioThemeContextValue | null>(null)

export const StudioThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [themeId, setThemeIdState] = useState<StudioThemeId>(() => {
    try {
      const saved = localStorage.getItem(STORAGE_KEY) as StudioThemeId | null
      if (saved && STUDIO_THEMES.some((t) => t.id === saved)) return saved
    } catch {
      // ignore storage read errors
    }
    return "putty"
  })

  const theme = useMemo(
    () => STUDIO_THEMES.find((t) => t.id === themeId) ?? STUDIO_THEMES[0],
    [themeId]
  )

  useEffect(() => {
    const root = document.documentElement
    root.setAttribute("data-theme", theme.id)
    root.classList.toggle("dark", theme.isDark)
    try {
      localStorage.setItem(STORAGE_KEY, theme.id)
    } catch {
      // ignore storage write errors
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
  }, [theme])

  const setThemeId = (next: StudioThemeId) => setThemeIdState(next)

  const cycleTheme = () => {
    const idx = STUDIO_THEMES.findIndex((t) => t.id === themeId)
    const next = STUDIO_THEMES[(idx + 1) % STUDIO_THEMES.length]
    setThemeIdState(next.id)
  }

  return (
    <StudioThemeContext.Provider value={{ themeId, theme, setThemeId, cycleTheme }}>
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
