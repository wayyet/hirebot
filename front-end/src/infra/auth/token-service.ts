import type { AuthClientTokens } from '@react-keycloak/core'
import type Keycloak from 'keycloak-js'

const AUTH_EXPIRED_MESSAGE = '登录状态已失效，请重新登录后重试'

class TokenService {
  private tokens: AuthClientTokens | null = null
  private keycloakClient: Keycloak | null = null

  update(tokens: AuthClientTokens, keycloakClient: Keycloak) {
    this.tokens = tokens
    this.keycloakClient = keycloakClient
  }

  getAccessToken(): string | undefined {
    return this.tokens?.token
  }

  async ensureFresh(): Promise<string | undefined> {
    if (!this.keycloakClient) {
      return this.tokens?.token
    }

    try {
      await this.keycloakClient.updateToken(30)
      const latestToken = this.keycloakClient.token ?? this.tokens?.token
      if (!latestToken) {
        throw new Error(AUTH_EXPIRED_MESSAGE)
      }

      return latestToken
    } catch {
      this.clear()
      throw new Error(AUTH_EXPIRED_MESSAGE)
    }
  }

  clear() {
    this.tokens = null
    this.keycloakClient = null
  }
}

export const tokenService = new TokenService()
