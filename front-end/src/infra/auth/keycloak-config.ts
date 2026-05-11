interface RuntimeAuthConfig {
  Authority?: string
  Realm?: string
  ClientId?: string
  BypassAuth?: boolean
  ApiBase?: string
  SandboxGatewayEndpoint?: string
  TemplateApiBase?: string
}

declare global {
  interface Window {
    __AUTH_CONFIG__?: RuntimeAuthConfig
  }
}

export const keycloakConfig = (() => {
  let url = (import.meta.env.VITE_KEYCLOAK_URL as string | undefined)?.trim()
  let realm = (import.meta.env.VITE_KEYCLOAK_REALM as string | undefined)?.trim()
  let clientId = (import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string | undefined)?.trim()

  if (typeof window !== 'undefined') {
    const runtimeConfig = window.__AUTH_CONFIG__
    if (runtimeConfig && typeof runtimeConfig === 'object') {
      url = runtimeConfig.Authority?.trim() || url
      realm = runtimeConfig.Realm?.trim() || realm
      clientId = runtimeConfig.ClientId?.trim() || clientId
    }
  }

  // 开发环境提供默认值，避免因未配置环境变量而白屏。
  if (import.meta.env.DEV) {
    url ||= 'http://test-passport.zyagi.cn:1080'
    realm ||= 'ai4cbrain'
    clientId ||= 'af'
  }

  if (!url || !realm || !clientId) {
    return null
  }

  return { url, realm, clientId } as const
})()

export const missingKeycloakEnv = (() => {
  if (keycloakConfig) {
    return [] as string[]
  }

  const missing = [
    !import.meta.env.VITE_KEYCLOAK_URL ? 'VITE_KEYCLOAK_URL' : null,
    !import.meta.env.VITE_KEYCLOAK_REALM ? 'VITE_KEYCLOAK_REALM' : null,
    !import.meta.env.VITE_KEYCLOAK_CLIENT_ID ? 'VITE_KEYCLOAK_CLIENT_ID' : null,
  ].filter((item): item is string => Boolean(item))

  return missing
})()

export const isKeycloakConfigured = keycloakConfig !== null

export const keycloakInitOptions = (() => {
  return {
    pkceMethod: 'S256' as const,
    checkLoginIframe: false,
    scope: 'openid profile email',
  } as const
})()
