import { useEffect, useMemo, useRef, useState } from 'react'
import {
  AlertCircle,
  BarChart2,
  Bot,
  Check,
  CheckCircle2,
  ChevronDown,
  Copy,
  ExternalLink,
  FileText,
  Loader2,
  MessageCircle,
  Rocket,
  SendHorizontal,
  Trash2,
  User,
  X,
  Zap,
  type LucideIcon,
} from 'lucide-react'
import { useTranslation } from 'react-i18next'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { tokenService } from '@/infra/auth/token-service'
import { GatewayWs } from '@/infra/sandbox/gateway-ws'
import { fetchSandboxSessionMessages, type SandboxMessage } from '@/infra/sandbox/sandbox-api'
import {
  api,
  type EmployeeDetail,
  type EvaluationSandboxConversationState,
  type EvaluationState,
  type EvaluationWorkspaceStatus,
  type HiringConversationMessage,
} from '@/infra/api'
import { API_BASE_URL } from '@/infra/api/httpClient'
import { Breadcrumb } from '@/shared/components/Breadcrumb'
import SessionListPanel from '@/features/team/components/SessionListPanel'
import { HiringToolStepsBlock } from '@/features/hiring/pages/components/HiringToolStepsBlock'
import type { ToolStep } from '@/features/hiring/pages/hiringPageTypes'
import { instanceBasePath } from '@/shared/utils/instancePath'

/** 评估页面本地消息类型（在 HiringConversationMessage 基础上增加工具调用步骤） */
type EvalChatMessage = HiringConversationMessage & { toolSteps?: ToolStep[] }

/** 执行轨迹 JSON 的事件条目 */
interface TraceLogEntry {
  type: string
  timestamp?: string
  timestamp_start?: string
  timestamp_end?: string
  text?: string
  state?: string        // for state_change
  name?: string         // for tool_use
  input?: unknown       // for tool_use
  content?: unknown     // for tool_result
}

interface TraceTurnSummary {
  total_messages?: number
  total_tool_calls?: number
  has_thought?: boolean
  think_count?: number
  execution_time_seconds?: number
  tool_calls_list?: string[]
}

interface TraceExecutionTrace {
  logs: TraceLogEntry[]
  assembled_assistant_text?: string
  think_blocks?: unknown[]
  summary?: TraceTurnSummary
}

/** 执行轨迹 JSON 的单轮结构 */
interface TraceTurn {
  turn_index: number
  test_case_id?: string
  user_input?: string
  execution_trace: TraceExecutionTrace
}

interface TraceMeta {
  total_turns?: number
  session_id?: string
  employee_name?: string
  iteration?: number
  collected_at?: string
  target_sandbox_id?: string
}

interface TraceProviderUsage {
  providerId?: string
  modelId?: string
  requests?: number
  inputTokens?: number
  outputTokens?: number
  cacheReadTokens?: number
}

/** 执行轨迹 JSON 文件的顶层结构 */
interface TraceJsonData {
  meta?: TraceMeta
  status?: string
  turns: TraceTurn[]
  http_supplement?: {
    dashboard?: {
      providers?: {
        usage?: TraceProviderUsage[]
      }
    }
  }
}

type ArtifactTab = 'overview' | 'testcase' | 'trace' | 'report'
type WorkflowStageStatus = 'pending' | 'running' | 'completed' | 'failed'
type WorkflowStage = {
  key: string
  title: string
  detail: string
  status: WorkflowStageStatus
  pendingLabel?: string
}

type EvaluationStarterAction = {
  key: string
  title: string
  description: string
  prompt: string
  icon: LucideIcon
}

const evaluationStarterActions: EvaluationStarterAction[] = [
  {
    key: 'explain-card',
    title: '解释当前题卡',
    description: '先说明本轮评估的目标、关键维度和判断重点。',
    prompt: '请先解释当前题卡的评估目标、关键维度、预期输出，以及我接下来应该重点关注什么。',
    icon: FileText,
  },
  {
    key: 'execution-plan',
    title: '给出执行计划',
    description: '把评估拆成清晰步骤，先看路径再正式开始。',
    prompt: '请基于当前题卡和已有素材，给出本轮评估的执行计划，按步骤列出每一步要验证什么。',
    icon: Zap,
  },
  {
    key: 'start-evaluation',
    title: '开始完整评估',
    description: '直接发起本轮评估，并持续同步结论和轨迹。',
    prompt: '评估材料已就绪，请开始执行 AI 评估，并持续同步关键结论、风险点和评分依据。',
    icon: Rocket,
  },
]

const evaluationSuggestionPrompts = [
  '对比上一轮评估，这次哪些维度有改善？',
  '把评分标准里的「合规性」拆成更细的指标。',
  '如果某个场景失败，我们应该优先追问什么？',
  '请先给出本轮评估最值得关注的风险点。',
]

function toAbsoluteApiUrl(path?: string | null): string | null {
  if (!path) return null
  const trimmed = path.trim()
  if (!trimmed) return null
  if (/^https?:\/\//i.test(trimmed)) return trimmed
  const normalized = trimmed.startsWith('/') ? trimmed : `/${trimmed}`
  // API_BASE_URL 为空串时表示相对路径部署（镜像内），直接返回相对路径即可
  if (!API_BASE_URL) return normalized
  return new URL(normalized, API_BASE_URL).toString()
}

function verdictLabel(verdict?: string | null) {
  if (verdict === 'passed') return '通过'
  if (verdict === 'failed') return '未通过'
  if (verdict === 'warning') return '待优化'
  return '待判定'
}

function formatDateTime(value?: string | null) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('zh-CN', {
    hour12: false,
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function shortSandboxId(value?: string | null) {
  if (!value) return '--'
  if (value.length <= 12) return value
  return `${value.slice(0, 6)}...${value.slice(-4)}`
}

function shortSessionId(value?: string | null) {
  if (!value) return '--'
  if (value.length <= 18) return value
  return `${value.slice(0, 8)}...${value.slice(-6)}`
}

function resolveStageStatus(stepStatus?: string | null): WorkflowStageStatus {
  if (stepStatus === 'completed') return 'completed'
  if (stepStatus === 'running') return 'running'
  if (stepStatus === 'failed') return 'failed'
  return 'pending'
}

function mergeStageStatus(statuses: Array<string | null | undefined>): WorkflowStageStatus {
  const normalized = statuses.map((item) => resolveStageStatus(item))
  if (normalized.includes('failed')) return 'failed'
  if (normalized.every((item) => item === 'completed')) return 'completed'
  if (normalized.some((item) => item === 'running' || item === 'completed')) return 'running'
  return 'pending'
}

function workflowStageTone(status: WorkflowStageStatus) {
  switch (status) {
    case 'completed': return 'eval-tone-completed'
    case 'running':   return 'eval-tone-running'
    case 'failed':    return 'eval-tone-failed'
    default:          return 'eval-tone-pending'
  }
}

function workflowStageStatusLabel(status: WorkflowStageStatus, pendingLabel = '等待中') {
  switch (status) {
    case 'completed':
      return '已完成'
    case 'running':
      return '进行中'
    case 'failed':
      return '失败'
    default:
      return pendingLabel
  }
}

function workflowStageTextTone(status: WorkflowStageStatus) {
  switch (status) {
    case 'completed':
      return 'eval-flow-step-text-completed'
    case 'running':
      return 'eval-flow-step-text-running'
    case 'failed':
      return 'eval-flow-step-text-failed'
    default:
      return 'eval-flow-step-text-pending'
  }
}

function findCurrentWorkflowStageIndex(stages: WorkflowStage[]) {
  const runningOrFailedIndex = stages.findIndex((stage) => stage.status === 'running' || stage.status === 'failed')
  if (runningOrFailedIndex >= 0) return runningOrFailedIndex

  const pendingIndex = stages.findIndex((stage) => stage.status === 'pending')
  if (pendingIndex >= 0) return pendingIndex

  return stages.length > 0 ? stages.length - 1 : -1
}

function renderWorkflowStageMarker(status: WorkflowStageStatus, order: number) {
  switch (status) {
    case 'completed':
      return <CheckCircle2 size={16} />
    case 'running':
      return <Loader2 size={15} className="animate-spin" />
    case 'failed':
      return <AlertCircle size={15} />
    default:
      return <span className="text-[11px] font-semibold">{String(order).padStart(2, '0')}</span>
  }
}

function logEvaluationDebug(label: string, payload?: unknown) {
  if (typeof console === 'undefined') return
  if (payload === undefined) {
    console.info(`[EvaluationPage] ${label}`)
    return
  }

  console.info(`[EvaluationPage] ${label}`, payload)
}

function mapSandboxMessages(messages: SandboxMessage[]): EvalChatMessage[] {
  return messages
    .filter((message) => message.type === 'user_message' || message.type === 'assistant_message')
    .map((message, index) => {
      const toolSteps: ToolStep[] | undefined = (message.toolCalls?.length ?? 0) > 0
        ? message.toolCalls!.map((tc, tcIdx) => ({
            id: `${index}-${tcIdx}-${tc.toolName}`,
            name: tc.toolName.startsWith('streaming.') ? tc.toolName.slice('streaming.'.length) : tc.toolName,
            args: tc.arguments,
            result: tc.result,
            status: 'done' as const,
          }))
        : undefined
      return {
        messageId: `${message.type}-${index}-${String(message.createdAt ?? Date.now())}`,
        role: message.type === 'user_message' ? 'user' : 'assistant',
        content: String(message.content ?? message.text ?? '').trim(),
        createdAt: String(message.createdAt ?? new Date().toISOString()),
        toolSteps,
      }
    })
    .filter((message) => message.content.length > 0 || (message.toolSteps?.length ?? 0) > 0)
}

export default function EvaluationPage() {
  const { id } = useParams<{ id: string }>()
  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [evaluation, setEvaluation] = useState<EvaluationState | null>(null)
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  const [rightCollapsed, setRightCollapsed] = useState(false)
  const [artifactTab, setArtifactTab] = useState<ArtifactTab>('overview')
  const [workspaceStatus, setWorkspaceStatus] = useState<EvaluationWorkspaceStatus | null>(null)
  const [workspacePolling, setWorkspacePolling] = useState(false)
  const [expandedQuestionCardIds, setExpandedQuestionCardIds] = useState<string[]>([])
  const [traceDataCache, setTraceDataCache] = useState<Record<string, TraceJsonData | 'loading' | 'error'>>({})
  const [expandedTraceUrls, setExpandedTraceUrls] = useState<string[]>([])

  const location = useLocation()
  const navigate = useNavigate()

  const [chatMessages, setChatMessages] = useState<EvalChatMessage[]>([])
  const [chatInput, setChatInput] = useState('')
  const [chatLoading, setChatLoading] = useState(false)
  const [chatSending, setChatSending] = useState(false)
  const [chatError, setChatError] = useState('')
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null)
  const [sessionListRefreshKey, setSessionListRefreshKey] = useState(0)
  const [sandboxConnected, setSandboxConnected] = useState(false)
  const [sessionSwitching, setSessionSwitching] = useState(false)
  const [streamingContent, setStreamingContent] = useState<string | null>(null)
  const [streamingToolSteps, setStreamingToolSteps] = useState<ToolStep[]>([])
  const [chatTyping, setChatTyping] = useState(false)
  const [, setSandboxConversation] = useState<EvaluationSandboxConversationState | null>(null)
  const wsEvaluating = false
  const wsProgress = ''
  const [resetting, setResetting] = useState(false)
  const [resetConfirm, setResetConfirm] = useState(false)
  const [sessionCopied, setSessionCopied] = useState(false)
  const chatEndRef = useRef<HTMLDivElement | null>(null)
  const chatInputRef = useRef<HTMLTextAreaElement | null>(null)
  const wsRef = useRef<GatewayWs | null>(null)
  const gatewayEndpointRef = useRef<string | null>(null)
  const sessionIdRef = useRef<string | null>(null)
  const streamingContentRef = useRef('')
  const streamingToolStepsRef = useRef<ToolStep[]>([])
  const connectionStateRef = useRef<{ endpoint: string | null; sessionId: string | null }>({
    endpoint: null,
    sessionId: null,
  })
  const ensureChatReadyPromiseRef = useRef<Promise<boolean> | null>(null)

  // ── 自动初始化状态 ──
  const [wsStatusLoaded, setWsStatusLoaded] = useState(false)
  const [autoInitVisible, setAutoInitVisible] = useState(false)
  const [autoInitCountdown, setAutoInitCountdown] = useState(3)
  const autoInitFiredRef = useRef(false)
  // 仅在当前会话内有效：点击「执行评估」后置为 true，刷新页面自动重置为 false
  const [hasTriggeredEval, setHasTriggeredEval] = useState(false)

  async function loadData() {
    if (!id) return
    setLoading(true)
    setError('')
    try {
      const [employeeData, evaluationData] = await Promise.all([
        api.employeeRuntime.getEmployee(id),
        api.employeeRuntime.getEvaluationState(id),
      ])
      setEmployee(employeeData)
      setEvaluation(evaluationData)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '加载 AI 评估数据失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData()
  }, [id])

  const isPrivateBranchEvaluation = employee?.instanceType === 'private_branch'
  // 私有分支评估是特殊流程：实例保持 live 状态，不进入普通雇佣评估的 interning_ai 状态。
  // 这里仅允许 private_branch + live 放行，避免影响普通员工原有评估链路。
  const canPrepare =
    employee?.status === 'hired' ||
    employee?.status === 'failed' ||
    employee?.status === 'interning_ai' ||
    (isPrivateBranchEvaluation && employee?.status === 'live')
  const isAiStage =
    employee?.status === 'interning_ai' ||
    (isPrivateBranchEvaluation && employee?.status === 'live' && employee?.evalPhase === 'ai_running')
  const aiRunning = isAiStage && employee?.evalPhase === 'ai_running'

  const workspaceReady = workspaceStatus?.overallStatus === 'ready'
  // 沙箱正在初始化时（轮询中或重置中）显示全屏过渡遮罩，防止用户看到未就绪的页面
  const showSandboxInitOverlay = workspacePolling || resetting

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [chatLoading, chatMessages, streamingContent])

  const questionCards = evaluation?.questionCards ?? []
  const testcaseOutlines = evaluation?.testcaseOutlines ?? []
  const questionCardMap = useMemo(
    () => new Map(questionCards.map((card) => [card.testcaseId, card])),
    [questionCards],
  )
  const testcaseItems = useMemo(
    () => testcaseOutlines.length > 0
      ? testcaseOutlines
      : questionCards.map((card) => ({
          testcaseId: card.testcaseId,
          title: card.title || card.testcaseId,
          userRequest: '',
        })),
    [questionCards, testcaseOutlines],
  )
  const traceAssets = (evaluation?.assetRefs ?? [])
    .filter((asset) => asset.assetType === 'trace-json')
    .slice(0, 8)
  const materialsReady = evaluation?.readiness?.status === 'ready'
  const reportSummary = evaluation?.latestReport ?? null
  const reportJsonUrl = toAbsoluteApiUrl(reportSummary?.reportJsonUrl ?? null)
  const reportHtmlUrl = toAbsoluteApiUrl(reportSummary?.reportHtmlUrl ?? null)
  const dimensionScores = reportSummary?.dimensionScores ?? []

  // 只要已有评估结果，或已进入人工复核/待上岗阶段，就允许进入人工评估页。
  // AI 结果只负责展示，不替用户做最终业务决策。
  const canNavigateToHumanEval =
    reportSummary != null ||
    employee?.evalPhase === 'pending_human_review' ||
    employee?.evalPhase === 'pending_onboarding' ||
    employee?.evalPhase === 'pending_onboarding_force'
  const humanEvalPath = id ? `${instanceBasePath(location.pathname, id)}/human-evaluation` : null
  const [enteringHumanEval, setEnteringHumanEval] = useState(false)
  const [showHumanEvalConfirm, setShowHumanEvalConfirm] = useState(false)
  const { t } = useTranslation()
  const hasChatTimelineContent =
    chatLoading ||
    chatMessages.length > 0 ||
    streamingContent !== null ||
    chatTyping

  function handleEnterHumanEval() {
    setShowHumanEvalConfirm(true)
  }

  async function confirmEnterHumanEval() {
    if (!id || !humanEvalPath) return
    setEnteringHumanEval(true)
    setShowHumanEvalConfirm(false)
    try {
      // 确认进入人工评估时把状态从 AI 评估改为人工评估
      await api.employeeRuntime.updateLifecycle(id, {
        status: 'interning_human',
        stageSummary: '人工评估进行中',
        primarySignal: '等待人工验证',
        signalLevel: 'ok',
      })
    } catch {
      // 状态转换失败不阻断导航，人工评估页面会二次检查并重试
    } finally {
      setEnteringHumanEval(false)
      navigate(humanEvalPath)
    }
  }
  const humanEvalBannerTone = reportSummary?.passed === false ? 'eval-banner-fail' : 'eval-banner-pass'
  const humanEvalBannerTextTone = 'eval-banner-text'
  const humanEvalBannerTitle = reportSummary == null
    ? '已进入人工评估阶段'
    : reportSummary.passed
      ? 'AI 评估已通过'
      : 'AI 评估未通过'
  const humanEvalBannerDescription = reportSummary?.overallScore != null
    ? `（综合评分 ${reportSummary.overallScore} 分），请进入人工评估决定后续流程`
    : '，请进入人工评估决定后续流程'

  function handleCopySessionId() {
    if (!workspaceStatus?.sessionId) return
    void navigator.clipboard.writeText(workspaceStatus.sessionId).then(() => {
      setSessionCopied(true)
      window.setTimeout(() => setSessionCopied(false), 1800)
    })
  }

  const workflowStages = useMemo<WorkflowStage[]>(() => {
    const stepMap = new Map((workspaceStatus?.steps ?? []).map((step) => [step.step, step.status]))
    // 保持原有四步流程语义：材料阶段以测试用例就绪作为完成标志
    const materialsStageStatus: WorkflowStageStatus = testcaseOutlines.length > 0
      ? 'completed'
      : mergeStageStatus([
          stepMap.get('upload_skill'),
          stepMap.get('upload_employee_template'),
          stepMap.get('upload_artifacts'),
          materialsReady ? 'completed' : stepMap.get('materials'),
        ])
    // 执行阶段：只有本次会话内点击「执行评估」才进入 running，刷新前未点击视为 pending；
    // 人工评估按钮出现（canNavigateToHumanEval）才算 completed
    const executionStatus: WorkflowStageStatus = canNavigateToHumanEval
      ? 'completed'
      : hasTriggeredEval
        ? 'running'
        : 'pending'

    return [
      {
        key: 'target',
        title: '创建目标沙箱',
        detail: workspaceStatus?.targetSandboxId ? `目标沙箱已创建：${shortSandboxId(workspaceStatus.targetSandboxId)}` : '创建被评估模板沙箱并拿到 gatewayEndpoint',
        status: resolveStageStatus(stepMap.get('target_sandbox')),
      },
      {
        key: 'evaluator',
        title: '创建评估沙箱',
        detail: workspaceStatus?.evaluatorSandboxId ? `评估沙箱已创建：${shortSandboxId(workspaceStatus.evaluatorSandboxId)}` : '创建最终与用户交互的评估沙箱',
        status: resolveStageStatus(stepMap.get('evaluator_sandbox')),
      },
      {
        key: 'materials',
        title: '装载模板与材料',
        detail: testcaseOutlines.length > 0
          ? `测试用例已就绪（${testcaseOutlines.length} 个场景）`
          : materialsReady
            ? '模板材料已进入评估沙箱，等待测试用例生成'
            : '上传评估技能包、目标模板和评估材料',
        status: materialsStageStatus,
      },
      {
        key: 'execution',
        title: '执行评分与报告',
        detail: reportSummary
          ? `已完成第 ${reportSummary.iteration} 轮评估，综合 ${reportSummary.overallScore} 分`
          : hasTriggeredEval
            ? '正在驱动评估并汇总报告'
            : '点击主按钮开始正式评估',
        status: executionStatus,
        pendingLabel: '未开始',
      },
    ]
  }, [
    canNavigateToHumanEval,
    hasTriggeredEval,
    materialsReady,
    reportSummary,
    testcaseOutlines.length,
    workspaceStatus,
  ])

  const currentWorkflowStageIndex = useMemo(
    () => findCurrentWorkflowStageIndex(workflowStages),
    [workflowStages],
  )

  const environmentStatus = useMemo(() => {
    if (workspaceReady) {
      return {
        label: '双沙箱已连接',
        dotClassName: 'eval-flow-status-dot eval-flow-status-dot-ready',
      }
    }
    if (workspaceStatus?.overallStatus === 'failed') {
      return {
        label: '双沙箱准备失败',
        dotClassName: 'eval-flow-status-dot eval-flow-status-dot-failed',
      }
    }
    if (workspaceStatus && workspaceStatus.overallStatus !== 'not_started') {
      return {
        label: '双沙箱准备中',
        dotClassName: 'eval-flow-status-dot eval-flow-status-dot-running',
      }
    }
    return {
      label: '尚未准备环境',
      dotClassName: 'eval-flow-status-dot eval-flow-status-dot-pending',
    }
  }, [workspaceReady, workspaceStatus])

  const primaryActionLabel = wsEvaluating
    ? (wsProgress || '执行中...')
    : hasTriggeredEval
      ? '重新执行评估'
      : '执行评估'

    const reportMetrics = useMemo(() => ([
    {
      label: '会话 ID',
      value: shortSessionId(workspaceStatus?.sessionId ?? reportSummary?.reportId ?? null),
      tone: 'eval-text-indigo',
    },
    {
      label: '题卡数量',
      value: `${questionCards.length}`,
      tone: 'eval-text-teal',
    },
    {
      label: 'Trace 产物',
      value: `${traceAssets.length}`,
      tone: 'eval-text-amber',
    },
    {
      label: '材料状态',
      value: materialsReady ? '已就绪' : '待补齐',
      tone: materialsReady ? 'eval-text-green-bright' : 'eval-text-red',
    },
  ]), [materialsReady, questionCards.length, reportSummary?.reportId, traceAssets.length, workspaceStatus?.sessionId])

  const workspaceProgressSummary = useMemo(() => {
    if (!workspaceStatus || workspaceStatus.overallStatus === 'not_started') {
      return null
    }

    const steps = workspaceStatus.steps ?? []
    const total = Math.max(steps.length, 1)
    const completed = steps.filter((step) => step.status === 'completed').length
    const runningStep = steps.find((step) => step.status === 'running')
    const failedStep = steps.find((step) => step.status === 'failed')
    const percent = workspaceStatus.overallStatus === 'ready'
      ? 100
      : workspaceStatus.overallStatus === 'failed'
        ? 100
        : Math.max(Math.round((completed / total) * 100), 8)

    const label = workspaceStatus.overallStatus === 'ready'
      ? '评估环境已就绪'
      : workspaceStatus.overallStatus === 'failed'
        ? '评估环境创建失败'
        : runningStep
          ? `进行中：${runningStep.detail || runningStep.step}`
          : '正在创建评估环境'

    return {
      percent,
      label,
      completed,
      total,
      failed: workspaceStatus.overallStatus === 'failed',
      errorMessage: failedStep?.detail || workspaceStatus.errorMessage || '',
    }
  }, [workspaceStatus])

  useEffect(() => {
    if (!workspaceStatus) return
    logEvaluationDebug('workspace status updated', {
      employeeId: id,
      overallStatus: workspaceStatus.overallStatus,
      sessionId: workspaceStatus.sessionId,
      targetSandboxId: workspaceStatus.targetSandboxId,
      targetGatewayEndpoint: workspaceStatus.targetGatewayEndpoint,
      evaluatorSandboxId: workspaceStatus.evaluatorSandboxId,
      evaluatorGatewayEndpoint: workspaceStatus.evaluatorGatewayEndpoint,
    })
  }, [id, workspaceStatus])

  useEffect(() => {
    if (!reportSummary) return
    logEvaluationDebug('latest report updated', {
      employeeId: id,
      reportId: reportSummary.reportId,
      iteration: reportSummary.iteration,
      overallScore: reportSummary.overallScore,
      passed: reportSummary.passed,
    })
  }, [id, reportSummary])

  useEffect(() => {
    if (!id) return

    let cancelled = false
    setWsStatusLoaded(false)
    async function loadWorkspaceStatusSnapshot() {
      try {
        const status = await api.employeeRuntime.getEvaluationWorkspaceStatus(id!)
        if (cancelled) return
        setWorkspaceStatus(status)
        const shouldPoll =
          status.overallStatus !== 'not_started' &&
          status.overallStatus !== 'ready' &&
          status.overallStatus !== 'failed'
        setWorkspacePolling(shouldPoll)
      } catch {
        if (!cancelled) {
          setWorkspaceStatus(null)
          setWorkspacePolling(false)
        }
      } finally {
        if (!cancelled) setWsStatusLoaded(true)
      }
    }

    void loadWorkspaceStatusSnapshot()
    return () => {
      cancelled = true
    }
  }, [id])

  useEffect(() => {
    if (!workspacePolling || !id) return

    let cancelled = false
    let timer: number

    async function poll() {
      if (cancelled) return
      try {
        const status = await api.employeeRuntime.getEvaluationWorkspaceStatus(id!)
        if (cancelled) return
        setWorkspaceStatus(status)
        if (status.overallStatus === 'ready' || status.overallStatus === 'failed') {
          setWorkspacePolling(false)
          return
        }
      } catch {
        // ignore polling errors
      }
      if (!cancelled) {
        timer = window.setTimeout(poll, 3000)
      }
    }

    poll()
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [workspacePolling, id])

  // 两端数据均加载完成后，判断是否需要自动初始化
  useEffect(() => {
    if (loading || !wsStatusLoaded) return
    if (!employee || !canPrepare || aiRunning) return
    if (autoInitFiredRef.current) return
    // 已有进行中 / 就绪 / 失败的工作区 → 无需自动初始化
    if (workspaceStatus && workspaceStatus.overallStatus !== 'not_started') return

    autoInitFiredRef.current = true
    setAutoInitCountdown(3)
    setAutoInitVisible(true)
  }, [loading, wsStatusLoaded, employee, canPrepare, aiRunning, workspaceStatus])

  // 切换到 trace tab 时自动展开并加载第一条轨迹
  useEffect(() => {
    if (artifactTab !== 'trace') return
    const sessionId = evaluation?.sessionId ?? null
    if (!sessionId) return
    if (!expandedTraceUrls.includes(sessionId)) {
      setExpandedTraceUrls(prev => [...prev, sessionId])
      void loadTraceContent(sessionId)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [artifactTab, evaluation?.sessionId])

  // 倒计时：每秒 -1，到 0 时自动触发初始化
  useEffect(() => {
    if (!autoInitVisible) return
    if (autoInitCountdown <= 0) {
      setAutoInitVisible(false)
      void submitAiDecision('START')
      return
    }
    const timer = window.setTimeout(() => {
      setAutoInitCountdown((c) => c - 1)
    }, 1000)
    return () => window.clearTimeout(timer)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoInitVisible, autoInitCountdown])

  function handleAutoInitNow() {
    setAutoInitVisible(false)
    void submitAiDecision('START')
  }

  function handleAutoInitCancel() {
    setAutoInitVisible(false)
  }

  async function syncSandboxHistory(endpoint: string, sessionId: string) {
    const sandboxMessages = await fetchSandboxSessionMessages(endpoint, sessionId)
    const mapped = mapSandboxMessages(sandboxMessages)
    setChatMessages(mapped)
  }

  async function connectEvaluationWs(endpoint: string) {
    if (
      wsRef.current?.isOpen() &&
      connectionStateRef.current.endpoint === endpoint &&
      connectionStateRef.current.sessionId === sessionIdRef.current
    ) {
      setSandboxConnected(true)
      return
    }

    wsRef.current?.disconnect()
    wsRef.current = null
    setSandboxConnected(false)

    const token = await tokenService.ensureFresh()
    if (!token) {
      throw new Error('评估沙箱鉴权失败，无法建立 WebSocket 连接')
    }

    const ws = new GatewayWs(endpoint, token)
    let settled = false
    let timeoutId: number | null = null

    const waitForOpen = new Promise<void>((resolve, reject) => {
      timeoutId = window.setTimeout(() => {
        if (settled) return
        settled = true
        reject(new Error('评估沙箱连接超时，请稍后重试'))
      }, 8000)

      ws.onStateChange = (state) => {
        setSandboxConnected(state === 'open')
        if (state === 'open' && !settled) {
          settled = true
          if (timeoutId !== null) {
            window.clearTimeout(timeoutId)
          }
          resolve()
        }
        if ((state === 'closed' || state === 'error') && !settled) {
          settled = true
          if (timeoutId !== null) {
            window.clearTimeout(timeoutId)
          }
          reject(new Error('评估沙箱连接未建立，无法发送消息'))
        }
      }
    })

    ws.onMessage = (msg) => {
      const messageType = String(msg.type ?? '')

      if (messageType === 'typing_start') {
        // 新轮次开始：重置流式内容和工具步骤
        streamingContentRef.current = ''
        streamingToolStepsRef.current = []
        setStreamingContent('')
        setStreamingToolSteps([])
        setChatTyping(true)
        return
      }

      if (messageType === 'text_delta' || messageType === 'assistant_chunk') {
        const chunk = String(msg.delta ?? msg.chunk ?? msg.content ?? msg.text ?? '')
        streamingContentRef.current += chunk
        setStreamingContent(streamingContentRef.current)
        setChatTyping(false)
        return
      }

      // 工具调用开始：添加 running 状态条目
      // tool_start: evaluator sandbox 格式，工具名在 msg.text
      // tool_use_start / tool_call_start: Anthropic 格式，工具名在 msg.name
      if (messageType === 'tool_start' || messageType === 'tool_use_start' || messageType === 'tool_call_start') {
        const rawName = messageType === 'tool_start'
          ? String((msg as unknown as Record<string, unknown>).text ?? '')
          : String(msg.name ?? msg.tool_name ?? msg.tool ?? 'tool')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        const toolId = String(msg.id ?? msg.tool_use_id ?? `tool-${Date.now()}`)
        const rawArgs = (msg as unknown as Record<string, unknown>).arguments
        const newStep: ToolStep = {
          id: toolId,
          name: toolName || 'tool',
          args: rawArgs != null
            ? (typeof rawArgs === 'string' ? rawArgs : JSON.stringify(rawArgs))
            : msg.input ? JSON.stringify(msg.input) : undefined,
          status: 'running',
        }
        streamingToolStepsRef.current = [...streamingToolStepsRef.current, newStep]
        setStreamingToolSteps([...streamingToolStepsRef.current])
        return
      }

      // 工具调用完成：优先按名称匹配最后一个 running 步骤，回退到最后一个 running
      if (messageType === 'tool_result' || messageType === 'tool_call_result') {
        const rawMsg = msg as unknown as Record<string, unknown>
        const rawName = String(rawMsg.tool_name ?? rawMsg.name ?? '')
        const toolName = rawName.startsWith('streaming.') ? rawName.slice('streaming.'.length) : rawName
        const result = msg.content
          ? typeof msg.content === 'string' ? msg.content : JSON.stringify(msg.content)
          : String(rawMsg.text ?? rawMsg.result ?? '')
        const isError = Boolean(rawMsg.is_error ?? rawMsg.isError)
        const list = streamingToolStepsRef.current
        let targetIdx = -1
        if (toolName) {
          for (let i = list.length - 1; i >= 0; i--) {
            if (list[i].status === 'running' && list[i].name === toolName) { targetIdx = i; break }
          }
        }
        if (targetIdx < 0) {
          for (let i = list.length - 1; i >= 0; i--) {
            if (list[i].status === 'running') { targetIdx = i; break }
          }
        }
        if (targetIdx >= 0) {
          const next = list.slice()
          next[targetIdx] = { ...next[targetIdx], status: isError ? 'error' : 'done', result }
          streamingToolStepsRef.current = next
          setStreamingToolSteps([...next])
        }
        return
      }

      if (messageType === 'typing_stop' || messageType === 'assistant_done') {
        const endpointValue = gatewayEndpointRef.current
        const sessionIdValue = sessionIdRef.current
        const completedToolSteps = [...streamingToolStepsRef.current]
        setStreamingContent(null)
        setStreamingToolSteps([])
        setChatTyping(false)
        streamingContentRef.current = ''
        streamingToolStepsRef.current = []
        if (endpointValue && sessionIdValue) {
          void syncSandboxHistory(endpointValue, sessionIdValue)
            .then(async () => {
              // 把本轮工具步骤附加到最后一条 bot 消息
              if (completedToolSteps.length > 0) {
                setChatMessages((prev) => {
                  const lastBotIdx = [...prev].reverse().findIndex((m) => m.role !== 'user')
                  if (lastBotIdx === -1) return prev
                  const idx = prev.length - 1 - lastBotIdx
                  const updated = [...prev]
                  updated[idx] = { ...updated[idx], toolSteps: completedToolSteps }
                  return updated
                })
              }
              setSessionListRefreshKey((current) => current + 1)
              // 每轮对话结束后刷新评估状态，检查是否有新报告产出
              if (id) {
                try {
                  const [evalState, employeeState] = await Promise.all([
                    api.employeeRuntime.getEvaluationState(id),
                    api.employeeRuntime.getEmployee(id),
                  ])
                  setEvaluation(evalState)
                  setEmployee(employeeState)
                } catch {
                  // 刷新失败不影响主流程
                }
              }
            })
            .catch((historyError: unknown) => {
              setChatError(historyError instanceof Error ? historyError.message : '同步评估沙箱历史失败')
            })
        }
        return
      }

      if (messageType === 'error') {
        setChatError(String(msg.text ?? msg.content ?? '评估沙箱返回错误'))
      }
    }

    ws.onReconnected = () => {
      const endpointValue = gatewayEndpointRef.current
      const sessionIdValue = sessionIdRef.current
      if (endpointValue && sessionIdValue) {
        void syncSandboxHistory(endpointValue, sessionIdValue).catch(() => {
          // ignore re-sync failures after reconnect
        })
      }
    }

    ws.connect()
    wsRef.current = ws
    await waitForOpen
    connectionStateRef.current = {
      endpoint,
      sessionId: sessionIdRef.current,
    }
  }

  async function ensureEvaluationChatReady() {
    if (!id || !aiRunning) return

    if (ensureChatReadyPromiseRef.current) {
      return ensureChatReadyPromiseRef.current
    }

    ensureChatReadyPromiseRef.current = (async () => {
      setChatLoading(true)
      setChatError('')

      try {
        // 调用 sandbox-connection：触发后端 session 创建、materials 上传、evaluation-context.json 上传，
        // 并拿到 evaluator 沙箱的 gateway endpoint 和 eval session id。
        const connection = await api.employeeRuntime.getSandboxConnection(id)
        const endpoint = connection.gatewayEndpoint.trim()
        // 把 eval session id 写回 workspaceStatus（右侧调试面板 Session 字段显示用）
        setWorkspaceStatus((prev) => (prev ? { ...prev, sessionId: connection.sessionId } : prev))

        // WS 会话 id 由沙箱侧分配，与 eval session id 不同；优先复用已有值，否则向沙箱查询
        let sessionId = sessionIdRef.current ?? ''
        if (!sessionId) {
          const conversation = await api.employeeRuntime.getEvaluationSandboxConversation(id)
          setSandboxConversation(conversation)
          sessionId = conversation.sessionId?.trim() ?? ''
        }

        if (!endpoint || !sessionId) {
          throw new Error('评估会话尚未绑定完成，无法恢复沙箱会话')
        }

        gatewayEndpointRef.current = endpoint
        sessionIdRef.current = sessionId
        setSelectedSessionId(sessionId)

        const alreadyReady =
          wsRef.current?.isOpen() &&
          connectionStateRef.current.endpoint === endpoint &&
          connectionStateRef.current.sessionId === sessionId

        if (!alreadyReady) {
          await syncSandboxHistory(endpoint, sessionId)
          await connectEvaluationWs(endpoint)
        }

        logEvaluationDebug('evaluation chat ready', { employeeId: id, endpoint, sessionId, reused: alreadyReady })
        return true
      } catch (readyError: unknown) {
        setChatError(readyError instanceof Error ? readyError.message : '初始化评估聊天失败')
        return false
      } finally {
        setChatLoading(false)
        ensureChatReadyPromiseRef.current = null
      }
    })()

    return ensureChatReadyPromiseRef.current
  }

  async function handleResetEvaluationData() {
    if (!id) return
    if (!resetConfirm) {
      setResetConfirm(true)
      return
    }
    setResetConfirm(false)
    setResetting(true)
    setError('')
    let resetOk = false
    try {
      await api.employeeRuntime.resetEvaluationData(id)
      setWorkspaceStatus(null)
      setWorkspacePolling(false)
      setChatMessages([])
      setEvaluation(null)
      setWsStatusLoaded(false)
      setHasTriggeredEval(false)
      // 阻止 auto-init effect 弹出倒计时遮罩，由下方直接调用 submitAiDecision 接管
      autoInitFiredRef.current = true
      await loadData()  // 刷新 employee 状态，确保 submitAiDecision 基于最新数据
      resetOk = true
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '清理评估数据失败')
    } finally {
      setResetting(false)
    }
    // 清理成功后直接进入带进度条的初始化流程，闭环整个流程
    if (resetOk) {
      await submitAiDecision('START')
    }
  }

  async function submitAiDecision(decision: 'START' | 'RUN') {
    if (!id) return
    setSubmitting(true)
    setError('')
    logEvaluationDebug('submit ai decision', { employeeId: id, decision })

    try {
      // RUN: 沙箱环境已在 START 阶段就绪，直接通过聊天 WS 发送评估触发消息，不再重复调用后端接口
      if (decision === 'RUN') {
        setHasTriggeredEval(true)
        setSubmitting(false)
        void sendEvaluatorMessage('评估材料已就绪，请开始执行 AI 评估。')
        return
      }

      // START: prepare environment (sandboxes + skill + materials)
      setWorkspaceStatus(null)
      setWorkspacePolling(true)
      const updated = await api.employeeRuntime.submitAiEvaluationDecision(id, { decision })
      setEmployee(updated)

      const latestWorkspaceStatus = await api.employeeRuntime.getEvaluationWorkspaceStatus(id)
      setWorkspaceStatus(latestWorkspaceStatus)
      if (latestWorkspaceStatus.overallStatus === 'ready' || latestWorkspaceStatus.overallStatus === 'failed') {
        setWorkspacePolling(false)
      }

      const evaluationState = await api.employeeRuntime.getEvaluationState(id)
      setEvaluation(evaluationState)

      if (decision === 'START') {
        await ensureEvaluationChatReady()
      }
    } catch (requestError: unknown) {
      if (decision === 'START') {
        setWorkspacePolling(false)
      }
      setError(requestError instanceof Error ? requestError.message : '提交 AI 评估动作失败')
    } finally {
      if (decision !== 'RUN') setSubmitting(false)
    }
  }

  async function sendEvaluatorMessage(overrideContent?: string) {
    if (!id || chatSending) return
    const content = (overrideContent !== undefined ? overrideContent : chatInput).trim()
    if (!content) return

    const optimistic: HiringConversationMessage = {
      messageId: `local_${Date.now()}`,
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    }

    if (overrideContent === undefined) {
      setChatInput('')
    }
    setChatSending(true)
    setChatError('')
    setChatMessages((prev) => [...prev, optimistic])
    logEvaluationDebug('send evaluator message', {
      employeeId: id,
      contentPreview: content.slice(0, 120),
    })

    try {
      if (!wsRef.current?.isOpen() || !sessionIdRef.current) {
        const ready = await ensureEvaluationChatReady()
        if (!ready) {
          throw new Error(chatError || '评估沙箱连接尚未就绪，请稍后重试')
        }
      }

      const activeWs = wsRef.current
      const activeSessionId = sessionIdRef.current
      if (!activeWs || !activeWs.isOpen() || !activeSessionId) {
        throw new Error('评估沙箱连接未建立，无法发送消息')
      }

      setStreamingContent('')
      streamingContentRef.current = ''

      const sent = activeWs.send({
        type: 'user_message',
        text: content,
        sessionId: activeSessionId,
        messageId: `eval-chat-${Date.now()}`,
      })

      if (!sent) {
        throw new Error('评估沙箱连接尚未就绪，请稍后重试')
      }

      setSessionListRefreshKey((current) => current + 1)
    } catch (sendError: unknown) {
      setChatError(sendError instanceof Error ? sendError.message : '发送消息到评估沙箱失败')
      setChatMessages((prev) => prev.filter((item) => item.messageId !== optimistic.messageId))
    } finally {
      setChatSending(false)
    }
  }

  function focusChatInput() {
    window.requestAnimationFrame(() => {
      chatInputRef.current?.focus()
    })
  }

  function handleStarterAction(prompt: string) {
    void sendEvaluatorMessage(prompt)
  }

  function handleSuggestionSelect(prompt: string) {
    setChatInput(prompt)
    focusChatInput()
  }

  async function loadTraceContent(sessionId: string) {
    if (traceDataCache[sessionId]) return
    if (!id) return
    setTraceDataCache(prev => ({ ...prev, [sessionId]: 'loading' }))
    try {
      const resp = await api.employeeRuntime.getTraceContent(id, sessionId)
      const parsed = JSON.parse(resp.traceJsonContent) as TraceJsonData
      setTraceDataCache(prev => ({ ...prev, [sessionId]: parsed }))
    } catch {
      setTraceDataCache(prev => ({ ...prev, [sessionId]: 'error' }))
    }
  }

  function toggleTraceExpand(sessionId: string) {
    const isExpanding = !expandedTraceUrls.includes(sessionId)
    setExpandedTraceUrls(prev =>
      isExpanding ? [...prev, sessionId] : prev.filter(k => k !== sessionId),
    )
    if (isExpanding) void loadTraceContent(sessionId)
  }

  function toggleQuestionCardDetails(testcaseId: string) {
    setExpandedQuestionCardIds((current) => (
      current.includes(testcaseId)
        ? current.filter((item) => item !== testcaseId)
        : [...current, testcaseId]
    ))
  }

  function handleRunSingleScenario(testcaseId: string, title: string) {
    void sendEvaluatorMessage(`请仅执行测试场景「${title}」（${testcaseId}），并说明执行结果、评分依据和结论。`)
  }

  useEffect(() => {
    if (!aiRunning || !id) {
      setChatMessages([])
      setChatError('')
      setSelectedSessionId(null)
      setStreamingContent(null)
      setStreamingToolSteps([])
      setChatTyping(false)
      gatewayEndpointRef.current = null
      sessionIdRef.current = null
      connectionStateRef.current = { endpoint: null, sessionId: null }
      ensureChatReadyPromiseRef.current = null
      wsRef.current?.disconnect()
      wsRef.current = null
      setSandboxConnected(false)
      return
    }

    void ensureEvaluationChatReady()

    return () => {
      wsRef.current?.disconnect()
      wsRef.current = null
      connectionStateRef.current = { endpoint: null, sessionId: null }
    }
  }, [aiRunning, id])

  async function handleSelectSession(sessionId: string) {
    if (sessionSwitching || sessionId === selectedSessionId) return

    const endpoint = gatewayEndpointRef.current
    if (!endpoint) {
      setChatError('评估沙箱连接地址缺失，无法切换会话')
      return
    }

    const previousSessionId = sessionIdRef.current
    setSessionSwitching(true)
    setSelectedSessionId(sessionId)
    setStreamingContent(null)
    setStreamingToolSteps([])
    setChatTyping(false)

    try {
      await syncSandboxHistory(endpoint, sessionId)
      sessionIdRef.current = sessionId
      await connectEvaluationWs(endpoint)
    } catch (sessionError: unknown) {
      setSelectedSessionId(previousSessionId)
      sessionIdRef.current = previousSessionId
      setChatError(sessionError instanceof Error ? sessionError.message : '切换评估会话失败')
      if (previousSessionId) {
        await syncSandboxHistory(endpoint, previousSessionId)
      }
    } finally {
      setSessionSwitching(false)
    }
  }

  async function handleNewChat() {
    if (!id) return
    const newSessionId = `evaluation:${id}:chat-${Date.now()}`
    sessionIdRef.current = newSessionId
    setSelectedSessionId(newSessionId)
    setChatMessages([])
    setStreamingContent(null)
    setStreamingToolSteps([])
    setChatTyping(false)
    setSessionListRefreshKey((current) => current + 1)
    const endpoint = gatewayEndpointRef.current
    if (endpoint) {
      await connectEvaluationWs(endpoint)
    }
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-[220px] items-center justify-center gap-2 p-8 text-sm text-[var(--hb-soft)]">
          <Loader2 size={16} className="animate-spin" />
          正在加载 AI 评估...
        </div>
      </div>
    )
  }

  if (!employee || !evaluation) {
    return (
      <div className="hb-page">
        <div className="hb-card p-8 text-sm text-[var(--hb-soft)]">评估数据不存在</div>
      </div>
    )
  }

  return (
    <div className="hb-page">
      <Breadcrumb items={[{ label: '员工详情', to: id ? instanceBasePath(location.pathname, id) : '/department-employees' }, { label: 'AI 评估' }]} />

      {/* 自动初始化过渡屏 */}
      {autoInitVisible && (
        <div className="flex h-[calc(100vh-116px)] min-h-[680px] items-center justify-center">
          <div className="w-full max-w-[360px] rounded-3xl border eval-chat-wrapper p-8 text-center shadow-xl">
            {/* Icon */}
            <div className="mb-5 flex justify-center">
              <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-[var(--hb-blue)]/10">
                <Rocket size={26} className="text-[var(--hb-blue)]" />
              </div>
            </div>

            {/* Title & description */}
            <h2 className="mb-1 text-[17px] font-semibold eval-text-title">
              {t('evaluationPage.autoInit.title')}
            </h2>
            <p className="mb-5 text-[13px] leading-relaxed eval-text-secondary">
              {t('evaluationPage.autoInit.desc', { name: employee.nickname, role: employee.roleName })}
            </p>

            {/* Countdown ring */}
            <div className="relative mx-auto mb-5 h-[88px] w-[88px]">
              <svg className="h-[88px] w-[88px] -rotate-90" viewBox="0 0 100 100">
                {/* Track */}
                <circle cx="50" cy="50" r="40" fill="none" stroke="var(--hb-border)" strokeWidth="6" />
                {/* Progress arc */}
                <circle
                  cx="50" cy="50" r="40"
                  fill="none"
                  stroke="var(--hb-blue)"
                  strokeWidth="6"
                  strokeLinecap="round"
                  strokeDasharray={`${2 * Math.PI * 40}`}
                  strokeDashoffset={`${2 * Math.PI * 40 * (1 - autoInitCountdown / 3)}`}
                  style={{ transition: 'stroke-dashoffset 0.9s linear' }}
                />
              </svg>
              <div className="absolute inset-0 flex flex-col items-center justify-center">
                <span className="text-[32px] font-bold leading-none eval-text-title">{autoInitCountdown}</span>
                <span className="mt-0.5 text-[11px] eval-text-caption">{t('evaluationPage.autoInit.seconds')}</span>
              </div>
            </div>

            {/* Hint */}
            <p className="mb-6 text-[12px] leading-relaxed eval-text-secondary">
              {t('evaluationPage.autoInit.hint')}
            </p>

            {/* Actions */}
            <div className="flex flex-col gap-2">
              <button type="button" className="hb-btn-primary w-full !py-2.5 gap-1.5" onClick={handleAutoInitNow}>
                <Rocket size={13} />
                {t('evaluationPage.autoInit.btnNow')}
              </button>
              <button type="button" className="hb-btn-ghost w-full !py-2 !text-[12px]" onClick={handleAutoInitCancel}>
                {t('evaluationPage.autoInit.btnCancel')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 沙箱初始化遮罩：轮询或重置过程中覆盖主内容，防止用户看到未初始化状态 */}
      {!autoInitVisible && showSandboxInitOverlay && (
        <div className="flex h-[calc(100vh-116px)] min-h-[680px] items-center justify-center">
          <div className="w-full max-w-[400px] rounded-3xl border eval-chat-wrapper p-8 text-center shadow-xl">
            <div className="mb-5 flex justify-center">
              <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-[var(--hb-blue)]/10">
                <Loader2 size={26} className="animate-spin text-[var(--hb-blue)]" />
              </div>
            </div>
            <h2 className="mb-1 text-[17px] font-semibold eval-text-title">
              {resetting ? '正在清理评估数据' : '正在初始化评估环境'}
            </h2>
            <p className="mb-6 text-[13px] leading-relaxed eval-text-secondary">
              正在为 <strong>{employee.nickname}</strong>
              {resetting ? ' 清理旧的评估数据，请稍候...' : ' 准备双沙箱环境，请稍候...'}
            </p>
            {!resetting && workspaceProgressSummary && (
              <div className="mb-4">
                <div className="mb-2 flex items-center justify-between text-[12px]">
                  <span className="truncate eval-text-secondary">{workspaceProgressSummary.label}</span>
                  <span className="ml-2 shrink-0 font-medium eval-text-title">
                    {workspaceProgressSummary.completed}/{workspaceProgressSummary.total}
                  </span>
                </div>
                <div className="h-1.5 w-full rounded-full eval-progress-track">
                  <div
                    className="h-1.5 rounded-full transition-all duration-500 eval-progress-bar-ok"
                    style={{ width: `${workspaceProgressSummary.percent}%` }}
                  />
                </div>
              </div>
            )}
            <p className="text-[12px] eval-text-caption">初始化完成后页面将自动就绪</p>
          </div>
        </div>
      )}

      {!autoInitVisible && !showSandboxInitOverlay && (
      <div className="flex h-[calc(100vh-116px)] min-h-[680px] flex-col gap-3">
        <div className="flex flex-col gap-3">
          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
              <div className="min-w-0">
                <h1 className="text-[20px] font-semibold eval-text-strong">AI 评估对话</h1>
                <p className="mt-1 text-[12px] leading-5 eval-text-secondary">
                  通过双沙箱实时对话推进评估，流程状态、会话和报告会持续同步到当前页面。
                </p>
              </div>
              <div className="flex max-w-full items-center justify-start xl:justify-end">
                <div className="rounded-full border eval-flow-target px-3 py-1.5 text-[11px]">
                  <span className="eval-text-caption">评估对象</span>
                  <span className="ml-2 font-medium eval-text-title">{employee.nickname} · {employee.roleName}</span>
                </div>
              </div>
            </div>

            {error && (
              <div className="rounded-xl border eval-bar-error px-3 py-2 text-xs">
                <span className="inline-flex items-center gap-1.5">
                  <AlertCircle size={12} />
                  {error}
                </span>
              </div>
            )}

            <section className="hb-card eval-flow-panel px-4 pb-3 pt-3">
              <div className="flex flex-col gap-3">
                <div className="flex flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
                  <div className="min-w-0 flex-1 overflow-x-auto pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
                    <div className="flex min-w-[1180px] pl-[50px]">
                    {workflowStages.map((stage, index) => {
                      const tone = workflowStageTone(stage.status)
                      const textTone = workflowStageTextTone(stage.status)
                      const isCurrentStage = index === currentWorkflowStageIndex
                      const connectorTone = stage.status === 'completed'
                        ? 'eval-flow-step-line-completed'
                        : stage.status === 'running'
                          ? 'eval-flow-step-line-running'
                          : stage.status === 'failed'
                            ? 'eval-flow-step-line-failed'
                            : 'eval-flow-step-line-pending'

                      return (
                        <div key={stage.key} className="flex min-w-0 flex-1">
                          <div className="min-w-0 flex-1">
                            <div className="mt-[4px] flex items-center">
                              <div className={`eval-flow-step-node ${tone} ${isCurrentStage ? 'eval-flow-step-node-current' : ''}`}>
                                {renderWorkflowStageMarker(stage.status, index + 1)}
                              </div>
                              {index < workflowStages.length - 1 && (
                                <div className={`eval-flow-step-line ${connectorTone}`} />
                              )}
                            </div>
                            <div className="eval-flow-stage-copy mt-[16px] pr-4">
                              <div className={`eval-flow-stage-title ${stage.status === 'pending' ? 'eval-flow-stage-title-muted' : ''} ${isCurrentStage ? 'eval-flow-stage-title-current' : ''}`}>
                                {stage.title}
                              </div>
                              <div className={`mt-1 inline-flex items-center gap-1.5 text-[12px] font-medium leading-4 ${textTone} ${isCurrentStage ? 'eval-flow-stage-status-current' : ''}`}>
                                {stage.status === 'completed' ? (
                                  <Check size={12} />
                                ) : stage.status === 'running' ? (
                                  <Loader2 size={12} className="animate-spin" />
                                ) : stage.status === 'failed' ? (
                                  <AlertCircle size={12} />
                                ) : (
                                  <span className="h-1.5 w-1.5 rounded-full bg-current opacity-70" />
                                )}
                                {workflowStageStatusLabel(stage.status, stage.pendingLabel)}
                              </div>
                            </div>
                          </div>
                        </div>
                      )
                    })}
                    </div>
                  </div>

                  <div className="flex shrink-0 flex-wrap items-center gap-2 xl:ml-8 xl:self-start">
                    {resetConfirm ? (
                      <div className="flex items-center gap-1.5 rounded-lg border border-[var(--hb-danger)]/30 bg-[var(--hb-danger)]/5 px-2.5 py-2">
                        <AlertCircle size={11} className="shrink-0 text-[var(--hb-danger)]" />
                        <span className="whitespace-nowrap text-[11px] text-[var(--hb-danger)]">确认清理？</span>
                        <button
                          type="button"
                          disabled={resetting || submitting}
                          className="text-[11px] font-semibold text-[var(--hb-danger)] underline-offset-2 hover:underline disabled:opacity-50"
                          onClick={() => void handleResetEvaluationData()}
                        >
                          {resetting ? (
                            <span className="flex items-center gap-1"><Loader2 size={10} className="animate-spin" />清理中...</span>
                          ) : '确认'}
                        </button>
                        <span className="text-[11px] text-[var(--hb-border)]">/</span>
                        <button
                          type="button"
                          disabled={resetting}
                          className="text-[11px] text-[var(--hb-soft)] hover:text-[var(--hb-body)] disabled:opacity-50"
                          onClick={() => setResetConfirm(false)}
                        >
                          取消
                        </button>
                      </div>
                    ) : (
                      <button
                        type="button"
                        disabled={resetting || submitting}
                        className="eval-flow-ghost-btn"
                        onClick={() => void handleResetEvaluationData()}
                        title="清理当前评估数据（工作区状态、会话记录、报告），便于重新走评估流程"
                      >
                        {resetting ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                        {resetting ? '清理中...' : '清理'}
                      </button>
                    )}
                    <button
                      type="button"
                      disabled={submitting || wsEvaluating || !aiRunning}
                      className="eval-flow-primary-btn min-w-[176px] justify-center"
                      onClick={() => void submitAiDecision('RUN')}
                    >
                      {wsEvaluating ? (
                        <Loader2 size={13} className="animate-spin" />
                      ) : (
                        <CheckCircle2 size={13} />
                      )}
                      {primaryActionLabel}
                    </button>
                  </div>
                </div>

                <div className="eval-flow-status-strip eval-flow-status-strip-indented">
                  <span className="eval-flow-status-item">
                    <span className={environmentStatus.dotClassName} />
                    {environmentStatus.label}
                  </span>
                  <span className="eval-flow-status-divider" aria-hidden="true" />
                  <span className={`eval-flow-status-item ${sandboxConnected ? 'eval-flow-status-connected' : 'eval-flow-status-muted'}`}>
                    会话{sandboxConnected ? '已连接' : '未连接'}
                  </span>
                  {workspaceStatus?.sessionId && (
                    <>
                      <span className="eval-flow-status-divider" aria-hidden="true" />
                      <span className="eval-flow-status-item eval-flow-status-session">
                        <span className="eval-flow-status-label">Session</span>
                        <span className="font-mono eval-flow-status-session-value">{shortSessionId(workspaceStatus.sessionId)}</span>
                        <button
                          type="button"
                          className="eval-flow-copy-btn"
                          onClick={handleCopySessionId}
                          title={sessionCopied ? '已复制' : '复制 Session'}
                        >
                          {sessionCopied ? <Check size={12} /> : <Copy size={12} />}
                        </button>
                      </span>
                    </>
                  )}
                  {workspaceProgressSummary?.errorMessage && (
                    <>
                      <span className="eval-flow-status-divider" aria-hidden="true" />
                      <span className="eval-flow-status-item eval-flow-status-error">
                        <AlertCircle size={12} className="shrink-0" />
                        {workspaceProgressSummary.errorMessage}
                      </span>
                    </>
                  )}
                </div>
              </div>
            </section>
          </div>
        </div>

        <section className="flex min-h-0 flex-1 gap-4">
          {gatewayEndpointRef.current && (
            <SessionListPanel
              gatewayEndpoint={gatewayEndpointRef.current}
              currentSessionId={selectedSessionId}
              onSelectSession={(sessionId) => void handleSelectSession(sessionId)}
              onNewChat={() => void handleNewChat()}
              refreshTrigger={sessionListRefreshKey}
            />
          )}

          <div className="hb-card flex min-w-0 flex-1 flex-col overflow-hidden">
            <div className="border-b eval-chat-footer px-5 py-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <div className="flex h-9 w-9 items-center justify-center rounded-2xl eval-icon-indigo">
                      <MessageCircle size={18} />
                    </div>
                    <div>
                      <div className="text-base font-semibold eval-text-title">评估对话</div>
                      <div className="text-[12px] leading-5 eval-text-secondary">主视图聚焦对话发起，题卡、轨迹和报告继续保留在右侧。</div>
                    </div>
                  </div>
                </div>
                <div className="flex flex-wrap gap-2 text-[11px]">
                  <span className={`rounded-full border px-2.5 py-1 ${sandboxConnected ? 'eval-badge-connected' : 'eval-badge-disconnected'}`}>
                    会话连接：{sandboxConnected ? '已连接' : '未连接'}
                  </span>
                  {selectedSessionId && (
                    <span className="rounded-full border eval-pill-neutral px-2.5 py-1">
                      当前会话：{shortSessionId(selectedSessionId)}
                    </span>
                  )}
                </div>
              </div>
            </div>
            <div className="flex flex-1 flex-col overflow-hidden eval-chat-bg px-5 pb-4 pt-2">
              {/* 评估结果横幅：无论通过或未通过，都应允许人工评估接管决策 */}
              {canNavigateToHumanEval && humanEvalPath && (
                <div className={`mb-3 flex shrink-0 items-center justify-between gap-3 rounded-2xl border px-4 py-3 shadow-sm ${humanEvalBannerTone}`}>
                  <div className={`flex items-center gap-2.5 text-sm font-medium ${humanEvalBannerTextTone}`}>
                    <CheckCircle2 size={16} className="shrink-0 eval-text-green-mid" />
                    <span>
                      {humanEvalBannerTitle}
                      {humanEvalBannerDescription}
                    </span>
                  </div>
                  <button
                    type="button"
                    disabled={enteringHumanEval}
                    className="hb-btn-primary shrink-0 !px-3 !py-1.5 !text-[12px] disabled:opacity-60"
                    onClick={() => void handleEnterHumanEval()}
                  >
                    {enteringHumanEval ? <Loader2 size={12} className="animate-spin" /> : null}
                    进入人工评估 →
                  </button>
                </div>
              )}
              <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
                {!aiRunning ? (
                  <div className="m-4 rounded-2xl border eval-inactive-tip px-4 py-3 text-sm leading-6">
                    请先点击“准备评估环境”。环境就绪后，这里会成为主聊天入口，你可以直接和评估沙箱对话，再结合右侧题卡、轨迹和报告辅助判断。
                  </div>
                ) : (
                  <>
                    {testcaseItems.length > 0 && (
                      <div className="shrink-0 border-b eval-chat-footer px-5 py-2.5">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="text-[12px] font-medium eval-text-green-mid">✓ 测试用例已就绪</span>
                          <span className="rounded-full border eval-stats-badge px-2 py-0.5 text-[11px]">
                            {testcaseItems.length} 个场景
                          </span>
                          {testcaseItems.slice(0, 3).map((outline) => (
                            <span key={outline.testcaseId} className="max-w-[160px] truncate rounded-full border eval-pill-neutral px-2 py-0.5 text-[11px]">
                              {outline.title || outline.testcaseId}
                            </span>
                          ))}
                          {testcaseItems.length > 3 && (
                            <button
                              type="button"
                              className="rounded-full border eval-pill-neutral px-2 py-0.5 text-[11px] eval-text-indigo transition-colors hover:bg-[var(--hb-blue)]/10"
                              onClick={() => {
                                setArtifactTab('testcase')
                              }}
                            >
                              +{testcaseItems.length - 3} 查看全部 →
                            </button>
                          )}
                        </div>
                      </div>
                    )}
                    <div className={`flex-1 px-5 py-4 ${hasChatTimelineContent ? 'space-y-3 overflow-y-auto' : 'overflow-y-hidden'}`}>
                      {chatLoading ? (
                        <div className="flex items-center gap-2 text-sm text-[var(--hb-soft)]">
                          <Loader2 size={14} className="animate-spin" />
                          正在加载评估沙箱对话...
                        </div>
                      ) : chatMessages.length === 0 ? (
                        <div className="flex min-h-full items-center justify-center py-8">
                          <section className="eval-chat-empty-stage flex w-full max-w-[760px] flex-col items-center">
                            <div className="eval-empty-stage-icon">
                              <MessageCircle size={20} />
                            </div>
                            <div className="mt-5 text-center">
                              <h2 className="text-[28px] font-semibold tracking-[-0.02em] eval-text-strong">从一句话开始评估</h2>
                              <p className="mx-auto mt-3 max-w-[560px] text-[14px] leading-7 eval-text-secondary">
                                暂无对话。选一个起手动作直接向评估沙箱提问，所有答复、执行轨迹和评分结论都会同步回到当前面板。
                              </p>
                            </div>

                            <div className="mt-9 grid w-full gap-3 md:grid-cols-3">
                              {evaluationStarterActions.map((action, index) => (
                                <button
                                  key={action.key}
                                  type="button"
                                  disabled={chatSending}
                                  className="eval-starter-card text-left disabled:cursor-not-allowed disabled:opacity-60"
                                  onClick={() => handleStarterAction(action.prompt)}
                                >
                                  <div className="flex items-start justify-between gap-3">
                                    <div className="eval-starter-card-icon">
                                      <action.icon size={16} />
                                    </div>
                                    <span className="eval-starter-card-index">{index + 1}</span>
                                  </div>
                                  <div className="mt-6 text-[18px] font-semibold tracking-[-0.02em] eval-text-title">
                                    {action.title}
                                  </div>
                                  <p className="mt-2 text-[13px] leading-6 eval-text-secondary">
                                    {action.description}
                                  </p>
                                </button>
                              ))}
                            </div>

                            <div className="mt-5 flex flex-wrap items-center justify-center gap-2">
                              {evaluationSuggestionPrompts.map((prompt) => (
                                <button
                                  key={prompt}
                                  type="button"
                                  disabled={chatSending}
                                  className="eval-suggestion-pill disabled:cursor-not-allowed disabled:opacity-60"
                                  onClick={() => handleSuggestionSelect(prompt)}
                                >
                                  {prompt}
                                </button>
                              ))}
                            </div>
                          </section>
                        </div>
                      ) : (
                        chatMessages.map((message) => {
                          const isUser = message.role.toLowerCase() === 'user'
                          return (
                            <div key={message.messageId} className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
                              {!isUser && (
                                <div className="hb-hiring-avatar mr-2 mt-0.5 shrink-0">评</div>
                              )}
                              <div className={`flex min-w-0 max-w-[90%] flex-col gap-1.5 ${isUser ? 'items-end' : 'items-start'}`}>
                                {!isUser && message.toolSteps && message.toolSteps.length > 0 && (
                                  <HiringToolStepsBlock steps={message.toolSteps} />
                                )}
                                <div
                                  className={`rounded-2xl px-3 py-2.5 text-sm leading-6 ${
                                    isUser
                                      ? 'eval-bubble-user'
                                      : 'border eval-bubble-bot'
                                  }`}
                                >
                                  <div className={`mb-1 text-[11px] ${isUser ? 'eval-bubble-meta-user' : 'eval-bubble-meta-bot'}`}>
                                    {isUser ? '你' : '评估沙箱'} · {formatDateTime(message.createdAt)}
                                  </div>
                                  {isUser ? (
                                    <div className="whitespace-pre-wrap break-words">{message.content}</div>
                                  ) : (
                                    <div className="hb-md prose prose-sm max-w-none break-words">
                                      <ReactMarkdown remarkPlugins={[remarkGfm]}>
                                        {message.content}
                                      </ReactMarkdown>
                                    </div>
                                  )}
                                </div>
                              </div>
                              {isUser && (
                                <div className="hb-hiring-avatar is-user ml-2 mt-0.5 shrink-0">你</div>
                              )}
                            </div>
                          )
                        })
                      )}
                      {/* 流式回复气泡：有工具步骤时先显示折叠面板，再显示流式文本或 typing 动画 */}
                      {(streamingContent !== null || chatTyping) && (
                        <div className="flex justify-start">
                          <div className="hb-hiring-avatar mr-2 mt-0.5 shrink-0">评</div>
                          <div className="flex min-w-0 max-w-[90%] flex-col items-start gap-1.5">
                            {streamingToolSteps.length > 0 && (
                              <HiringToolStepsBlock steps={streamingToolSteps} />
                            )}
                            {chatTyping && streamingContent === '' ? (
                              <div className="hb-hiring-bubble is-bot hb-hiring-bubble-loading">
                                {[0, 1, 2].map((i) => (
                                  <span
                                    key={i}
                                    className="hb-hiring-typing-dot"
                                    style={{ animationDelay: `${i * 0.15}s` }}
                                  />
                                ))}
                              </div>
                            ) : streamingContent ? (
                              <div className="rounded-2xl border eval-bubble-bot px-3 py-2.5 text-sm leading-6">
                                <div className="mb-1 text-[11px] eval-bubble-meta-bot">评估沙箱 · 正在回复</div>
                                <div className="hb-md prose prose-sm max-w-none break-words">
                                  <ReactMarkdown remarkPlugins={[remarkGfm]}>
                                    {streamingContent}
                                  </ReactMarkdown>
                                </div>
                              </div>
                            ) : null}
                          </div>
                        </div>
                      )}
                      {hasChatTimelineContent ? <div ref={chatEndRef} /> : null}
                    </div>
                    <div className="border-t eval-chat-footer px-4 py-4">
                      <div className="eval-composer-shell flex items-end gap-3 rounded-[24px] border px-4 py-3">
                        <textarea
                          ref={chatInputRef}
                          value={chatInput}
                          onChange={(event) => setChatInput(event.target.value)}
                          onKeyDown={(event) => {
                            if (event.key === 'Enter' && !event.shiftKey) {
                              event.preventDefault()
                              void sendEvaluatorMessage()
                            }
                          }}
                          rows={2}
                          disabled={chatSending}
                          placeholder="向评估沙箱发送消息（Enter 发送，Shift+Enter 换行）"
                          className="eval-composer-input min-h-[88px] flex-1 resize-none bg-transparent px-1 py-2 text-sm leading-6 outline-none disabled:opacity-60"
                        />
                        <button
                          type="button"
                          disabled={chatSending || !chatInput.trim()}
                          className="hb-btn-primary mb-1 !h-11 !w-11 !rounded-full !px-0 !py-0 disabled:!bg-[#d4d4d8]"
                          onClick={() => void sendEvaluatorMessage()}
                        >
                          {chatSending ? <Loader2 size={12} className="animate-spin" /> : <SendHorizontal size={12} />}
                        </button>
                      </div>
                    </div>
                  </>
                )}
              </div>

              {chatError && (
                <div className="mt-2 rounded-xl border eval-bar-error px-2.5 py-1.5 text-[11px]">
                  {chatError}
                </div>
              )}
              {sessionSwitching && (
                <div className="mt-2 rounded-xl border eval-bar-info px-2.5 py-1.5 text-[11px]">
                  正在切换评估会话...
                </div>
              )}
            </div>
          </div>
          <div
            className={`${
              rightCollapsed ? 'w-10' : 'w-[320px] xl:w-[340px] 2xl:w-[360px]'
            } hb-card flex shrink-0 flex-col overflow-hidden transition-all duration-200`}
          >
            {rightCollapsed ? (
              <button
                type="button"
                onClick={() => setRightCollapsed(false)}
                className="eval-collapse-btn flex h-full w-full items-center justify-center transition-colors"
              >
                <ChevronDown size={16} className="-rotate-90 text-[var(--hb-caption)]" />
              </button>
            ) : (
              <>
                <div className="flex items-center border-b eval-tab-bar px-2">
                  {[
                    { key: 'overview' as ArtifactTab, label: '概览报告', icon: BarChart2 },
                    { key: 'testcase' as ArtifactTab, label: '测试用例', icon: FileText },
                    { key: 'trace' as ArtifactTab, label: '执行轨迹', icon: Zap },
                    { key: 'report' as ArtifactTab, label: '评估报告', icon: BarChart2 },
                  ].map((tab) => (
                    <button
                      key={tab.key}
                      type="button"
                      onClick={() => setArtifactTab(tab.key)}
                      className={`eval-side-tab-button flex flex-1 items-center justify-center gap-1 border-b-2 px-2 py-3 text-[11px] font-medium whitespace-nowrap ${
                        artifactTab === tab.key ? 'eval-tab-active' : 'eval-tab-inactive'
                      }`}
                    >
                      <tab.icon size={12} />
                      {tab.label}
                    </button>
                  ))}
                  <button
                    type="button"
                    onClick={() => setRightCollapsed(true)}
                    className="ml-auto rounded-lg px-2 py-2 text-[var(--hb-caption)] transition-colors hover:bg-[var(--hb-surface-soft)] hover:text-[var(--hb-soft)]"
                  >
                    <ChevronDown size={14} className="rotate-90" />
                  </button>
                </div>

                <div className="flex-1 overflow-y-auto p-4 pt-3 text-xs">
                  {artifactTab === 'overview' && (
                    <div className="space-y-3">
                      <div className={`rounded-[22px] border p-4 ${reportSummary?.passed === false ? 'eval-report-fail' : 'eval-report-pass'}`}>
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <div className={`text-[11px] font-semibold uppercase tracking-[0.08em] ${reportSummary?.passed === false ? 'eval-text-red-2' : 'eval-text-green-mid'}`}>
                              {reportSummary == null ? '等待评估' : reportSummary.passed ? '✓ 评估通过' : '✗ 评估未通过'}
                            </div>
                            <div className="mt-1 text-base font-semibold eval-text-title">AI 评估结论</div>
                            <div className="mt-1 text-[11px] leading-relaxed eval-text-secondary">
                              {reportSummary
                                ? `第 ${reportSummary.iteration} 轮 · ${formatDateTime(reportSummary.createdAtUtc)}`
                                : '执行评估后，这里会展示本轮结论和关键指标。'}
                            </div>
                          </div>
                          <div className="rounded-2xl border eval-score-card px-4 py-3 text-center">
                            <div className="text-3xl font-bold tabular-nums eval-text-title">{reportSummary?.overallScore ?? '--'}</div>
                            <div className="mt-1 text-[11px] eval-text-secondary">综合评分</div>
                          </div>
                        </div>
                        <div className="mt-4 grid grid-cols-2 gap-2">
                          {reportMetrics.slice(0, 4).map((metric) => (
                            <div key={metric.label} className="rounded-2xl border eval-score-card px-3 py-2.5">
                              <div className="text-[10px] uppercase tracking-[0.06em] eval-text-caption">{metric.label}</div>
                              <div className={`mt-1 text-sm font-semibold ${metric.tone}`}>{metric.value}</div>
                            </div>
                          ))}
                        </div>
                        <div className="mt-4 rounded-2xl border eval-recommendation px-3 py-3 text-[11px] leading-relaxed">
                          {evaluation.recommendation || '暂无建议，等待评估结果生成。'}
                        </div>
                      </div>

                      {dimensionScores.length > 0 && (
                        <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel">
                          <summary className="eval-side-disclosure-summary">
                            <span className="inline-flex items-center gap-1.5">
                              <BarChart2 size={12} />
                              维度评分明细
                            </span>
                          </summary>
                          <div className="eval-side-disclosure-body space-y-2">
                            {dimensionScores.map((item) => (
                              <div key={item.dimension} className="rounded-xl border eval-dim-item px-3 py-2.5">
                                <div className="flex items-center justify-between gap-2">
                                  <span className="text-[11px] font-medium eval-text-title">{item.dimension}</span>
                                  <span className="tabular-nums text-[11px] font-semibold eval-text-indigo">{item.score}</span>
                                </div>
                                {item.comment && (
                                  <div className="mt-1.5 text-[10px] leading-relaxed eval-text-secondary">{item.comment}</div>
                                )}
                              </div>
                            ))}
                          </div>
                        </details>
                      )}

                      {(reportJsonUrl || reportHtmlUrl || workspaceStatus?.sessionId) && (
                        <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel">
                          <summary className="eval-side-disclosure-summary">
                            <span className="inline-flex items-center gap-1.5">
                              <FileText size={12} />
                              报告资源与调试信息
                            </span>
                          </summary>
                          <div className="eval-side-disclosure-body space-y-3">
                            {(reportJsonUrl || reportHtmlUrl) && (
                              <div className="flex flex-wrap gap-2">
                                {reportJsonUrl && (
                                  <a
                                    href={reportJsonUrl}
                                    target="_blank"
                                    rel="noreferrer"
                                    className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]"
                                  >
                                    <ExternalLink size={10} />
                                    查看报告 JSON
                                  </a>
                                )}
                                {reportHtmlUrl && reportSummary && (
                                  <a
                                    href={reportHtmlUrl}
                                    download={`evaluation-report-${reportSummary.reportId}.html`}
                                    target="_blank"
                                    rel="noreferrer"
                                    className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]"
                                  >
                                    <ExternalLink size={10} />
                                    下载报告 HTML
                                  </a>
                                )}
                              </div>
                            )}
                            <div className="space-y-1 text-[10px] font-mono leading-relaxed eval-text-secondary">
                              <div>session: {workspaceStatus?.sessionId ?? '--'}</div>
                              <div>target: {workspaceStatus?.targetSandboxId ?? '--'}</div>
                              <div>evaluator: {workspaceStatus?.evaluatorSandboxId ?? '--'}</div>
                            </div>
                          </div>
                        </details>
                      )}

                      {testcaseItems.length > 0 && (
                        <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel" open>
                          <summary className="eval-side-disclosure-summary">
                            <span className="inline-flex items-center gap-1.5">
                              <Check size={12} />
                              测试用例 · {testcaseItems.length} 个
                            </span>
                          </summary>
                          <div className="eval-side-disclosure-body space-y-1.5">
                            {testcaseItems.slice(0, 3).map((outline) => (
                              <div key={outline.testcaseId} className="truncate rounded-xl border eval-pill-neutral px-2.5 py-1.5 text-[11px]">
                                {outline.title || outline.testcaseId}
                              </div>
                            ))}
                            {testcaseItems.length > 3 && (
                              <button
                                type="button"
                                onClick={() => setArtifactTab('testcase')}
                                className="w-full rounded-xl border eval-pill-neutral px-2.5 py-1.5 text-left text-[11px] eval-text-indigo"
                              >
                                +{testcaseItems.length - 3} 查看全部 →
                              </button>
                            )}
                          </div>
                        </details>
                      )}
                    </div>
                  )}

                  {artifactTab === 'testcase' && (
                    <div className="space-y-3">
                      {testcaseItems.length === 0 ? (
                        !workspaceReady ? (
                          <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                            请先完成沙箱初始化流程，随后展示测试用例。
                          </div>
                        ) : !materialsReady ? (
                          <div className="rounded-[20px] border eval-side-notice-warning px-4 py-3 text-[11px] leading-relaxed">
                            素材未就绪，等待完成“加载评估素材”后将自动激活更多场景。
                          </div>
                        ) : (
                          <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                            暂无测试用例。
                          </div>
                        )
                      ) : (
                        <>
                          <div className="rounded-[18px] eval-side-status-banner px-4 py-3 text-[12px] font-medium">
                            <span className="inline-flex items-center gap-2">
                              <Check size={13} />
                              用例已就绪，可开始评估
                            </span>
                          </div>

                          <div className="flex items-center gap-2 px-1">
                            <div className="text-[13px] font-semibold eval-text-title">测试场景</div>
                            <span className="text-[12px] font-medium eval-text-caption">{testcaseItems.length} 个</span>
                          </div>

                          <div className="space-y-3">
                            {testcaseItems.map((outline) => {
                              const card = questionCardMap.get(outline.testcaseId)
                              const expanded = expandedQuestionCardIds.includes(outline.testcaseId)

                              return (
                                <article key={outline.testcaseId} className="rounded-[20px] border eval-side-case-card px-4 py-4">
                                  <div className="flex items-start justify-between gap-3">
                                    <div className="flex min-w-0 items-start gap-3">
                                      <span className="mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full bg-[var(--hb-text-green)]" />
                                      <div className="min-w-0">
                                        <div className="text-[14px] font-semibold leading-6 eval-text-title">
                                          {outline.title}
                                        </div>
                                        <div className="mt-2 border-l-2 border-[rgba(148,163,184,0.18)] pl-3 text-[12px] leading-6 eval-text-body-2">
                                          {outline.userRequest || '未提供用户请求。'}
                                        </div>
                                      </div>
                                    </div>
                                    <span className="shrink-0 text-[11px] font-mono eval-text-caption">{outline.testcaseId}</span>
                                  </div>

                                  <div className="mt-4 flex flex-wrap items-center gap-4 text-[12px]">
                                    {card && (
                                      <button
                                        type="button"
                                        className="eval-side-inline-action"
                                        onClick={() => toggleQuestionCardDetails(outline.testcaseId)}
                                      >
                                        {expanded ? '收起题卡' : '展开题卡'}
                                      </button>
                                    )}
                                    <button
                                      type="button"
                                      disabled={!aiRunning || chatSending}
                                      className="eval-side-inline-action disabled:opacity-50"
                                      onClick={() => handleRunSingleScenario(outline.testcaseId, outline.title)}
                                    >
                                      仅运行此场景
                                    </button>
                                  </div>

                                  {expanded && card && (
                                    <div className="mt-4 rounded-[18px] border eval-side-case-detail px-3 py-3">
                                      {card.prompt && (
                                        <div className="rounded-xl border eval-prompt-box px-3 py-2.5 text-[11px] leading-relaxed">
                                          {card.prompt}
                                        </div>
                                      )}

                                      {card.steps.length > 0 && (
                                        <div className="mt-3 space-y-2">
                                          <div className="text-[10px] font-semibold uppercase tracking-[0.06em] eval-text-caption">
                                            评估步骤（{card.steps.length}）
                                          </div>
                                          {card.steps.map((step, stepIndex) => (
                                            <div
                                              key={`${card.testcaseId}_step_${stepIndex}`}
                                              className="flex gap-2 rounded-xl border eval-step-row px-2.5 py-2"
                                            >
                                              <span className="flex h-4 w-4 shrink-0 items-center justify-center rounded-full eval-seq-circle text-[9px] font-semibold">
                                                {stepIndex + 1}
                                              </span>
                                              <div className="text-[11px] leading-relaxed eval-text-body-2">{step}</div>
                                            </div>
                                          ))}
                                        </div>
                                      )}

                                      {(card.scoringHint || card.sourceFile) && (
                                        <div className="mt-3 space-y-2">
                                          {card.scoringHint && (
                                            <div className="rounded-xl border border-dashed eval-scoring-hint px-3 py-2 text-[11px] leading-relaxed">
                                              <span className="font-semibold eval-text-body-2">评分提示：</span>
                                              {card.scoringHint}
                                            </div>
                                          )}
                                          {card.sourceFile && (
                                            <div className="text-[10px] eval-text-caption">来源文件：{card.sourceFile}</div>
                                          )}
                                        </div>
                                      )}
                                    </div>
                                  )}
                                </article>
                              )
                            })}
                          </div>
                        </>
                      )}
                    </div>
                  )}

                  {artifactTab === 'trace' && (
                    <div className="space-y-3">
                      {traceAssets.length === 0 ? (
                        <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                          暂无执行轨迹，请先执行评估。
                        </div>
                      ) : (
                        <>
                          <div className="rounded-[18px] border eval-overview-panel px-4 py-3">
                            <div className="text-[12px] font-medium eval-text-title">已生成 {traceAssets.length} 份执行轨迹</div>
                            <div className="mt-1 text-[11px] eval-text-secondary">最新更新时间：{formatDateTime(traceAssets[0]?.createdAtUtc)}</div>
                          </div>
                          {traceAssets.map((asset, index) => {
                            const traceLink = toAbsoluteApiUrl(asset.publicUrl)
                            const traceSessionId = evaluation?.sessionId ?? null
                            const isExpanded = traceSessionId != null && expandedTraceUrls.includes(traceSessionId)
                            const traceData = traceSessionId != null ? traceDataCache[traceSessionId] : undefined
                            return (
                              <div key={asset.relativePath} className="rounded-[18px] border eval-trace-card px-3 py-3">
                                {/* 头部 */}
                                <div className="flex items-center justify-between gap-2">
                                  <div className="flex items-center gap-1.5">
                                    <Zap size={11} className="eval-text-brand" />
                                    <span className="text-[12px] font-semibold eval-text-title">
                                      轨迹 #{index + 1}
                                    </span>
                                    <span className="text-[11px] eval-text-caption">{formatDateTime(asset.createdAtUtc)}</span>
                                  </div>
                                  {traceSessionId && (
                                    <button
                                      type="button"
                                      onClick={() => toggleTraceExpand(traceSessionId)}
                                      className="flex items-center gap-1 rounded-full border eval-pill-neutral px-2.5 py-0.5 text-[11px]"
                                    >
                                      {traceData === 'loading'
                                        ? <Loader2 size={10} className="animate-spin" />
                                        : <ChevronDown size={10} className={`transition-transform ${isExpanded ? 'rotate-180' : ''}`} />}
                                      {isExpanded ? '收起' : '展开轨迹'}
                                    </button>
                                  )}
                                </div>

                                {/* 展开后的时间线 */}
                                {isExpanded && (
                                  <div className="mt-3 space-y-2">
                                    {traceData === 'loading' && (
                                      <div className="flex items-center gap-2 text-[11px] eval-text-secondary">
                                        <Loader2 size={11} className="animate-spin" />
                                        正在加载轨迹数据...
                                      </div>
                                    )}
                                    {traceData === 'error' && (
                                      <div className="rounded-xl border eval-side-notice-warning px-3 py-2 text-[11px]">
                                        加载失败，请检查网络或重试。
                                      </div>
                                    )}
                                    {traceData != null && traceData !== 'loading' && traceData !== 'error' && (() => {
                                      const td = traceData as TraceJsonData
                                      const usage = td.http_supplement?.dashboard?.providers?.usage?.[0]
                                      const TURN_COLORS = ['l0', 'l1', 'l2', 'l3', 'l4'] as const
                                      return (
                                        <>
                                          {/* ── 元信息横条 ── */}
                                          <div className="flex flex-wrap gap-1.5">
                                            {td.status && (
                                              <span className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold ${
                                                td.status === 'completed'
                                                  ? 'eval-tone-completed'
                                                  : td.status === 'failed'
                                                  ? 'eval-tone-failed'
                                                  : 'eval-tone-running'
                                              }`}>{td.status}</span>
                                            )}
                                            {td.meta?.total_turns != null && (
                                              <span className="rounded-full border eval-stats-badge px-2 py-0.5 text-[10px]">
                                                {td.meta.total_turns} 轮
                                              </span>
                                            )}
                                            {td.meta?.iteration != null && (
                                              <span className="rounded-full border eval-stats-badge px-2 py-0.5 text-[10px]">
                                                iter-{td.meta.iteration}
                                              </span>
                                            )}
                                            {usage?.modelId && (
                                              <span className="rounded-full border eval-trace-model-badge px-2 py-0.5 text-[10px] font-mono">
                                                {usage.modelId}
                                              </span>
                                            )}
                                            {usage && (
                                              <span className="rounded-full border eval-trace-token-badge px-2 py-0.5 text-[10px]">
                                                ↑{usage.inputTokens ?? 0} ↓{usage.outputTokens ?? 0}
                                                {(usage.cacheReadTokens ?? 0) > 0 && (
                                                  <span className="opacity-65"> cache {usage.cacheReadTokens}</span>
                                                )}
                                              </span>
                                            )}
                                          </div>

                                          {/* ── 逐轮时间线 ── */}
                                          {td.turns.map((turn) => {
                                            const et = turn.execution_trace
                                            const colorIdx = turn.turn_index % TURN_COLORS.length
                                            const toolLogs = et.logs.filter(l =>
                                              l.type === 'tool_use' || l.type === 'tool_result'
                                            )
                                            return (
                                              <details
                                                key={turn.turn_index}
                                                open
                                                className={`eval-side-disclosure rounded-[16px] border eval-overview-panel eval-trace-turn-${TURN_COLORS[colorIdx]}`}
                                              >
                                                <summary className="eval-side-disclosure-summary">
                                                  <span className="inline-flex items-center gap-2">
                                                    <span className={`inline-flex h-[20px] w-[20px] items-center justify-center rounded-full text-[10px] font-bold eval-trace-seq-${colorIdx}`}>
                                                      {turn.turn_index + 1}
                                                    </span>
                                                    {turn.test_case_id && (
                                                      <span className="rounded-full bg-[var(--hb-soft)] eval-text-caption px-1.5 py-0.5 text-[10px] font-mono">
                                                        {turn.test_case_id}
                                                      </span>
                                                    )}
                                                    {et.summary?.execution_time_seconds != null && (
                                                      <span className="text-[10px] eval-text-caption">
                                                        {et.summary.execution_time_seconds.toFixed(1)}s
                                                      </span>
                                                    )}
                                                  </span>
                                                </summary>
                                                <div className="eval-side-disclosure-body space-y-2">
                                                  {/* 用户输入 */}
                                                  <div className="rounded-xl border eval-trace-user-block px-2.5 py-2">
                                                    <div className="mb-1 flex items-center gap-1">
                                                      <User size={9} />
                                                      <span className="text-[10px] font-medium opacity-70">用户</span>
                                                    </div>
                                                    {turn.user_input
                                                      ? <div className="text-[11px] font-medium">{turn.user_input}</div>
                                                      : <div className="text-[11px] opacity-40 italic">（已通过评估脚本注入，内容不在轨迹中记录）</div>
                                                    }
                                                  </div>
                                                  {/* AI 回复 */}
                                                  {et.assembled_assistant_text && (
                                                    <div className="rounded-xl border eval-trace-ai-block px-2.5 py-2">
                                                      <div className="mb-1 flex items-center gap-1 eval-text-indigo">
                                                        <Bot size={9} />
                                                        <span className="text-[10px] font-medium">AI 回复</span>
                                                      </div>
                                                      <div className="max-h-[100px] overflow-y-auto whitespace-pre-wrap break-words text-[11px] leading-relaxed eval-text-secondary">
                                                        {et.assembled_assistant_text}
                                                      </div>
                                                    </div>
                                                  )}
                                                  {/* 执行统计 */}
                                                  {et.summary && (
                                                    <div className="flex flex-wrap gap-1.5">
                                                      {et.summary.total_messages != null && (
                                                        <span className="rounded-full border eval-stats-badge px-2 py-0.5 text-[10px]">
                                                          {et.summary.total_messages} msgs
                                                        </span>
                                                      )}
                                                      {(et.summary.total_tool_calls ?? 0) > 0 && (
                                                        <span className="rounded-full border eval-stats-badge-ontology px-2 py-0.5 text-[10px]">
                                                          {et.summary.total_tool_calls} 工具
                                                        </span>
                                                      )}
                                                      {et.summary.has_thought && (
                                                        <span className="rounded-full border eval-trace-thought-badge px-2 py-0.5 text-[10px]">
                                                          思维链 ×{et.summary.think_count}
                                                        </span>
                                                      )}
                                                    </div>
                                                  )}
                                                  {/* 工具调用日志 */}
                                                  {toolLogs.map((log, li) => (
                                                    <div key={li} className="rounded-xl border eval-dim-item px-2.5 py-2">
                                                      <div className="flex items-center gap-1.5">
                                                        <span className={`rounded-md px-1.5 py-0.5 text-[10px] font-mono ${
                                                          log.type === 'tool_use'
                                                            ? 'eval-trace-badge-tool-use'
                                                            : 'eval-trace-badge-tool-result'
                                                        }`}>
                                                          {log.type === 'tool_use' ? '→ tool' : '← result'}
                                                        </span>
                                                        {log.name && (
                                                          <span className="text-[11px] font-medium eval-text-title">{log.name}</span>
                                                        )}
                                                        {(log.timestamp_start ?? log.timestamp) && (
                                                          <span className="ml-auto text-[10px] eval-text-caption font-mono">
                                                            {(log.timestamp_start ?? log.timestamp ?? '').slice(11, 19)}
                                                          </span>
                                                        )}
                                                      </div>
                                                      {log.type === 'tool_use' && log.input != null && (
                                                        <pre className="mt-1.5 max-h-[80px] overflow-y-auto break-all text-[10px] font-mono eval-text-secondary whitespace-pre-wrap">
                                                          {JSON.stringify(log.input, null, 2).slice(0, 400)}
                                                        </pre>
                                                      )}
                                                      {log.type === 'tool_result' && log.content != null && (
                                                        <pre className="mt-1.5 max-h-[80px] overflow-y-auto break-all text-[10px] font-mono eval-text-secondary whitespace-pre-wrap">
                                                          {typeof log.content === 'string'
                                                            ? log.content.slice(0, 400)
                                                            : JSON.stringify(log.content).slice(0, 400)}
                                                        </pre>
                                                      )}
                                                    </div>
                                                  ))}
                                                </div>
                                              </details>
                                            )
                                          })}

                                          {/* 原始 JSON 链接 */}
                                          {traceLink && (
                                            <a
                                              href={traceLink}
                                              target="_blank"
                                              rel="noreferrer"
                                              className="inline-flex items-center gap-1 text-[11px] eval-link"
                                            >
                                              <ExternalLink size={10} />
                                              查看原始 Trace JSON
                                            </a>
                                          )}
                                        </>
                                      )
                                    })()}
                                  </div>
                                )}
                              </div>
                            )
                          })}
                        </>
                      )}
                    </div>
                  )}

                  {artifactTab === 'report' && (
                    <div className="space-y-3">
                      {!reportSummary ? (
                        <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                          暂无评估报告，请先执行评估。
                        </div>
                      ) : (
                        <>
                          {/* 整体结论 */}
                          <div className={`rounded-2xl border p-4 ${reportSummary.passed ? 'eval-report-pass' : 'eval-report-fail'}`}>
                            <div className="flex items-center justify-between gap-3">
                              <div>
                                <div className={`text-[11px] font-semibold uppercase tracking-[0.08em] ${reportSummary.passed ? 'eval-text-green-mid' : 'eval-text-red-2'}`}>
                                  {reportSummary.passed ? '✓ 评估通过' : '✗ 评估未通过'}
                                </div>
                                <div className="mt-1 text-[11px] eval-text-secondary">
                                  第 {reportSummary.iteration} 轮 · {formatDateTime(reportSummary.createdAtUtc)}
                                </div>
                              </div>
                              <div className="rounded-xl border eval-score-card px-3 py-2 text-center shadow-sm">
                                <div className="text-2xl font-bold tabular-nums eval-text-title">{reportSummary.overallScore}</div>
                                <div className="text-[10px] eval-text-secondary">综合评分</div>
                              </div>
                            </div>
                          </div>

                          {/* 维度评分 */}
                          {dimensionScores.length > 0 && (
                            <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel">
                              <summary className="eval-side-disclosure-summary">
                                <span>维度评分明细</span>
                              </summary>
                              <div className="eval-side-disclosure-body space-y-2">
                                {dimensionScores.map((item) => (
                                  <div key={item.dimension} className="rounded-xl border eval-dim-item px-3 py-2">
                                    <div className="flex items-center justify-between gap-2">
                                      <span className="text-[11px] font-medium eval-text-title">{item.dimension}</span>
                                      <span className="tabular-nums text-[11px] font-semibold eval-text-indigo">{item.score}</span>
                                    </div>
                                    {item.comment && (
                                      <div className="mt-1 text-[10px] leading-relaxed eval-text-secondary">{item.comment}</div>
                                    )}
                                  </div>
                                ))}
                              </div>
                            </details>
                          )}

                          {/* 有评估结果就允许进入人工评估，由用户做最终决策 */}
                          {humanEvalPath && (
                            <button
                              type="button"
                              className="hb-btn-primary w-full !py-2 !text-[12px]"
                              onClick={() => handleEnterHumanEval()}
                            >
                              <CheckCircle2 size={12} />
                              进入人工评估环节 →
                            </button>
                          )}
                        </>
                      )}
                    </div>
                  )}
                </div>
              </>
            )}
          </div>
        </section>
      </div>
      )}

      {showHumanEvalConfirm && (
        <div className="hb-modal-mask" onClick={() => setShowHumanEvalConfirm(false)}>
          <div className="hb-modal hb-delete-confirm-modal" onClick={(e) => e.stopPropagation()}>
            <button type="button" className="hb-modal-close" onClick={() => setShowHumanEvalConfirm(false)}>
              <X size={16} />
            </button>
            <div className="hb-modal-head">
              <h3 className="hb-modal-title">{t('evaluationPage.confirmHumanEvalTitle')}</h3>
              <p className="hb-modal-sub">{t('evaluationPage.confirmHumanEvalMessage')}</p>
            </div>
            <div className="hb-modal-foot">
              <button type="button" className="hb-btn-ghost" onClick={() => setShowHumanEvalConfirm(false)}>
                {t('evaluationPage.confirmHumanEvalCancel')}
              </button>
              <button
                type="button"
                className="hb-btn-primary"
                onClick={() => void confirmEnterHumanEval()}
                disabled={enteringHumanEval}
              >
                {enteringHumanEval ? <Loader2 size={12} className="animate-spin" /> : null}
                {t('evaluationPage.confirmHumanEvalConfirm')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

