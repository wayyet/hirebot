function parseBooleanFlag(value: string | undefined): boolean {
  if (!value) {
    return false
  }

  return ['1', 'true', 'yes', 'on'].includes(value.trim().toLowerCase())
}

// 后端通过 runtime-config.js 注入的 BypassAuth 优先级最高
const runtimeBypass = typeof window !== 'undefined' && window.__AUTH_CONFIG__?.BypassAuth === true

const bypassFlag =
  runtimeBypass ||
  parseBooleanFlag(import.meta.env.VITE_AUTH_BYPASS as string | undefined) ||
  parseBooleanFlag(import.meta.env.VITE_SKIP_LOGIN as string | undefined)

export const isAuthBypassed = bypassFlag
