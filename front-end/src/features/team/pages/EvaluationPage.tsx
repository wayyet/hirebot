import { useEffect, useMemo, useRef, useState } from 'react'
import {
  AlertCircle,
  BarChart2,
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
  X,
  Zap,
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

type ArtifactTab = 'overview' | 'testcase' | 'trace' | 'report'
type WorkflowStageStatus = 'pending' | 'running' | 'completed' | 'failed'

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

function workflowStagePill(status: WorkflowStageStatus) {
  switch (status) {
    case 'completed':
      return '已完成'
    case 'running':
      return '进行中'
    case 'failed':
      return '失败'
    default:
      return '等待中'
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
  const traceAssets = (evaluation?.assetRefs ?? [])
    .filter((asset) => asset.assetType === 'trace-json')
    .slice(0, 8)
  const materialsReady = evaluation?.readiness?.status === 'ready'
  const reportSummary = evaluation?.latestReport ?? null
  const reportJsonUrl = toAbsoluteApiUrl(reportSummary?.reportJsonUrl ?? null)
  const reportHtmlUrl = toAbsoluteApiUrl(reportSummary?.reportHtmlUrl ?? null)
  const dimensionScores = reportSummary?.dimensionScores ?? []
  const testcaseReady = evaluation?.readiness?.testcasesReady ?? false
  const ontologyReady = evaluation?.readiness?.ontologyReady ?? false

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

  const workflowStages = useMemo<Array<{ key: string; title: string; detail: string; status: WorkflowStageStatus }>>(() => {
    const stepMap = new Map((workspaceStatus?.steps ?? []).map((step) => [step.step, step.status]))
    const executionStatus: WorkflowStageStatus = reportSummary
      ? reportSummary.passed ? 'completed' : 'failed'
      : wsEvaluating
        ? 'running'
        : aiRunning || chatMessages.length > 0
          ? 'running'
          : 'pending'

    return [
      {
        key: 'target',
        title: '创建目标沙箱',
        detail: workspaceStatus?.targetSandboxId ? `目标 ${shortSandboxId(workspaceStatus.targetSandboxId)}` : '先创建被评估模板沙箱并拿到 gatewayEndpoint',
        status: resolveStageStatus(stepMap.get('target_sandbox')),
      },
      {
        key: 'evaluator',
        title: '创建评估沙箱',
        detail: workspaceStatus?.evaluatorSandboxId ? `评估 ${shortSandboxId(workspaceStatus.evaluatorSandboxId)}` : '创建最终与用户交互的评估沙箱',
        status: resolveStageStatus(stepMap.get('evaluator_sandbox')),
      },
      {
        key: 'materials',
        title: '装载模板与材料',
        detail: materialsReady ? '题卡、本体、模板材料已进入评估沙箱' : '上传评估技能包、目标模板和评估材料',
        status: mergeStageStatus([
          stepMap.get('upload_skill'),
          stepMap.get('upload_employee_template'),
          stepMap.get('upload_artifacts'),
          materialsReady ? 'completed' : stepMap.get('materials'),
        ]),
      },
      {
        key: 'questions',
        title: '展示题卡与标准',
        detail: questionCards.length > 0 ? `已生成 ${questionCards.length} 张题卡` : '左侧会话展示评估阶段、题目和补充说明',
        status: questionCards.length > 0 ? 'completed' : materialsReady ? 'running' : 'pending',
      },
      {
        key: 'execution',
        title: '执行评分与报告',
        detail: reportSummary ? `第 ${reportSummary.iteration} 轮，综合 ${reportSummary.overallScore} 分` : wsEvaluating ? (wsProgress || '正在驱动目标沙箱执行') : '通过聊天或 WS 触发正式评估',
        status: executionStatus,
      },
    ]
  }, [aiRunning, chatMessages.length, materialsReady, questionCards.length, reportSummary, workspaceStatus, wsEvaluating, wsProgress])

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

            <section className="hb-card eval-flow-panel px-4 py-4">
              <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
                <div className="min-w-0 flex-1 overflow-x-auto pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
                  <div className="flex min-w-[980px]">
                    {workflowStages.map((stage, index) => {
                      const tone = workflowStageTone(stage.status)
                      const textTone = workflowStageTextTone(stage.status)
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
                            <div className="flex items-center">
                              <div className={`eval-flow-step-node ${tone}`}>
                                {renderWorkflowStageMarker(stage.status, index + 1)}
                              </div>
                              {index < workflowStages.length - 1 && (
                                <div className={`eval-flow-step-line ${connectorTone}`} />
                              )}
                            </div>
                            <div className="mt-3 pr-4">
                              <div className="text-[12px] font-semibold leading-5 eval-text-title">{stage.title}</div>
                              <div className={`mt-1 text-[11px] font-medium leading-4 ${textTone}`}>
                                {workflowStagePill(stage.status)}
                              </div>
                            </div>
                          </div>
                        </div>
                      )
                    })}
                  </div>
                </div>

                <div className="flex shrink-0 flex-wrap items-center gap-2 xl:ml-6">
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
                    className="eval-flow-primary-btn"
                    onClick={() => void submitAiDecision('RUN')}
                  >
                    {wsEvaluating ? <Loader2 size={13} className="animate-spin" /> : <CheckCircle2 size={13} />}
                    {wsEvaluating ? (wsProgress || '执行中...') : '执行评估'}
                  </button>
                </div>
              </div>

              <div className="mt-4 flex flex-wrap items-center gap-0 border-t eval-chat-footer pt-3 text-[11px]">
                {workspaceReady && (
                  <span className="eval-flow-status-item eval-flow-status-ok">双沙箱已连接</span>
                )}
                <span className={`eval-flow-status-item ${sandboxConnected ? 'eval-flow-status-connected' : 'eval-flow-status-muted'}`}>
                  会话{sandboxConnected ? '已连接' : '未连接'}
                </span>
                {workspaceStatus?.sessionId && (
                  <span className="eval-flow-status-item eval-flow-status-session">
                    <span className="eval-flow-status-label">Session</span>
                    <span className="font-mono eval-text-title">{shortSessionId(workspaceStatus.sessionId)}</span>
                    <button
                      type="button"
                      className="eval-flow-copy-btn"
                      onClick={handleCopySessionId}
                      title={sessionCopied ? '已复制' : '复制 Session'}
                    >
                      {sessionCopied ? <Check size={12} /> : <Copy size={12} />}
                    </button>
                  </span>
                )}
                {workspaceProgressSummary?.errorMessage && (
                  <span className="eval-flow-status-item eval-flow-status-error">
                    {workspaceProgressSummary.errorMessage}
                  </span>
                )}
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
                      <div className="text-base font-semibold eval-text-title">评估对话主视图</div>
                      <div className="text-[12px] leading-5 eval-text-secondary">以聊天为主，右侧只保留题卡、轨迹和报告。</div>
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
            <div className="flex flex-1 flex-col overflow-hidden eval-chat-bg px-5 py-4">
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
              <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-[28px] border eval-chat-wrapper shadow-[0_20px_60px_rgba(15,23,42,0.06)]">
                {!aiRunning ? (
                  <div className="m-4 rounded-2xl border eval-inactive-tip px-4 py-3 text-sm leading-6">
                    请先点击“准备评估环境”。环境就绪后，这里会成为主聊天入口，你可以直接和评估沙箱对话，再结合右侧题卡、轨迹和报告辅助判断。
                  </div>
                ) : (
                  <>
                    {testcaseOutlines.length > 0 && (
                      <div className="shrink-0 border-b eval-chat-footer px-5 py-2.5">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="text-[12px] font-medium eval-text-green-mid">✓ 测试用例已就绪</span>
                          <span className="rounded-full border eval-stats-badge px-2 py-0.5 text-[11px]">
                            {testcaseOutlines.length} 个场景
                          </span>
                          {testcaseOutlines.slice(0, 3).map((outline) => (
                            <span key={outline.testcaseId} className="rounded-full border eval-pill-neutral px-2 py-0.5 text-[11px] truncate max-w-[160px]">
                              {outline.title}
                            </span>
                          ))}
                          {testcaseOutlines.length > 3 && (
                            <button
                              type="button"
                              className="rounded-full border eval-pill-neutral px-2 py-0.5 text-[11px] text-[var(--hb-blue)] hover:bg-[var(--hb-blue)]/10 transition-colors"
                              onClick={() => {
                                setRightCollapsed(false)
                                setArtifactTab('testcase')
                              }}
                            >
                              +{testcaseOutlines.length - 3} 查看全部 →
                            </button>
                          )}
                        </div>
                      </div>
                    )}
                    <div className="flex-1 space-y-3 overflow-y-auto px-5 py-4">
                      {chatLoading ? (
                        <div className="flex items-center gap-2 text-sm text-[var(--hb-soft)]">
                          <Loader2 size={14} className="animate-spin" />
                          正在加载评估沙箱对话...
                        </div>
                      ) : chatMessages.length === 0 ? (
                        <div className="rounded-2xl border border-dashed eval-empty-chat px-4 py-4 text-sm leading-6">
                          暂无对话。你可以先让评估沙箱解释当前题卡、给出执行计划，或者要求它开始一次完整评估。
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
                      <div ref={chatEndRef} />
                    </div>
                    <div className="border-t eval-chat-footer px-4 py-4">
                      <div className="flex items-end gap-2">
                        <textarea
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
                          className="min-h-[72px] flex-1 resize-y rounded-2xl border eval-textarea px-4 py-3.5 text-sm leading-6 outline-none disabled:opacity-60"
                        />
                        <button
                          type="button"
                          disabled={chatSending || !chatInput.trim()}
                          className="hb-btn-primary !px-4 !py-3.5 disabled:!bg-[#d4d4d8]"
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
                <div className="flex border-b eval-tab-bar">
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
                      className={`flex flex-1 items-center justify-center gap-1 border-b-2 py-2 text-xs font-medium ${
                        artifactTab === tab.key ? 'eval-tab-active' : 'eval-tab-inactive'
                      }`}
                    >
                      <tab.icon size={11} />
                      {tab.label}
                    </button>
                  ))}
                  <button
                    type="button"
                    onClick={() => setRightCollapsed(true)}
                    className="ml-auto px-2 py-2 text-[var(--hb-caption)] transition-colors hover:text-[var(--hb-soft)]"
                  >
                    <ChevronDown size={14} className="rotate-90" />
                  </button>
                </div>

                <div className="flex-1 overflow-y-auto p-3 text-xs">
                  {artifactTab === 'overview' && (
                    <div className="space-y-3">
                      <div className="rounded-2xl border eval-stats-header p-4">
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <div className="text-[11px] font-semibold uppercase tracking-[0.08em] eval-text-indigo-label">Summary Report</div>
                            <div className="mt-1 text-base font-semibold eval-text-title">AI 评估结论</div>
                            <div className="mt-1 text-[11px] leading-relaxed eval-text-secondary">布局参考原型的总结面板，但仅展示当前后端真实返回的数据。</div>
                          </div>
                          <div className="rounded-2xl border eval-score-card px-4 py-3 text-center shadow-sm">
                            <div className="text-3xl font-bold tabular-nums eval-text-title">{reportSummary?.overallScore ?? '--'}</div>
                            <div className="mt-1 text-[11px] eval-text-secondary">综合评分</div>
                          </div>
                        </div>
                        <div className="mt-3 grid grid-cols-2 gap-2">
                          {reportMetrics.map((metric) => (
                            <div key={metric.label} className="rounded-xl border eval-score-card px-3 py-2.5">
                              <div className="text-[11px] eval-text-secondary">{metric.label}</div>
                              <div className={`mt-1 text-sm font-semibold ${metric.tone}`}>{metric.value}</div>
                            </div>
                          ))}
                        </div>
                      </div>

                      <div className="rounded-xl border eval-overview-panel p-3">
                        <div className="mb-2 flex items-center gap-1.5 font-semibold text-[var(--hb-body)]">
                          <BarChart2 size={11} />
                          维度评分对比
                        </div>
                        {dimensionScores.length === 0 ? (
                          <div className="text-[11px] text-[var(--hb-soft)]">当前报告未返回维度明细。</div>
                        ) : (
                          <div className="overflow-hidden rounded-lg border eval-table-row-border">
                            <table className="w-full border-collapse text-left text-[11px]">
                              <thead className="eval-table-header">
                                <tr>
                                  <th className="px-3 py-2 font-medium">维度</th>
                                  <th className="px-3 py-2 font-medium">得分</th>
                                  <th className="px-3 py-2 font-medium">说明</th>
                                </tr>
                              </thead>
                              <tbody>
                                {dimensionScores.map((item) => (
                                  <tr key={item.dimension} className="border-t eval-table-row-border align-top">
                                    <td className="px-3 py-2 font-medium eval-text-title">{item.dimension}</td>
                                    <td className="px-3 py-2 tabular-nums eval-text-title">{item.score}</td>
                                    <td className="px-3 py-2 eval-text-secondary">{item.comment || '--'}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        )}
                      </div>

                      <div className="rounded-xl border eval-overview-panel p-3">
                        <div className="mb-2 flex items-center gap-1.5 font-semibold text-[var(--hb-body)]">
                          <FileText size={11} />
                          报告与建议
                        </div>
                        {reportSummary ? (
                          <div className="space-y-2 text-[11px] text-[var(--hb-soft)]">
                            <div>生成时间：{formatDateTime(reportSummary.createdAtUtc)}</div>
                            <div>评估轮次：第 {reportSummary.iteration} 轮</div>
                            <div className="flex flex-wrap gap-2">
                              {reportJsonUrl && (
                                <a
                                  href={reportJsonUrl}
                                  target="_blank"
                                  rel="noreferrer"
                                  className="inline-flex items-center gap-1 eval-link"
                                >
                                  <ExternalLink size={10} />
                                  查看报告 JSON
                                </a>
                              )}
                              {reportHtmlUrl && (
                                <a
                                  href={reportHtmlUrl}
                                  download={`evaluation-report-${reportSummary.reportId}.html`}
                                  target="_blank"
                                  rel="noreferrer"
                                  className="inline-flex items-center gap-1 eval-link"
                                >
                                  <ExternalLink size={10} />
                                  下载报告 HTML
                                </a>
                              )}
                            </div>
                          </div>
                        ) : (
                          <div className="text-[11px] text-[var(--hb-soft)]">暂无评估报告，请先执行评估。</div>
                        )}
                        <div className="mt-3 rounded-lg border eval-recommendation px-3 py-2.5 text-[11px] leading-relaxed">
                          {evaluation.recommendation}
                        </div>
                      </div>

                      <div className="rounded-xl border border-dashed eval-debug-panel p-3">
                        <div className="mb-1 text-[11px] font-semibold eval-text-blue">调试信息</div>
                        <div className="space-y-1 text-[10px] font-mono leading-relaxed text-[var(--hb-soft)]">
                          <div>target: {workspaceStatus?.targetSandboxId ?? '--'} | {workspaceStatus?.targetGatewayEndpoint ?? '--'}</div>
                          <div>evaluator: {workspaceStatus?.evaluatorSandboxId ?? '--'} | {workspaceStatus?.evaluatorGatewayEndpoint ?? '--'}</div>
                          <div>session: {workspaceStatus?.sessionId ?? '--'}</div>
                        </div>
                      </div>
                    </div>
                  )}

                  {artifactTab === 'testcase' && (
                    <div className="space-y-2">
                      {/* 测试场景概览（与聊天区域上方一致） */}
                      {testcaseOutlines.length > 0 && (
                        <div className="overflow-hidden rounded-2xl border eval-scenario-list shadow-[0_1px_3px_rgba(0,0,0,0.04)]">
                          <div className="px-3 pb-2.5 pt-3">
                            <div className="mb-2 text-[11px] font-semibold eval-text-title">
                              测试场景（{testcaseOutlines.length} 个）
                            </div>
                            <div className="space-y-1.5">
                              {testcaseOutlines.map((outline) => (
                                <div key={outline.testcaseId} className="rounded-lg border eval-step-row px-2.5 py-2">
                                  <div className="flex items-start justify-between gap-2">
                                    <div className="flex min-w-0 items-start gap-1.5">
                                      <span className="mt-1 h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--hb-text-green)]" />
                                      <div className="min-w-0">
                                        <div className="text-[11px] font-medium eval-text-body">{outline.title}</div>
                                        {outline.userRequest && (
                                          <div className="mt-0.5 text-[10px] leading-relaxed eval-text-secondary">{outline.userRequest}</div>
                                        )}
                                      </div>
                                    </div>
                                    <span className="shrink-0 text-[10px] font-mono eval-text-caption">{outline.testcaseId}</span>
                                  </div>
                                </div>
                              ))}
                            </div>
                          </div>
                          <div className="border-t eval-scenario-footer px-3 py-2">
                            <span className="text-[10px] font-medium eval-text-green-mid">✓ 用例已就绪，可开始评估</span>
                          </div>
                        </div>
                      )}
                      {!workspaceReady ? (
                        <div className="rounded-xl border eval-empty-card px-3 py-3 text-[11px]">
                          请先完成沙箱初始化流程，随后展示测试用例。
                        </div>
                      ) : !materialsReady ? (
                        <div className="rounded-xl border eval-empty-card px-3 py-3 text-[11px]">
                          素材未就绪，等待完成“加载评估素材”。
                        </div>
                      ) : questionCards.length === 0 ? (
                        <div className="rounded-xl border eval-empty-card px-3 py-3 text-[11px]">
                          暂无测试用例。
                        </div>
                      ) : (
                        <>
                          {/* 统计头 */}
                          <div className="rounded-2xl border eval-stats-header p-3">
                            <div className="flex flex-wrap items-center gap-2 text-[11px]">
                              <span className="rounded-full border eval-stats-badge px-2.5 py-1">
                                题卡数量：{questionCards.length}
                              </span>
                              <span className={`rounded-full border px-2.5 py-1 ${testcaseReady ? 'eval-stats-badge-ready' : 'eval-stats-badge-pending'}`}>
                                {testcaseReady ? '✓ 用例已就绪' : '等待就绪'}
                              </span>
                              {ontologyReady && (
                                <span className="rounded-full border eval-stats-badge-ontology px-2.5 py-1">
                                  本体已就绪
                                </span>
                              )}
                            </div>
                          </div>

                          {/* 逐个展开的题卡完整详情 */}
                          {questionCards.map((card, index) => (
                            <div key={`${card.testcaseId}_${index}`} className="rounded-2xl border eval-question-card p-3 shadow-[0_1px_3px_rgba(0,0,0,0.04)]">
                              {/* 标题行 */}
                              <div className="flex items-start justify-between gap-2">
                                <div className="flex min-w-0 items-center gap-2">
                                  <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full eval-seq-circle text-[10px] font-semibold">
                                    {index + 1}
                                  </span>
                                  <span className="truncate text-[12px] font-semibold eval-text-title">
                                    {card.title || card.testcaseId}
                                  </span>
                                </div>
                                <span className="shrink-0 rounded-full border eval-id-badge px-2 py-0.5 text-[10px] font-mono">
                                  {card.testcaseId}
                                </span>
                              </div>

                              {/* 来源文件 */}
                              {card.sourceFile && (
                                <div className="mt-1.5 flex items-center gap-1 text-[10px] eval-text-caption">
                                  <FileText size={9} />
                                  <span className="truncate">{card.sourceFile}</span>
                                </div>
                              )}

                              {/* 提示词（完整展示不截断） */}
                              {card.prompt && (
                                <div className="mt-2 rounded-xl border eval-prompt-box px-3 py-2.5 text-[11px] leading-relaxed">
                                  {card.prompt}
                                </div>
                              )}

                              {/* 评估步骤（展开列表） */}
                              {card.steps.length > 0 && (
                                <div className="mt-2 space-y-1.5">
                                  <div className="text-[10px] font-semibold uppercase tracking-[0.06em] eval-text-caption">
                                    评估步骤（{card.steps.length}）
                                  </div>
                                  {card.steps.map((step, stepIndex) => (
                                    <div
                                      key={`${card.testcaseId}_step_${stepIndex}`}
                                      className="flex gap-2 rounded-lg border eval-step-row px-2.5 py-2"
                                    >
                                      <span className="flex h-4 w-4 shrink-0 items-center justify-center rounded-full eval-seq-circle text-[9px] font-semibold">
                                        {stepIndex + 1}
                                      </span>
                                      <div className="text-[11px] leading-relaxed eval-text-body-2">{step}</div>
                                    </div>
                                  ))}
                                </div>
                              )}

                              {/* 评分提示（完整展示） */}
                              {card.scoringHint && (
                                <div className="mt-2 rounded-xl border border-dashed eval-scoring-hint px-3 py-2 text-[11px] leading-relaxed">
                                  <span className="font-semibold eval-text-body-2">评分提示：</span>
                                  {card.scoringHint}
                                </div>
                              )}
                            </div>
                          ))}
                        </>
                      )}
                    </div>
                  )}

                  {artifactTab === 'trace' && (
                    <div className="space-y-2">
                      {traceAssets.length === 0 ? (
                        <div className="rounded-xl border eval-empty-card px-3 py-3 text-[11px]">
                          暂无执行轨迹，请先执行评估。
                        </div>
                      ) : (
                        <>
                          <div className="rounded-2xl border eval-stats-header p-3">
                            <div className="flex flex-wrap items-center gap-2 text-[11px]">
                              <span className="rounded-full border eval-stats-badge px-2.5 py-1">Trace 数量：{traceAssets.length}</span>
                              <span className="rounded-full border eval-stats-badge px-2.5 py-1">最新更新时间：{formatDateTime(traceAssets[0]?.createdAtUtc)}</span>
                            </div>
                          </div>
                          {traceAssets.map((asset, index) => {
                            const traceLink = toAbsoluteApiUrl(asset.publicUrl)
                            const relatedScenario = evaluation.scenarios.find(
                              (item) => item.scenarioId === asset.relatedKey || item.scenarioName === asset.relatedKey,
                            )
                            return (
                              <div key={asset.relativePath} className="rounded-xl border eval-trace-card p-2.5">
                                <div className="mb-1 flex items-center gap-1.5">
                                  <Zap size={10} className="eval-text-brand" />
                                  <span className="font-semibold text-[var(--hb-body)]">
                                    轨迹 #{index + 1} {asset.relatedKey ? `路 ${asset.relatedKey}` : ''}
                                  </span>
                                </div>
                                <div className="text-[11px] text-[var(--hb-soft)]">创建时间：{formatDateTime(asset.createdAtUtc)}</div>
                                {relatedScenario && (
                                  <div className="mt-1 text-[11px] text-[var(--hb-soft)]">
                                    场景：{relatedScenario.scenarioName} 路 判定：{verdictLabel(relatedScenario.verdict)}
                                  </div>
                                )}
                                <div className="mt-1 break-all text-[11px] eval-text-caption">{asset.relativePath}</div>
                                {traceLink && (
                                  <a
                                    href={traceLink}
                                    target="_blank"
                                    rel="noreferrer"
                                    className="mt-2 inline-flex items-center gap-1 text-[11px] eval-link"
                                  >
                                    <ExternalLink size={10} />
                                    查看原始 Trace JSON
                                  </a>
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
                        <div className="rounded-xl border eval-empty-card px-3 py-3 text-[11px]">
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
                            <div className="rounded-xl border eval-overview-panel p-3">
                              <div className="mb-2 text-[11px] font-semibold text-[var(--hb-body)]">维度评分明细</div>
                              <div className="space-y-2">
                                {dimensionScores.map((item) => (
                                  <div key={item.dimension} className="rounded-lg border eval-dim-item px-3 py-2">
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
                            </div>
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

