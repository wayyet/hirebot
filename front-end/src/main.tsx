import { StrictMode, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { ReactKeycloakProvider } from '@react-keycloak/web'
import Keycloak from 'keycloak-js'
import './index.css'
import App from '@/app/App'
import MissingKeycloakConfigScreen from '@/app/components/MissingKeycloakConfigScreen'
import { isAuthBypassed } from '@/infra/auth/auth-mode'
import {
  keycloakConfig,
  keycloakInitOptions,
  isKeycloakConfigured,
  missingKeycloakEnv,
} from '@/infra/auth/keycloak-config'
import { tokenService } from '@/infra/auth/token-service'

const keycloakClient = keycloakConfig ? new Keycloak(keycloakConfig) : null
const shouldUseKeycloak = !isAuthBypassed && isKeycloakConfigured && keycloakClient

function AuthBootstrapScreen({ errorMessage }: { errorMessage: string | null }) {
  return (
    <div className="min-h-screen bg-slate-50 px-6 py-10">
      <div className="mx-auto flex min-h-[calc(100vh-5rem)] max-w-5xl items-center justify-center">
        <div className="w-full max-w-xl rounded-xl border border-slate-200 bg-white p-8 shadow-sm">
          <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
            Auth Bootstrap
          </span>
          <h1 className="mt-3 text-2xl font-semibold text-slate-900">
            {errorMessage ? '登录初始化失败' : '正在初始化登录'}
          </h1>
          <p className="mt-3 text-sm leading-6 text-slate-600">
            {errorMessage
              ? '认证服务没有完成初始化，当前页面不会再一直停在检查状态。你可以刷新页面重试，或联系管理员检查认证域名与浏览器策略。'
              : '正在连接统一认证服务并准备登录上下文，请稍候。'}
          </p>

          {errorMessage ? (
            <>
              <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                {errorMessage}
              </div>

              <div className="mt-6">
                <button
                  type="button"
                  onClick={() => window.location.reload()}
                  className="inline-flex items-center justify-center rounded-lg bg-slate-900 px-5 py-2.5 text-sm font-medium text-white transition-colors hover:bg-slate-800"
                >
                  刷新后重试
                </button>
              </div>
            </>
          ) : null}
        </div>
      </div>
    </div>
  )
}

function KeycloakApp({ authClient }: { authClient: Keycloak }) {
  const [authInitError, setAuthInitError] = useState<string | null>(null)

  return (
    <ReactKeycloakProvider
      authClient={authClient}
      initOptions={keycloakInitOptions}
      LoadingComponent={<AuthBootstrapScreen errorMessage={authInitError} />}
      onTokens={(tokens) => tokenService.update(tokens, authClient)}
      onEvent={(event, error) => {
        if (event === 'onAuthLogout') {
          tokenService.clear()
        }

        if (event === 'onInitError') {
          setAuthInitError(error instanceof Error ? error.message : 'Keycloak 初始化失败。')
          return
        }

        if (event === 'onReady' || event === 'onAuthSuccess') {
          setAuthInitError(null)
        }
      }}
    >
      <App />
    </ReactKeycloakProvider>
  )
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {shouldUseKeycloak ? (
      <KeycloakApp authClient={keycloakClient!} />
    ) : isAuthBypassed ? (
      <App />
    ) : (
      <MissingKeycloakConfigScreen missingKeycloakEnv={missingKeycloakEnv} />
    )}
  </StrictMode>,
)
