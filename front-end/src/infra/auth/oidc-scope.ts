export const DEFAULT_OIDC_SCOPE = 'openid profile email organization'

export function normalizeOidcScope(value: string): string {
  const scopes = value
    .split(/\s+/)
    .map(scope => scope.trim())
    .filter(Boolean)

  if (!scopes.includes('openid')) {
    scopes.unshift('openid')
  }

  return Array.from(new Set(scopes)).join(' ')
}

export function resolveOidcScope(runtimeScope?: string, envScope?: string): string {
  const configuredScope = runtimeScope?.trim() || envScope?.trim() || DEFAULT_OIDC_SCOPE
  return normalizeOidcScope(configuredScope)
}

export function isStoredScopeCurrent(storedScope: string | undefined, currentScope: string): boolean {
  if (!storedScope) return false
  const storedScopes = new Set(normalizeOidcScope(storedScope).split(' '))
  const currentScopes = normalizeOidcScope(currentScope).split(' ')

  if (storedScopes.size !== currentScopes.length) {
    return false
  }

  return currentScopes.every(scope => storedScopes.has(scope))
}
