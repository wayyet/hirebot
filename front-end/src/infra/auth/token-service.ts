import { getAccessToken } from '@/infra/auth/oidc'

const AUTH_EXPIRED_MESSAGE = '登录状态已失效，请重新登录后重试'

class TokenService {
  async ensureFresh(): Promise<string | undefined> {
    // oidc.ts 的 getAccessToken 内部会自动尝试 refresh_token 续期
    const token = await getAccessToken()
    if (!token) {
      throw new Error(AUTH_EXPIRED_MESSAGE)
    }
    return token
  }
}

export const tokenService = new TokenService()

