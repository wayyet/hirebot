import { useEffect, useMemo, useState } from 'react'
import {
  AlertCircle,
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  Copy,
  ExternalLink,
  GitBranch,
  Loader2,
  MessageSquare,
  Paperclip,
  Play,
  Plus,
  RefreshCw,
  Search,
  Send,
  ShieldCheck,
  Sparkles,
  Upload,
  X,
} from 'lucide-react'
import { api, type EmployeeDetail, type EmployeeSummary, type EmployeeTemplateCard, type EmployeeTemplateDetail, type EvaluationSandboxConversationState, type EvaluationState, type HiringConversationMessage, type HiringConversationTimeline, type HiringStagePreview, type HiringWorkflowState, type TeamImItem } from '@/infra/api'

type PrototypeRole = 'manager' | 'member'
type PrototypeView =
  | 'templates'
  | 'dept'
  | 'my'
  | 'template'
  | 'employee'
  | 'hire'
  | 'ai-eval'
  | 'human-eval'
  | 'review'
  | 'publish'
  | 'im'
  | 'chat'
  | 'clone'
  | 'quick-clone'
  | 'branch'

type ToastKind = 'success' | 'info' | 'error'

type Toast = {
  id: string
  text: string
  kind: ToastKind
}

type LocalChatMessage = {
  id: string
  role: 'user' | 'bot'
  content: string
  createdAt: string
}

type ImChannelId = 'lark' | 'dingding' | 'wecom'
type ImMethodId = 'websocket' | 'callback'
type ImBindingStatus = 'connected' | 'error' | 'unconfigured'

type ImField = {
  key: string
  label: string
  required: boolean
  placeholder: string
}

type ImBinding = {
  channelId: ImChannelId
  methodId: ImMethodId
  form: Record<string, string>
  status: Exclude<ImBindingStatus, 'unconfigured'>
  connectedAt: string
}

type ImBindingStore = Record<string, Partial<Record<ImChannelId, ImBinding>>>

type ImStep = {
  name: string
  done: boolean
}

const ROLE_KEY = 'hirebot_prototype_role_v2'
const VIEW_KEY = 'hirebot_prototype_view_v2'
const TEMPLATE_KEY = 'hirebot_prototype_selected_template_v2'
const EMPLOYEE_KEY = 'hirebot_prototype_selected_employee_v2'
const HIRE_KEY = 'hirebot_prototype_hire_v2'
const CHAT_KEY = 'hirebot_prototype_chat_v2'
const FIXTURE_SEED_KEY = 'hirebot_prototype_fixture_seeded_v1'
const FIXTURE_AUTO_ATTEMPT_KEY = 'hirebot_prototype_fixture_auto_attempt_v1'
const IM_BINDING_KEY = 'hirebot_prototype_im_bindings_v1'

const IM_CHANNEL_META: Record<ImChannelId, { short: string; name: string; accent: string }> = {
  lark: { short: '飞', name: '飞书', accent: 'blue' },
  dingding: { short: '钉', name: '钉钉', accent: 'orange' },
  wecom: { short: '企', name: '企微', accent: 'green' },
}

const IM_SCHEMAS: Record<ImChannelId, {
  title: string
  intro: string
  methods: Record<ImMethodId, {
    label: string
    help: string
    steps: string[]
    fields: ImField[]
    webhookPath?: string
  }>
}> = {
  lark: {
    title: '飞书',
    intro: '输入飞书应用凭据以将此数字员工绑定到飞书机器人。App ID 和 App Secret 为必填项。',
    methods: {
      websocket: {
        label: 'WebSocket 长连接（推荐）',
        help: '通过长连接方式接收飞书事件，适合开箱即用接入。',
        steps: ['校验 App ID / App Secret', '建立 WebSocket 长连接', '注册实例绑定', '同步连接状态'],
        fields: [
          { key: 'appId', label: 'App ID', required: true, placeholder: '请输入飞书自建应用 app_id' },
          { key: 'appSecret', label: 'App Secret', required: true, placeholder: '请输入飞书自建应用 app_secret' },
        ],
      },
      callback: {
        label: '使用 URL 回调',
        help: '回调模式下需要额外提供 Encrypt Key 和可选的 Verification Token。',
        steps: ['校验回调凭据', '注册 Webhook URL', '验证 Encrypt Key', '同步连接状态'],
        fields: [
          { key: 'appId', label: 'App ID', required: true, placeholder: '请输入飞书自建应用 app_id' },
          { key: 'appSecret', label: 'App Secret', required: true, placeholder: '请输入飞书自建应用 app_secret' },
          { key: 'encryptKey', label: 'Encrypt Key', required: true, placeholder: '请输入 Encrypt Key' },
          { key: 'verificationToken', label: 'Verification Token', required: false, placeholder: '可选，未填写则按默认验签' },
        ],
        webhookPath: 'feishu',
      },
    },
  },
  dingding: {
    title: '钉钉',
    intro: '输入钉钉机器人凭据以将此数字员工绑定到钉钉机器人。App ID 和 App Secret 为必填项。',
    methods: {
      websocket: {
        label: 'WebSocket 长连接（推荐）',
        help: '长连接模式适合快速接入，不需要额外配置回调网关。',
        steps: ['校验 ClientID / Secret', '建立 WebSocket 长连接', '注册实例绑定', '同步连接状态'],
        fields: [
          { key: 'appId', label: 'App ID', required: true, placeholder: '请输入钉钉 ClientID（App Key）' },
          { key: 'appSecret', label: 'App Secret', required: true, placeholder: '请输入钉钉 App Secret' },
        ],
      },
      callback: {
        label: '使用 URL 回调',
        help: '回调模式支持补充 Token 与 AES Key，适合已有企业回调网关。',
        steps: ['校验回调凭据', '注册 Webhook URL', '验证 Encrypt Key / AES Key', '同步连接状态'],
        fields: [
          { key: 'appId', label: 'App ID', required: true, placeholder: '请输入钉钉 ClientID' },
          { key: 'appSecret', label: 'App Secret', required: true, placeholder: '请输入钉钉 App Secret' },
          { key: 'encryptKey', label: 'Encrypt Key', required: true, placeholder: '请输入消息加密密钥' },
          { key: 'token', label: 'Token', required: false, placeholder: '可选，签名校验 Token' },
          { key: 'aesKey', label: 'AES Key', required: false, placeholder: '可选，消息体 AES 解密密钥' },
        ],
        webhookPath: 'dingtalk',
      },
    },
  },
  wecom: {
    title: '企微',
    intro: '选择连接方式并输入对应凭据，以将此数字员工绑定到企微 AIBot。',
    methods: {
      websocket: {
        label: 'WebSocket 长连接（推荐）',
        help: '直接输入 AgentID 与 Secret 即可建立企微长连接。',
        steps: ['校验 AgentID / Secret', '建立 WebSocket 长连接', '注册实例绑定', '同步连接状态'],
        fields: [
          { key: 'appId', label: 'App ID', required: true, placeholder: '请输入企微 AgentID' },
          { key: 'appSecret', label: 'App Secret', required: true, placeholder: '请输入企微应用 Secret' },
        ],
      },
      callback: {
        label: '使用 URL 回调',
        help: 'URL 回调模式只需要 Token 与 EncodingAESKey 完成验签。',
        steps: ['校验回调凭据', '注册 Webhook URL', '验证 Token / EncodingAESKey', '同步连接状态'],
        fields: [
          { key: 'token', label: 'Token', required: true, placeholder: '请输入回调 Token' },
          { key: 'encodingAesKey', label: 'EncodingAESKey', required: true, placeholder: '请输入 EncodingAESKey' },
        ],
        webhookPath: 'wecom',
      },
    },
  },
}

function readStorage<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key)
    if (!raw) return fallback
    return JSON.parse(raw) as T
  } catch {
    return fallback
  }
}

function writeStorage(key: string, value: unknown) {
  try {
    localStorage.setItem(key, JSON.stringify(value))
  } catch {
    // ignore storage failures in prototype mode
  }
}

function mkId(prefix = 'id') {
  return `${prefix}_${Math.random().toString(36).slice(2, 8)}`
}

function firstChar(value: string) {
  return value.trim().slice(0, 1) || '雇'
}

function formatTrustRate(value: number) {
  return `${Math.round(value * 100)}%`
}

function formatDate(value?: string | null) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

function safeText(value?: string | null) {
  return value?.trim() || '--'
}

function statusClass(status?: string | null, lifecycleStatus?: string | null) {
  const normalized = (status ?? lifecycleStatus ?? '').toLowerCase()
  if (normalized.includes('live') || normalized.includes('通过') || normalized.includes('已上岗')) return 'green'
  if (normalized.includes('intern') || normalized.includes('待') || normalized.includes('pending') || normalized.includes('ai')) return 'blue'
  if (normalized.includes('fail') || normalized.includes('error') || normalized.includes('异常')) return 'orange'
  return 'gray'
}

function statusLabel(status?: string | null, lifecycleStatus?: string | null) {
  const value = (status ?? lifecycleStatus ?? '').trim().toLowerCase()
  if (value === 'live') return '已上岗'
  if (value === 'hired') return '已雇佣'
  if (value === 'interning_ai') return 'AI 评估'
  if (value === 'interning_human') return '人工评估'
  if (value === 'failed') return '评估失败'
  if (value === 'retired') return '已退役'
  if (value.includes('待上岗')) return '待上岗'
  if (value.includes('待人工')) return '人工评估'
  if (value.includes('待ai')) return 'AI 评估'
  return lifecycleStatus || status || '--'
}

function ownershipLabel(value?: string | null) {
  const normalized = (value ?? '').trim().toLowerCase()
  if (normalized === 'personal_clone') return '我的分身'
  if (normalized === 'private_branch') return '私有分支'
  return '部门员工'
}

function ownershipClass(value?: string | null) {
  const normalized = (value ?? '').trim().toLowerCase()
  if (normalized === 'personal_clone') return 'purple'
  if (normalized === 'private_branch') return 'pink'
  return 'blue'
}

function localChatKey(employeeId: string, role: PrototypeRole) {
  return `${employeeId}::${role}`
}

function seedLocalChat(employeeName: string, role: PrototypeRole): LocalChatMessage[] {
  return [
    {
      id: mkId('msg'),
      role: 'bot',
      content: `你好，我是 ${employeeName}。这里是 ${role === 'manager' ? '部门长' : '普通成员'} 视角的原型聊天预览。`,
      createdAt: new Date().toISOString(),
    },
  ]
}

function botReply(prompt: string, employeeName: string) {
  if (prompt.includes('JD') || prompt.includes('岗位')) {
    return `可以，我会按 ${employeeName} 的当前能力边界先给你一份结构化草稿。`
  }
  if (prompt.includes('简历') || prompt.includes('候选人')) {
    return '收到，我会先按岗位契合度和风险分层，再把需要人工复核的候选人单独列出来。'
  }
  if (prompt.includes('周报') || prompt.includes('汇总')) {
    return '可以，我会整理成核心结论、关键变化、后续建议三段式输出。'
  }
  return `我已经收到这条任务，会先按 ${employeeName} 的规则组织答案。`
}

function buildConnectSteps(channelId: ImChannelId, methodId: ImMethodId): ImStep[] {
  return IM_SCHEMAS[channelId].methods[methodId].steps.map((name) => ({ name, done: false }))
}

function readImStore(): ImBindingStore {
  try {
    const raw = localStorage.getItem(IM_BINDING_KEY)
    return raw ? JSON.parse(raw) as ImBindingStore : {}
  } catch {
    return {}
  }
}

function writeImStore(nextStore: ImBindingStore) {
  try {
    localStorage.setItem(IM_BINDING_KEY, JSON.stringify(nextStore))
  } catch {
    // ignore storage failures in prototype mode
  }
}

function getEmployeeBindings(employeeId: string): Partial<Record<ImChannelId, ImBinding>> {
  return readImStore()[employeeId] ?? {}
}

function getBinding(employeeId: string, channelId: ImChannelId): ImBinding | null {
  return getEmployeeBindings(employeeId)[channelId] ?? null
}

function saveEmployeeBinding(employeeId: string, channelId: ImChannelId, binding: ImBinding) {
  const store = readImStore()
  const next = { ...(store[employeeId] ?? {}) }
  next[channelId] = binding
  store[employeeId] = next
  writeImStore(store)
  return next[channelId]
}

function removeEmployeeBinding(employeeId: string, channelId: ImChannelId) {
  const store = readImStore()
  const next = { ...(store[employeeId] ?? {}) }
  delete next[channelId]
  store[employeeId] = next
  writeImStore(store)
  return next
}

function getBindingStatus(employeeId: string, channelId: ImChannelId): ImBindingStatus {
  const binding = getBinding(employeeId, channelId)
  return binding ? (binding.status ?? 'connected') : 'unconfigured'
}

function getConnectedChannels(employeeId: string) {
  return (Object.keys(getEmployeeBindings(employeeId)) as ImChannelId[]).filter((channelId) => {
    const binding = getBinding(employeeId, channelId)
    return !!binding && binding.status !== 'error'
  })
}

function getImEmployeeTypeLabel(employee: EmployeeSummary | EmployeeDetail | null | undefined) {
  if (!employee) return ''
  const instanceType = employee.instanceType
  if (instanceType === 'department') return '部门员工不配置 IM'
  return '个人分身 / 私有分支可单独配置 IM'
}

function ImStatusStrip({
  employeeId,
  compact = false,
  onClick,
}: {
  employeeId: string
  compact?: boolean
  onClick?: (channelId: ImChannelId) => void
}) {
  return (
    <div className={`im-status-strip ${compact ? 'compact' : ''}`}>
      {(Object.keys(IM_CHANNEL_META) as ImChannelId[]).map((channelId) => {
        const channel = IM_CHANNEL_META[channelId]
        const status = getBindingStatus(employeeId, channelId)
        const cls = status === 'connected' ? 'connected' : status === 'error' ? 'error' : 'idle'
        const label = status === 'connected' ? '已连接' : status === 'error' ? '异常' : '未配置'

        return (
          <button
            key={channelId}
            type="button"
            className={`im-status-dot ${cls}`}
            onClick={(event) => {
              event.stopPropagation()
              onClick?.(channelId)
            }}
          >
            <span className="im-status-dot-mark">{channel.short}</span>
            {!compact ? <span className="im-status-dot-text">{channel.name} · {label}</span> : null}
          </button>
        )
      })}
    </div>
  )
}

function ImPickerModal({
  open,
  onClose,
  employee,
  onConfig,
}: {
  open: boolean
  onClose: () => void
  employee: EmployeeSummary | EmployeeDetail | null
  onConfig: () => void
}) {
  const [opened, setOpened] = useState<ImChannelId | ''>('')

  useEffect(() => {
    if (!open) setOpened('')
  }, [open, employee?.employeeId])

  if (!open || !employee) return null

  const connected = getConnectedChannels(employee.employeeId)

  return (
    <div className="modal-mask" onClick={onClose}>
      <div className="modal" style={{ position: 'relative' }} onClick={(event) => event.stopPropagation()}>
        <button className="modal-close" onClick={onClose}><X className="icn" /></button>
        <div className="modal-head">
          <h3 className="modal-title">{connected.length === 0 ? '该员工尚未接入 IM' : `去 IM · ${employee.nickname}`}</h3>
          <p className="modal-sub">
            {connected.length === 0
              ? '平台会话已经可用，你也可以现在去完成 IM 配置。'
              : '演示模式下会模拟拉起对应平台私聊。'}
          </p>
        </div>
        <div className="modal-body">
          {connected.length === 0 ? (
            <div className="callout info">
              IM 接入是个人分身层面的可选动作，不阻塞上岗。完成配置后，你就能从卡片、详情页和站内对话页顶部直接“去 IM”。
            </div>
          ) : (
            <>
              <div className="jump-grid">
                {connected.map((channelId) => {
                  const channel = IM_CHANNEL_META[channelId]
                  return (
                    <button
                      key={channelId}
                      type="button"
                      className={`jump-chip ${opened === channelId ? 'active' : ''}`}
                      onClick={() => setOpened(channelId)}
                    >
                      <span className={`jump-chip-mark ${channel.accent}`}>{channel.short}</span>
                      <span>{channel.name}</span>
                    </button>
                  )
                })}
              </div>
              {opened ? (
                <>
                  <div className="spacer-16" />
                  <div className="callout success">
                    演示模式已模拟拉起 {IM_CHANNEL_META[opened].name} 私聊。真实产品里这里会跳转到对应 IM 机器人会话。
                  </div>
                </>
              ) : null}
            </>
          )}
        </div>
        <div className="modal-foot">
          {connected.length === 0 ? (
            <>
              <button className="btn btn-ghost btn-sm" onClick={onClose}>取消</button>
              <button className="btn btn-primary btn-sm" onClick={() => { onClose(); onConfig(); }}>去配置 IM</button>
            </>
          ) : (
            <button className="btn btn-ghost btn-sm" onClick={onClose}>关闭</button>
          )}
        </div>
      </div>
    </div>
  )
}

function usePrototypeData() {
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [templates, setTemplates] = useState<EmployeeTemplateCard[]>([])
  const [employees, setEmployees] = useState<EmployeeSummary[]>([])
  const [imItems, setImItems] = useState<TeamImItem[]>([])

  const load = async () => {
    setLoading(true)
    setError('')
    try {
      const [templateList, employeeList, imList] = await Promise.all([
        api.employeeTemplate.getList({ page: 1, pageSize: 50 }),
        api.employeeRuntime.getEmployees(),
        api.teamIm.getItems({ status: 'all', page: 1, pageSize: 50 }),
      ])
      setTemplates(templateList.items ?? [])
      setEmployees(employeeList ?? [])
      setImItems(imList ?? [])
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '原型数据加载失败')
      setTemplates([])
      setEmployees([])
      setImItems([])
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  return { loading, error, templates, employees, imItems, reload: load }
}

type FixtureImportResult = {
  ownerSubject: string
  fixtureDirectories: number
  importedEmployees: number
  importedImItems: number
  employeeIds: string[]
}

function formatEmployeeSubtitle(employee: EmployeeSummary) {
  return employee.roleName || employee.sourceTemplate || employee.owningTeam || '未命名实例'
}

function formatEmployeeSignal(employee: EmployeeSummary) {
  return employee.primarySignal || employee.stageSummary || '暂无主信号'
}

function renderTrustProof(card: EmployeeTemplateCard) {
  return [
    `${card.trustProof.hiredCount} 个采用`,
    `${formatTrustRate(card.trustProof.successRate)} 成功率`,
    `评分 ${card.trustProof.avgRating.toFixed(1)}`,
  ].join(' · ')
}

export default function PrototypeStandalonePage() {
  const [role, setRole] = useState<PrototypeRole>(() => readStorage<PrototypeRole>(ROLE_KEY, 'manager'))
  const [view, setView] = useState<PrototypeView>(() => readStorage<PrototypeView>(VIEW_KEY, 'templates'))
  const [selectedTemplateId, setSelectedTemplateId] = useState<string>(() => readStorage<string>(TEMPLATE_KEY, ''))
  const [selectedEmployeeId, setSelectedEmployeeId] = useState<string>(() => readStorage<string>(EMPLOYEE_KEY, ''))
  const [hireId, setHireId] = useState<string>(() => readStorage<string>(HIRE_KEY, ''))
  const [toasts, setToasts] = useState<Toast[]>([])
  const [templateQuery, setTemplateQuery] = useState('')
  const [deptQuery, setDeptQuery] = useState('')
  const [deptTab, setDeptTab] = useState<'hired' | 'intern' | 'live'>('live')
  const [internSubTab, setInternSubTab] = useState<'ai' | 'human'>('ai')
  const [myFilter, setMyFilter] = useState<'all' | 'live' | 'evaluating' | 'branch' | 'failed'>('all')
  const [chatInput, setChatInput] = useState('')
  const [cloneName, setCloneName] = useState('')
  const [cloneDesc, setCloneDesc] = useState('')
  const [reviewComment, setReviewComment] = useState('建议先补齐边界条件，再重新进入 AI 评估。')
  const [publishComment, setPublishComment] = useState('已完成身份配置，进入上岗。')
  const [hireInput, setHireInput] = useState('')
  const [hireTimeline, setHireTimeline] = useState<HiringConversationTimeline | null>(null)
  const [hireWorkflow, setHireWorkflow] = useState<HiringWorkflowState | null>(null)
  const [hirePreview, setHirePreview] = useState<HiringStagePreview | null>(null)
  const [hireStatusText, setHireStatusText] = useState('')
  const [hireMessages, setHireMessages] = useState<HiringConversationMessage[]>([])
  const [evaluationState, setEvaluationState] = useState<EvaluationState | null>(null)
  const [evaluationConversation, setEvaluationConversation] = useState<EvaluationSandboxConversationState | null>(null)
  const [evaluationInput, setEvaluationInput] = useState('')
  const [chatThreads, setChatThreads] = useState<Record<string, LocalChatMessage[]>>(() => readStorage(CHAT_KEY, {}))
  const [employeeDetail, setEmployeeDetail] = useState<EmployeeDetail | null>(null)
  const [templateDetail, setTemplateDetail] = useState<EmployeeTemplateDetail | null>(null)
  const [loadingTemplateDetail, setLoadingTemplateDetail] = useState(false)
  const [loadingEmployeeDetail, setLoadingEmployeeDetail] = useState(false)
  const [busy, setBusy] = useState(false)
  const [imFilter, setImFilter] = useState<'all' | 'pending' | 'confirmed'>('all')
  const [imJumpEmployee, setImJumpEmployee] = useState<EmployeeSummary | null>(null)
  const [imChannelId, setImChannelId] = useState<ImChannelId>('lark')
  const [imMethodId, setImMethodId] = useState<ImMethodId>('websocket')
  const [imForm, setImForm] = useState<Record<string, string>>({})
  const [imPhase, setImPhase] = useState<0 | 1 | 2>(0)
  const [imSteps, setImSteps] = useState<ImStep[]>(() => buildConnectSteps('lark', 'websocket'))
  const [fixtureImporting, setFixtureImporting] = useState(false)
  const [fixtureSeeded, setFixtureSeeded] = useState(() => readStorage<boolean>(FIXTURE_SEED_KEY, false))
  const [fixtureAutoAttempted, setFixtureAutoAttempted] = useState(() => readStorage<boolean>(FIXTURE_AUTO_ATTEMPT_KEY, false))

  const { loading, error, templates, employees, imItems, reload } = usePrototypeData()

  useEffect(() => {
    writeStorage(ROLE_KEY, role)
  }, [role])

  useEffect(() => {
    writeStorage(VIEW_KEY, view)
  }, [view])

  useEffect(() => {
    writeStorage(TEMPLATE_KEY, selectedTemplateId)
  }, [selectedTemplateId])

  useEffect(() => {
    writeStorage(EMPLOYEE_KEY, selectedEmployeeId)
  }, [selectedEmployeeId])

  useEffect(() => {
    writeStorage(HIRE_KEY, hireId)
  }, [hireId])

  useEffect(() => {
    writeStorage(CHAT_KEY, chatThreads)
  }, [chatThreads])

  useEffect(() => {
    writeStorage(FIXTURE_SEED_KEY, fixtureSeeded)
  }, [fixtureSeeded])

  useEffect(() => {
    writeStorage(FIXTURE_AUTO_ATTEMPT_KEY, fixtureAutoAttempted)
  }, [fixtureAutoAttempted])

  useEffect(() => {
    if (role === 'member' && (view === 'templates' || view === 'template' || view === 'hire' || view === 'quick-clone')) {
      setView('dept')
    }
  }, [role, view])

  useEffect(() => {
    if (loading || error || fixtureAutoAttempted || employees.length > 0) {
      return
    }

    setFixtureAutoAttempted(true)
    writeStorage(FIXTURE_AUTO_ATTEMPT_KEY, true)
    void importFixtureData()
  }, [loading, error, fixtureAutoAttempted, employees.length])

  useEffect(() => {
    if (!selectedTemplateId) {
      setTemplateDetail(null)
      return
    }

    let cancelled = false
    setLoadingTemplateDetail(true)
    api.employeeTemplate.getDetail(selectedTemplateId)
      .then((detail) => {
        if (!cancelled) {
          setTemplateDetail(detail)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setTemplateDetail(null)
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoadingTemplateDetail(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [selectedTemplateId])

  useEffect(() => {
    if (!selectedEmployeeId) {
      setEmployeeDetail(null)
      return
    }

    let cancelled = false
    setLoadingEmployeeDetail(true)
    api.employeeRuntime.getEmployee(selectedEmployeeId)
      .then((detail) => {
        if (!cancelled) {
          setEmployeeDetail(detail)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setEmployeeDetail(null)
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoadingEmployeeDetail(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [selectedEmployeeId])

  useEffect(() => {
    if (view !== 'im' || !selectedEmployeeId || !employeeDetail) {
      return
    }

    if (employeeDetail.instanceType === 'department') {
      setImPhase(0)
      return
    }

    const connectedChannels = getConnectedChannels(employeeDetail.employeeId)
    const nextChannelId = connectedChannels[0] ?? 'lark'
    const binding = getBinding(employeeDetail.employeeId, nextChannelId)
    const nextMethodId = binding?.methodId ?? 'websocket'

    setImChannelId(nextChannelId)
    setImMethodId(nextMethodId)
    setImForm(binding ? { ...binding.form } : {})
    setImPhase(binding ? 2 : 0)
    setImSteps(buildConnectSteps(nextChannelId, nextMethodId))
  }, [employeeDetail, selectedEmployeeId, view])

  useEffect(() => {
    if (!hireId) {
      setHireTimeline(null)
      setHireWorkflow(null)
      setHirePreview(null)
      setHireMessages([])
      setHireStatusText('')
      return
    }

    let cancelled = false

    async function refreshHireState() {
      try {
        const [workflow, timeline, preview, status] = await Promise.all([
          api.hiringWorkflow.getWorkflowState(hireId),
          api.hiringWorkflow.getConversationTimeline(hireId),
          api.hiringWorkflow.getStagePreview(hireId).catch(() => null),
          api.employeeTemplate.getHiringStatus(hireId).catch(() => null),
        ])
        if (!cancelled) {
          setHireWorkflow(workflow)
          setHireTimeline(timeline)
          setHirePreview(preview)
          setHireMessages(timeline.messages ?? [])
          setHireStatusText(status ? `${status.status}${status.errorMessage ? ` · ${status.errorMessage}` : ''}` : workflow.collectionPhase)
        }
      } catch (requestError: unknown) {
        if (!cancelled) {
          setHireStatusText(requestError instanceof Error ? requestError.message : '招聘流程读取失败')
        }
      }
    }

    void refreshHireState()
    const timer = window.setInterval(() => {
      void refreshHireState()
    }, 6000)

    return () => {
      cancelled = true
      window.clearInterval(timer)
    }
  }, [hireId])

  useEffect(() => {
    if (!selectedEmployeeId) {
      setEvaluationState(null)
      setEvaluationConversation(null)
      return
    }

    let cancelled = false
    if (view === 'ai-eval' || view === 'human-eval' || view === 'review' || view === 'publish') {
      api.employeeRuntime.getEvaluationState(selectedEmployeeId)
        .then((state) => {
          if (!cancelled) {
            setEvaluationState(state)
          }
        })
        .catch(() => {
          if (!cancelled) {
            setEvaluationState(null)
          }
        })
    }

    return () => {
      cancelled = true
    }
  }, [selectedEmployeeId, view])

  useEffect(() => {
    if (!selectedEmployeeId || (view !== 'ai-eval' && view !== 'human-eval')) {
      return
    }

    let cancelled = false
    let timer: number
    let currentInterval = 7000
    let lastCount = 0
    let stableCount = 0
    let lastMessageId: string | null = null
    let noChangeCount = 0
    let shouldStop = false
    const maxNoChangeBeforeStop = 3

    function isConversationDone(messages: HiringConversationMessage[]): boolean {
      if (messages.length === 0) return false
      const last = messages[messages.length - 1]
      return last.role === 'assistant'
        && last.content.length > 0
        && !last.content.includes('[tool_use]')
    }

    function scheduleNext() {
      if (cancelled) return
      timer = window.setTimeout(() => {
        void poll()
      }, currentInterval)
    }

    async function poll() {
      if (cancelled) return
      try {
        const conversation = await api.employeeRuntime.getEvaluationSandboxConversation(selectedEmployeeId!, lastMessageId)
        if (cancelled) return
        // null means 304 Not Modified — no new messages
        if (conversation === null) {
          noChangeCount++
          if (noChangeCount >= maxNoChangeBeforeStop) {
            shouldStop = true
            return
          }
          return
        }
        noChangeCount = 0
        setEvaluationConversation(conversation)
        const newMessages = conversation.messages ?? []
        const newCount = newMessages.length
        if (newCount > 0) {
          lastMessageId = newMessages[newCount - 1].messageId
        }
        if (newCount === lastCount && newCount > 0) {
          stableCount++
        } else {
          stableCount = 0
        }
        lastCount = newCount
        if (stableCount >= 10) {
          currentInterval = 20000
        } else if (stableCount >= 4) {
          currentInterval = 12000
        }
        if (isConversationDone(newMessages)) {
          shouldStop = true
          return
        }
      } catch {
        // keep silent during polling
      }
      if (!cancelled && !shouldStop) {
        scheduleNext()
      }
    }

    void poll()

    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [selectedEmployeeId, view])

  const templateList = useMemo(() => {
    const keyword = templateQuery.trim().toLowerCase()
    if (!keyword) return templates
    return templates.filter((item) => {
      return [
        item.name,
        item.tagline,
        item.coreAbilityTags.join(' '),
      ].join(' ').toLowerCase().includes(keyword)
    })
  }, [templateQuery, templates])

  const departmentEmployees = useMemo(() => {
    const base = employees.filter((item) => item.instanceType === 'department')
    const byTab = (() => {
      if (deptTab === 'live') return base.filter((item) => item.status === 'live')
      if (deptTab === 'hired') return base.filter((item) => item.status === 'hired' || item.status === 'failed')
      return base.filter((item) => item.status === 'interning_ai' || item.status === 'interning_human')
    })()

    const keyword = deptQuery.trim().toLowerCase()
    if (!keyword) return byTab
    return byTab.filter((item) => {
      return [
        item.nickname,
        item.roleName,
        item.sourceTemplate,
        item.primarySignal,
        item.stageSummary,
      ].join(' ').toLowerCase().includes(keyword)
    })
  }, [deptQuery, deptTab, employees])

  const myEmployees = useMemo(() => {
    return employees.filter((item) => item.instanceType === 'personal_clone' || item.instanceType === 'private_branch')
  }, [employees])

  const visibleMyEmployees = useMemo(() => {
    if (myFilter === 'all') return myEmployees
    if (myFilter === 'live') return myEmployees.filter((item) => item.status === 'live')
    if (myFilter === 'evaluating') return myEmployees.filter((item) => item.status === 'interning_ai' || item.status === 'interning_human')
    if (myFilter === 'branch') return myEmployees.filter((item) => item.instanceType === 'private_branch')
    return myEmployees.filter((item) => item.status === 'failed')
  }, [myEmployees, myFilter])

  const templateTotal = templates.length
  const deptLiveCount = employees.filter((item) => item.instanceType === 'department' && item.status === 'live').length
  const deptEvaluatingCount = employees.filter((item) => item.instanceType === 'department' && (item.status === 'interning_ai' || item.status === 'interning_human')).length
  const myLiveCount = myEmployees.filter((item) => item.status === 'live').length

  function pushToast(text: string, kind: ToastKind = 'success') {
    const id = mkId('toast')
    setToasts((previous) => [...previous, { id, text, kind }])
    window.setTimeout(() => {
      setToasts((previous) => previous.filter((item) => item.id !== id))
    }, 2500)
  }

  async function importFixtureData() {
    if (fixtureImporting) {
      return
    }

    setFixtureImporting(true)
    try {
      const result: FixtureImportResult = await api.employeeRuntime.importFixtureInstances()
      setFixtureSeeded(true)
      pushToast(`示例数据已导入：${result.importedEmployees} 个员工，${result.importedImItems} 条 IM 信息`, 'success')
      await reload()
    } catch (requestError: unknown) {
      setFixtureSeeded(false)
      pushToast(requestError instanceof Error ? requestError.message : '导入示例实例失败', 'error')
    } finally {
      setFixtureImporting(false)
    }
  }

  function openTemplate(templateId: string) {
    setSelectedTemplateId(templateId)
    setView('template')
  }

  function openEmployee(employeeId: string) {
    setSelectedEmployeeId(employeeId)
    setView('employee')
  }

  function openImConfig(employeeId: string) {
    setSelectedEmployeeId(employeeId)
    setView('im')
  }

  function openImPicker(employee: EmployeeSummary | EmployeeDetail) {
    setImJumpEmployee(employee)
  }

  async function startHire(templateId: string, useFixture = false) {
    setBusy(true)
    try {
      if (useFixture) {
        const result = await api.employeeTemplate.fixtureHire(templateId)
        pushToast(`Fixture 雇佣已完成：${result.employeeId}`, 'success')
        setSelectedEmployeeId(result.employeeId)
        openEmployee(result.employeeId)
        return
      }

      const result = await api.employeeTemplate.hire(templateId, {
        tenantId: 'tenant-default',
        operatorId: 'prototype',
        useCase: 'prototype-preview',
      })
      setSelectedTemplateId(templateId)
      setHireId(result.hireId)
      setView('hire')
      pushToast(`雇佣已创建：${result.hireId}`, 'success')
      await api.hiringWorkflow.startConversation(result.hireId).catch(() => null)
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '雇佣失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function sendHireMessage() {
    if (!hireId || !hireInput.trim()) return
    const content = hireInput.trim()
    setHireInput('')
    setBusy(true)
    try {
      const result = await api.hiringWorkflow.sendConversationMessage(hireId, {
        content,
        structuredAnswers: {},
        materials: [],
      })
      setHireMessages((previous) => [...previous, result.assistantMessage])
      setHireTimeline((previous) => previous ? { ...previous, messages: [...previous.messages, result.assistantMessage] } : previous)
      pushToast('雇佣对话已发送', 'success')
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '发送失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function uploadHireFile(file: File) {
    if (!hireId) return
    setBusy(true)
    try {
      await api.hiringWorkflow.uploadMaterialFile(hireId, file, { source: 'prototype' })
      pushToast(`已上传资料：${file.name}`, 'success')
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '上传失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function finalizeHire() {
    if (!hireId) return
    setBusy(true)
    try {
      const result = await api.hiringWorkflow.finalize(hireId)
      pushToast(`已生成交付物：${result.downloadUrl}`, 'success')
      if (result.employeeId) {
        setSelectedEmployeeId(result.employeeId)
        openEmployee(result.employeeId)
      }
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '生成实例失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function refreshHire() {
    if (!hireId) return
    setBusy(true)
    try {
      const [workflow, timeline, preview] = await Promise.all([
        api.hiringWorkflow.getWorkflowState(hireId),
        api.hiringWorkflow.getConversationTimeline(hireId),
        api.hiringWorkflow.getStagePreview(hireId).catch(() => null),
      ])
      setHireWorkflow(workflow)
      setHireTimeline(timeline)
      setHirePreview(preview)
      setHireMessages(timeline.messages ?? [])
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '刷新失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function sendEvaluationMessage() {
    if (!selectedEmployeeId || !evaluationInput.trim()) return
    const content = evaluationInput.trim()
    setEvaluationInput('')
    setBusy(true)
    try {
      const conversation = await api.employeeRuntime.sendEvaluationSandboxMessage(selectedEmployeeId, {
        content,
        structuredAnswers: {},
      })
      setEvaluationConversation(conversation)
      pushToast('评估沙箱消息已发送', 'success')
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '评估消息发送失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function submitAiDecision(decision: 'START' | 'LOAD_SKILL' | 'RUN' | 'PASS' | 'FAIL') {
    if (!selectedEmployeeId) return
    setBusy(true)
    try {
      const updated = await api.employeeRuntime.submitAiEvaluationDecision(selectedEmployeeId, { decision })
      setSelectedEmployeeId(updated.employeeId)
      pushToast(`AI 决策已提交：${decision}`, 'success')
      await reload()
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '提交 AI 决策失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function submitHumanDecision(decision: 'ONBOARD' | 'REJECT' | 'FORCE') {
    if (!selectedEmployeeId) return
    setBusy(true)
    try {
      const updated = await api.employeeRuntime.submitOnboardingDecision(selectedEmployeeId, { decision, comment: publishComment })
      setSelectedEmployeeId(updated.employeeId)
      pushToast(`人工结论已提交：${decision}`, 'success')
      await reload()
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '提交人工结论失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function rollbackToHired() {
    if (!selectedEmployeeId) return
    setBusy(true)
    try {
      const updated = await api.employeeRuntime.updateLifecycle(selectedEmployeeId, {
        status: 'hired',
        stageSummary: 'Review 回退到已雇佣，等待重新发起 AI 评估',
        primarySignal: '待操作：重新进入 AI 评估',
        signalLevel: 'warn',
      })
      setSelectedEmployeeId(updated.employeeId)
      pushToast('已回退到已雇佣', 'success')
      await reload()
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '回退失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function retryAi() {
    if (!selectedEmployeeId) return
    await submitAiDecision('START')
    setView('ai-eval')
  }

  async function createClone(kind: 'clone' | 'branch' | 'quick-clone') {
    if (!selectedEmployeeId) return
    const source = employeeDetail ?? employees.find((item) => item.employeeId === selectedEmployeeId)
    if (!source) return
    const displayName = cloneName.trim() || `${source.nickname}${kind === 'branch' ? ' · 私有分支' : ' · 我的分身'}`
    const displayDescription = cloneDesc.trim() || (kind === 'branch'
      ? '基于当前实例创建的私有分支，保留独立对话与配置。'
      : `基于 ${source.nickname} 创建的个人分身。`)

    setBusy(true)
    try {
      const clone = await api.employeeRuntime.createPersonalClone(selectedEmployeeId, {
        displayName,
        displayDescription,
      })
      setSelectedEmployeeId(clone.employeeId)
      setView('employee')
      pushToast(kind === 'branch' ? '已创建私有分支（当前后端以 personal_clone 落库）' : '已创建个人分身', 'success')
      await reload()
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '创建分身失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  async function confirmImItem(itemId: string) {
    setBusy(true)
    try {
      await api.teamIm.confirmItem(itemId)
      pushToast('IM 待办已确认', 'success')
      await reload()
    } catch (requestError: unknown) {
      pushToast(requestError instanceof Error ? requestError.message : '确认失败', 'error')
    } finally {
      setBusy(false)
    }
  }

  function renderTemplateList() {
    return (
      <section className="space-y-5">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <div className="text-xs uppercase tracking-[0.2em] text-[#737373]">部门长入口</div>
            <h1 className="mt-2 text-[clamp(2rem,3vw,3rem)] font-semibold leading-tight text-[#0a0a0a]">
              企业模板池
            </h1>
            <p className="mt-3 max-w-2xl text-sm leading-6 text-[#404040]">
              这里是原型里的模板池，但底层数据已切到真实 API。点击模板即可进入详情，后续雇佣、评估、发布都沿用同一批后端接口。
            </p>
          </div>
          <button type="button" className="hb-btn-primary" onClick={() => reload()}>
            <RefreshCw size={14} />
            刷新模板
          </button>
        </div>

        <div className="hb-search-shell">
          <Search size={16} />
          <input
            value={templateQuery}
            onChange={(event) => setTemplateQuery(event.target.value)}
            className="hb-search-input"
            placeholder="搜索模板名称、能力标签或说明"
          />
          <div className="hb-search-controls">
            <button type="button" className="hb-btn-primary" onClick={() => setTemplateQuery('')}>
              清空筛选
            </button>
          </div>
        </div>

        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {templateList.map((template) => (
            <button
              key={template.templateId}
              type="button"
              className="hb-card p-5 text-left transition-transform duration-150 hover:-translate-y-0.5"
              onClick={() => openTemplate(template.templateId)}
            >
              <div className="mb-3 flex items-start gap-3">
                <span className="hb-squircle h-11 w-11 bg-[#dde9ff] text-[#3d5cff]">
                  {firstChar(template.name)}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center justify-between gap-2">
                    <h3 className="truncate text-[15px] font-semibold text-[#0a0a0a]">{template.name}</h3>
                    <span className={`hb-pill ${template.isAvailable ? 'green' : 'gray'}`}>
                      {template.isAvailable ? '可雇佣' : '未开放'}
                    </span>
                  </div>
                  <p className="mt-1 line-clamp-2 text-xs text-[#737373]">{template.tagline}</p>
                </div>
              </div>

              <div className="flex flex-wrap gap-2">
                {template.coreAbilityTags.slice(0, 3).map((tag) => (
                  <span key={tag} className="hb-pill blue">
                    {tag}
                  </span>
                ))}
              </div>

              <div className="mt-4 rounded-2xl border border-[#f3f4f6] bg-[#fafafa] p-3 text-xs text-[#404040]">
                {renderTrustProof(template)}
              </div>

              <div className="mt-4 flex items-center justify-between border-t border-[#f5f5f5] pt-3 text-xs text-[#737373]">
                <span>点击进入详情</span>
                <span className="text-[#4a6cf7]">查看 →</span>
              </div>
            </button>
          ))}
        </div>
      </section>
    )
  }

  function renderDeptList() {
    const counts = {
      hired: employees.filter((item) => item.instanceType === 'department' && (item.status === 'hired' || item.status === 'failed')).length,
      intern: employees.filter((item) => item.instanceType === 'department' && (item.status === 'interning_ai' || item.status === 'interning_human')).length,
      live: employees.filter((item) => item.instanceType === 'department' && item.status === 'live').length,
    }

    return (
      <section className="space-y-5">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <div className="text-xs uppercase tracking-[0.2em] text-[#737373]">团队资产总览</div>
            <h1 className="mt-2 text-[clamp(2rem,3vw,3rem)] font-semibold leading-tight text-[#0a0a0a]">
              部门数字员工
            </h1>
            <p className="mt-3 max-w-2xl text-sm leading-6 text-[#404040]">
              这里展示真实后端返回的部门员工。管理者能看到已雇佣、评估中、已上岗三个阶段，普通成员只看已上岗。
            </p>
          </div>
          {role === 'manager' ? (
            <button type="button" className="hb-btn-primary" onClick={() => setView('templates')}>
              <Plus size={14} />
              从模板雇佣
            </button>
          ) : null}
        </div>

        <div className="hb-search-shell">
          <Search size={16} />
          <input
            value={deptQuery}
            onChange={(event) => setDeptQuery(event.target.value)}
            className="hb-search-input"
            placeholder="搜索员工名称、能力、主信号"
          />
          <div className="hb-search-controls">
            <button type="button" className="hb-btn-primary" onClick={() => setDeptQuery('')}>
              清空筛选
            </button>
          </div>
        </div>

        <div className="hb-chip-row">
          {[
            { id: 'hired' as const, label: '已雇佣', count: counts.hired },
            { id: 'intern' as const, label: '待实习', count: counts.intern },
            { id: 'live' as const, label: '已上岗', count: counts.live },
          ].map((item) => (
            <button
              key={item.id}
              type="button"
              className={`hb-chip ${deptTab === item.id ? 'is-active' : ''}`}
              onClick={() => setDeptTab(item.id)}
            >
              {item.label}
              <span>{item.count}</span>
            </button>
          ))}
        </div>

        {role === 'manager' && deptTab === 'intern' ? (
          <div className="hb-chip-row">
            <button
              type="button"
              className={`hb-chip ${internSubTab === 'ai' ? 'is-active' : ''}`}
              onClick={() => setInternSubTab('ai')}
            >
              AI 评估
            </button>
            <button
              type="button"
              className={`hb-chip ${internSubTab === 'human' ? 'is-active' : ''}`}
              onClick={() => setInternSubTab('human')}
            >
              人工评估
            </button>
          </div>
        ) : null}

        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {departmentEmployees.map((employee) => (
            <button
              key={employee.employeeId}
              type="button"
              className="hb-card p-5 text-left transition-transform duration-150 hover:-translate-y-0.5"
              onClick={() => openEmployee(employee.employeeId)}
            >
              <div className="mb-3 flex items-start gap-3">
                <span className="hb-squircle h-11 w-11 bg-[#dde9ff] text-[#3d5cff]">
                  {firstChar(employee.nickname)}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center justify-between gap-2">
                    <h3 className="truncate text-[15px] font-semibold text-[#0a0a0a]">{employee.nickname}</h3>
                    <span className={`hb-pill ${statusClass(employee.status, employee.lifecycleStatus)}`}>
                      {statusLabel(employee.status, employee.lifecycleStatus)}
                    </span>
                  </div>
                  <p className="mt-1 truncate text-xs text-[#737373]">{formatEmployeeSubtitle(employee)}</p>
                </div>
              </div>

              <p className="line-clamp-2 min-h-10 text-sm leading-relaxed text-[#404040]">
                {formatEmployeeSignal(employee)}
              </p>

              <div className="mt-4 flex items-center justify-between border-t border-[#f5f5f5] pt-3 text-xs text-[#737373]">
                <span>最近更新 {employee.createdAt}</span>
                <div className="flex items-center gap-2">
                  {employee.instanceType === 'department' && employee.status === 'live' ? (
                    <button
                      type="button"
                      className="hb-btn-link text-xs"
                      onClick={(event) => {
                        event.stopPropagation()
                        setSelectedEmployeeId(employee.employeeId)
                        setCloneName(`${employee.nickname} · 我的分身`)
                        setCloneDesc(`基于 ${employee.nickname} 创建的个人分身。`)
                        setView('clone')
                      }}
                    >
                      复制分身
                    </button>
                  ) : null}
                  <span className="text-[#4a6cf7]">查看详情 →</span>
                </div>
              </div>
            </button>
          ))}
        </div>
      </section>
    )
  }

  function renderMyList() {
    const counts = {
      all: myEmployees.length,
      live: myEmployees.filter((item) => item.status === 'live').length,
      evaluating: myEmployees.filter((item) => item.status === 'interning_ai' || item.status === 'interning_human').length,
      branch: myEmployees.filter((item) => item.instanceType === 'private_branch').length,
      failed: myEmployees.filter((item) => item.status === 'failed').length,
    }

    return (
      <section className="space-y-5">
        <div>
          <div className="text-xs uppercase tracking-[0.2em] text-[#737373]">个人资产面板</div>
          <h1 className="mt-2 text-[clamp(2rem,3vw,3rem)] font-semibold leading-tight text-[#0a0a0a]">
            我的数字员工
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-[#404040]">
            这里展示真实后端里你的个人分身和私有分支。已上岗的资产可以继续对话、配置 IM 或创建更多分支。
          </p>
        </div>

        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          {[
            { label: '实例总数', value: counts.all },
            { label: '已上岗', value: counts.live },
            { label: '评估中', value: counts.evaluating },
            { label: '私有分支', value: counts.branch, note: `失败实例 ${counts.failed}` },
          ].map((item) => (
            <div key={item.label} className="hb-card p-4">
              <div className="text-xs text-[#737373]">{item.label}</div>
              <div className="mt-2 text-2xl font-semibold text-[#0a0a0a]">{item.value}</div>
              {item.note ? <div className="mt-1 text-xs text-[#737373]">{item.note}</div> : null}
            </div>
          ))}
        </div>

        <div className="hb-chip-row">
          {[
            { id: 'all' as const, label: '全部', count: counts.all },
            { id: 'live' as const, label: '已上岗', count: counts.live },
            { id: 'evaluating' as const, label: '评估中', count: counts.evaluating },
            { id: 'branch' as const, label: '私有分支', count: counts.branch },
            { id: 'failed' as const, label: '待回退', count: counts.failed },
          ].map((item) => (
            <button
              key={item.id}
              type="button"
              className={`hb-chip ${myFilter === item.id ? 'is-active' : ''}`}
              onClick={() => setMyFilter(item.id)}
            >
              {item.label}
              <span>{item.count}</span>
            </button>
          ))}
        </div>

        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {visibleMyEmployees.map((employee) => (
            <button
              key={employee.employeeId}
              type="button"
              className="hb-card p-5 text-left transition-transform duration-150 hover:-translate-y-0.5"
              onClick={() => openEmployee(employee.employeeId)}
            >
              <div className="mb-3 flex items-start gap-3">
                <span className="hb-squircle h-11 w-11 bg-[#ece7fb] text-[#6a5acd]">
                  {firstChar(employee.nickname)}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center justify-between gap-2">
                    <h3 className="truncate text-[15px] font-semibold text-[#0a0a0a]">{employee.nickname}</h3>
                    <span className={`hb-pill ${ownershipClass(employee.instanceType)}`}>
                      {ownershipLabel(employee.instanceType)}
                    </span>
                  </div>
                  <p className="mt-1 truncate text-xs text-[#737373]">{formatEmployeeSubtitle(employee)}</p>
                </div>
              </div>
              <p className="line-clamp-2 min-h-10 text-sm leading-relaxed text-[#404040]">
                {formatEmployeeSignal(employee)}
              </p>
              <div className="mt-3 rounded-2xl border border-[#f3f4f6] bg-[#fafafa] p-3">
                <div className="text-[11px] font-semibold uppercase tracking-[0.16em] text-[#737373]">IM 接入状态</div>
                <div className="mt-2">
                  <ImStatusStrip employeeId={employee.employeeId} compact onClick={() => openImPicker(employee)} />
                </div>
              </div>
              <div className="mt-4 flex items-center justify-between border-t border-[#f5f5f5] pt-3 text-xs text-[#737373]">
                <span>最近更新 {employee.createdAt}</span>
                <div className="flex items-center gap-2">
                  {employee.status === 'live' ? (
                    <button
                      type="button"
                      className="hb-btn-link text-xs"
                      onClick={(event) => {
                        event.stopPropagation()
                        const connected = getConnectedChannels(employee.employeeId).length > 0
                        if (connected) {
                          openImPicker(employee)
                        } else {
                          openImConfig(employee.employeeId)
                        }
                      }}
                    >
                      {getConnectedChannels(employee.employeeId).length > 0 ? '去 IM' : '配置 IM'}
                    </button>
                  ) : null}
                  {employee.instanceType === 'personal_clone' && employee.status === 'live' ? (
                    <button
                      type="button"
                      className="hb-btn-link text-xs"
                      onClick={(event) => {
                        event.stopPropagation()
                        setSelectedEmployeeId(employee.employeeId)
                        setCloneName(`${employee.nickname} · 私有分支`)
                        setCloneDesc('基于当前分身创建的私有分支，保留独立对话与配置。')
                        setView('branch')
                      }}
                    >
                      私有分支
                    </button>
                  ) : null}
                  <span className="text-[#4a6cf7]">查看详情 →</span>
                </div>
              </div>
            </button>
          ))}
        </div>
      </section>
    )
  }

  function renderTemplateDetail() {
    if (loadingTemplateDetail || !templateDetail) {
      return (
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载模板详情...
        </div>
      )
    }

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView('templates')}>
          <ArrowLeft size={14} />
          返回模板池
        </button>

        <div className="hb-card p-6">
          <div className="flex flex-wrap items-start gap-4">
            <span className="hb-squircle h-16 w-16 bg-[#dde9ff] text-2xl text-[#3d5cff]">
              {firstChar(templateDetail.name)}
            </span>
            <div className="min-w-0 flex-1">
              <h1 className="text-[30px] font-semibold leading-tight text-[#0a0a0a]">{templateDetail.name}</h1>
              <p className="mt-2 text-sm text-[#737373]">{templateDetail.tagline}</p>
              <p className="mt-3 text-sm leading-relaxed text-[#404040]">{templateDetail.description || '暂无模板说明'}</p>
            </div>
            <div className="flex flex-col gap-2">
              <button type="button" className="hb-btn-primary" onClick={() => void startHire(templateDetail.templateId, false)} disabled={busy}>
                <Play size={14} />
                发起雇佣
              </button>
              <button type="button" className="hb-btn-ghost" onClick={() => void startHire(templateDetail.templateId, true)} disabled={busy}>
                <ShieldCheck size={14} />
                使用 Fixture
              </button>
            </div>
          </div>
        </div>

        <div className="grid gap-5 xl:grid-cols-2">
          <div className="hb-card p-6">
            <h2 className="text-base font-semibold text-[#0a0a0a]">核心能力</h2>
            <ul className="mt-3 space-y-2 text-sm text-[#404040]">
              {templateDetail.coreAbilities.map((item) => (
                <li key={item}>• {item}</li>
              ))}
            </ul>
          </div>
          <div className="hb-card p-6">
            <h2 className="text-base font-semibold text-[#0a0a0a]">能力边界</h2>
            <ul className="mt-3 space-y-2 text-sm text-[#404040]">
              {templateDetail.responsibilityBoundary.outOfScope.map((item) => (
                <li key={item}>• {item}</li>
              ))}
            </ul>
          </div>
        </div>

        <div className="hb-card p-6">
          <h2 className="text-base font-semibold text-[#0a0a0a]">准入条件</h2>
          <div className="mt-4 grid gap-3 md:grid-cols-2">
            {templateDetail.prerequisites.map((item) => (
              <div key={`${item.systemName}-${item.permissionName}`} className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
                <div className="text-sm font-semibold text-[#0a0a0a]">{item.systemName}</div>
                <div className="mt-1 text-xs text-[#737373]">{item.permissionName}</div>
                <div className="mt-2 text-xs text-[#404040]">{item.purpose}</div>
              </div>
            ))}
          </div>
        </div>
      </section>
    )
  }

  function renderEmployeeDetail() {
    if (loadingEmployeeDetail || !employeeDetail) {
      return (
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载实例详情...
        </div>
      )
    }

    const isDepartment = employeeDetail.instanceType === 'department'
    const isLive = employeeDetail.status === 'live'
    const isPersonal = employeeDetail.instanceType === 'personal_clone' || employeeDetail.instanceType === 'private_branch'

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView(isDepartment ? 'dept' : 'my')}>
          <ArrowLeft size={14} />
          返回{isDepartment ? '部门数字员工' : '我的数字员工'}
        </button>

        <div className="hb-card p-6">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3">
                <span className="hb-squircle h-12 w-12 bg-[#dde9ff] text-[#3d5cff]">
                  {firstChar(employeeDetail.nickname)}
                </span>
                <div className="min-w-0">
                  <h1 className="truncate text-[28px] font-semibold leading-tight text-[#0a0a0a]">{employeeDetail.nickname}</h1>
                  <p className="mt-1 text-sm text-[#737373]">
                    模板：{employeeDetail.sourceTemplate} · 最近更新 {employeeDetail.createdAt}
                  </p>
                </div>
              </div>
              <p className="mt-4 text-sm leading-relaxed text-[#404040]">{employeeDetail.primarySignal || employeeDetail.stageSummary}</p>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <span className={`hb-pill ${statusClass(employeeDetail.status, employeeDetail.lifecycleStatus)}`}>
                {statusLabel(employeeDetail.status, employeeDetail.lifecycleStatus)}
              </span>
              <span className={`hb-pill ${ownershipClass(employeeDetail.instanceType)}`}>
                {ownershipLabel(employeeDetail.instanceType)}
              </span>
            </div>
          </div>

          <div className="mt-5 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-3">
              <div className="text-xs text-[#737373]">Owner</div>
              <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">{employeeDetail.ownerUserId}</div>
            </div>
            <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-3">
              <div className="text-xs text-[#737373]">Department</div>
              <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">{employeeDetail.departmentId}</div>
            </div>
            <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-3">
              <div className="text-xs text-[#737373]">生命周期</div>
              <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">{employeeDetail.lifecycleStatus}</div>
            </div>
            <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-3">
              <div className="text-xs text-[#737373]">阶段摘要</div>
              <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">{safeText(employeeDetail.stageSummary)}</div>
            </div>
          </div>

          <div className="mt-5 flex flex-wrap gap-2">
            {isDepartment && isLive ? (
              <button type="button" className="hb-btn-primary" onClick={() => setView('clone')}>
                <Copy size={14} />
                创建个人分身
              </button>
            ) : null}
            {isDepartment ? (
              <>
                <button type="button" className="hb-btn-ghost" onClick={() => setView('ai-eval')}>
                  <Sparkles size={14} />
                  AI 评估
                </button>
                <button type="button" className="hb-btn-ghost" onClick={() => setView('human-eval')}>
                  <ShieldCheck size={14} />
                  人工评估
                </button>
                <button type="button" className="hb-btn-ghost" onClick={() => setView('review')}>
                  <GitBranch size={14} />
                  Review
                </button>
                <button type="button" className="hb-btn-ghost" onClick={() => setView('publish')}>
                  <ArrowRight size={14} />
                  发布 / 上岗
                </button>
              </>
            ) : null}
            {isPersonal ? (
              <>
                <button type="button" className="hb-btn-primary" onClick={() => setView('chat')}>
                  <MessageSquare size={14} />
                  站内对话
                </button>
                <button type="button" className="hb-btn-ghost" onClick={() => openImConfig(employeeDetail.employeeId)}>
                  <ExternalLink size={14} />
                  IM 配置
                </button>
                {getConnectedChannels(employeeDetail.employeeId).length > 0 ? (
                  <button type="button" className="hb-btn-ghost" onClick={() => openImPicker(employeeDetail)}>
                    去 IM
                  </button>
                ) : null}
                <button type="button" className="hb-btn-ghost" onClick={() => setView('branch')}>
                  <GitBranch size={14} />
                  私有分支
                </button>
              </>
            ) : null}
          </div>
        </div>

        <div className="grid gap-5 xl:grid-cols-2">
          <div className="hb-card p-6">
            <h2 className="text-base font-semibold text-[#0a0a0a]">能力与边界</h2>
            <div className="mt-3 space-y-2 text-sm text-[#404040]">
              {employeeDetail.capabilities.map((item) => (
                <div key={item.name} className="flex items-center justify-between gap-3 rounded-2xl border border-[#f3f4f6] bg-[#fafafa] px-3 py-2">
                  <span>{item.name}</span>
                  <span className={`hb-pill ${item.ready ? 'green' : 'gray'}`}>{item.ready ? '已启用' : '未启用'}</span>
                </div>
              ))}
            </div>
          </div>

          <div className="hb-card p-6">
            <h2 className="text-base font-semibold text-[#0a0a0a]">待办事项</h2>
            <div className="mt-3 space-y-2">
              {employeeDetail.pendingActions.length > 0 ? (
                employeeDetail.pendingActions.map((action) => (
                  <div key={action} className="flex items-center justify-between gap-3 rounded-2xl border border-[#f3f4f6] bg-[#fafafa] px-3 py-2 text-sm">
                    <span>{action}</span>
                    <button
                      type="button"
                      className="hb-btn-link text-xs"
                      onClick={async () => {
                        setBusy(true)
                        try {
                          const updated = await api.employeeRuntime.completePendingAction(employeeDetail.employeeId, action)
                          setEmployeeDetail(updated)
                          pushToast('待办已完成', 'success')
                          await reload()
                        } catch (requestError: unknown) {
                          pushToast(requestError instanceof Error ? requestError.message : '完成待办失败', 'error')
                        } finally {
                          setBusy(false)
                        }
                      }}
                    >
                      完成
                    </button>
                  </div>
                ))
              ) : (
                <div className="rounded-2xl border border-[#f3f4f6] bg-[#fafafa] px-3 py-4 text-sm text-[#737373]">
                  当前没有待办事项。
                </div>
              )}
            </div>
          </div>
        </div>

        {isPersonal ? (
          <div className="hb-card p-6">
            <div className="row between wrap" style={{ marginBottom: 14 }}>
              <h3 className="section-h" style={{ marginBottom: 0 }}>IM 接入区</h3>
              <button className="btn btn-ghost btn-sm" onClick={() => openImConfig(employeeDetail.employeeId)}>前往多平台 IM 配置</button>
            </div>

            <div className="grid gap-3 lg:grid-cols-3">
              {(Object.keys(IM_CHANNEL_META) as ImChannelId[]).map((channelId) => {
                const channel = IM_CHANNEL_META[channelId]
                const binding = getBinding(employeeDetail.employeeId, channelId)
                const status = getBindingStatus(employeeDetail.employeeId, channelId)
                const statusText = status === 'connected' ? '已连接' : status === 'error' ? '配置异常' : '未配置'
                const actionLabel = status === 'connected' ? `去 ${channel.name}` : status === 'error' ? '重新配置' : '配置 IM'

                return (
                  <div key={channelId} className="status-panel">
                    <div className="row between" style={{ alignItems: 'flex-start' }}>
                      <div className="row" style={{ gap: 10 }}>
                        <span className={`jump-chip-mark ${channel.accent}`}>{channel.short}</span>
                        <div>
                          <div style={{ fontWeight: 600 }}>{channel.name}</div>
                          <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>
                            {binding ? `${binding.methodId === 'callback' ? 'URL 回调' : 'WebSocket 长连接'} · ${binding.connectedAt}` : '尚未完成接入'}
                          </div>
                        </div>
                      </div>
                      <span className={`pill ${status === 'connected' ? 'green' : status === 'error' ? 'pink' : 'gray'} dot`}>
                        {statusText}
                      </span>
                    </div>
                    <div className="spacer-16" />
                    <button className="btn btn-ghost btn-sm" onClick={() => status === 'connected' ? openImPicker(employeeDetail as EmployeeSummary) : openImConfig(employeeDetail.employeeId)}>
                      {actionLabel}
                    </button>
                  </div>
                )
              })}
            </div>
          </div>
        ) : null}
      </section>
    )
  }

  function renderHireView() {
    if (!hireId) {
      return (
        <div className="hb-card p-6">
          <h1 className="text-[28px] font-semibold leading-tight text-[#0a0a0a]">雇佣流程</h1>
          <p className="mt-2 text-sm text-[#737373]">先从模板池点击一个模板，才会开始真实的雇佣流程。</p>
        </div>
      )
    }

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView('template')}>
          <ArrowLeft size={14} />
          返回模板详情
        </button>

        <div className="hb-card p-6">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <div className="text-xs uppercase tracking-[0.2em] text-[#737373]">六步雇佣</div>
              <h1 className="mt-2 text-[28px] font-semibold leading-tight text-[#0a0a0a]">
                真实雇佣流程 · {selectedTemplateId || '未选择模板'}
              </h1>
              <p className="mt-2 text-sm text-[#737373]">当前 hireId：{hireId}</p>
            </div>
            <div className="flex flex-wrap gap-2">
              <button type="button" className="hb-btn-ghost" onClick={() => void refreshHire()} disabled={busy}>
                <RefreshCw size={14} />
                刷新
              </button>
              <button type="button" className="hb-btn-ghost" onClick={() => setView('ai-eval')}>
                <Sparkles size={14} />
                进入 AI 评估
              </button>
            </div>
          </div>

          <div className="mt-4 grid gap-3 md:grid-cols-3 xl:grid-cols-6">
            {['目标', '场景', '系统', '缺口', '实例', '完成'].map((step, index) => (
              <div key={step} className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-3">
                <div className="text-xs text-[#737373]">Step {index + 1}</div>
                <div className="mt-1 text-sm font-semibold text-[#0a0a0a]">{step}</div>
              </div>
            ))}
          </div>
        </div>

        <div className="grid gap-5 xl:grid-cols-[1.4fr_0.9fr]">
          <div className="hb-card p-6">
            <div className="flex items-center justify-between gap-2">
              <h2 className="text-base font-semibold text-[#0a0a0a]">对话与材料</h2>
              <span className={`hb-pill ${hireStatusText ? statusClass(hireStatusText) : 'gray'}`}>{hireStatusText || '待同步'}</span>
            </div>

            <div className="mt-4 space-y-3 rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
              {(hireMessages.length > 0 ? hireMessages : (hireTimeline?.messages ?? [])).map((message) => (
                <div key={message.messageId} className="rounded-2xl border border-[#f3f4f6] bg-white px-3 py-2">
                  <div className="text-[11px] text-[#737373]">{message.role}</div>
                  <div className="mt-1 text-sm leading-relaxed text-[#404040]">{message.content}</div>
                </div>
              ))}
              {hireMessages.length === 0 && !hireTimeline ? (
                <div className="text-sm text-[#737373]">还没有启动对话，点击下面的输入框发送第一条消息。</div>
              ) : null}
            </div>

            <div className="mt-4 flex gap-2">
              <input
                value={hireInput}
                onChange={(event) => setHireInput(event.target.value)}
                className="hb-search-input flex-1"
                placeholder="向雇佣沙箱发送一条消息"
              />
              <button type="button" className="hb-btn-primary" onClick={() => void sendHireMessage()} disabled={busy}>
                <Send size={14} />
                发送
              </button>
            </div>

            <div className="mt-3 flex flex-wrap gap-2">
              <label className="hb-btn-ghost cursor-pointer">
                <Upload size={14} />
                上传资料
                <input
                  type="file"
                  className="hidden"
                  onChange={(event) => {
                    const file = event.target.files?.[0]
                    if (file) {
                      void uploadHireFile(file)
                    }
                  }}
                />
              </label>
              <button type="button" className="hb-btn-ghost" onClick={() => setHireInput('请总结目前的业务背景和下一步需要补齐的资料')}>
                <Paperclip size={14} />
                插入提示语
              </button>
            </div>
          </div>

          <div className="space-y-5">
            <div className="hb-card p-6">
              <h2 className="text-base font-semibold text-[#0a0a0a]">流程控制</h2>
              <div className="mt-3 flex flex-wrap gap-2">
                <button type="button" className="hb-btn-ghost" onClick={() => void refreshHire()} disabled={busy}>
                  <RefreshCw size={14} />
                  刷新状态
                </button>
                <button type="button" className="hb-btn-ghost" onClick={() => api.hiringWorkflow.pauseConversation(hireId).then(() => pushToast('对话已暂停', 'success')).catch((error) => pushToast(error instanceof Error ? error.message : '暂停失败', 'error'))}>
                  暂停
                </button>
                <button type="button" className="hb-btn-ghost" onClick={() => api.hiringWorkflow.resumeConversation(hireId).then(() => pushToast('对话已恢复', 'success')).catch((error) => pushToast(error instanceof Error ? error.message : '恢复失败', 'error'))}>
                  恢复
                </button>
                <button type="button" className="hb-btn-primary" onClick={() => void finalizeHire()} disabled={busy}>
                  <Play size={14} />
                  生成实例
                </button>
              </div>
              {hireWorkflow ? (
                <div className="mt-4 space-y-2 text-sm text-[#404040]">
                  <div>阶段：{hireWorkflow.currentStage}</div>
                  <div>收集阶段：{hireWorkflow.collectionPhase}</div>
                  <div>审计日志：{hireWorkflow.auditLogs.length} 条</div>
                  <div>是否暂停：{hireWorkflow.isConversationPaused ? '是' : '否'}</div>
                </div>
              ) : null}
              {hirePreview ? (
                <div className="mt-4 rounded-2xl border border-[#f3f4f6] bg-[#fafafa] p-3 text-xs text-[#404040]">
                  <div className="font-semibold text-[#0a0a0a]">最新预览</div>
                  <div className="mt-1">{hirePreview.summary}</div>
                  {hirePreview.missingFields.length > 0 ? <div className="mt-2">缺口：{hirePreview.missingFields.join('、')}</div> : null}
                </div>
              ) : null}
            </div>

            <div className="hb-card p-6">
              <h2 className="text-base font-semibold text-[#0a0a0a]">当前流程信息</h2>
              <div className="mt-3 space-y-2 text-sm text-[#404040]">
                <div>当前模板：{templateDetail?.name || selectedTemplateId || '--'}</div>
                <div>流程状态：{hireTimeline?.currentStage || '--'}</div>
                <div>租约状态：{hireStatusText || '--'}</div>
                <div>对话数：{hireTimeline?.messages.length ?? 0}</div>
              </div>
            </div>
          </div>
        </div>
      </section>
    )
  }

  function renderAiEvalView() {
    if (!selectedEmployeeId) {
      return <div className="hb-card p-6">先选择一个实例，再进入 AI 评估。</div>
    }

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView('employee')}>
          <ArrowLeft size={14} />
          返回实例详情
        </button>

        <div className="hb-card p-6">
          <h1 className="text-[28px] font-semibold leading-tight text-[#0a0a0a]">
            AI 评估 · {employeeDetail?.nickname || selectedEmployeeId}
          </h1>
          <p className="mt-2 text-sm text-[#737373]">这里使用真实后端的评估状态、沙箱对话和 AI 决策接口。</p>
        </div>

        <div className="grid gap-5 xl:grid-cols-2">
          <div className="hb-card p-6">
            <h2 className="text-base font-semibold text-[#0a0a0a]">评估状态</h2>
            <div className="mt-3 space-y-2 text-sm text-[#404040]">
              <div>总体状态：{evaluationState?.overallStatus || '--'}</div>
              <div>建议：{evaluationState?.recommendation || '--'}</div>
              <div>场景数：{evaluationState?.scenarios.length ?? 0}</div>
              <div>Session：{evaluationState?.sessionId || '--'}</div>
            </div>
            <div className="mt-4 space-y-3">
              {(evaluationState?.scenarios ?? []).map((scenario) => (
                <div key={scenario.scenarioId} className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-3">
                  <div className="flex items-center justify-between gap-2">
                    <div className="text-sm font-semibold text-[#0a0a0a]">{scenario.scenarioName}</div>
                    <span className={`hb-pill ${statusClass(scenario.verdict)}`}>{statusLabel(scenario.verdict)}</span>
                  </div>
                  <div className="mt-1 text-xs text-[#737373]">{scenario.status}</div>
                  {scenario.verdictComment ? <div className="mt-2 text-xs text-[#404040]">{scenario.verdictComment}</div> : null}
                </div>
              ))}
            </div>
          </div>

          <div className="hb-card p-6">
            <div className="flex items-center justify-between gap-2">
              <h2 className="text-base font-semibold text-[#0a0a0a]">评估沙箱对话</h2>
              <button type="button" className="hb-btn-ghost" onClick={() => void reload()}>
                <RefreshCw size={14} />
                刷新
              </button>
            </div>
            <div className="mt-3 space-y-2 rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
              {(evaluationConversation?.messages ?? []).map((message) => (
                <div key={message.messageId} className="rounded-2xl border border-[#f3f4f6] bg-white px-3 py-2">
                  <div className="text-[11px] text-[#737373]">{message.role}</div>
                  <div className="mt-1 text-sm leading-relaxed text-[#404040]">{message.content}</div>
                </div>
              ))}
              {(evaluationConversation?.messages ?? []).length === 0 ? <div className="text-sm text-[#737373]">暂无消息</div> : null}
            </div>
            <div className="mt-4 flex gap-2">
              <input
                value={evaluationInput}
                onChange={(event) => setEvaluationInput(event.target.value)}
                className="hb-search-input flex-1"
                placeholder="发送一条评估消息"
              />
              <button type="button" className="hb-btn-primary" onClick={() => void sendEvaluationMessage()} disabled={busy}>
                <Send size={14} />
                发送
              </button>
            </div>

            <div className="mt-4 flex flex-wrap gap-2">
              {(['START', 'LOAD_SKILL', 'RUN', 'PASS', 'FAIL'] as const).map((decision) => (
                <button key={decision} type="button" className="hb-btn-ghost" onClick={() => void submitAiDecision(decision)} disabled={busy}>
                  {decision}
                </button>
              ))}
            </div>
          </div>
        </div>
      </section>
    )
  }

  function renderHumanEvalView() {
    if (!selectedEmployeeId) {
      return <div className="hb-card p-6">先选择一个实例，再进入人工评估。</div>
    }

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView('employee')}>
          <ArrowLeft size={14} />
          返回实例详情
        </button>

        <div className="hb-card p-6">
          <h1 className="text-[28px] font-semibold leading-tight text-[#0a0a0a]">
            人工评估 · {employeeDetail?.nickname || selectedEmployeeId}
          </h1>
          <p className="mt-2 text-sm text-[#737373]">这里接真实后端的人评决策接口，保留原型中的判定感。</p>
        </div>

        <div className="hb-card p-6">
          <h2 className="text-base font-semibold text-[#0a0a0a]">场景摘要</h2>
          <div className="mt-3 space-y-3">
            {(evaluationState?.scenarios ?? []).map((scenario) => (
              <div key={scenario.scenarioId} className="rounded-2xl border border-[#ececec] bg-[#fafafa] p-3">
                <div className="flex items-center justify-between gap-2">
                  <div className="text-sm font-semibold text-[#0a0a0a]">{scenario.scenarioName}</div>
                  <span className={`hb-pill ${statusClass(scenario.verdict)}`}>{statusLabel(scenario.verdict)}</span>
                </div>
                <div className="mt-1 text-xs text-[#737373]">{scenario.status}</div>
              </div>
            ))}
          </div>
        </div>

        <div className="hb-card p-6">
          <h2 className="text-base font-semibold text-[#0a0a0a]">人工决策</h2>
          <div className="mt-3 flex flex-wrap gap-2">
            <button type="button" className="hb-btn-primary" onClick={() => void submitHumanDecision('ONBOARD')} disabled={busy}>
              <ShieldCheck size={14} />
              通过并待上岗
            </button>
            <button type="button" className="hb-btn-ghost" onClick={() => void submitHumanDecision('REJECT')} disabled={busy}>
              不通过并进入 Review
            </button>
            <button type="button" className="hb-btn-ghost" onClick={() => void submitHumanDecision('FORCE')} disabled={busy}>
              强制通过
            </button>
          </div>
          <p className="mt-3 text-xs text-[#737373]">发布说明会一并带入接口，保持原型里的“先评估后上岗”节奏。</p>
        </div>
      </section>
    )
  }

  function renderReviewView() {
    if (!selectedEmployeeId || !employeeDetail) {
      return <div className="hb-card p-6">先选择一个失败或待回退的实例。</div>
    }

    const basedTemplateId = employeeDetail.basedOnTemplateId || employeeDetail.sourceTemplateId

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView('employee')}>
          <ArrowLeft size={14} />
          返回实例详情
        </button>

        <div className="hb-card p-6">
          <h1 className="text-[28px] font-semibold leading-tight text-[#0a0a0a]">Review · {employeeDetail.nickname}</h1>
          <p className="mt-2 text-sm text-[#737373]">在这里做回退或继续雇佣。当前分支里没有独立私有分支 API 时，会用个人分身接口承接。</p>
        </div>

        <div className="grid gap-5 md:grid-cols-3">
          <div className="hb-card p-4">
            <div className="text-xs text-[#737373]">当前状态</div>
            <div className="mt-2 text-lg font-semibold text-[#0a0a0a]">{employeeDetail.lifecycleStatus}</div>
          </div>
          <div className="hb-card p-4">
            <div className="text-xs text-[#737373]">来源模板</div>
            <div className="mt-2 text-lg font-semibold text-[#0a0a0a]">{basedTemplateId || '--'}</div>
          </div>
          <div className="hb-card p-4">
            <div className="text-xs text-[#737373]">主信号</div>
            <div className="mt-2 text-lg font-semibold text-[#0a0a0a]">{safeText(employeeDetail.primarySignal)}</div>
          </div>
        </div>

        <div className="hb-card p-6">
          <div className="flex flex-wrap gap-2">
            <button type="button" className="hb-btn-ghost" onClick={() => void rollbackToHired()} disabled={busy}>
              回退到已雇佣
            </button>
            <button type="button" className="hb-btn-ghost" onClick={() => void retryAi()} disabled={busy}>
              重新进入 AI 评估
            </button>
            <button type="button" className="hb-btn-primary" onClick={() => setView('hire')} disabled={!basedTemplateId || busy}>
              继续雇佣（Fixture）
            </button>
          </div>
          <div className="mt-4 rounded-2xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
            {reviewComment}
          </div>
          <textarea
            value={reviewComment}
            onChange={(event) => setReviewComment(event.target.value)}
            className="mt-3 min-h-[100px] w-full rounded-2xl border border-[#e5e5e5] bg-white px-4 py-3 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
          />
        </div>
      </section>
    )
  }

  function renderPublishView() {
    if (!selectedEmployeeId || !employeeDetail) {
      return <div className="hb-card p-6">先选择一个待上岗实例。</div>
    }

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView('employee')}>
          <ArrowLeft size={14} />
          返回实例详情
        </button>

        <div className="hb-card p-6">
          <h1 className="text-[28px] font-semibold leading-tight text-[#0a0a0a]">发布 / 上岗 · {employeeDetail.nickname}</h1>
          <p className="mt-2 text-sm text-[#737373]">这里用真实后端的上岗接口完成最后一步。原型里的“生成实例包”对应这一步。</p>
        </div>

        <div className="hb-card p-6">
          <div className="flex flex-wrap gap-2">
            <button type="button" className="hb-btn-primary" onClick={() => void submitHumanDecision('ONBOARD')} disabled={busy}>
              <CheckCircle2 size={14} />
              上岗
            </button>
            <button type="button" className="hb-btn-ghost" onClick={() => void submitHumanDecision('FORCE')} disabled={busy}>
              强制上岗
            </button>
          </div>
          <textarea
            value={publishComment}
            onChange={(event) => setPublishComment(event.target.value)}
            className="mt-4 min-h-[120px] w-full rounded-2xl border border-[#e5e5e5] bg-white px-4 py-3 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
          />
        </div>
      </section>
    )
  }

  function renderImView() {
    const employee = selectedEmployeeId ? (employeeDetail ?? employees.find((item) => item.employeeId === selectedEmployeeId) ?? null) : null
    const isDepartment = employee?.instanceType === 'department'
    const currentBindings = employee ? getEmployeeBindings(employee.employeeId) : {}
    const connectedChannels = employee ? getConnectedChannels(employee.employeeId) : []
    const schema = IM_SCHEMAS[imChannelId]
    const method = schema.methods[imMethodId]
    const currentBinding = employee ? getBinding(employee.employeeId, imChannelId) : null
    const currentStatus = currentBinding ? currentBinding.status : 'unconfigured'
    const webhookUrl = method.webhookPath && employee ? `https://{platform}/im/${method.webhookPath}/webhook/${employee.employeeId}` : ''

    const rows = imItems.filter((item) => {
      if (selectedEmployeeId && item.employeeId !== selectedEmployeeId) return false
      if (imFilter === 'pending') return item.status === 'pending'
      if (imFilter === 'confirmed') return item.status === 'confirmed'
      return true
    })

    function switchChannel(nextChannelId: ImChannelId) {
      if (imPhase === 1 || !employee || isDepartment) return
      const nextBinding = currentBindings[nextChannelId] ?? null
      const nextMethod = nextBinding ? nextBinding.methodId : 'websocket'
      setImChannelId(nextChannelId)
      setImMethodId(nextMethod)
      setImForm(nextBinding ? { ...nextBinding.form } : {})
      setImPhase(0)
      setImSteps(buildConnectSteps(nextChannelId, nextMethod))
    }

    function switchMethod(nextMethodId: ImMethodId) {
      if (imPhase === 1 || !employee || isDepartment) return
      setImMethodId(nextMethodId)
      setImForm({})
      setImPhase(0)
      setImSteps(buildConnectSteps(imChannelId, nextMethodId))
    }

    function updateField(key: string, value: string) {
      setImForm((previous) => ({ ...previous, [key]: value }))
    }

    function removeCurrentBinding() {
      if (!employee || isDepartment) return
      removeEmployeeBinding(employee.employeeId, imChannelId)
      setImForm({})
      setImPhase(0)
      setImSteps(buildConnectSteps(imChannelId, 'websocket'))
      setImMethodId('websocket')
      pushToast(`${schema.title} 绑定已解除`, 'info')
    }

    function startConnect() {
      if (!employee || isDepartment) return
      const missing = method.fields.find((field) => field.required && !(imForm[field.key] || '').trim())
      if (missing) {
        pushToast(`${missing.label} 必填`, 'error')
        return
      }

      const nextSteps = buildConnectSteps(imChannelId, imMethodId)
      setImPhase(1)
      setImSteps(nextSteps)

      let pointer = 0
      const timer = window.setInterval(() => {
        setImSteps((previous) => previous.map((item, index) => (index === pointer ? { ...item, done: true } : item)))
        pointer += 1
        if (pointer >= nextSteps.length) {
          window.clearInterval(timer)
          window.setTimeout(() => {
            saveEmployeeBinding(employee.employeeId, imChannelId, {
              channelId: imChannelId,
              methodId: imMethodId,
              form: { ...imForm },
              status: 'connected',
              connectedAt: formatDate(new Date().toISOString()),
            })
            setImPhase(2)
            pushToast(`${schema.title} 已连接`, 'success')
            void reload()
          }, 320)
        }
      }, 450)
    }

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView('employee')}>
          <ArrowLeft size={14} />
          返回实例详情
        </button>

        <div className="hb-card p-6">
          <h1 className="text-[28px] font-semibold leading-tight text-[#0a0a0a]">IM 配置</h1>
          <p className="mt-2 text-sm text-[#737373]">
            {employee
              ? `${getImEmployeeTypeLabel(employee)}。这里对齐原型里的多平台 IM 接入流，支持飞书 / 钉钉 / 企微的本地绑定、解除绑定和“去 IM”跳转。`
              : '先选择一个个人分身或私有分支，再进行 IM 配置。'}
          </p>
        </div>

        {employee && isDepartment ? (
          <div className="hb-card p-5">
            <div className="flex items-center justify-between gap-3">
              <div>
                <div className="text-sm font-semibold text-[#0a0a0a]">部门员工不配置 IM</div>
                <div className="mt-1 text-xs text-[#737373]">IM 接入属于个人分身层面的可选动作。先复制一个分身，再到这里完成飞书 / 钉钉 / 企微绑定。</div>
              </div>
              <button type="button" className="hb-btn-primary" onClick={() => setView('clone')}>
                去复制分身
              </button>
            </div>
          </div>
        ) : null}

        {employee && !isDepartment ? (
          <div className="grid gap-5 xl:grid-cols-[1.25fr_0.95fr]">
            <div className="hb-card p-6">
              <h2 className="text-base font-semibold text-[#0a0a0a]">平台选择</h2>
              <div className="mt-3 flex flex-wrap gap-2">
                {(Object.keys(IM_CHANNEL_META) as ImChannelId[]).map((channelId) => {
                  const channel = IM_CHANNEL_META[channelId]
                  const binding = currentBindings[channelId]
                  const status = binding ? binding.status : 'unconfigured'
                  return (
                    <button
                      key={channelId}
                      type="button"
                      className={`hb-chip ${imChannelId === channelId ? 'is-active' : ''}`}
                      onClick={() => switchChannel(channelId)}
                    >
                      <span className={`jump-chip-mark ${channel.accent}`}>{channel.short}</span>
                      <span>{channel.name}</span>
                      <span className={`hb-pill ${status === 'connected' ? 'green' : status === 'error' ? 'orange' : 'gray'} dot`}>
                        {status === 'connected' ? '已连接' : status === 'error' ? '异常' : '未配置'}
                      </span>
                    </button>
                  )
                })}
              </div>

              <div className="spacer-16" />
              <div className="status-panel">
                <div className="row between" style={{ alignItems: 'flex-start' }}>
                  <div>
                    <div style={{ fontWeight: 600, fontSize: 15 }}>{schema.title}</div>
                    <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>{schema.intro}</div>
                  </div>
                  <span className={`pill ${currentStatus === 'connected' ? 'green' : currentStatus === 'error' ? 'pink' : 'gray'} dot`}>
                    {currentStatus === 'connected' ? '已连接' : currentStatus === 'error' ? '配置异常' : '未配置'}
                  </span>
                </div>
                <div className="spacer-16" />
                <dl className="mini-kv">
                  <dt>绑定对象</dt><dd>{employee.nickname}</dd>
                  <dt>连接方式</dt><dd>{currentBinding ? (currentBinding.methodId === 'callback' ? 'URL 回调' : 'WebSocket 长连接') : '尚未选择'}</dd>
                  <dt>最近连接</dt><dd>{currentBinding ? currentBinding.connectedAt : '—'}</dd>
                  <dt>已连接平台</dt><dd><span className="tnum">{connectedChannels.length}</span> 个</dd>
                </dl>
              </div>

              <div className="spacer-16" />
              <div className="callout info">已连接平台之间互不影响。重新注册只会更新当前平台，不会覆盖其他平台的凭据。</div>
            </div>

            <div className="hb-card p-6">
              {imPhase !== 1 ? (
                <>
                  <h2 className="text-base font-semibold text-[#0a0a0a]">接入模式</h2>
                  <div className="mt-3 flex flex-wrap gap-2">
                    {Object.entries(schema.methods).map(([methodKey, item]) => (
                      <button
                        key={methodKey}
                        type="button"
                        className={`hb-chip ${imMethodId === methodKey ? 'is-active' : ''}`}
                        onClick={() => switchMethod(methodKey as ImMethodId)}
                      >
                        {item.label}
                      </button>
                    ))}
                  </div>
                  <div className="spacer-12" />
                  <div className="callout info">{method.help}</div>

                  <div className="spacer-16" />
                  <h3 className="section-h">凭证表单</h3>
                  <div className="field-grid">
                    {method.fields.map((field) => (
                      <div key={field.key} className={`form-row ${method.fields.length % 2 === 1 && method.fields.indexOf(field) === method.fields.length - 1 ? 'field-span-2' : ''}`}>
                        <label>{field.label} <span className="muted">· {field.required ? '必填' : '可选'}</span></label>
                        <input
                          type="text"
                          value={imForm[field.key] || ''}
                          placeholder={field.placeholder}
                          onChange={(event) => updateField(field.key, event.target.value)}
                        />
                      </div>
                    ))}
                  </div>

                  {webhookUrl ? (
                    <>
                      <div className="spacer-12" />
                      <div className="inline-panel">
                        <div className="inline-panel-title">Webhook URL</div>
                        <div className="file-chip" style={{ width: '100%', justifyContent: 'space-between' }}>
                          <span>{webhookUrl}</span>
                          <span className="ok">一键复制</span>
                        </div>
                      </div>
                    </>
                  ) : null}

                  <div className="spacer-16" />
                  <div className="row between wrap">
                    <div className="row wrap" style={{ gap: 8 }}>
                      {currentBinding ? <button className="btn btn-ghost btn-sm" onClick={() => openImPicker(employee)}>去 IM</button> : null}
                      {currentBinding ? <button className="btn btn-danger-ghost btn-sm" onClick={removeCurrentBinding}>解除绑定</button> : null}
                    </div>
                    <div className="row wrap" style={{ gap: 8 }}>
                      <button className="btn btn-ghost btn-sm" onClick={() => setView('employee')}>取消</button>
                      <button className="btn btn-primary btn-sm" onClick={startConnect}>
                        {currentBinding ? '保存并重连' : '注册并连接'} <ArrowRight size={14} className="icn" />
                      </button>
                    </div>
                  </div>
                </>
              ) : (
                <>
                  <h2 className="text-base font-semibold text-[#0a0a0a]">连接中…</h2>
                  <div className="status-panel">
                    <div className="row between" style={{ alignItems: 'flex-start' }}>
                      <div>
                        <div style={{ fontWeight: 600, fontSize: 14 }}>{schema.title} · {method.label}</div>
                        <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>正在建立连接，请保持当前配置不变。</div>
                      </div>
                      <span className="pill blue">连接中</span>
                    </div>
                  </div>
                  <div className="spacer-16" />
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
                    {imSteps.map((item, idx) => (
                      <div key={`${item.name}-${idx}`} className="flow-step">
                        <span>{item.name}</span>
                        {item.done ? <span className="pill green dot">已完成</span> : <span className="pill gray">进行中</span>}
                      </div>
                    ))}
                  </div>
                </>
              )}
            </div>
          </div>
        ) : null}

        <div className="hb-card p-6">
          <div className="flex items-center justify-between gap-2">
            <h2 className="text-base font-semibold text-[#0a0a0a]">真实 team IM 待办</h2>
            <div className="hb-chip-row" style={{ margin: 0 }}>
              {[
                { id: 'all' as const, label: '全部', count: rows.length },
                { id: 'pending' as const, label: '待确认', count: rows.filter((item) => item.status === 'pending').length },
                { id: 'confirmed' as const, label: '已确认', count: rows.filter((item) => item.status === 'confirmed').length },
              ].map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className={`hb-chip ${imFilter === item.id ? 'is-active' : ''}`}
                  onClick={() => setImFilter(item.id)}
                >
                  {item.label}
                  <span>{item.count}</span>
                </button>
              ))}
            </div>
          </div>

          <div className="mt-4 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {rows.map((item) => (
              <div key={item.itemId} className="hb-card p-5">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="text-sm font-semibold text-[#0a0a0a]">{item.employeeName}</div>
                    <div className="mt-1 text-xs text-[#737373]">{item.category} · {item.source}</div>
                  </div>
                  <span className={`hb-pill ${item.status === 'confirmed' ? 'green' : 'orange'}`}>
                    {item.status === 'confirmed' ? '已确认' : '待确认'}
                  </span>
                </div>
                <p className="mt-3 text-sm leading-relaxed text-[#404040]">{item.content}</p>
                <div className="mt-4 flex items-center justify-between border-t border-[#f5f5f5] pt-3">
                  <span className="text-xs text-[#737373]">{formatDate(item.receivedAt)}</span>
                  {item.status === 'pending' ? (
                    <button type="button" className="hb-btn-link text-xs" onClick={() => void confirmImItem(item.itemId)}>
                      确认
                    </button>
                  ) : (
                    <span className="text-xs text-[#737373]">已确认 {formatDate(item.confirmedAt)}</span>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>
    )
  }

  function renderChatView() {
    if (!selectedEmployeeId || !employeeDetail) {
      return <div className="hb-card p-6">先选择一个实例，再进入聊天页。</div>
    }

    const key = localChatKey(selectedEmployeeId, role)
    const messages = chatThreads[key] ?? seedLocalChat(employeeDetail.nickname, role)
    const employeeName = employeeDetail.nickname

    function sendLocalChatMessage() {
      if (!chatInput.trim()) return
      const content = chatInput.trim()
      const nextMessages: LocalChatMessage[] = [
        ...messages,
        {
          id: mkId('msg'),
          role: 'user',
          content,
          createdAt: new Date().toISOString(),
        },
        {
          id: mkId('msg'),
          role: 'bot',
          content: botReply(content, employeeName),
          createdAt: new Date().toISOString(),
        },
      ]
      setChatThreads((previous) => ({
        ...previous,
        [key]: nextMessages,
      }))
      setChatInput('')
    }

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView('employee')}>
          <ArrowLeft size={14} />
          返回实例详情
        </button>

        <div className="hb-card p-6">
          <h1 className="text-[28px] font-semibold leading-tight text-[#0a0a0a]">站内对话预览 · {employeeDetail.nickname}</h1>
          <p className="mt-2 text-sm text-[#737373]">当前没有独立的站内聊天 API，这里保留原型中的本地对话体验；真正的对话流请走雇佣 / 评估沙箱。</p>
        </div>

        <div className="hb-card p-6">
          <div className="space-y-3 rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
            {messages.map((message) => (
              <div key={message.id} className="rounded-2xl border border-[#f3f4f6] bg-white px-3 py-2">
                <div className="text-[11px] text-[#737373]">{message.role === 'user' ? '我' : employeeDetail.nickname}</div>
                <div className="mt-1 text-sm leading-relaxed text-[#404040]">{message.content}</div>
              </div>
            ))}
          </div>

          <div className="mt-4 flex gap-2">
            <input
              value={chatInput}
              onChange={(event) => setChatInput(event.target.value)}
              className="hb-search-input flex-1"
              placeholder="输入一条原型聊天消息"
            />
            <button type="button" className="hb-btn-primary" onClick={sendLocalChatMessage}>
              <Send size={14} />
              发送
            </button>
          </div>
        </div>
      </section>
    )
  }

  function renderCloneView(kind: 'clone' | 'quick-clone' | 'branch') {
    if (!selectedEmployeeId || !employeeDetail) {
      return <div className="hb-card p-6">先选择一个可复制的实例。</div>
    }

    const isBranch = kind === 'branch'
    const title = kind === 'clone' ? '创建个人分身' : kind === 'quick-clone' ? '快捷复制' : '创建私有分支'

    return (
      <section className="space-y-5">
        <button type="button" className="hb-btn-ghost" onClick={() => setView('employee')}>
          <ArrowLeft size={14} />
          返回实例详情
        </button>

        <div className="hb-card p-6">
          <h1 className="text-[28px] font-semibold leading-tight text-[#0a0a0a]">
            {title} · {employeeDetail.nickname}
          </h1>
          <p className="mt-2 text-sm text-[#737373]">
            这个动作已经接上真实后端的 personal clone 接口。当前后端没有单独的 private branch 创建接口，所以私有分支在数据层仍会以 personal clone 落库。
          </p>
        </div>

        <div className="hb-card p-6">
          <div className="grid gap-4 md:grid-cols-2">
            <label className="block">
              <div className="mb-1 text-sm font-medium text-[#404040]">display_name</div>
              <input
                value={cloneName}
                onChange={(event) => setCloneName(event.target.value)}
                className="w-full rounded-lg border border-[#e5e5e5] bg-white px-3 py-2 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
                placeholder={isBranch ? `${employeeDetail.nickname} · 私有分支` : `${employeeDetail.nickname} · 我的分身`}
              />
            </label>
            <label className="block">
              <div className="mb-1 text-sm font-medium text-[#404040]">display_description</div>
              <input
                value={cloneDesc}
                onChange={(event) => setCloneDesc(event.target.value)}
                className="w-full rounded-lg border border-[#e5e5e5] bg-white px-3 py-2 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
                placeholder={isBranch ? '只保留 offer / 跟进场景' : '记住我的工作偏好'}
              />
            </label>
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="button" className="hb-btn-primary" onClick={() => void createClone(kind)} disabled={busy}>
              立即生成
            </button>
            <button type="button" className="hb-btn-ghost" onClick={() => setView('employee')}>
              取消
            </button>
          </div>
        </div>
      </section>
    )
  }

  function renderCurrentView() {
    if (loading) {
      return (
        <div className="hb-card flex min-h-[260px] items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载真实数据...
        </div>
      )
    }

    switch (view) {
      case 'templates':
        return renderTemplateList()
      case 'dept':
        return renderDeptList()
      case 'my':
        return renderMyList()
      case 'template':
        return renderTemplateDetail()
      case 'employee':
        return renderEmployeeDetail()
      case 'hire':
        return renderHireView()
      case 'ai-eval':
        return renderAiEvalView()
      case 'human-eval':
        return renderHumanEvalView()
      case 'review':
        return renderReviewView()
      case 'publish':
        return renderPublishView()
      case 'im':
        return renderImView()
      case 'chat':
        return renderChatView()
      case 'clone':
        return renderCloneView('clone')
      case 'quick-clone':
        return renderCloneView('quick-clone')
      case 'branch':
        return renderCloneView('branch')
      default:
        return renderTemplateList()
    }
  }

  const activeTop = view === 'templates' || view === 'template' || view === 'hire'
    ? 'templates'
    : view === 'dept' || view === 'employee' || view === 'ai-eval' || view === 'human-eval' || view === 'review' || view === 'publish'
      ? 'dept'
      : 'my'

  return (
    <div className="hb-shell">
      <header className="hb-topnav">
        <div className="hb-topnav-inner">
          <button
            type="button"
            onClick={() => setView(role === 'manager' ? 'templates' : 'dept')}
            className="hb-brand"
          >
            <span className="hb-brand-logo">雇</span>
            <span className="hb-brand-text">HireBot 原型预览</span>
          </button>

          <nav className="hb-nav">
            {role === 'manager' ? (
              <button type="button" className={`hb-nav-item ${activeTop === 'templates' ? 'is-active' : ''}`} onClick={() => setView('templates')}>
                企业模板池
                <span className="hb-nav-flag">new</span>
              </button>
            ) : null}
            <button type="button" className={`hb-nav-item ${activeTop === 'dept' ? 'is-active' : ''}`} onClick={() => setView('dept')}>
              部门数字员工
            </button>
            <button type="button" className={`hb-nav-item ${activeTop === 'my' ? 'is-active' : ''}`} onClick={() => setView('my')}>
              我的数字员工
            </button>
          </nav>

          <div className="hb-nav-right">
            <div className="hb-role-switch">
              <button type="button" className={role === 'manager' ? 'is-active' : ''} onClick={() => setRole('manager')}>
                🧑‍💼 部门长
              </button>
              <button type="button" className={role === 'member' ? 'is-active' : ''} onClick={() => setRole('member')}>
                🧑‍💻 普通成员
              </button>
            </div>
            <span className="hb-pill blue">Keycloak 授权</span>
            <button type="button" className="hb-btn-ghost text-sm" onClick={() => void importFixtureData()} disabled={fixtureImporting}>
              {fixtureImporting ? '导入中...' : '导入示例数据'}
            </button>
            <div className="hb-user-chip">
              <span className="hb-user-avatar">{role === 'manager' ? '李' : '王'}</span>
              <span>{role === 'manager' ? '李部门长 · 研发部' : '王成员 · 研发部'}</span>
            </div>
          </div>
        </div>
      </header>

      <main className="hb-main">
        <div className="hb-page hb-page-wide">
          {error ? <div className="hb-alert hb-alert-error mb-5"><AlertCircle size={14} /><span>{error}</span></div> : null}

          <div className="mb-5 grid gap-4 md:grid-cols-4">
            <div className="hb-card p-4">
              <div className="text-xs text-[#737373]">模板数</div>
              <div className="mt-2 text-2xl font-semibold text-[#0a0a0a]">{templateTotal}</div>
            </div>
            <div className="hb-card p-4">
              <div className="text-xs text-[#737373]">部门已上岗</div>
              <div className="mt-2 text-2xl font-semibold text-[#0a0a0a]">{deptLiveCount}</div>
            </div>
            <div className="hb-card p-4">
              <div className="text-xs text-[#737373]">部门评估中</div>
              <div className="mt-2 text-2xl font-semibold text-[#0a0a0a]">{deptEvaluatingCount}</div>
            </div>
            <div className="hb-card p-4">
              <div className="text-xs text-[#737373]">个人资产</div>
              <div className="mt-2 text-2xl font-semibold text-[#0a0a0a]">{myLiveCount}</div>
            </div>
          </div>

          {!loading && employees.length === 0 ? (
            <div className="hb-card mb-5 flex flex-wrap items-center justify-between gap-4 p-5">
              <div>
                <div className="text-base font-semibold text-[#0a0a0a]">当前还没有演示实例</div>
                <p className="mt-1 text-sm text-[#737373]">原型页会优先读取真实后端数据；如果还没导入 fixture，这里可以一键生成示例实例。</p>
              </div>
              <button type="button" className="hb-btn-primary" onClick={() => void importFixtureData()} disabled={fixtureImporting}>
                {fixtureImporting ? '导入中...' : '生成示例实例'}
              </button>
            </div>
          ) : null}

          {renderCurrentView()}
        </div>
      </main>

      <ImPickerModal
        open={!!imJumpEmployee}
        employee={imJumpEmployee}
        onClose={() => setImJumpEmployee(null)}
        onConfig={() => {
          const current = imJumpEmployee
          setImJumpEmployee(null)
          if (current) openImConfig(current.employeeId)
        }}
      />

      <button
        type="button"
        className="hb-feedback-strip"
        onClick={() => pushToast('原型反馈已记录，后续可以继续补齐没有后端接口的动作位', 'info')}
      >
        原型反馈
      </button>

      <div className="hb-toast-wrap">
        {toasts.map((toast) => (
          <div key={toast.id} className={`hb-toast ${toast.kind}`}>
            {toast.text}
          </div>
        ))}
      </div>
    </div>
  )
}
