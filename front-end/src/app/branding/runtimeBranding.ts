export function resolveDisplayProductName(warmThemeEnabled: boolean, defaultBrandName: string): string {
  return warmThemeEnabled ? 'Y Work' : defaultBrandName
}

export function resolveBrandWordmarkSrc(language: string): string {
  return language.toLowerCase().startsWith('zh') ? '/brand-zh.svg' : '/brand-en.svg'
}

export function resolveSystemBrandIconSrc(warmThemeEnabled: boolean): string {
  return warmThemeEnabled ? '/favicon-warm.svg' : '/favicon.svg'
}

export function resolveSystemTitle(warmThemeEnabled: boolean, language: string): string {
  if (warmThemeEnabled) {
    return 'Y Work'
  }

  return language.toLowerCase().startsWith('zh') ? '好雇' : 'Good Crew'
}
