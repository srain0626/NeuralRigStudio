import React, { createContext, useContext, useEffect, useState } from "react"

export type StudioLocale = "ko" | "en"

const STORAGE_KEY = "neuralrig.locale"

interface I18nContextValue {
  locale: StudioLocale
  setLocale: (next: StudioLocale) => void
  toggleLocale: () => void
  tx: (ko: string, en: string) => string
}

const I18nContext = createContext<I18nContextValue | null>(null)

export const I18nProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [locale, setLocaleState] = useState<StudioLocale>(() => {
    try {
      const saved = localStorage.getItem(STORAGE_KEY)
      if (saved === "en" || saved === "ko") return saved
    } catch {
      // ignore
    }
    return "ko"
  })

  useEffect(() => {
    document.documentElement.lang = locale
    try {
      localStorage.setItem(STORAGE_KEY, locale)
    } catch {
      // ignore
    }
  }, [locale])

  const setLocale = (next: StudioLocale) => setLocaleState(next)
  const toggleLocale = () => setLocaleState((prev) => (prev === "ko" ? "en" : "ko"))
  const tx = (ko: string, en: string) => (locale === "ko" ? ko : en)

  return (
    <I18nContext.Provider value={{ locale, setLocale, toggleLocale, tx }}>
      {children}
    </I18nContext.Provider>
  )
}

export function useI18n(): I18nContextValue {
  const ctx = useContext(I18nContext)
  if (!ctx) {
    throw new Error("useI18n must be used inside I18nProvider")
  }
  return ctx
}
