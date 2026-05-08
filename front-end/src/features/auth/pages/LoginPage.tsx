import { useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { getAuthUser, signIn, isOidcConfigured } from '@/infra/auth/oidc'
import { isAuthBypassed } from '@/infra/auth/auth-mode'

const POST_LOGIN_REDIRECT_KEY = 'ncrew_post_login_redirect'

function normalizeRedirectPath(raw: string | null): string {
  if (!raw || raw.trim().length === 0) {
    return '/market'
  }

  const value = raw.trim()
  if (!value.startsWith('/')) {
    return '/market'
  }

  return value
}

function persistRedirectPath(path: string) {
  sessionStorage.setItem(POST_LOGIN_REDIRECT_KEY, path)
}

function consumeRedirectPath(fallbackPath: string) {
  const storedPath = sessionStorage.getItem(POST_LOGIN_REDIRECT_KEY)

  if (storedPath) {
    sessionStorage.removeItem(POST_LOGIN_REDIRECT_KEY)
    return normalizeRedirectPath(storedPath)
  }

  return fallbackPath
}

export default function LoginMain() {
  if (isAuthBypassed) {
    return <BypassedLoginPage />
  }

  return <OidcLoginPage />
}

function OidcLoginPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const redirectPath = normalizeRedirectPath(searchParams.get('redirect'))

  // 已登录则直接跳转
  const { data: user, isLoading } = useQuery({
    queryKey: ['auth-user'],
    queryFn: getAuthUser,
    staleTime: 60_000,
    retry: false,
  })

  useEffect(() => {
    if (!isLoading && user) {
      navigate(consumeRedirectPath(redirectPath), { replace: true })
    }
  }, [isLoading, user, navigate, redirectPath])

  const handleLogin = () => {
    persistRedirectPath(redirectPath)
    void signIn()
  }

  return (
    <div className="min-h-screen bg-[var(--hb-grad)] px-6 py-10">
      <div className="mx-auto flex min-h-[calc(100vh-5rem)] max-w-5xl items-center justify-center">
        <div className="hb-section w-full max-w-xl">
          <span className="hb-kicker">Sign In</span>
          <h1 className="hb-page-title">登录 HireBot 雇佣端</h1>
          <p className="hb-page-copy">
            当前系统使用统一登录（OIDC）。点击下方按钮后将跳转到登录页完成认证。
          </p>

          {!isOidcConfigured && (
            <div className="hb-alert hb-alert-warn mt-5">
              <span>OIDC 认证服务未配置，请检查环境变量或运行时配置。</span>
            </div>
          )}

          <div className="hb-alert hb-alert-info mt-5">
            <span>登录成功后会自动返回你刚才访问的页面。</span>
          </div>

          <div className="mt-6">
            <button
              type="button"
              onClick={handleLogin}
              disabled={!isOidcConfigured || isLoading}
              className="hb-btn-primary w-full justify-center py-3"
            >
              {isLoading ? '检查登录状态...' : '前往统一登录'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

function BypassedLoginPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const redirectPath = normalizeRedirectPath(searchParams.get('redirect'))

  useEffect(() => {
    navigate(redirectPath, { replace: true })
  }, [navigate, redirectPath])

  return (
    <div className="min-h-screen bg-[var(--hb-grad)] px-6 py-10">
      <div className="mx-auto flex min-h-[calc(100vh-5rem)] max-w-5xl items-center justify-center">
        <div className="hb-section w-full max-w-xl">
          <span className="hb-kicker">Skip Login</span>
          <h1 className="hb-page-title">已启用开发态跳过登录</h1>
          <p className="hb-page-copy">
            当前前端已绕过 Keycloak 登录，正在直接进入你刚才访问的页面。
          </p>
        </div>
      </div>
    </div>
  )
}
