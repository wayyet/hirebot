import { createContext, useContext } from 'react'

export type HirebotUserRole = 'manager' | 'member'

export interface UserRoleContextValue {
  role: HirebotUserRole
  setRole: (role: HirebotUserRole) => void
}

export const UserRoleContext = createContext<UserRoleContextValue | null>(null)

export function useUserRole() {
  const context = useContext(UserRoleContext)
  if (!context) {
    throw new Error('useUserRole must be used inside UserRoleContext.Provider')
  }
  return context
}
