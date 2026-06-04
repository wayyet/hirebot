import { useEffect, useMemo, useRef, useState } from 'react'
import {
  AlertCircle,
  Loader2,
  X,
} from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { tokenService } from '@/infra/auth/token-service'
import { GatewayWs } from '@/infra/sandbox/gateway-ws'
import { fetchAdminSessions, fetchSandboxSessionMessages } from '@/infra/sandbox/sandbox-api'
import {
  api,
  type EmployeeDetail,
  type EvaluationState,
  type EvaluationWorkspaceStatus,
  type HiringConversationMessage,
} from '@/infra/api'
import { Breadcrumb } from '@/shared/components/Breadcrumb'
import { TYPEWRITER_SOFT_FINISH_DEFER_MS, useTypewriterStream } from '@/shared/hooks/useTypewriterStream'
import SessionListPanel from '@/features/team/components/SessionListPanel'
import type { ToolStep } from '@/features/hiring/pages/hiringPageTypes'
import { instanceBasePath } from '@/shared/utils/instancePath'

import type { ArtifactTab, EvalChatMessage, TraceJsonData, WorkflowStage, WorkflowStageStatus } from './evaluation/evaluationTypes'
import {
  shortSandboxId,
  shortSessionId,
  resolveStageStatus,
  mergeStageStatus,
  findCurrentWorkflowStageIndex,
  logEvaluationDebug,
  mapSandboxMessages,
} from './evaluation/evaluationUtils'
import { EvalAutoInitScreen } from './evaluation/EvalAutoInitScreen'
import { EvalSandboxInitOverlay } from './evaluation/EvalSandboxInitOverlay'
import { EvalWorkflowPanel } from './evaluation/EvalWorkflowPanel'
import { EvalChatPanel } from './evaluation/EvalChatPanel'
import { EvalArtifactsPanel } from './evaluation/EvalArtifactsPanel'

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
  const {
    displayText: streamingContent,
    start: startTypewriterStream,
    append: appendTypewriterStream,
    finish: finishTypewriterStream,
    reset: resetTypewriterStream,
  } = useTypewriterStream()
  const [streamingToolSteps, setStreamingToolSteps] = useState<ToolStep[]>([])
  const [chatTyping, setChatTyping] = useState(false)
  // sandboxConversation state 已移除，session ID 改为直接从网关查询
  const wsEvaluating = false
  const wsProgress = ''
  const [resetting, setResetting] = useState(false)
  const [resetConfirm, setResetConfirm] = useState(false)
  const [sessionCopied, setSessionCopied] = useState(false)
  const wsRef = useRef<GatewayWs | null>(null)
  const gatewayEndpointRef = useRef<string | null>(null)
  const sessionIdRef = useRef<string | null>(null)
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
  const [artifactRefreshKey, setArtifactRefreshKey] = useState(0)

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
  // hiring（雇佣中）状态不允许进入评估页面，需要先完成雇佣流程（ImportPackage）
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
  const testcaseCount = testcaseItems.length
  const traceAssets = (evaluation?.assetRefs ?? [])
    .filter((asset) => asset.assetType === 'trace-json')
    .slice(0, 8)
  const materialsReady = evaluation?.readiness?.status === 'ready'
  const reportSummary = evaluation?.latestReport ?? null

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

  const workflowStages = useMemo<WorkflowStage[]>(() => {
    const stepMap = new Map((workspaceStatus?.steps ?? []).map((step) => [step.step, step.status]))
    // 保持原有四步流程语义：材料阶段以测试用例就绪作为完成标志
    const materialsStageStatus: WorkflowStageStatus = testcaseCount > 0
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
        detail: testcaseCount > 0
          ? `测试用例已就绪（${testcaseCount} 个场景）`
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
    testcaseCount,
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
      value: `${testcaseCount}`,
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
  ]), [materialsReady, testcaseCount, reportSummary?.reportId, traceAssets.length, workspaceStatus?.sessionId])

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
    // assetRefs 中没有 trace-json 时跳过，避免向后端发出必然 404 的请求
    if (traceAssets.length === 0) return
    if (!expandedTraceUrls.includes(sessionId)) {
      setExpandedTraceUrls(prev => [...prev, sessionId])
      void loadTraceContent(sessionId)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [artifactTab, evaluation?.sessionId, traceAssets.length])

  useEffect(() => {
    if (!id || artifactRefreshKey === 0) return

    let cancelled = false
    let timer: number | undefined
    let attempts = 0
    const maxAttempts = 120

    async function refreshEvaluationAssets() {
      if (cancelled) return
      attempts += 1
      try {
        const nextState = await api.employeeRuntime.getEvaluationState(id!)
        if (!cancelled) {
          setEvaluation(nextState)
        }
      } catch {
        // Keep polling; transient sync lag should not break the evaluation chat.
      }

      if (!cancelled && attempts < maxAttempts) {
        timer = window.setTimeout(refreshEvaluationAssets, 5000)
      }
    }

    timer = window.setTimeout(refreshEvaluationAssets, 3000)
    return () => {
      cancelled = true
      if (timer !== undefined) {
        window.clearTimeout(timer)
      }
    }
  }, [artifactRefreshKey, id])

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
    resetTypewriterStream()
    streamingToolStepsRef.current = []
    setStreamingToolSteps([])
    setChatTyping(false)
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
        if (state === 'closed' || state === 'error') {
          resetTypewriterStream()
          streamingToolStepsRef.current = []
          setStreamingToolSteps([])
          setChatTyping(false)
        }
      }
    })

    ws.onMessage = (msg) => {
      const messageType = String(msg.type ?? '')

      if (messageType === 'typing_start') {
        // 新轮次开始：重置流式内容和工具步骤
        streamingToolStepsRef.current = []
        startTypewriterStream()
        setStreamingToolSteps([])
        setChatTyping(true)
        return
      }

      if (messageType === 'text_delta' || messageType === 'assistant_chunk') {
        const chunk = String(msg.delta ?? msg.chunk ?? msg.content ?? msg.text ?? '')
        appendTypewriterStream(chunk)
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
        const fallbackReply = String(msg.content ?? msg.text ?? '')
        const finishOptions = messageType === 'typing_stop'
          ? { deferMs: TYPEWRITER_SOFT_FINISH_DEFER_MS }
          : undefined
        finishTypewriterStream(fallbackReply, () => {
          setStreamingToolSteps([])
          setChatTyping(false)
          streamingToolStepsRef.current = []
          if (endpointValue && sessionIdValue) {
            void syncSandboxHistory(endpointValue, sessionIdValue)
              .then(async () => {
                // 把本轮工具步骤附加到最后一条 bot 消息。
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
                // 每轮对话结束后刷新评估状态，检查是否有新报告产出。
                if (id) {
                  try {
                    const [evalState, employeeState] = await Promise.all([
                      api.employeeRuntime.getEvaluationState(id),
                      api.employeeRuntime.getEmployee(id),
                    ])
                    setEvaluation(evalState)
                    setEmployee(employeeState)
                    setArtifactRefreshKey((current) => current + 1)
                  } catch {
                    // 刷新失败不影响主流程
                  }
                }
              })
              .catch((historyError: unknown) => {
                setChatError(historyError instanceof Error ? historyError.message : '同步评估沙箱历史失败')
              })
          }
        }, finishOptions)
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

        // WS 会话 id 由沙箱侧分配，与 eval session id 不同；优先复用已有值，否则直接向网关查询
        let sessionId = sessionIdRef.current ?? ''
        if (!sessionId) {
          // 直接向网关 admin/sessions 取最新会话，避免后端存储的历史 session ID 因过期而触发 404
          const adminResp = await fetchAdminSessions(endpoint, { page: 1, pageSize: 1 })
          const latestSession = adminResp.active[0] ?? adminResp.persisted.items[0]
          sessionId = latestSession?.id?.trim() ?? ''
          // 网关无会话时兜底走后端接口（首次创建沙箱的边界情况）
          if (!sessionId) {
            const conversation = await api.employeeRuntime.getEvaluationSandboxConversation(id)
            sessionId = conversation.sessionId?.trim() ?? ''
          }
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
      setArtifactRefreshKey(0)
      setTraceDataCache({})
      setExpandedTraceUrls([])
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
        setArtifactRefreshKey((current) => current + 1)
        setTraceDataCache({})
        setExpandedTraceUrls([])
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

      startTypewriterStream()

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
      resetTypewriterStream()
      streamingToolStepsRef.current = []
      setStreamingToolSteps([])
      setChatTyping(false)
    } finally {
      setChatSending(false)
    }
  }

  async function loadTraceContent(sessionId: string) {
    // 已在加载中或已成功缓存时跳过；'error' 状态允许重试
    if (traceDataCache[sessionId] && traceDataCache[sessionId] !== 'error') return
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
      resetTypewriterStream()
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
      resetTypewriterStream()
      streamingToolStepsRef.current = []
      setStreamingToolSteps([])
      setChatTyping(false)
    }
  }, [aiRunning, id, resetTypewriterStream])

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
    resetTypewriterStream()
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
    resetTypewriterStream()
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
      <div className="hb-page hb-workflow-page hb-eval-page">
        <div className="hb-card hb-detail-state">
          <Loader2 size={16} className="animate-spin" />
          正在加载 AI 评估...
        </div>
      </div>
    )
  }

  if (!employee || !evaluation) {
    return (
      <div className="hb-page hb-workflow-page hb-eval-page">
        <div className="hb-card p-8 text-sm text-[var(--hb-soft)]">评估数据不存在</div>
      </div>
    )
  }

  return (
    <div className="hb-page hb-workflow-page hb-eval-page">
      <Breadcrumb items={[{ label: '员工详情', to: id ? instanceBasePath(location.pathname, id) : '/department-employees' }, { label: 'AI 评估' }]} />

      {/* 自动初始化过渡屏 */}
      {autoInitVisible && (
        <EvalAutoInitScreen
          countdown={autoInitCountdown}
          employeeName={employee.nickname}
          roleName={employee.roleName}
          onNow={handleAutoInitNow}
          onCancel={handleAutoInitCancel}
        />
      )}

      {/* 沙箱初始化遮罩：轮询或重置过程中覆盖主内容 */}
      {!autoInitVisible && showSandboxInitOverlay && (
        <EvalSandboxInitOverlay
          resetting={resetting}
          employeeName={employee.nickname}
          progressSummary={workspaceProgressSummary}
        />
      )}

      {!autoInitVisible && !showSandboxInitOverlay && (
      <div className="flex h-[calc(100vh-116px)] min-h-[680px] flex-col gap-3">
        <div className="flex flex-col gap-3">
          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-3 xl:flex-row xl:items-end xl:justify-between">
              <div className="min-w-0">
                <h1 className="text-[20px] font-semibold eval-text-strong">AI 评估对话</h1>
                <p className="mt-1 text-[12px] leading-5 eval-text-secondary">
                  通过双沙箱实时对话推进评估，流程状态、会话和报告会持续同步到当前页面。
                </p>
              </div>
              <div className="rounded-full border eval-flow-target shrink-0 px-3 py-1.5 text-[11px]">
                <span className="eval-text-caption">评估对象</span>
                <span className="ml-2 font-medium eval-text-title">{employee.nickname} · {employee.roleName}</span>
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

            <EvalWorkflowPanel
              stages={workflowStages}
              currentStageIndex={currentWorkflowStageIndex}
              resetConfirm={resetConfirm}
              resetting={resetting}
              submitting={submitting}
              wsEvaluating={wsEvaluating}
              aiRunning={aiRunning}
              primaryActionLabel={primaryActionLabel}
              onSetResetConfirm={setResetConfirm}
              onReset={() => void handleResetEvaluationData()}
              onSubmitRun={() => void submitAiDecision('RUN')}
            />
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

          <EvalChatPanel
            aiRunning={aiRunning}
            chatLoading={chatLoading}
            chatSending={chatSending}
            chatMessages={chatMessages}
            streamingContent={streamingContent}
            streamingToolSteps={streamingToolSteps}
            chatTyping={chatTyping}
            chatInput={chatInput}
            chatError={chatError}
            sessionSwitching={sessionSwitching}
            sandboxConnected={sandboxConnected}
            environmentStatus={environmentStatus}
            workspaceStatus={workspaceStatus}
            sessionCopied={sessionCopied}
            errorMessage={workspaceProgressSummary?.errorMessage ?? ''}
            onCopySessionId={handleCopySessionId}
            testcaseItems={testcaseItems}
            canNavigateToHumanEval={canNavigateToHumanEval}
            humanEvalPath={humanEvalPath}
            humanEvalBannerTone={humanEvalBannerTone}
            humanEvalBannerTextTone={humanEvalBannerTextTone}
            humanEvalBannerTitle={humanEvalBannerTitle}
            humanEvalBannerDescription={humanEvalBannerDescription}
            enteringHumanEval={enteringHumanEval}
            onSendMessage={(content) => void sendEvaluatorMessage(content)}
            onEnterHumanEval={handleEnterHumanEval}
            onSetChatInput={setChatInput}
            onSetArtifactTab={setArtifactTab}
          />
          <EvalArtifactsPanel
            artifactTab={artifactTab}
            rightCollapsed={rightCollapsed}
            reportSummary={reportSummary}
            reportMetrics={reportMetrics}
            evaluation={evaluation}
            workspaceStatus={workspaceStatus}
            testcaseItems={testcaseItems}
            questionCardMap={questionCardMap}
            traceAssets={traceAssets}
            traceDataCache={traceDataCache}
            expandedTraceUrls={expandedTraceUrls}
            expandedQuestionCardIds={expandedQuestionCardIds}
            workspaceReady={workspaceReady}
            materialsReady={materialsReady}
            aiRunning={aiRunning}
            chatSending={chatSending}
            humanEvalPath={humanEvalPath}
            onSetArtifactTab={setArtifactTab}
            onSetRightCollapsed={setRightCollapsed}
            onToggleTraceExpand={toggleTraceExpand}
            onToggleQuestionCardDetails={toggleQuestionCardDetails}
            onRunSingleScenario={handleRunSingleScenario}
            onEnterHumanEval={handleEnterHumanEval}
          />
        </section>
      </div>
      )}

      {showHumanEvalConfirm && (
        <div className="hb-modal-mask" onClick={() => setShowHumanEvalConfirm(false)}>
          <div className="hb-modal hb-delete-confirm-modal eval-human-confirm-modal" onClick={(e) => e.stopPropagation()}>
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

