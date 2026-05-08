import { useEffect, useMemo, useState } from 'react'
import {
  AlertCircle,
  ArrowLeft,
  Bot,
  CheckCircle2,
  Copy,
  Loader2,
  RefreshCw,
  Settings2,
  ShieldCheck,
  Trash2,
  Wifi,
  Link2,
} from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { useUxOverlay } from '@/app/context/UxOverlayContext'
import {
  api,
  type EmployeeDetail,
  type ImConfigItem,
  type ImConfigRequest,
  type ImConnectionMode,
  type ImPlatformId,
} from '@/infra/api'
import {
  firstCharacter,
  ownershipClass,
  ownershipLabel,
  statusClass,
  statusLabel,
  toEmployeeDetailSummary,
  withEmployeeView,
} from '@/features/hiring/pages/employeeView'

type DraftValueKey = Exclude<keyof ImConfigRequest, 'connectionMode'>

type PlatformSchema = {
  label: string
  accent: 'blue' | 'orange' | 'green'
  intro: string
  defaultMode: ImConnectionMode
  modes: Record<ImConnectionMode, {
    label: string
    help: string
    allowed: boolean
    fields: Array<{
      key: DraftValueKey
      label: string
      placeholder: string
      required: boolean
      type?: 'text' | 'password'
    }>
  }>
}

const PLATFORM_ORDER: ImPlatformId[] = ['feishu', 'dingtalk', 'wecom']

const PLATFORM_SCHEMAS: Record<ImPlatformId, PlatformSchema> = {
  feishu: {
    label: '飞书',
    accent: 'blue',
    intro: '飞书支持 WebSocket 长连接与 URL 回调两种接入方式，适合站内会话完成后直接拉起企业 IM。',
    defaultMode: 'websocket',
    modes: {
      websocket: {
        label: 'WebSocket 长连接',
        help: '通过长连接方式接收飞书事件，适合先跑通机器人再补回调网关。',
        allowed: true,
        fields: [
          { key: 'appId', label: 'App ID', placeholder: '请输入飞书自建应用 app_id', required: true },
          { key: 'appSecret', label: 'App Secret', placeholder: '请输入飞书自建应用 app_secret', required: true, type: 'password' },
        ],
      },
      url_callback: {
        label: 'URL 回调',
        help: '回调模式需要提供 Encrypt Key，Verification Token 可选。',
        allowed: true,
        fields: [
          { key: 'appId', label: 'App ID', placeholder: '请输入飞书自建应用 app_id', required: true },
          { key: 'appSecret', label: 'App Secret', placeholder: '请输入飞书自建应用 app_secret', required: true, type: 'password' },
          { key: 'encryptKey', label: 'Encrypt Key', placeholder: '请输入 Encrypt Key', required: true, type: 'password' },
          { key: 'verificationToken', label: 'Verification Token', placeholder: '可选，未填写则按默认验签', required: false, type: 'password' },
        ],
      },
    },
  },
  dingtalk: {
    label: '钉钉',
    accent: 'orange',
    intro: '钉钉支持 WebSocket 与 URL 回调两种模式，WebSocket 模式额外需要 AgentID。',
    defaultMode: 'websocket',
    modes: {
      websocket: {
        label: 'WebSocket 长连接',
        help: '长连接模式适合快速接入，当前实现需要 AgentID、App ID 和 App Secret。',
        allowed: true,
        fields: [
          { key: 'appId', label: 'App ID', placeholder: '请输入钉钉 ClientID（App Key）', required: true },
          { key: 'appSecret', label: 'App Secret', placeholder: '请输入钉钉 App Secret', required: true, type: 'password' },
          { key: 'agentId', label: 'Agent ID', placeholder: '请输入钉钉 AgentID', required: true },
        ],
      },
      url_callback: {
        label: 'URL 回调',
        help: '回调模式支持补充 Token 与 AES Key，适合已有企业回调网关。',
        allowed: true,
        fields: [
          { key: 'appId', label: 'App ID', placeholder: '请输入钉钉 ClientID', required: true },
          { key: 'appSecret', label: 'App Secret', placeholder: '请输入钉钉 App Secret', required: true, type: 'password' },
          { key: 'encryptKey', label: 'Encrypt Key', placeholder: '请输入消息加密密钥', required: true, type: 'password' },
          { key: 'agentId', label: 'Agent ID', placeholder: '请输入钉钉 AgentID', required: true },
          { key: 'token', label: 'Token', placeholder: '可选，签名校验 Token', required: false, type: 'password' },
          { key: 'aesKey', label: 'AES Key', placeholder: '可选，消息体 AES 解密密钥', required: false, type: 'password' },
        ],
      },
    },
  },
  wecom: {
    label: '企微',
    accent: 'green',
    intro: '企业微信当前仅支持 URL 回调模式，保存后会生成对应的回调地址供企业后台配置。',
    defaultMode: 'url_callback',
    modes: {
      websocket: {
        label: 'WebSocket 长连接',
        help: '企业微信当前不支持 WebSocket 长连接。',
        allowed: false,
        fields: [],
      },
      url_callback: {
        label: 'URL 回调',
        help: '企业微信需要 corpId、AgentID、AgentSecret、Token 与 AES Key 才能完成接入。',
        allowed: true,
        fields: [
          { key: 'corpId', label: 'Corp ID', placeholder: '请输入企业微信 CorpID', required: true },
          { key: 'agentId', label: 'Agent ID', placeholder: '请输入企微 AgentID', required: true },
          { key: 'agentSecret', label: 'Agent Secret', placeholder: '请输入企微应用 Secret', required: true, type: 'password' },
          { key: 'token', label: 'Token', placeholder: '请输入回调 Token', required: true, type: 'password' },
          { key: 'aesKey', label: 'AES Key', placeholder: '请输入 EncodingAESKey', required: true, type: 'password' },
        ],
      },
    },
  },
}

const DEFAULT_DRAFT = (platform: ImPlatformId): ImConfigRequest => ({
  connectionMode: PLATFORM_SCHEMAS[platform].defaultMode,
  appId: '',
  appSecret: '',
  encryptKey: '',
  token: '',
  aesKey: '',
  verificationToken: '',
  corpId: '',
  agentId: '',
  agentSecret: '',
})

function draftFromConfig(platform: ImPlatformId, config: ImConfigItem | null): ImConfigRequest {
  const draft = DEFAULT_DRAFT(platform)
  if (!config) {
    return draft
  }

  return {
    ...draft,
    connectionMode:
      config.connectionMode === 'websocket' || config.connectionMode === 'url_callback'
        ? config.connectionMode
        : draft.connectionMode,
    appId: config.appId ?? '',
    appSecret: config.appSecret ?? '',
    encryptKey: config.encryptKey ?? '',
    token: config.token ?? '',
    aesKey: config.aesKey ?? '',
    verificationToken: config.verificationToken ?? '',
    corpId: config.corpId ?? '',
    agentId: config.agentId ?? '',
    agentSecret: config.agentSecret ?? '',
  }
}

function toConfigMap(items: ImConfigItem[]) {
  return PLATFORM_ORDER.reduce<Record<ImPlatformId, ImConfigItem | null>>((acc, platform) => {
    acc[platform] = items.find((item) => item.platform === platform) ?? null
    return acc
  }, {
    feishu: null,
    dingtalk: null,
    wecom: null,
  })
}

function statusTone(item: ImConfigItem | null): 'green' | 'orange' | 'gray' {
  if (!item) return 'gray'
  if (item.lastError) return 'orange'
  if (item.status === 'active') return 'green'
  return 'gray'
}

function statusText(item: ImConfigItem | null) {
  if (!item) return '未配置'
  if (item.lastError) return '配置异常'
  if (item.status === 'active') return '已连接'
  return item.status
}

function fieldType(key: DraftValueKey): 'text' | 'password' {
  if (key === 'appSecret' || key === 'encryptKey' || key === 'token' || key === 'aesKey' || key === 'verificationToken' || key === 'agentSecret') {
    return 'password'
  }
  return 'text'
}

function isPersonalAssetOwnership(ownership?: string | null) {
  return ownership === 'personal_clone' || ownership === 'private_branch'
}

export default function InstanceImConfigPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { showToast } = useUxOverlay()

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [selectedPlatform, setSelectedPlatform] = useState<ImPlatformId>('feishu')
  const [configMap, setConfigMap] = useState<Record<ImPlatformId, ImConfigItem | null>>({
    feishu: null,
    dingtalk: null,
    wecom: null,
  })
  const [drafts, setDrafts] = useState<Record<ImPlatformId, ImConfigRequest>>({
    feishu: DEFAULT_DRAFT('feishu'),
    dingtalk: DEFAULT_DRAFT('dingtalk'),
    wecom: DEFAULT_DRAFT('wecom'),
  })
  const [webhookUrl, setWebhookUrl] = useState('')
  const [webhookLoading, setWebhookLoading] = useState(false)

  const employeeView = useMemo(() => {
    if (!employee) return null
    return withEmployeeView(toEmployeeDetailSummary(employee))
  }, [employee])

  const isPersonalAsset = employeeView ? isPersonalAssetOwnership(employeeView.ownership) : false
  const currentSchema = PLATFORM_SCHEMAS[selectedPlatform]
  const currentMode = drafts[selectedPlatform].connectionMode
  const currentModeSchema = currentSchema.modes[currentMode]
  const currentConfig = configMap[selectedPlatform]
  const configuredCount = PLATFORM_ORDER.filter((platform) => configMap[platform]?.status === 'active').length

  async function loadPage() {
    if (!id) return

    setLoading(true)
    setError('')
    setNotice('')
    try {
      const [detail, configs] = await Promise.all([
        api.employeeRuntime.getEmployee(id),
        api.employeeRuntime.getImConfigs(id),
      ])

      const nextConfigMap = toConfigMap(configs.configs)
      setEmployee(detail)
      setConfigMap(nextConfigMap)
      setDrafts((previous) => {
        const next = { ...previous }
        PLATFORM_ORDER.forEach((platform) => {
          const existing = nextConfigMap[platform]
          if (existing) {
            next[platform] = draftFromConfig(platform, existing)
          }
        })
        return next
      })
      const firstActive = PLATFORM_ORDER.find((platform) => nextConfigMap[platform]?.status === 'active')
      if (firstActive) {
        setSelectedPlatform(firstActive)
      }
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '加载 IM 配置失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadPage()
  }, [id])

  useEffect(() => {
    if (!id || !employeeView || !isPersonalAsset) {
      setWebhookUrl('')
      setWebhookLoading(false)
      return
    }

    let cancelled = false
    setWebhookLoading(true)
    api.employeeRuntime.getImWebhookUrl(id, selectedPlatform)
      .then((result) => {
        if (!cancelled) {
          setWebhookUrl(result.webhookUrl)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setWebhookUrl('')
        }
      })
      .finally(() => {
        if (!cancelled) {
          setWebhookLoading(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [id, employeeView, isPersonalAsset, selectedPlatform])

  useEffect(() => {
    if (!currentModeSchema.allowed) {
      setDrafts((previous) => ({
        ...previous,
        [selectedPlatform]: {
          ...previous[selectedPlatform],
          connectionMode: currentSchema.defaultMode,
        },
      }))
    }
  }, [currentModeSchema.allowed, currentSchema.defaultMode, selectedPlatform])

  function updateField(key: DraftValueKey, value: string) {
    setDrafts((previous) => ({
      ...previous,
      [selectedPlatform]: {
        ...previous[selectedPlatform],
        [key]: value,
      },
    }))
  }

  function changeMode(mode: ImConnectionMode) {
    if (!currentSchema.modes[mode].allowed) {
      return
    }

    setDrafts((previous) => ({
      ...previous,
      [selectedPlatform]: {
        ...previous[selectedPlatform],
        connectionMode: mode,
      },
    }))
  }

  function selectPlatform(platform: ImPlatformId) {
    setSelectedPlatform(platform)
  }

  async function refreshConfigs() {
    if (!id) return

    const configs = await api.employeeRuntime.getImConfigs(id)
    const nextConfigMap = toConfigMap(configs.configs)
    setConfigMap(nextConfigMap)
    setDrafts((previous) => {
      const next = { ...previous }
      PLATFORM_ORDER.forEach((platform) => {
        const existing = nextConfigMap[platform]
        if (existing) {
          next[platform] = draftFromConfig(platform, existing)
        }
      })
      return next
    })
  }

  async function saveConfig() {
    if (!id) return
    if (!isPersonalAsset) {
      showToast('部门员工不配置 IM，请先创建个人分身', 'error')
      return
    }

    const draft = drafts[selectedPlatform]
    const requiredFields = currentModeSchema.fields.filter((field) => field.required)
    const missing = requiredFields.find((field) => !(draft[field.key] ?? '').trim())
    if (missing) {
      showToast(`${missing.label} 必填`, 'error')
      return
    }

    setSaving(true)
    setError('')
    setNotice('')
    try {
      const result = await api.employeeRuntime.upsertImConfig(id, selectedPlatform, draft)
      setNotice(`${currentSchema.label} · ${result.message}`)
      showToast(`${currentSchema.label} 配置已保存`, 'success')
      await refreshConfigs()
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '保存 IM 配置失败')
    } finally {
      setSaving(false)
    }
  }

  async function deleteConfig() {
    if (!id) return
    if (!isPersonalAsset) {
      showToast('部门员工不配置 IM，请先创建个人分身', 'error')
      return
    }

    if (!window.confirm(`确认撤销 ${currentSchema.label} 配置吗？`)) {
      return
    }

    setSaving(true)
    setError('')
    try {
      await api.employeeRuntime.deleteImConfig(id, selectedPlatform)
      setNotice(`${currentSchema.label} 配置已撤销`)
      showToast(`${currentSchema.label} 绑定已解除`, 'success')
      setDrafts((previous) => ({
        ...previous,
        [selectedPlatform]: DEFAULT_DRAFT(selectedPlatform),
      }))
      await refreshConfigs()
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '删除 IM 配置失败')
    } finally {
      setSaving(false)
    }
  }

  function copyWebhookUrl() {
    if (!webhookUrl) return
    void navigator.clipboard.writeText(webhookUrl)
    showToast('Webhook 地址已复制', 'success')
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载 IM 配置页...
        </div>
      </div>
    )
  }

  if (!employee || !employeeView) {
    return (
      <div className="hb-page space-y-4">
        <button type="button" onClick={() => navigate('/department-employees')} className="hb-btn-ghost">
          <ArrowLeft size={14} />
          返回员工列表
        </button>
        <div className="hb-card p-8 text-sm text-[#737373]">实例不存在</div>
      </div>
    )
  }

  return (
    <div className="hb-page space-y-5">
      <button type="button" onClick={() => navigate(`/instances/${employee.employeeId}`)} className="hb-btn-ghost">
        <ArrowLeft size={14} />
        返回实例详情
      </button>

      {error ? (
        <div className="hb-alert hb-alert-error">
          <AlertCircle size={14} />
          <span>{error}</span>
        </div>
      ) : null}

      {notice ? (
        <div className="hb-alert hb-alert-success">
          <CheckCircle2 size={14} />
          <span>{notice}</span>
        </div>
      ) : null}

      <section className="hb-card p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <span className="hb-kicker">多平台 IM 接入</span>
            <h1 className="hb-page-title">IM 配置 · {employee.nickname}</h1>
            <p className="hb-page-copy">
              对齐原型里的多平台接入流，支持飞书 / 钉钉 / 企微的本地配置、Webhook 生成和解绑。
            </p>
          </div>
          <div className="flex flex-col items-end gap-2">
            <span className={`hb-pill ${statusClass(employeeView.mappedStatus, employeeView.lifecycleStatus)}`}>
              {statusLabel(employeeView.mappedStatus, employeeView.lifecycleStatus)}
            </span>
            <span className={`hb-pill ${ownershipClass(employeeView.ownership)}`}>{ownershipLabel(employeeView.ownership)}</span>
          </div>
        </div>
      </section>

      {!isPersonalAsset ? (
        <section className="hb-card p-6">
          <div className="flex items-start justify-between gap-4">
            <div>
              <div className="flex items-center gap-2 text-base font-semibold text-[#0a0a0a]">
                <Bot size={16} />
                部门员工不配置 IM
              </div>
              <p className="mt-2 max-w-2xl text-sm leading-relaxed text-[#737373]">
                IM 接入属于个人分身和私有分支层面的可选动作。先复制一个分身，再到这里完成飞书 / 钉钉 / 企微绑定。
              </p>
            </div>
            <button type="button" className="hb-btn-primary" onClick={() => navigate(`/clone/${employee.employeeId}`)}>
              去复制分身
            </button>
          </div>
        </section>
      ) : (
        <>
          <section className="hb-stat-grid">
            <div className="hb-stat-card">
              <div className="hb-stat-label">已配置平台</div>
              <div className="hb-stat-value">{configuredCount}</div>
            </div>
            <div className="hb-stat-card">
              <div className="hb-stat-label">当前平台</div>
              <div className="hb-stat-value">{PLATFORM_SCHEMAS[selectedPlatform].label}</div>
            </div>
            <div className="hb-stat-card">
              <div className="hb-stat-label">当前模式</div>
              <div className="hb-stat-value">{currentModeSchema.label}</div>
            </div>
            <div className="hb-stat-card">
              <div className="hb-stat-label">Webhook</div>
              <div className="hb-stat-value">{webhookUrl ? '已生成' : '待生成'}</div>
            </div>
          </section>

          <section className="hb-section">
            <div className="hb-section-head">
              <div>
                <h2 className="hb-section-title">平台选择</h2>
                <p className="hb-section-copy">每个平台独立配置，保存后会写入实例 IM 配置表，不会覆盖其他平台。</p>
              </div>
            </div>

            <div className="grid gap-4 xl:grid-cols-3">
              {PLATFORM_ORDER.map((platform) => {
                const schema = PLATFORM_SCHEMAS[platform]
                const item = configMap[platform]
                const active = selectedPlatform === platform
                const tone = statusTone(item)
                const modeLabel = item?.connectionMode ? schema.modes[item.connectionMode].label : '尚未选择模式'
                const configuredAt = item?.configuredAt ? new Date(item.configuredAt).toLocaleString('zh-CN', { hour12: false }) : '—'

                return (
                  <button
                    key={platform}
                    type="button"
                    onClick={() => selectPlatform(platform)}
                    className={`hb-card p-5 text-left transition-transform duration-150 hover:-translate-y-0.5 ${active ? 'ring-2 ring-[#4a6cf7]/20' : ''}`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="flex items-center gap-2 text-base font-semibold text-[#0a0a0a]">
                          <span className={`hb-squircle h-8 w-8 ${schema.accent === 'blue' ? 'bg-[#dde9ff] text-[#3d5cff]' : schema.accent === 'orange' ? 'bg-[#fff0df] text-[#b45309]' : 'bg-[#e7f9ee] text-[#15803d]'}`}>
                            {firstCharacter(schema.label)}
                          </span>
                          {schema.label}
                        </div>
                        <p className="mt-2 text-sm leading-relaxed text-[#737373]">{schema.intro}</p>
                      </div>
                      <span className={`hb-pill ${tone}`}>{statusText(item)}</span>
                    </div>

                    <div className="mt-4 flex flex-wrap gap-2">
                      <span className="hb-pill gray">{modeLabel}</span>
                      <span className="hb-pill blue">{item?.webhookPath ? 'Webhook 已生成' : '待配置 Webhook'}</span>
                    </div>

                    <div className="mt-4 grid grid-cols-2 gap-3 text-xs text-[#737373]">
                      <div>
                        <div>绑定时间</div>
                        <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">{configuredAt}</div>
                      </div>
                      <div>
                        <div>回调路径</div>
                        <div className="mt-1 truncate text-sm font-semibold text-[#0a0a0a]">{item?.webhookPath || '待生成'}</div>
                      </div>
                    </div>
                  </button>
                )
              })}
            </div>
          </section>

          <section className="hb-detail-split">
            <div className="hb-card hb-detail-panel">
              <div className="hb-detail-section-head">
                <div>
                  <h2 className="hb-section-heading !mb-0">{currentSchema.label} 配置</h2>
                  <p className="mt-2 text-sm text-[#737373]">{currentSchema.intro}</p>
                </div>
                <span className={`hb-pill ${statusTone(currentConfig)}`}>{statusText(currentConfig)}</span>
              </div>

              <div className="mt-5 hb-chip-row">
                {Object.entries(currentSchema.modes).map(([mode, spec]) => (
                  <button
                    key={mode}
                    type="button"
                    className={`hb-chip ${currentMode === mode ? 'is-active' : ''}`}
                    onClick={() => changeMode(mode as ImConnectionMode)}
                    disabled={!spec.allowed}
                  >
                    {spec.label}
                    {!spec.allowed ? <span>暂不可用</span> : null}
                  </button>
                ))}
              </div>

              <div className="mt-4 hb-callout info">
                {currentModeSchema.help}
              </div>

              {!currentModeSchema.allowed ? (
                <div className="mt-4 hb-alert hb-alert-warn">
                  <ShieldCheck size={14} />
                  <span>当前平台暂不支持此接入方式，请切换到可用模式。</span>
                </div>
              ) : null}

              <div className="mt-5 grid gap-4 md:grid-cols-2">
                {currentModeSchema.fields.map((field) => (
                  <label key={field.key} className="hb-field md:col-span-1">
                    <span className="hb-field-label">
                      {field.label} {field.required ? '*' : ''}
                    </span>
                    <input
                      type={field.type ?? fieldType(field.key)}
                      value={drafts[selectedPlatform][field.key] ?? ''}
                      onChange={(event) => updateField(field.key, event.target.value)}
                      className="hb-input"
                      placeholder={field.placeholder}
                      disabled={saving}
                    />
                    {field.required ? null : <span className="hb-field-help">可选项，可在平台后台补充。</span>}
                  </label>
                ))}
              </div>

              <div className="mt-5 rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="text-sm font-semibold text-[#0a0a0a]">Webhook URL</div>
                    <div className="mt-1 text-xs text-[#737373]">
                      {webhookLoading ? '正在获取当前平台的回调地址...' : '保存前后都可以复制到对应平台后台。'}
                    </div>
                  </div>
                  <button
                    type="button"
                    className="hb-btn-ghost !px-3 !py-1.5 !text-xs"
                    onClick={() => void refreshConfigs()}
                    disabled={saving || webhookLoading}
                  >
                    <RefreshCw size={12} />
                    刷新状态
                  </button>
                </div>
                <div className="mt-3 flex flex-wrap items-center gap-2">
                  <div className="min-w-0 flex-1 break-all rounded-xl bg-white px-3 py-2 text-xs text-[#404040]">
                    {webhookUrl || 'Webhook 地址尚未加载'}
                  </div>
                  <button
                    type="button"
                    className="hb-btn-ghost !px-3 !py-1.5 !text-xs"
                    onClick={copyWebhookUrl}
                    disabled={!webhookUrl}
                  >
                    <Copy size={12} />
                    复制地址
                  </button>
                </div>
              </div>

              <div className="mt-5 hb-callout success">
                <CheckCircle2 size={16} />
                <div>
                  <div className="font-semibold text-[#0a0a0a]">保存后会加密入库</div>
                  <div className="mt-1 text-sm text-[#404040]">
                    已保存的凭证会在再次打开页面时回填，方便本地联调和更新平台配置。
                  </div>
                </div>
              </div>

              <div className="mt-5 flex flex-wrap justify-end gap-2">
                <button type="button" className="hb-btn-ghost" onClick={() => navigate(`/instances/${employee.employeeId}`)}>
                  取消
                </button>
                <button type="button" className="hb-btn-ghost" onClick={() => void deleteConfig()} disabled={saving || !currentConfig}>
                  <Trash2 size={14} />
                  撤销绑定
                </button>
                <button type="button" className="hb-btn-primary" onClick={() => void saveConfig()} disabled={saving}>
                  {saving ? <Loader2 size={14} className="animate-spin" /> : <Settings2 size={14} />}
                  {currentConfig ? '保存并更新' : '保存配置'}
                </button>
              </div>
            </div>

            <div className="hb-card hb-detail-panel">
              <h2 className="hb-section-heading">接入说明</h2>
              <div className="space-y-3">
                <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                  <div className="text-sm font-semibold text-[#0a0a0a]">1. 选择平台</div>
                  <div className="mt-1 text-sm text-[#737373]">飞书、钉钉与企微分别独立存储，互不覆盖。</div>
                </div>
                <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                  <div className="text-sm font-semibold text-[#0a0a0a]">2. 填写凭证</div>
                  <div className="mt-1 text-sm text-[#737373]">按所选模式补齐必填项，后台会做格式校验并加密保存。</div>
                </div>
                <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                  <div className="text-sm font-semibold text-[#0a0a0a]">3. 复制 Webhook</div>
                  <div className="mt-1 text-sm text-[#737373]">把当前页生成的回调地址填到对应 IM 平台后台即可。</div>
                </div>
              </div>

              <div className="mt-5 hb-callout info">
                <Wifi size={16} />
                <div>
                  <div className="font-semibold text-[#0a0a0a]">站内会话与 IM 独立</div>
                  <div className="mt-1 text-sm text-[#404040]">
                    这页只负责平台接入配置；配置完成后，你仍然可以从实例详情页进入站内对话或直接去 IM 使用。
                  </div>
                </div>
              </div>

              <div className="mt-5 rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                <div className="flex items-center gap-2 text-sm font-semibold text-[#0a0a0a]">
                  <Link2 size={14} />
                  当前实例信息
                </div>
                <div className="mt-3 grid gap-3 text-sm text-[#404040]">
                  <div className="flex items-center justify-between gap-3">
                    <span>实例名称</span>
                    <span className="font-medium text-[#0a0a0a]">{employee.nickname}</span>
                  </div>
                  <div className="flex items-center justify-between gap-3">
                    <span>实例 ID</span>
                    <span className="font-medium text-[#0a0a0a]">{employee.employeeId}</span>
                  </div>
                  <div className="flex items-center justify-between gap-3">
                    <span>所属部门</span>
                    <span className="font-medium text-[#0a0a0a]">{employee.departmentId || employee.owningTeam}</span>
                  </div>
                  <div className="flex items-center justify-between gap-3">
                    <span>状态</span>
                    <span className="font-medium text-[#0a0a0a]">{statusLabel(employeeView.mappedStatus, employeeView.lifecycleStatus)}</span>
                  </div>
                </div>
              </div>
            </div>
          </section>
        </>
      )}
    </div>
  )
}
