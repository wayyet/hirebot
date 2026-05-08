function parseBooleanFlag(value: string | undefined): boolean {
  if (!value) {
    return false
  }

  return ['1', 'true', 'yes', 'on'].includes(value.trim().toLowerCase())
}

const bypassFlag =
  parseBooleanFlag(import.meta.env.VITE_AUTH_BYPASS as string | undefined) ||
  parseBooleanFlag(import.meta.env.VITE_SKIP_LOGIN as string | undefined)

export const isAuthBypassed = bypassFlag
