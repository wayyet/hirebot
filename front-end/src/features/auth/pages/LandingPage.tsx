import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  ArrowRight,
  Bot,
  ChevronDown,
  Globe,
  Layers,
  LogIn,
  LogOut,
  Moon,
  Palette,
  ShieldCheck,
  Sun,
  User,
  Users,
  Zap,
} from 'lucide-react'
import { Sparkles } from 'lucide-react'
import { useTheme } from '@/app/theme/ThemeProvider'
import yWorkHireLogo from '@/assets/y-work-hire-logo.svg'
import {
  resolveBrandWordmarkSrc,
  resolveDisplayProductName,
  resolveSystemTitle,
} from '@/app/branding/runtimeBranding'
import { getAuthUser, getUserDisplayName, isOidcConfigured, signOut, userManager } from '@/infra/auth/oidc'
import { isAuthBypassed } from '@/infra/auth/auth-mode'

const LANGS = [
  { code: 'zh', label: '中文' },
  { code: 'en', label: 'English' },
]

const FEATURES = [
  { icon: Layers, key: 'templatePool', color: '#4F8EF7' },
  { icon: Users, key: 'hiring', color: '#7B5EFF' },
  { icon: Bot, key: 'employees', color: '#10B981' },
  { icon: ShieldCheck, key: 'onboarding', color: '#F59E0B' },
  { icon: Zap, key: 'skills', color: '#EC4899' },
  { icon: Sparkles, key: 'collaboration', color: '#06B6D4' },
] as const

const STEPS = [
  { num: '01', key: 'browse' },
  { num: '02', key: 'hire' },
  { num: '03', key: 'onboard' },
] as const

function GoodCrewMark({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 240 240"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
      className={className}
    >
      <path
        d="M95.625 45C100.112 45 103.75 48.731 103.75 53.3333V70C103.75 74.6024 100.112 78.3333 95.625 78.3333H63.125C58.6377 78.3333 55 82.0643 55 86.6667V153.333C55 157.936 58.6377 161.667 63.125 161.667H95.625C100.112 161.667 103.75 157.936 103.75 153.333V128.333C103.75 123.731 107.388 120 111.875 120H128.125C132.612 120 136.25 123.731 136.25 128.333V158.216C136.25 160.426 135.393 162.546 133.87 164.108L106.13 192.559C104.607 194.121 102.54 195 100.386 195H61.0938C56.6064 195 52.9688 191.269 52.9688 186.667V172.083C52.9688 167.481 49.3311 163.75 44.8438 163.75H30.625C26.1377 163.75 22.5 160.019 22.5 155.417V81.7839C22.5003 79.5742 23.3569 77.4544 24.8804 75.8919L52.6196 47.4414C54.1431 45.8789 56.2098 45.0003 58.3643 45H95.625Z"
        fill="currentColor"
      />
      <path
        d="M209.375 145C213.862 145 217.5 148.731 217.5 153.333V158.216C217.5 160.426 216.643 162.546 215.12 164.108L187.38 192.559C185.857 194.121 183.79 195 181.636 195H147.739C145.585 195 143.518 194.121 141.995 192.559L135.901 186.309C132.729 183.054 132.729 177.779 135.901 174.525L146.057 164.108C147.581 162.546 149.647 161.667 151.802 161.667H176.875C181.081 161.667 184.543 158.389 184.96 154.188L185.04 152.479C185.457 148.278 188.919 145 193.125 145H209.375Z"
        fill="currentColor"
      />
      <path
        d="M181.636 45C183.79 45.0003 185.857 45.8789 187.38 47.4414L215.12 75.8919C216.643 77.4544 217.5 79.5742 217.5 81.7839V86.6667C217.5 91.269 213.862 95 209.375 95H193.125C188.919 95 185.457 91.7222 185.04 87.5212L184.96 85.8122C184.543 81.6111 181.081 78.3333 176.875 78.3333H152.5C148.013 78.3333 144.375 82.0643 144.375 86.6667V103.333C144.375 107.936 140.737 111.667 136.25 111.667H120C115.513 111.667 111.875 107.936 111.875 103.333V81.7839C111.875 79.5742 112.732 77.4544 114.255 75.8919L141.995 47.4414C143.518 45.8789 145.585 45.0003 147.739 45H181.636Z"
        fill="currentColor"
      />
    </svg>
  )
}

function normalizeRedirectPath(raw: string | null): string {
  if (!raw || !raw.trim().startsWith('/')) return '/template-pool'
  return raw.trim()
}

export default function LandingPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const redirectParam = searchParams.get('redirect')
  const hasExplicitRedirect = Boolean(redirectParam?.trim().startsWith('/'))
  const redirectPath = normalizeRedirectPath(redirectParam)
  const { brand, cycleBrand, isDark, toggleMode, warmThemeEnabled, warmThemeManagedByRuntime } = useTheme()
  const currentLang = i18n.resolvedLanguage ?? i18n.language ?? 'zh'
  const productName = resolveDisplayProductName(warmThemeEnabled, t('brand.name'))
  const brandName = productName
  const originalBrandWordmarkSrc = resolveBrandWordmarkSrc(currentLang)
  const [langOpen, setLangOpen] = useState(false)
  const [userOpen, setUserOpen] = useState(false)
  const langRef = useRef<HTMLDivElement>(null)
  const userRef = useRef<HTMLDivElement>(null)

  const { data: user, isFetching, isLoading } = useQuery({
    queryKey: ['auth-user'],
    queryFn: getAuthUser,
    staleTime: 60_000,
    refetchOnMount: 'always',
    retry: false,
  })
  const canEnterWorkspace = isAuthBypassed || Boolean(user)
  const isCheckingAuth = isLoading || isFetching

  useEffect(() => {
    if (!hasExplicitRedirect) {
      return
    }

    if (isAuthBypassed) {
      navigate(redirectPath, { replace: true })
      return
    }

    if (!isLoading && user) {
      navigate(redirectPath, { replace: true })
    }
  }, [hasExplicitRedirect, isLoading, navigate, redirectPath, user])

  useEffect(() => {
    function onDocClick(event: MouseEvent) {
      const target = event.target as Node
      if (langRef.current && !langRef.current.contains(target)) {
        setLangOpen(false)
      }
      if (userRef.current && !userRef.current.contains(target)) {
        setUserOpen(false)
      }
    }

    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [])

  useEffect(() => {
    document.title = resolveSystemTitle(warmThemeEnabled, currentLang)
  }, [currentLang, warmThemeEnabled])

  const handlePrimaryAction = () => {
    if (canEnterWorkspace) {
      navigate(redirectPath)
      return
    }

    // 登录后继续回到用户原本想进入的业务路径。
    void userManager.signinRedirect({ state: { returnTo: redirectPath } })
  }

  const switchLang = (code: string) => {
    i18n.changeLanguage(code)
    localStorage.setItem('ncrew-hire-lang', code)
    setLangOpen(false)
  }

  if (hasExplicitRedirect && (isAuthBypassed || (!isLoading && user))) {
    return (
      <div className="hb-landing-loading">
        <div className="hb-landing-loading-dot" />
      </div>
    )
  }

  const currentLangLabel = LANGS.find((lang) => lang.code === i18n.language)?.label ?? 'ZH'
  const shouldShowWorkspaceAction = canEnterWorkspace || isCheckingAuth
  const navActionLabel = shouldShowWorkspaceAction ? t('landing.enterConsole') : t('landing.loginBtn')
  const ctaActionLabel = shouldShowWorkspaceAction ? t('landing.enterConsole') : t('landing.ctaPrimary')
  const primaryActionDisabled = !canEnterWorkspace && (isCheckingAuth || !isOidcConfigured)
  const userDisplayName = user ? getUserDisplayName(user, currentLang) : ''
  const userEmail = typeof user?.profile.email === 'string' ? user.profile.email : ''
  const displayName = userDisplayName || t('user.defaultName')
  const userInitial = displayName.trim().charAt(0).toUpperCase()

  return (
    <div className="hb-landing">
      <nav className="hb-landing-nav">
        <div className="hb-landing-nav-inner">
          <div className="hb-landing-brand">
            {warmThemeEnabled ? (
              <>
                <div className="hb-brand-logo hb-brand-logo--mark">
                  <img src={yWorkHireLogo} alt="" className="hb-brand-logo-mark" />
                </div>
                <div className="hb-brand-body">
                  <span className="hb-brand-name">{brandName}</span>
                  <span className="hb-brand-tagline">{t('brand.tagline')}</span>
                </div>
              </>
            ) : (
              <>
                <img
                  src={originalBrandWordmarkSrc}
                  alt={t('brand.name')}
                  className="hb-brand-wordmark"
                />
                <span className="hb-landing-brand-suffix">{t('nav.brandSuffix')}</span>
              </>
            )}
          </div>

          <div className="hb-landing-nav-actions">
            {!warmThemeEnabled ? (
              <button
                className="hb-icon-btn"
                onClick={toggleMode}
                aria-label={t('theme.toggle')}
                title={t('theme.toggle')}
              >
                {isDark ? <Sun size={16} /> : <Moon size={16} />}
              </button>
            ) : null}

            {!warmThemeManagedByRuntime ? (
              <button
                className="hb-nav-utility-btn"
                onClick={cycleBrand}
                aria-label={t('theme.brandToggle')}
                title={t('theme.brandToggle')}
              >
                <Palette size={16} />
                <span>{t(`theme.brand.${brand}`)}</span>
              </button>
            ) : null}

            <div className="hb-lang-dropdown" ref={langRef}>
              <button
                className="hb-nav-utility-btn"
                onClick={() => setLangOpen((current) => !current)}
              >
                <Globe size={16} />
                <span>{currentLangLabel}</span>
                <ChevronDown size={16} />
              </button>
              {langOpen ? (
                <div className="hb-dropdown-menu hb-dropdown-menu--right">
                  {LANGS.map((lang) => (
                    <button
                      key={lang.code}
                      className={`hb-dropdown-item${i18n.language === lang.code ? ' is-active' : ''}`}
                      onClick={() => switchLang(lang.code)}
                    >
                      {lang.label}
                    </button>
                  ))}
                </div>
              ) : null}
            </div>

            {canEnterWorkspace ? (
              <div className="landing-auth-popover" ref={userRef}>
                <button
                  type="button"
                  className="app-layout-user-button"
                  onClick={() => {
                    setUserOpen((current) => !current)
                    setLangOpen(false)
                  }}
                  aria-haspopup="menu"
                  aria-expanded={userOpen}
                >
                  <div className="app-layout-user-avatar">
                    {userInitial || <User size={14} />}
                  </div>
                  <span className="app-layout-user-name">{displayName}</span>
                  <ChevronDown
                    size={16}
                    className={`app-layout-chevron${userOpen ? ' is-open' : ''}`}
                  />
                </button>
                {userOpen ? (
                  <div className="glass-modal app-layout-menu app-layout-user-menu" role="menu">
                    <div className="app-layout-user-menu-header">
                      <div className="app-layout-user-menu-avatar">
                        <User size={15} />
                      </div>
                      <div className="app-layout-user-menu-meta">
                        <div className="app-layout-user-menu-name">{displayName}</div>
                        {userEmail ? (
                          <div className="app-layout-user-menu-email">{userEmail}</div>
                        ) : null}
                      </div>
                    </div>
                    <button
                      type="button"
                      className="app-layout-menu-item is-active"
                      onClick={() => {
                        navigate('/template-pool')
                        setUserOpen(false)
                      }}
                    >
                      <span className="app-layout-menu-icon"><LogIn size={14} /></span>
                      <span>{t('nav.console')}</span>
                    </button>
                    {user && !isAuthBypassed ? (
                      <>
                        <div className="app-layout-menu-divider" />
                        <button
                          type="button"
                          className="app-layout-menu-item is-danger"
                          onClick={() => {
                            signOut()
                            setUserOpen(false)
                          }}
                        >
                          <span className="app-layout-menu-icon"><LogOut size={14} /></span>
                          <span>{t('nav.logout')}</span>
                        </button>
                      </>
                    ) : null}
                  </div>
                ) : null}
              </div>
            ) : (
              <button
                className="hb-landing-login-btn"
                onClick={handlePrimaryAction}
                disabled={primaryActionDisabled}
              >
                <LogIn size={14} />
                {navActionLabel}
              </button>
            )}
          </div>
        </div>
      </nav>

      <section className="hb-landing-hero">
        <div className="hb-landing-hero-grid" aria-hidden="true" />
        <div className="hb-landing-hero-glow hb-landing-hero-glow--l" aria-hidden="true" />
        <div className="hb-landing-hero-glow hb-landing-hero-glow--r" aria-hidden="true" />
        <div className="hb-landing-hero-glow hb-landing-hero-glow--b" aria-hidden="true" />

        <div className="hb-landing-hero-content">
          <div className="hb-landing-badge hb-anim-fade-up" style={{ animationDelay: '0ms' }}>
            <GoodCrewMark className="hb-landing-badge-icon" />
            <span>{t('landing.badge')}</span>
          </div>

          <h1 className="hb-landing-headline hb-anim-fade-up" style={{ animationDelay: '80ms' }}>
            {t('landing.heroTitle')}
          </h1>

          <p className="hb-landing-slogan hb-anim-fade-up" style={{ animationDelay: '140ms' }}>
            {t('landing.heroSlogan')}
          </p>

          <p className="hb-landing-subtitle hb-anim-fade-up" style={{ animationDelay: '180ms' }}>
            {t('landing.heroSubtitle', { productName })}
          </p>

          <div className="hb-landing-cta hb-anim-fade-up" style={{ animationDelay: '280ms' }}>
            <button
              className="hb-landing-cta-primary"
              onClick={handlePrimaryAction}
              disabled={primaryActionDisabled}
            >
              <LogIn size={17} />
              {ctaActionLabel}
              <ArrowRight size={15} />
            </button>
            <a className="hb-landing-cta-secondary" href="#features">
              {t('landing.ctaLearnMore')}
              <ChevronDown size={15} />
            </a>
          </div>

          <div className="hb-landing-tags hb-anim-fade-up" style={{ animationDelay: '380ms' }}>
            {['Template Pool', 'Hiring Flow', 'Employee Hub', 'Skills', 'Onboarding', 'Collaboration'].map((tag) => (
              <span key={tag} className="hb-landing-tag">{tag}</span>
            ))}
          </div>
        </div>
      </section>

      <section id="features" className="hb-landing-section">
        <div className="hb-landing-container">
          <div className="hb-landing-section-head">
            <span className="hb-landing-section-label">{t('landing.featuresLabel')}</span>
            <h2 className="hb-landing-section-title">{t('landing.featuresTitle')}</h2>
            <p className="hb-landing-section-copy">{t('landing.featuresSubtitle')}</p>
          </div>

          <div className="hb-landing-features-grid">
            {FEATURES.map(({ icon: Icon, key, color }) => (
              <div key={key} className="hb-landing-feature-card">
                <div
                  className="hb-landing-feature-icon"
                  style={{ background: `${color}18`, border: `1px solid ${color}30` }}
                >
                  <Icon size={22} color={color} />
                </div>
                <h3 className="hb-landing-feature-title">
                  {t(`landing.feature.${key}.title`)}
                </h3>
                <p className="hb-landing-feature-desc">
                  {t(`landing.feature.${key}.desc`)}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="hb-landing-section hb-landing-section--alt">
        <div className="hb-landing-container">
          <div className="hb-landing-section-head">
            <span className="hb-landing-section-label hb-landing-section-label--accent">
              {t('landing.stepsLabel')}
            </span>
            <h2 className="hb-landing-section-title">{t('landing.stepsTitle')}</h2>
          </div>

          <div className="hb-landing-steps">
            {STEPS.map(({ num, key }) => (
              <div key={key} className="hb-landing-step">
                <div className="hb-landing-step-num">{num}</div>
                <div>
                  <h4 className="hb-landing-step-title">{t(`landing.step.${key}.title`)}</h4>
                  <p className="hb-landing-step-desc">{t(`landing.step.${key}.desc`)}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="hb-landing-final">
        <div className="hb-landing-final-inner">
          <div className="hb-landing-final-icon">
            <Users size={28} color="#fff" />
          </div>
          <h2 className="hb-landing-final-title">{t('landing.finalCtaTitle')}</h2>
          <p className="hb-landing-final-copy">{t('landing.finalCtaCopy', { productName })}</p>
          <button
            className="hb-landing-cta-primary"
            onClick={handlePrimaryAction}
            disabled={primaryActionDisabled}
          >
            <LogIn size={17} />
            {ctaActionLabel}
            <ArrowRight size={15} />
          </button>
        </div>
      </section>

      <footer className="hb-landing-footer">
        <span>{t('brand.footer', { productName })}</span>
      </footer>
    </div>
  )
}
