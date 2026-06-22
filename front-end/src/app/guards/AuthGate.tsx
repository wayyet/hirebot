import type { ReactElement } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { getAuthUser, getResourceAccessRoles } from '@/infra/auth/oidc'
import { isAuthBypassed } from '@/infra/auth/auth-mode'

export default function AuthGate({ children }: { children: ReactElement }) {
  if (isAuthBypassed) {
    return children
  }

  return <OidcAuthGate>{children}</OidcAuthGate>
}

function OidcAuthGate({ children }: { children: ReactElement }) {
  const { data: user, isLoading } = useQuery({
    queryKey: ['auth-user'],
    queryFn: getAuthUser,
    staleTime: 60_000,
    retry: false,
  })
  const location = useLocation()

  if (isLoading) {
    return (
      <div className="min-h-screen bg-white flex items-center justify-center text-sm text-slate-500">
        正在检查登录状态...
      </div>
    )
  }

  if (!user) {
    const redirect = encodeURIComponent(location.pathname + location.search)
    return <Navigate to={`/?redirect=${redirect}`} replace />
  }

  if (getResourceAccessRoles(user).length === 0) {
    return <Navigate to="/403" replace />
  }

  return children
}

