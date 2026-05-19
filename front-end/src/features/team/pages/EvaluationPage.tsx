import { useEffect, useMemo, useRef, useState } from 'react'
import {
  AlertCircle,
  BarChart2,
  CheckCircle2,
  ChevronDown,
  ExternalLink,
  FileText,
  Loader2,
  MessageCircle,
  PlayCircle,
  SendHorizontal,
  Zap,
} from 'lucide-react'
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
import { Breadcrumb } from '@/shared/components/Breadcrumb'
import SessionListPanel from '@/features/team/components/SessionListPanel'
import { HiringToolStepsBlock } from '@/features/hiring/pages/components/HiringToolStepsBlock'
import type { ToolStep } from '@/features/hiring/pages/hiringPageTypes'
import { instanceBasePath } from '@/shared/utils/instancePath'

/** 评估页面本地消息类型（在 HiringConversationMessage 基础上增加工具调用步骤） */
type EvalChatMessage = HiringConversationMessage & { toolSteps?: ToolStep[] }

type ArtifactTab = 'overview' | 'testcase' | 'trace' | 'report'
type WorkflowStageStatus = 'pending' | 'running' | 'completed' | 'failed'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5280'

function toAbsoluteApiUrl(path?: string | null): string | null {
  if (!path) return null
  const trimmed = path.trim()
  if (!trimmed) return null
  if (/^https?:\/\//i.test(trimmed)) return trimmed
  return new URL(trimmed.startsWith('/') ? trimmed : `/${trimmed}`, API_BASE_URL).toString()
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
    case 'completed':
      return 'border-[#dcfce7] bg-[#f0fdf4] text-[#166534]'
    case 'running':
      return 'border-[#bfdbfe] bg-[#eff6ff] text-[#1d4ed8]'
    case 'failed':
      return 'border-[#fecdd3] bg-[#fff1f2] text-[#be123c]'
    default:
      return 'border-[#ececec] bg-white text-[#737373]'
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

  const [rightCollapsed, setRightCollapsed] = useState(true)
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
  const showWorkspaceProgress =
    workspacePolling || (!!workspaceStatus && workspaceStatus.overallStatus !== 'not_started' && workspaceStatus.overallStatus !== 'ready')

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

  // AI 评估已通过 或 已进入人工复核阶段，可跳转到人工评估页
  const canNavigateToHumanEval =
    reportSummary?.passed === true ||
    employee?.evalPhase === 'pending_human_review' ||
    employee?.evalPhase === 'pending_onboarding' ||
    employee?.evalPhase === 'pending_onboarding_force'
  const humanEvalPath = id ? `${instanceBasePath(location.pathname, id)}/human-evaluation` : null

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

  const primaryQuestionCard = questionCards[0] ?? null
  const reportMetrics = useMemo(() => ([
    {
      label: '会话 ID',
      value: shortSessionId(workspaceStatus?.sessionId ?? reportSummary?.reportId ?? null),
      tone: 'text-[#4f46e5]',
    },
    {
      label: '题卡数量',
      value: `${questionCards.length}`,
      tone: 'text-[#0f766e]',
    },
    {
      label: 'Trace 产物',
      value: `${traceAssets.length}`,
      tone: 'text-[#b45309]',
    },
    {
      label: '材料状态',
      value: materialsReady ? '已就绪' : '待补齐',
      tone: materialsReady ? 'text-[#15803d]' : 'text-[#b91c1c]',
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
      // 3 秒后自动取消二次确认状态
      setTimeout(() => setResetConfirm(false), 3000)
      return
    }
    setResetConfirm(false)
    setResetting(true)
    setError('')
    try {
      await api.employeeRuntime.resetEvaluationData(id)
      setWorkspaceStatus(null)
      setWorkspacePolling(false)
      setChatMessages([])
      setEvaluation(null)
      await loadData()
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '清理评估数据失败')
    } finally {
      setResetting(false)
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
        <div className="hb-card flex min-h-[220px] items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载 AI 评估...
        </div>
      </div>
    )
  }

  if (!employee || !evaluation) {
    return (
      <div className="hb-page">
        <div className="hb-card p-8 text-sm text-[#737373]">评估数据不存在</div>
      </div>
    )
  }

  return (
    <div className="hb-page hb-page-wide">
      <Breadcrumb items={[{ label: '员工详情', to: id ? instanceBasePath(location.pathname, id) : '/department-employees' }, { label: 'AI 评估' }]} />
      <div className="flex h-[calc(100vh-116px)] min-h-[680px] flex-col gap-3">
        <section className="hb-card p-2.5">
          <div className="flex flex-wrap items-center gap-2">
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-1.5">
                <h1 className="text-[16px] font-semibold text-[#0a0a0a]">AI 评估对话</h1>
                <span className="rounded-full border border-[#e5e7eb] bg-[#f9fafb] px-2 py-0.5 text-[10px] text-[#4b5563]">
                  {employee.nickname} 路 {employee.roleName}
                </span>
              </div>
              <div className="mt-1.5 flex flex-wrap gap-1.5 text-[10px]">
                <span className={`rounded-full border px-2 py-0.5 ${testcaseReady ? 'border-[#dcfce7] bg-[#f0fdf4] text-[#166534]' : 'border-[#fecdd3] bg-[#fff1f2] text-[#be123c]'}`}>
                  测试用例：{testcaseReady ? '已就绪' : '待补充'}
                </span>
                <span className={`rounded-full border px-2 py-0.5 ${ontologyReady ? 'border-[#dcfce7] bg-[#f0fdf4] text-[#166534]' : 'border-[#fecdd3] bg-[#fff1f2] text-[#be123c]'}`}>
                  评估本体：{ontologyReady ? '已就绪' : '待补充'}
                </span>
                <span className="rounded-full border border-[#e5e7eb] bg-white px-2 py-0.5 text-[#4b5563]">
                  Session：{shortSessionId(workspaceStatus?.sessionId)}
                </span>
                {workspaceReady && (
                  <span className="rounded-full border border-[#bfdbfe] bg-[#eff6ff] px-2 py-0.5 text-[#1d4ed8]">
                    双沙箱已连接
                  </span>
                )}
              </div>
            </div>
            <div className="ml-auto flex flex-wrap items-center gap-1.5">
              <button
                type="button"
                disabled={submitting || !canPrepare || aiRunning}
                className="hb-btn-primary !px-2.5 !py-1 !text-[11px]"
                onClick={() => void submitAiDecision('START')}
              >
                <PlayCircle size={12} />
                准备评估环境
              </button>
              <button
                type="button"
                disabled={submitting || wsEvaluating || !aiRunning}
                className="hb-btn-ghost !px-2.5 !py-1 !text-[11px]"
                onClick={() => void submitAiDecision('RUN')}
              >
                {wsEvaluating ? <Loader2 size={12} className="animate-spin" /> : <CheckCircle2 size={12} />}
                {wsEvaluating ? (wsProgress || 'WS 评估中...') : '执行评估'}
              </button>
              <button
                type="button"
                disabled={resetting || submitting}
                className={`!px-2.5 !py-1 !text-[11px] ${resetConfirm ? 'hb-btn-danger' : 'hb-btn-ghost'}`}
                onClick={() => void handleResetEvaluationData()}
                title="清理当前评估数据（工作区状态、会话记录、报告），便于重新走评估流程"
              >
                {resetting ? <Loader2 size={12} className="animate-spin" /> : <AlertCircle size={12} />}
                {resetting ? '清理中...' : resetConfirm ? '确认清理？' : '清理评估数据'}
              </button>
              <button
                type="button"
                onClick={() => setRightCollapsed((current) => !current)}
                className="hb-btn-ghost !px-2.5 !py-1 !text-[11px]"
              >
                <BarChart2 size={12} />
                {rightCollapsed ? '展开辅助面板' : '收起辅助面板'}
              </button>
            </div>
          </div>

          {error && (
            <div className="mt-3 rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-3 py-2 text-xs text-[#b3263c]">
              <span className="inline-flex items-center gap-1.5">
                <AlertCircle size={12} />
                {error}
              </span>
            </div>
          )}

          {showWorkspaceProgress && workspaceProgressSummary && (
            <div className="mt-2 rounded-xl border border-[#ececec] bg-[#fafafa] px-2.5 py-2">
              <div className="flex flex-wrap items-center gap-2 text-[10px]">
                <span className={`rounded-full px-2 py-0.5 font-medium ${workspaceProgressSummary.failed ? 'bg-[#fff1f2] text-[#b3263c]' : 'bg-[#e8edff] text-[#4a6cf7]'}`}>
                  {workspaceProgressSummary.label}
                </span>
                <span className="text-[#737373]">
                  {workspaceProgressSummary.completed}/{workspaceProgressSummary.total} 步
                </span>
                {workspaceProgressSummary.errorMessage && (
                  <span className="truncate text-[#b3263c]">{workspaceProgressSummary.errorMessage}</span>
                )}
              </div>
              <div className="mt-1.5 h-1 w-full rounded-full bg-[#efefef]">
                <div
                  className={`h-1 rounded-full transition-all duration-500 ${workspaceProgressSummary.failed ? 'bg-[#b3263c]' : 'bg-[#4a6cf7]'}`}
                  style={{ width: `${workspaceProgressSummary.percent}%` }}
                />
              </div>
            </div>
          )}

          <div className="mt-2 flex gap-1.5 overflow-x-auto whitespace-nowrap pb-0.5 text-[10px] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
            {workflowStages.map((stage, index) => (
              <span key={stage.key} className={`inline-flex shrink-0 items-center gap-1 rounded-full border px-2 py-0.5 ${workflowStageTone(stage.status)}`}>
                <span className="font-semibold">0{index + 1}</span>
                <span>{stage.title}</span>
                <span className="rounded-full bg-white/80 px-1.5 py-0.5 text-[10px]">{workflowStagePill(stage.status)}</span>
              </span>
            ))}
          </div>
        </section>

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
            <div className="border-b border-[#ececec] px-5 py-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <div className="flex h-9 w-9 items-center justify-center rounded-2xl bg-[#eef2ff] text-[#4f46e5]">
                      <MessageCircle size={18} />
                    </div>
                    <div>
                      <div className="text-base font-semibold text-[#111827]">评估对话主视图</div>
                      <div className="text-[12px] leading-5 text-[#6b7280]">以聊天为主，右侧只保留题卡、轨迹和报告。</div>
                    </div>
                  </div>
                </div>
                <div className="flex flex-wrap gap-2 text-[11px]">
                  <span className={`rounded-full border px-2.5 py-1 ${sandboxConnected ? 'border-[#dcfce7] bg-[#f0fdf4] text-[#166534]' : 'border-[#e5e7eb] bg-white text-[#737373]'}`}>
                    会话连接：{sandboxConnected ? '已连接' : '未连接'}
                  </span>
                  {selectedSessionId && (
                    <span className="rounded-full border border-[#e5e7eb] bg-white px-2.5 py-1 text-[#4b5563]">
                      当前会话：{shortSessionId(selectedSessionId)}
                    </span>
                  )}
                </div>
              </div>
            </div>
            <div className="flex flex-1 flex-col overflow-hidden bg-[#fafafa] px-5 py-4">
              {/* AI 评估通过横幅：提示用户进入人工评估环节 */}
              {canNavigateToHumanEval && humanEvalPath && (
                <div className="mb-3 flex shrink-0 items-center justify-between gap-3 rounded-2xl border border-[#bbf7d0] bg-[#f0fdf4] px-4 py-3 shadow-sm">
                  <div className="flex items-center gap-2.5 text-sm font-medium text-[#166534]">
                    <CheckCircle2 size={16} className="shrink-0 text-[#16a34a]" />
                    <span>
                      AI 评估已通过
                      {reportSummary?.overallScore != null && `（综合评分 ${reportSummary.overallScore} 分）`}
                      ，可进入人工评估环节
                    </span>
                  </div>
                  <button
                    type="button"
                    className="hb-btn-primary shrink-0 !px-3 !py-1.5 !text-[12px]"
                    onClick={() => navigate(humanEvalPath)}
                  >
                    进入人工评估 →
                  </button>
                </div>
              )}
              <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-[28px] border border-[#ececec] bg-white shadow-[0_20px_60px_rgba(15,23,42,0.06)]">
                {!aiRunning ? (
                  <div className="m-4 rounded-2xl border border-[#ececec] bg-[#fafafa] px-4 py-3 text-sm leading-6 text-[#737373]">
                    请先点击“准备评估环境”。环境就绪后，这里会成为主聊天入口，你可以直接和评估沙箱对话，再结合右侧题卡、轨迹和报告辅助判断。
                  </div>
                ) : (
                  <>
                    {testcaseOutlines.length > 0 && (
                      <div className="shrink-0 border-b border-[#ececec] bg-white px-5 py-4">
                        <div className="overflow-hidden rounded-2xl border border-[#e5e7eb] bg-white shadow-[0_1px_3px_rgba(0,0,0,0.04)]">
                          <div className="px-4 pb-3 pt-4">
                            <div className="mb-3 text-[13px] font-semibold text-[#111827]">
                              测试场景（{testcaseOutlines.length} 个）
                            </div>
                            <div className="space-y-2.5">
                              {testcaseOutlines.map((outline) => (
                                <div key={outline.testcaseId} className="flex items-center justify-between gap-3">
                                  <div className="flex min-w-0 items-center gap-2.5">
                                    <span className="h-2 w-2 shrink-0 rounded-full bg-[#16a34a]" />
                                    <span className="truncate text-[13px] text-[#374151]">{outline.title}</span>
                                  </div>
                                  <span className="shrink-0 text-[12px] font-medium text-[#16a34a]">✓ 1 用例</span>
                                </div>
                              ))}
                            </div>
                          </div>
                          <div className="border-t border-[#f3f4f6] px-4 py-2.5">
                            <span className="text-[12px] font-medium text-[#16a34a]">✓ 用例已就绪，可开始评估</span>
                          </div>
                        </div>
                      </div>
                    )}
                    <div className="flex-1 space-y-3 overflow-y-auto px-5 py-4">
                      {chatLoading ? (
                        <div className="flex items-center gap-2 text-sm text-[#737373]">
                          <Loader2 size={14} className="animate-spin" />
                          正在加载评估沙箱对话...
                        </div>
                      ) : chatMessages.length === 0 ? (
                        <div className="rounded-2xl border border-dashed border-[#d1d5db] bg-[#fafafa] px-4 py-4 text-sm leading-6 text-[#737373]">
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
                                      ? 'bg-[#000000] text-white'
                                      : 'border border-[#ececec] bg-[#fafafa] text-[#404040]'
                                  }`}
                                >
                                  <div className={`mb-1 text-[11px] ${isUser ? 'text-[#e5e5e5]' : 'text-[#9ca3af]'}`}>
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
                              <div className="rounded-2xl border border-[#ececec] bg-[#fafafa] px-3 py-2.5 text-sm leading-6 text-[#404040]">
                                <div className="mb-1 text-[11px] text-[#9ca3af]">评估沙箱 · 正在回复</div>
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
                    <div className="border-t border-[#ececec] px-4 py-4">
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
                          className="min-h-[72px] flex-1 resize-y rounded-2xl border border-[#e5e5e5] bg-[#fafafa] px-4 py-3.5 text-sm leading-6 outline-none focus:border-[#4a6cf7] disabled:opacity-60"
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
                <div className="mt-2 rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-2.5 py-1.5 text-[11px] text-[#b3263c]">
                  {chatError}
                </div>
              )}
              {sessionSwitching && (
                <div className="mt-2 rounded-xl border border-[#dbeafe] bg-[#eff6ff] px-2.5 py-1.5 text-[11px] text-[#1d4ed8]">
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
                className="flex h-full w-full items-center justify-center transition-colors hover:bg-[#fafafa]"
              >
                <ChevronDown size={16} className="-rotate-90 text-[#9ca3af]" />
              </button>
            ) : (
              <>
                <div className="flex border-b border-[#ececec]">
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
                        artifactTab === tab.key ? 'border-[#0a0a0a] text-[#0a0a0a]' : 'border-transparent text-[#737373]'
                      }`}
                    >
                      <tab.icon size={11} />
                      {tab.label}
                    </button>
                  ))}
                  <button
                    type="button"
                    onClick={() => setRightCollapsed(true)}
                    className="ml-auto px-2 py-2 text-[#9ca3af] transition-colors hover:text-[#404040]"
                  >
                    <ChevronDown size={14} className="rotate-90" />
                  </button>
                </div>

                <div className="flex-1 overflow-y-auto p-3 text-xs">
                  {artifactTab === 'overview' && (
                    <div className="space-y-3">
                      <div className="rounded-2xl border border-[#e5e7eb] bg-[linear-gradient(180deg,#ffffff_0%,#f8fafc_100%)] p-4">
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <div className="text-[11px] font-semibold uppercase tracking-[0.08em] text-[#4f46e5]">Summary Report</div>
                            <div className="mt-1 text-base font-semibold text-[#111827]">AI 评估结论</div>
                            <div className="mt-1 text-[11px] leading-relaxed text-[#6b7280]">布局参考原型的总结面板，但仅展示当前后端真实返回的数据。</div>
                          </div>
                          <div className="rounded-2xl border border-[#e5e7eb] bg-white px-4 py-3 text-center shadow-sm">
                            <div className="text-3xl font-bold tabular-nums text-[#111827]">{reportSummary?.overallScore ?? '--'}</div>
                            <div className="mt-1 text-[11px] text-[#6b7280]">综合评分</div>
                          </div>
                        </div>
                        <div className="mt-3 grid grid-cols-2 gap-2">
                          {reportMetrics.map((metric) => (
                            <div key={metric.label} className="rounded-xl border border-[#e5e7eb] bg-white px-3 py-2.5">
                              <div className="text-[11px] text-[#6b7280]">{metric.label}</div>
                              <div className={`mt-1 text-sm font-semibold ${metric.tone}`}>{metric.value}</div>
                            </div>
                          ))}
                        </div>
                      </div>

                      <div className="rounded-xl border border-[#ececec] bg-white p-3">
                        <div className="mb-2 flex items-center gap-1.5 font-semibold text-[#404040]">
                          <BarChart2 size={11} />
                          维度评分对比
                        </div>
                        {dimensionScores.length === 0 ? (
                          <div className="text-[11px] text-[#737373]">当前报告未返回维度明细。</div>
                        ) : (
                          <div className="overflow-hidden rounded-lg border border-[#f3f4f6]">
                            <table className="w-full border-collapse text-left text-[11px]">
                              <thead className="bg-[#f8fafc] text-[#6b7280]">
                                <tr>
                                  <th className="px-3 py-2 font-medium">维度</th>
                                  <th className="px-3 py-2 font-medium">得分</th>
                                  <th className="px-3 py-2 font-medium">说明</th>
                                </tr>
                              </thead>
                              <tbody>
                                {dimensionScores.map((item) => (
                                  <tr key={item.dimension} className="border-t border-[#f3f4f6] align-top">
                                    <td className="px-3 py-2 font-medium text-[#111827]">{item.dimension}</td>
                                    <td className="px-3 py-2 tabular-nums text-[#111827]">{item.score}</td>
                                    <td className="px-3 py-2 text-[#6b7280]">{item.comment || '--'}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        )}
                      </div>

                      <div className="rounded-xl border border-[#ececec] bg-white p-3">
                        <div className="mb-2 flex items-center gap-1.5 font-semibold text-[#404040]">
                          <FileText size={11} />
                          报告与建议
                        </div>
                        {reportSummary ? (
                          <div className="space-y-2 text-[11px] text-[#737373]">
                            <div>生成时间：{formatDateTime(reportSummary.createdAtUtc)}</div>
                            <div>评估轮次：第 {reportSummary.iteration} 轮</div>
                            <div className="flex flex-wrap gap-2">
                              {reportJsonUrl && (
                                <a
                                  href={reportJsonUrl}
                                  target="_blank"
                                  rel="noreferrer"
                                  className="inline-flex items-center gap-1 text-[#2563eb]"
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
                                  className="inline-flex items-center gap-1 text-[#2563eb]"
                                >
                                  <ExternalLink size={10} />
                                  下载报告 HTML
                                </a>
                              )}
                            </div>
                          </div>
                        ) : (
                          <div className="text-[11px] text-[#737373]">暂无评估报告，请先执行评估。</div>
                        )}
                        <div className="mt-3 rounded-lg border border-[#f3f4f6] bg-[#fafafa] px-3 py-2.5 text-[11px] leading-relaxed text-[#6b7280]">
                          {evaluation.recommendation}
                        </div>
                      </div>

                      <div className="rounded-xl border border-dashed border-[#dbeafe] bg-[#f8fbff] p-3">
                        <div className="mb-1 text-[11px] font-semibold text-[#1d4ed8]">调试信息</div>
                        <div className="space-y-1 text-[10px] font-mono leading-relaxed text-[#64748b]">
                          <div>target: {workspaceStatus?.targetSandboxId ?? '--'} | {workspaceStatus?.targetGatewayEndpoint ?? '--'}</div>
                          <div>evaluator: {workspaceStatus?.evaluatorSandboxId ?? '--'} | {workspaceStatus?.evaluatorGatewayEndpoint ?? '--'}</div>
                          <div>session: {workspaceStatus?.sessionId ?? '--'}</div>
                        </div>
                      </div>
                    </div>
                  )}

                  {artifactTab === 'testcase' && (
                    <div className="space-y-2">
                      {!workspaceReady ? (
                        <div className="rounded-xl border border-[#ececec] bg-white px-3 py-3 text-[11px] text-[#737373]">
                          请先完成沙箱初始化流程，随后展示测试用例。
                        </div>
                      ) : !materialsReady ? (
                        <div className="rounded-xl border border-[#ececec] bg-white px-3 py-3 text-[11px] text-[#737373]">
                          素材未就绪，等待完成“加载评估素材”。
                        </div>
                      ) : questionCards.length === 0 ? (
                        <div className="rounded-xl border border-[#ececec] bg-white px-3 py-3 text-[11px] text-[#737373]">
                          暂无测试用例。
                        </div>
                      ) : (
                        <>
                          {primaryQuestionCard && (
                            <div className="rounded-2xl border border-[#e5e7eb] bg-[linear-gradient(180deg,#ffffff_0%,#fafafa_100%)] p-3">
                              <div className="flex items-start justify-between gap-2">
                                <div>
                                  <div className="text-[11px] font-semibold uppercase tracking-[0.08em] text-[#4f46e5]">当前题卡</div>
                                  <div className="mt-1 text-sm font-semibold text-[#111827]">{primaryQuestionCard.title || primaryQuestionCard.testcaseId}</div>
                                </div>
                                <div className="rounded-full border border-[#e5e7eb] bg-white px-2 py-0.5 text-[10px] font-mono text-[#6b7280]">{primaryQuestionCard.testcaseId}</div>
                              </div>
                              <div className="mt-3 rounded-xl border border-[#ececec] bg-white px-3 py-2.5 text-[11px] leading-relaxed text-[#4b5563]">
                                {primaryQuestionCard.prompt}
                              </div>
                              <div className="mt-3 space-y-2">
                                {primaryQuestionCard.steps.map((step, index) => (
                                  <div key={`${primaryQuestionCard.testcaseId}_step_${index}`} className="flex gap-2 rounded-xl border border-[#ececec] bg-white px-3 py-2.5">
                                    <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-[#eef2ff] text-[10px] font-semibold text-[#4f46e5]">{index + 1}</span>
                                    <div className="text-[11px] leading-relaxed text-[#4b5563]">{step}</div>
                                  </div>
                                ))}
                              </div>
                              {primaryQuestionCard.scoringHint && (
                                <div className="mt-3 rounded-xl border border-dashed border-[#d1d5db] bg-[#f9fafb] px-3 py-2 text-[11px] leading-relaxed text-[#6b7280]">
                                  评分提示：{primaryQuestionCard.scoringHint}
                                </div>
                              )}
                            </div>
                          )}

                          {questionCards.map((card, index) => (
                            <div key={`${card.testcaseId}_${index}`} className="rounded-xl border border-[#ececec] bg-white p-3">
                              <div className="mb-1 flex items-center gap-1.5">
                                <span className="font-mono text-[11px] text-[#9ca3af]">#{index + 1}</span>
                                <span className="font-medium text-[#0a0a0a]">{card.title || card.testcaseId}</span>
                              </div>
                              <div className="text-[11px] text-[#737373]">ID: {card.testcaseId}</div>
                              {card.prompt && (
                                <div className="mt-1.5 text-[11px] leading-relaxed text-[#525252] line-clamp-3">{card.prompt}</div>
                              )}
                              <div className="mt-2 flex flex-wrap items-center gap-2 text-[11px]">
                                {card.steps.length > 0 && (
                                  <span className="rounded-full border border-[#ececec] bg-[#fafafa] px-2 py-0.5 text-[#737373]">
                                    {card.steps.length} 个步骤
                                  </span>
                                )}
                                {card.scoringHint && (
                                  <span className="rounded-full border border-[#ececec] bg-[#fafafa] px-2 py-0.5 text-[#737373]">
                                    评分提示
                                  </span>
                                )}
                                {card.sourceFile && (
                                  <span className="truncate text-[#9ca3af] max-w-[180px]">{card.sourceFile}</span>
                                )}
                              </div>
                            </div>
                          ))}
                        </>
                      )}
                    </div>
                  )}

                  {artifactTab === 'trace' && (
                    <div className="space-y-2">
                      {traceAssets.length === 0 ? (
                        <div className="rounded-xl border border-[#ececec] bg-white px-3 py-3 text-[11px] text-[#737373]">
                          暂无执行轨迹，请先执行评估。
                        </div>
                      ) : (
                        <>
                          <div className="rounded-2xl border border-[#e5e7eb] bg-[linear-gradient(180deg,#ffffff_0%,#f8fafc_100%)] p-3">
                            <div className="flex flex-wrap items-center gap-2 text-[11px] text-[#64748b]">
                              <span className="rounded-full border border-[#e2e8f0] bg-white px-2.5 py-1">Trace 数量：{traceAssets.length}</span>
                              <span className="rounded-full border border-[#e2e8f0] bg-white px-2.5 py-1">最新更新时间：{formatDateTime(traceAssets[0]?.createdAtUtc)}</span>
                            </div>
                          </div>
                          {traceAssets.map((asset, index) => {
                            const traceLink = toAbsoluteApiUrl(asset.publicUrl)
                            const relatedScenario = evaluation.scenarios.find(
                              (item) => item.scenarioId === asset.relatedKey || item.scenarioName === asset.relatedKey,
                            )
                            return (
                              <div key={asset.relativePath} className="rounded-xl border border-[#ececec] bg-[#fafafa] p-2.5">
                                <div className="mb-1 flex items-center gap-1.5">
                                  <Zap size={10} className="text-[#4a6cf7]" />
                                  <span className="font-semibold text-[#404040]">
                                    轨迹 #{index + 1} {asset.relatedKey ? `路 ${asset.relatedKey}` : ''}
                                  </span>
                                </div>
                                <div className="text-[11px] text-[#737373]">创建时间：{formatDateTime(asset.createdAtUtc)}</div>
                                {relatedScenario && (
                                  <div className="mt-1 text-[11px] text-[#737373]">
                                    场景：{relatedScenario.scenarioName} 路 判定：{verdictLabel(relatedScenario.verdict)}
                                  </div>
                                )}
                                <div className="mt-1 break-all text-[11px] text-[#9ca3af]">{asset.relativePath}</div>
                                {traceLink && (
                                  <a
                                    href={traceLink}
                                    target="_blank"
                                    rel="noreferrer"
                                    className="mt-2 inline-flex items-center gap-1 text-[11px] text-[#2563eb]"
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
                        <div className="rounded-xl border border-[#ececec] bg-white px-3 py-3 text-[11px] text-[#737373]">
                          暂无评估报告，请先执行评估。
                        </div>
                      ) : (
                        <>
                          {/* 整体结论 */}
                          <div className={`rounded-2xl border p-4 ${reportSummary.passed ? 'border-[#bbf7d0] bg-[#f0fdf4]' : 'border-[#fecdd3] bg-[#fff1f2]'}`}>
                            <div className="flex items-center justify-between gap-3">
                              <div>
                                <div className={`text-[11px] font-semibold uppercase tracking-[0.08em] ${reportSummary.passed ? 'text-[#16a34a]' : 'text-[#be123c]'}`}>
                                  {reportSummary.passed ? '✓ 评估通过' : '✗ 评估未通过'}
                                </div>
                                <div className="mt-1 text-[11px] text-[#6b7280]">
                                  第 {reportSummary.iteration} 轮 · {formatDateTime(reportSummary.createdAtUtc)}
                                </div>
                              </div>
                              <div className="rounded-xl border border-[#e5e7eb] bg-white px-3 py-2 text-center shadow-sm">
                                <div className="text-2xl font-bold tabular-nums text-[#111827]">{reportSummary.overallScore}</div>
                                <div className="text-[10px] text-[#6b7280]">综合评分</div>
                              </div>
                            </div>
                          </div>

                          {/* 维度评分 */}
                          {dimensionScores.length > 0 && (
                            <div className="rounded-xl border border-[#ececec] bg-white p-3">
                              <div className="mb-2 text-[11px] font-semibold text-[#404040]">维度评分明细</div>
                              <div className="space-y-2">
                                {dimensionScores.map((item) => (
                                  <div key={item.dimension} className="rounded-lg border border-[#f3f4f6] bg-[#fafafa] px-3 py-2">
                                    <div className="flex items-center justify-between gap-2">
                                      <span className="text-[11px] font-medium text-[#111827]">{item.dimension}</span>
                                      <span className="tabular-nums text-[11px] font-semibold text-[#4f46e5]">{item.score}</span>
                                    </div>
                                    {item.comment && (
                                      <div className="mt-1 text-[10px] leading-relaxed text-[#6b7280]">{item.comment}</div>
                                    )}
                                  </div>
                                ))}
                              </div>
                            </div>
                          )}

                          {/* 报告文件链接 */}
                          <div className="rounded-xl border border-[#ececec] bg-white p-3">
                            <div className="mb-2 text-[11px] font-semibold text-[#404040]">报告文件</div>
                            <div className="flex flex-col gap-2">
                              {reportJsonUrl && (
                                <a href={reportJsonUrl} target="_blank" rel="noreferrer"
                                  className="inline-flex items-center gap-1.5 text-[11px] text-[#2563eb]">
                                  <ExternalLink size={10} /> 查看报告 JSON
                                </a>
                              )}
                              {reportHtmlUrl && (
                                <a href={reportHtmlUrl} download={`evaluation-report-${reportSummary.reportId}.html`}
                                  target="_blank" rel="noreferrer"
                                  className="inline-flex items-center gap-1.5 text-[11px] text-[#2563eb]">
                                  <ExternalLink size={10} /> 下载报告 HTML
                                </a>
                              )}
                            </div>
                          </div>

                          {/* 通过时显示跳转按钮 */}
                          {reportSummary.passed && humanEvalPath && (
                            <button
                              type="button"
                              className="hb-btn-primary w-full !py-2 !text-[12px]"
                              onClick={() => navigate(humanEvalPath)}
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
    </div>
  )
}

