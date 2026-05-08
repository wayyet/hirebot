export default function MissingKeycloakConfigScreen({ missingKeycloakEnv }: { missingKeycloakEnv: string[] }) {
  return (
    <div className="min-h-screen bg-[var(--hb-grad)] px-6 py-10">
      <div className="mx-auto flex min-h-[calc(100vh-5rem)] max-w-5xl items-center justify-center">
        <div className="hb-section w-full max-w-2xl">
          <span className="hb-kicker">Auth Config</span>
          <h1 className="hb-page-title">Keycloak 配置缺失</h1>
          <p className="hb-page-copy">
            当前前端无法初始化登录能力，请先补齐环境变量后刷新页面。
          </p>
          <div className="hb-alert hb-alert-warn mt-5">
            <span>{missingKeycloakEnv.length > 0 ? `缺失项：${missingKeycloakEnv.join(', ')}` : '请检查 Keycloak 配置'}</span>
          </div>
          <p className="mt-4 text-xs text-[#737373]">
            可参考 <code className="rounded bg-[#fafafa] px-1.5 py-0.5">.env.example</code> 配置后重启开发服务。
          </p>
        </div>
      </div>
    </div>
  )
}
