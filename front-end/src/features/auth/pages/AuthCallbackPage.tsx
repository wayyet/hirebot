import { useEffect, useRef, useState } from 'react'
import { userManager } from '@/infra/auth/oidc'

/**
 * OIDC 授权码回调页面。
 * 挂载后执行一次 signinRedirectCallback，完成 code 换 token，
 * 然后跳转到登录前保存的 returnTo 路径。
 *
 * 使用 useRef 防止 React StrictMode 在开发环境下双重调用 effect 时
 * 重复执行回调逻辑（第二次调用会因 state/code 已消耗而失败）。
 */
export default function AuthCallbackPage() {
  const called = useRef(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    // 严格模式下 effect 执行两次，第二次直接跳过
    if (called.current) return
    called.current = true

    userManager
      .signinRedirectCallback()
      .then((user) => {
        const state = user?.state as { returnTo?: string } | null | undefined
        const returnTo = state?.returnTo ?? '/department-employees'
        window.location.replace(returnTo)
      })
      .catch((err: unknown) => {
        const msg = err instanceof Error ? err.message : String(err)
        console.error('[oidc] callback 处理失败:', msg)
        setError(msg)
        // 3 秒后自动跳回登录页
        setTimeout(() => window.location.replace('/login'), 3000)
      })
  }, [])

  if (error) {
    return (
      <div className="min-h-screen bg-[var(--hb-grad)] flex items-center justify-center px-6">
        <div className="hb-section max-w-md w-full">
          <h1 className="hb-page-title text-red-600">登录回调失败</h1>
          <p className="hb-page-copy mt-2 text-red-500">{error}</p>
          <p className="mt-4 text-sm text-slate-500">3 秒后自动跳转到登录页...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-[var(--hb-grad)] flex items-center justify-center">
      <div className="text-sm text-slate-500">正在完成登录，请稍候...</div>
    </div>
  )
}
