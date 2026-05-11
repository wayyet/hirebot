import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  ArrowRight, Bot, ChevronDown, Globe, Layers,
  LogIn, Moon, ShieldCheck, Sparkles, Sun, Users, Zap,
} from 'lucide-react'
import { getAuthUser, isOidcConfigured, userManager } from '@/infra/auth/oidc'
import { isAuthBypassed } from '@/infra/auth/auth-mode'

// ── 常量 ────────────────────────────────────────────────────────────────────

const LANGS = [
  { code: 'zh', label: '中文' },
  { code: 'en', label: 'English' },
]

const FEATURES = [
  { icon: Layers,       key: 'templatePool',   color: '#4F8EF7' },
  { icon: Users,        key: 'hiring',         color: '#7B5EFF' },
  { icon: Bot,          key: 'employees',      color: '#10B981' },
  { icon: ShieldCheck,  key: 'onboarding',     color: '#F59E0B' },
  { icon: Zap,          key: 'skills',         color: '#EC4899' },
  { icon: Sparkles,     key: 'collaboration',  color: '#06B6D4' },
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

// ── 主组件 ──────────────────────────────────────────────────────────────────

export default function LandingPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const redirectPath = normalizeRedirectPath(searchParams.get('redirect'))

  const [isDark, setIsDark] = useState(
    () => localStorage.getItem('ncrew-hire-theme') === 'dark',
  )
  const [langOpen, setLangOpen] = useState(false)
  const langRef = useRef<HTMLDivElement>(null)

  const { data: user, isLoading } = useQuery({
    queryKey: ['auth-user'],
    queryFn: getAuthUser,
    staleTime: 60_000,
    retry: false,
  })

  // 深色模式同步
  useEffect(() => {
    if (isDark) {
      document.documentElement.classList.add('dark')
      localStorage.setItem('ncrew-hire-theme', 'dark')
    } else {
      document.documentElement.classList.remove('dark')
      localStorage.setItem('ncrew-hire-theme', 'light')
    }
  }, [isDark])

  // 已登录 → 直接进入目标页
  useEffect(() => {
    if (isAuthBypassed) {
      navigate(redirectPath, { replace: true })
      return
    }
    if (!isLoading && user) {
      navigate(redirectPath, { replace: true })
    }
  }, [isLoading, user, navigate, redirectPath])

  // 点击外部关闭语言下拉
  useEffect(() => {
    function onDocClick(e: MouseEvent) {
      if (langRef.current && !langRef.current.contains(e.target as Node)) {
        setLangOpen(false)
      }
    }
    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [])

  const handleLogin = () => {
    // 将目标路径写入 OIDC state，AuthCallbackPage 从 user.state.returnTo 读取
    void userManager.signinRedirect({ state: { returnTo: redirectPath } })
  }

  const switchLang = (code: string) => {
    i18n.changeLanguage(code)
    localStorage.setItem('ncrew-hire-lang', code)
    setLangOpen(false)
  }

  // 登录跳转或 bypass 跳转期间显示占位
  if (isAuthBypassed || (!isLoading && user)) {
    return (
      <div className="hb-landing-loading">
        <div className="hb-landing-loading-dot" />
      </div>
    )
  }

  const currentLangLabel = LANGS.find(l => l.code === i18n.language)?.label ?? 'ZH'

  return (
    <div className="hb-landing">

      {/* ── 顶部导航 ─────────────────────────────────────────────────────── */}
      <nav className="hb-landing-nav">
        <div className="hb-landing-nav-inner">

          {/* 品牌 */}
          <div className="hb-landing-brand">
            <div className="hb-brand-logo">
              <Sparkles size={16} color="#fff" />
            </div>
            <div className="hb-brand-body">
              <span className="hb-brand-name">{t('brand.name')}</span>
              <span className="hb-brand-tagline">{t('brand.tagline')}</span>
            </div>
          </div>

          {/* 右侧操作区 */}
          <div className="hb-landing-nav-actions">

            {/* 主题切换 */}
            <button
              className="hb-icon-btn"
              onClick={() => setIsDark(v => !v)}
              aria-label={t('theme.toggle')}
            >
              {isDark ? <Sun size={16} /> : <Moon size={16} />}
            </button>

            {/* 语言切换 */}
            <div className="hb-lang-dropdown" ref={langRef}>
              <button
                className="hb-nav-utility-btn"
                onClick={() => setLangOpen(v => !v)}
              >
                <Globe size={14} />
                <span>{currentLangLabel}</span>
                <ChevronDown size={12} />
              </button>
              {langOpen && (
                <div className="hb-dropdown-menu hb-dropdown-menu--right">
                  {LANGS.map(l => (
                    <button
                      key={l.code}
                      className={`hb-dropdown-item${i18n.language === l.code ? ' is-active' : ''}`}
                      onClick={() => switchLang(l.code)}
                    >
                      {l.label}
                    </button>
                  ))}
                </div>
              )}
            </div>

            {/* 登录按钮 */}
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

      {/* ── Hero 区 ──────────────────────────────────────────────────────── */}
      <section className="hb-landing-hero">
        <div className="hb-landing-hero-grid" aria-hidden="true" />
        <div className="hb-landing-hero-glow hb-landing-hero-glow--l" aria-hidden="true" />
        <div className="hb-landing-hero-glow hb-landing-hero-glow--r" aria-hidden="true" />

        <div className="hb-landing-hero-content">

          {/* 角标 */}
          <div className="hb-landing-badge hb-anim-fade-up" style={{ animationDelay: '0ms' }}>
            <Sparkles size={12} />
            {t('landing.badge')}
          </div>

          {/* 大标题 */}
          <h1 className="hb-landing-headline hb-anim-fade-up" style={{ animationDelay: '80ms' }}>
            {t('landing.heroTitle1')}
            <br />
            <span className="hb-landing-headline-accent">
              {t('landing.heroTitle2')}
            </span>
          </h1>

          {/* 副标题 */}
          <p className="hb-landing-subtitle hb-anim-fade-up" style={{ animationDelay: '180ms' }}>
            {t('landing.heroSubtitle')}
          </p>

          {/* CTA 按钮 */}
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

          {/* 技术标签 */}
          <div className="hb-landing-tags hb-anim-fade-up" style={{ animationDelay: '380ms' }}>
            {['Template Pool', 'Hiring Flow', 'Employee Hub', 'Skills', 'Onboarding', 'Collaboration'].map(tag => (
              <span key={tag} className="hb-landing-tag">{tag}</span>
            ))}
          </div>

        </div>
      </section>

      {/* ── 功能模块 ─────────────────────────────────────────────────────── */}
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

      {/* ── 使用步骤 ─────────────────────────────────────────────────────── */}
      <section className="hb-landing-section hb-landing-section--alt">
        <div className="hb-landing-container">

          <div className="hb-landing-section-head">
            <span className="hb-landing-section-label" style={{ color: '#7B5EFF' }}>
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

      {/* ── 页底 CTA ─────────────────────────────────────────────────────── */}
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

      {/* ── 页脚 ─────────────────────────────────────────────────────────── */}
      <footer className="hb-landing-footer">
        <span>{t('brand.footer')}</span>
      </footer>

    </div>
  )
}
