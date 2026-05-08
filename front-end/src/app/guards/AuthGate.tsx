import type { ReactElement } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useKeycloak } from '@react-keycloak/web'
import { isAuthBypassed } from '@/infra/auth/auth-mode'

export default function AuthGate({ children }: { children: ReactElement }) {
  if (isAuthBypassed) {
    return children
  }

  return <KeycloakAuthGate>{children}</KeycloakAuthGate>
}

function KeycloakAuthGate({ children }: { children: ReactElement }) {
  const { keycloak, initialized } = useKeycloak()
  const location = useLocation()

  if (!initialized) {
    return (
      <div className="min-h-screen bg-white flex items-center justify-center text-sm text-slate-500">
        正在检查登录状态...
      </div>
    )
  }

  if (!keycloak?.authenticated) {
    const redirect = encodeURIComponent(location.pathname + location.search)
    return <Navigate to={`/login?redirect=${redirect}`} replace />
  }

  return children
}
