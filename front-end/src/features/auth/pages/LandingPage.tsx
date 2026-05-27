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
  Moon,
  Palette,
  ShieldCheck,
  Sparkles,
  Sun,
  Users,
  Zap,
} from 'lucide-react'
import { useTheme } from '@/app/theme/ThemeProvider'
import yWorkHireLogo from '@/assets/y-work-hire-logo.svg'
import { getAuthUser, isOidcConfigured, userManager } from '@/infra/auth/oidc'
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

function normalizeRedirectPath(raw: string | null): string {
  if (!raw || !raw.trim().startsWith('/')) return '/template-pool'
  return raw.trim()
}

export default function LandingPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const redirectPath = normalizeRedirectPath(searchParams.get('redirect'))
  const { brand, cycleBrand, isDark, toggleMode, warmThemeEnabled, warmThemeManagedByRuntime } = useTheme()
  const [langOpen, setLangOpen] = useState(false)
  const langRef = useRef<HTMLDivElement>(null)

  const { data: user, isLoading } = useQuery({
    queryKey: ['auth-user'],
    queryFn: getAuthUser,
    staleTime: 60_000,
    retry: false,
  })

  useEffect(() => {
    if (isAuthBypassed) {
      navigate(redirectPath, { replace: true })
      return
    }

    if (!isLoading && user) {
      navigate(redirectPath, { replace: true })
    }
  }, [isLoading, navigate, redirectPath, user])

  useEffect(() => {
    function onDocClick(event: MouseEvent) {
      if (langRef.current && !langRef.current.contains(event.target as Node)) {
        setLangOpen(false)
      }
    }

    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [])

  const handleLogin = () => {
    // 登录后继续回到用户原本想进入的业务路径。
    void userManager.signinRedirect({ state: { returnTo: redirectPath } })
  }

  const switchLang = (code: string) => {
    i18n.changeLanguage(code)
    localStorage.setItem('ncrew-hire-lang', code)
    setLangOpen(false)
  }

  if (isAuthBypassed || (!isLoading && user)) {
    return (
      <div className="hb-landing-loading">
        <div className="hb-landing-loading-dot" />
      </div>
    )
  }

  const currentLangLabel = LANGS.find((lang) => lang.code === i18n.language)?.label ?? 'ZH'

  return (
    <div className="hb-landing">
      <nav className="hb-landing-nav">
        <div className="hb-landing-nav-inner">
          <div className="hb-landing-brand">
            <div className={`hb-brand-logo${warmThemeEnabled ? ' hb-brand-logo--mark' : ''}`}>
              {warmThemeEnabled ? (
                <img src={yWorkHireLogo} alt="" className="hb-brand-logo-mark" />
              ) : (
                <Sparkles size={16} color="#fff" />
              )}
            </div>
            <div className="hb-brand-body">
              <span className="hb-brand-name">{t('brand.name')}</span>
              <span className="hb-brand-tagline">{t('brand.tagline')}</span>
            </div>
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
                <Palette size={14} />
                <span>{t(`theme.brand.${brand}`)}</span>
              </button>
            ) : null}

            <div className="hb-lang-dropdown" ref={langRef}>
              <button
                className="hb-nav-utility-btn"
                onClick={() => setLangOpen((current) => !current)}
              >
                <Globe size={14} />
                <span>{currentLangLabel}</span>
                <ChevronDown size={12} />
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

            <button
              className="hb-landing-login-btn"
              onClick={handleLogin}
              disabled={!isOidcConfigured}
            >
              <LogIn size={14} />
              {t('landing.loginBtn')}
            </button>
          </div>
        </div>
      </nav>

      <section className="hb-landing-hero">
        <div className="hb-landing-hero-grid" aria-hidden="true" />
        <div className="hb-landing-hero-glow hb-landing-hero-glow--l" aria-hidden="true" />
        <div className="hb-landing-hero-glow hb-landing-hero-glow--r" aria-hidden="true" />

        <div className="hb-landing-hero-content">
          <div className="hb-landing-badge hb-anim-fade-up" style={{ animationDelay: '0ms' }}>
            <Sparkles size={12} />
            {t('landing.badge')}
          </div>

          <h1 className="hb-landing-headline hb-anim-fade-up" style={{ animationDelay: '80ms' }}>
            {t('landing.heroTitle1')}
            <br />
            <span className="hb-landing-headline-accent">
              {t('landing.heroTitle2')}
            </span>
          </h1>

          <p className="hb-landing-subtitle hb-anim-fade-up" style={{ animationDelay: '180ms' }}>
            {t('landing.heroSubtitle')}
          </p>

          <div className="hb-landing-cta hb-anim-fade-up" style={{ animationDelay: '280ms' }}>
            <button
              className="hb-landing-cta-primary"
              onClick={handleLogin}
              disabled={!isOidcConfigured}
            >
              <LogIn size={17} />
              {t('landing.ctaPrimary')}
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
          <p className="hb-landing-final-copy">{t('landing.finalCtaCopy')}</p>
          <button
            className="hb-landing-cta-primary"
            onClick={handleLogin}
            disabled={!isOidcConfigured}
          >
            <LogIn size={17} />
            {t('landing.ctaPrimary')}
            <ArrowRight size={15} />
          </button>
        </div>
      </section>

      <footer className="hb-landing-footer">
        <span>{t('brand.footer')}</span>
      </footer>
    </div>
  )
}
