import { useEffect, useMemo, useRef, useState } from 'react'
import {
  AlertCircle,
  BarChart2,
  CheckCircle2,
  ChevronDown,
  ExternalLink,
  FileText,
  Loader2,
  PlayCircle,
  SendHorizontal,
  Zap,
} from 'lucide-react'
import { useLocation, useParams } from 'react-router-dom'
import { GatewayWs, type GatewayMessage } from '@/infra/sandbox/gateway-ws'
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
import { instanceBasePath } from '@/shared/utils/instancePath'

type ArtifactTab = 'testcase' | 'trace' | 'report'

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

function progressStepByState(params: {
  canPrepare: boolean
  aiRunning: boolean
}) {
  if (params.canPrepare) return 0
  if (params.aiRunning) return 1
  return 2
}

export default function EvaluationPage() {
  const { id } = useParams<{ id: string }>()
  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [evaluation, setEvaluation] = useState<EvaluationState | null>(null)
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  const [rightCollapsed, setRightCollapsed] = useState(false)
  const [artifactTab, setArtifactTab] = useState<ArtifactTab>('testcase')
  const [workspaceStatus, setWorkspaceStatus] = useState<EvaluationWorkspaceStatus | null>(null)
  const [workspacePolling, setWorkspacePolling] = useState(false)

  const location = useLocation();

  const [chatMessages, setChatMessages] = useState<HiringConversationMessage[]>([])
  const [chatInput, setChatInput] = useState('')
  const [chatLoading, setChatLoading] = useState(false)
  const [chatSending, setChatSending] = useState(false)
  const [chatError, setChatError] = useState('')
  const [, setSandboxConversation] = useState<EvaluationSandboxConversationState | null>(null)
  const [wsEvaluating, setWsEvaluating] = useState(false)
  const [wsProgress, setWsProgress] = useState('')
  const chatEndRef = useRef<HTMLDivElement | null>(null)
  const pollingCancelledRef = useRef(false)
  const lastMessageCountRef = useRef(0)
  const stablePollCountRef = useRef(0)

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

  const progressStep = progressStepByState({ canPrepare: canPrepare && !aiRunning, aiRunning })

  const workspaceReady = workspaceStatus?.overallStatus === 'ready'
  const showWorkspaceProgress =
    workspacePolling || (!!workspaceStatus && workspaceStatus.overallStatus !== 'not_started' && workspaceStatus.overallStatus !== 'ready')

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [chatLoading, chatMessages])

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

  async function submitAiDecision(decision: 'START' | 'RUN') {
    if (!id) return
    setSubmitting(true)
    setError('')

    try {
      // RUN: WebSocket direct evaluation flow
      if (decision === 'RUN') {
        await api.employeeRuntime.submitAiEvaluationDecision(id, { decision })
        const connection = await api.employeeRuntime.getSandboxConnection(id)
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
      setEmployee((prev) => prev ? { ...prev, status: syncResult.status as EmployeeDetail['status'] } : prev)
      setError('')

      const evaluationState = await api.employeeRuntime.getEvaluationState(id)
      setEvaluation(evaluationState)
    } catch (wsError: unknown) {
      setError(wsError instanceof Error ? wsError.message : 'WebSocket evaluation failed')
    } finally {
      ws.disconnect()
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

    try {
      const conversation = await api.employeeRuntime.sendEvaluationSandboxMessage(id, { content })
      setSandboxConversation(conversation)
      setChatMessages(conversation.messages ?? [])
    } catch (sendError: unknown) {
      setChatError(sendError instanceof Error ? sendError.message : '发送消息到评估沙箱失败')
      setChatMessages((prev) => prev.filter((item) => item.messageId !== optimistic.messageId))
    } finally {
      setChatSending(false)
    }
  }

  useEffect(() => {
    if (!aiRunning || !id) {
      pollingCancelledRef.current = true
      setChatMessages([])
      setChatError('')
      setSandboxConversation(null)
      return
    }

    pollingCancelledRef.current = false
    lastMessageCountRef.current = 0
    stablePollCountRef.current = 0

    let timer: number
    let currentInterval = 6000
    let lastMessageId: string | null = null
    let noChangeCount = 0
    const maxNoChangeBeforeStop = 3  // ~30s total after slowdown kicks in

    function isConversationDone(messages: HiringConversationMessage[]): boolean {
      if (messages.length === 0) return false
      const last = messages[messages.length - 1]
      // AI is done when last message is assistant, non-empty, and not a tool_use
      return last.role === 'assistant'
        && last.content.length > 0
        && !last.content.includes('[tool_use]')
    }

    function scheduleNext() {
      timer = window.setTimeout(() => {
        void pollConversation()
      }, currentInterval)
    }

    let shouldStop = false

    async function pollConversation() {
      if (pollingCancelledRef.current) return
      setChatLoading(true)
      setChatError('')
      try {
        const conversation = await api.employeeRuntime.getEvaluationSandboxConversation(id!, lastMessageId)
        if (pollingCancelledRef.current) return
        // null means 304 Not Modified - no new messages, keep current state
        if (conversation === null) {
          noChangeCount++
          if (noChangeCount >= maxNoChangeBeforeStop) {
            shouldStop = true
            return
          }
          return
        }
        noChangeCount = 0
        setSandboxConversation(conversation)
        const newMessages = conversation.messages ?? []
        setChatMessages(newMessages)
        const newCount = newMessages.length
        if (newCount > 0) {
          lastMessageId = newMessages[newCount - 1].messageId
        }
        if (newCount === lastMessageCountRef.current && newCount > 0) {
          stablePollCountRef.current++
        } else {
          stablePollCountRef.current = 0
        }
        lastMessageCountRef.current = newCount
        // Slow down: 6s -> 12s -> 20s
        if (stablePollCountRef.current >= 10) {
          currentInterval = 20000
        } else if (stablePollCountRef.current >= 4) {
          currentInterval = 12000
        }
        if (isConversationDone(newMessages)) {
          shouldStop = true
          return
        }
      } catch (conversationError: unknown) {
        if (!pollingCancelledRef.current) {
          setChatError(conversationError instanceof Error ? conversationError.message : '读取评估沙箱对话失败')
        }
      } finally {
        if (!pollingCancelledRef.current) {
          setChatLoading(false)
          if (!shouldStop) {
            scheduleNext()
          }
        }
      }
    }

    void pollConversation()
    return () => {
      pollingCancelledRef.current = true
      window.clearTimeout(timer)
    }
  }, [aiRunning, id])

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

  const traceAssets = (evaluation.assetRefs ?? [])
    .filter((asset) => asset.assetType === 'trace-json')
    .slice(0, 8)
  const questionCards = evaluation.questionCards ?? []
  const materialsReady = evaluation.readiness?.status === 'ready'
  const reportSummary = evaluation.latestReport ?? null
  const reportJsonUrl = toAbsoluteApiUrl(reportSummary?.reportJsonUrl ?? null)
  const reportHtmlUrl = toAbsoluteApiUrl(reportSummary?.reportHtmlUrl ?? null)

  return (
    <div className="hb-page">
      <Breadcrumb items={[{ label: '员工详情', to: id ? instanceBasePath(location.pathname, id) : '/department-employees' }, { label: 'AI 评估' }]} />
      <div className="flex h-[calc(100vh-132px)] min-h-[680px] flex-col gap-4">
        <section className="hb-card p-4">
          <div className="flex flex-wrap items-start gap-3">
            <div className="h-6 w-px bg-[#ececec]" />
            <div className="min-w-0 flex-1">
              <h1 className="text-[18px] font-semibold text-[#0a0a0a]">AI 评估训练</h1>
              <p className="mt-0.5 truncate text-xs text-[#737373]">
                {employee.nickname} 路 {employee.roleName}
              </p>
              <p className="mt-1 text-xs text-[#737373]">当前阶段：{employee.stageSummary || '待发起 AI 评估'}</p>
            </div>
            <div className="ml-auto flex flex-wrap items-center gap-2">
              <button
                type="button"
                disabled={submitting || !canPrepare || aiRunning}
                className="hb-btn-primary !px-3 !py-1.5 !text-xs"
                onClick={() => void submitAiDecision('START')}
              >
                <PlayCircle size={12} />
                准备评估环境
              </button>
              <button
                type="button"
                disabled={submitting || wsEvaluating || !aiRunning}
                className="hb-btn-ghost !px-3 !py-1.5 !text-xs"
                onClick={() => void submitAiDecision('RUN')}
              >
                {wsEvaluating ? <Loader2 size={12} className="animate-spin" /> : <CheckCircle2 size={12} />}
                {wsEvaluating ? (wsProgress || 'WS 评估中...') : '执行评估'}
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
            <div className="mt-3">
              <EvaluationWorkspaceProgress status={workspaceStatus} polling={workspacePolling} />
            </div>
          )}

          {workspaceReady && (
            <div className="mt-3">
              <span className="inline-flex items-center gap-1.5 rounded-full border border-[#bfdbfe] bg-[#eff6ff] px-3 py-1 text-xs font-medium text-[#1d4ed8]">
                <CheckCircle2 size={12} />
                目标沙箱（{shortSandboxId(workspaceStatus?.targetSandboxId)}）与评估沙箱（{shortSandboxId(workspaceStatus?.evaluatorSandboxId)}）已连接
              </span>
            </div>
          )}


          <div className="mt-3 rounded-xl border border-[#ececec] bg-[#fafafa] p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="text-xs font-medium text-[#404040]">
                评估状态
              </div>
              <div className="text-xs text-[#737373]">evalPhase: {employee.evalPhase || '未设置'}</div>
            </div>
            <div className="mt-2 h-1.5 w-full rounded-full bg-[#efefef]">
              <div className="h-1.5 rounded-full bg-[#4a6cf7]" style={{ width: `${((progressStep + 1) / 3) * 100}%` }} />
            </div>
            <div className="mt-2 flex flex-wrap gap-2 text-[11px]">
              {['环境准备', '执行评估', '评估判分'].map((step, index) => (
                <span
                  key={step}
                  className={`rounded-full px-2 py-1 font-medium ${
                    index < progressStep
                      ? 'bg-[#e6f5ec] text-[#15803d]'
                      : index === progressStep
                      ? 'bg-[#e8edff] text-[#4a6cf7]'
                      : 'bg-[#efefef] text-[#737373]'
                  }`}
                >
                  {step}
                </span>
              ))}
            </div>
            <div className="mt-2 text-[11px] text-[#737373]">
              {isPrivateBranchEvaluation ? '私有分支评估流程：复用当前分身 runtime 沙箱作为 target，仅准备 evaluator 沙箱与评估素材，然后执行 WS 评估并判定结果。' : '评估流程：准备环境（创建沙箱 + 上传 Skill + 加载考题）→ 执行评估（WS 直连评估沙箱）→ 判定结果'}
            </div>
          </div>

          {workspaceReady && (
            <div className="mt-3 rounded-xl border border-[#ececec] bg-[#fafafa] p-3">
              <div className="mb-2 flex items-center gap-1.5 text-xs font-semibold text-[#404040]">
                <BarChart2 size={12} />
                评估概览
              </div>
              <div className="grid gap-2 text-xs sm:grid-cols-5">
                <div className="rounded-xl border border-[#ececec] bg-white p-2">
                  <div className="text-[#737373]">场景总数</div>
                  <div className="mt-1 text-sm font-semibold text-[#0a0a0a] tabular-nums">{overview.total}</div>
                </div>
                <div className="rounded-xl border border-[#ececec] bg-white p-2">
                  <div className="text-[#737373]">通过</div>
                  <div className="mt-1 text-sm font-semibold text-[#15803d] tabular-nums">{overview.passed}</div>
                </div>
                <div className="rounded-xl border border-[#ececec] bg-white p-2">
                  <div className="text-[#737373]">未通过</div>
                  <div className="mt-1 text-sm font-semibold text-[#be185d] tabular-nums">{overview.failed}</div>
                </div>
                <div className="rounded-xl border border-[#ececec] bg-white p-2">
                  <div className="text-[#737373]">待判定</div>
                  <div className="mt-1 text-sm font-semibold text-[#c47a26] tabular-nums">{overview.pending}</div>
                </div>
                <div className="rounded-xl border border-[#ececec] bg-white p-2">
                  <div className="text-[#737373]">综合评分</div>
                  <div className="mt-1 text-sm font-semibold text-[#0a0a0a] tabular-nums">{overview.score}</div>
                </div>
              </div>
            </div>
          )}
        </section>

        <section className="flex min-h-0 flex-1 gap-4">
          <div className="hb-card flex min-w-0 flex-1 flex-col overflow-hidden">
            <div className="border-b border-[#ececec] px-4 py-3">
              <div className="flex flex-wrap gap-2 text-[11px]">
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

            <div className="flex-1 overflow-hidden bg-[#fafafa] px-4 py-3">
              <div className="flex h-full flex-col overflow-hidden rounded-xl border border-[#ececec] bg-white">
                {!aiRunning ? (
                  <div className="m-3 rounded-xl border border-[#ececec] bg-[#fafafa] px-3 py-2 text-xs text-[#737373]">
                    请先点击“准备评估环境”，进入 ai_running 后可连接评估沙箱。
                  </div>
                ) : (
                  <>
                    <div className="flex-1 space-y-2 overflow-y-auto px-3 py-2">
                      {chatLoading ? (
                        <div className="flex items-center gap-1.5 text-xs text-[#737373]">
                          <Loader2 size={12} className="animate-spin" />
                          正在加载评估沙箱对话...
                        </div>
                      ) : chatMessages.length === 0 ? (
                        <div className="text-xs text-[#737373]">暂无对话，发送消息后将同步评估沙箱回复。</div>
                      ) : (
                        chatMessages.map((message) => {
                          const isUser = message.role.toLowerCase() === 'user'
                          return (
                            <div key={message.messageId} className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
                              <div
                                className={`max-w-[90%] rounded-xl px-2.5 py-2 text-xs leading-relaxed ${
                                  isUser
                                    ? 'bg-[#000000] text-white'
                                    : 'border border-[#ececec] bg-[#fafafa] text-[#404040]'
                                }`}
                              >
                                <div className={`mb-1 text-[10px] ${isUser ? 'text-[#e5e5e5]' : 'text-[#9ca3af]'}`}>
                                  {isUser ? '你' : '评估沙箱'} 路 {formatDateTime(message.createdAt)}
                                </div>
                                <div className="whitespace-pre-wrap break-words">{message.content}</div>
                              </div>
                            </div>
                          )
                        })
                      )}
                      <div ref={chatEndRef} />
                    </div>
                    <div className="border-t border-[#ececec] px-2.5 py-2">
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
                          className="min-h-[44px] flex-1 resize-y rounded-xl border border-[#e5e5e5] bg-[#fafafa] px-3 py-2 text-xs outline-none focus:border-[#4a6cf7] disabled:opacity-60"
                        />
                        <button
                          type="button"
                          disabled={chatSending || !chatInput.trim()}
                          className="hb-btn-primary !px-2.5 !py-2 disabled:!bg-[#d4d4d8]"
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
            </div>
          </div>
          <div
            className={`${
              rightCollapsed ? 'w-10' : 'w-[360px]'
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
                        questionCards.map((card, index) => (
                          <div key={`${card.testcaseId}_${index}`} className="rounded-xl border border-[#ececec] bg-white p-3">
                            <div className="flex items-center gap-1.5 mb-1">
                              <span className="text-[11px] text-[#9ca3af] font-mono">#{index + 1}</span>
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
                                <span className="text-[#9ca3af] truncate max-w-[140px]">{card.sourceFile}</span>
                              )}
                            </div>
                          </div>
                        ))
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
                        traceAssets.map((asset, index) => {
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
                        })
                      )}
                    </div>
                  )}

                  {artifactTab === 'report' && (
                    <div className="space-y-3">
                      {!reportSummary ? (
                        <div className="rounded-xl border border-[#ececec] bg-white p-3 text-[11px] text-[#737373]">
                          暂无评估报告，请先执行评估。
                        </div>
                      ) : (
                        <>
                          <div className="rounded-xl border border-[#ececec] bg-[#fafafa] p-3 text-center">
                            <div className="text-4xl font-bold text-[#0a0a0a] tabular-nums">{reportSummary.overallScore}</div>
                            <div className="mt-1 text-xs text-[#737373]">综合评分</div>
                            <div className="mt-2 inline-flex">
                              <span className={reportSummary.passed ? 'hb-pill green' : 'hb-pill pink'}>
                                {reportSummary.passed ? 'AI 判定：通过' : 'AI 判定：未通过'}
                              </span>
                            </div>
                          </div>

                          <div className="rounded-xl border border-[#ececec] bg-white p-2.5 text-[11px] text-[#737373]">
                            <div>生成时间：{formatDateTime(reportSummary.createdAtUtc)}</div>
                            {reportJsonUrl && (
                              <a
                                href={reportJsonUrl}
                                target="_blank"
                                rel="noreferrer"
                                className="mt-1 inline-flex items-center gap-1 text-[#2563eb]"
                              >
                                <ExternalLink size={10} />
                                查看报告 JSON
                              </a>
                            )}
                            {reportHtmlUrl && (
                              <a
                                href={reportHtmlUrl}
                                target="_blank"
                                rel="noreferrer"
                                className="ml-3 mt-1 inline-flex items-center gap-1 text-[#2563eb]"
                              >
                                <ExternalLink size={10} />
                                查看报告 HTML
                              </a>
                            )}
                          </div>
                        </>
                      )}

                      <div className="rounded-xl border border-[#ececec] bg-white p-2.5">
                        <div className="mb-1 flex items-center gap-1.5 font-semibold text-[#404040]">
                          <FileText size={11} />
                          评估建议
                        </div>
                        <p className="text-[11px] leading-relaxed text-[#737373]">{evaluation.recommendation}</p>
                      </div>
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


