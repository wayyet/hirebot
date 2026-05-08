import { useEffect, type ReactNode } from 'react'
import { ArrowRight, Bot, Loader2, ShieldCheck, Sparkles } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { getAuthUser, isOidcConfigured, signIn } from '@/infra/auth/oidc'
import { isAuthBypassed } from '@/infra/auth/auth-mode'

const POST_LOGIN_REDIRECT_KEY = 'ncrew_post_login_redirect'

const LOGIN_HIGHLIGHTS = [
  { icon: Sparkles, label: '模板直达雇佣流程' },
  { icon: Bot, label: '入职配置与上岗协作' },
  { icon: ShieldCheck, label: '统一认证与安全访问' },
] as const

function normalizeRedirectPath(raw: string | null): string {
  if (!raw || raw.trim().length === 0) {
    return '/template-pool'
  }

  const value = raw.trim()
  if (!value.startsWith('/')) {
    return '/template-pool'
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

function resolveRedirectLabel(path: string) {
  if (path === '/template-pool') {
    return '模板广场'
  }

  if (path === '/department-employees') {
    return '部门数字员工'
  }

  if (path === '/my-employees') {
    return '我的数字员工'
  }

  if (path.startsWith('/templates/')) {
    return '模板详情'
  }

  if (path.startsWith('/hiring/')) {
    return '雇佣流程'
  }

  if (path.startsWith('/instances/')) {
    return '数字员工详情'
  }

  return path
}

export default function LoginPage() {
  if (isAuthBypassed) {
    return <BypassedLoginPage />
  }

  return <OidcLoginPage />
}

function OidcLoginPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const redirectPath = normalizeRedirectPath(searchParams.get('redirect'))
  const redirectLabel = resolveRedirectLabel(redirectPath)

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
  }, [isLoading, navigate, redirectPath, user])

  const handleLogin = () => {
    persistRedirectPath(redirectPath)
    void signIn()
  }

  return (
    <AuthEntryShell
      headerTag="统一认证入口"
      kicker="数字员工雇佣端"
      title={
        <>
          雇佣下一位
          <br />
          <span className="hb-login-title-accent">数字员工</span>
        </>
      }
      copy="从模板挑选、发起雇佣，到完成入职与上岗协作，HireBot 将整个数字员工雇佣流程收束到同一个入口。"
      primaryLabel={isLoading ? '检查登录状态中...' : '进入统一登录'}
      primaryBusy={isLoading}
      onPrimary={handleLogin}
      primaryDisabled={!isOidcConfigured || isLoading}
      metaTitle={redirectLabel}
      metaCopy={`认证成功后会自动返回 ${redirectPath}`}
      statusLabel={isOidcConfigured ? '认证状态' : '需要处理'}
      statusTitle={isOidcConfigured ? 'OIDC 已连接' : 'OIDC 尚未配置'}
      statusCopy={
        isOidcConfigured
          ? '使用统一身份认证完成登录，随后继续你刚才访问的页面。'
          : '请检查前端运行时配置或环境变量，确保认证服务地址可用后再登录。'
      }
      statusTone={isOidcConfigured ? 'info' : 'warn'}
    />
  )
}

function BypassedLoginPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const redirectPath = normalizeRedirectPath(searchParams.get('redirect'))
  const redirectLabel = resolveRedirectLabel(redirectPath)

  useEffect(() => {
    navigate(redirectPath, { replace: true })
  }, [navigate, redirectPath])

  return (
    <AuthEntryShell
      headerTag="开发态直通"
      kicker="本地开发模式"
      title={
        <>
          已跳过登录
          <br />
          <span className="hb-login-title-accent">正在进入 {redirectLabel}</span>
        </>
      }
      copy="当前前端处于开发态跳过登录模式，本页只作为入口占位，页面将直接进入你刚才请求的业务路径。"
      metaTitle={redirectLabel}
      metaCopy={`当前目标路径：${redirectPath}`}
      statusLabel="运行状态"
      statusTitle="开发态已开启"
      statusCopy="如果你想验证真实登录流程，请关闭跳过登录配置后重新访问当前页面。"
      statusTone="success"
    />
  )
}

type AuthEntryShellProps = {
  headerTag: string
  kicker: string
  title: ReactNode
  copy: string
  metaTitle: string
  metaCopy: string
  statusLabel: string
  statusTitle: string
  statusCopy: string
  statusTone: 'info' | 'warn' | 'success'
  primaryLabel?: string
  primaryBusy?: boolean
  primaryDisabled?: boolean
  onPrimary?: () => void
}

function AuthEntryShell({
  headerTag,
  kicker,
  title,
  copy,
  metaTitle,
  metaCopy,
  statusLabel,
  statusTitle,
  statusCopy,
  statusTone,
  primaryLabel,
  primaryBusy = false,
  primaryDisabled = false,
  onPrimary,
}: AuthEntryShellProps) {
  return (
    <div className="hb-login-page">
      <div className="hb-login-grid" aria-hidden="true" />
      <div className="hb-login-orb hb-login-orb-left" aria-hidden="true" />
      <div className="hb-login-orb hb-login-orb-right" aria-hidden="true" />

      <div className="hb-login-shell">
        <header className="hb-login-header">
          <div className="hb-login-brand">
            <span className="hb-login-brand-mark">HB</span>
            <div className="hb-login-brand-copy">
              <strong>HireBot</strong>
              <span>Hiring</span>
            </div>
          </div>

          <span className="hb-login-header-tag">{headerTag}</span>
        </header>

        <main className="hb-login-main">
          <section className="hb-login-hero">
            <span className="hb-login-kicker">{kicker}</span>
            <h1 className="hb-login-title">{title}</h1>
            <p className="hb-login-copy">{copy}</p>

            {primaryLabel ? (
              <div className="hb-login-actions">
                <button
                  type="button"
                  onClick={onPrimary}
                  disabled={primaryDisabled}
                  className="hb-login-primary-btn"
                >
                  {primaryBusy ? (
                    <Loader2 size={18} className="animate-spin" />
                  ) : null}
                  <span>{primaryLabel}</span>
                  <ArrowRight size={18} />
                </button>
              </div>
            ) : null}

            <div className="hb-login-highlight-row" aria-label="登录入口亮点">
              {LOGIN_HIGHLIGHTS.map(({ icon: Icon, label }) => (
                <span key={label} className="hb-login-highlight-pill">
                  <Icon size={14} />
                  {label}
                </span>
              ))}
            </div>

            <div className="hb-login-meta-strip">
              <div className="hb-login-meta-block">
                <span className="hb-login-meta-label">登录后去向</span>
                <strong>{metaTitle}</strong>
                <p>{metaCopy}</p>
              </div>

              <div className={`hb-login-meta-block is-${statusTone}`}>
                <span className="hb-login-meta-label">{statusLabel}</span>
                <strong>{statusTitle}</strong>
                <p>{statusCopy}</p>
              </div>
            </div>
          </section>
        </main>

        <footer className="hb-login-footer">HireBot · 数字员工雇佣端</footer>
      </div>
    </div>
  )
}
