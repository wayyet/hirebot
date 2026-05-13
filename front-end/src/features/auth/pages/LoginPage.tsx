import { useEffect, type ReactNode } from 'react'
import { ArrowRight, Bot, Loader2, ShieldCheck, Sparkles } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { getAuthUser, isOidcConfigured, signIn } from '@/infra/auth/oidc'
import { isAuthBypassed } from '@/infra/auth/auth-mode'

const POST_LOGIN_REDIRECT_KEY = 'ncrew_post_login_redirect'

const LOGIN_HIGHLIGHT_ICONS = [Sparkles, Bot, ShieldCheck] as const

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

function resolveRedirectLabel(path: string, t: (key: string) => string) {
  if (path === '/template-pool') return t('auth.redirectLabels.templatePool')
  if (path === '/department-employees') return t('auth.redirectLabels.departmentEmployees')
  if (path === '/my-employees') return t('auth.redirectLabels.myEmployees')
  if (path.startsWith('/template-pool/templates/')) return t('auth.redirectLabels.templateDetail')
  if (path.startsWith('/template-pool/hiring/')) return t('auth.redirectLabels.hiringFlow')
  if (path.includes('/instances/')) return t('auth.redirectLabels.instanceDetail')
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
  const { t } = useTranslation()
  const redirectPath = normalizeRedirectPath(searchParams.get('redirect'))
  const redirectLabel = resolveRedirectLabel(redirectPath, t)

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
      headerTag={t('auth.headerTag')}
      kicker={t('auth.kicker')}
      title={
        <>
          {t('auth.title1')}
          <br />
          <span className="hb-login-title-accent">{t('auth.title2')}</span>
        </>
      }
      copy={t('auth.copy')}
      primaryLabel={isLoading ? t('auth.primaryLabelLoading') : t('auth.primaryLabel')}
      primaryBusy={isLoading}
      onPrimary={handleLogin}
      primaryDisabled={!isOidcConfigured || isLoading}
      metaTitle={redirectLabel}
      metaCopy={t('auth.redirectTo', { path: redirectPath })}
      statusLabel={isOidcConfigured ? t('auth.authStatus') : t('auth.needsAttention')}
      statusTitle={isOidcConfigured ? t('auth.oidcConnected') : t('auth.oidcNotConfigured')}
      statusCopy={
        isOidcConfigured
          ? t('auth.oidcConnectedDesc')
          : t('auth.oidcNotConfiguredDesc')
      }
      statusTone={isOidcConfigured ? 'info' : 'warn'}
    />
  )
}

function BypassedLoginPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const { t } = useTranslation()
  const redirectPath = normalizeRedirectPath(searchParams.get('redirect'))
  const redirectLabel = resolveRedirectLabel(redirectPath, t)

  useEffect(() => {
    navigate(redirectPath, { replace: true })
  }, [navigate, redirectPath])

  return (
    <AuthEntryShell
      headerTag={t('auth.bypassHeaderTag')}
      kicker={t('auth.bypassKicker')}
      title={
        <>
          {t('auth.bypassTitle1')}
          <br />
          <span className="hb-login-title-accent">{t('auth.bypassTitle2')} {redirectLabel}</span>
        </>
      }
      copy={t('auth.bypassCopy')}
      metaTitle={redirectLabel}
      metaCopy={t('auth.currentTarget', { path: redirectPath })}
      statusLabel={t('auth.runningStatus')}
      statusTitle={t('auth.devModeRunning')}
      statusCopy={t('auth.devModeDesc')}
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
  statusLabel,
  statusTitle,
  statusCopy,
  statusTone,
  primaryLabel,
  primaryBusy = false,
  primaryDisabled = false,
  onPrimary,
}: AuthEntryShellProps) {
  const { t } = useTranslation()
  const highlightKeys = ['auth.highlights.template', 'auth.highlights.onboarding', 'auth.highlights.security'] as const

  return (
    <div className="hb-login-page">
      <div className="hb-login-grid" aria-hidden="true" />
      <div className="hb-login-orb hb-login-orb-left" aria-hidden="true" />
      <div className="hb-login-orb hb-login-orb-right" aria-hidden="true" />

      <div className="hb-login-shell">
        <header className="hb-login-header">
          <div className="hb-login-brand">
            <span className="hb-login-brand-mark"><Sparkles size={16} /></span>
            <strong className="hb-login-brand-text">{t('brand.name')}</strong>
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

            <div className="hb-login-highlight-row" aria-label={kicker}>
              {LOGIN_HIGHLIGHT_ICONS.map((Icon, idx) => (
                <span key={highlightKeys[idx]} className="hb-login-highlight-pill">
                  <Icon size={14} />
                  {t(highlightKeys[idx])}
                </span>
              ))}
            </div>

            <div className="hb-login-meta-strip">
              <div className={`hb-login-meta-block is-${statusTone}`}>
                <span className="hb-login-meta-label">{statusLabel}</span>
                <strong>{statusTitle}</strong>
                <p>{statusCopy}</p>
              </div>
            </div>
          </section>
        </main>

        <footer className="hb-login-footer">{t('brand.footer')}</footer>
      </div>
    </div>
  )
}
