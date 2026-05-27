/* eslint-disable react-refresh/only-export-components */
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

export const THEME_MODES = ['light', 'dark'] as const
export const THEME_BRANDS = ['amber', 'blue'] as const
export const THEME_SKINS = ['warm', 'default'] as const

export type ThemeMode = (typeof THEME_MODES)[number]
export type ThemeBrand = (typeof THEME_BRANDS)[number]
export type ThemeSkin = (typeof THEME_SKINS)[number]

const THEME_MODE_STORAGE_KEY = 'ncrew-hire-theme-mode'
const THEME_BRAND_STORAGE_KEY = 'ncrew-hire-theme-brand'
const LEGACY_THEME_STORAGE_KEY = 'ncrew-hire-theme'

type ThemeContextValue = {
  mode: ThemeMode
  brand: ThemeBrand
  skin: ThemeSkin
  warmThemeEnabled: boolean
  warmThemeManagedByRuntime: boolean
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

function resolveConfiguredBrand(): ThemeBrand | null {
  if (typeof window === 'undefined') {
    return null
  }

  const configured = window.__AUTH_CONFIG__?.EnableWarmTheme
  if (typeof configured !== 'boolean') {
    return null
  }

  return configured ? 'amber' : 'blue'
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
    return 'blue'
  }

  const configuredBrand = resolveConfiguredBrand()
  if (configuredBrand) {
    return configuredBrand
  }

  const savedBrand = window.localStorage.getItem(THEME_BRAND_STORAGE_KEY)
  return isThemeBrand(savedBrand) ? savedBrand : 'blue'
}

function deriveSkin(brand: ThemeBrand): ThemeSkin {
  return brand === 'amber' ? 'warm' : 'default'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const configuredBrand = resolveConfiguredBrand()
  const warmThemeManagedByRuntime = configuredBrand !== null
  const [mode, setMode] = useState<ThemeMode>(readInitialMode)
  const [brand, setBrandState] = useState<ThemeBrand>(readInitialBrand)
  const skin = deriveSkin(brand)

  useEffect(() => {
    if (configuredBrand && brand !== configuredBrand) {
      setBrandState(configuredBrand)
    }
  }, [brand, configuredBrand])

  useEffect(() => {
    const root = document.documentElement

    // 兼容现有 `.dark` 变体，同时让主题系统读取 mode / brand / skin。
    root.dataset.mode = mode
    root.dataset.brand = brand
    root.dataset.skin = skin
    root.classList.toggle('dark', mode === 'dark')
    root.style.colorScheme = mode

    localStorage.setItem(THEME_MODE_STORAGE_KEY, mode)
    localStorage.setItem(THEME_BRAND_STORAGE_KEY, brand)
    localStorage.setItem(LEGACY_THEME_STORAGE_KEY, mode)
  }, [brand, mode, skin])

  useEffect(() => {
    function handleStorage(event: StorageEvent) {
      if (event.key === THEME_MODE_STORAGE_KEY && isThemeMode(event.newValue)) {
        setMode(event.newValue)
      }

      if (event.key !== THEME_BRAND_STORAGE_KEY) {
        return
      }

      if (configuredBrand) {
        setBrandState(configuredBrand)
        return
      }

      if (isThemeBrand(event.newValue)) {
        setBrandState(event.newValue)
      }
    }

    window.addEventListener('storage', handleStorage)
    return () => window.removeEventListener('storage', handleStorage)
  }, [configuredBrand])

  const value = useMemo<ThemeContextValue>(() => ({
    mode,
    brand,
    skin,
    warmThemeEnabled: brand === 'amber',
    warmThemeManagedByRuntime,
    isDark: mode === 'dark',
    setMode,
    toggleMode: () => setMode((current) => (current === 'dark' ? 'light' : 'dark')),
    setBrand: (nextBrand) => {
      if (warmThemeManagedByRuntime) {
        return
      }

      setBrandState(nextBrand)
    },
    cycleBrand: () => {
      if (warmThemeManagedByRuntime) {
        return
      }

      setBrandState((current) => (current === 'amber' ? 'blue' : 'amber'))
    },
  }), [brand, mode, skin, warmThemeManagedByRuntime])

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
