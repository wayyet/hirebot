import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { LogOut, Loader2 } from 'lucide-react'
import { useKeycloak } from '@react-keycloak/web'
import { UserRoleContext, type HirebotUserRole } from '@/app/context/UserRoleContext'
import { useUxOverlay } from '@/app/context/UxOverlayContext'
import { isAuthBypassed } from '@/infra/auth/auth-mode'
import { tokenService } from '@/infra/auth/token-service'

const ROLE_STORAGE_KEY = 'hirebot_user_role_v1'

type NavItem = {
  path: string
  label: string
  managerOnly?: boolean
  alwaysVisible?: boolean
  isNew?: boolean
}

const navItems: NavItem[] = [
  { path: '/template-pool', label: '企业模板池', managerOnly: true, isNew: true },
  { path: '/department-employees', label: '部门数字员工', alwaysVisible: true },
  { path: '/my-employees', label: '我的数字员工', alwaysVisible: true },
  { path: '/prototype', label: '原型预览', alwaysVisible: true },
]

function deriveDefaultRole(): HirebotUserRole {
  const cachedRole = localStorage.getItem(ROLE_STORAGE_KEY)
  if (cachedRole === 'manager' || cachedRole === 'member') {
    return cachedRole
  }
  return 'manager'
}

function isNavItemActive(pathname: string, navPath: string) {
  if (pathname === navPath) return true
  if (navPath === '/template-pool') {
    return pathname.startsWith('/templates/') || pathname.startsWith('/hiring/')
  }
  if (navPath === '/department-employees') {
    return pathname.startsWith('/department-employees')
      || pathname.startsWith('/instances/')
      || pathname.includes('/evaluation')
      || pathname.includes('/review')
      || pathname.includes('/onboarding')
  }
  if (navPath === '/my-employees') {
    return pathname.startsWith('/my-employees')
      || pathname.startsWith('/clone/')
      || pathname.startsWith('/private-branch/')
      || pathname.includes('/chat')
  }
  if (navPath === '/prototype') {
    return pathname.startsWith('/prototype')
  }
  return pathname.startsWith(navPath)
}

export default function Layout({ children }: { children: React.ReactNode }) {
  const location = useLocation()
  const navigate = useNavigate()
  const { keycloak } = useKeycloak()
  const { showToast } = useUxOverlay()
  const [role, setRole] = useState<HirebotUserRole>(deriveDefaultRole)
  const [logoutLoading, setLogoutLoading] = useState(false)

  useEffect(() => {
    localStorage.setItem(ROLE_STORAGE_KEY, role)
  }, [role])

  useEffect(() => {
    if (role === 'member' && location.pathname.startsWith('/template-pool')) {
      navigate('/department-employees', { replace: true })
    }
  }, [location.pathname, navigate, role])

  const handleLogout = useCallback(async () => {
    if (logoutLoading) return
    setLogoutLoading(true)
    try {
      if (isAuthBypassed || !keycloak) {
        tokenService.clear()
        navigate('/login', { replace: true })
        return
      }
      await keycloak.logout({
        redirectUri: `${window.location.origin}/login`,
      })
    } catch {
      tokenService.clear()
      navigate('/login', { replace: true })
    } finally {
      setLogoutLoading(false)
    }
  }, [logoutLoading, keycloak, navigate])

  const visibleNavItems = useMemo(() => {
    return navItems.filter((item) => {
      if (item.alwaysVisible) return true
      if (item.managerOnly) return role === 'manager'
      return true
    })
  }, [role])

  return (
    <UserRoleContext.Provider value={{ role, setRole }}>
      <div className="hb-shell">
        <header className="hb-topnav">
          <div className="hb-topnav-inner">
            <Link to={role === 'manager' ? '/template-pool' : '/department-employees'} className="hb-brand">
              <span className="hb-brand-logo">雇</span>
              <span className="hb-brand-text">HireBot 雇佣端</span>
              <span className="hb-brand-eyes" aria-hidden>👀</span>
            </Link>

            <nav className="hb-nav">
              {visibleNavItems.map((item) => {
                const active = isNavItemActive(location.pathname, item.path)
                return (
                  <Link key={item.path} to={item.path} className={`hb-nav-item ${active ? 'is-active' : ''}`}>
                    {item.label}
                    {item.isNew ? <span className="hb-nav-flag">new</span> : null}
                  </Link>
                )
              })}
            </nav>

            <div className="hb-nav-right">
              <div className="hb-role-switch">
                <button
                  type="button"
                  className={role === 'manager' ? 'is-active' : ''}
                  onClick={() => setRole('manager')}
                >
                  🧑‍💼 部门长
                </button>
                <button
                  type="button"
                  className={role === 'member' ? 'is-active' : ''}
                  onClick={() => setRole('member')}
                >
                  🧑‍💻 普通成员
                </button>
              </div>
              <div className="hb-user-chip">
                <span className="hb-user-avatar">{role === 'manager' ? '李' : '王'}</span>
                <span>{role === 'manager' ? '李部门长 · 研发部' : '王成员 · 研发部'}</span>
              </div>
              <button
                type="button"
                className="hb-btn-ghost hb-logout-btn"
                onClick={() => void handleLogout()}
                disabled={logoutLoading}
                title="退出登录"
              >
                {logoutLoading ? <Loader2 size={14} className="animate-spin" /> : <LogOut size={14} />}
              </button>
            </div>
          </div>
        </header>
        <main className="hb-main">{children}</main>
        <button
          type="button"
          className="hb-feedback-strip"
          onClick={() => showToast('反馈入口已收到，后续将接入真实表单', 'info')}
        >
          建议反馈
        </button>
      </div>
    </UserRoleContext.Provider>
  )
}
