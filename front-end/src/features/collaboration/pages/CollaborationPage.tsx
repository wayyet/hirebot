import { useEffect, useMemo, useState } from 'react'
import {
  AlertCircle,
  Check,
  CheckCircle2,
  Copy,
  ExternalLink,
  Link2,
  Loader2,
  MessageSquare,
  RefreshCw,
  Settings2,
  Users,
  Wifi,
} from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { api, type CollaborationGroupSummary, type EmployeeDetail, type EmployeeSummary } from '@/infra/api'
import { firstCharacter } from '@/features/hiring/pages/employeeView'
import { getIMPlatformIcon, type IMPlatform, openIMDirect } from '@/shared/utils/imDeepLink'

type TaskStatus = 'done' | 'running' | 'pending'
type FeishuConnectionType = 'websocket' | 'callback'
type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'error'
type ActiveSection = 'live' | 'intern'

interface ActiveEmployee extends EmployeeDetail {
  id: string
}

interface RecentTask {
  text: string
  time: string
  status: TaskStatus
}

interface FeishuConfig {
  appId: string
  appSecret: string
  botName: string
}

interface CallbackConfig {
  callbackUrl: string
  verificationToken: string
  encryptKey: string
}

function normalizePlatform(platform?: string): IMPlatform {
  if (platform === '飞书' || platform === '钉钉' || platform === '企业微信') {
    return platform
  }
  return '飞书'
}

function toDetailFallback(summary: EmployeeSummary): EmployeeDetail {
  return {
    ...summary,
    capabilities: [],
    internshipStartAt: null,
    graduatedAt: null,
    satisfactionScore: null,
    evalPhase: null,
    evalIteration: null,
    evalMaxIterations: null,
  }
}

function toActiveEmployee(detail: EmployeeDetail): ActiveEmployee {
  return {
    ...detail,
    id: detail.employeeId,
    capabilities: detail.capabilities ?? [],
    pendingActions: detail.pendingActions ?? [],
  }
}

function formatRelativeDay(dateText: string): string {
  const date = new Date(dateText)
  if (Number.isNaN(date.getTime())) {
    return '刚刚'
  }
  const diffMs = Date.now() - date.getTime()
  const hours = Math.floor(diffMs / (1000 * 60 * 60))
  if (hours < 1) return '刚刚'
  if (hours < 24) return `${hours} 小时前`
  const days = Math.floor(hours / 24)
  if (days <= 7) return `${days} 天前`
  return dateText
}

function buildRecentTask(employee: ActiveEmployee): RecentTask | null {
  if (employee.pendingActions.length > 0) {
    return {
      text: employee.pendingActions[0],
      time: formatRelativeDay(employee.createdAt),
      status: employee.lifecycleStatus === '待人工评估' ? 'pending' : 'running',
    }
  }
  if (employee.primarySignal) {
    return {
      text: employee.primarySignal,
      time: formatRelativeDay(employee.createdAt),
      status: employee.lifecycleStatus === '实习中' ? 'running' : 'done',
    }
  }
  if (employee.stageSummary) {
    return {
      text: employee.stageSummary,
      time: formatRelativeDay(employee.createdAt),
      status: 'pending',
    }
  }
  return null
}

function classifyEmployee(employee: ActiveEmployee): ActiveSection | null {
  if (employee.status === 'live' || employee.lifecycleStatus === '已转正') {
    return 'live'
  }
  if (
    employee.status === 'interning_ai'
    || employee.status === 'interning_human'
    || employee.lifecycleStatus.includes('实习')
    || employee.lifecycleStatus.includes('待上岗')
  ) {
    return 'intern'
  }
  return null
}

function toneForConnection(status: ConnectionStatus) {
  if (status === 'connected') return 'green'
  if (status === 'connecting') return 'blue'
  if (status === 'error') return 'orange'
  return 'gray'
}

export default function CollaborationPage() {
  const navigate = useNavigate()

  const [showFeishuConfig, setShowFeishuConfig] = useState(false)
  const [connectionType, setConnectionType] = useState<FeishuConnectionType>('websocket')
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('disconnected')
  const [feishuConfig, setFeishuConfig] = useState<FeishuConfig>({
    appId: '',
    appSecret: '',
    botName: '',
  })
  const [callbackConfig, setCallbackConfig] = useState<CallbackConfig>({
    callbackUrl: '',
    verificationToken: '',
    encryptKey: '',
  })
  const [webSocketUrl, setWebSocketUrl] = useState('')
  const [saveConfigLoading, setSaveConfigLoading] = useState(false)
  const [configSaved, setConfigSaved] = useState(false)
  const [employees, setEmployees] = useState<ActiveEmployee[]>([])
  const [groups, setGroups] = useState<CollaborationGroupSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [activeSection, setActiveSection] = useState<ActiveSection>('live')

  useEffect(() => {
    void loadCollaborationData()
  }, [])

  const activeEmployees = useMemo(() => {
    return employees.filter((employee) => classifyEmployee(employee) !== null && (employee.isConfigured ?? true))
  }, [employees])

  const defaultPlatform = useMemo(
    () => normalizePlatform(groups.find((group) => !group.isArchived)?.imPlatform),
    [groups],
  )

  const liveEmployees = useMemo(
    () => activeEmployees.filter((employee) => classifyEmployee(employee) === 'live'),
    [activeEmployees],
  )
  const internEmployees = useMemo(
    () => activeEmployees.filter((employee) => classifyEmployee(employee) === 'intern'),
    [activeEmployees],
  )

  const visibleEmployees = activeSection === 'live' ? liveEmployees : internEmployees

  async function loadCollaborationData() {
    setLoading(true)
    setError('')
    try {
      const [summaries, groupSummaries] = await Promise.all([
        api.employeeRuntime.getEmployees(),
        api.collaboration.getGroups(false),
      ])
      const details = await Promise.all(
        summaries.map(async (summary) => {
          try {
            return await api.employeeRuntime.getEmployee(summary.employeeId)
          } catch {
            return toDetailFallback(summary)
          }
        }),
      )
      setEmployees(details.map(toActiveEmployee))
      setGroups(groupSummaries)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '加载协作数据失败')
      setEmployees([])
      setGroups([])
    } finally {
      setLoading(false)
    }
  }

  async function handleSaveFeishuConfig() {
    setSaveConfigLoading(true)
    setConfigSaved(false)
    await new Promise((resolve) => setTimeout(resolve, 1000))
    if (connectionType === 'websocket') {
      setWebSocketUrl(`wss://open.feishu.cn/websocket/app=${feishuConfig.appId}`)
    }
    setSaveConfigLoading(false)
    setConfigSaved(true)
  }

  async function handleConnect() {
    setConnectionStatus('connecting')
    await new Promise((resolve) => setTimeout(resolve, 1200))
    if (
      (connectionType === 'websocket' && feishuConfig.appId && feishuConfig.appSecret)
      || (connectionType === 'callback' && callbackConfig.callbackUrl)
    ) {
      setConnectionStatus('connected')
      return
    }
    setConnectionStatus('error')
  }

  function handleDisconnect() {
    setConnectionStatus('disconnected')
  }

  function handleCopyWsUrl() {
    void navigator.clipboard.writeText(webSocketUrl)
  }

  function openChat(employee: ActiveEmployee) {
    openIMDirect(defaultPlatform, `bot_${employee.id}`, employee.nickname)
  }

  return (
    <div className="hb-page hb-page-wide">
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">协作台</span>
          <h1 className="hb-page-title">
            在企业 IM 中调度 <span className="accent">已上岗与实习中的数字员工</span>
          </h1>
          <p className="hb-page-copy">
            通过企业 IM 发起一对一协作，查看最近任务、能力分布和群组活跃情况；数据全部来自真实后端状态。
          </p>
        </div>
        <div className="hb-page-actions">
          <span className={`hb-pill ${toneForConnection(connectionStatus)}`}>
            {connectionStatus === 'connected' ? '渠道已连接' : connectionStatus === 'connecting' ? '连接中' : connectionStatus === 'error' ? '连接失败' : '未连接'}
          </span>
          <button
            type="button"
            onClick={() => setShowFeishuConfig(true)}
            className="hb-btn-primary"
          >
            <Settings2 size={14} />
            飞书渠道配置
          </button>
        </div>
      </div>

      <div className="hb-stat-grid">
        <div className="hb-stat-card">
          <div className="hb-stat-label"><Users size={14} /> 可协作数字员工</div>
          <div className="hb-stat-value">{activeEmployees.length}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">已上岗</div>
          <div className="hb-stat-value">{liveEmployees.length}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">实习中</div>
          <div className="hb-stat-value">{internEmployees.length}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">协作群数量</div>
          <div className="hb-stat-value">{groups.length}</div>
        </div>
      </div>

      <section className="hb-section mt-5">
        <div className="hb-section-head">
          <div>
            <h2 className="hb-section-title">协作群概览</h2>
            <p className="hb-section-copy">用最近活跃度和挂载数量快速判断数字员工的群聊落点。</p>
          </div>
        </div>
        {groups.length === 0 ? (
          <div className="hb-alert hb-alert-info">
            <MessageSquare size={14} />
            <span>当前还没有可展示的协作群。等后端同步群组后，这里会展示真实的 IM 群信息。</span>
          </div>
        ) : (
          <div className="grid gap-4 xl:grid-cols-3">
            {groups.slice(0, 6).map((group) => (
              <div key={group.groupId} className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="text-sm font-semibold text-[#0a0a0a]">{group.groupName}</div>
                    <div className="mt-1 text-xs text-[#737373]">{group.businessPurpose}</div>
                  </div>
                  <span className={`hb-pill ${group.status.includes('正常') ? 'green' : 'gray'}`}>{group.status}</span>
                </div>
                <div className="mt-4 flex flex-wrap gap-2">
                  <span className="hb-pill blue">{normalizePlatform(group.imPlatform)}</span>
                  <span className="hb-pill gray">{group.digitalEmployeeCount} 个数字员工</span>
                </div>
                <div className="mt-4 grid grid-cols-3 gap-3 text-xs text-[#737373]">
                  <div>
                    <div>成员数</div>
                    <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">{group.memberCount}</div>
                  </div>
                  <div>
                    <div>7天协作量</div>
                    <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">{group.collaborationVolume7d}</div>
                  </div>
                  <div>
                    <div>最近活跃</div>
                    <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">{formatRelativeDay(group.recentActivityTime)}</div>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>

      {error ? (
        <div className="hb-alert hb-alert-error mt-5">
          <AlertCircle size={14} />
          <span>{error}</span>
        </div>
      ) : null}

      <section className="hb-section mt-5">
        <div className="hb-section-head">
          <div>
            <h2 className="hb-section-title">员工协作列表</h2>
            <p className="hb-section-copy">切换不同阶段查看谁在对外协作、最近做了什么，以及能否直接发起对话。</p>
          </div>
        </div>

        <div className="hb-chip-row">
          <button
            type="button"
            className={`hb-chip ${activeSection === 'live' ? 'is-active' : ''}`}
            onClick={() => setActiveSection('live')}
          >
            已转正
            <span>{liveEmployees.length}</span>
          </button>
          <button
            type="button"
            className={`hb-chip ${activeSection === 'intern' ? 'is-active' : ''}`}
            onClick={() => setActiveSection('intern')}
          >
            实习中
            <span>{internEmployees.length}</span>
          </button>
        </div>

        <div className="mt-5">
          {loading && activeEmployees.length === 0 ? (
            <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-10 text-[#737373]">
              <Loader2 size={16} className="animate-spin" />
              加载协作数据中...
            </div>
          ) : activeEmployees.length === 0 ? (
            <div className="hb-empty">
              <div className="hb-empty-title">暂无可协作的数字员工</div>
              <div className="hb-empty-copy">先完成雇佣与上岗流程，后续这里会自动出现可以在 IM 中调度的员工。</div>
              <button type="button" className="hb-btn-primary" onClick={() => navigate('/template-pool')}>
                去模板池雇佣
              </button>
            </div>
          ) : (
            <div className="hb-list-shell">
              {visibleEmployees.map((employee) => {
                const task = buildRecentTask(employee)
                return (
                  <div key={employee.id} className="hb-list-row">
                    <div className="flex min-w-[240px] flex-1 items-center gap-3">
                      <span className="hb-squircle h-11 w-11 bg-[#dde9ff] text-[#3d5cff]">
                        {firstCharacter(employee.nickname)}
                      </span>
                      <div className="min-w-0">
                        <button
                          type="button"
                          className="block truncate text-left text-sm font-semibold text-[#0a0a0a]"
                          onClick={() => navigate(`/instances/${employee.id}`)}
                        >
                          {employee.nickname}
                        </button>
                        <div className="mt-1 truncate text-xs text-[#737373]">{employee.roleName || employee.sourceTemplate}</div>
                      </div>
                    </div>

                    <div className="flex min-w-[120px] flex-wrap gap-2">
                      <span className={`hb-pill ${activeSection === 'live' ? 'green' : 'orange'}`}>
                        {activeSection === 'live' ? '已转正' : '实习中'}
                      </span>
                      <span className="hb-pill gray">{getIMPlatformIcon(defaultPlatform)} {defaultPlatform}</span>
                    </div>

                    <div className="flex min-w-[220px] flex-1 flex-wrap gap-2">
                      {employee.capabilities.length > 0
                        ? employee.capabilities.slice(0, 4).map((capability, index) => (
                          <span key={capability.name} className={`hb-pill ${index % 2 === 0 ? 'blue' : 'gray'}`}>
                            {capability.name}
                          </span>
                        ))
                        : <span className="text-xs text-[#9ca3af]">暂无能力配置</span>}
                    </div>

                    <div className="min-w-[220px] flex-1 text-sm text-[#404040]">
                      <div className="line-clamp-2">{task?.text || '暂无最近任务记录'}</div>
                      <div className="mt-1 text-xs text-[#737373]">{task?.time || '等待同步'}</div>
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      <button
                        type="button"
                        onClick={() => navigate(`/instances/${employee.id}`)}
                        className="hb-btn-ghost !px-4 !py-2 !text-xs"
                      >
                        查看详情
                      </button>
                      <button
                        type="button"
                        onClick={() => openChat(employee)}
                        className="hb-btn-primary !px-4 !py-2 !text-xs"
                      >
                        <ExternalLink size={12} />
                        发起对话
                      </button>
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </div>
      </section>

      {showFeishuConfig ? (
        <div className="hb-modal-mask" onClick={() => setShowFeishuConfig(false)}>
          <div className="hb-modal max-w-[720px]" onClick={(event) => event.stopPropagation()}>
            <button type="button" className="hb-modal-close" onClick={() => setShowFeishuConfig(false)}>×</button>
            <div className="hb-modal-head">
              <h2 className="hb-modal-title">飞书渠道配置</h2>
              <p className="hb-modal-sub">配置数字员工在企业 IM 中的接入方式，当前仍为前端演示态。</p>
            </div>
            <div className="hb-modal-body">
              <div className="hb-chip-row mb-5">
                <button
                  type="button"
                  className={`hb-chip ${connectionType === 'websocket' ? 'is-active' : ''}`}
                  onClick={() => setConnectionType('websocket')}
                >
                  <Wifi size={14} />
                  WebSocket 长连接
                </button>
                <button
                  type="button"
                  className={`hb-chip ${connectionType === 'callback' ? 'is-active' : ''}`}
                  onClick={() => setConnectionType('callback')}
                >
                  <Link2 size={14} />
                  URL 回调
                </button>
              </div>

              {connectionType === 'websocket' ? (
                <div className="grid gap-4 md:grid-cols-2">
                  <label className="hb-field">
                    <span className="hb-field-label">App ID</span>
                    <input
                      value={feishuConfig.appId}
                      onChange={(event) => setFeishuConfig({ ...feishuConfig, appId: event.target.value })}
                      className="hb-input"
                      placeholder="填写飞书应用 App ID"
                    />
                  </label>
                  <label className="hb-field">
                    <span className="hb-field-label">App Secret</span>
                    <input
                      value={feishuConfig.appSecret}
                      onChange={(event) => setFeishuConfig({ ...feishuConfig, appSecret: event.target.value })}
                      className="hb-input"
                      placeholder="填写飞书应用 App Secret"
                    />
                  </label>
                  <label className="hb-field md:col-span-2">
                    <span className="hb-field-label">机器人名称</span>
                    <input
                      value={feishuConfig.botName}
                      onChange={(event) => setFeishuConfig({ ...feishuConfig, botName: event.target.value })}
                      className="hb-input"
                      placeholder="数字员工助理"
                    />
                  </label>
                </div>
              ) : (
                <div className="grid gap-4 md:grid-cols-2">
                  <label className="hb-field md:col-span-2">
                    <span className="hb-field-label">回调 URL</span>
                    <input
                      value={callbackConfig.callbackUrl}
                      onChange={(event) => setCallbackConfig({ ...callbackConfig, callbackUrl: event.target.value })}
                      className="hb-input"
                      placeholder="https://your-domain.com/feishu/callback"
                    />
                    <span className="hb-field-help">飞书会向这个地址推送事件通知。</span>
                  </label>
                  <label className="hb-field">
                    <span className="hb-field-label">Verification Token</span>
                    <input
                      value={callbackConfig.verificationToken}
                      onChange={(event) => setCallbackConfig({ ...callbackConfig, verificationToken: event.target.value })}
                      className="hb-input"
                    />
                  </label>
                  <label className="hb-field">
                    <span className="hb-field-label">Encrypt Key（可选）</span>
                    <input
                      value={callbackConfig.encryptKey}
                      onChange={(event) => setCallbackConfig({ ...callbackConfig, encryptKey: event.target.value })}
                      className="hb-input"
                    />
                  </label>
                </div>
              )}

              <div className="mt-5 space-y-3">
                {connectionStatus === 'connected' ? (
                  <div className="hb-alert hb-alert-success">
                    <CheckCircle2 size={14} />
                    <span>{connectionType === 'websocket' ? 'WebSocket 已连接。' : '回调地址已注册。'}</span>
                  </div>
                ) : connectionStatus === 'error' ? (
                  <div className="hb-alert hb-alert-error">
                    <AlertCircle size={14} />
                    <span>连接失败，请检查配置项后重试。</span>
                  </div>
                ) : connectionStatus === 'connecting' ? (
                  <div className="hb-alert hb-alert-info">
                    <Loader2 size={14} className="animate-spin" />
                    <span>正在建立连接，请稍候...</span>
                  </div>
                ) : null}

                {configSaved && webSocketUrl && connectionType === 'websocket' ? (
                  <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-3 text-sm text-[#404040]">
                    <div className="font-medium text-[#0a0a0a]">已生成连接地址</div>
                    <div className="mt-2 break-all rounded-xl bg-white px-3 py-2 text-xs text-[#525252]">{webSocketUrl}</div>
                  </div>
                ) : null}
              </div>
            </div>
            <div className="hb-modal-foot">
              {webSocketUrl && connectionStatus !== 'connected' && connectionType === 'websocket' ? (
                <button type="button" className="hb-btn-ghost" onClick={handleCopyWsUrl}>
                  <Copy size={14} />
                  复制连接地址
                </button>
              ) : null}
              {connectionStatus === 'connected' ? (
                <button type="button" className="hb-btn-ghost" onClick={handleDisconnect}>
                  断开连接
                </button>
              ) : (
                <>
                  <button
                    type="button"
                    className="hb-btn-ghost"
                    onClick={() => void handleSaveFeishuConfig()}
                    disabled={saveConfigLoading}
                  >
                    {saveConfigLoading ? <Loader2 size={14} className="animate-spin" /> : <Check size={14} />}
                    保存配置
                  </button>
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => void handleConnect()}
                  >
                    {connectionType === 'websocket' ? <Wifi size={14} /> : <RefreshCw size={14} />}
                    {connectionType === 'websocket' ? '建立连接' : '注册回调'}
                  </button>
                </>
              )}
            </div>
          </div>
        </div>
      ) : null}
    </div>
  )
}
