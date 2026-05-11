/**
 * 轻量级自定义 OIDC 客户端 — Authorization Code flow，不使用 PKCE。
 * 只依赖 `crypto.getRandomValues`（在 HTTP 非安全上下文中同样可用），
 * 解决 keycloak-js 要求 Web Crypto API（HTTPS）的限制。
 *
 * 参考 ncrew-builder console 的 oidc.ts 实现，适配 HireBot 运行时配置格式。
 */

// ---------------------------------------------------------------------------
// Runtime config（后端通过 runtime-config.js 注入）
// ---------------------------------------------------------------------------

const runtimeCfg = typeof window !== 'undefined' ? (window.__AUTH_CONFIG__ ?? {}) : {}

function buildAuthority(): string {
  let base = runtimeCfg.Authority?.trim()
  let realm = runtimeCfg.Realm?.trim()

  if (!base) {
    base = (import.meta.env.VITE_KEYCLOAK_URL as string | undefined)?.trim() ?? ''
  }
  if (!realm) {
    realm = (import.meta.env.VITE_KEYCLOAK_REALM as string | undefined)?.trim() ?? ''
  }

  // 开发环境提供默认值
  if (import.meta.env.DEV) {
    base ||= 'http://test-passport.zyagi.cn:1080'
    realm ||= 'ai4cbrain'
  }

  if (!base || !realm) return ''
  return `${base.replace(/\/+$/, '')}/realms/${realm}`
}

function buildClientId(): string {
  let clientId = runtimeCfg.ClientId?.trim()
  if (!clientId) {
    clientId = (import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string | undefined)?.trim()
  }
  if (!clientId && import.meta.env.DEV) {
    clientId = 'af'
  }
  return clientId ?? ''
}

const authority = buildAuthority()
const clientId = buildClientId()

if (!authority || !clientId) {
  console.warn('[oidc] OIDC authority 或 clientId 未配置')
}

export const isOidcConfigured = Boolean(authority && clientId)

const REDIRECT_URI = typeof window !== 'undefined'
  ? `${window.location.origin}/auth/callback`
  : ''

// ---------------------------------------------------------------------------
// 错误类型
// ---------------------------------------------------------------------------

export class AuthError extends Error {
  constructor(message = 'Authentication required') {
    super(message)
    this.name = 'AuthError'
  }
}

// ---------------------------------------------------------------------------
// Storage 工具（localStorage + sessionStorage 降级）
// ---------------------------------------------------------------------------

const PREFIX = 'hirebot_oidc_'

function storeSave(key: string, value: string) {
  try { localStorage.setItem(PREFIX + key, value) } catch { sessionStorage.setItem(PREFIX + key, value) }
}

function storeLoad(key: string): string | null {
  return localStorage.getItem(PREFIX + key) ?? sessionStorage.getItem(PREFIX + key)
}

function storeRemove(key: string) {
  localStorage.removeItem(PREFIX + key)
  sessionStorage.removeItem(PREFIX + key)
}

function storeClear() {
  const all = [...Object.keys(localStorage), ...Object.keys(sessionStorage)]
  all.filter(k => k.startsWith(PREFIX)).forEach(k => {
    localStorage.removeItem(k)
    sessionStorage.removeItem(k)
  })
}

// ---------------------------------------------------------------------------
// 工具函数
// ---------------------------------------------------------------------------

function randomString(length: number): string {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
  const data = new Uint8Array(length)
  crypto.getRandomValues(data)
  return Array.from(data, b => chars[b % chars.length]).join('')
}

function parseJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const b64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    const padded = b64.padEnd(Math.ceil(b64.length / 4) * 4, '=')
    const bytes = Uint8Array.from(atob(padded), c => c.charCodeAt(0))
    return JSON.parse(new TextDecoder().decode(bytes))
  } catch {
    return null
  }
}

/** token 是否过期（含 10 秒提前量）*/
function tokenExpired(token: string): boolean {
  const payload = parseJwtPayload(token)
  if (!payload || typeof payload.exp !== 'number') return false
  return payload.exp - Math.floor(Date.now() / 1000) <= 10
}

// ---------------------------------------------------------------------------
// OIDC Discovery（缓存单次）
// ---------------------------------------------------------------------------

let discoveryCache: Record<string, string> | null = null

async function ensureDiscovery(): Promise<Record<string, string>> {
  if (discoveryCache) return discoveryCache
  const resp = await fetch(`${authority}/.well-known/openid-configuration`)
  if (!resp.ok) throw new Error('OIDC discovery 失败: ' + resp.status)
  discoveryCache = await resp.json()
  return discoveryCache!
}

function redirectToLogin() {
  window.location.assign('/')
}

// ---------------------------------------------------------------------------
// TokenSet 持久化
// ---------------------------------------------------------------------------

interface TokenSet {
  access_token: string
  id_token?: string
  refresh_token?: string
}

function saveTokenSet(ts: TokenSet) {
  storeSave('token_set', JSON.stringify(ts))
}

function loadTokenSet(): TokenSet | null {
  const raw = storeLoad('token_set')
  if (!raw) return null
  try { return JSON.parse(raw) } catch { return null }
}

// ---------------------------------------------------------------------------
// OidcUser 类型
// ---------------------------------------------------------------------------

export interface OidcUser {
  access_token: string
  id_token?: string
  refresh_token?: string
  expired: boolean
  profile: {
    sub?: string
    name?: string
    given_name?: string
    family_name?: string
    preferred_username?: string
    nickname?: string
    email?: string
    [key: string]: unknown
  }
  state?: unknown
}

function buildUser(ts: TokenSet, state?: unknown): OidcUser {
  return {
    access_token: ts.access_token,
    id_token: ts.id_token,
    refresh_token: ts.refresh_token,
    expired: tokenExpired(ts.access_token),
    profile: (parseJwtPayload(ts.access_token) ?? {}) as OidcUser['profile'],
    state,
  }
}

// ---------------------------------------------------------------------------
// Token 换取 & 刷新
// ---------------------------------------------------------------------------

async function exchangeCode(code: string): Promise<TokenSet> {
  const endpoints = await ensureDiscovery()
  const resp = await fetch(endpoints.token_endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      client_id: clientId,
      code,
      redirect_uri: REDIRECT_URI,
    }),
  })
  if (!resp.ok) throw new Error('Token 换取失败: ' + resp.status)
  const ts: TokenSet = await resp.json()
  saveTokenSet(ts)
  return ts
}

async function tryRefresh(): Promise<TokenSet | null> {
  const ts = loadTokenSet()
  if (!ts?.refresh_token) return null
  const endpoints = await ensureDiscovery()
  const resp = await fetch(endpoints.token_endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'refresh_token',
      client_id: clientId,
      refresh_token: ts.refresh_token,
    }),
  })
  if (!resp.ok) { storeClear(); return null }
  const next: TokenSet = await resp.json()
  // 部分 IdP 不在 refresh 响应中返回 refresh_token，保留旧值
  if (!next.refresh_token && ts.refresh_token) next.refresh_token = ts.refresh_token
  saveTokenSet(next)
  return next
}

// ---------------------------------------------------------------------------
// userManager — 核心 OIDC 操作
// ---------------------------------------------------------------------------

export const userManager = {
  async getUser(): Promise<OidcUser | null> {
    let ts = loadTokenSet()
    if (!ts) return null
    if (tokenExpired(ts.access_token)) {
      ts = await tryRefresh()
      if (!ts) return null
    }
    return buildUser(ts)
  },

  async signinRedirect(options?: { state?: unknown }): Promise<void> {
    if (!authority || !clientId) throw new Error('OIDC authority / clientId 未配置')
    const endpoints = await ensureDiscovery()
    const state = randomString(32)
    storeSave('state', state)
    if (options?.state !== undefined) {
      storeSave('signin_state', JSON.stringify(options.state))
    }
    const params = new URLSearchParams({
      client_id: clientId,
      redirect_uri: REDIRECT_URI,
      response_type: 'code',
      scope: 'openid profile email',
      state,
    })
    window.location.assign(`${endpoints.authorization_endpoint}?${params}`)
  },

  async signinRedirectCallback(): Promise<OidcUser> {
    const params = new URLSearchParams(window.location.search)
    const code = params.get('code')
    const state = params.get('state')
    const error = params.get('error')

    if (error) {
      const desc = params.get('error_description')
      throw new Error(error + (desc ? ': ' + desc : ''))
    }
    if (!code) throw new Error('回调 URL 中未找到 authorization code')

    const expectedState = storeLoad('state')
    if (!state || !expectedState || state !== expectedState) {
      throw new Error('OIDC state 校验失败')
    }
    storeRemove('state')

    const ts = await exchangeCode(code)

    const rawSigninState = storeLoad('signin_state')
    storeRemove('signin_state')
    const signinState = rawSigninState ? JSON.parse(rawSigninState) : undefined

    // 从 URL 清除 OIDC 参数，保持 URL 整洁
    const url = new URL(window.location.href)
    ;['code', 'state', 'session_state', 'iss', 'error', 'error_description'].forEach(k =>
      url.searchParams.delete(k),
    )
    window.history.replaceState({}, document.title, url.toString())

    return buildUser(ts, signinState)
  },

  async removeUser(): Promise<void> {
    storeClear()
  },

  async signoutRedirect(): Promise<void> {
    const ts = loadTokenSet()
    storeClear()
    let logoutUrl: string | undefined
    try {
      const endpoints = await ensureDiscovery()
      logoutUrl = endpoints.end_session_endpoint ?? endpoints.revocation_endpoint
    } catch {
      redirectToLogin()
      return
    }
    if (!logoutUrl) { redirectToLogin(); return }
    const p = new URLSearchParams({
      post_logout_redirect_uri: window.location.origin + '/',
      client_id: clientId,
    })
    if (ts?.id_token) p.set('id_token_hint', ts.id_token)
    window.location.assign(`${logoutUrl}?${p}`)
  },
}

// ---------------------------------------------------------------------------
// 对外公共 API
// ---------------------------------------------------------------------------

let redirectingToSignIn = false

export async function getAuthUser(): Promise<OidcUser | null> {
  return userManager.getUser()
}

export async function getAccessToken(): Promise<string | null> {
  const user = await getAuthUser()
  return user?.access_token ?? null
}

export async function requireAccessToken(): Promise<string> {
  const token = await getAccessToken()
  if (!token) throw new AuthError()
  return token
}

export function isAuthError(error: unknown): error is AuthError {
  return error instanceof AuthError
}

export async function redirectToSignIn(): Promise<void> {
  if (redirectingToSignIn) return
  redirectingToSignIn = true
  try { await userManager.removeUser() } catch { /* 忽略 */ }
  try {
    await userManager.signinRedirect({
      state: {
        returnTo: `${window.location.pathname}${window.location.search}${window.location.hash}`,
      },
    })
  } finally {
    redirectingToSignIn = false
  }
}

export async function signIn(): Promise<void> {
  await userManager.signinRedirect()
}

export async function signOut(): Promise<void> {
  await userManager.signoutRedirect()
}

export function getUserDisplayName(user: OidcUser): string {
  const p = user.profile
  return String(p.name ?? p.nickname ?? p.preferred_username ?? p.given_name ?? 'User')
}
