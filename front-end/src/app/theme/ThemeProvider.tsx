/* eslint-disable react-refresh/only-export-components */
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

export const THEME_MODES = ['light', 'dark'] as const
export const THEME_BRANDS = ['amber', 'blue'] as const

export type ThemeMode = (typeof THEME_MODES)[number]
export type ThemeBrand = (typeof THEME_BRANDS)[number]

const THEME_MODE_STORAGE_KEY = 'ncrew-hire-theme-mode'
const THEME_BRAND_STORAGE_KEY = 'ncrew-hire-theme-brand'
const LEGACY_THEME_STORAGE_KEY = 'ncrew-hire-theme'

type ThemeContextValue = {
  mode: ThemeMode
  brand: ThemeBrand
  isDark: boolean
  setMode: (mode: ThemeMode) => void
  toggleMode: () => void
  setBrand: (brand: ThemeBrand) => void
  cycleBrand: () => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

function isThemeMode(value: string | null): value is ThemeMode {
  return value === 'light' || value === 'dark'
}

function isThemeBrand(value: string | null): value is ThemeBrand {
  return value === 'amber' || value === 'blue'
}

function readInitialMode(): ThemeMode {
  if (typeof window === 'undefined') {
    return 'light'
  }

  const savedMode = window.localStorage.getItem(THEME_MODE_STORAGE_KEY)
  if (isThemeMode(savedMode)) {
    return savedMode
  }

  const legacyMode = window.localStorage.getItem(LEGACY_THEME_STORAGE_KEY)
  return legacyMode === 'dark' ? 'dark' : 'light'
}

function readInitialBrand(): ThemeBrand {
  if (typeof window === 'undefined') {
    return 'amber'
  }

  const savedBrand = window.localStorage.getItem(THEME_BRAND_STORAGE_KEY)
  return isThemeBrand(savedBrand) ? savedBrand : 'amber'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<ThemeMode>(readInitialMode)
  const [brand, setBrand] = useState<ThemeBrand>(readInitialBrand)

  useEffect(() => {
    const root = document.documentElement

    // 兼容现有 `.dark` 变体，同时让新主题系统读取 data-mode / data-brand。
    root.dataset.mode = mode
    root.dataset.brand = brand
    root.classList.toggle('dark', mode === 'dark')
    root.style.colorScheme = mode

    localStorage.setItem(THEME_MODE_STORAGE_KEY, mode)
    localStorage.setItem(THEME_BRAND_STORAGE_KEY, brand)
    localStorage.setItem(LEGACY_THEME_STORAGE_KEY, mode)
  }, [brand, mode])

  useEffect(() => {
    function handleStorage(event: StorageEvent) {
      if (event.key === THEME_MODE_STORAGE_KEY && isThemeMode(event.newValue)) {
        setMode(event.newValue)
      }

      if (event.key === THEME_BRAND_STORAGE_KEY && isThemeBrand(event.newValue)) {
        setBrand(event.newValue)
      }
    }

    window.addEventListener('storage', handleStorage)
    return () => window.removeEventListener('storage', handleStorage)
  }, [])

  const value = useMemo<ThemeContextValue>(() => ({
    mode,
    brand,
    isDark: mode === 'dark',
    setMode,
    toggleMode: () => setMode((current) => (current === 'dark' ? 'light' : 'dark')),
    setBrand,
    cycleBrand: () => setBrand((current) => (current === 'amber' ? 'blue' : 'amber')),
  }), [brand, mode])

  return (
    <ThemeContext.Provider value={value}>
      {children}
    </ThemeContext.Provider>
  )
}

export function useTheme() {
  const context = useContext(ThemeContext)

  if (!context) {
    throw new Error('useTheme must be used within ThemeProvider')
  }

  return context
}
