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
import { useLocation, useParams } from 'react-router-dom'
import { tokenService } from '@/infra/auth/token-service'
import { GatewayWs, type GatewayMessage } from '@/infra/sandbox/gateway-ws'
import { fetchSandboxSessionMessages, type SandboxMessage } from '@/infra/sandbox/sandbox-api'
import {
  api,
  type EmployeeDetail,
  type EvaluationSandboxConnectionResult,
  type EvaluationSandboxConversationState,
  type EvaluationState,
  type EvaluationVerdictPayload,
  type EvaluationWorkspaceStatus,
  type HiringConversationMessage,
} from '@/infra/api'
import { Breadcrumb } from '@/shared/components/Breadcrumb'
import { EvaluationWorkspaceProgress } from '@/features/team/components/EvaluationWorkspaceProgress'
import SessionListPanel from '@/features/team/components/SessionListPanel'
import { instanceBasePath } from '@/shared/utils/instancePath'

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

function shortEndpoint(value?: string | null) {
  if (!value) return '--'
  const normalized = value.replace(/^https?:\/\//i, '')
  return normalized.length <= 42 ? normalized : `${normalized.slice(0, 24)}...${normalized.slice(-14)}`
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

function mapSandboxMessages(messages: SandboxMessage[]): HiringConversationMessage[] {
  return messages
    .filter((message) => message.type === 'user_message' || message.type === 'assistant_message')
    .map((message, index) => ({
      messageId: `${message.type}-${index}-${String(message.createdAt ?? Date.now())}`,
      role: message.type === 'user_message' ? 'user' : 'assistant',
      content: String(message.content ?? message.text ?? '').trim(),
      createdAt: String(message.createdAt ?? new Date().toISOString()),
    }))
    .filter((message) => message.content.length > 0)
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

  const location = useLocation();

  const [chatMessages, setChatMessages] = useState<HiringConversationMessage[]>([])
  const [chatInput, setChatInput] = useState('')
  const [chatLoading, setChatLoading] = useState(false)
  const [chatSending, setChatSending] = useState(false)
  const [chatError, setChatError] = useState('')
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null)
  const [sessionListRefreshKey, setSessionListRefreshKey] = useState(0)
  const [sandboxConnected, setSandboxConnected] = useState(false)
  const [sessionSwitching, setSessionSwitching] = useState(false)
  const [streamingContent, setStreamingContent] = useState<string | null>(null)
  const [, setSandboxConversation] = useState<EvaluationSandboxConversationState | null>(null)
  const [wsEvaluating, setWsEvaluating] = useState(false)
  const [wsProgress, setWsProgress] = useState('')
  const chatEndRef = useRef<HTMLDivElement | null>(null)
  const wsRef = useRef<GatewayWs | null>(null)
  const gatewayEndpointRef = useRef<string | null>(null)
  const sessionIdRef = useRef<string | null>(null)
  const streamingContentRef = useRef('')
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

  const overview = useMemo(() => {
    if (!evaluation) {
      return { total: 0, passed: 0, failed: 0, pending: 0, score: 0 }
    }

    const total = evaluation.scenarios.length
    const passed = evaluation.scenarios.filter((scenario) => scenario.verdict === 'passed').length
    const failed = evaluation.scenarios.filter((scenario) => scenario.verdict === 'failed').length
    const pending = total - passed - failed
    const score = evaluation.latestReport?.overallScore ?? 0
    return { total, passed, failed, pending, score }
  }, [evaluation])

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
  const traceAssets = (evaluation?.assetRefs ?? [])
    .filter((asset) => asset.assetType === 'trace-json')
    .slice(0, 8)
  const materialsReady = evaluation?.readiness?.status === 'ready'
  const reportSummary = evaluation?.latestReport ?? null
  const reportJsonUrl = toAbsoluteApiUrl(reportSummary?.reportJsonUrl ?? null)
  const reportHtmlUrl = toAbsoluteApiUrl(reportSummary?.reportHtmlUrl ?? null)
  const dimensionScores = reportSummary?.dimensionScores ?? []
  const readinessMessage = evaluation?.readiness?.message ?? '等待评估材料检查'
  const testcaseReady = evaluation?.readiness?.testcasesReady ?? false
  const ontologyReady = evaluation?.readiness?.ontologyReady ?? false

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

  const connectionCards = useMemo(() => ([
    {
      key: 'target',
      title: '目标沙箱',
      idLabel: shortSandboxId(workspaceStatus?.targetSandboxId),
      runtimeLabel: shortSessionId(workspaceStatus?.targetRuntimeId),
      endpoint: shortEndpoint(workspaceStatus?.targetGatewayEndpoint),
      description: '先创建，用于拿到被评估数字人的通讯地址。',
    },
    {
      key: 'evaluator',
      title: '评估沙箱',
      idLabel: shortSandboxId(workspaceStatus?.evaluatorSandboxId),
      runtimeLabel: shortSessionId(workspaceStatus?.evaluatorRuntimeId),
      endpoint: shortEndpoint(workspaceStatus?.evaluatorGatewayEndpoint),
      description: '最终和用户聊天、展示题卡、生成报告的评估专家沙箱。',
    },
  ]), [workspaceStatus])

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
        streamingContentRef.current = ''
        setStreamingContent('')
        return
      }

      if (messageType === 'text_delta' || messageType === 'assistant_chunk') {
        const chunk = String(msg.delta ?? msg.chunk ?? msg.content ?? msg.text ?? '')
        streamingContentRef.current += chunk
        setStreamingContent(streamingContentRef.current)
        return
      }

      if (messageType === 'typing_stop' || messageType === 'assistant_done') {
        const endpointValue = gatewayEndpointRef.current
        const sessionIdValue = sessionIdRef.current
        setStreamingContent(null)
        streamingContentRef.current = ''
        if (endpointValue && sessionIdValue) {
          void syncSandboxHistory(endpointValue, sessionIdValue)
            .then(() => setSessionListRefreshKey((current) => current + 1))
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
        let endpoint = workspaceStatus?.evaluatorGatewayEndpoint?.trim() ?? gatewayEndpointRef.current ?? ''
        let sessionId = workspaceStatus?.sessionId?.trim() ?? sessionIdRef.current ?? ''

        if (!endpoint) {
          const latestStatus = await api.employeeRuntime.getEvaluationWorkspaceStatus(id)
          setWorkspaceStatus(latestStatus)
          endpoint = latestStatus.evaluatorGatewayEndpoint?.trim() ?? ''
          sessionId = sessionId || latestStatus.sessionId?.trim() || ''
        }

        if (!sessionId) {
          const conversation = await api.employeeRuntime.getEvaluationSandboxConversation(id)
          setSandboxConversation(conversation)
          sessionId = conversation.sessionId?.trim() ?? ''
          setWorkspaceStatus((prev) => prev ? { ...prev, sessionId } : prev)
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

  async function submitAiDecision(decision: 'START' | 'RUN') {
    if (!id) return
    setSubmitting(true)
    setError('')
    logEvaluationDebug('submit ai decision', { employeeId: id, decision })

    try {
      // RUN: WebSocket direct evaluation flow
      if (decision === 'RUN') {
        await api.employeeRuntime.submitAiEvaluationDecision(id, { decision })
        const connection = await api.employeeRuntime.getSandboxConnection(id)
        logEvaluationDebug('sandbox connection ready', {
          employeeId: id,
          sessionId: connection.sessionId,
          targetSandboxId: connection.targetSandboxId,
          targetGatewayEndpoint: connection.targetGatewayEndpoint,
          evaluatorSandboxId: connection.evaluatorSandboxId,
          evaluatorGatewayEndpoint: connection.gatewayEndpoint,
        })
        await runWsEvaluation(connection)
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

  async function runWsEvaluation(connection: EvaluationSandboxConnectionResult) {
    if (!id) return
    setWsEvaluating(true)
    setWsProgress('正在连接评估沙箱...')
    setError('')

    const wsUrl = connection.gatewayEndpoint.trim()
    const token = connection.sandboxToken
    const ws = new GatewayWs(wsUrl, token)
    logEvaluationDebug('run ws evaluation start', {
      employeeId: id,
      sessionId: connection.sessionId,
      targetSandboxId: connection.targetSandboxId,
      targetGatewayEndpoint: connection.targetGatewayEndpoint,
      evaluatorSandboxId: connection.evaluatorSandboxId,
      evaluatorGatewayEndpoint: wsUrl,
    })

    try {
      // Connect and wait for open
      await new Promise<void>((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error('WebSocket connection timeout')), 30000)
        ws.onStateChange = (state) => {
          if (state === 'open') { clearTimeout(timeout); resolve() }
          if (state === 'error' || state === 'closed') { clearTimeout(timeout); reject(new Error(`WebSocket ${state}`)) }
        }
        ws.connect()
      })

      setWsProgress('已连接，正在发送评估数据...')
      logEvaluationDebug('ws connected', {
        employeeId: id,
        sessionId: connection.sessionId,
        evaluatorGatewayEndpoint: wsUrl,
      })

      // Build evaluation message
      const payloadText = connection.evaluationPayloadJson ??
        JSON.stringify({
          session_id: connection.sessionId,
          target_hire_id: connection.targetHireId,
          instruction: `You are the AI evaluation expert (ai-evaluation skill). The evaluation payload data was not pre-built by the backend.
Please use your available tools (evaluation_score and evaluation_generate_report) to help complete the evaluation.
If evaluation data (testcases, traces, ontology) is missing, respond with a clear message indicating what specific data is needed.
Otherwise, use the available evaluation tools to score based on whatever data has been provided in this conversation.`
        })

      // Send and wait for verdict
      const verdictPromise = new Promise<GatewayMessage>((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error('Evaluation timeout (5 min)')), 300000)
        let accumulated = ''
        ws.onMessage = (msg) => {
          if (msg.type === 'assistant_chunk' && typeof msg.text === 'string') {
            accumulated += msg.text
            setWsProgress(`正在评估... 已接收 ${accumulated.length} 字符`)
          }
          if (msg.type === 'assistant_done') {
            clearTimeout(timeout)
            resolve({ ...msg, text: accumulated || (msg.text as string) })
          }
          if (msg.type === 'error') {
            clearTimeout(timeout)
            reject(new Error((msg.text as string) || 'Evaluator sandbox returned an error'))
          }
        }
      })

      ws.send({
        type: 'user_message',
        text: payloadText,
        messageId: `eval-${crypto.randomUUID ? crypto.randomUUID() : Date.now()}`,
      })

      const resultMsg = await verdictPromise
      setWsProgress('评估完成，正在保存结果...')

      // Parse verdict — use brace counting to handle { } inside string values
      const rawText = (resultMsg.text as string) || ''

      function extractJson(text: string): string | null {
        // Try markdown code fence first: ```json ... ```
        const fenceMatch = text.match(/```json\s*([\s\S]*?)```/)
        if (fenceMatch) return fenceMatch[1].trim()

        // Find the first { and count braces to find the matching }
        const start = text.indexOf('{')
        if (start < 0) return null
        let depth = 0
        let inString = false
        let escape = false
        for (let i = start; i < text.length; i++) {
          const ch = text[i]
          if (escape) { escape = false; continue }
          if (ch === '\\') { escape = true; continue }
          if (ch === '"') { inString = !inString; continue }
          if (inString) continue
          if (ch === '{') { depth++ }
          else if (ch === '}') { depth--; if (depth === 0) return text.substring(start, i + 1) }
        }
        return null
      }

      let verdict: EvaluationVerdictPayload
      const json = extractJson(rawText)
      if (json) {
        try {
          const parsed = JSON.parse(json)
          verdict = {
            verdict: parsed.verdict || 'FAIL',
            overallScore: parsed.overall_score ?? 0,
            summary: parsed.summary || '',
            dimensionScores: (parsed.dimension_scores || []).map((d: Record<string, unknown>) => ({
              dimension: (d.dimension as string) || '',
              score: (d.score as number) || 0,
              comment: (d.comment as string) || '',
              evidenceRefs: (d.evidence_refs as string[]) || [],
            })),
          }
        } catch {
          verdict = {
            verdict: 'FAIL',
            overallScore: 0,
            summary: `JSON parse error: ${rawText.substring(0, 200)}`,
            dimensionScores: [],
          }
        }
      } else {
        verdict = {
          verdict: 'FAIL',
          overallScore: 0,
          summary: `Failed to parse verdict: ${rawText.substring(0, 200)}`,
          dimensionScores: [],
        }
      }

      // Sync verdict back to backend
      const syncResult = await api.employeeRuntime.syncVerdict(id, {
        sessionId: connection.sessionId,
        verdict,
      })
      logEvaluationDebug('verdict synced', {
        employeeId: id,
        sessionId: connection.sessionId,
        verdict: verdict.verdict,
        overallScore: verdict.overallScore,
      })
      setEmployee((prev) => prev ? { ...prev, status: syncResult.status as EmployeeDetail['status'] } : prev)
      setError('')

      const evaluationState = await api.employeeRuntime.getEvaluationState(id)
      setEvaluation(evaluationState)
    } catch (wsError: unknown) {
      logEvaluationDebug('ws evaluation failed', wsError)
      setError(wsError instanceof Error ? wsError.message : 'WebSocket evaluation failed')
    } finally {
      ws.disconnect()
      logEvaluationDebug('ws disconnected', {
        employeeId: id,
        sessionId: connection.sessionId,
      })
      setWsEvaluating(false)
      setWsProgress('')
      setSubmitting(false)
    }
  }

  async function sendEvaluatorMessage() {
    if (!id || chatSending) return
    const content = chatInput.trim()
    if (!content) return

    const optimistic: HiringConversationMessage = {
      messageId: `local_${Date.now()}`,
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    }

    setChatInput('')
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
                <span className="rounded-full border border-[#e5e7eb] bg-white px-2 py-0.5 text-[10px] text-[#6b7280]">
                  {employee.stageSummary || '待发起 AI 评估'}
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

          {showWorkspaceProgress && (
            <div className="mt-2">
              <EvaluationWorkspaceProgress status={workspaceStatus} polling={workspacePolling} />
            </div>
          )}

          <div className="mt-2 flex flex-wrap gap-1.5 text-[10px]">
            {workflowStages.map((stage, index) => (
              <span key={stage.key} className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 ${workflowStageTone(stage.status)}`}>
                <span className="font-semibold">0{index + 1}</span>
                <span>{stage.title}</span>
                <span className="rounded-full bg-white/80 px-1.5 py-0.5 text-[10px]">{workflowStagePill(stage.status)}</span>
              </span>
            ))}
          </div>

          <div className="mt-1 text-[10px] leading-5 text-[#737373]">{readinessMessage}</div>
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
                      <div className="text-[12px] leading-5 text-[#6b7280]">以聊天驱动评估，右侧面板只负责补充题卡、轨迹和报告。</div>
                    </div>
                  </div>
                </div>
                <div className="flex flex-wrap gap-2 text-[11px]">
                  {connectionCards.map((card) => (
                    <span key={card.key} className="rounded-full border border-[#e5e7eb] bg-white px-2.5 py-1 text-[#4b5563]">
                      {card.title}：{card.idLabel}
                    </span>
                  ))}
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
              <div className="mt-3 flex flex-wrap gap-2 text-[11px]">
                <span className="rounded-full border border-[#e5e7eb] bg-[#f9fafb] px-2.5 py-1 text-[#4b5563]">
                  场景总数：<span className="font-semibold tabular-nums text-[#0a0a0a]">{overview.total}</span>
                </span>
                <span className="rounded-full border border-[#dcfce7] bg-[#f0fdf4] px-2.5 py-1 text-[#166534]">
                  通过：<span className="font-semibold tabular-nums">{overview.passed}</span>
                </span>
                <span className="rounded-full border border-[#fce7f3] bg-[#fdf2f8] px-2.5 py-1 text-[#9d174d]">
                  未通过：<span className="font-semibold tabular-nums">{overview.failed}</span>
                </span>
                <span className="rounded-full border border-[#fef3c7] bg-[#fffbeb] px-2.5 py-1 text-[#92400e]">
                  待判定：<span className="font-semibold tabular-nums">{overview.pending}</span>
                </span>
                <span className="rounded-full border border-[#e5e7eb] bg-white px-2.5 py-1 text-[#4b5563]">
                  综合评分：<span className="font-semibold tabular-nums text-[#0a0a0a]">{overview.score}</span>
                </span>
              </div>
            </div>
            <div className="flex-1 overflow-hidden bg-[#fafafa] px-5 py-4">
              <div className="flex h-full flex-col overflow-hidden rounded-[28px] border border-[#ececec] bg-white shadow-[0_20px_60px_rgba(15,23,42,0.06)]">
                {!aiRunning ? (
                  <div className="m-4 rounded-2xl border border-[#ececec] bg-[#fafafa] px-4 py-3 text-sm leading-6 text-[#737373]">
                    请先点击“准备评估环境”。环境就绪后，这里会成为主聊天入口，你可以直接和评估沙箱对话，再结合右侧题卡、轨迹和报告辅助判断。
                  </div>
                ) : (
                  <>
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
                              <div
                                className={`max-w-[94%] rounded-2xl px-3 py-2.5 text-sm leading-6 ${
                                  isUser
                                    ? 'bg-[#000000] text-white'
                                    : 'border border-[#ececec] bg-[#fafafa] text-[#404040]'
                                }`}
                              >
                                <div className={`mb-1 text-[11px] ${isUser ? 'text-[#e5e5e5]' : 'text-[#9ca3af]'}`}>
                                  {isUser ? '你' : '评估沙箱'} 路 {formatDateTime(message.createdAt)}
                                </div>
                                <div className="whitespace-pre-wrap break-words">{message.content}</div>
                              </div>
                            </div>
                          )
                        })
                      )}
                      {streamingContent !== null && (
                        <div className="flex justify-start">
                          <div className="max-w-[94%] rounded-2xl border border-[#ececec] bg-[#fafafa] px-3 py-2.5 text-sm leading-6 text-[#404040]">
                            <div className="mb-1 text-[11px] text-[#9ca3af]">评估沙箱 · 正在回复</div>
                            <div className="whitespace-pre-wrap break-words">{streamingContent || '...'}</div>
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
                </div>
              </>
            )}
          </div>
        </section>
      </div>
    </div>
  )
}


